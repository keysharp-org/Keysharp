using System.Linq.Expressions;
using Keysharp.Builtins;

namespace Keysharp.Internals.Invoke
{
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class FastCtor
	{
		private static readonly ConcurrentDictionary<Type, Factory> ByClass = new();

		internal static Factory For(Type type) => type == null ? null : ByClass.GetOrAdd(type, ForClass);

		internal static bool CanConstruct(Type type) => For(type) != null;

		internal static Any Create(Any prototype) => Required(prototype.type).Create(prototype);

		internal static Any Instantiate(Type type) => Required(type).Instantiate();

		private static Factory Required(Type type) => For(type)
			?? throw new MissingMethodException($"{type.FullName} has no supported native constructor.");

		// A built-in's native constructor is protected when its public one is C#'s, which runs its initialization.
		private static Factory ForClass(Type type) =>
			!type.IsAbstract && typeof(Any).IsAssignableFrom(type) && Constructor(type, Type.EmptyTypes) is { } ctor ? new Factory(ctor) : null;

		internal static ConstructorInfo Constructor(Type type, Type[] parameters) =>
			type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.ExactBinding, parameters)
				is ({ IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true }) and var ctor ? ctor : null;

		// Interpreted until it has been called often enough to be worth compiling, as DelegateFactory's methods are.
		internal sealed class Factory
		{
			private readonly ConstructorInfo ctor;
			private readonly ConstructorInvoker invoker;
			private readonly bool isStruct;
			private int callsUntilCompilation = DelegateFactory.CompilationThreshold;
			private Func<Any> compiled;

			internal Factory(ConstructorInfo ctor)
			{
				this.ctor = ctor;
				invoker = ConstructorInvoker.Create(ctor);
				isStruct = Struct.IsStructType(ctor.DeclaringType);
			}

			// An instance of the prototype's class without running any of its script code, as AutoHotkey's NewInstance before
			// Initialize.
			internal Any Create(Any prototype)
			{
				if (isStruct && StructLayout.GetValidated(prototype.type).Invalid)
					return null;

				var instance = Instantiate();
				if (instance._base != prototype)
					instance.SetBaseInternal(prototype);
				return instance;
			}

			internal Any Instantiate() => compiled is { } hot ? hot() : New();

			private Any New()
			{
				if (Interlocked.Decrement(ref callsUntilCompilation) == 0)
					return (compiled = Expression.Lambda<Func<Any>>(Expression.Convert(Expression.New(ctor), typeof(Any))).Compile())();

				return (Any)invoker.Invoke();
			}
		}
	}
}
