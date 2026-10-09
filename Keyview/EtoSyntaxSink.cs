#if !WINDOWS
namespace Keyview;

internal sealed class EtoSyntaxSink : ISyntaxSink
{
	private readonly RichTextArea area;
	private readonly Action<bool> applying;
	internal EtoSyntaxSink(RichTextArea area, Action<bool> applying) { this.area = area; this.applying = applying; }
	public void Style(int start, int endExclusive, SyntaxColor color)
	{
		applying(true);
		try
		{
			area.Buffer.SetForeground(new Range<int>(start, endExclusive - 1), color == SyntaxColor.Default
				? area.TextColor : SyntaxPalette.ToColor(color, SyntaxPalette.IsDark));
		}
		finally { applying(false); }
	}
}

internal static class EtoHighlightExtensions
{
	private sealed class Cache { internal SyntaxHighlightSnapshot Snapshot; internal bool Applying; internal long Generation, Version; }
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RichTextArea, Cache> caches = new ();
	internal static bool IsApplyingStyle(RichTextArea area) => caches.GetOrCreateValue(area).Applying;
	internal static void Invalidate(RichTextArea area)
	{
		var cache = caches.GetOrCreateValue(area);
		cache.Snapshot = null;
		cache.Generation++;
	}

	internal static bool Highlight(this SyntaxHighlighter highlighter, RichTextArea area, Action pump = null, Func<long> currentVersion = null)
	{
		var cache = caches.GetOrCreateValue(area);
		var generation = cache.Generation;
		var version = currentVersion?.Invoke() ?? 0;
		var snapshot = new SyntaxHighlightSnapshot(highlighter, area.Text ?? "");
		var sink = new EtoSyntaxSink(area, applying => cache.Applying = applying);
		// Reinserted text can have lost its native tags even when the source returns to the cached text.
		var previous = cache.Version != version && cache.Snapshot?.Text == snapshot.Text ? null : cache.Snapshot;
		// Clear only the changed window; tags elsewhere follow edits in the native buffer.
		var applied = snapshot.ApplyChangedRange(sink, previous, pump,
			() => cache.Generation == generation && (currentVersion == null || currentVersion() == version));
		cache.Snapshot = applied ? snapshot : null;
		if (applied) cache.Version = version;
		return applied;
	}
}
#endif
