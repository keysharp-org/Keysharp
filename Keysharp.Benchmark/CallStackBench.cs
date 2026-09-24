using static Keysharp.Runtime.Script;

namespace Keysharp.Benchmark;

// What the script call stack costs: every dispatched call keeps it, and every Error reads it.
public class CallStackBench : BaseTest
{
	private const int Calls = 100_000;
	private const int ErrorCount = 1_000;

	private object id = default!;
	private object strLen = default!;
	private object errorClass = default!;

	[GlobalSetup]
	public void Setup()
	{
		id = Functions.Func((Delegate)__Main.Id);
		strLen = Functions.Func((Delegate)Strings.StrLen);
		__Main.deep = Functions.Func((Delegate)__Main.Deep);
		errorClass = _ks_s.Vars.Statics[typeof(Error)];
	}

	[Benchmark(OperationsPerInvoke = Calls)]
	public void UserFunctionCall()
	{
		for (var i = 0; i < Calls; i++)
			_ = Invoke(id, null, 1L);
	}

	[Benchmark(OperationsPerInvoke = Calls)]
	public void BuiltinCall()
	{
		for (var i = 0; i < Calls; i++)
			_ = Invoke(strLen, null, "abc");
	}

	[Benchmark(OperationsPerInvoke = ErrorCount)]
	public void ErrorFromRuntime()
	{
		for (var i = 0; i < ErrorCount; i++)
			_ = new Error("x");
	}

	[Benchmark(OperationsPerInvoke = ErrorCount)]
	public void ErrorLine()
	{
		for (var i = 0; i < ErrorCount; i++)
			_ = ((Error)Invoke(errorClass, null, "x")).Line;
	}

	[Benchmark(OperationsPerInvoke = ErrorCount)]
	public void ThrowAndCatch()
	{
		for (var i = 0; i < ErrorCount; i++)
		{
			try
			{
				using (Keysharp.Runtime.Flow.EnterTry())
					throw Keysharp.Runtime.Flow.Throw(new Error("x"));
			}
			catch (KeysharpException)
			{
			}
		}
	}

	[Benchmark(OperationsPerInvoke = ErrorCount)]
	public void StackAtDepth20()
	{
		for (var i = 0; i < ErrorCount; i++)
			_ = Invoke(__Main.deep, null, 20L);
	}

	public class __Main : Module
	{
		internal static object deep = default!;

		public static object Id(object x) => x;

		public static object Deep(object n) => (long)n == 0 ? new Error("x").Stack : Invoke(deep, null, (long)n - 1);
	}
}
