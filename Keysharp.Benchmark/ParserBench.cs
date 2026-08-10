namespace Keysharp.Benchmark;

public class ParserBench : BaseTest
{
	private new Keysharp.Runtime.Script _ks_s = default!;

	[Params(1000)]
	public int Size { get; set; }

	[Benchmark]
	public void CreateTreeFromFile()
	{
		var ch = new CompilerHelper();

		_ = ch.CreateCompilationUnitFromFile("./Keysharp.ks");
	}

	[GlobalSetup]
	public void Setup()
	{
		_ks_s = new();
		_ks_s.Vars.InitClasses();
	}
}
