namespace Keysharp.Internals.Window
{
	/// <summary>
	/// The settings a window search matches with, as AutoHotkey's WindowSearchSettings, whose TitleFindFast is
	/// <see cref="TitleMatchModeSpeed"/> here: the searching thread's, or those a WinEvent captured, with the
	/// criteria's ahk_opt over them.
	/// </summary>
	internal readonly record struct WindowSearchSettings(long TitleMatchMode, bool TitleMatchModeSpeed, bool DetectHiddenWindows, bool DetectHiddenText)
	{
		internal static WindowSearchSettings Current
			=> new(ThreadAccessors.A_TitleMatchMode, ThreadAccessors.A_TitleMatchModeSpeed, ThreadAccessors.A_DetectHiddenWindows, ThreadAccessors.A_DetectHiddenText);

		/// <summary>These settings with the ahk_opt of <paramref name="criteria"/> over them.</summary>
		internal WindowSearchSettings With(SearchCriteria criteria)
			=> criteria?.Options is { } options && TryApplyOptions(options, out var applied) ? applied : this;

		/// <summary>
		/// AutoHotkey's ParseOption: these settings with each ahk_opt word applied in turn. False for a word naming no
		/// setting, which leaves a WinTitle matching nothing. Only the character after Hidden or HiddenText is read,
		/// and one other than 0 or 1 leaves the setting as it was, as in AutoHotkey.
		/// </summary>
		internal bool TryApplyOptions(ReadOnlySpan<char> options, out WindowSearchSettings applied)
		{
			applied = this;

			foreach (var range in options.SplitAny(SpaceTabSv))
			{
				var word = options[range];

				if (word.IsEmpty)
					continue;

				// AutoHotkey copies each word into a buffer of 12 characters and rejects one that does not fit.
				if (word.Length >= 12)
					return false;

				if (word is "1" or "2" or "3")
					applied = applied with { TitleMatchMode = word[0] - '0' };
				else if (word.Equals(Keyword_RegEx, StringComparison.OrdinalIgnoreCase))
					applied = applied with { TitleMatchMode = 4L };
				else if (word.Equals(Keyword_Fast, StringComparison.OrdinalIgnoreCase))
					applied = applied with { TitleMatchModeSpeed = true };
				else if (word.Equals(Keyword_Slow, StringComparison.OrdinalIgnoreCase))
					applied = applied with { TitleMatchModeSpeed = false };
				else if (word.StartsWith(Keyword_Hidden, StringComparison.OrdinalIgnoreCase))
				{
					var rest = word[Keyword_Hidden.Length..];
					var text = rest.StartsWith("Text", StringComparison.OrdinalIgnoreCase);

					if (text)
						rest = rest[4..];

					var flag = rest.IsEmpty ? '1' : rest[0];

					if (flag is '0' or '1')
						applied = text ? applied with { DetectHiddenText = flag == '1' } : applied with { DetectHiddenWindows = flag == '1' };
				}
				else
					return false;
			}

			return true;
		}
	}
}
