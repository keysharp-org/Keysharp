namespace Keyview;

internal readonly record struct SyntaxSpan(int Start, int End, SyntaxColor Color);

internal sealed class SyntaxHighlightSnapshot : ISyntaxSink
{
	internal List<SyntaxSpan> Spans { get; } = [];

	internal string Text { get; }

	internal SyntaxHighlightSnapshot(SyntaxHighlighter highlighter, string text)
	{
		Text = text;
		highlighter.Highlight(this, text);
	}

	public void Style(int start, int endExclusive, SyntaxColor color)
	{
		if (Spans.Count > 0)
		{
			var previous = Spans[^1];
			if (previous.Color == color && Text.AsSpan(previous.End, start - previous.End).Trim().IsEmpty)
			{
				Spans[^1] = previous with { End = endExclusive };
				return;
			}
		}

		Spans.Add(new SyntaxSpan(start, endExclusive, color));
	}

	internal bool ApplyChangedRange(ISyntaxSink sink, SyntaxHighlightSnapshot previous, Action pump = null, Func<bool> isCurrent = null)
	{
		if (isCurrent?.Invoke() == false)
			return false;
		var (start, end) = ChangedRange(previous);
		if (end <= start)
			return true;
		sink.Style(start, end, SyntaxColor.Default);
		var applied = 0;
		for (var i = FirstSpan(start); i < Spans.Count && Spans[i].Start < end; i++)
		{
			if (isCurrent?.Invoke() == false)
				return false;
			var span = Spans[i];
			sink.Style(Math.Max(start, span.Start), Math.Min(end, span.End), span.Color);
			if (pump != null && ++applied >= 512)
			{
				applied = 0;
				pump();
				if (isCurrent?.Invoke() == false)
					return false;
			}
		}

		return isCurrent?.Invoke() != false;
	}

	internal (int Start, int End) ChangedRange(SyntaxHighlightSnapshot previous)
	{
		if (previous == null)
			return (0, Text.Length);
		var prefix = 0;
		while (prefix < Text.Length && prefix < previous.Text.Length && Text[prefix] == previous.Text[prefix])
			prefix++;
		var suffix = 0;
		while (suffix < Text.Length - prefix && suffix < previous.Text.Length - prefix
			&& Text[Text.Length - suffix - 1] == previous.Text[previous.Text.Length - suffix - 1])
			suffix++;

		var start = prefix;
		var end = Text.Length - suffix;
		var common = 0;
		while (common < Spans.Count && common < previous.Spans.Count && SameSpan(Spans[common], previous, previous.Spans[common], 0))
			common++;
		var newEnd = Spans.Count;
		var oldEnd = previous.Spans.Count;
		var shift = Text.Length - previous.Text.Length;
		while (newEnd > common && oldEnd > common && SameSpan(Spans[newEnd - 1], previous, previous.Spans[oldEnd - 1], shift))
		{ newEnd--; oldEnd--; }

		if (common < newEnd)
		{ start = Math.Min(start, Spans[common].Start); end = Math.Max(end, Spans[newEnd - 1].End); }

		if (common < oldEnd)
		{
			start = Math.Min(start, previous.Spans[common].Start);
			end = Math.Max(end, previous.Spans[oldEnd - 1].End + shift);
		}

		start = Math.Clamp(start, 0, Text.Length);
		end = Math.Clamp(end, start, Text.Length);
		if (start == end && Text == previous.Text && common == Spans.Count && common == previous.Spans.Count)
			return (0, 0);
		// Neighbor-dependent identifier colors can change across the edited token's boundary.
		var first = FirstSpan(start);
		if (first > 0)
			start = Math.Min(start, Spans[first - 1].Start);
		var last = FirstSpan(end);
		if (last < Spans.Count)
			end = Math.Max(end, Spans[last].End);
		return (start, end);
	}

	internal int FirstSpan(int position)
	{
		int low = 0, high = Spans.Count;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (Spans[middle].End <= position)
				low = middle + 1;
			else
				high = middle;
		}

		return low;
	}

	private bool SameSpan(SyntaxSpan span, SyntaxHighlightSnapshot previous, SyntaxSpan old, int shift) =>
		span.Start == old.Start + shift && span.End == old.End + shift && span.Color == old.Color
		&& Text.AsSpan(span.Start, span.End - span.Start).SequenceEqual(previous.Text.AsSpan(old.Start, old.End - old.Start));
}