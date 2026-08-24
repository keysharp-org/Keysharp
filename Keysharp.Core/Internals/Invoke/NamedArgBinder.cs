namespace Keysharp.Internals.Invoke
{
	using Keysharp.Builtins;
	using NamedArgs = Keysharp.Builtins.Ks.NamedArgs;

	/// <summary>
	/// Folds a call's <see cref="NamedArgs"/> into positional argument slots.
	/// <para>
	/// Named arguments ride in the ordinary <c>object[]</c> argument array as a single trailing value of a
	/// dedicated type (see <see cref="NamedArgs"/>). Every forwarding hop -- a spread, a variadic collection, a
	/// relay like <c>Func.Bind</c> or <c>Class.Call</c> -- passes it along as data without knowing it exists; it is
	/// bound wherever a parameter list is finally known: the compiled-invoke wrapper for a normal call, and the COM
	/// and Clr paths for the targets that resolve their own. A name the callee declares fills that parameter's
	/// slot; a name it does not declare spills into its variadic tail as data (which is what makes
	/// <c>w(args*) =&gt; inner(args*)</c> relay them), or is an error when there is no tail to spill into.
	/// </para>
	/// <para>
	/// Binding failures throw directly rather than going through <c>Errors.*Occurred</c>, matching
	/// <c>DelegateFactory.ThrowMissingArgument</c> and the argument-count checks it sits beside: these are
	/// argument-binding faults raised from inside the invoke wrapper, not operations a script can be asked to
	/// continue past.
	/// </para>
	/// </summary>
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class NamedArgBinder
	{
		/// <summary>
		/// True when <paramref name="args"/> carries named arguments. One bounds test and one type test, which is
		/// what every invocation pays whether or not it names anything, so it must stay this cheap. It is
		/// correct by DEFINITION rather than by any guarded invariant: whether a container is a named argument is
		/// decided purely by position -- the last one binds by name, and one anywhere else (a spread can deposit
		/// one before later positional arguments) is an ordinary positional value.
		/// </summary>
		internal static bool Has(object[] args) => args != null && args.Length != 0 && args[^1] is NamedArgs;

		/// <summary>
		/// Where the positional arguments end: the index of the trailing <see cref="NamedArgs"/>, or
		/// <c>args.Length</c> when there is none (<paramref name="named"/> null). The single definition of that
		/// boundary -- everything below works from it.
		/// <para>
		/// More than one container in a row is possible only by spreading a source that carries names
		/// (<c>w(args*) =&gt; inner(args*, extra: 1)</c>, or two such spreads), and they are unioned into one. The
		/// ordinary call has exactly one, so the loop does not run.
		/// </para>
		/// </summary>
		internal static int SplitAt(object[] args, out NamedArgs named)
		{
			if (!Has(args))
			{
				named = null;
				return args?.Length ?? 0;
			}

			var first = args.Length - 1;
			named = (NamedArgs)args[first];

			while (first > 0 && args[first - 1] is NamedArgs earlier)
			{
				named = Union(earlier, named);
				first--;
			}

			return first;
		}

		/// <summary>
		/// The union of two containers, for the one shape that produces more than one: a call that spreads a source
		/// carrying names and supplies its own (<c>w(args*) =&gt; inner(args*, extra: 1)</c>), or two such spreads. A
		/// name in both is the ordinary supplied-twice error, raised here because this is where the second one is
		/// seen and no callee is known yet.
		/// </summary>
		private static NamedArgs Union(NamedArgs a, NamedArgs b)
		{
			if (a.Count == 0)
				return b;

			if (b.Count == 0)
				return a;

			var merged = new NamedArgs();
			Absorb(a);
			Absorb(b);
			return merged;

			void Absorb(NamedArgs source)
			{
				foreach (var (name, value) in source.Entries())
				{
					if (merged.Store.ContainsKey(name))
						throw new ValueError(SuppliedTwiceMessage(name, null));

					merged[name] = value;
				}
			}
		}

		/// <summary>
		/// <see cref="SplitAt"/> for the callers that need the positional head as an array of its own --
		/// <c>BoundFunc</c>'s slot merging and the Clr overload search, which both keep it. Returns the input
		/// untouched when there are no names, so the ordinary positional path neither copies nor allocates.
		/// </summary>
		internal static object[] Split(object[] args, out NamedArgs named)
		{
			var at = SplitAt(args, out named);
			return named == null ? args ?? System.Array.Empty<object>() : args[..at];
		}

		/// <summary>
		/// Puts a call's names back on the tail of a positional array -- the ones a BoundFunc carries, then the ones
		/// this call supplied. Returns the input untouched when there are none, so the ordinary positional path
		/// neither copies nor allocates.
		/// </summary>
		internal static object[] Append(object[] args, NamedArgs a, NamedArgs b)
		{
			var named = a == null ? b : b == null ? a : Union(a, b);

			if (named == null)
				return args;

			var result = new object[args.Length + 1];
			System.Array.Copy(args, result, args.Length);
			result[^1] = named;
			return result;
		}

		/// <summary>
		/// Lifts the names out of a call's argument array and puts their values where IDispatch wants them, with
		/// <paramref name="names"/> parallel to that run. The two COM paths differ only in WHERE the named values
		/// sit, so they are one shaper: whichever run they occupy, <c>names[i]</c> names the i'th of them.
		/// <para>
		/// Returns the input untouched, and an empty names array, when there are none -- this runs on every COM
		/// invoke.
		/// </para>
		/// </summary>
		/// <param name="namedLead">
		/// True for <c>Type.InvokeMember</c>, whose <c>namedParameters[i]</c> names <c>args[i]</c>, so the named
		/// values must LEAD (verified against a live IDispatch target, Scripting.Dictionary). False for the raw
		/// <c>IDispatch::Invoke</c> path, which wants the names out-of-band in
		/// <c>DISPPARAMS.rgdispidNamedArgs</c> and their values left trailing in <c>rgvarg</c>.
		/// <para>
		/// Either way the CLR or the target marshals the names to DISPIDs via <c>IDispatch::GetIDsOfNames</c>, so a
		/// target that cannot resolve a name (no type information, or simply no such parameter) raises there --
		/// which is the behaviour wanted. Naming a parameter that a positional argument also covers is likewise
		/// rejected by the target, matching Keysharp's own rule that a parameter may not be supplied twice.
		/// </para>
		/// </param>
		private static object[] ComShape(object[] args, bool namedLead, out string[] names)
		{
			var positional = Split(args, out var named);
			var entries = named == null ? System.Array.Empty<(string Name, object Value)>() : named.Entries();

			if (entries.Length == 0)
			{
				names = System.Array.Empty<string>();
				return positional;
			}

			names = new string[entries.Length];
			var values = new object[positional.Length + entries.Length];
			System.Array.Copy(positional, 0, values, namedLead ? entries.Length : 0, positional.Length);
			var at = namedLead ? 0 : positional.Length;

			for (var i = 0; i < entries.Length; i++)
			{
				names[i] = entries[i].Name;
				values[at + i] = entries[i].Value;
			}

			return values;
		}

		/// <summary>The layout <c>Type.InvokeMember</c> expects: named values first. See <see cref="ComShape"/>.</summary>
		internal static object[] ToComLayout(object[] args, out string[] names) => ComShape(args, namedLead: true, out names);

		/// <summary>The layout raw <c>IDispatch::Invoke</c> expects: named values last. See <see cref="ComShape"/>.</summary>
		internal static object[] StripNames(object[] args, out string[] names) => ComShape(args, namedLead: false, out names);

		/// <summary>
		/// Whether the call can go to <paramref name="mph"/> at all -- either every name resolves to a declared
		/// parameter, or the callee has a variadic tail, which absorbs whatever it does not declare.
		/// </summary>
		internal static bool Accepts(MethodPropertyHolder mph, NamedArgs named) => mph.variadicParamIndex >= 0 || Declares(mph, named);

		/// <summary>
		/// Whether every name resolves to a DECLARED parameter. A variadic tail absorbing one does not count, which
		/// is what lets overload selection prefer the sibling that actually has the parameter.
		/// </summary>
		internal static bool Declares(MethodPropertyHolder mph, NamedArgs named)
		{
			if (named == null)
				return true;

			var map = mph.ParamIndexByName;

			foreach (var name in named.Store.Keys)
				if (name is not string spelling || !map.ContainsKey(spelling))
					return false;

			return true;
		}

		/// <summary>
		/// How many argument slots precede the method's first declared parameter, given how the receiver is being
		/// passed. Shared by the compiled-invoke wrapper and <c>BoundFunc</c> so the two cannot drift apart:
		/// <list type="bullet">
		/// <item><c>1</c> -- real instance method whose receiver was passed as <c>args[0]</c>.</item>
		/// <item><c>0</c> -- receiver supplied out-of-band, or a plain static.</item>
		/// <item><c>-1</c> -- a static using the explicit <c>object @this</c> convention with the receiver supplied
		/// out-of-band (the caller prepends it). Safe despite being negative because it is gated on
		/// <c>receiverInCounts</c>, which tests for that <c>@this</c> parameter with the SAME predicate
		/// <c>BuildParamIndexMap</c> uses to exclude it from the name map: where this returns -1,
		/// <c>parameters[0]</c> is provably the receiver and no name can resolve to slot -1. A static without one,
		/// invoked with an instance from elsewhere (the Clr overload swap in <c>Reflections</c>), gets 0.</item>
		/// </list>
		/// </summary>
		internal static int ArgBase(MethodPropertyHolder mph, object instance) =>
			!mph.IsStatic ? (instance == null ? 1 : 0) : (instance != null && mph.receiverInCounts ? -1 : 0);

		/// <summary>
		/// Resolves each name to its argument slot and merges the values into a positional array, or returns null
		/// with <paramref name="failure"/> describing the first name that could not be placed.
		/// <para>
		/// A null slot is FREE: whether it is a hole <c>Bind</c> left for arguments still to come or an omitted
		/// argument (<c>f(, x: 1)</c>), nothing was supplied there, so a name is entitled to fill it. Only a slot
		/// already holding a real value collides -- whether a positional argument or an earlier name put it there,
		/// which is why one test covers both.
		/// </para>
		/// <para>
		/// Shared by every caller that has to do this -- the invoke wrapper, <c>BoundFunc</c>'s bind-time placement,
		/// and the Clr overload search -- because they differ only in what they do about a failure: the first two
		/// raise, the third moves on to the next candidate overload.
		/// </para>
		/// </summary>
		/// <param name="count">How many leading elements of <paramref name="args"/> are positional (from <see cref="SplitAt"/>).</param>
		/// <param name="allowSpill">
		/// Whether a name the callee does not declare may be handed back in <paramref name="spilled"/> instead of
		/// failing. Set for a VARIADIC callee, which has somewhere to put it. See <see cref="Bind"/>.
		/// </param>
		/// <param name="spilled">The undeclared names, or null when there were none.</param>
		internal static object[] TryPlace(Dictionary<string, int> map, int argBase, object[] args, int count, NamedArgs named,
										  out string failure, out NamedArgs spilled, bool allowSpill = false)
		{
			failure = default;
			spilled = null;
			// A snapshot, because a subclass may override __Enum (see NamedArgs.Entries) and reading it then runs
			// script -- which must not be able to add a bindable name after the array below has been sized.
			var entries = named.Entries();
			var slots = new int[entries.Length];
			var size = count;
			var placed = 0;

			// Resolve every name first, so the array is sized once and a bad name fails before anything is copied.
			for (var i = 0; i < entries.Length; i++)
			{
				if (!map.TryGetValue(entries[i].Name, out var paramIndex))
				{
					if (!allowSpill)
					{
						failure = entries[i].Name;
						return null;
					}

					slots[i] = -1;
					continue;
				}

				placed++;
				slots[i] = paramIndex + argBase;

				if (slots[i] >= size)
					size = slots[i] + 1;
			}

			var merged = new object[size];
			System.Array.Copy(args, merged, count);

			for (var i = 0; i < entries.Length; i++)
			{
				if (slots[i] < 0)
					continue;   // spilled; collected below

				// A spread's values are flattened into the positional array before the call, so an overlap with one
				// of them surfaces here too.
				if (merged[slots[i]] != null)
				{
					failure = entries[i].Name;
					return null;
				}

				merged[slots[i]] = entries[i].Value;
			}

			if (placed != entries.Length)
			{
				// Nothing placed means the whole container moves on as it stands -- the forwarding case, which must
				// not copy. A PARTIAL spill is the only shape that allocates.
				if (placed == 0)
					spilled = named;
				else
				{
					spilled = new NamedArgs();

					for (var i = 0; i < entries.Length; i++)
						if (slots[i] < 0)
							spilled[entries[i].Name] = entries[i].Value;
				}
			}

			return merged;
		}

		/// <summary>Raises for the name <see cref="TryPlace"/> could not place, naming the member and what it accepts.</summary>
		internal static void ThrowPlaceFailure(MethodPropertyHolder mph, string name)
		{
			var map = mph.ParamIndexByName;

			throw new ValueError(
				map.ContainsKey(name)
				? SuppliedTwiceMessage(name, Describe(mph))
				: UnknownNameMessage(name, Describe(mph), map.OrderBy(kv => kv.Value).Select(kv => kv.Key)));
		}

		// The two diagnostics a named argument can produce, in one place: `#Warn NamedArg` reports the same two at
		// compile time (Lowerer.CheckNamedArgs) from a signature it read rather than from an MPH, and two hand-copied
		// wordings for one condition is how they drift apart.
		internal static string UnknownNameMessage(string name, string callee, IEnumerable<string> bindable)
		{
			var names = string.Join(", ", bindable);
			return $"'{name}' is not a parameter of {callee}."
				   + (names.Length == 0 ? " It has no parameters that can be named."
										: $" Its named parameters are: {names}.");
		}

		// "more than once" rather than "positionally and by name": by the time the collision is detected, a
		// Bind-time placement has already turned a named supply into an occupied slot, so the two cases are
		// indistinguishable here -- and `f.Bind(head: 1)(head: 2)` supplied both BY NAME. `callee` is null where the
		// collision is seen before any callee is known (two spreads each carrying the same name).
		internal static string SuppliedTwiceMessage(string name, string callee) =>
			callee == null ? $"Parameter '{name}' was supplied more than once."
						   : $"Parameter '{name}' of {callee} was supplied more than once.";

		// Whether the argument array is ALREADY in the shape Bind's fast path would hand on: exactly one container,
		// carrying at least one name. An empty one is not an argument at all and must be dropped, and a run of them
		// has to be unioned first -- both are cheap, and letting either through would make what a variadic callee
		// collects depend on whether that callee happened to declare a bindable parameter.
		private static bool PassesThrough(object[] args) =>
			((NamedArgs)args[^1]).Count != 0 && (args.Length < 2 || args[^2] is not NamedArgs);

		/// <summary>
		/// Rewrites <paramref name="args"/> into a purely positional array, leaving any name the callee does not
		/// declare in its variadic tail as an ordinary element. Call only when <see cref="Has"/> holds: the fast
		/// path below reads the trailing container without re-testing for one.
		/// <para>
		/// A variadic parameter collects named arguments the same way it collects everything else -- that is what
		/// lets a wrapper relay them (<c>w(args*) =&gt; inner(args*)</c>) or examine them, and it is why no callee
		/// needs marking of any kind: being variadic IS having somewhere to put them. A non-variadic callee has
		/// nowhere, so there an undeclared name stays an error.
		/// </para>
		/// </summary>
		/// <param name="argBase">From <see cref="ArgBase"/>, for the receiver convention this call is using.</param>
		internal static object[] Bind(MethodPropertyHolder mph, object[] args, int argBase)
		{
			var absorb = mph.variadicParamIndex >= 0;

			// A pure wrapper (`w(args*)`) or relay declares nothing bindable, so the container would spill straight
			// back to the tail it already occupies -- the array is correct as it stands. This keeps the forwarding
			// hot path allocation-free. Valid only when the variadic tail begins at args[0]: an UNBOUND method's
			// receiver slot precedes it (variadicParamIndex + argBase != 0), and there the slow path correctly
			// leaves that slot empty and fails on the missing receiver, where returning the array unchanged would
			// let the container be consumed as the receiver.
			if (absorb && mph.ParamIndexByName.Count == 0 && mph.variadicParamIndex + argBase == 0 && PassesThrough(args))
				return args;

			var count = SplitAt(args, out var named);
			var merged = TryPlace(mph.ParamIndexByName, argBase, args, count, named, out var failure, out var spilled,
								  allowSpill: absorb);

			if (merged == null)
				ThrowPlaceFailure(mph, failure);

			if (spilled == null)
				return merged;

			// The spilled names must land AT OR AFTER the variadic parameter's slot, never in a declared slot before
			// it: `f(a, rest*)` called as `f(Title: 1)` supplies nothing positionally, so index 0 is `a`, not the tail.
			var at = Math.Max(merged.Length, mph.variadicParamIndex + argBase);
			var result = new object[at + 1];
			System.Array.Copy(merged, result, merged.Length);
			result[at] = spilled;
			return result;
		}

		// MethodPropertyHolder owns the script-visible name, including class/prototype/accessor qualification.
		private static string Describe(MethodPropertyHolder mph)
		{
			var name = mph.QualifiedName;
			// An anonymous lambda has no name to print; naming it "" would read as `is not a parameter of .`
			return name.Length != 0 ? name : "this function";
		}
	}
}
