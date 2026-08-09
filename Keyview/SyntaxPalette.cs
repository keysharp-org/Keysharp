namespace Keyview
{
	/// <summary>The one place a <see cref="SyntaxColor"/> becomes an RGB value, so the two editors cannot drift.</summary>
	internal static class SyntaxPalette
	{
		// 0xRRGGBB. Light theme.
		internal const int Comment = 0x008000;   // green
		internal const int String = 0xA3150D;   // dark red
		internal const int Number = 0x556B2F;   // dark olive green
		internal const int Keyword = 0x0000FF;   // blue
		internal const int Builtin = 0x3492B8;   // turquoise
		internal const int Method = 0x795E26;   // brown/gold - function & method calls
		internal const int Property = 0x63298D;   // purple - member/property access
		internal const int Key = 0xCC6600;   // dark orange - hotkey/remap keys ("::" stays default)

		internal static int ToRgb(SyntaxColor color) => color switch
		{
			SyntaxColor.Comment => Comment,
			SyntaxColor.String => String,
			SyntaxColor.Number => Number,
			SyntaxColor.Keyword => Keyword,
			SyntaxColor.Builtin => Builtin,
			SyntaxColor.Method => Method,
			SyntaxColor.Property => Property,
			SyntaxColor.Key => Key,
			_ => 0x000000,
		};

#if !WINDOWS
		internal static Color ToEto(SyntaxColor color)
		{
			var rgb = ToRgb(color);
			return Color.FromArgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
		}
#endif
	}
}
