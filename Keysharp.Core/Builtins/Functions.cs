namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for function object and function reflection-related functions.
	/// </summary>
	public static class Functions
	{
		/// <summary>
		/// Returns a function as a function object.
		/// If function was a delegate, then a new <see cref="KeysharpFunc"/> is returned using delegate.Method.
		/// If function was already an <see cref="KeysharpFunc"/>, then it's just casted and returned.
		/// </summary>
		/// <param name="function">The delegate or KeysharpFunc to return an object for.</param>
		/// <param name="obj">The instance to bind a delegate to. Default: null for an unbound function.</param>
		/// <returns>An <see cref="KeysharpFunc"/> which can later be called like a function.</returns>
		[PublicHiddenFromUser]
		public static KeysharpFunc Func(object function, object obj = null) => GetKeysharpFunc(function, obj, obj != null);
		[PublicHiddenFromUser]
		public static KeysharpFunc Func(Delegate del, object obj = null) => GetKeysharpFunc(del, obj, obj != null);
		[PublicHiddenFromUser]
		public static KeysharpFunc Closure(Delegate del, object obj = null) => new Closure(del, obj);

		/// <summary>
		/// Resolves a function by name against a module, the global function table and the built-ins.
		/// Name resolution is internal, for the places where a name is all there is: <c>%"Name"%</c> derefs,
		/// <c>#Import</c> member binding and RegEx callouts. A callback reaches a built-in as a function object,
		/// which <see cref="GetKeysharpFunc"/> handles.
		/// </summary>
		/// <param name="name">The name of the function to find.</param>
		/// <param name="moduleType">The module to search first, or null to search the current one and to let a
		/// closure of the executing deref function win over it.</param>
		/// <param name="paramCount">The number of parameters the function has. Default: use the first one found.</param>
		/// <param name="throwIfBad">Whether to throw when the name resolves to nothing. Default: false.</param>
		/// <returns>An <see cref="KeysharpFunc"/>, or null when the name names no function.</returns>
		/// <exception cref="MethodError">A <see cref="MethodError"/> exception is thrown if throwIfBad was true and no function was found.</exception>
		[PublicHiddenFromUser]
		public static KeysharpFunc GetKeysharpFuncByName(object name, Type moduleType = null, object paramCount = null, bool throwIfBad = false)
		{
			var s = name.As();

			if (s.Length == 0)
				return null;//Empty string will just return null, which is a valid value in some cases.

			var script = Script.TheScript;

			if (moduleType == null)
			{
				// A currently-executing deref function exposes its locals/closures by name. Resolve a closure (a
				// live KeysharpFunc instance, possibly capturing locals) ahead of the module/global tables — these are
				// per-invocation and must never be cached.
				var scope = Script.executingUserFunc;

				if (scope != null && scope.TryGetVar(s, out var scopeVal) && scopeVal is KeysharpFunc scopeFo && scopeFo.IsValid)
					return scopeFo;

				moduleType = script.CurrentModuleType;
			}

			var cachedKeysharpFunc = script.FunctionData.cachedKeysharpFunc;
			KeysharpFunc del;

			if (moduleType != null)
			{
				var key = new ModuleFuncKey(s, moduleType, paramCount.Ai(-1));
				del = script.FunctionData.cachedModuleKeysharpFunc.GetOrAdd(
					key,
					(k) => new KeysharpFunc(s, moduleType, paramCount)
				);

				if (!del.IsValid)
				{
					// Fall back to global/built-in functions when the module doesn't define the method.
					del = cachedKeysharpFunc.GetOrAdd(s, (key) => new KeysharpFunc(s, (object)null, paramCount));
				}
			}
			else
				del = cachedKeysharpFunc.GetOrAdd(s, (key) => new KeysharpFunc(s, (object)null, paramCount));

			if (del.IsValid)
				return del;

			if (throwIfBad)
				_ = Errors.MethodErrorOccurred($"Unable to retrieve method {s} when creating a function object.");

			return null;
		}

		/// <summary>Converts a callback value to the object a registration stores, or raises for a non-object value.</summary>
		internal static object ToCallback(object value)
		{
			switch (value)
			{
				case KeysharpFunc or Delegate:
					return GetKeysharpFunc(value, null, true);

				case Any:
					return value;

				case string { Length: > 0 }:
					return GetKeysharpFunc(value);            // raises, naming the %"Name"% remedy

				default:
					var type = Types.Type(value);
					Error err = new TypeError($"Expected an object but got {(type.Length > 0 && "AEIOUaeiou".Contains(type[0]) ? "an" : "a")} {type}.",
											  null, Errors.Describe(value));
					return Errors.ErrorOccurred(err) ? throw err : null;
			}
		}

		/// <summary>Validates a callback against AHK's functor rules. -1 checks only whether it is callable.</summary>
		internal static bool ValidateFunctor(object callback, int argCount)
		{
			if (callback is KeysharpFunc fo)
			{
				if (argCount < 0 || fo.MinParams <= argCount && (fo.IsVariadic || fo.MaxParams >= argCount))
					return true;

				// QualifiedName rather than Name, which is empty for a bound function and would name nothing.
				return InvalidCallback(fo.MinParams > argCount ? $"requires {fo.MinParams}" : $"accepts at most {fo.MaxParams}",
									   argCount, fo.Mph.QualifiedName);
			}

			// A COM or CLR object is called through its own dispatch, which cannot be asked beforehand.
			if (callback is not KeysharpObject obj)
				return callback != null;

			bool hasMin = false, hasMax = false;

			if (argCount >= 0)
			{
				if (!TryCountProperty(obj, "MinParams", out var min, out hasMin))
					return false;

				if (hasMin && min > argCount)
					return InvalidCallback($"requires {min}", argCount, Types.Type(obj));

				// As AHK, MaxParams is asked only when it could matter, and IsVariadic only when MaxParams falls short.
				if (argCount > 0 && !(hasMin && min == argCount))
				{
					if (!TryCountProperty(obj, "MaxParams", out var max, out hasMax))
						return false;

					if (hasMax && argCount > max)
					{
						if (!TryCountProperty(obj, "IsVariadic", out var variadic, out _))
							return false;

						if (variadic == 0)
							return InvalidCallback($"accepts at most {max}", argCount, Types.Type(obj));
					}
				}
			}

			// An object that states a count is taken to be callable, as AHK takes it.
			if (hasMin || hasMax || IsInvocable(obj))
				return true;

			_ = Errors.MethodErrorOccurred($"This value of type \"{Types.Type(obj)}\" has no method named \"Call\".");
			return false;
		}

		/// <summary>Gets the callback's declared MinParams, or raises when a callable object does not provide it.</summary>
		internal static bool TryMinParams(object callback, out long minParams)
		{
			minParams = 0;

			if (callback is KeysharpFunc fo)
			{
				minParams = fo.MinParams;
				return true;
			}

			if (callback is KeysharpObject obj)
			{
				if (!TryCountProperty(obj, "MinParams", out minParams, out var present))
					return false;

				if (present)
					return true;
			}

			_ = Errors.PropertyErrorOccurred($"This value of type \"{Types.Type(callback)}\" has no property named \"MinParams\".");
			return false;
		}

		/// <summary>Converts and validates a callback.</summary>
		internal static object CheckedCallback(object value, int argCount)
			=> ToCallback(value) is { } callback && ValidateFunctor(callback, argCount) ? callback : null;

		/// <summary>Validates an event registration's AddRemove value.</summary>
		internal static bool TryAddRemove(object value, out long addRemove)
		{
			addRemove = value.Al(1L);

			if (addRemove is >= -1 and <= 1)
				return true;

			_ = Errors.ValueErrorOccurred($"AddRemove must be 1, -1 or 0, but was {addRemove}.");
			return false;
		}

		/// <summary>Compares function wrappers by target and other callbacks by identity, as AHK does.</summary>
		internal static bool SameCallback(object a, object b)
			=> ReferenceEquals(a, b) || IsWrapper(a) && IsWrapper(b) && a.Equals(b);

		/// <summary>The hash that agrees with <see cref="SameCallback"/>.</summary>
		internal static int CallbackHash(object callback)
			=> IsWrapper(callback) ? callback.GetHashCode() : callback != null ? RuntimeHelpers.GetHashCode(callback) : 0;

		private static bool IsWrapper(object callback) => callback is KeysharpFunc and not Keysharp.Builtins.Closure;

		/// <summary>A callback's name for a listing: a function's own, otherwise its type, as AHK names it.</summary>
		internal static string CallbackName(object callback) => callback is KeysharpFunc f ? f.Name : Types.Type(callback);

		private static bool InvalidCallback(string accepts, int argCount, string name)
		{
			_ = Errors.ValueErrorOccurred($"Invalid callback function: it {accepts} parameter(s), but is called with {argCount}.", name);
			return false;
		}

		// A missing count is allowed; a present one must be an integer. Run its getter, as AHK does.
		private static bool TryCountProperty(KeysharpObject obj, string name, out long value, out bool present)
		{
			value = 0;
			present = HasProp(obj, name) != 0L;

			if (!present)
				return true;

			switch (Script.GetPropertyValueOrNull(obj, name))
			{
				case long l: value = l; return true;
				case int i: value = i; return true;
				case bool b: value = b ? 1L : 0L; return true;
			}

			_ = Errors.TypeErrorOccurred($"Type mismatch: {name} must be an integer.");
			return false;
		}

		// Include every route Script.InvokeOrNull can use for a nameless call.
		private static bool IsInvocable(KeysharpObject obj)
			=> HasMember(obj, "Call") || HasMember(obj, "__Call") || obj is IMetaObject;

		// Match AHK Object::GetMethod without running a getter; a getter hides inherited values but not methods.
		private static bool HasMember(Any obj, string name)
		{
			var getterSeen = false;

			for (var o = obj; o != null; o = o.Base)
			{
				if (o.op == null || !o.op.TryGetValue(name, out var desc))
					continue;

				if (desc.Call != null)
					return true;

				if (desc.Get != null)
					getterSeen = true;
				else if (desc.Value != null)
					return !getterSeen && desc.Value is Any;
			}

			return false;
		}

		/// <summary>
		/// Internal helper to get a function object which supports different ways of identifying such: a function
		/// object as it is, or a delegate as the function object that stands for it. A callback site takes any other
		/// callable object as well, through <see cref="ToCallback"/>.
		/// </summary>
		/// <param name="h">The object to examine. This can be an existing function object or a delegate.</param>
		/// <param name="inst">The instance to bind a delegate to. Default: null for an unbound function.</param>
		/// <param name="throwIfBad">Whether throw an exception if the method could not be found. Default: false.</param>
		/// <returns>An <see cref="KeysharpFunc"/> which may be a newly recreated one, or h if it was already one.</returns>
		/// <exception cref="MethodError">A <see cref="MethodError"/> exception is thrown if a function object couldn't be created</exception>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if h is not a function.</exception>
		[PublicHiddenFromUser]
		public static KeysharpFunc GetKeysharpFunc(object h, object inst = null, bool throwIfBad = false)
		{
			KeysharpFunc del = null;

			if (h is string s)
			{
				if (s.Length == 0)
					return null;//Empty string will just return null, which is a valid value in some cases.

				// Its own branch rather than the type check below, which reports only when throwIfBad is set: a name
				// is the one wrong value worth naming a remedy for, and worth reporting to every caller.
				// %"Name"% resolves before the call, so what arrives here is always the function object itself.
				_ = Errors.TypeErrorOccurred($"Cannot use the string \"{s}\" as a function. Pass the function itself, or %\"{s}\"% to resolve a name at run time.");
				return default;
			}
			else if (h is KeysharpFunc fo)
			{
				del = fo;

				if (!del.IsValid)
				{
					del = null;

					if (throwIfBad)
					{
						_ = Errors.MethodErrorOccurred($"Existing function object was invalid.");
						return default;
					}
				}
			}
			else if (h is Delegate d)
			{
				// An unbound delegate answers the same for the life of the script, so it is worth caching by its
				// own identity. One bound to an instance is per-instance and cannot be shared.
				del = inst == null
					  ? Script.TheScript.FunctionData.cachedKeysharpFunc.GetOrAdd(d, (key) => new KeysharpFunc((Delegate)key, null))
					  : new KeysharpFunc(d, inst);

				if (!del.IsValid)
				{
					del = null;

					if (throwIfBad)
					{
						_ = Errors.MethodErrorOccurred($"Unable to retrieve method info for {d.Method.Name} when creating a function object from delegate.");
						return default;
					}
				}
			}
			else if (throwIfBad)
			{
				_ = Errors.TypeErrorOccurred(h, typeof(KeysharpFunc));
				return null;
			}

			return del;
		}

		/// <summary>
		/// Gets a method of an object.
		/// </summary>
		/// <param name="value">The object to find the method on. Can't be a ComObject.</param>
		/// <param name="name">If omitted, validation is performed on value itself and value is returned if successful.<br/>
		/// Otherwise, specify the name of the method to retrieve.
		/// </param>
		/// <param name="paramCount">The number of parameters the method has. Default: use the first method found.</param>
		/// <returns>An <see cref="KeysharpFunc"/> which can later be called like a method.</returns>
		/// <exception cref="MethodError">A <see cref="MethodError"/> exception is thrown if the method cannot be found.</exception>
		public static object GetMethod(object value, object name = null, object paramCount = null)
		{
			var v = value;
			var n = name.As();
			var count = paramCount.Ai(-1);
			var mph = Reflections.FindAndCacheMethod(v.GetType(), n.Length > 0 ? n : "Call", count);

			if (mph != null && mph.mi != null)
				return new KeysharpFunc(mph.mi, null);

			return Script.CompatReturnsUnsetForMissing ? null
				: Errors.MethodErrorOccurred($"Unable to retrieve method {n} from object of type {v.GetType()} with parameter count {count}.");
		}

		/// <summary>
		/// Returns whether the specified value has a method by the specified name.
		/// </summary>
		/// <param name="value">The object to find the method on. Can't be a ComObject.</param>
		/// <param name="name">If omitted, Value itself is checked whether it is callable.<br/>
		/// Otherwise, specify the method name to check for.
		/// </param>
		/// <param name="paramCount">The number of parameters the method has. Default: use the first function found.</param>
		/// <returns>1 if the method was found on the object, else 0.</returns>
		public static long HasMethod(object value, object name = null, object paramCount = null)
		{
			var n = name.As();
			if (n == "") n = "Call";
			var count = paramCount.Ai(-1);

			var mitup = GetMethodOrProperty(value, n, count, checkBase: true, throwIfMissing: false, invokeMeta: false);
			if (mitup.Item2 == null) return 0L;
			switch (mitup.Item2)
			{
				case KeysharpFunc fn:
					if (count != -1)
					{
						bool hasThis = value is KeysharpFunc ? false : value is KeysharpObject ? true : fn.IsMethod;
						if (count < (fn.MinParams - (hasThis ? 1 : 0))) return 0L;
						if (count > (fn.MaxParams - (hasThis ? 1 : 0)) && !fn.IsVariadic) return 0L;
					}
					return 1L;
				case KeysharpObject callable:
				case MethodPropertyHolder mph:
					return 1L;
			}
			return 0L;
		}

		/// <summary>
		/// Returns whether the specified value has a property by the specified name.
		/// </summary>
		/// <param name="value">The object to find the property on. Can't be a ComObject.</param>
		/// <param name="name">The property name to check for.</param>
		/// <param name="paramCount">The number of parameters the property takes.<br/>
		/// This is used for indexers which can take 1 or more parameters. Default: 0.
		/// </param>
		/// <returns>1 if the property was found on the object, else 0.</returns>
		public static long HasProp(object value, object name, object paramCount = null, bool checkBase = true)
		{
			var val = value;
			var n = name.As();
			var count = paramCount.Ai(-1);
			Any nextBase = null;

			if (value is Any kso)
			{
				if (kso.op != null && kso.op.ContainsKey(n))
					return 1L;

				if (checkBase)
				{
                    var Base = kso;
                    while ((nextBase = Base.Base) != null && nextBase != null && nextBase is KeysharpObject)
                    {
                        Base = (KeysharpObject)nextBase;
						if (Base != null && Base.op.ContainsKey(n))
							return 1L;
                    }
                }

				return 0L;
			}

			var mph = Reflections.FindAndCacheProperty(val.GetType(), n, count);
			return mph != null && mph.pi != null ? 1L : 0L;
		}

		/// <summary>
		/// Creates a <see cref="BoundFunc"/> object which calls a method of a given object.
		/// </summary>
		/// <param name="obj">The object to find the method on.</param>
		/// <param name="method">The method's name. If omitted, the bound function calls obj itself.</param>
		/// <param name="args">The arguments to bind to the function.</param>
		/// <returns>An new <see cref="BoundFunc"/> object with the specified arguments bound to it.</returns>
		public static object ObjBindMethod(object obj, object method = null, params object[] args)
		{
			var o = obj;
			var n = method.As("Call");

			if (obj is Any)
				return new BoundFunc(new MethodPropertyHolder(n), args, o);
			else if (Reflections.FindAndCacheMethod(o.GetType(), n, -1) is MethodPropertyHolder mph && mph.mi != null)
				return new BoundFunc(mph, [obj, .. args], o);

			return Errors.ErrorOccurred($"Unable to retrieve method {n} for object.");
		}
	}

	internal readonly struct ModuleFuncKey : IEquatable<ModuleFuncKey>
	{
		internal readonly string Name;
		internal readonly Type ModuleType;
		internal readonly int ParamCount;

		internal ModuleFuncKey(string name, Type moduleType, int paramCount)
		{
			Name = name;
			ModuleType = moduleType;
			ParamCount = paramCount;
		}

		public bool Equals(ModuleFuncKey other)
			=> ParamCount == other.ParamCount
				&& ReferenceEquals(ModuleType, other.ModuleType)
				&& string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

		public override bool Equals(object obj) => obj is ModuleFuncKey other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				int h = StringComparer.OrdinalIgnoreCase.GetHashCode(Name ?? string.Empty);
				h = (h * 397) ^ (ModuleType?.GetHashCode() ?? 0);
				h = (h * 397) ^ ParamCount;
				return h;
			}
		}
	}

	internal sealed class ModuleFuncKeyComparer : IEqualityComparer<ModuleFuncKey>
	{
		public bool Equals(ModuleFuncKey x, ModuleFuncKey y) => x.Equals(y);
		public int GetHashCode(ModuleFuncKey obj) => obj.GetHashCode();
	}

	internal class FunctionData
	{
		internal ConcurrentLfu<object, KeysharpFunc> cachedKeysharpFunc = new (Environment.ProcessorCount, 2000, new ThreadPoolScheduler(), new CaseEqualityComp(eCaseSense.Off));
		internal ConcurrentLfu<ModuleFuncKey, KeysharpFunc> cachedModuleKeysharpFunc = new (Environment.ProcessorCount, 2000, new ThreadPoolScheduler(), new ModuleFuncKeyComparer());
	}
}
