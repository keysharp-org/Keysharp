using static Keysharp.Scripting.Script;

namespace Keysharp.Benchmark
{
	/// <summary>
	/// Duplicate of GenericExtensions.GetOrAdd<K, V>().
	/// </summary>
	public static class DictionaryExtensions
	{
		internal static V GetOrAdd<K, V>(this IDictionary<K, V> dictionary, K k, Func<V> constructionFunc)
		{
			if (!dictionary.TryGetValue(k, out var val))
				dictionary.Add(k, val = constructionFunc());

			return val;
		}
	}

	public class MapReadBenchmark : BaseTest
	{
		private Dictionary<object, object> dkt = [];
		private Map map = null!, mapScript = null!;
		private List<string> strings = [];

		[Params(100000)]
		public int Size { get; set; }

		public MapReadBenchmark()
		{
			map = new Keysharp.Core.Map();
			mapScript = new Keysharp.Core.Map();
		}

		[Benchmark]
		public void MapRead()
		{
			foreach (var s in strings)
				_ = map![s];
		}

		[Benchmark]
		public void MapScriptIndexRead()
		{
			foreach (var s in strings)
				_ = Index(mapScript, s);
		}

		[Benchmark(Baseline = true)]
		public void NativeDictionaryTryGet()
		{
			foreach (var s in strings)
				_ = dkt.TryGetValue(s, out _);
		}

		[GlobalSetup]
		public void Setup()
		{
			for (var i = 0; i < Size; i++)
			{
				var s = i.ToString();
				map[s] = i;
				mapScript[s] = i;
				dkt[s] = i;
				strings.Add(s);
			}
		}
	}

	public class MapWriteBenchmark : BaseTest
	{
		private readonly Dictionary<object, object> dkt = [];
		private readonly Map map = null!, mapScript = null!;
		private readonly List<string> strings = [];

		[Params(100000)]
		public int Size { get; set; }

		public MapWriteBenchmark()
		{
			map = new Keysharp.Core.Map();
			mapScript = new Keysharp.Core.Map();
		}

		[Benchmark]
		public void MapWrite()
		{
			map.Clear();

			for (var i = 0; i < Size; i++)
				map[strings[i]] = i;
		}

		[Benchmark]
		public void MapScriptSetObjectWrite()
		{
			mapScript!.Clear();

			for (var i = 0; i < Size; i++)
				_ = SetObject(mapScript, i, strings[i]);
		}

		[Benchmark(Baseline = true)]
		public void NativeDictionaryWrite()
		{
			dkt.Clear();

			for (var i = 0; i < Size; i++)
				dkt[strings[i]] = i;
		}

		[GlobalSetup]
		public void Setup()
		{
			for (var i = 0; i < Size; i++)
				strings.Add(i.ToString());
		}
	}

	public class NestedMapPopulatorBenchmark : BaseTest
	{
		private List<string> strings = [];
		private Dictionary<string, object> masterDkt = [];

		[Params(3)]
		public int StringLength { get; set; }

		public NestedMapPopulatorBenchmark()
		{
			masterDkt = [];
			strings = [];
		}

		[Benchmark]
		public void PopulateNestedMap()
		{
			masterDkt = [];

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				var dkt1 = (Dictionary<string, object>)masterDkt.GetOrAdd(c1.ToString(), () => new Dictionary<string, object>());

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					var dkt2 = (Dictionary<string, object>)dkt1.GetOrAdd(c2.ToString(), () => new Dictionary<string, object>());

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						var dkt3 = (Dictionary<string, object>)dkt2.GetOrAdd(c3.ToString(), () => new Dictionary<string, object>());
					}
				}
			}
		}

		[Benchmark(Baseline = true)]
		public void PopulateList()
		{
			strings = new List<string>((int)Math.Pow(26, 3));

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				strings.Add(c1.ToString());

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					strings.Add(c1.ToString() + c2);

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						strings.Add(c1.ToString() + c2.ToString() + c3);
					}
				}
			}
		}
	}

	public class NestedMapRetrieverBenchmark : BaseTest
	{
		private List<string> strings = [];
		private Dictionary<string, object> masterDkt = [];

		[Params(3)]
		public int StringLength { get; set; }

		public NestedMapRetrieverBenchmark()
		{
		}

		[Benchmark]
		public void RetrieveFromNestedMap()
		{
			var foundCt = 0;
			var str = "";

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				str = c1.ToString();

				if (Retrieve(masterDkt, str))
					foundCt++;

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					if (Retrieve(masterDkt, str + c2))
						foundCt++;

					str = c1.ToString() + c2;

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						if (Retrieve(masterDkt, str + c3.ToString()))
							foundCt++;
					}
				}
			}

			//Console.WriteLine($"RetrieveFromNestedMap() foundCt: {foundCt}.");
		}

		[Benchmark(Baseline = true)]
		public void RetrieveFromList()
		{
			var foundCt = 0;
			var str = "";

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				str = c1.ToString();

				if (strings.IndexOf(str) != -1)
					foundCt++;

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					if (strings.IndexOf(str + c2) != -1)
						foundCt++;

					str = c1.ToString() + c2;

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						if (strings.IndexOf(str + c3) != -1)
							foundCt++;
					}
				}
			}

			//Console.WriteLine($"RetrieveFromList() foundCt: {foundCt}.");
		}

		[GlobalSetup]
		public void Setup()
		{
			masterDkt = [];
			strings = new List<string>((int)Math.Pow(26, 3));

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				strings.Add(c1.ToString());

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					strings.Add(c1.ToString() + c2);

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						strings.Add(c1.ToString() + c2.ToString() + c3);
					}
				}
			}

			for (var c1 = 'a'; c1 < 'z'; c1++)
			{
				var dkt1 = (Dictionary<string, object>)masterDkt.GetOrAdd(c1.ToString(), () => new Dictionary<string, object>());

				for (var c2 = 'a'; c2 < 'z'; c2++)
				{
					var dkt2 = (Dictionary<string, object>)dkt1.GetOrAdd(c2.ToString(), () => new Dictionary<string, object>());

					for (var c3 = 'a'; c3 < 'z'; c3++)
					{
						var dkt3 = (Dictionary<string, object>)dkt2.GetOrAdd(c3.ToString(), () => new Dictionary<string, object>());
					}
				}
			}
		}
		private static bool Retrieve(Dictionary<string, object> dkt, string str)
		{
			foreach (var c in str)
			{
				if (dkt.TryGetValue(c.ToString(), out var newDkt))
				{
					if (newDkt is Dictionary<string, object> tempDkt)
					{
						dkt = tempDkt;
					}
					else
						return false;
				}
				else
					return false;
			}

			return true;
		}
	}
}