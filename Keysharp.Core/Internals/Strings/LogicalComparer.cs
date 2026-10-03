namespace Keysharp.Internals.Strings
{
	/// <summary>
	/// Locale word order with exact decimal-digit runs and leading-zero ties. Text weights follow the captured user's
	/// culture; ICU/NLS collation tables can differ from those a particular Windows release uses for StrCmpLogicalW.
	/// </summary>
	internal static class LogicalComparer
	{
		private enum TokenKind { BeforeDigits, Digits, AfterDigits }
		private const CompareOptions PrimaryOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

		internal static int Compare(string left, string right)
		{
			if (left == right) return 0;
			if (left == null) return -1;
			if (right == null) return 1;

			var compare = (CaseCompare.UserCulture ?? CultureInfo.CurrentCulture).CompareInfo;
			var leftText = WordText(left);
			var rightText = WordText(right);
			var x = leftText.AsSpan();
			var y = rightText.AsSpan();

			while (!x.IsEmpty && !y.IsEmpty)
			{
				var xKind = Kind(x, compare, out _);
				var yKind = Kind(y, compare, out _);
				if (xKind != yKind) return (int)xKind - (int)yKind;
				var xLength = TokenLength(x, xKind, compare);
				var yLength = TokenLength(y, yKind, compare);
				var result = xKind == TokenKind.Digits ? CompareDigits(x[..xLength], y[..yLength])
					: compare.Compare(x[..xLength], y[..yLength], PrimaryOptions);
				if (result != 0) return result;
				x = x[xLength..];
				y = y[yLength..];
			}

			if (!x.IsEmpty || !y.IsEmpty) return x.IsEmpty ? -1 : 1;

			// Accents and word-sort punctuation break ties after the primary text and numbers, so é2 follows e1.
			var secondary = compare.Compare(leftText, rightText, CompareOptions.IgnoreCase);
			if (secondary != 0) return secondary;
			var punctuation = (left.Length - leftText.Length).CompareTo(right.Length - rightText.Length);
			return punctuation != 0 ? punctuation : compare.Compare(left, right, CompareOptions.IgnoreCase);
		}

		private static string WordText(string text) => text.AsSpan().IndexOfAny('\'', '-') < 0 ? text : text.RemoveAll("'-");

		// Token boundaries depend on this string alone. A fixed text/digit rank preserves punctuation without making
		// numeric '2' < '10' conflict with lexical comparisons against non-decimal number characters such as '²'.
		private static TokenKind Kind(ReadOnlySpan<char> text, CompareInfo compare, out int width)
		{
			var ch = text[0];
			if (ch is >= '0' and <= '9') { width = 1; return TokenKind.Digits; }
			if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') { width = 1; return TokenKind.AfterDigits; }
			var rune = ReadRune(text, out width);
			var category = Rune.GetUnicodeCategory(rune);
			if (category == UnicodeCategory.DecimalDigitNumber) return TokenKind.Digits;
			// A decomposed accent stays with its text, so é and e + combining acute have the same token boundaries.
			if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
				return TokenKind.AfterDigits;
			return compare.Compare(text[..width], "0", PrimaryOptions) < 0 ? TokenKind.BeforeDigits : TokenKind.AfterDigits;
		}

		private static int TokenLength(ReadOnlySpan<char> text, TokenKind kind, CompareInfo compare)
		{
			var length = 0;
			while (length < text.Length && Kind(text[length..], compare, out var width) == kind) length += width;
			return length;
		}

		private static int CompareDigits(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
		{
			var x = SignificantDigits(left, out var xCount, out var xZeros).EnumerateRunes();
			var y = SignificantDigits(right, out var yCount, out var yZeros).EnumerateRunes();
			var significant = xCount.CompareTo(yCount);
			if (significant != 0) return significant;

			while (x.MoveNext() && y.MoveNext())
			{
				var digit = Rune.GetNumericValue(x.Current).CompareTo(Rune.GetNumericValue(y.Current));
				if (digit != 0) return digit;
			}

			return yZeros.CompareTo(xZeros);
		}

		private static ReadOnlySpan<char> SignificantDigits(ReadOnlySpan<char> text, out int count, out int zeros)
		{
			var start = 0;
			count = zeros = 0;
			foreach (var rune in text.EnumerateRunes())
			{
				if (count == 0 && Rune.GetNumericValue(rune) == 0) { zeros++; start += rune.Utf16SequenceLength; }
				else count++;
			}
			return text[start..];
		}

		private static Rune ReadRune(ReadOnlySpan<char> text, out int width)
		{
			if (Rune.DecodeFromUtf16(text, out var rune, out width) == System.Buffers.OperationStatus.Done) return rune;
			width = 1;
			return Rune.ReplacementChar;
		}
	}
}
