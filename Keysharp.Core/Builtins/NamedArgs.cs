using Keysharp.Runtime;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The named arguments of one call, as a case-insensitive <see cref="Map"/> whose keys ARE the names. A Map
		/// rather than an Object because these are keys the SCRIPT chooses, not members named in source: an
		/// Object's own properties share a namespace with its own members, so a parameter called <c>Base</c> or
		/// <c>Clone</c> could not be supplied at all, and a key backed by a property descriptor could supply
		/// something other than a stored value.
		/// <para>
		/// The lowerer emits one of these per call that names anything, as the LAST argument, and from there it
		/// travels like any other value: a variadic parameter collects it as an ordinary element, a spread re-emits
		/// it, a relay such as <c>Func.Bind</c> passes it along -- none of them needing to know what it is. It is
		/// folded into positional slots wherever a callee's parameter list finally becomes known (see
		/// <c>NamedArgBinder</c>).
		/// </para>
		/// <para>
		/// Being the last argument is what makes it a named argument; one that a spread deposits anywhere else is
		/// ordinary positional data. That single rule is also why a call needs only one test on the last element to
		/// know whether it has names at all.
		/// </para>
		/// <para>
		/// Reachable from a script as <c>#import "Ks" { NamedArgs }</c>, for the two things that need the type
		/// itself: building a call whose names are decided at run time (<c>na := NamedArgs("timeout", 5)</c>, or
		/// <c>na[Name] := Value</c> for a name that is itself computed) and examining the ones a variadic function
		/// collected (<c>args[-1]["timeout"]</c>). The flip side, as with <see cref="VarRef"/>, is that the type
		/// carries the meaning: one passed as the last argument is always a named argument and never plain data, so
		/// pass it inside an Array or a property when that is what is meant.
		/// </para>
		/// </summary>
		public class NamedArgs : Map
		{
			/// <summary>
			/// Takes the same name/value pairs as <see cref="Map"/>: <c>NamedArgs("timeout", 5, "retries", 2)</c>.
			/// <para>
			/// Case sensitivity is pinned off, because parameter names are matched case-insensitively and a
			/// container that matched them any other way would disagree with the binder about which names it even
			/// carries. Pinned on BOTH construction paths, since a script builds one through <c>__New</c> and the
			/// lowerer through the constructor.
			/// </para>
			/// </summary>
			public NamedArgs(params object[] nameValuePairs) : base(null)
			{
				CaseSense = "Off";
				Fill(nameValuePairs);
			}

			public override object __New(params object[] nameValuePairs)
			{
				CaseSense = "Off";
				Fill(nameValuePairs);
				return DefaultObject;
			}

			/// <summary>
			/// Stores the pairs. A lone <see cref="Map"/> argument is COPIED rather than handed to
			/// <c>Map.Set</c>, which would adopt its dictionary by reference and overwrite the case mode pinned
			/// above with the source's -- leaving a container that shares another object's names and matches them
			/// case-sensitively. Everything else is Map's own handling; <c>null</c> reaches it too, so the backing
			/// store is allocated with the right comparer and is never left null.
			/// </summary>
			private void Fill(object[] nameValuePairs)
			{
				if (nameValuePairs is [Map source])
				{
					_ = Set(null);

					foreach (var (name, value) in source)
						this[name] = value;

					return;
				}

				_ = Set(nameValuePairs);
			}

			/// <summary>
			/// The backing store, for the structural questions -- does this call name anything, does a candidate
			/// overload declare all of them. Only the keys are read, so nothing here can run script, unlike
			/// <see cref="Entries"/>. Never null, unlike <c>map</c> itself, which stays unallocated until something
			/// is stored.
			/// </summary>
			internal Dictionary<object, object> Store => map ?? noStore;

			private static readonly Dictionary<object, object> noStore = new();

			/// <summary>
			/// The names and the values they supply. Ordinarily walked straight off <see cref="Map.EnumerableMap"/>,
			/// which is already a snapshot -- replaced rather than mutated whenever the container is written to --
			/// so a name added or removed part-way through binding cannot corrupt the walk, at one allocation.
			/// <para>
			/// A subclass that overrides <c>__Enum</c> is honoured instead, through the full enumeration protocol,
			/// so what binds is what a <c>for Name, Value in na</c> loop over the same container yields. That path
			/// costs an enumerator and its refs, which is why it is taken only when there is genuinely an override
			/// to honour -- every container the lowerer builds takes the direct walk.
			/// </para>
			/// <para>
			/// The order is Map's, which is sorted -- diagnostics and <see cref="ToString"/> read alphabetically,
			/// and nothing else depends on it. An override reached from binding is ordinary script, so one that
			/// passes the container onward as a last argument recurses, exactly as a <c>for</c> loop over it would;
			/// and one written as a PROPERTY rather than a method has its getter run twice, once to identify it and
			/// once to drive it.
			/// </para>
			/// </summary>
			internal (string Name, object Value)[] Entries()
			{
				// An override has to live somewhere on the base chain, so a container based DIRECTLY on the
				// built-in prototype cannot have one -- which is every container the lowerer emits, answering on a
				// reference compare rather than a by-name resolution it is guaranteed to lose. A subclass keeps its
				// own prototype in front, whether declared in source or built at run time, so it still resolves.
				if (!ReferenceEquals(_base, TheScript.Vars.Prototypes[typeof(NamedArgs)]) && Loops.ScriptEnum(this) != null)
					return EnumeratedEntries();

				if (Count == 0)
					return System.Array.Empty<(string, object)>();

				var entries = new (string Name, object Value)[Count];
				var i = 0;

				foreach (var (name, value) in EnumerableMap)
					entries[i++] = (NameOf(name), value);

				return entries;
			}

			/// <summary>Drives the enumeration protocol, for the subclass that overrode it. See <see cref="Entries"/>.</summary>
			private (string Name, object Value)[] EnumeratedEntries()
			{
				var entries = new List<(string, object)>((int)Count);
				var enumerator = Loops.MakeEnumerator(this, 2L);
				var nameRef = new VarRef(null);
				var valueRef = new VarRef(null);
				var args = new object[] { nameRef, valueRef };

				while (enumerator.Call(args).IsCallbackResultNonEmpty())
					entries.Add((NameOf(nameRef.__Value), valueRef.__Value));

				return entries.ToArray();
			}

			// The parameter name a key stands for, for both paths above: a key that is not a string is rendered as
			// one, so it surfaces as an unknown parameter name rather than disappearing.
			private static string NameOf(object key) => key?.ToString() ?? "";

			/// <summary>
			/// A copy holding the same names and values, for an owner that must not share -- see <c>BoundFunc</c>,
			/// which re-emits its bound names on every call and so cannot hold the caller's container. Shallow, like
			/// every other snapshot in the language: an object value is still shared.
			/// </summary>
			internal NamedArgs Copy()
			{
				var copy = new NamedArgs();

				foreach (var (name, value) in Entries())
					copy[name] = value;

				return copy;
			}

			/// <summary>
			/// The names it carries, in the order it enumerates them -- which is Map's, sorted, not the order the
			/// call site wrote them. This is what a variadic built-in prints when it collects one.
			/// </summary>
			[PublicHiddenFromUser]
			public override string ToString()
			{
				var sb = new StringBuilder();

				foreach (var (name, value) in Entries())
					_ = sb.Append(sb.Length != 0 ? ", " : "").Append(name).Append(": ").Append(value);

				return sb.ToString();
			}
		}
	}
}
