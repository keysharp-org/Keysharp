using Array = Keysharp.Builtins.Array;

namespace Keysharp.Benchmark;

public class IndexBench : BaseTest
{
	private dynamic? dynamickeysharparray;
	private Array? keysharparray;
	private object[]? nativearray;
	private double[]? nativedoublearray;
	private double totalSum;

	[Params(500000)]
	public int Size { get; set; }

	public IndexBench()
	{
		dynamickeysharparray = new Keysharp.Builtins.Array();
		keysharparray = new Keysharp.Builtins.Array();
	}

	[Benchmark]
	public void KeysharpArrayIndexEnumeratorRead()
	{
		var total = 0.0;
		var e0 = keysharparray;
		var e2 = Loops.MakeEnumerator(e0, 1);
		_ = Loops.Push();
		object val = null!;
		object vr = new VarRef(() => val, value => val = value);

		for (; Keysharp.Runtime.Flow.IsTrueAndRunning(e2.Call(vr));)
		{
			_ = Keysharp.Runtime.Loops.Inc();
			total += (double)val!;
e3:
			;
		}

e4:
		_ = Loops.Pop();

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark]
	public void KeysharpArrayIndexMethodRead()
	{
		var total = 0.0;

		for (long i = 1; i <= Size; i++)
			total += (double)GetIndex(keysharparray, i);

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark]
	public void KeysharpArrayIndexRead()
	{
		var total = 0.0;

		for (var i = 1; i <= Size; i++)
			total += (double)keysharparray![i];

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark]
	public void KeysharpDynamicArrayIndexRead()
	{
		var total = 0.0;

		for (var i = 1; i <= Size; i++)
			total += (double)dynamickeysharparray![i];

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark]
	public void NativeDoubleArrayRead()
	{
		var total = 0.0;

		for (var i = 0; i < Size; i++)
			total += nativedoublearray![i];

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark(Baseline = true)]
	public void NativeObjectArrayRead()
	{
		var total = 0.0;

		for (var i = 0; i < Size; i++)
			total += (double)nativearray![i];

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[Benchmark]
	public unsafe void NativeUnsafeDoubleArrayRead()
	{
		var total = 0.0;

		fixed (double* ptr = nativedoublearray)
		{
			for (var i = 0; i < Size; i++)
				total += ptr[i];
		}

		if (!total.IsAlmostEqual(totalSum))
			throw new Exception($"{total} was not equal to {totalSum}.");
	}

	[GlobalSetup]
	public void Setup()
	{
		totalSum = 0;
		Size = 1000000;
		nativearray = new object[Size];
		nativedoublearray = new double[Size];
		keysharparray = new Array()
		{
			Capacity = Size
		};
		dynamickeysharparray = new Array();
		dynamickeysharparray.Capacity = Size;

		for (var i = 0; i < Size; i++)
		{
			var val = Maths.Random();
			_ = val.TryCoerceDouble(out var d);
			nativearray[i] = d;
			nativedoublearray[i] = d;
			_ = ((System.Collections.IList)keysharparray).Add(d);
			_ = dynamickeysharparray.Push(d);
			totalSum += d;
		}
	}
}