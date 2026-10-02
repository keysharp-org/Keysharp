namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The holder for a StatusBar control. Parts are 1-based. The semantics are shared; each toolkit's half, the
		/// partial members at the end, is in Windows/StatusBar.cs and Unix/StatusBar.cs.
		/// </summary>
		public partial class StatusBar
		{
			private KeysharpStatusStrip Strip => (KeysharpStatusStrip)Ctrl;

			/// <summary>
			/// Shows an icon in a part and returns its icon handle, which the part owns: replacing the icon or destroying
			/// the window releases it.
			/// </summary>
			public nint SetIcon(object fileName, object iconNumber = null, object partNumber = null)
			{
				if (!fileName.CoerceString(out var filename))
					return 0;

				var iconnumber = ImageHelper.PrepareIconNumber(iconNumber);

				if (!TryPart(partNumber, 3, "SetIcon", out var part))
					return 0;

				if (part >= Strip.Items.Count)
					return (nint)(long)Errors.ErrorOccurred("Failed", 0L);

				var (bmp, source) = ImageHelper.LoadImage(filename, 0, 0, iconnumber);
				(source as IDisposable)?.Dispose();

				if (bmp == null)
					return (nint)(long)Errors.ErrorOccurred("Can't load icon.", null, filename, 0L);

				return SetPartIcon(part, bmp);
			}

			/// <summary>
			/// Divides the bar into parts of the given widths, followed by one that takes the rest of the bar. A part that
			/// remains keeps its text and icon. Returns the bar's window handle.
			/// </summary>
			public long SetParts(params object[] widths)
			{
				var parts = new List<int>(widths.Length);

				for (var i = 0; i < widths.Length; i++)
				{
					if (widths[i] is null)
						continue;

					if (!widths[i].CoerceInt(out var width))
						return 0L;

					if (width < 0)
						return (long)Errors.InvalidParameterErrorOccurred(i + 1, "Gui.StatusBar.Prototype.SetParts", widths[i], 0L);

					parts.Add(width);
				}

				SetPartWidths(parts);
				return Hwnd;
			}

			/// <summary>
			/// Sets a part's text, and its border when a style is given: 0 sunken, 1 none, 2 raised, and returns true. A
			/// part the bar does not have fails, as in AutoHotkey.
			/// </summary>
			public bool SetText(object newText, object partNumber = null, object style = null)
			{
				if (!newText.CoerceString(out var text) || !TryPart(partNumber, 2, "SetText", out var part) || !style.CoerceLong(out var s, -1))
					return false;

				if (part >= Strip.Items.Count)
					return (bool)Errors.ErrorOccurred("Failed", false);

				SetPartText(part, text, s);
				return true;
			}

			//A part number runs from 1 to 256, as in AutoHotkey; the 0-based index comes back.
			private static bool TryPart(object partNumber, int position, string method, out int part)
			{
				part = 0;

				if (!partNumber.CoerceInt(out var number, 1))
					return false;

				if (number is < 1 or > 256)
				{
					_ = Errors.InvalidParameterErrorOccurred(position, $"Gui.StatusBar.Prototype.{method}", partNumber);
					return false;
				}

				part = number - 1;
				return true;
			}

			//Each toolkit's half. Part indexes here are 0-based and exist.

			/// <summary>Shows the bitmap, which the part takes ownership of, and returns its icon handle or 0.</summary>
			private partial nint SetPartIcon(int part, Bitmap bitmap);

			/// <summary>Makes the parts the given widths plus a last one filling the rest, reusing those that remain.</summary>
			private partial void SetPartWidths(List<int> widths);

			/// <summary>Sets a part's text, and its border unless the style is -1.</summary>
			private partial void SetPartText(int part, string text, long style);
		}
	}
}
