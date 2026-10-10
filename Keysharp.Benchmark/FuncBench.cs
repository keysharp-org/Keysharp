namespace Keysharp.Benchmark;

public class FuncBench : BaseTest
{
	private __Main.Myclass? cl;

	// The for-loop's per-element output write, three ways: the raw property write (the floor), the write every
	// loop actually performs (SetPropertyValue, which takes the plain-ref shortcut inside), and the same call on
	// a ref that cannot take it. The shortcut earns its keep iff plain << subclassed.
	private VarRef plainRef = default!;

	// The variadic argument path: collecting a `rest*` tail into the Array the body sees, and spreading one back
	// out at a call site (`inner(rest*)`). Both run on every variadic call and neither was covered before.
	private object[] rawTail = default!;

	private Keysharp.Builtins.Array spreadSource = default!;
	private VarRef subclassedRef = default!;
	private long totalSum;

	// `for k, v in obj` resolves __Enum once per LOOP, not per iteration, so the worst case for that resolution
	// is a tiny collection iterated in a tight outer loop -- which is what this measures.
	private Keysharp.Builtins.Map twoEntryMap = default!;

	[Params(500000L)]
	public long Size { get; set; }

	public object? x { get; set; }

	[Benchmark]
	public void ForEachSmallMap()
	{
		for (var i = 0L; i < Size; i++)
		{
			var e = MakeEnumerator(twoEntryMap, 2L);
			var k = new VarRef(null);
			var v = new VarRef(null);
			var a = new object[] { k, v };

			while (e.Call(a).IsCallbackResultNonEmpty())
				;
		}
	}

	public object IncFunc()
	{
		x = Add(x, 1L);
		return "";
	}

	[Benchmark]
	public void KeysharpClassFuncLoopIncrement()
	{
		_ = SetPropertyValue(cl, "x", 0L);
		_ = Invoke(cl, "ClassIncTestFuncScript");

		if ((long)GetPropertyValue(cl, "x") != totalSum)
			throw new Exception($"{x} was not equal to {totalSum}.");
	}

	[Benchmark]
	public void KeysharpFuncLoopIncrement()
	{
		x = 0L;
		_ = Push(Keysharp.Runtime.LoopType.Normal);

		for (var e0 = Loop(Size).GetEnumerator();
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
		_ = Push(Keysharp.Runtime.LoopType.Normal);

		for (var e0 = Loop(Size).GetEnumerator();
				IsTrueAndRunning(e0.MoveNext());
			)
		{
			{
				x = Add(x, 1L);
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
		_ = Push(Keysharp.Runtime.LoopType.Normal);

		for (var e0 = Loop(Size).GetEnumerator();
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
		_ = Push(Keysharp.Runtime.LoopType.Normal);

		for (var e0 = Loop(Size).GetEnumerator();
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
		cl = (__Main.Myclass)Invoke(__Main.myclass, "Call");

		rawTail = ["a", "b", "c"];
		spreadSource = new Keysharp.Builtins.Array(rawTail);

		object sink = 0L;
		plainRef = new VarRef(sink);
		subclassedRef = new DerivedRef(sink);
		twoEntryMap = new Keysharp.Builtins.Map("a", 1L, "b", 2L);

		_ = _ks_s.Vars.Prototypes[typeof(__Main.Myclass)];
	}

	[Benchmark]
	public void VariadicCollect()
	{
		for (var i = 0L; i < Size; i++)
			_ = new Keysharp.Builtins.Array(rawTail);
	}

	[Benchmark]
	public void VariadicSpread()
	{
		for (var i = 0L; i < Size; i++)
			_ = FlattenValues(spreadSource);
	}

	[Benchmark]
	public void VarRefWritePlain()
	{
		for (var i = 0L; i < Size; i++)
			_ = SetPropertyValue(plainRef, "__Value", i);
	}

	[Benchmark]
	public void VarRefWriteRaw()
	{
		for (var i = 0L; i < Size; i++)
			plainRef.__Value = i;
	}

	[Benchmark]
	public void VarRefWriteSubclassed()
	{
		for (var i = 0L; i < Size; i++)
			_ = SetPropertyValue(subclassedRef, "__Value", i);
	}

	public class __Main : Module
	{
		public static object myclass => _ks_s.Vars.Statics[typeof(Myclass)];

		public class Myclass : KeysharpObject
		{
			static Myclass()
			{
			}

			public Myclass(params object[] args) : base(args)
			{
			}

			public static void __Init(object @this)
			{
				_ = Keysharp.Runtime.Script.InvokeOrNull((_ks_s.Vars.Prototypes[typeof(KeysharpObject)], @this), "__Init");
				_ = Keysharp.Runtime.Script.SetPropertyValue(@this, "x", 0L);
			}

			public static object Classinc(object @this)
			{
				object _ks_temp1;
				object _ks_temp2;
				return Keysharp.Runtime.Script.MultiStatement(_ks_temp1 = @this, _ks_temp2 = "x", Keysharp.Runtime.Script.SetPropertyValue(_ks_temp1, _ks_temp2, Keysharp.Runtime.Script.Add(Keysharp.Runtime.Script.GetPropertyValue(_ks_temp1, _ks_temp2), 1L)));
			}

			public static object Classinctestfuncscript(object @this)
			{
				object size;
				size = 500000L;
				_ = Keysharp.Runtime.Script.SetPropertyValue(@this, "x", 0L);
				{
					_ = Keysharp.Runtime.Loops.Push(Keysharp.Runtime.LoopType.Normal);
					var _ks_e1 = Keysharp.Runtime.Loops.Loop(size).GetEnumerator();
					try
					{
						for (; IsTrueAndRunning(_ks_e1.MoveNext());)
						{
							_ = Keysharp.Runtime.Script.Invoke(@this, "ClassInc");
_ks_e1_next:
							;
						}
					}
					finally
					{
						_ = Keysharp.Runtime.Loops.Pop();
					}

_ks_e1_end:
					;
				}

				return "";
			}

#pragma warning disable IDE0060 // Remove unused parameter

			public static void static__Init(object @this)
			{
			}

			public static new Myclass staticCall(object @this, params object[] args) => new(args);

#pragma warning restore IDE0060 // Remove unused parameter
		}
	}

	// Subclassing is the one thing that makes a ref non-plain, so this is what the dispatching case must be.
	private sealed class DerivedRef(object x) : VarRef(x);
}