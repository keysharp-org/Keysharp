using System.Linq.Expressions;

namespace Keysharp.Internals.Invoke
{
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class FastCtor
	{
		private static readonly ConcurrentDictionary<Type, Func<object[], object>> Cache = new();

		public static object Call(Type type, params object[] args)
		{
			var activator = Cache.GetOrAdd(type, BuildFactory);
			return activator(args);
		}

		private static Func<object[], object> BuildFactory(Type type)
		{
			// Script constructors receive their arguments as a single object array.
			var ctor = type.GetConstructor([typeof(object[])]);
			if (ctor is null)
				return args => Activator.CreateInstance(type, args);

			var invoker = ConstructorInvoker.Create(ctor);
			var callsUntilCompilation = DelegateFactory.CompilationThreshold;
			return Construct;

			object Construct(object[] args)
			{
				if (Interlocked.Decrement(ref callsUntilCompilation) == 0)
				{
					var arguments = Expression.Parameter(typeof(object[]), "args");
					var body = Expression.Convert(Expression.New(ctor, arguments), typeof(object));
					var hot = Expression.Lambda<Func<object[], object>>(body, arguments).Compile();
					Cache[type] = hot;
					return hot(args);
				}

				return invoker.Invoke((object)args);
			}
		}
	}
}
