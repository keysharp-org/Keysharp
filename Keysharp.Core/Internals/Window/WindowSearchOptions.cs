namespace Keysharp.Internals.Window
{
	internal sealed class WindowSearchOptions
	{
		internal static readonly WindowSearchOptions Empty = new();

		internal long? TitleMatchMode { get; set; }
		internal bool? TitleMatchModeSpeed { get; set; }
		internal bool? DetectHiddenWindows { get; set; }
		internal bool? DetectHiddenText { get; set; }
		internal bool IsEmpty => TitleMatchMode == null && TitleMatchModeSpeed == null && DetectHiddenWindows == null && DetectHiddenText == null;

		internal static WindowSearchOptions Merge(WindowSearchOptions primary, WindowSearchOptions fallback = null)
		{
			if (primary == null || primary.IsEmpty)
				return fallback == null || fallback.IsEmpty ? Empty : fallback;

			if (fallback == null || fallback.IsEmpty)
				return primary;

			return new WindowSearchOptions
			{
				TitleMatchMode = primary?.TitleMatchMode ?? fallback?.TitleMatchMode,
				TitleMatchModeSpeed = primary?.TitleMatchModeSpeed ?? fallback?.TitleMatchModeSpeed,
				DetectHiddenWindows = primary?.DetectHiddenWindows ?? fallback?.DetectHiddenWindows,
				DetectHiddenText = primary?.DetectHiddenText ?? fallback?.DetectHiddenText
			};
		}

		internal bool ApplyToken(string token)
		{
			switch (token)
			{
				case "1":
					TitleMatchMode = 1L;
					return true;

				case "2":
					TitleMatchMode = 2L;
					return true;

				case "3":
					TitleMatchMode = 3L;
					return true;

				case var x when x.Equals(Keyword_RegEx, StringComparison.OrdinalIgnoreCase):
					TitleMatchMode = 4L;
					return true;

				case var x when x.Equals(Keyword_Fast, StringComparison.OrdinalIgnoreCase):
					TitleMatchModeSpeed = true;
					return true;

				case var x when x.Equals(Keyword_Slow, StringComparison.OrdinalIgnoreCase):
					TitleMatchModeSpeed = false;
					return true;

				case var x when x.Equals(Keyword_Hidden, StringComparison.OrdinalIgnoreCase) ||
					x.Equals("hidden1", StringComparison.OrdinalIgnoreCase):
					DetectHiddenWindows = true;
					return true;

				case var x when x.Equals("hidden0", StringComparison.OrdinalIgnoreCase):
					DetectHiddenWindows = false;
					return true;

				case var x when x.Equals("hiddentext", StringComparison.OrdinalIgnoreCase) ||
					x.Equals("hiddentext1", StringComparison.OrdinalIgnoreCase):
					DetectHiddenText = true;
					return true;

				case var x when x.Equals("hiddentext0", StringComparison.OrdinalIgnoreCase):
					DetectHiddenText = false;
					return true;
			}

			return false;
		}
	}
}
