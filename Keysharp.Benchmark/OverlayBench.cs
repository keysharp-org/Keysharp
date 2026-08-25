using BenchmarkDotNet.Order;

namespace Keysharp.Benchmark;

/// <summary>
/// Draw-path cost for a hidden <see cref="Ks.KeysharpOverlay"/>. Presentation is main-thread-affine and is
/// measured by <c>Keysharp/Scripts/Benchmarks/overlay-present.ks</c> instead.
/// </summary>
[Orderer(SummaryOrderPolicy.Declared)]
public class OverlayBench : BaseTest
{
	private const int FillsPerFrame = 800;

	[Params("1200x800", "2560x1440", "2880x1800")]
	public string Surface { get; set; } = "1200x800";

	private Ks.KeysharpOverlay hidden = null!;
	private long width;
	private long height;

	[GlobalSetup]
	public void Setup()
	{
		var parts = Surface.Split('x');
		width = long.Parse(parts[0]);
		height = long.Parse(parts[1]);
		hidden = new Ks.KeysharpOverlay();
		_ = hidden.__New(0L, 0L, width, height);
		_ = hidden.Canvas.Clear("0x40102030");
	}

	[GlobalCleanup]
	public void Cleanup() => _ = hidden?.Destroy();

	[Benchmark(Baseline = true)]
	public void Clear() => _ = hidden.Canvas.Clear("0x40102030");

	[Benchmark]
	public void FillRectOnce() => _ = hidden.Canvas.FillRect(20L, 20L, 200L, 40L, "0xFF3060A0");

	[Benchmark]
	public void Frame()
	{
		_ = hidden.Canvas.Clear("0x40102030");
		FillMany(hidden);
	}

	[Benchmark]
	public void DrawTextOnce() => _ = hidden.Canvas.DrawText("Wave 12", 40L, 40L, "0xFFFFFFFF", "s16 bold");

	private void FillMany(Ks.KeysharpOverlay target)
	{
		const long cols = 40L;
		var cellWidth = width / cols;
		var cellHeight = height / (FillsPerFrame / cols);

		for (long i = 0; i < FillsPerFrame; i++)
		{
			var x = (i % cols) * cellWidth;
			var y = (i / cols) * cellHeight;
			_ = target.Canvas.FillRect(x, y, cellWidth - 1, cellHeight - 1, 0xFF3060A0L + (i & 0x3F));
		}
	}
}
