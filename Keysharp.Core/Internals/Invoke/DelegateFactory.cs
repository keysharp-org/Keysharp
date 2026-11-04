using Keysharp.Builtins;
using System.Linq.Expressions;

namespace Keysharp.Internals.Invoke
{
	/// <summary>
	/// Adapts script arguments to CLR members. Cold calls interpret the adapter; repeated calls use the same
	/// expression compiled to a direct call, without a counter or tier check on the hot path.
	/// </summary>
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class DelegateFactory
	{
		// The calls a member, field or constructor runs cold before it is compiled. Compiling costs about as much as 500
		// interpreted calls, while a hot loop waits on each call it interprets; 128 measured best across both.
		internal const int CompilationThreshold = 128;

		internal static Func<object, object[], object> CreateDelegate(MethodPropertyHolder mph) => new Invocation(mph).Invoke;

		private sealed class Invocation
		{
			private readonly MethodPropertyHolder mph;
			private Core core;
			private readonly bool scoped;
			private readonly ModuleData module;

			internal Invocation(MethodPropertyHolder mph)
			{
				this.mph = mph;
				var expression = BuildCore(mph);
				// Stack-only values cannot be interpreted; a value-type receiver needs the compiled call's copy semantics.
				if (!mph.IsStatic && mph.mi.DeclaringType.IsValueType
					|| mph.parameters.Any(parameter => NeedsCompilation(parameter.ParameterType)) || NeedsCompilation(mph.mi.ReturnType))
					core = expression.Compile();
				else
					core = CreateColdCore(expression);
				module = mph.InScriptModule ? ModuleData.GetOrCreate(mph.moduleType) : null;
				scoped = module != null || mph.compatibilityLevel != null;
			}

			private Core CreateColdCore(Expression<Core> expression)
			{
				var interpreted = expression.Compile(preferInterpretation: true);
				var callsUntilCompilation = CompilationThreshold;
				return ColdInvoke;

				// In-flight cold calls retain this closure; publication releases it once they finish.
				object ColdInvoke(object target, object[] args, int start)
				{
					if (Interlocked.Decrement(ref callsUntilCompilation) == 0)
					{
						var compiled = expression.Compile();
						Volatile.Write(ref core, compiled);
						return compiled(target, args, start);
					}
					return interpreted(target, args, start);
				}
			}

			private static bool NeedsCompilation(Type type) =>
				type.IsByRefLike || type.IsByRef || type.IsPointer || type.IsFunctionPointer;

			public object Invoke(object instance, object[] args)
			{
				args ??= System.Array.Empty<object>();

				// Bind names before validating the count. A setter's trailing value is data, even when it is a named-argument container.
				if (!mph.isSetter && NamedArgBinder.Has(args))
					args = NamedArgBinder.Bind(mph, args, NamedArgBinder.ArgBase(mph, instance));

				var isInstance = !mph.IsStatic;
				int lastProvided = args.Length;
				int provided = lastProvided + (instance == null ? 0 : 1);
				if (isInstance && mph.IsCompilerGenerated) provided--; // account for Inst-provided 'this', which does not count as argument

				for (int i = lastProvided - 1; i >= 0; i--)
				{
					if (args[i] == null) provided--;
					else break;
				}

				// As AutoHotkey reports them: a builtin given too few arguments names itself, while a script function names
				// the first parameter it lacks, which binding the arguments finds.
				if (provided < mph.MinParams && !mph.IsScript)
					throw new ArgumentError("Too few parameters passed to function.", null, mph.QualifiedName);

				if (!mph.IsVariadic && provided > mph.MaxParams)
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
				var previousCompatibility = 0;
				var switched = false;
				var switchedCompatibility = false;

				if (scoped)
				{
					previousModule = stack.ModuleOrDefault(TheScript);
					switched = module != null && !ReferenceEquals(previousModule, module);
					switchedCompatibility = switched || mph.compatibilityLevel != null;

					if (switched)
						stack.Module = module;

					if (switchedCompatibility)
					{
						previousCompatibility = Script.currentCompatibilityLevel;
						Script.currentCompatibilityLevel = mph.compatibilityLevel ?? module.CompatibilityLevel;
					}
				}

				try
				{
					return Volatile.Read(ref core)(target, args, start);
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
					if (switchedCompatibility)
						Script.currentCompatibilityLevel = previousCompatibility;
				}
			}
		}

		private static Expression<Core> BuildCore(MethodPropertyHolder holder)
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

			return Expression.Lambda<Core>(body, pTarget, pArgs, pStart);
		}

		private delegate object Core(object target, object[] args, int start);

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

		// The error of a required parameter that is missing or unset. The function's body has not started, so the error is
		// its caller's, as AutoHotkey reports it.
		[StackTraceHidden]
		private static object ThrowMissingArgument(string name)
		{
			CallStack.Current.PopUnstarted();
			throw new ArgumentError("Missing a required parameter.", null, name);
		}
	}
}
