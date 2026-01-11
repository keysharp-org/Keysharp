using System.Security.Cryptography;
using BenchmarkDotNet.Order;

namespace Keysharp.Benchmark
{
	[MemoryDiagnoser]
	[InProcess]
	[Orderer(SummaryOrderPolicy.FastestToSlowest)] // Order the result
	//[IterationCount(30)]
	//[IterationCount(3)]
	//[InvocationCount(50)]
	//[InvocationCount(3)]
	//[WarmupCount(15)]
	[HideColumns("Error", "StdDev", "RatioSD", "Gen0", "Gen1", "Gen2")]
	public class BaseTest
	{
		internal static Keysharp.Scripting.Script _ks_s = new ();

		public BaseTest()
		{
			if (_ks_s == null)
				_ks_s = new ();
		}
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
			_ = config.AddColumnProvider([.. DefaultConfig.Instance.GetColumnProviders()]);
			_ = config.AddExporter([.. DefaultConfig.Instance.GetExporters()]);
			_ = config.AddDiagnoser([.. DefaultConfig.Instance.GetDiagnosers()]);
			_ = config.AddAnalyser([.. DefaultConfig.Instance.GetAnalysers()]);
			_ = config.AddValidator([.. DefaultConfig.Instance.GetValidators()]);
#if !DEBUG
			_ = config.AddJob([.. DefaultConfig.Instance.GetJobs()]);
			config.UnionRule = ConfigUnionRule.AlwaysUseGlobal; // Overriding the default
#endif
			/*
			    summary = BenchmarkRunner.Run<MapReadBenchmark>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<MapWriteBenchmark>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<IndexBench>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<ListAddBench>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<HexBench>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<MathBench>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
			    summary = BenchmarkRunner.Run<FuncBench>(config);
			    MarkdownExporter.Console.ExportToLog(summary, logger);
				summary = BenchmarkRunner.Run<DllBench>();
				MarkdownExporter.Console.ExportToLog(summary, logger);
			*/
			summary = BenchmarkRunner.Run<FuncBench>();
			MarkdownExporter.Console.ExportToLog(summary, logger);
			//ConclusionHelper.Print(logger, summary.BenchmarksCases.First().Config.GetCompositeAnalyser().Analyse(summary).ToList());
			_ = Console.ReadLine();
		}
		//public static void Main(string[] args)
		//{
		//  var summary = BenchmarkRunner.Run<Md5VsSha256>();
		//}
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

}