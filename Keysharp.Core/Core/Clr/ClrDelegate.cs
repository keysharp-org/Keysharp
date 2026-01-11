using System.Linq.Expressions;

namespace Keysharp.Core
{
	internal static class ClrDelegateMarshaler
	{
		private static readonly ConcurrentDictionary<Type, Func<object, Delegate>> _cache = new();

		public static Delegate FromKeysharpFunc(Type delegateType, object callable)
			=> _cache.GetOrAdd(delegateType, BuildFactoryFor)(callable);

		private static Func<object, Delegate> BuildFactoryFor(Type delType)
		{
			var invoke = delType.GetMethod("Invoke")!;
			var pars = invoke.GetParameters();
			var retT = invoke.ReturnType;

			if (pars.Any(p => p.ParameterType.IsByRef))
				throw new NotSupportedException($"Delegates with ref/out parameters are not supported: {delType.FullName}");

			// -------------------- FAST PATH (DynamicMethod) --------------------
			// retT Shim(object kf, T0 a0, T1 a1, ...)
			var argTypes = new Type[1 + pars.Length];
			argTypes[0] = typeof(object);
			for (int i = 0; i < pars.Length; i++)
				argTypes[i + 1] = pars[i].ParameterType;

			var dm = new DynamicMethod(
				name: $"Clr_DelegateShim_{delType.Name}",
				returnType: retT,
				parameterTypes: argTypes,
				m: typeof(ClrDelegateMarshaler).Module,
				skipVisibility: true);

			var il = dm.GetILGenerator();

			// locals
			var ksArgs = il.DeclareLocal(typeof(object[]));
			LocalBuilder retObj = null;
			if (retT != typeof(void))
				retObj = il.DeclareLocal(typeof(object));

			// ksArgs = new object[n]
			EmitLdcI4(il, pars.Length);
			il.Emit(OpCodes.Newarr, typeof(object));
			il.Emit(OpCodes.Stloc, ksArgs);

			// fill ksArgs[i] = ConvertOut(ai)
			var convertOutMI = typeof(ManagedInvoke).GetMethod(nameof(ManagedInvoke.ConvertOut), BindingFlags.NonPublic | BindingFlags.Static)!;
			for (int i = 0; i < pars.Length; i++)
			{
				il.Emit(OpCodes.Ldloc, ksArgs);
				EmitLdcI4(il, i);
				il.Emit(OpCodes.Ldarg, i + 1);                 // arg0 is kf
				if (argTypes[i + 1].IsValueType)
					il.Emit(OpCodes.Box, argTypes[i + 1]);
				il.Emit(OpCodes.Call, convertOutMI);
				il.Emit(OpCodes.Stelem_Ref);
			}

			// ret = InvokeKeysharpFunc(kf, ksArgs)
			var invokerMI = typeof(ClrDelegateMarshaler).GetMethod(nameof(InvokeKeysharpFunc), BindingFlags.NonPublic | BindingFlags.Static)!;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldloc, ksArgs);
			il.Emit(OpCodes.Call, invokerMI);

			if (retT == typeof(void))
			{
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ret);
			}
			else
			{
				var coerceMI = typeof(ManagedInvoke).GetMethod(nameof(ManagedInvoke.CoerceToType), BindingFlags.NonPublic | BindingFlags.Static)!;
				il.Emit(OpCodes.Stloc, retObj);
				il.Emit(OpCodes.Ldloc, retObj);
				il.Emit(OpCodes.Ldtoken, retT);
				il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
				il.Emit(OpCodes.Call, coerceMI);
				if (retT.IsValueType)
					il.Emit(OpCodes.Unbox_Any, retT);
				else
					il.Emit(OpCodes.Castclass, retT);
				il.Emit(OpCodes.Ret);
			}

			// factory with fallback
			bool useSlowPath = false;

			Delegate FastFactory(object kf)
			{
				try
				{
					if (!useSlowPath)
						return dm.CreateDelegate(delType, kf); // closed over first parameter (kf)
				}
				catch (BadImageFormatException)
				{
					// Signature shape the runtime won't close this way (e.g., some LINQ cases).
					useSlowPath = true;
				}
				catch (ArgumentException)
				{
					// Defensive: treat any signature mismatch the same.
					useSlowPath = true;
				}

				// -------------------- SLOW PATH (per-instance Expression) --------------------
				// Build once per instance capturing kf as a Constant.
				var invokeMI = delType.GetMethod("Invoke")!;
				var parms = invokeMI.GetParameters().Select((p, i) => Expression.Parameter(p.ParameterType, $"a{i}")).ToArray();

				var arrVar = Expression.Variable(typeof(object[]), "ksArgs");
				var retVar = Expression.Variable(typeof(object), "ret");

				var newArr = Expression.NewArrayBounds(typeof(object), Expression.Constant(parms.Length));
				var convert = typeof(ManagedInvoke).GetMethod("ConvertOut", BindingFlags.NonPublic | BindingFlags.Static)!;
				var invoker = typeof(ClrDelegateMarshaler).GetMethod(nameof(InvokeKeysharpFunc), BindingFlags.NonPublic | BindingFlags.Static)!;
				var coerce = typeof(ManagedInvoke).GetMethod("CoerceToType", BindingFlags.NonPublic | BindingFlags.Static)!;

				var stores = parms.Select((p, i) =>
					Expression.Assign(
						Expression.ArrayAccess(arrVar, Expression.Constant(i)),
						Expression.Call(convert, Expression.Convert(p, typeof(object)))
					));

				var body =
					retT == typeof(void)
					? (Expression)Expression.Block(
						new[] { arrVar, retVar },
						Expression.Assign(arrVar, newArr),
						stores.Aggregate((Expression)Expression.Empty(), (acc, s) => Expression.Block(acc, s)),
						Expression.Assign(retVar, Expression.Call(invoker, Expression.Constant(kf, typeof(object)), arrVar))
					  )
					: Expression.Block(
						new[] { arrVar, retVar },
						Expression.Assign(arrVar, newArr),
						stores.Aggregate((Expression)Expression.Empty(), (acc, s) => Expression.Block(acc, s)),
						Expression.Assign(retVar, Expression.Call(invoker, Expression.Constant(kf, typeof(object)), arrVar)),
						Expression.Convert(
							Expression.Call(coerce, retVar, Expression.Constant(retT, typeof(Type))),
							retT)
					  );

				var lambda = Expression.Lambda(delType, body, parms);
				return lambda.Compile();
			}

			return FastFactory;
		}

		private static void EmitLdcI4(ILGenerator il, int value)
		{
			switch (value)
			{
				case -1: il.Emit(OpCodes.Ldc_I4_M1); return;
				case 0: il.Emit(OpCodes.Ldc_I4_0); return;
				case 1: il.Emit(OpCodes.Ldc_I4_1); return;
				case 2: il.Emit(OpCodes.Ldc_I4_2); return;
				case 3: il.Emit(OpCodes.Ldc_I4_3); return;
				case 4: il.Emit(OpCodes.Ldc_I4_4); return;
				case 5: il.Emit(OpCodes.Ldc_I4_5); return;
				case 6: il.Emit(OpCodes.Ldc_I4_6); return;
				case 7: il.Emit(OpCodes.Ldc_I4_7); return;
				case 8: il.Emit(OpCodes.Ldc_I4_8); return;
			}
			if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
				il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
			else
				il.Emit(OpCodes.Ldc_I4, value);
		}

		internal static MethodInfo TryCloseGenericMethod(MethodInfo m, object[] args)
		{
			if (!m.IsGenericMethodDefinition) return m;

			var ps = m.GetParameters();
			var map = new Dictionary<Type, Type>();

			// 1) Bind generics from actual runtime arg types (unwrapped)
			for (int i = 0; i < ps.Length; i++)
			{
				var pT = ps[i].ParameterType;
				var aT = (args != null && i < args.Length) ? UnwrapRuntimeType(args[i]) : null;
				if (aT == null) continue;
				BindFromParameter(map, pT, aT);
			}

			var gpars = m.GetGenericArguments();

			// 2) If fully bound, close it
			if (gpars.All(map.ContainsKey))
				return m.MakeGenericMethod(gpars.Select(gp => map[gp]).ToArray());

			// 3) LINQ helper: if this is System.Linq.Enumerable.* and some generics remain,
			//    default any *unbound* generic parameters to 'object'. This covers
			//    Select<TSource,TResult>, SelectMany<TSource,TResult>, GroupBy<K,V> shapes, etc.
			if (m.DeclaringType == typeof(System.Linq.Enumerable))
			{
				var filled = new Type[gpars.Length];
				for (int i = 0; i < gpars.Length; i++)
					filled[i] = map.TryGetValue(gpars[i], out var bound) ? bound : typeof(object);

				try
				{
					return m.MakeGenericMethod(filled);
				}
				catch
				{
					// If constraints reject 'object', fall through and leave it open
					// (the candidate will fail and another overload may succeed).
				}
			}

			// 4) Couldn’t infer fully → leave open; caller will skip this candidate on invoke
			return m;

			// ---- helpers ----
			static Type UnwrapRuntimeType(object arg)
			{
				if (arg is null) return null;

				// ByRef box
				if (arg is KeysharpObject box && Script.TryGetPropertyValue(out var v, box, "__Value"))
					arg = v;

				if (arg is Clr.ManagedInstance mi) return mi._type;
				if (arg is Clr.ManagedType) return typeof(Type);
#if WINDOWS
				if (arg is ComValue) return typeof(IntPtr);
#endif

				return arg.GetType();
			}

			static void BindFromParameter(Dictionary<Type, Type> map, Type paramType, Type argType)
			{
				if (paramType.IsGenericParameter)
				{
					if (!map.ContainsKey(paramType)) map[paramType] = argType;
					return;
				}

				if (paramType.IsArray && argType.IsArray)
				{
					BindFromParameter(map, paramType.GetElementType()!, argType.GetElementType()!);
					return;
				}

				if (paramType.IsGenericType)
				{
					var pDef = paramType.GetGenericTypeDefinition();
					var aMatch = GetGenericAssignment(argType, pDef);
					if (aMatch != null)
					{
						var pArgs = paramType.GetGenericArguments();
						var aArgs = aMatch.GetGenericArguments();
						for (int i = 0; i < pArgs.Length; i++)
							BindFromParameter(map, pArgs[i], aArgs[i]);
						return;
					}

					// Structural best-effort if shapes align
					var pInner = paramType.GetGenericArguments();
					if (argType.IsGenericType)
					{
						var aInner = argType.GetGenericArguments();
						for (int i = 0, n = Math.Min(pInner.Length, aInner.Length); i < n; i++)
							BindFromParameter(map, pInner[i], aInner[i]);
					}
				}
			}

			static Type GetGenericAssignment(Type concrete, Type openGeneric)
			{
				if (concrete.IsGenericType && concrete.GetGenericTypeDefinition() == openGeneric)
					return concrete;

				foreach (var it in concrete.GetInterfaces())
					if (it.IsGenericType && it.GetGenericTypeDefinition() == openGeneric)
						return it;

				for (var bt = concrete.BaseType; bt != null; bt = bt.BaseType)
					if (bt.IsGenericType && bt.GetGenericTypeDefinition() == openGeneric)
						return bt;

				return null;
			}
		}


		private static object InvokeKeysharpFunc(object func, object[] args)
		{
			if (func is IFuncObj fo) return fo.Call(args);
			if (func is Delegate dnet) return dnet.DynamicInvoke(args);
			return Script.Invoke(func, "Call", args);
		}
	}

	internal static class ClrDelegateFactory
	{
		/// <summary>
		/// Supports:
		///   1) (callable)                    -> FromKeysharpFunc
		///   2) (target, "InstanceMethod")    -> CreateDelegate on instance
		///   3) (ManagedType/Type, "Static")  -> CreateDelegate on type (static)
		/// Returns ManagedInstance(delegateType, delegate) or an Error node.
		/// </summary>
		internal static object BuildManagedDelegateNode(Type delegateType, object[] argv)
		{
			try
			{
				// 1) single arg: Keysharp callable / existing delegate
				if (argv.Length == 1)
				{
					var arg = argv[0];
					if (arg is Delegate existing)
					{
						// if already assignable, just wrap
						if (delegateType.IsInstanceOfType(existing))
							return new Clr.ManagedInstance(delegateType, existing);

						// try to adapt using DynamicInvoke-compatible thunk
						var adapted = Delegate.CreateDelegate(delegateType, existing.Target, existing.Method.Name, /*ignoreCase*/ false, /*throwOnBindFailure*/ false);
						if (adapted != null)
							return new Clr.ManagedInstance(delegateType, adapted);
					}

					var del = ClrDelegateMarshaler.FromKeysharpFunc(delegateType, arg);
					return new Clr.ManagedInstance(delegateType, del);
				}

				// 2) two args with method name string
				if (argv.Length == 2 && argv[1] is string mname)
				{
					// (ManagedType/Type, "StaticMethod")
					if (argv[0] is Clr.ManagedType mt)
					{
						var del = Delegate.CreateDelegate(delegateType, mt._type, mname, /*ignoreCase*/ true, /*throwOnBindFailure*/ true);
						return new Clr.ManagedInstance(delegateType, del);
					}
					if (argv[0] is Type tt)
					{
						var del = Delegate.CreateDelegate(delegateType, tt, mname, /*ignoreCase*/ true, /*throwOnBindFailure*/ true);
						return new Clr.ManagedInstance(delegateType, del);
					}

					// (targetInstance, "InstanceMethod")
					var target = argv[0] is Clr.ManagedInstance mi ? mi._instance : argv[0];
					var del2 = Delegate.CreateDelegate(delegateType, target, mname, /*ignoreCase*/ true);
					return new Clr.ManagedInstance(delegateType, del2);
				}

				return Errors.ErrorOccurred($"Cannot construct delegate '{delegateType.FullName}' from {argv.Length} argument(s).");
			}
			catch (Exception ex)
			{
				return Errors.ErrorOccurred($"Delegate build for '{delegateType.FullName}' failed: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
