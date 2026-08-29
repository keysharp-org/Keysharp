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

		/// <summary>
		/// Internal helper to get a function object which supports different ways of identifying such.
		/// This is the boundary every built-in that takes a callback goes through, so it accepts only things
		/// which already are a function: an existing function object or a delegate.
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
