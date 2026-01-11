#if WINDOWS
namespace Keysharp.Core
{
	internal static class ListViewHelper
	{
		internal static long AddOrInsertListViewItem(ListView lv, ListViewOptions lvo, List<string> strs, int insert)
		{
			var item = new ListViewItem();

			while (item.SubItems.Count < lv.Columns.Count)
				_ = item.SubItems.Add("");

			for (int i = 0, j = lvo.colstart; i < strs.Count && j < item.SubItems.Count; i++, j++)
				item.SubItems[j].Text = strs[i];

			var row = lv.Items.Count + 1;
			_ = insert >= 0 ? lv.Items.Insert(row = Math.Min(insert, lv.Items.Count), item) : lv.Items.Add(item);
			ApplyListViewOptions(lv, item, lvo);


			return row;
		}

		internal static void ApplyListViewOptions(ListView lv, ListViewItem item, ListViewOptions lvo)
		{
			item.Focused = lvo.focused;
			item.Selected = lvo.select;
			item.Checked = lvo.ischecked;

			if (lvo.vis)
				item.EnsureVisible();

			if (lvo.icon >= 0 && lv.SmallImageList != null && lvo.icon < lv.SmallImageList.Images.Count)
				item.ImageIndex = lvo.icon;
		}

		internal static void ParseAndApplyListViewColumnOptions(ColumnHeader col, string options)
		{
			var lvco = ParseListViewColumnOptions(options);
			var lv = col.ListView as KeysharpListView;

			lv.BeginUpdate();

			if (lvco.width.HasValue)
			{
				var scale = !lv.GetGuiControl().DpiScaling ? 1.0 : A_ScaledScreenDPI;
				col.Width = (int)(lvco.width.Value * scale);
			}

			if (lvco.auto.HasValue)
				lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

			if (lvco.autohdr.HasValue)
				lv.SetListViewColumnSizes();

			if (lvco.icon.HasValue)
				col.ImageIndex = lvco.icon.Value == 0 ? -1 : lvco.icon.Value;

#if WINDOWS

			if (lvco.iconright.HasValue)
			{
				var colflags = new LV_COLUMN();
				colflags.mask = WindowsAPI.LVCF_FMT;
				_ = WindowsAPI.SendLVColMessage(lv.Handle, WindowsAPI.LVM_GETCOLUMN, (uint)col.Index, ref colflags);
				// Set the new format flags
				colflags.mask = WindowsAPI.LVCF_FMT | WindowsAPI.LVCF_IMAGE;

				if (lvco.iconright.Value)
					colflags.fmt |= WindowsAPI.LVCFMT_IMAGE | WindowsAPI.LVCFMT_BITMAP_ON_RIGHT;
				else
					colflags.fmt |= WindowsAPI.LVCFMT_IMAGE & ~WindowsAPI.LVCFMT_BITMAP_ON_RIGHT;

				_ = WindowsAPI.SendLVColMessage(lv.Handle, WindowsAPI.LVM_SETCOLUMN, (uint)col.Index, ref colflags);
			}

#endif

			if (lvco.inttype.HasValue)
			{
				lv.ListViewItemSorter = lvco.inttype.Value ? new ListViewIntegerComparer(col) : new ListViewCaseInsensitiveComparer(col);
				col.TextAlign = HorizontalAlignment.Right;
			}

			if (lvco.floattype.HasValue)
			{
				lv.ListViewItemSorter = lvco.floattype.Value ? new ListViewFloatComparer(col) : new ListViewCaseInsensitiveComparer(col);
				col.TextAlign = HorizontalAlignment.Right;
			}

			if (lvco.texttype.IsTrue())
				lv.ListViewItemSorter = new ListViewCaseInsensitiveComparer(col);

			//Note that due to a limitation in the underlying control, the first column will always be left aligned.
			if (lvco.center.HasValue)//These will also change the alignment of the header, but unsure how to separate the two.
				col.TextAlign = lvco.center.Value ? HorizontalAlignment.Center : HorizontalAlignment.Left;

			if (lvco.left.HasValue)
				col.TextAlign = lvco.left.Value ? HorizontalAlignment.Left : HorizontalAlignment.Right;

			if (lvco.right.HasValue)
				col.TextAlign = lvco.right.Value ? HorizontalAlignment.Right : HorizontalAlignment.Left;

			if (lvco.casesensitive.IsTrue())
				lv.ListViewItemSorter = new ListViewCaseSensitiveComparer(col);
			else if (lvco.caselocale.IsTrue())
				lv.ListViewItemSorter = new ListViewCaseLocaleComparer(col);
			else if (lvco.logical.IsTrue())
				lv.ListViewItemSorter = new ListViewLogicalComparer(col);
			else if (lvco.sort.IsTrue() || lvco.sortdesc.IsTrue())
				lv.ListViewItemSorter = new ListViewCaseInsensitiveComparer(col);

			if (lvco.desc.HasValue)	
				lv.Sorting = lvco.desc.Value ? SortOrder.Descending : SortOrder.Ascending;

			if (lvco.nosort.HasValue)
				lv.Sorting = lvco.nosort.Value ? SortOrder.None : SortOrder.Descending;

			if (lvco.sort.IsTrue())
				lv.Sorting = SortOrder.Ascending;

			if (lvco.sortdesc.IsTrue())
				lv.Sorting = SortOrder.Descending;

			if (lvco.uni.HasValue)
				lv.uni = lvco.uni.Value;

			lv.EndUpdate();
		}

		internal static ListViewColumnOptions ParseListViewColumnOptions(string options)
		{
			var lvco = new ListViewColumnOptions();

			foreach (Range r in options.AsSpan().SplitAny(Spaces))
			{
				var opt = options.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					var temp = 0;
					var b = false;

					if (int.TryParse(opt, out var width)) { lvco.width = width; }
					else if (Options.TryParse(opt, "AutoHdr", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.autohdr = b; }
					else if (Options.TryParse(opt, "Auto", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.auto = b; }
					else if (opt.Equals("-Icon", StringComparison.OrdinalIgnoreCase)) { lvco.icon = 0; }
					else if (Options.TryParse(opt, "IconRight", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.iconright = b; }
					else if (Options.TryParse(opt, "Icon", ref temp)) { lvco.icon = temp; }
					else if (Options.TryParse(opt, "Float", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.floattype = b; }
					else if (Options.TryParse(opt, "Integer", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.inttype = b; }
					else if (Options.TryParse(opt, "Text", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.texttype = b; }
					else if (Options.TryParse(opt, "Center", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.center = b; }
					else if (Options.TryParse(opt, "Left", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.left = b; }
					else if (Options.TryParse(opt, "Right", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.right = b; }
					else if (Options.TryParse(opt, "CaseLocale", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.caselocale = b; }
					else if (Options.TryParse(opt, "Case", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.casesensitive = b; }
					else if (Options.TryParse(opt, "Desc", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.desc = b; }
					else if (Options.TryParse(opt, "Logical", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.logical = b; }
					else if (Options.TryParse(opt, "NoSort", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.nosort = b; }
					else if (Options.TryParse(opt, "SortDesc", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.sortdesc = b; }
					else if (Options.TryParse(opt, "Sort", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.sort = b; }
					else if (Options.TryParse(opt, "Uni", ref b, StringComparison.OrdinalIgnoreCase, true, true)) { lvco.uni = b; }
				}
			}

			return lvco;
		}

		internal static ListViewOptions ParseListViewOptions(string options)
		{
			var lvo = new ListViewOptions();

			foreach (Range r in options.AsSpan().SplitAny(Spaces))
			{
				var opt = options.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					var temp = 0;

					if (Options.TryParse(opt, "Check", ref lvo.ischecked, StringComparison.OrdinalIgnoreCase, true, true)) { }
					else if (Options.TryParse(opt, "Focus", ref lvo.focused, StringComparison.OrdinalIgnoreCase, true, true)) { }
					else if (Options.TryParse(opt, "Select", ref lvo.select, StringComparison.OrdinalIgnoreCase, true, true)) { }
					else if (Options.TryParse(opt, "Col", ref temp)) { lvo.colstart = temp - 1; }
					else if (Options.TryParse(opt, "Icon", ref lvo.icon)) { }
					else if (opt.Equals("Vis", StringComparison.OrdinalIgnoreCase)) { lvo.vis = true; }
				}
			}

			return lvo;
		}

		internal static void SetListViewColumnSizes(this ListView lv, int width = -2)
		{
			foreach (ColumnHeader col in lv.Columns)
				col.Width = width;
		}

		internal class ListViewColumnOptions
		{
			public bool? auto;
			public bool? autohdr;
			public bool? caselocale;
			public bool? casesensitive;
			public bool? center;
			public bool? desc;
			public bool? floattype;
			public int? icon;
			public bool? iconright;
			public bool? inttype;
			public bool? left;
			public bool? logical;
			public bool? nosort;
			public bool? right;
			public bool? sort;
			public bool? sortdesc;
			public bool? texttype;
			public bool? uni;
			public int? width;
		}

		internal abstract class ListViewComparer : IComparer
		{
			protected ColumnHeader col;

			public ListViewComparer()
			{
			}

			public ListViewComparer(ColumnHeader column) => col = column;

			public abstract int Compare(object x, object y);

			public int SortOrder() => col.ListView.Sorting == System.Windows.Forms.SortOrder.Descending ? -1 : 1;
		}

		internal class ListViewOptions
		{
			public int colstart = 0;
			public bool focused = false;
			public int icon = int.MinValue;
			public bool ischecked = false;
			public bool select = false;
			public bool vis = false;
		}

		private class ListViewCaseInsensitiveComparer : ListViewComparer
		{
			public ListViewCaseInsensitiveComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r) =>
			l is ListViewItem x&& r is ListViewItem y&& col.Index < x.SubItems.Count&& col.Index < y.SubItems.Count
			? SortOrder()* string.Compare(x.SubItems[col.Index].Text, y.SubItems[col.Index].Text, true)
			: 0;
		}

		private class ListViewCaseLocaleComparer : ListViewComparer
		{
			public ListViewCaseLocaleComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r) =>
			l is ListViewItem x&& r is ListViewItem y&& col.Index < x.SubItems.Count&& col.Index < y.SubItems.Count
			? SortOrder()* string.Compare(x.SubItems[col.Index].Text, y.SubItems[col.Index].Text, StringComparison.CurrentCultureIgnoreCase)
			: 0;
		}

		private class ListViewCaseSensitiveComparer : ListViewComparer
		{
			public ListViewCaseSensitiveComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r) =>
			l is ListViewItem x&& r is ListViewItem y&& col.Index < x.SubItems.Count&& col.Index < y.SubItems.Count
			? SortOrder()* string.Compare(x.SubItems[col.Index].Text, y.SubItems[col.Index].Text, StringComparison.Ordinal)
			: 0;
		}

		private class ListViewFloatComparer : ListViewComparer
		{
			public ListViewFloatComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r)
			{
				if (l is ListViewItem x && r is ListViewItem y && col.Index < x.SubItems.Count && col.Index < y.SubItems.Count)
					if (double.TryParse(x.SubItems[col.Index].Text, out var i1) && double.TryParse(y.SubItems[col.Index].Text, out var i2))
						return (i1 < i2 ? -1 : i1 > i2 ? 1 : 0) * SortOrder();

				return 0;
			}
		}

		private class ListViewIntegerComparer : ListViewComparer
		{
			public ListViewIntegerComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r)
			{
				if (l is ListViewItem x && r is ListViewItem y && col.Index < x.SubItems.Count && col.Index < y.SubItems.Count)
				{
					var i1 = x.SubItems[col.Index].Text.ParseLong(false);
					var i2 = y.SubItems[col.Index].Text.ParseLong(false);
					return (i1 < i2 ? -1 : i1 > i2 ? 1 : 0) * SortOrder();
				}

				return 0;
			}
		}

		private class ListViewLogicalComparer : ListViewComparer
		{
			public ListViewLogicalComparer(ColumnHeader column)
				: base(column)
			{
			}

			public override int Compare(object l, object r) =>
			l is ListViewItem x&& r is ListViewItem y&& col.Index < x.SubItems.Count&& col.Index < y.SubItems.Count
			? SortOrder()* NaturalComparer.NaturalCompare(x.SubItems[col.Index].Text, y.SubItems[col.Index].Text)
			: 0;
		}
	}
}
#endif