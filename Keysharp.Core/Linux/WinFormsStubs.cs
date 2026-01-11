#if LINUX
namespace System.Windows.Forms
{
	public enum DockStyle
	{
		None = 0,
		Top = 1,
		Bottom = 2,
		Left = 3,
		Right = 4,
		Fill = 5
	}

	public enum HorizontalAlignment
	{
		Left = 0,
		Center = 1,
		Right = 2
	}

	public class ColumnHeader
	{
		public string Text { get; set; } = "";
		public int Width { get; set; } = -1;
		public HorizontalAlignment TextAlign { get; set; } = HorizontalAlignment.Left;
		public int ImageIndex { get; set; } = -1;
		public object Tag { get; set; }
		internal int Index { get; set; } = -1;
	}

	public class ColumnHeaderCollection : Collection<ColumnHeader>
	{
		protected override void InsertItem(int index, ColumnHeader item)
		{
			base.InsertItem(index, item);
			RefreshIndices();
		}

		protected override void SetItem(int index, ColumnHeader item)
		{
			base.SetItem(index, item);
			RefreshIndices();
		}

		protected override void RemoveItem(int index)
		{
			base.RemoveItem(index);
			RefreshIndices();
		}

		protected override void ClearItems()
		{
			base.ClearItems();
			RefreshIndices();
		}

		public void AddRange(IEnumerable<ColumnHeader> items)
		{
			if (items == null)
				return;
			foreach (var item in items)
				Add(item);
		}

		private void RefreshIndices()
		{
			for (var i = 0; i < Count; i++)
				this[i].Index = i;
		}
	}

	public sealed class LinkLabelLinkClickedEventArgs : EventArgs
	{
	}

	public struct Message
	{
		public nint HWnd;
		public int Msg;
		public nint WParam;
		public nint LParam;
		public nint Result;
	}
}
#endif
