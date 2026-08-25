using System.Security.Cryptography;

using BenchmarkDotNet.Order;

namespace Keysharp.Benchmark;

[MemoryDiagnoser]
[InProcess]
// Order the result
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
//[IterationCount(30)]
//[IterationCount(3)]
//[InvocationCount(50)]
//[InvocationCount(3)]
//[WarmupCount(15)]
[HideColumns("Error", "StdDev", "RatioSD", "Gen0", "Gen1", "Gen2")]
public class BaseTest
{
	internal static Keysharp.Runtime.Script _ks_s = null!;

	public BaseTest() => _ks_s ??= new(GetType().BaseType);
}

public sealed class Program
{
	[System.STAThreadAttribute()]
	public static void Main(string[] args)
	{
		BenchmarkDotNet.Reports.Summary summary;
		var logger = ConsoleLogger.Default;
#if DEBUG
		var config = new BenchmarkDotNet.Configs.DebugInProcessConfig();
#else
		var config = new ManualConfig();
#endif
		_ = config.AddLogger(logger);   // ManualConfig has none, so without this a run prints nothing until it ends
		_ = config.AddColumnProvider([.. DefaultConfig.Instance.GetColumnProviders()]);
		_ = config.AddExporter([.. DefaultConfig.Instance.GetExporters()]);
		_ = config.AddDiagnoser([.. DefaultConfig.Instance.GetDiagnosers()]);
		_ = config.AddAnalyser([.. DefaultConfig.Instance.GetAnalysers()]);
#if !DEBUG
		_ = config.AddValidator([.. DefaultConfig.Instance.GetValidators()]);
		_ = config.AddJob([.. DefaultConfig.Instance.GetJobs()]);
		config.UnionRule = ConfigUnionRule.AlwaysUseGlobal; // Overriding the default
#endif

		//Uncomment the tests you want to run.
		//summary = BenchmarkRunner.Run<MapReadBenchmark>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<MapWriteBenchmark>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<IndexBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<ListAddBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<HexBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<MathBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<FuncBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<OverlayBench>(config);
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		//summary = BenchmarkRunner.Run<DllBench>();
		//MarkdownExporter.Console.ExportToLog(summary, logger);
		summary = BenchmarkRunner.Run<FuncBench>(config);
		MarkdownExporter.Console.ExportToLog(summary, logger);

		//ConclusionHelper.Print(logger, summary.BenchmarksCases.First().Config.GetCompositeAnalyser().Analyse(summary).ToList());
		_ = Console.ReadLine();
	}
}

public class Md5VsSha256
{
	private const int N = 10000;
	private readonly byte[] data;

	private readonly SHA256 sha256 = SHA256.Create();
	private readonly MD5 md5 = MD5.Create();

	public Md5VsSha256()
	{
		data = new byte[N];
		new Random(42).NextBytes(data);
	}

	[Benchmark]
	public byte[] Sha256() => sha256.ComputeHash(data);

	[Benchmark]
	public byte[] Md5() => md5.ComputeHash(data);
}
