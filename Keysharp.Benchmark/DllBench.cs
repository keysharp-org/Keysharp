#if WINDOWS
namespace Keysharp.Benchmark
{
	[IterationCount(5)]
	[InvocationCount(5)]
	[WarmupCount(15)]
	public class DllBench : BaseTest
	{
		private static readonly object mcode_e = new Keysharp.Core.Map("1", 4L, "2", 1L);
		private static readonly object mcode_c = (_ = Keysharp.Scripting.Script.IfTest(Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.ValueEquality, Accessors.A_PtrSize, 8L)) ? (object)(_ = "x64") : (object)(_ = "x86"));
		private static object p = 0L, ptr = 0L, result = 0L;

		public static object CallbackTwoArgs(object arg1, object arg2)
		{
			return _ = Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Add, arg1, arg2);
		}

		public static object MCode(object mcode)
		{
			object? m = null;
			object? s = null;
			object? p = null;
			object? op = null;

			if (Keysharp.Scripting.Script.IfTest(Keysharp.Scripting.Script.OperateUnary(Keysharp.Scripting.Script.Operator.LogicalNot, Keysharp.Core.RegEx.RegExMatch(mcode, Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Concat, "^([0-9]+),(", Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Concat, Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Concat, Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Concat, mcode_c, ":|.*?,"), mcode_c), ":)([^,]+)")), new VarRef(() => m, v => m = v)))))
			{
				return _ = "";
			}

			s = 0L;

			if (Keysharp.Scripting.Script.IfTest(Keysharp.Scripting.Script.OperateUnary(Keysharp.Scripting.Script.Operator.LogicalNot, Keysharp.Core.Dll.DllCall("crypt32\\CryptStringToBinary", "str", Keysharp.Scripting.Script.GetPropertyValue(m, "3"), "uint", 0L, "uint", Keysharp.Scripting.Script.Index(mcode_e, Keysharp.Scripting.Script.GetPropertyValue(m, "1")), "ptr", 0L, "uint*", new VarRef(() => s, v => s = v), "ptr", 0L, "ptr", 0L))))
			{
				return _ = "";
			}

			p = Keysharp.Core.Dll.DllCall("GlobalAlloc", "uint", 0L, "ptr", s, "ptr");

			if (Keysharp.Scripting.Script.IfTest(Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.ValueEquality, mcode_c, "x64")))
			{
				op = 0L;
				_ = Keysharp.Core.Dll.DllCall("VirtualProtect", "ptr", p, "ptr", s, "uint", 64L, "uint*", new VarRef(() => op, value => op = value));
			}

			if (Keysharp.Scripting.Script.IfTest(Keysharp.Core.Dll.DllCall("crypt32\\CryptStringToBinary", "str", Keysharp.Scripting.Script.GetPropertyValue(m, "3"), "uint", 0L, "uint", Keysharp.Scripting.Script.Index(mcode_e, Keysharp.Scripting.Script.GetPropertyValue(m, "1")), "ptr", p, "uint*", new VarRef(() => s, v => s = v), "ptr", 0L, "ptr", 0L)))
			{
				return _ = p;
			}

			_ = Keysharp.Core.Dll.DllCall("GlobalFree", "ptr", p);
			return "";
		}

		[Params(100000)]
		public int Size { get; set; }

		[Benchmark(Baseline = true)]
		public void CreateCallbackThenFree()
		{
			try
			{
				_ = Keysharp.Core.Loops.Push(Keysharp.Core.LoopType.Normal);

				for (System.Collections.IEnumerator _ks_e0 = Keysharp.Core.Loops.Loop(Size).GetEnumerator(); Keysharp.Core.Flow.IsTrueAndRunning(_ks_e0.MoveNext());
					)
				{
					p = Keysharp.Core.Dll.CallbackCreate(Keysharp.Core.Functions.Func(CallbackTwoArgs));
					result = Keysharp.Core.Dll.DllCall(ptr, "ptr", p, "int", Keysharp.Scripting.Script.OperateUnary(Keysharp.Scripting.Script.Operator.Subtract, 1L), "int", 4L);
					_ = Keysharp.Core.Dll.CallbackFree(p);
					_ks_e1:
					;
				}
			}
			finally
			{
				_ = Keysharp.Core.Loops.Pop();
			}
		}

		[GlobalSetup]
		public void Setup()
		{
			ptr = MCode("2,x64:SInIidFEicJI/+A=");
		}
	}
}
#endif