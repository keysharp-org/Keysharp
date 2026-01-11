using static Keysharp.Core.Flow;
using static Keysharp.Core.Loops;
using static Keysharp.Scripting.Script;
using static Keysharp.Scripting.Script.Operator;

namespace Keysharp.Benchmark
{
	public class FuncBench : BaseTest
	{
		private Myclass? cl;
		private long totalSum;

		[Params(500000L)]
		public long Size { get; set; }

		public object? x { get; set; }

		public object IncFunc()
		{
			x = Operate(Add, x, 1L);
			return "";
		}

		[Benchmark]
		public void KeysharpClassFuncLoopIncrement()
		{
			_ = SetPropertyValue(0L, cl, "x");
			_ = Invoke(cl, "ClassIncTestFuncScript");

			if ((long)GetPropertyValue(cl, "x") != totalSum)
				throw new Exception($"{x} was not equal to {totalSum}.");
		}

		[Benchmark]
		public void KeysharpFuncLoopIncrement()
		{
			x = 0L;
			_ = Push(Keysharp.Core.LoopType.Normal);

			for (System.Collections.IEnumerator e0 = Loop(Size).GetEnumerator();
					IsTrueAndRunning(e0.MoveNext());
				)
			{
				_ = IncFunc();
				e1:
				;
			}

			e2:
			_ = Pop();

			if ((long)x != totalSum)
				throw new Exception($"{x} was not equal to {totalSum}.");
		}

		[Benchmark]
		public void KeysharpLoopIncrement()
		{
			x = 0L;
			_ = Push(Keysharp.Core.LoopType.Normal);

			for (System.Collections.IEnumerator e0 = Loop(Size).GetEnumerator();
					IsTrueAndRunning(e0.MoveNext());
				)
			{
				{
					x = Operate(Add, x, 1L);
					e1:
					;
				}
			}

			e2:
			_ = Pop();

			if ((long)x != totalSum)
				throw new Exception($"{x} was not equal to {totalSum}.");
		}

		[Benchmark]
		public void KeysharpNativeLongLoopIncrement()
		{
			var total = 0L;
			_ = Push(Keysharp.Core.LoopType.Normal);

			for (System.Collections.IEnumerator e0 = Loop(Size).GetEnumerator();
					IsTrueAndRunning(e0.MoveNext());
				)
			{
				total++;
				e1:
				;
			}

			e2:
			_ = Pop();

			if (total != totalSum)
				throw new Exception($"{x} was not equal to {totalSum}.");
		}

		[Benchmark]
		public void KeysharpNativeObjectLoopIncrement()
		{
			x = 0L;
			_ = Push(Keysharp.Core.LoopType.Normal);

			for (System.Collections.IEnumerator e0 = Loop(Size).GetEnumerator();
					IsTrueAndRunning(e0.MoveNext());
				)
			{
				x = (long)x + 1L;
				e1:
				;
			}

			e2:
			_ = Pop();

			if ((long)x != totalSum)
				throw new Exception($"{x} was not equal to {totalSum}.");
		}

		[Benchmark(Baseline = true)]
		public void NativeLoopIncrement()
		{
			var total = 0L;

			for (var i = 0L; i < Size; i++)
				total++;

			if (total != totalSum)
				throw new Exception($"{total} was not equal to {totalSum}.");
		}

		[GlobalSetup]
		public void Setup()
		{
			Size = 500000L;
			totalSum = Size;
			cl = (Myclass)Invoke(myclass, "Call");

			var mc = _ks_s.Vars.Prototypes[typeof(Myclass)];
		}

		public object myclass => _ks_s.Vars.Statics[typeof(Myclass)];

		public class Myclass : KeysharpObject
		{
			public Myclass(params object[] args) : base(args)
			{
			}

			public static object Classinc(object @this)
			{
				object _ks_temp1 = null;
				object _ks_temp2 = null;
				return Keysharp.Scripting.Script.MultiStatement(_ks_temp1 = @this, _ks_temp2 = "x", Keysharp.Scripting.Script.SetPropertyValue(Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Add, Keysharp.Scripting.Script.GetPropertyValue(_ks_temp1, _ks_temp2), 1L), _ks_temp1, _ks_temp2));
			}

			public static object Classinctestfuncscript(object @this)
			{
				object size = null;
				size = 500000L;
				Keysharp.Scripting.Script.SetPropertyValue(0L, @this, "x");
				{
					System.Collections.IEnumerator _ks_e1 = Keysharp.Core.Loops.Loop(size).GetEnumerator();
					Keysharp.Core.Loops.Push(Keysharp.Core.LoopType.Normal);
					try
					{
						for (; IsTrueAndRunning(_ks_e1.MoveNext());)
						{
							Keysharp.Scripting.Script.Invoke(@this, "ClassInc");
						_ks_e1_next:
							;
						}
					}
					finally
					{
						Keysharp.Core.Loops.Pop();
					}

				_ks_e1_end:
					;
				}

				return "";
			}

			public static void __Init(object @this)
			{
				Keysharp.Scripting.Script.Invoke((object)(_ks_s.Vars.Prototypes[typeof(KeysharpObject)], @this), "__Init");
				Keysharp.Scripting.Script.SetPropertyValue(0L, @this, "x");
			}

			public static void static__Init(object @this)
			{
				Keysharp.Scripting.Script.SetPropertyValue("myclass", _ks_s.Vars.Prototypes[typeof(Myclass)], "__Class");
			}

			static Myclass()
			{
			}

			public static Myclass staticCall(object @this, params object[] args)
			{
				return new Myclass(args);
			}
		}
	}

	//[MemoryDiagnoser]
}