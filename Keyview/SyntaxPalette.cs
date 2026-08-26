namespace Keyview
{
	/// <summary>The one place a <see cref="SyntaxColor"/> becomes an RGB value, so the two editors cannot drift.</summary>
	internal static class SyntaxPalette
	{
		internal static bool IsDark
		{
			get => Luminance(EditorBackground) < Luminance(EditorForeground);
		}

		internal static int EditorBackground => SystemColorRgb(
#if WINDOWS
			SystemColors.Window
#else
			SystemColors.ControlBackground
#endif
		);

		internal static int EditorForeground => SystemColorRgb(
#if WINDOWS
			SystemColors.WindowText
#else
			SystemColors.ControlText
#endif
		);

		internal static int SelectionBackground => SystemColorRgb(
#if WINDOWS
			SystemColors.Highlight
#else
			SystemColors.Selection
#endif
		);

		internal static int SelectionForeground => SystemColorRgb(
#if WINDOWS
			SystemColors.HighlightText
#else
			SystemColors.SelectionText
#endif
		);

		internal static int Caret => EditorForeground;
		internal static int CaretLine => IsDark ? 0x2A2D2E : 0xF0F8FF;
		internal static int MarginBackground => IsDark ? 0x252526 : 0xEEEEEE;
		internal static int MarginForeground => 0x858585;
		internal static int StringEolBackground => IsDark ? 0x5A1D1D : 0xFFC0CB;
		internal static int Preprocessor => IsDark ? 0xC586C0 : 0x808080;
		internal static int StatusSuccess => IsDark ? 0x6A9955 : 0x008000;
		internal static int StatusError => IsDark ? 0xF48771 : 0xFF0000;

		internal static int ToRgb(SyntaxColor color, bool isDark) => color switch
		{
			SyntaxColor.Comment => isDark ? 0x6A9955 : 0x008000,
			SyntaxColor.String => isDark ? 0xCE9178 : 0xA3150D,
			SyntaxColor.Number => isDark ? 0xB5CEA8 : 0x556B2F,
			SyntaxColor.Keyword => isDark ? 0x569CD6 : 0x0000FF,
			SyntaxColor.Builtin => isDark ? 0x4EC9B0 : 0x3492B8,
			SyntaxColor.Method => isDark ? 0xDCDCAA : 0x795E26,
			SyntaxColor.Property => isDark ? 0x9CDCFE : 0x63298D,
			SyntaxColor.Key => isDark ? 0xD7BA7D : 0xCC6600,
			_ => EditorForeground,
		};

		internal static Color ToColor(SyntaxColor color, bool isDark) => ToColor(ToRgb(color, isDark));

		internal static Color ToColor(int rgb) =>
			Color.FromArgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

		private static int Luminance(int rgb) =>
			((rgb >> 16) & 0xff) * 299 + ((rgb >> 8) & 0xff) * 587 + (rgb & 0xff) * 114;

		private static int SystemColorRgb(Color color) => color.ToArgb() & 0xFFFFFF;
	}
}
