//#define CONCURRENT
using System.Collections.Generic;
using System.Linq.Expressions;
using Antlr4.Runtime.Misc;
using Label = System.Reflection.Emit.Label;

namespace Keysharp.Core.Common.Invoke
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
		internal readonly MethodInfo mi;
		internal readonly ParameterInfo[] parameters;
		internal readonly PropertyInfo pi;
		internal readonly Action<object, object> SetProp;
		protected readonly ConcurrentStackArrayPool<object> paramsPool;
		internal readonly bool anyOptional;
		internal readonly bool isGuiType;
		internal readonly bool isSetter;
		internal readonly bool isItemSetter;
		internal readonly int variadicParamIndex = -1;
		internal readonly int[] requiredIdx;

#if CONCURRENT
        internal static ConcurrentDictionary<MethodInfo, MethodPropertyHolder> methodCache = new();
		internal static ConcurrentDictionary<PropertyInfo, MethodPropertyHolder> propertyCache = new();
#else
		internal static Dictionary<MethodInfo, MethodPropertyHolder> methodCache = new();
		internal static Dictionary<PropertyInfo, MethodPropertyHolder> propertyCache = new();
#endif

		internal bool IsBind { get; private set; }
		internal bool IsStatic { get; private set; }
		internal bool IsCompilerGenerated { get; private set; }
		internal bool IsStaticFunc { get; private set; }
		internal bool IsStaticProp { get; private set; }
		internal bool IsVariadic => variadicParamIndex != -1;
		internal int ParamLength { get; }
		internal int MinParams = 0;
		internal int MaxParams = 0;

        internal const int DotNetMaxParams = 8192; // https://www.tabsoverspaces.com/233892-whats-the-maximum-number-of-arguments-for-method-in-csharp-and-in-net

		private const string setterPrefix = "set_";
        private const string classSetterPrefix = Keywords.ClassStaticPrefix + setterPrefix;

		string _name = null;
		internal string Name
		{
			get
			{
				if (_name != null)
					return _name;

				if (mi == null)
					return _name = "";

				var nameAttrs = mi.GetCustomAttributes(typeof(UserDeclaredNameAttribute));
				if (nameAttrs.Any())
				{
					return _name = ((UserDeclaredNameAttribute)nameAttrs.First()).Name;
				}

				string funcName = mi.Name;
				var prefixes = new[] { "static", "get_", "set_" };
				foreach (var p in prefixes)
				{
					if (funcName.StartsWith(p, StringComparison.Ordinal))
						funcName = funcName.Substring(p.Length);
				}

				if (mi.DeclaringType.Namespace != TheScript.ProgramType.Namespace || mi.DeclaringType.Name == Keywords.MainClassName)
					return _name = funcName;

				string declaringType = mi.DeclaringType.FullName;

				var idx = declaringType.IndexOf(Keywords.MainClassName + "+");
				string nestedPath = idx < 0
					? declaringType       // no “Program.” found, just return whole
					: declaringType.Substring(idx + Keywords.MainClassName.Length + 1);

				return _name = $"{nestedPath.Replace('+', '.')}.{funcName}";
			}
		}

		public static MethodPropertyHolder GetOrAdd(MethodInfo mi)
        {
#if CONCURRENT
            return methodCache.GetOrAdd(mi, key => new MethodPropertyHolder(mi));
#else
			if (methodCache.TryGetValue(mi, out var mph))
                return mph;
            return methodCache[mi] = new MethodPropertyHolder(mi);
#endif
        }

		internal static MethodPropertyHolder GetOrAdd(PropertyInfo pi)
		{
#if CONCURRENT
            return propertyCache.GetOrAdd(pi, key => new MethodPropertyHolder(pi));
#else
			if (propertyCache.TryGetValue(pi, out var mph))
				return mph;
			return propertyCache[pi] = new MethodPropertyHolder(pi);
#endif
        }

        public MethodPropertyHolder() { }

		public MethodPropertyHolder(MethodInfo m)
        {
            mi = m;

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

			anyOptional = variadicParamIndex != -1 || MinParams != MaxParams;

			var isFuncObj = typeof(IFuncObj).IsAssignableFrom(mi.DeclaringType);

			if (isFuncObj && mi.Name == "Bind")
				IsBind = true;

			if (isFuncObj && mi.Name == "Call")
			{
				_callFunc = (inst, obj) => ((IFuncObj)inst).Call(obj);
			}
		}

		public MethodPropertyHolder(PropertyInfo p)
		{
            pi = p;
			isGuiType = Gui.IsGuiType(pi.DeclaringType);
			parameters = pi.GetIndexParameters();
			ParamLength = parameters.Length;
			MinParams = MaxParams = ParamLength;

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

                        if (ret is int i)
                            ret = (long)i;//Try to keep everything as long.

							return ret;
						};
						SetProp = (inst, arg) =>
						{
							var ctrl = inst.GetControl();//If it's a gui control, then invoke on the gui thread.
							ctrl.CheckedInvoke(() => pi.SetValue(null, arg), true);//This can be null if called before a Gui object is fully initialized.
						};
					}
					else
					{
						if (pi.PropertyType == typeof(int))
						{
							_callFunc = (inst, arg) =>
							{
								var ret = pi.GetValue(null);

							if (ret is int i)
								ret = (long)i;//Try to keep everything as long.

							return ret;
						};
					}
					else
						_callFunc = (inst, obj) => pi.GetValue(null);

					SetProp = (inst, obj) => pi.SetValue(null, obj);
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

						if (ret is int i)
							ret = (long)i;//Try to keep everything as long.

							return ret;
						};
						SetProp = (inst, obj) =>
						{
							var ctrl = inst.GetControl();//If it's a gui control, then invoke on the gui thread.
							ctrl.CheckedInvoke(() => pi.SetValue(inst, obj), true);//This can be null if called before a Gui object is fully initialized.
						};
					}
					else
					{
						if (pi.PropertyType == typeof(int))
						{
							_callFunc = (inst, obj) =>
							{
								var ret = pi.GetValue(inst);

							if (ret is int i)
								ret = (long)i;//Try to keep everything as long.

							return ret;
						};
					}
					else
						_callFunc = (inst, obj) => pi.GetValue(inst);

					SetProp = pi.SetValue;
				}
			}
		}

		// Allow creating a "fake" MPH for ObjBindMethod
		public MethodPropertyHolder(string name)
		{
			_name = name;
			variadicParamIndex = 1;
		}

		internal static void ClearCache()
		{
			methodCache.Clear();
            propertyCache.Clear();
		}
	}
    public class ArgumentError : Error
    {
        public ArgumentError()
            : base(new TargetParameterCountException().Message)
        {
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

		public static Func<object, object[], object> CreateDelegate(MethodPropertyHolder mph)
		{
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

			// Compile the small "core" once.
			var core = CompileCore(mi, ps, isSoft, defaults);

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
			object[] defaults)
		{
			var pTarget = Expression.Parameter(typeof(object), "target");
			var pArgs = Expression.Parameter(typeof(object[]), "args");
			var pStart = Expression.Parameter(typeof(int), "start");
			var argsLen = Expression.ArrayLength(pArgs);

			var a = new Expression[ps.Length];

			// new ArgumentError() for non-optional missing/null
			var throwArgErr = Expression.Throw(Expression.New(typeof(ArgumentError)), typeof(object));

			for (int i = 0; i < ps.Length; i++)
			{
				var idx = Expression.Add(pStart, Expression.Constant(i));
				var inRange = Expression.LessThan(idx, argsLen);
				var elem = Expression.ArrayIndex(pArgs, idx);
				var valOrNull = Expression.Condition(inRange, elem, Expression.Constant(null, typeof(object)));

				Expression chosen = isSoft[i]
					? Expression.Condition(
						Expression.Equal(valOrNull, Expression.Constant(null, typeof(object))),
						Expression.Constant(defaults[i], typeof(object)),
						valOrNull)
					: Expression.Condition(
						Expression.Equal(valOrNull, Expression.Constant(null, typeof(object))),
						throwArgErr,
						valOrNull);

				a[i] = Expression.Convert(chosen, ps[i].ParameterType);
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
					: Expression.Convert(call, typeof(object));

			return Expression.Lambda<Func<object, object[], int, object>>(body, pTarget, pArgs, pStart)
							 .Compile();
		}

		private static object MaterializeDefault(ParameterInfo p)
		{
			var def = p.DefaultValue;
			return (def is DBNull || def == System.Reflection.Missing.Value) ? null : def;
		}
	}

#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class FastCtor
	{
#if CONCURRENT
		private static readonly ConcurrentDictionary<System.Type, Func<object[], object>> Cache = new();
#else
		private static readonly Dictionary<System.Type, Func<object[], object>> Cache = new();
#endif

		// Semantics: always call a public ctor like:  new T(object[] args)
		public static object Call(Type type, params object[] args)
		{
			args ??= System.Array.Empty<object>();

#if CONCURRENT
			var activator = Cache.GetOrAdd(type, BuildFactory);
#else
			if (!Cache.TryGetValue(type, out var activator))
				Cache[type] = activator = BuildFactory(type);
#endif
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