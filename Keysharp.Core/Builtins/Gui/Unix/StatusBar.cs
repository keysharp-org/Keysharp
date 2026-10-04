#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.StatusBar"/>: each part is a row of labels, which a change of text or icon updates
		/// in place and a change of parts lays out again. There are no icon handles off Windows, so SetIcon returns 0.
		/// </summary>
		public partial class StatusBar
		{
			private partial nint SetPartIcon(int part, Bitmap bitmap)
			{
				var item = Strip.Items[part];
				var old = item.Image;
				item.Image = bitmap;
				Strip.UpdatePart(part);
				old?.Dispose();
				return 0;
			}

			private partial void SetPartWidths(List<int> widths)
			{
				var items = Strip.Items;
				var count = widths.Count + 1;

				//As in AutoHotkey, the parts that remain keep their text and icon, and only those removed release theirs.
				while (items.Count > count)
				{
					var removed = items[items.Count - 1];
					items.RemoveAt(items.Count - 1);
					removed.Image?.Dispose();
				}

				for (var i = 0; i < count; i++)
				{
					if (i == items.Count)
						items.Add(new KeysharpToolStripStatusLabel());

					var part = items[i];
					var last = i == count - 1;
					part.AutoSize = last;
					part.Spring = last;
					part.Width = last ? -1 : widths[i];
				}

				Strip.UpdateItems();
			}

			private partial void SetPartText(int part, string text, long style)
			{
				var item = Strip.Items[part];
				item.Text = text;

				if (style >= 0)
					item.Style = (int)style;

				Strip.UpdatePart(part);
			}
		}
	}
}
#endif
