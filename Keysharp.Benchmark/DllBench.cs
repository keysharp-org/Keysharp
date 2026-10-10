#if WINDOWS
namespace Keysharp.Benchmark;

[IterationCount(5)]
[InvocationCount(5)]
[WarmupCount(15)]
public class DllBench : BaseTest
{
	private static readonly object mcode_c = _ = Keysharp.Runtime.Script.IfTest(Keysharp.Runtime.Script.ValueEquality(Accessors.A_PtrSize, 8L)) ? (_ = "x64") : (object)(_ = "x86");
	private static readonly object mcode_e = new Keysharp.Builtins.Map("1", 4L, "2", 1L);
	private static object p = 0L, ptr = 0L, result = 0L;

	[Params(100000)]
	public int Size { get; set; }

	public static object CallbackTwoArgs(object arg1, object arg2) => _ = Keysharp.Runtime.Script.Add(arg1, arg2);

	public static object MCode(object mcode)
	{
		object? m = null;
		object? s = null;
		object? p = null;
		object? op = null;

		if (Keysharp.Runtime.Script.IfTest(Keysharp.Runtime.Script.LogicalNot(Keysharp.Builtins.RegEx.RegExMatch(mcode, Keysharp.Runtime.Script.Concat("^([0-9]+),(", Keysharp.Runtime.Script.Concat(Keysharp.Runtime.Script.Concat(Keysharp.Runtime.Script.Concat(mcode_c, ":|.*?,"), mcode_c), ":)([^,]+)")), new VarRef(() => m, v => m = v)))))
		{
			return _ = "";
		}

		s = 0L;

		if (Keysharp.Runtime.Script.IfTest(Keysharp.Runtime.Script.LogicalNot(Keysharp.Builtins.Dll.DllCall("crypt32\\CryptStringToBinary", "str", Keysharp.Runtime.Script.GetPropertyValue(m, "3"), "uint", 0L, "uint", Keysharp.Runtime.Script.GetIndex(mcode_e, Keysharp.Runtime.Script.GetPropertyValue(m, "1")), "ptr", 0L, "uint*", new VarRef(() => s, v => s = v), "ptr", 0L, "ptr", 0L))))
		{
			return _ = "";
		}

		p = Keysharp.Builtins.Dll.DllCall("GlobalAlloc", "uint", 0L, "ptr", s, "ptr");

		if (Keysharp.Runtime.Script.IfTest(Keysharp.Runtime.Script.ValueEquality(mcode_c, "x64")))
		{
			op = 0L;
			_ = Keysharp.Builtins.Dll.DllCall("VirtualProtect", "ptr", p, "ptr", s, "uint", 64L, "uint*", new VarRef(() => op, value => op = value));
		}

		if (Keysharp.Runtime.Script.IfTest(Keysharp.Builtins.Dll.DllCall("crypt32\\CryptStringToBinary", "str", Keysharp.Runtime.Script.GetPropertyValue(m, "3"), "uint", 0L, "uint", Keysharp.Runtime.Script.GetIndex(mcode_e, Keysharp.Runtime.Script.GetPropertyValue(m, "1")), "ptr", p, "uint*", new VarRef(() => s, v => s = v), "ptr", 0L, "ptr", 0L)))
		{
			return _ = p;
		}

		_ = Keysharp.Builtins.Dll.DllCall("GlobalFree", "ptr", p);
		return "";
	}
	[Benchmark(Baseline = true)]
	public void CreateCallbackThenFree()
	{
		try
		{
			_ = Keysharp.Runtime.Loops.Push(Keysharp.Runtime.LoopType.Normal);

			for (var _ks_e0 = Keysharp.Runtime.Loops.Loop(Size).GetEnumerator(); Keysharp.Runtime.Flow.IsTrueAndRunning(_ks_e0.MoveNext());
				)
			{
				p = Keysharp.Builtins.Dll.CallbackCreate(Keysharp.Builtins.Functions.Func(CallbackTwoArgs));
				result = Keysharp.Builtins.Dll.DllCall(ptr, "ptr", p, "int", Keysharp.Runtime.Script.Minus(1L), "int", 4L);
				_ = Keysharp.Builtins.Dll.CallbackFree(p);
_ks_e1:
				;
			}
		}
		finally
		{
			_ = Keysharp.Runtime.Loops.Pop();
		}
	}

	[GlobalSetup]
	public void Setup() => ptr = MCode("2,x64:SInIidFEicJI/+A=");
}
#endif
