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
		public Func<object, object[], object> CallFunc => _callFunc ??= CreateCallFunc();

		private Func<object, object[], object> CreateCallFunc()
		{
			var call = DelegateFactory.CreateDelegate(this);
			return mi != null && RequiresUIThread(mi, AccessorProperty()) ? OnUIThread(call) : call;
		}

		/// <summary>A member marked <c>[RequiresUIThread]</c> as a script calls it: inline on the UI thread, else marshaled there.</summary>
		private static Func<object, object[], object> OnUIThread(Func<object, object[], object> call) =>
			(inst, args) => TheScript.IsOnMainThread ? call(inst, args) : CallStack.InvokeOnUIThread(call, inst, args);

		/// <summary>Whether this is declared in a script's module, whose module and compatibility version it runs under.</summary>
		internal readonly bool InScriptModule;

		/// <summary>Whether this is a script function, which records its locations in its frame, rather than a builtin.</summary>
		internal readonly bool IsScript;

		internal MemberInfo memberInfo => ((MemberInfo)mi ?? pi) ?? fi;
		internal readonly MethodInfo mi;
		internal readonly ParameterInfo[] parameters;
		internal readonly PropertyInfo pi;
		internal readonly FieldInfo fi;
		internal readonly Type moduleType;
		internal readonly int? compatibilityLevel;
		private Action<object, object> setProp;
		internal Action<object, object> SetProp
		{
			get => setProp ??= HasSetter ? CreateFieldSetter() : null;
			private set => setProp = value;
		}
		// Checking writability must not compile accessors while discovering module variables.
		internal bool HasSetter => fi != null ? !fi.IsInitOnly && !fi.IsLiteral : setProp != null;
		internal readonly bool anyOptional;
		internal readonly bool isSetter;
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
		internal int ParamLength { get; }
		internal int MinParams = 0;
		internal int MaxParams = 0;

		internal static bool HasScriptGetter(PropertyInfo property) => property.GetMethod?.IsPublic == true;

		internal static bool HasScriptSetter(PropertyInfo property)
		{
			var setter = property.SetMethod;
			// Reflection otherwise presents an init accessor as an ordinary public setter.
			return setter?.IsPublic == true
				&& !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
		}

		internal static bool IsInlineBoundary(MethodInfo method) =>
			method.DeclaringType?.Namespace == Keywords.MainNamespaceName
			&& (method.IsDefined(typeof(InlineCSharpAttribute), false)
				|| method.IsSpecialName && method.Name.Length > 4
				&& (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal))
				&& method.DeclaringType.GetProperty(method.Name[4..], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
					?.IsDefined(typeof(InlineCSharpAttribute), false) == true);

        internal const int DotNetMaxParams = 8192; // https://www.tabsoverspaces.com/233892-whats-the-maximum-number-of-arguments-for-method-in-csharp-and-in-net

		/// <summary>
		/// Whether a parameter is the explicit <c>object @this</c> receiver rather than a real argument. ONE
		/// definition, because two consumers have to agree exactly: <c>BuildParamIndexMap</c> excludes it from the
		/// name map, and <c>NamedArgBinder.ArgBase</c> shifts every name's slot down by one when it is present
		/// (via <see cref="receiverInCounts"/>). If those two ever disagreed, a name would resolve to slot -1 and
		/// index outside the argument array.
		/// </summary>
		private static bool IsExplicitThis(ParameterInfo p) =>
			p.Name.AsSpan().TrimStart('@').Equals("this", StringComparison.OrdinalIgnoreCase);

		// Our own accessor convention for a hand-written Core member, which has no property to pair with:
		// `Clipboard.staticget_Text`, `Structs.get_Size`. NameMangler.StaticGetter emits the same shape.
		private const string getterPrefix = "get_";
		private const string setterPrefix = "set_";
        private const string classSetterPrefix = Keywords.ClassStaticPrefix + setterPrefix;

		private string _name, _qualifiedName;
		/// <summary>The bare member name used for dispatch: never qualified, never accessor-suffixed.</summary>
		internal string Name => _name ??= BuildName();
		/// <summary>The fully qualified AHK-visible name — <c>Class.Prototype.Method</c>, <c>Class.Property.Get</c> —
		/// which is what <c>Func.Name</c> and <c>A_ThisFunc</c> report. Empty for a callable a script cannot name.</summary>
		internal string QualifiedName => _qualifiedName ??= BuildQualifiedName();

		/// <summary>
		/// The property an accessor belongs to. Metadata already pairs the two, so the <c>get_</c>/<c>set_</c> the C#
		/// compiler puts in front of an accessor's name is never parsed back off it.
		/// </summary>
		private PropertyInfo AccessorProperty()
		{
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
			return mi.IsSpecialName
				   ? mi.DeclaringType?.GetProperties(flags).FirstOrDefault(p => p.GetMethod == mi || p.SetMethod == mi)
				   : null;
		}

		private static bool RequiresUIThread(MethodInfo method, PropertyInfo property) =>
			method?.IsDefined(typeof(RequiresUIThreadAttribute), false) == true
			|| property?.IsDefined(typeof(RequiresUIThreadAttribute), false) == true
			|| (method?.DeclaringType ?? property?.DeclaringType)?.IsDefined(typeof(RequiresUIThreadAttribute), false) == true;

		/// <summary>The bare script name of a property: our own static prefix off, C#'s indexer name mapped to AHK's.</summary>
		private static string PropertyScriptName(PropertyInfo property)
		{
			if (GetUserDeclaredName(property) is { } declared)
				return declared;

			var name = property.Name;
			_ = StripPrefix(ref name, Keywords.ClassStaticPrefix);
			return name == "Item" ? "__Item" : name;
		}

		private string BuildName()
		{
			if (mi == null)
				return pi?.Name ?? fi?.Name ?? "";

			if (GetUserDeclaredName(mi) is { } declaredName)
				return declaredName;

			// Every local function the Lowerer emits carries the attribute above, so a name the C# compiler chose
			// for one names nothing a script could write.
			if (mi.Name.StartsWith("<", StringComparison.Ordinal))
				return "";

			if (AccessorProperty() is { } property)
				return PropertyScriptName(property);

			var name = mi.Name;
			_ = StripPrefix(ref name, Keywords.ClassStaticPrefix);
			if (!StripPrefix(ref name, getterPrefix))
				_ = StripPrefix(ref name, setterPrefix);
			if (name == "Item")
				name = "__Item";
			return name.Length == 0 ? nameof(KeysharpFunc.Call) : name;
		}

		private string BuildQualifiedName()
		{
			if (mi == null)
				return pi?.Name ?? fi?.Name ?? "";

			// The exact source spelling of the member itself, which the mangler TitleCases away (`mixedCase` is
			// emitted as `get_MixedCase`), so the qualified name is built from this rather than from the identifier.
			var declaredName = GetUserDeclaredName(mi);

			// A local function belongs to its lexical scope, not to the display class the C# compiler invented for
			// it. Every one the Lowerer emits carries the attribute, so that invented name is never read back.
			if (mi.Name.StartsWith("<", StringComparison.Ordinal))
				return declaredName ?? "";

			var emittedName = mi.Name;
			var declaringType = mi.DeclaringType;
			var isCompiledProgramContainer = declaringType != null && (declaringType == moduleType
				|| declaringType.Name == Keywords.MainClassName && declaringType.Namespace == Keywords.MainNamespaceName);
			if (isCompiledProgramContainer && declaredName == null
					&& !mi.Name.StartsWith(Keywords.TopLevelFunctionPrefix, StringComparison.Ordinal)
					&& !mi.IsDefined(typeof(InlineCSharpAttribute), false))
				return "";

			var isStaticMember = StripPrefix(ref emittedName, Keywords.ClassStaticPrefix)
				|| mi.GetCustomAttribute<StaticAttribute>() != null;
			// A real property pairs with its accessor in metadata, so which accessor this is never comes from the
			// method's spelling. A hand-written Core accessor has no property, and there our own get_/set_
			// convention names it. Always capitalised: AutoHotkey reports the case of the `get`/`set` keyword as
			// written, which C# erases, and Keysharp deliberately does not carry it -- only the member's own case,
			// which it does preserve, is worth the machinery.
			var property = AccessorProperty();
			string propertyName = null;
			string accessor;

			if (property != null)
			{
				accessor = property.GetMethod == mi ? ".Get" : ".Set";
				isStaticMember = isStaticMember
					|| property.GetCustomAttribute<StaticAttribute>() != null
					|| property.Name.StartsWith(Keywords.ClassStaticPrefix, StringComparison.Ordinal);
				propertyName = PropertyScriptName(property);
			}
			else
			{
				accessor = StripPrefix(ref emittedName, getterPrefix) ? ".Get"
					: StripPrefix(ref emittedName, setterPrefix) ? ".Set"
					: "";
			}

			var topLevel = declaringType == null || isCompiledProgramContainer
				|| declaringType.IsAbstract && declaringType.IsSealed;
			if (topLevel)
				_ = StripPrefix(ref emittedName, Keywords.TopLevelFunctionPrefix);

			var funcName = propertyName ?? declaredName
				?? (accessor.Length != 0 && emittedName == "Item" ? "__Item" : emittedName);
			if (topLevel)
				return funcName + accessor;

			var className = ScriptVisibleClassName(declaringType, moduleType);
			return className.Length == 0
				? funcName + accessor
				: $"{className}.{(isStaticMember ? "" : "Prototype.")}{funcName}{accessor}";
		}

		private static bool StripPrefix(ref string name, string prefix)
		{
			if (!name.StartsWith(prefix, StringComparison.Ordinal))
				return false;

			name = name.Substring(prefix.Length);
			return true;
		}

		private static string ScriptVisibleClassName(Type declaringType, Type moduleType)
		{
			var parts = new Stack<string>();

			for (var cur = declaringType; cur != null && cur != moduleType; cur = cur.DeclaringType)
			{
				if (cur.Name == Keywords.MainClassName && cur.Namespace == Keywords.MainNamespaceName
						|| cur.IsDefined(typeof(CompilerGeneratedAttribute), false))
					continue;

				parts.Push(GetUserDeclaredName(cur) ?? cur.Name);
			}

			return string.Join(".", parts);
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

			var byIndex = new HashSet<int>(ParamIndexByName.Values);

			for (var i = 0; i < parameters.Length; i++)
			{
				var p = parameters[i];
				var isVariadic = i == variadicParamIndex;

				if (!isVariadic && !byIndex.Contains(i))
					continue;

				var hasDefault = !isVariadic && p.HasDefaultValue && p.DefaultValue is object dv && dv is not DBNull;
				scan.Add(new ParamScanEntry(i, ScriptParameterName(p),
											isVariadic || p.IsOptional,
											p.GetCustomAttribute<Keysharp.Runtime.ByRefAttribute>() != null,
											isVariadic,
											hasDefault,
											hasDefault ? p.DefaultValue : null));
			}

			return scan;
		}

		internal string ScriptParameterName(ParameterInfo parameter)
		{
			var declared = parameter.GetCustomAttribute<Keysharp.Runtime.UserDeclaredNameAttribute>()?.Name;
			var name = declared ?? parameter.Name ?? "";

			if (name.StartsWith('@'))
				name = name.Substring(1);

			if (declared == null && parameter.Position == variadicParamIndex && parameter.ParameterType == typeof(object[])
				&& name.StartsWith(Keywords.InternalPrefix, StringComparison.Ordinal))
				name = name.Substring(Keywords.InternalPrefix.Length);

			var builtin = memberInfo?.DeclaringType?.Assembly == typeof(Ks).Assembly
				&& memberInfo.DeclaringType.Namespace?.StartsWith("Keysharp.Builtins", StringComparison.Ordinal) == true;
			return declared == null && builtin && name.Length != 0
				? char.ToUpperInvariant(name[0]) + name.Substring(1)
				: name;
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
		/// Names ignore Unicode case for named-argument binding. Built once, lazily: most methods are
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

			// Register the documented name first, then the CLR spelling where no documented name claims it.
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
				return i != variadicParamIndex && !(IsStatic && i == 0 && IsExplicitThis(p));
			}

			// A lowered script assembly keeps the C# keyword escape in metadata (NameMangler.Escape emits the
			// identifier text verbatim), so a parameter named `class` arrives as "@class" while the same parameter
			// in hand-written C# arrives as "class". Normalize so both bind by the name scripts use.
			static string Normalize(string n) => string.IsNullOrEmpty(n) ? "" : n[0] == '@' ? n.Substring(1) : n;
		}

		public static MethodPropertyHolder GetOrAdd(MethodInfo mi)
        {
			return methodCache.GetOrAdd(mi, static key => new MethodPropertyHolder(key));
        }

		internal static MethodPropertyHolder GetOrAdd(PropertyInfo pi)
		{
			return propertyCache.GetOrAdd(pi, static key => new MethodPropertyHolder(key));
        }

		internal static MethodPropertyHolder GetOrAdd(FieldInfo fi)
		{
			return fieldCache.GetOrAdd(fi, static key => new MethodPropertyHolder(key));
        }

        public MethodPropertyHolder() { }

		public MethodPropertyHolder(MethodInfo m)
		{
			mi = m;
			compatibilityLevel = mi.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()?.Level;
			moduleType = !mi.IsStatic && typeof(Keysharp.Runtime.Module).IsAssignableFrom(mi.DeclaringType)
				&& mi.DeclaringType.Assembly == typeof(Keysharp.Runtime.Module).Assembly ? null : ResolveModuleType(mi.DeclaringType);
			// Ks and Ahk derive from Module so #import has something to bind against, but their members are builtins.
			InScriptModule = moduleType != null && moduleType.Assembly != typeof(Keysharp.Runtime.Module).Assembly;
			var inlineMarked = MethodPropertyHolder.IsInlineBoundary(mi);
			// A script's inline C# records no locations, so on the call stack it is a builtin.
			IsScript = InScriptModule && !inlineMarked;

            IsStatic = mi.IsStatic;
			IsStaticFunc = mi.Attributes.HasFlag(MethodAttributes.Static);
			IsCompilerGenerated = mi.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
			var hasHiddenThis = !IsStatic && !IsCompilerGenerated;
			if (hasHiddenThis) // Built-in instance method, so account for the implicit "this"
			{
				MinParams++; MaxParams++;
			}

			parameters = mi.GetParameters();
			ParamLength = parameters.Length;

			// Determine if the method is a set_Item overload.
			isSetter = mi.Name.StartsWith(setterPrefix, StringComparison.Ordinal) || mi.Name.StartsWith(classSetterPrefix, StringComparison.Ordinal);
			var isItemSetter = isSetter && (mi.Name == "set_Item" || mi.Name.Equals("set___Item", StringComparison.OrdinalIgnoreCase)
				|| mi.Name.Equals(classSetterPrefix + "__Item", StringComparison.OrdinalIgnoreCase));
			var req = new List<int>(ParamLength);

			for (var i = 0; i < parameters.Length; i++)
			{
				var pmi = parameters[i];

				if (pmi.IsVariadic() || (!inlineMarked && pmi.ParameterType == typeof(object[])
						&& i == (isItemSetter ? parameters.Length - 2 : parameters.Length - 1)))
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
			compatibilityLevel = (pi.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()
				?? pi.GetMethod?.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>()
				?? pi.SetMethod?.GetCustomAttribute<Keysharp.Runtime.CompatibilityModeAttribute>())?.Level;
			moduleType = ResolveModuleType(pi.DeclaringType);
			parameters = pi.GetIndexParameters();
			ParamLength = parameters.Length;
			MinParams = MaxParams = ParamLength;

			// Decide once rather than per access: ordinary properties retain scalar coercion, while an inline
			// boundary additionally unwraps managed proxies for object and value-type targets.
			var kind = ArgCoercer.KindOf(pi.PropertyType);
			var normalize = ArgCoercer.IsNarrow(kind);
			var inlineMarked = pi.DeclaringType?.Namespace == Keywords.MainNamespaceName
				&& pi.IsDefined(typeof(InlineCSharpAttribute), false);
			var coerce = ArgCoercer.NeedsCoercion(pi.PropertyType, inlineMarked);
			var canRead = HasScriptGetter(pi);
			var canWrite = HasScriptSetter(pi);

			IsStaticProp = (pi.GetMethod ?? pi.SetMethod)?.IsStatic == true;
			var isStatic = IsStaticProp;

			if (canRead)
			{
				_callFunc = (inst, args) =>
				{
					var ret = pi.GetValueUnwrapped(isStatic ? null : inst ?? (args is { Length: > 0 } ? args[0] : null));
					return normalize ? ArgCoercer.NormalizeScalar(ret) : ret;
				};
				if (RequiresUIThread(pi.GetMethod, pi))
					_callFunc = OnUIThread(_callFunc);
			}

			if (canWrite)
			{
				var uiSet = RequiresUIThread(pi.SetMethod, pi)
					? (Func<object, object[], object>)((inst, args) => { pi.SetValueUnwrapped(inst, args[0]); return null; })
					: null;
				// Convert on the caller so conversion hooks and continuable errors stay on that thread.
				SetProp = (inst, value) =>
				{
					if (coerce && !ArgCoercer.TryCoerceValue(value, pi.PropertyType, inlineMarked, out value))
						return;

					var target = isStatic ? null : inst;
					if (uiSet != null && !TheScript.IsOnMainThread)
						_ = CallStack.InvokeOnUIThread(uiSet, target, [value]);
					else
						pi.SetValueUnwrapped(target, value);
				};
			}

			if (!canRead)
				_callFunc = (inst, args) => Errors.PropertyErrorOccurred($"Property {pi.Name} is write-only.");

			// Module properties enter through the variable store rather than CompileCore.
			if (inlineMarked)
			{
				var get = _callFunc;
				// Same out-conversion the compiled method path applies to an inline return: a property typed
				// as a CLR class (or `object` holding one) reaches the script wrapped, never raw.
				var convertOut = ArgCoercer.CanLeakClrValue(pi.PropertyType);
				_callFunc = (inst, args) =>
				{
					try
					{
						var ret = get(inst, args);
						return convertOut ? ManagedInvoke.ConvertOut(ret) : ret;
					}
					catch (Exception ex) { return Keysharp.Runtime.Script.MapInlineError<object>(ex, pi.Name); }
				};

				if (SetProp is { } set)
					SetProp = (inst, obj) =>
					{
						try { set(inst, obj); }
						catch (Exception ex) { _ = Keysharp.Runtime.Script.MapInlineError<object>(ex, pi.Name); }
					};
			}
		}

		public MethodPropertyHolder(FieldInfo f)
		{
			fi = f;
			moduleType = ResolveModuleType(fi.DeclaringType);
			IsStatic = fi.IsStatic;
			parameters = System.Array.Empty<ParameterInfo>();
			ParamLength = 0;
			MinParams = MaxParams = 0;
		}

		private Action<object, object> CreateFieldSetter()
		{
			var instParam = Expression.Parameter(typeof(object), "inst");
			var valParam = Expression.Parameter(typeof(object), "value");
			Expression assignExpr;
			// Same conversion policy as parameters and properties: a script assigning to a typed field must
			// not be able to raise an uncatchable InvalidCastException out of the compiled setter.
			var boundary = fi.IsDefined(typeof(InlineCSharpAttribute), false);
			var steps = ArgCoercer.NeedsCoercion(fi.FieldType, boundary) ? new ArgCoercer.ArgumentSteps(null) : null;
			var coercedVal = steps?.Coerce(valParam, fi.FieldType, boundary) ?? Expression.Convert(valParam, fi.FieldType);
			if (fi.IsStatic)
				assignExpr = Expression.Assign(Expression.Field(null, fi), coercedVal);
			else
				assignExpr = Expression.Assign(Expression.Field(Expression.Convert(instParam, fi.DeclaringType), fi), coercedVal);
			return Expression.Lambda<Action<object, object>>(steps?.Wrap(assignExpr) ?? assignExpr, instParam, valParam).Compile();
		}

		// Allow creating a "fake" MPH for ObjBindMethod: it names a member to resolve on the receiver at call time,
		// so both names are the caller's literal and neither can be recovered from metadata there is none of.
		public MethodPropertyHolder(string name)
		{
			_name = _qualifiedName = name;
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

			var isInstance = !mph.IsStatic;
			var isCompilerGenerated = mph.IsCompilerGenerated;
			var isVariadic = mph.IsVariadic;

			// The core reads fixed arguments directly and constructs only the final variadic tail.
			var core = CompileCore(mph);
			// A script function runs under its own module and compatibility version; a builtin keeps its caller's.
			var module = mph.InScriptModule ? ModuleData.GetOrCreate(mph.moduleType) : null;
			var compatibility = mph.compatibilityLevel;
			var scoped = module != null || compatibility != null;

			return NormalInvoke;

			// The returned delegate performs:
			//  - exact arg-count validation
			//  - receiver selection without copying the arguments
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

				// As AutoHotkey reports them: a builtin given too few arguments names itself, while a script function names
				// the first parameter it lacks, which binding the arguments finds.
				if (provided < mph.MinParams && !mph.IsScript)
					throw new ArgumentError("Too few parameters passed to function.", null, mph.QualifiedName);

				if (!isVariadic && provided > mph.MaxParams)
					throw new Error("Too many parameters passed to function.", null, mph.QualifiedName);

				// A negative start supplies static parameter zero through target; positive start skips a CLR receiver.
				object target;
				int start;

				if (isInstance && instance == null)
				{
					if (args.Length == 0)
						throw new ArgumentError("Too few parameters passed to function.", null, mph.QualifiedName);
					(target, start) = (args[0], 1);
				}
				else
					(target, start) = (instance, !isInstance && instance != null ? -1 : 0);

				// The frame goes on once the argument count is accepted, so a count error is the caller's, as in AutoHotkey.
				var stack = CallStack.Current;
				var depth = stack.Push(mph);
				ModuleData previousModule = null;
				int? previousCompatibility = null;
				var switched = false;

				if (scoped)
				{
					previousModule = stack.ModuleOrDefault(TheScript);
					switched = module != null && !ReferenceEquals(previousModule, module);
					previousCompatibility = switched || compatibility != null ? Script.currentCompatibilityLevel : null;

					if (switched)
						stack.Module = module;

					if (switched || compatibility != null)
						Script.currentCompatibilityLevel = compatibility ?? module.CompatibilityLevel;
				}

				try
				{
					return core(target, args, start);
				}
				catch (Exception ex) when (CallStack.RememberAndPass(ex))
				{
					throw;
				}
				finally
				{
					stack.Pop(depth);

					if (switched)
						stack.Module = previousModule;
					if (previousCompatibility != null)
						Script.currentCompatibilityLevel = previousCompatibility.Value;
				}
			};
		}

		// ----------------- Expression core -----------------

		private static Func<object, object[], int, object> CompileCore(MethodPropertyHolder holder)
		{
			var mi = holder.mi ?? throw new ArgumentNullException(nameof(holder.mi));
			var ps = holder.parameters;
			var variadicParamIndex = holder.variadicParamIndex;
			var isVariadicSetter = holder.isSetter && variadicParamIndex >= 0 && variadicParamIndex == ps.Length - 2;
			var pTarget = Expression.Parameter(typeof(object), "target");
			var pArgs = Expression.Parameter(typeof(object[]), "args");
			var pStart = Expression.Parameter(typeof(int), "start");
			var argsLen = Expression.ArrayLength(pArgs);
			// Resolve the boundary once while building the delegate. Ordinary script/built-in calls retain their
			// original input expressions and therefore pay no wrapper check at invocation time.
			var inlineMarked = MethodPropertyHolder.IsInlineBoundary(mi);

			var a = new Expression[ps.Length];
			// A conversion the script continued has to stop the call, so once any argument needs one, every argument
			// is evaluated ahead of the call, in order, where a stop leaves nothing on the evaluation stack.
			var steps = ps.Where((p, i) => i != variadicParamIndex).Any(p => ArgCoercer.NeedsCoercion(p.ParameterType, inlineMarked))
				? new ArgCoercer.ArgumentSteps(ArgCoercer.DefaultObjectResult)
				: null;

			for (int i = 0; i < ps.Length; i++)
			{
				if (i == variadicParamIndex)
				{
					Expression packed = Expression.Call(typeof(DelegateFactory), nameof(PackVariadic), null,
						pTarget, pArgs, pStart, Expression.Constant(i), Expression.Constant(ps.Length), Expression.Constant(isVariadicSetter),
						Expression.Constant(holder.IsScript ? holder.ScriptParameterName(ps[^1]) : ""));
					if (ps[i].IsDefined(typeof(Keysharp.Runtime.VariadicAttribute), false))
						packed = Expression.Call(typeof(Keysharp.Builtins.Array).GetMethod(nameof(Keysharp.Builtins.Array.Literal),
							BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly), packed);
					a[i] = Expression.Convert(packed, ps[i].ParameterType);
					a[i] = steps?.Hold(a[i]) ?? a[i];
					continue;
				}

				var tailValue = isVariadicSetter && i == ps.Length - 1;
				Expression idx = tailValue
					? Expression.Subtract(argsLen, Expression.Constant(1))
					: Expression.Add(pStart, Expression.Constant(i));
				var inRange = Expression.LessThan(idx, argsLen);
				var elem = Expression.ArrayIndex(pArgs, idx);
				var valOrNull = Expression.Condition(inRange, elem, Expression.Constant(null, typeof(object)));
				if (mi.IsStatic && (i == 0 || tailValue))
					valOrNull = Expression.Condition(Expression.Equal(idx, Expression.Constant(-1)), pTarget, valOrNull);

				// An optional parameter, or a setter's final value (set_Item's too), takes its default when missing; a required
				// one throws, naming the parameter only for a script function, as AutoHotkey does.
				var soft = ps[i].IsOptional || ps[i].HasDefaultValue || holder.isSetter && i == ps.Length - 1;
				Expression chosen = Expression.Condition(
					Expression.Equal(valOrNull, Expression.Constant(null, typeof(object))),
					soft ? (Expression)Expression.Constant(MaterializeDefault(ps[i]), typeof(object))
						: Expression.Call(throwMissingArgumentMethod, Expression.Constant(holder.IsScript ? holder.ScriptParameterName(ps[i]) : "")),
					valOrNull);

				if (ps[i].IsDefined(typeof(Keysharp.Runtime.ByRefAttribute), false))
				{
					var reference = Expression.Variable(typeof(object), "reference");
					var name = ps[i].GetCustomAttribute<Keysharp.Runtime.UserDeclaredNameAttribute>()?.Name
							   ?? ps[i].Name?.TrimStart('@');
					chosen = Expression.Block([reference],
						Expression.Assign(reference, chosen),
						Expression.IfThen(Expression.NotEqual(reference, Expression.Constant(null)),
							Expression.Call(demandReferenceMethod, reference, Expression.Constant(false), Expression.Constant(name, typeof(string)))),
						reference);
				}

				a[i] = steps?.Coerce(chosen, ps[i].ParameterType, inlineMarked) ?? Expression.Convert(chosen, ps[i].ParameterType);
			}

			Expression call;
			ParameterExpression receiver = null;
			if (mi.IsStatic)
			{
				call = Expression.Call(mi, a);
			}
			else if (mi.DeclaringType!.IsValueType)
			{
				call = Expression.Call(Expression.Convert(pTarget, mi.DeclaringType), mi, a);
			}
			else
			{
				// A wrong receiver is reported before converting arguments.
				receiver = Expression.Variable(mi.DeclaringType, "receiver");
				call = Expression.Call(receiver, mi, a);
			}

			// An inline member's return crosses the CLR->script boundary, so a declared CLR type -- or an
			// `object` return holding one -- takes the same out-conversion Ks.Clr applies (Clr.Instance/
			// Clr.Type wrapping, numeric widening) instead of leaking a raw CLR object into the script.
			// A member whose declared type cannot leak keeps the compile-time-only NormalizeReturn path.
			Expression body =
				mi.ReturnType == typeof(void)
					? Expression.Block(call, Expression.Constant(null, typeof(object)))
					: inlineMarked && ArgCoercer.CanLeakClrValue(mi.ReturnType)
						? ArgCoercer.ConvertOut(call)
						: ArgCoercer.NormalizeReturn(call, mi.ReturnType);

			if (steps != null)
				body = steps.Wrap(body);

			if (inlineMarked)
			{
				var ex = Expression.Parameter(typeof(Exception), "__ksex");
				body = Expression.TryCatch(
						   Expression.Convert(body, typeof(object)),
						   Expression.Catch(ex,
											Expression.Call(typeof(Keysharp.Runtime.Script), nameof(Keysharp.Runtime.Script.MapInlineError),
															[typeof(object)], ex, Expression.Constant(holder.QualifiedName is { Length: > 0 } name ? name : mi.Name))));
			}

			if (receiver != null)
				body = Expression.Block(typeof(object), [receiver],
						   Expression.Assign(receiver, Expression.TypeAs(pTarget, receiver.Type)),
						   Expression.Condition(
							   Expression.Equal(receiver, Expression.Constant(null, receiver.Type)),
							   Expression.Call(throwReceiverTypeMethod, pTarget, Expression.Constant(receiver.Type, typeof(Type))),
							   Expression.Convert(body, typeof(object))));

			return Expression.Lambda<Func<object, object[], int, object>>(body, pTarget, pArgs, pStart)
							 .Compile();
		}

		// Preserve explicit packed arrays, but never rewrite the caller's slots while collecting a tail.
		private static object[] PackVariadic(object target, object[] args, int start, int index, int paramCount, bool setter, string valueName)
		{
			var count = args.Length - start;
			if (setter && count <= index)
				return (object[])ThrowMissingArgument(valueName);
			if (paramCount == 1 && start == 0) return args;
			var offset = start + index;
			if ((paramCount != 1 || start > 0) && count == paramCount
				&& (offset < 0 ? target : args[offset]) is object[] packed)
				return packed;

			if (offset == 0 && !setter)
				return args;

			var length = Math.Max(0, count - index - (setter ? 1 : 0));
			if (length == 0) return System.Array.Empty<object>();
			var tail = new object[length];
			var prefix = offset < 0 ? 1 : 0;
			if (prefix != 0) tail[0] = target;
			System.Array.Copy(args, Math.Max(0, offset), tail, prefix, length - prefix);
			return tail;
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

			// Only fields originating in an inline block carry the marker, which distinguishes a public
			// `object` field written by the user from the object fields generated for script globals.
			var boxedField = fi.IsDefined(typeof(InlineCSharpAttribute), false) && ArgCoercer.CanLeakClrValue(fi.FieldType)
							 ? ArgCoercer.ConvertOut(fieldExpr)
							 : ArgCoercer.NormalizeReturn(fieldExpr, fi.FieldType);
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
		private static readonly MethodInfo demandReferenceMethod =
			typeof(Refs).GetMethod(nameof(Refs.Demand), [typeof(object), typeof(bool), typeof(string)]);
		private static readonly MethodInfo throwReceiverTypeMethod =
			typeof(DelegateFactory).GetMethod(nameof(ThrowReceiverType), BindingFlags.NonPublic | BindingFlags.Static);

		[StackTraceHidden]
		private static object ThrowReceiverType(object target, Type declaring)
		{
			CallStack.Current.PopUnstarted();
			throw Errors.ExpectedTypeError(Types.TypeName(declaring), target);
		}

		// Thrown from the compiled core when a required parameter is missing or unset. Declared in C# (not via
		// Expression.Throw) so the Error->Exception operator is applied and the error surfaces normally. The function's
		// body has not started, so the error is its caller's, as AutoHotkey reports it.
		[StackTraceHidden]
		private static object ThrowMissingArgument(string name)
		{
			CallStack.Current.PopUnstarted();
			throw new ArgumentError("Missing a required parameter.", null, name);
		}
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
