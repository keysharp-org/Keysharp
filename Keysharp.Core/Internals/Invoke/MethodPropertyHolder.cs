using Keysharp.Builtins;
using System.Collections.Generic;
using System.Linq.Expressions;
using Label = System.Reflection.Emit.Label;

namespace Keysharp.Internals.Invoke
{
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal class MethodPropertyHolder
	{
		public Func<object, object[], object> _callFunc;
		public Func<object, object[], object> CallFunc
        {
            get
            {
                if (_callFunc != null)
                    return _callFunc;

				var del = DelegateFactory.CreateDelegate(this);

                if (isGuiType)
                {
                    _callFunc = (inst, args) =>
                    {
                        var ctrl = (inst ?? args[0]).GetControl();
                        object ret = null;
                        ctrl.CheckedInvoke(() =>
                        {
                            ret = del(inst, args);
                        }, true);
                        return ret;
                    };
                }
                else
                    _callFunc = del;

                return _callFunc;
			}
        }

		internal MemberInfo memberInfo => ((MemberInfo)mi ?? pi) ?? fi;
		internal readonly MethodInfo mi;
		internal readonly ParameterInfo[] parameters;
		internal readonly PropertyInfo pi;
		internal readonly FieldInfo fi;
		internal readonly Type moduleType;
		internal readonly Semver.SemVersion compatibilityVersion;
		internal readonly Action<object, object> SetProp;
		protected readonly ConcurrentStackArrayPool<object> paramsPool;
		internal readonly bool anyOptional;
		internal readonly bool isGuiType;
		internal readonly bool isSetter;
		internal readonly bool isItemSetter;
		internal readonly int variadicParamIndex = -1;
		internal readonly int[] requiredIdx;
		internal readonly bool receiverInCounts;

		/// <summary>
		/// The adjustment that turns <see cref="MaxParams"/>/<see cref="MinParams"/> into the number of arguments a
		/// CALLER supplies, for the receiver this call will really use: -1 when the counts include a receiver that
		/// is supplied out-of-band (so the caller never passes it), else 0.
		/// </summary>
		internal int ReceiverCorrection(object inst) => receiverInCounts && inst != null ? -1 : 0;

        internal static ConcurrentDictionary<MethodInfo, MethodPropertyHolder> methodCache = new();
		internal static ConcurrentDictionary<PropertyInfo, MethodPropertyHolder> propertyCache = new();
		internal static ConcurrentDictionary<FieldInfo, MethodPropertyHolder> fieldCache = new();

		internal bool IsBind { get; private set; }
		internal bool IsStatic { get; private set; }
		internal bool IsCompilerGenerated { get; private set; }
		internal bool IsStaticFunc { get; private set; }
		internal bool IsStaticProp { get; private set; }
		internal bool IsVariadic => variadicParamIndex != -1;
		internal bool IsExported => memberInfo?.GetCustomAttribute<Export>() != null;
		internal int ParamLength { get; }
		internal int MinParams = 0;
		internal int MaxParams = 0;

        internal const int DotNetMaxParams = 8192; // https://www.tabsoverspaces.com/233892-whats-the-maximum-number-of-arguments-for-method-in-csharp-and-in-net

		/// <summary>
		/// Whether a parameter is the explicit <c>object @this</c> receiver rather than a real argument. ONE
		/// definition, because two consumers have to agree exactly: <c>BuildParamIndexMap</c> excludes it from the
		/// name map, and <c>NamedArgBinder.ArgBase</c> shifts every name's slot down by one when it is present
		/// (via <see cref="receiverInCounts"/>). If those two ever disagreed, a name would resolve to slot -1 and
		/// index outside the argument array.
		/// </summary>
		private static bool IsExplicitThis(ParameterInfo p) =>
			p.Name?.TrimStart('@').Equals("this", StringComparison.OrdinalIgnoreCase) ?? false;

		private const string setterPrefix = "set_";
        private const string classSetterPrefix = Keywords.ClassStaticPrefix + setterPrefix;

		private static readonly string[] namePrefixes = ["static", "get_", "set_"];

		/// <summary>
		/// Recovers the name a script wrote from the emitted C# one. Closures, nested functions and lambdas are
		/// lowered to LOCAL functions, and Roslyn renames those to <c>&lt;Outer&gt;g__Name|n_m</c> -- which is what
		/// <c>Func.Name</c> and every error message naming a function would otherwise print. An anonymous lambda has
		/// no name a script could write, so it reports as empty, matching AutoHotkey.
		/// <para>
		/// The trailing <c>_&lt;n&gt;</c> is the lowerer's uniquing counter (see Lowerer.LowerFatArrow), stripped
		/// with it. A nested function genuinely named <c>foo_2</c> therefore reports as <c>foo</c> -- cosmetic, and
		/// only in a diagnostic.
		/// </para>
		/// </summary>
		private static string Unmangle(string emitted)
		{
			// `<Outer>g__Inner|n_m` -- take what is between "g__" and the '|'.
			if (emitted.Length == 0 || emitted[0] != '<')
				return emitted;

			var open = emitted.IndexOf("g__", StringComparison.Ordinal);
			var bar = emitted.LastIndexOf('|');

			if (open < 0 || bar < open)
				return emitted;

			var inner = emitted.Substring(open + 3, bar - open - 3);

			if (inner.StartsWith(Keywords.TopLevelFunctionPrefix, StringComparison.Ordinal))
				inner = inner.Substring(Keywords.TopLevelFunctionPrefix.Length);

			if (inner.StartsWith(Keywords.AnonymousLambdaPrefix, StringComparison.Ordinal)
					|| inner.StartsWith(Keywords.AnonymousFatArrowLambdaPrefix, StringComparison.Ordinal))
				return "";

			var lastUnderscore = inner.LastIndexOf('_');
			return lastUnderscore > 0 && inner.AsSpan(lastUnderscore + 1).Length != 0
				   && ulong.TryParse(inner.AsSpan(lastUnderscore + 1), out _)
				? inner.Substring(0, lastUnderscore)
				: inner;
		}

		string _name = null;
		internal string Name
		{
			get
			{
				if (_name != null)
					return _name;

				if (mi == null)
				{
					if (pi != null)
						return _name = pi.Name;
					if (fi != null)
						return _name = fi.Name;
					return _name = "";
				}

				var name = GetUserDeclaredName(mi);
				if (name != null)
				{
					return _name = name;
				}

				string funcName = Unmangle(mi.Name);

				foreach (var p in namePrefixes)
				{
					if (funcName.StartsWith(p, StringComparison.Ordinal))
						funcName = funcName.Substring(p.Length);
				}

				if (mi.DeclaringType.Namespace != TheScript.ProgramType.Namespace || mi.DeclaringType.Name == Keywords.MainClassName)
					return _name = funcName;

				var parts = new Stack<string>();
				var script = TheScript;
				for (var cur = mi.DeclaringType; cur != null && cur != script.ProgramType; cur = cur.DeclaringType)
				{
					if (IsModuleContainer(cur, script))
						continue;
					parts.Push(cur.Name);
				}

				if (parts.Count == 0)
					return _name = funcName;

				return _name = $"{string.Join(".", parts)}.{funcName}";
			}
		}

		private Dictionary<string, int> _paramIndexByName;

		/// <summary>One bindable parameter, as reported by <c>Func.Params</c>; signature-level, so cached here.</summary>
		internal sealed record ParamScanEntry(int Index, string Name, bool Optional, bool ByRef, bool Variadic, bool HasDefault, object Default);

		private List<ParamScanEntry> _paramScan;

		/// <summary>
		/// The bindable parameters in declaration order, for <c>Func.Params</c>. Unfiltered: a BoundFunc skips
		/// the entries whose slots its Bind already filled, which is per-instance data and stays out of this cache.
		/// </summary>
		internal List<ParamScanEntry> ParamScan => _paramScan ??= BuildParamScan();

		private List<ParamScanEntry> BuildParamScan()
		{
			var scan = new List<ParamScanEntry>();

			// name -> index, then inverted: the binder's map is the authority on which names bind, and on which
			// parameters are addressable at all (the receiver and the variadic tail are already excluded there).
			var byIndex = new Dictionary<int, string>();

			foreach (var kv in ParamIndexByName)
				if (!byIndex.ContainsKey(kv.Value))
					byIndex[kv.Value] = kv.Key;

			for (var i = 0; i < parameters.Length; i++)
			{
				var p = parameters[i];
				var isVariadic = i == variadicParamIndex;

				if (!isVariadic && !byIndex.ContainsKey(i))
					continue;   // the receiver

				var hasDefault = !isVariadic && p.HasDefaultValue && p.DefaultValue is object dv && dv is not DBNull;
				// A lowered variadic parameter carries the `KS_` signature prefix (see Lowerer.VariadicRawName);
				// scripts know it by the name they wrote.
				var name = isVariadic && p.Name?.StartsWith(Keywords.InternalPrefix, StringComparison.Ordinal) == true
						   ? p.Name.Substring(Keywords.InternalPrefix.Length)
						   : isVariadic ? p.Name : byIndex[i];
				scan.Add(new ParamScanEntry(i, name,
											isVariadic || p.IsOptional,
											p.GetCustomAttribute<Keysharp.Runtime.ByRefAttribute>() != null,
											isVariadic,
											hasDefault,
											hasDefault ? p.DefaultValue : null));
			}

			return scan;
		}

		/// <summary>
		/// The lifecycle method (<c>__New</c>, <c>__Init</c>) a type actually uses: the most-derived declaration,
		/// walking base-ward.
		/// <para>
		/// A plain <c>Type.GetMethod(name, Public | Instance)</c> throws <c>AmbiguousMatchException</c> as soon as
		/// a built-in declares its own real signature -- that declaration is <c>new</c>, not <c>override</c> (see
		/// <c>Buffer.__New</c>), so <c>Any.__New(params object[])</c> is still inherited and BOTH are public
		/// instance methods of that name. Walking <c>DeclaredOnly</c> outward picks the nearest one, which is what
		/// C# <c>new</c>-hiding and script-side overriding both pick.
		/// </para>
		/// </summary>
		internal static MethodInfo FindLifecycleMethod(Type t, string name)
		{
			for (var cur = t; cur != null; cur = cur.BaseType)
				if (cur.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } mi)
					return mi;

			return null;
		}

		/// <summary>The <c>__New</c> a type constructs through. See <see cref="FindLifecycleMethod"/>.</summary>
		internal static MethodInfo FindConstructor(Type t) => FindLifecycleMethod(t, "__New");

		/// <summary>
		/// Parameter name -> index into <see cref="parameters"/>, for binding named arguments (<c>f(Name: value)</c>).
		/// Case-insensitive, matching every other identifier in the language. Built once, lazily: most methods are
		/// never called with named arguments, so the cost stays with the scripts that use them.
		/// <para>
		/// Excluded from the map, and so not bindable by name: the receiver, and the variadic parameter (a
		/// <c>rest*</c> tail is positional by nature).
		/// </para>
		/// </summary>
		internal Dictionary<string, int> ParamIndexByName => _paramIndexByName ??= BuildParamIndexMap();

		private Dictionary<string, int> BuildParamIndexMap()
		{
			var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			if (parameters == null)   // the "fake" MPH used by ObjBindMethod: no signature to bind against
				return map;

			// [UserDeclaredName] carries the name a script writes when it cannot be the C# identifier: a documented built-in
			// spelling, or -- on lowered user functions -- the original casing of an identifier whose case fold does
			// not round-trip (see Lowerer.ParamDecls). BOTH spellings are registered, so the declared name and the
			// emitted one each bind.
			//
			// Two passes, because the two kinds of name can collide across parameters (a `[UserDeclaredName("text")]` on one
			// while another is literally called `text`). Declared names go in first and are never displaced: they are
			// what the documentation promises, and what ParamNameTests pins. A single pass would hand the collision
			// to whichever parameter came first.
			for (var i = 0; i < parameters.Length; i++)
				if (Bindable(i, out var p))
				{
					var declared = Normalize(p.GetCustomAttribute<Keysharp.Runtime.UserDeclaredNameAttribute>()?.Name);

					if (declared.Length != 0)
						map[declared] = i;
				}

			for (var i = 0; i < parameters.Length; i++)
				if (Bindable(i, out var p))
				{
					var metaName = Normalize(p.Name);

					if (metaName.Length != 0)
						_ = map.TryAdd(metaName, i);
				}

			return map;

			// The variadic tail is positional by nature. The receiver is not an argument: the implicit `this` of a C#
			// instance method never appears in `parameters` at all, but the explicit `object @this` convention
			// (Any.HasProp(@this, obj), and every lowered user method) puts it at index 0.
			bool Bindable(int i, out ParameterInfo p)
			{
				p = parameters[i];
				return i != variadicParamIndex && !(i == 0 && IsExplicitThis(p));
			}

			// A lowered script assembly keeps the C# keyword escape in metadata (NameMangler.Escape emits the
			// identifier text verbatim), so a parameter named `class` arrives as "@class" while the same parameter
			// in hand-written C# arrives as "class". Normalize so both bind by the name scripts use.
			static string Normalize(string n) => string.IsNullOrEmpty(n) ? "" : n[0] == '@' ? n.Substring(1) : n;
		}

		public static MethodPropertyHolder GetOrAdd(MethodInfo mi)
        {
            return methodCache.GetOrAdd(mi, key => new MethodPropertyHolder(mi));
        }

		internal static MethodPropertyHolder GetOrAdd(PropertyInfo pi)
		{
            return propertyCache.GetOrAdd(pi, key => new MethodPropertyHolder(pi));
        }

		internal static MethodPropertyHolder GetOrAdd(FieldInfo fi)
		{
            return fieldCache.GetOrAdd(fi, key => new MethodPropertyHolder(fi));
        }

        public MethodPropertyHolder() { }

		public MethodPropertyHolder(MethodInfo m)
		{
			mi = m;
			compatibilityVersion = mi.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()?.Version;
			moduleType = ResolveModuleType(mi.DeclaringType);

            IsStatic = mi.IsStatic;
			IsStaticFunc = mi.Attributes.HasFlag(MethodAttributes.Static);
			isGuiType = Gui.IsGuiType(mi.DeclaringType);
			IsCompilerGenerated = mi.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
			var hasHiddenThis = !IsStatic && !IsCompilerGenerated;
			if (hasHiddenThis) // Built-in instance method, so account for the implicit "this"
			{
				MinParams++; MaxParams++;
			}

			parameters = mi.GetParameters();
			ParamLength = parameters.Length;

			// Determine if the method is a set_Item overload.
			isSetter = mi.Name.StartsWith(setterPrefix) || mi.Name.StartsWith(classSetterPrefix);
			isItemSetter = isSetter && (mi.Name == "set_Item" || mi.Name.Equals("set___Item", StringComparison.OrdinalIgnoreCase));
			var req = new List<int>(ParamLength);

			for (var i = 0; i < parameters.Length; i++)
			{
				var pmi = parameters[i];

                if (pmi.IsVariadic() || ((pmi.ParameterType == typeof(object[])) && (i == (isItemSetter ? parameters.Length - 2 : parameters.Length - 1))))
                    variadicParamIndex = i;
                else
                {
					if (!pmi.IsOptional)
					{
						MinParams++;
						req.Add(i);
					}
				}
			}
			requiredIdx = req.ToArray();

			if (isSetter) // Allow value to be unset
                MinParams--;

			MaxParams = parameters.Length + (hasHiddenThis ? 1 : 0) - (variadicParamIndex == -1 ? 0 : 1);

			// Whether MinParams/MaxParams COUNT the receiver: a hidden-this instance method adds it above, and the
			// explicit `object @this` convention carries it as parameters[0]. Anything reasoning about how many
			// arguments a caller actually passes must correct for it via ReceiverCorrection -- never with
			// NamedArgBinder.ArgBase, which answers a different question (the args-slot offset of parameter 0).
			receiverInCounts = hasHiddenThis || (IsStatic && parameters.Length > 0 && IsExplicitThis(parameters[0]));

			anyOptional = variadicParamIndex != -1 || MinParams != MaxParams;

			var isKeysharpFunc = typeof(KeysharpFunc).IsAssignableFrom(mi.DeclaringType);

			if (isKeysharpFunc && mi.Name == "Bind")
				IsBind = true;

			if (isKeysharpFunc && mi.Name == "Call" && !mi.IsStatic)
			{
				_callFunc = (inst, obj) =>
				{
					// When inst is null the KeysharpFunc was obtained as an unbound prototype method
					// (e.g. MsgBox.Call.Bind(MsgBox)). The caller has prepended the target as
					// the first argument, so shift it into the instance role.
					if (inst == null && obj?.Length > 0 && obj[0] is KeysharpFunc fo)
						return fo.Call(obj[1..]);
					return ((KeysharpFunc)inst).Call(obj);
				};
			}
		}

		public MethodPropertyHolder(PropertyInfo p)
		{
			pi = p;
			compatibilityVersion = (pi.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()
				?? pi.GetMethod?.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()
				?? pi.SetMethod?.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>())?.Version;
			moduleType = ResolveModuleType(pi.DeclaringType);
			isGuiType = Gui.IsGuiType(pi.DeclaringType);
			parameters = pi.GetIndexParameters();
			ParamLength = parameters.Length;
			MinParams = MaxParams = ParamLength;

			// Decided once here rather than per get/set: a property whose type is not one AutoHotkey has needs its
			// value widened on the way out, and an `object`-typed one -- the overwhelming majority -- needs no
			// conversion in either direction, so it keeps the bare reflection setter it had before this policy.
			var kind = ArgCoercer.KindOf(pi.PropertyType);
			var normalize = ArgCoercer.IsNarrow(kind);
			var coerce = kind != ArgCoercer.Kind.None;

			if (pi.GetAccessors().Any(x => x.IsStatic))
			{
				IsStaticProp = true;

				if (isGuiType)
				{
					_callFunc = (inst, obj) =>//Gui calls aren't worth optimizing further.
					{
						object ret = null;
						var ctrl = (inst ?? obj[0]).GetControl();//If it's a gui control, then invoke on the gui thread.
						ctrl.CheckedInvoke(() =>
						{
							ret = pi.GetValue(null);
						}, true);//This can be null if called before a Gui object is fully initialized.

						return normalize ? ArgCoercer.NormalizeScalar(ret) : ret;
					};
					// Coerce on the calling thread, so a conversion TypeError is raised there rather than marshaled.
					// This is what makes `gui.MarginX := 5.5` survivable: reflection's own binder is stricter than the
					// script language and rejects a Float for the Int64 property with an ArgumentException, which is
					// not a KeysharpException and so killed the process past any try/catch.
					SetProp = (inst, arg) =>
					{
						arg = ArgCoercer.CoerceValue(arg, pi.PropertyType);
						var ctrl = inst.GetControl();//If it's a gui control, then invoke on the gui thread.
						ctrl.CheckedInvoke(() => pi.SetValue(null, arg), true);//This can be null if called before a Gui object is fully initialized.
					};
				}
				else
				{
					if (normalize)
						_callFunc = (inst, obj) => ArgCoercer.NormalizeScalar(pi.GetValue(null));
					else
						_callFunc = (inst, obj) => pi.GetValue(null);

					SetProp = coerce ? (inst, obj) => pi.SetValue(null, ArgCoercer.CoerceValue(obj, pi.PropertyType))
									 : (inst, obj) => pi.SetValue(null, obj);
				}
			}
			else
			{
				if (isGuiType)
				{
					_callFunc = (inst, args) =>
					{
						object ret = null;
						var ctrl = (inst ?? args[0]).GetControl();//If it's a gui control, then invoke on the gui thread.
						ctrl.CheckedInvoke(() =>
						{
							ret = pi.GetValue(inst ?? args[0]);
						}, true);//This can be null if called before a Gui object is fully initialized.

						return normalize ? ArgCoercer.NormalizeScalar(ret) : ret;
					};
					// See the static branch above for why the coercion happens here rather than inside CheckedInvoke.
					SetProp = (inst, obj) =>
					{
						obj = ArgCoercer.CoerceValue(obj, pi.PropertyType);
						var ctrl = inst.GetControl();//If it's a gui control, then invoke on the gui thread.
						ctrl.CheckedInvoke(() => pi.SetValue(inst, obj), true);//This can be null if called before a Gui object is fully initialized.
					};
				}
				else
				{
					if (normalize)
						_callFunc = (inst, obj) => ArgCoercer.NormalizeScalar(pi.GetValue(inst));
					else
						_callFunc = (inst, obj) => pi.GetValue(inst);

					// Deliberately still reflection, unlike the compiled setter the FieldInfo constructor builds:
					// FindAndCacheProperty creates an MPH for EVERY property of a type the first time any one of
					// them is named, so compiling here would pay an Expression.Compile per property of every type a
					// script touches -- for a delegate that is usually never called, since an Any-derived builtin
					// resolves an assignment through its prototype's set_X accessor and never reaches SetProp.
					SetProp = coerce ? (inst, obj) => pi.SetValue(inst, ArgCoercer.CoerceValue(obj, pi.PropertyType))
									 : pi.SetValue;
				}
			}
		}

		public MethodPropertyHolder(FieldInfo f)
		{
			fi = f;
			moduleType = ResolveModuleType(fi.DeclaringType);
			IsStatic = fi.IsStatic;
			isGuiType = Gui.IsGuiType(fi.DeclaringType);
			parameters = System.Array.Empty<ParameterInfo>();
			ParamLength = 0;
			MinParams = MaxParams = 0;

			if (!fi.IsInitOnly && !fi.IsLiteral)
			{
				var instParam = Expression.Parameter(typeof(object), "inst");
				var valParam = Expression.Parameter(typeof(object), "value");
				Expression assignExpr;
				// Same conversion policy as parameters and properties: a script assigning to a typed field must
				// not be able to raise an uncatchable InvalidCastException out of the compiled setter.
				var coercedVal = ArgCoercer.Coerce(valParam, fi.FieldType);
				if (fi.IsStatic)
					assignExpr = Expression.Assign(Expression.Field(null, fi), coercedVal);
				else
					assignExpr = Expression.Assign(Expression.Field(Expression.Convert(instParam, fi.DeclaringType), fi), coercedVal);
				SetProp = Expression.Lambda<Action<object, object>>(assignExpr, instParam, valParam).Compile();
			}
		}

		// Allow creating a "fake" MPH for ObjBindMethod
		public MethodPropertyHolder(string name)
		{
			_name = name;
			variadicParamIndex = 1;
		}

		private static Type ResolveModuleType(Type type)
		{
			for (var t = type; t != null; t = t.DeclaringType)
			{
				if (typeof(Keysharp.Runtime.Module).IsAssignableFrom(t))
					return t;
			}

			return null;
		}

		internal static void ClearCache()
		{
			methodCache.Clear();
            propertyCache.Clear();
			fieldCache.Clear();
		}
	}
	/**
	 * As of 10/2025 I've investigated multiple approaches on how to best do function invokes:
	 * 1) MethodBase.Invoke: slow, throws TargetInvocationExceptions which need to be upwrapped and rethrown (slow)
	 * 2) MethodInvoker.Invoke: not much faster than MethodBase.Invoke, but doesn't wrap exceptions in TargetInvocationException.
	 * 3) Code generation: complex, usually requires writing a separate package/project, is not applicable to user code
	 *		since it is often dynamically generated too.
	 * 4) IL.Emit: just as fast as expression trees (the current approach), but more complex to implement and maintain.
	 *      Additionally, expression trees can be interpreted in environments that don't allow dynamic code generation.
	 *      Downside is that IL.Emit and expression trees have a bit more overhead during the initial compilation,
	 *      and require loading large dlls.
	 */
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class DelegateFactory
	{
		public static Func<object, object[], object> CreateDelegate(MethodInfo mi)
			=> CreateDelegate(MethodPropertyHolder.GetOrAdd(mi));

		public static Func<object, object[], object> CreateDelegate(PropertyInfo property)
		{
			if (property == null) throw new ArgumentNullException(nameof(property));
			var getter = property.GetGetMethod(true) ?? throw new ArgumentException("The provided property does not have a getter.", nameof(property));
			return CreateDelegate(MethodPropertyHolder.GetOrAdd(getter));
		}

		public static Func<object, object[], object> CreateDelegate(FieldInfo field)
		{
			if (field == null) throw new ArgumentNullException(nameof(field));
			return CreateFieldDelegate(MethodPropertyHolder.GetOrAdd(field));
		}

		public static Func<object, object[], object> CreateDelegate(MethodPropertyHolder mph)
		{
			if (mph.fi != null)
				return CreateFieldDelegate(mph);

			var mi = mph.mi ?? throw new ArgumentNullException(nameof(mph.mi));
			var ps = mph.parameters;
			var isInstance = !mph.IsStatic;
			var isCompilerGenerated = mph.IsCompilerGenerated;
			var isVariadic = mph.IsVariadic;
			int paramCount = ps.Length;

			// Precompute "soft optional" + boxed defaults once.
			var isSoft = new bool[paramCount];
			var defaults = new object[paramCount];

			for (int i = 0; i < paramCount; i++)
			{
				// Treat the final "value" of any setter (including set_Item) as soft-optional
				bool soft =
					ps[i].IsOptional ||
					ps[i].HasDefaultValue ||
					(mph.isSetter && i == paramCount - 1);

				isSoft[i] = soft;
				defaults[i] = soft ? MaterializeDefault(ps[i]) : null;
			}

			// Compile the small "core" once. The variadic index has to go along: by the time the core runs,
			// NormalInvoke has already packed a real object[] into that slot, and it must not be coerced.
			var core = CompileCore(mi, ps, isSoft, defaults, mph.variadicParamIndex);

			return NormalInvoke;

			// The returned delegate performs:
			//  - exact arg-count validation
			//  - instance splicing convention
			//  - params packing (incl. set_Item)
			//  - then calls the compiled core (which handles defaults & per-slot null checks)
#if !INTERNALDEBUG
			[DebuggerStepThrough]
#endif
			object NormalInvoke(object instance, object[] args)
			{
				args ??= System.Array.Empty<object>();

				// ---- fold named arguments into positional slots ----
				// This has to happen BEFORE the count validation below: that trims trailing nulls to work out how many
				// arguments were really supplied, and would otherwise count the trailing container as a positional one
				// (so a named argument filling a required parameter would read as "too few arguments").
				// See NamedArgBinder.ArgBase for why the offset depends on how the receiver is being passed.
				// NEVER for a setter: an assignment's value slot is a DATA channel with no named-argument syntax,
				// and its value arrives trailing -- binding would eat a container being ASSIGNED (`r.__Value := na`,
				// a for-loop writing a forwarded container element into `&out`) as a named argument of the accessor.
				if (!mph.isSetter && NamedArgBinder.Has(args))
					args = NamedArgBinder.Bind(mph, args, NamedArgBinder.ArgBase(mph, instance));

				// ---- validate the argument count ----
				int lastProvided = args.Length;
				int provided = lastProvided + (instance == null ? 0 : 1);
				if (isInstance && isCompilerGenerated) provided--; // account for Inst-provided 'this', which does not count as argument

				for (int i = lastProvided - 1; i >= 0; i--)
				{
					if (args[i] == null) provided--;
					else break;
				}

				if (provided < mph.MinParams)
					throw new ValueError($"Too few arguments provided for function {mph.Name}");

				if (!isVariadic && provided > mph.MaxParams)
					throw new ValueError($"Too many arguments provided for function {mph.Name}");

				// ---- instance splicing ----
				object target;
				int start = 0;
				object[] working = args;

				if (isInstance)
				{
					if (instance != null)
					{
						target = instance;
					}
					else
					{
						if (working.Length == 0)
							throw new ValueError($"Too few arguments provided for function {mph.Name}");

						target = working[0];
						start = 1;
					}
				}
				else
				{
					target = null;
					if (instance != null)
					{
						var combined = new object[working.Length + 1];
						combined[0] = instance;
						System.Array.Copy(working, 0, combined, 1, working.Length);
						working = combined;
					}
				}

				int eff = Math.Max(0, working.Length - start);

				// ---- params packing (if any) ----
				if (isVariadic)
				{
					int k = mph.variadicParamIndex;

					if (mph.isItemSetter)
					{
						// set_Item(params object[] keys, object value)
						// formal shape: [ .. fixed .., k = keys[], k+1 = value ]
						int needed = paramCount; // k + 2
						if (eff >= needed)
						{
							// Already enough to rewrite in-place
							if (!(eff == needed && working[start + k] is object[]))
							{
								int keyCount = eff - 1 - k;
								var keys = keyCount <= 0 ? System.Array.Empty<object>() : new object[keyCount];
								for (int j = 0; j < keyCount; j++)
									keys[j] = working[start + k + j];

								working[start + k] = keys;
								working[start + k + 1] = working[start + eff - 1];
							}

							return core(target, working, start);
						}
						else
						{
							// Expand and synthesize keys[] + optional value
							var expanded = new object[start + needed];
							System.Array.Copy(working, 0, expanded, 0, Math.Min(working.Length, expanded.Length));

							int avail = eff;
							if (avail <= k)
								throw new ArgumentError(); // missing required head

							int keysAvail = Math.Max(0, avail - 1 - k);
							var keys = keysAvail == 0 ? System.Array.Empty<object>() : new object[keysAvail];
							for (int j = 0; j < keysAvail; j++)
								keys[j] = working[start + k + j];

							expanded[start + k] = keys;
							expanded[start + k + 1] = (avail > k) ? working[start + avail - 1] : null;

							return core(target, expanded, start);
						}
					}
					else
					{
						// Normal params at [k]
						int final = paramCount;

						if (paramCount == 1 && start == 0)
							return core(target, [working], start);

						if (eff > final)
						{
							int tail = eff - k;
							var packed = tail <= 0 ? System.Array.Empty<object>() : new object[tail];
							for (int j = 0; j < tail; j++)
								packed[j] = working[start + k + j];

							working[start + k] = packed;
							return core(target, working, start);
						}

						if (eff == final)
						{
							if (working[start + k] is not object[])
								working[start + k] = new object[] { working[start + k] };
							return core(target, working, start);
						}

						// eff < final → synthesize empty params
						var expanded = new object[start + final];
						System.Array.Copy(working, 0, expanded, 0, Math.Min(working.Length, expanded.Length));
						expanded[start + k] = System.Array.Empty<object>();
						return core(target, expanded, start);
					}
				}

				// No params → just run the compiled core (which fills defaults & validates per-slot nulls).
				return core(target, working, start);
			};
		}

		// ----------------- Expression core -----------------

		private static Func<object, object[], int, object> CompileCore(
			MethodInfo mi,
			ParameterInfo[] ps,
			bool[] isSoft,
			object[] defaults,
			int variadicParamIndex = -1)
		{
			var pTarget = Expression.Parameter(typeof(object), "target");
			var pArgs = Expression.Parameter(typeof(object[]), "args");
			var pStart = Expression.Parameter(typeof(int), "start");
			var argsLen = Expression.ArrayLength(pArgs);

			var a = new Expression[ps.Length];

			for (int i = 0; i < ps.Length; i++)
			{
				var idx = Expression.Add(pStart, Expression.Constant(i));
				var inRange = Expression.LessThan(idx, argsLen);
				var elem = Expression.ArrayIndex(pArgs, idx);
				var valOrNull = Expression.Condition(inRange, elem, Expression.Constant(null, typeof(object)));

				Expression chosen;
				if (isSoft[i])
				{
					chosen = Expression.Condition(
						Expression.Equal(valOrNull, Expression.Constant(null, typeof(object))),
						Expression.Constant(defaults[i], typeof(object)),
						valOrNull);
				}
				else
				{
					// Throw for non-optional missing/null via a C# helper rather than Expression.Throw.
					// An expression-tree throw emits a raw IL throw that bypasses the user-defined Error->Exception
					// operator, so the CLR would wrap the non-Exception Error in a RuntimeWrappedException. Going
					// through ThrowMissingArgument() lets the C# compiler apply the operator normally; the resulting
					// KeysharpException carries the ArgumentError as its UserError and surfaces as a normal Keysharp error.
					// The 1-based position and parameter name are baked in per slot so the message points at the culprit.
					var throwArgErr = Expression.Call(throwMissingArgumentMethod,
						Expression.Constant(i + 1),
						Expression.Constant(ps[i].Name, typeof(string)));

					chosen = Expression.Condition(
						Expression.Equal(valOrNull, Expression.Constant(null, typeof(object))),
						throwArgErr,
						valOrNull);
				}

				// The packed variadic slot is handed over as-is; everything else goes through the one conversion
				// policy, which is an identity for `object` (the overwhelming majority of members).
				a[i] = i == variadicParamIndex
					   ? Expression.Convert(chosen, ps[i].ParameterType)
					   : ArgCoercer.Coerce(chosen, ps[i].ParameterType);
			}

			Expression call;
			if (mi.IsStatic)
			{
				call = Expression.Call(mi, a);
			}
			else
			{
				var decl = mi.DeclaringType!;
				var inst = Expression.Convert(pTarget, decl); // castclass/unbox.any semantics
				call = Expression.Call(inst, mi, a);
			}

			Expression body =
				mi.ReturnType == typeof(void)
					? Expression.Block(call, Expression.Constant(null, typeof(object)))
					: ArgCoercer.NormalizeReturn(call, mi.ReturnType);

			return Expression.Lambda<Func<object, object[], int, object>>(body, pTarget, pArgs, pStart)
							 .Compile();
		}

		private static Func<object, object[], object> CreateFieldDelegate(MethodPropertyHolder mph)
		{
			var fi = mph.fi ?? throw new ArgumentNullException(nameof(mph.fi));
			var isStatic = fi.IsStatic;

			var instParam = Expression.Parameter(typeof(object), "inst");
			Expression fieldExpr;
			if (isStatic)
				fieldExpr = Expression.Field(null, fi);
			else
				fieldExpr = Expression.Field(Expression.Convert(instParam, fi.DeclaringType), fi);

			var boxedField = ArgCoercer.NormalizeReturn(fieldExpr, fi.FieldType);
			var getter = Expression.Lambda<Func<object, object>>(boxedField, instParam).Compile();

			return (inst, args) =>
			{
				object target = inst;
				if (!isStatic && target == null && args != null && args.Length > 0)
					target = args[0];

				return getter(target);
			};
		}

		private static object MaterializeDefault(ParameterInfo p)
		{
			var def = p.DefaultValue;
			return (def is DBNull || def == System.Reflection.Missing.Value) ? null : def;
		}

		private static readonly MethodInfo throwMissingArgumentMethod =
			typeof(DelegateFactory).GetMethod(nameof(ThrowMissingArgument), BindingFlags.NonPublic | BindingFlags.Static);

		// Thrown from the compiled core when a required parameter is missing or unset. Declared in C# (not via
		// Expression.Throw) so the Error->Exception operator is applied and the error surfaces normally.
		[StackTraceHidden]
		private static object ThrowMissingArgument(int position, string name)
			=> throw new ArgumentError($"Parameter #{position}{(string.IsNullOrEmpty(name) ? "" : $" ('{name}')")} is required but was omitted or unset.");
	}

#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class FastCtor
	{
		private static readonly ConcurrentDictionary<System.Type, Func<object[], object>> Cache = new();

		// Semantics: always call a public ctor like:  new T(object[] args)
		public static object Call(Type type, params object[] args)
		{
			var activator = Cache.GetOrAdd(type, BuildFactory);
			return activator(args);
		}

		private static Func<object[], object> BuildFactory(System.Type type)
		{
			// Look for a single public instance ctor with signature (object[])
			var ctor = type.GetConstructor(new[] { typeof(object[]) });
			if (ctor is null)
			{
				return a => System.Activator.CreateInstance(type, a)!;
			}

			var a = System.Linq.Expressions.Expression.Parameter(typeof(object[]), "args");
			var body = System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.New(ctor, a), typeof(object));
			return System.Linq.Expressions.Expression.Lambda<Func<object[], object>>(body, a).Compile();
		}
	}
}
