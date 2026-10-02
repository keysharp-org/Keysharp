#if WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>The WinForms half of <see cref="Gui.StatusBar"/>: each part is a status strip label.</summary>
		public partial class StatusBar
		{
			private partial nint SetPartIcon(int part, Bitmap bitmap) => ((KeysharpToolStripStatusLabel)Strip.Items[part]).SetIcon(bitmap);

			private partial void SetPartWidths(List<int> widths)
			{
				var items = Strip.Items;
				var count = widths.Count + 1;

				//As in AutoHotkey, the parts that remain keep their text and icon, and only those removed release theirs.
				//Each is out of the strip before it is disposed, since disposing a part removes it from its strip.
				while (items.Count > count)
				{
					var removed = items[items.Count - 1];
					items.RemoveAt(items.Count - 1);
					removed.Dispose();
				}

				for (var i = 0; i < count; i++)
				{
					if (i == items.Count)
						_ = items.Add(new KeysharpToolStripStatusLabel { Alignment = ToolStripItemAlignment.Left });

					if (items[i] is ToolStripStatusLabel part)
					{
						var last = i == count - 1;
						part.AutoSize = last;
						part.Spring = last;

						if (!last)
							part.Width = widths[i];
					}
				}
			}

			private partial void SetPartText(int part, string text, long style)
			{
				var item = Strip.Items[part];
				item.Text = text;

				if (item is ToolStripStatusLabel label)
				{
					if (style == 0)
					{
						label.BorderStyle = Border3DStyle.Sunken;
						label.BorderSides = ToolStripStatusLabelBorderSides.All;
					}
					else if (style == 1)
					{
						label.BorderStyle = Border3DStyle.Flat;
						label.BorderSides = ToolStripStatusLabelBorderSides.None;
					}
					else if (style == 2)
					{
						label.BorderStyle = Border3DStyle.Raised;
						label.BorderSides = ToolStripStatusLabelBorderSides.All;
					}
				}
			}
		}
	}
}
#endif
