using Keysharp.Builtins;
using System.Collections.Generic;
using System.Linq.Expressions;

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
			var call = fi != null ? CreateFieldGetter() : DelegateFactory.CreateDelegate(this);
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
			get => Volatile.Read(ref setProp) ?? (HasSetter ? CreateFieldSetter() : null);
			private set => Volatile.Write(ref setProp, value);
		}
		// Checking writability must not compile accessors while discovering module variables.
		internal bool HasSetter => fi != null ? !fi.IsInitOnly && !fi.IsLiteral : setProp != null;
		internal readonly bool isSetter;
		internal readonly int variadicParamIndex = -1;
		internal readonly bool receiverInCounts;

		/// <summary>
		/// The adjustment that turns <see cref="MaxParams"/>/<see cref="MinParams"/> into the number of arguments a
		/// caller supplies, for the receiver this call will really use: -1 when the counts include a receiver that
		/// is supplied out-of-band (so the caller never passes it), else 0.
		/// </summary>
		internal int ReceiverCorrection(object inst) => receiverInCounts && inst != null ? -1 : 0;

        internal static ConcurrentDictionary<MethodInfo, MethodPropertyHolder> methodCache = new();
		internal static ConcurrentDictionary<PropertyInfo, MethodPropertyHolder> propertyCache = new();
		internal static ConcurrentDictionary<FieldInfo, MethodPropertyHolder> fieldCache = new();

		internal bool IsStatic { get; private set; }
		internal bool IsCompilerGenerated { get; private set; }
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

		/// <summary>
		/// Whether a parameter is the explicit <c>object @this</c> receiver rather than a real argument. One
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
				return pi?.Name ?? fi?.Name;

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
			var isCompiledProgramContainer = declaringType != null && (InScriptModule && declaringType == moduleType
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
		/// <c>Buffer.__New</c>), so <c>Any.__New(params object[])</c> is inherited as well and both are public
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

			for (var i = 0; i < parameters.Length; i++)
			{
				var pmi = parameters[i];

				if (pmi.IsVariadic() || (!inlineMarked && pmi.ParameterType == typeof(object[])
						&& i == (isItemSetter ? parameters.Length - 2 : parameters.Length - 1)))
                    variadicParamIndex = i;
				else if (!pmi.IsOptional)
					MinParams++;
			}

			if (isSetter) // Allow value to be unset
                MinParams--;

			MaxParams = parameters.Length + (hasHiddenThis ? 1 : 0) - (variadicParamIndex == -1 ? 0 : 1);

			// Whether MinParams/MaxParams count the receiver: a hidden-this instance method adds it above, and the
			// explicit `object @this` convention carries it as parameters[0]. Anything reasoning about how many
			// arguments a caller actually passes must correct for it via ReceiverCorrection, not with
			// NamedArgBinder.ArgBase, which answers a different question (the args-slot offset of parameter 0).
			receiverInCounts = hasHiddenThis || (IsStatic && parameters.Length > 0 && IsExplicitThis(parameters[0]));

			var isKeysharpFunc = typeof(KeysharpFunc).IsAssignableFrom(mi.DeclaringType);

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

			// Module properties enter through the variable store rather than a method's core.
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
			IsStatic = fi.IsStatic;
			parameters = System.Array.Empty<ParameterInfo>();
			ParamLength = 0;
			MinParams = MaxParams = 0;
		}

		private Func<object, object[], object> CreateFieldGetter()
		{
			var field = fi;
			var isStatic = field.IsStatic;
			var convertOut = field.IsDefined(typeof(InlineCSharpAttribute), false) && ArgCoercer.CanLeakClrValue(field.FieldType);
			var widen = ArgCoercer.IsNarrow(ArgCoercer.KindOf(field.FieldType));
			var callsUntilCompilation = DelegateFactory.CompilationThreshold;
			// Instance access needs the compiled receiver cast; reflection reports different errors for invalid receivers.
			Func<object, object> getter = null;
			getter = !isStatic || field.FieldType.IsPointer || field.FieldType.IsFunctionPointer
				? CompileFieldGetter(field, convertOut) : GetField;

			object GetField(object target)
			{
				if (Interlocked.Decrement(ref callsUntilCompilation) == 0)
				{
					var hot = CompileFieldGetter(field, convertOut);
					Volatile.Write(ref getter, hot);
					return hot(target);
				}

				var value = field.GetValue(target);
				return convertOut ? ManagedInvoke.ConvertOut(value) : widen ? ArgCoercer.NormalizeScalar(value) : value;
			}

			return (inst, args) =>
			{
				var target = isStatic ? null : inst ?? (args is { Length: > 0 } ? args[0] : null);
				return Volatile.Read(ref getter)(target);
			};
		}

		private static Func<object, object> CompileFieldGetter(FieldInfo field, bool convertOut)
		{
			var instance = Expression.Parameter(typeof(object), "inst");
			var access = Expression.Field(field.IsStatic ? null : Expression.Convert(instance, field.DeclaringType), field);
			var result = convertOut ? ArgCoercer.ConvertOut(access) : ArgCoercer.NormalizeReturn(access, field.FieldType);
			return Expression.Lambda<Func<object, object>>(result, instance).Compile();
		}

		private Action<object, object> CreateFieldSetter()
		{
			var field = fi;
			var boundary = field.IsDefined(typeof(InlineCSharpAttribute), false);
			var convert = ArgCoercer.NeedsCoercion(field.FieldType, boundary);
			// Reflection differs for receiver casts and raw value casts, including null unboxing and boxed-struct stores.
			if (!field.IsStatic || field.FieldType.IsValueType && ArgCoercer.KindOf(field.FieldType) == ArgCoercer.Kind.None
				|| !convert && field.FieldType != typeof(object))
			{
				var direct = CompileFieldSetter(field);
				return Interlocked.CompareExchange(ref setProp, direct, null) ?? direct;
			}

			var callsUntilCompilation = DelegateFactory.CompilationThreshold;
			Action<object, object> compiled = null;

			void SetField(object inst, object value)
			{
				// A caller can retain this setter after the holder publishes its compiled replacement.
				var hot = Volatile.Read(ref compiled);
				if (hot != null)
				{
					hot(inst, value);
					return;
				}

				if (Interlocked.Decrement(ref callsUntilCompilation) == 0)
				{
					hot = CompileFieldSetter(field);
					Volatile.Write(ref compiled, hot);
					Volatile.Write(ref setProp, hot);
					hot(inst, value);
					return;
				}

				if (!convert || ArgCoercer.TryCoerceValue(value, field.FieldType, boundary, out value))
					field.SetValue(null, value);
			}

			Action<object, object> cold = SetField;
			return Interlocked.CompareExchange(ref setProp, cold, null) ?? cold;
		}

		private static Action<object, object> CompileFieldSetter(FieldInfo field)
		{
			var instance = Expression.Parameter(typeof(object), "inst");
			var value = Expression.Parameter(typeof(object), "value");
			var boundary = field.IsDefined(typeof(InlineCSharpAttribute), false);
			var steps = ArgCoercer.NeedsCoercion(field.FieldType, boundary) ? new ArgCoercer.ArgumentSteps(null) : null;
			var converted = steps?.Coerce(value, field.FieldType, boundary) ?? Expression.Convert(value, field.FieldType);
			var access = Expression.Field(field.IsStatic ? null : Expression.Convert(instance, field.DeclaringType), field);
			var assign = Expression.Assign(access, converted);
			return Expression.Lambda<Action<object, object>>(steps?.Wrap(assign) ?? assign, instance, value).Compile();
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
	}
}
