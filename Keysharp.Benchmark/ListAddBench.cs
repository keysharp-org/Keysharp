namespace Keysharp.Benchmark;

public class ListAddBench : BaseTest
{
	private readonly Keysharp.Builtins.Array keysharparray;
	private readonly List<object> nativelist = [];
	private readonly object o = 123L;

	[Params(500000)]
	public int Size { get; set; }

	/// <summary>
	/// Inits *must* be done in the constructor. Doing them inline causes the method lookups
	/// such as "Push" to fail.
	/// </summary>
	public ListAddBench() => keysharparray = new Keysharp.Builtins.Array();

	[Benchmark]
	public void KeysharpArrayDirectAdd()
	{
		keysharparray.Length = 0L;

		for (var i = 0; i < Size; i++)
			_ = ((System.Collections.IList)keysharparray).Add(o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void KeysharpArrayDirectAddWithPrealloc()
	{
		keysharparray.Length = 0L;
		keysharparray.Capacity = Size;

		for (var i = 0; i < Size; i++)
			_ = ((System.Collections.IList)keysharparray).Add(o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void KeysharpArrayDirectPush()
	{
		keysharparray.Length = 0L;

		for (var i = 0; i < Size; i++)
			_ = keysharparray.Push(o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void KeysharpArrayDirectPushWithPrealloc()
	{
		keysharparray.Length = 0L;
		keysharparray.Capacity = Size;

		for (var i = 0; i < Size; i++)
			_ = keysharparray.Push(o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void KeysharpArrayScriptPush()
	{
		keysharparray.Length = 0L;

		for (var i = 0; i < Size; i++)
			_ = Keysharp.Runtime.Script.InvokeOrNull(keysharparray, "Push", o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void KeysharpArrayScriptPushWithPrealloc()
	{
		keysharparray.Length = 0L;
		keysharparray.Capacity = Size;

		for (var i = 0; i < Size; i++)
			_ = Keysharp.Runtime.Script.InvokeOrNull(keysharparray, "Push", o);

		if (keysharparray.Count != Size)
			throw new Exception($"Native list size of {keysharparray.Count} was not equal to Size {Size}.");
	}

	[Benchmark(Baseline = true)]
	public void NativeListAdd()
	{
		nativelist.Clear();

		for (var i = 0; i < Size; i++)
			nativelist.Add(o);

		if (nativelist.Count != Size)
			throw new Exception($"Native list size of {nativelist.Count} was not equal to Size {Size}.");
	}

	[Benchmark]
	public void NativeListAddWithPrealloc()
	{
		nativelist.Clear();
		nativelist.Capacity = Size;

		for (var i = 0; i < Size; i++)
			nativelist.Add(o);

		if (nativelist.Count != Size)
			throw new Exception($"Native list size of {nativelist.Count} was not equal to Size {Size}.");
	}

	[GlobalSetup]
	public void Setup() => Size = 500000;
}