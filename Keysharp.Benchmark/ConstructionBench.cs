using Keysharp.Builtins;
using Array = Keysharp.Builtins.Array;

namespace Keysharp.Benchmark;

public class ConstructionBench : BaseTest
{
	private readonly object[] values = [1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L];
	private readonly object[] pairs = ["One", 1L, "Two", 2L, "Three", 3L, "Four", 4L];
	private object[] foreign = null!;
	private object arrayClass = null!, mapClass = null!;
	private InputHook hook = null!;

	[GlobalSetup]
	public void Setup()
	{
		foreign = [new System.Collections.ArrayList(values)];
		arrayClass = _ks_s.Vars.Statics[typeof(Array)];
		mapClass = _ks_s.Vars.Statics[typeof(Map)];
		hook = new InputHook("L16V", "", "alpha,beta");
	}

	[Benchmark]
	public object ArrayEmpty() => new Array();

	[Benchmark]
	public object ArrayPopulated() => new Array(values);

	[Benchmark]
	public object ArrayLiteral() => Array.Literal(values);

	[Benchmark]
	public object ArrayForeignEnumerable() => new Array(foreign);

	[Benchmark]
	public object MapEmpty() => new Map();

	[Benchmark]
	public object MapPopulated() => new Map(pairs);

	[Benchmark]
	public object MapCaseInsensitive() => new CaseInsensitiveMap(pairs);

	[Benchmark]
	public object NamedArguments() => new Ks.NamedArgs(pairs);

	[Benchmark]
	public object RawArray() => Objects.ObjCreateWithDataPtr(arrayClass, 0L);

	[Benchmark]
	public object RawMap() => Objects.ObjCreateWithDataPtr(mapClass, 0L);

	[Benchmark]
	public object InputHookNew() => new InputHook("L16V", "", "alpha,beta");

	[Benchmark]
	public object InputHookReconfigure() => hook.__New("L16V", "", "alpha,beta");

	public class CaseInsensitiveMap(params object[] args) : Map(args)
	{
		public object __Init(params object[] args)
		{
			CaseSense = "Off";
			return "";
		}
	}
}
