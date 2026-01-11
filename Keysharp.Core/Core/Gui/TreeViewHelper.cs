namespace Keysharp.Core
{
	internal static class TreeViewHelper
	{
		internal static TreeNode TV_FindNode(KeysharpTreeView parent, long id)
		{
			if (id == 0)
				return parent.TopNode;

			var match = parent.Nodes.Find(id.ToString(), true);
			return match.Length == 0 ? null : match[0];
		}

		internal static long TV_NodeOptions(TreeNode node, long parent, string options, bool modify)
		{
			var ret = node.Handle.ToInt64();

			if (string.IsNullOrEmpty(options))
				return ret;

			if (node.TreeView is KeysharpTreeView tv)
			{
				var bold = false;
				var ischecked = 0;
				var expanded = 0;
				var icon = int.MinValue;

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

						if (opt.Length > 0)
						{
#if WINDOWS
							if (opt.Equals("-Bold", StringComparison.OrdinalIgnoreCase)) { node.NodeFont = new Font(tv.Font, FontStyle.Regular); }
							else if (Options.TryParse(opt, "Bold", ref bold)) { node.NodeFont = new Font(tv.Font, bold ? FontStyle.Bold : FontStyle.Regular); }
#else
							if (opt.Equals("-Bold", StringComparison.OrdinalIgnoreCase)) { node.NodeFont = new Font(tv.Font.FamilyName, tv.Font.Size, FontStyle.None); }
							else if (Options.TryParse(opt, "Bold", ref bold)) { node.NodeFont = new Font(tv.Font.FamilyName, tv.Font.Size, bold ? FontStyle.Bold : FontStyle.None); }
#endif
							else if (opt.Equals("-Check", StringComparison.OrdinalIgnoreCase)) { node.Checked = false; }
							else if (Options.TryParse(opt, "Check", ref ischecked, StringComparison.OrdinalIgnoreCase, true)) { node.Checked = ischecked != 0; }
							else if (opt.Equals("-Expand", StringComparison.OrdinalIgnoreCase)) { node.Collapse(); tv.RemoveMarkForExpansion(node); }
						else if (Options.TryParse(opt, "Expand", ref expanded, StringComparison.OrdinalIgnoreCase, true, 1))
						{
							if (expanded != 0)
							{
								if (modify)
								{
									if (node.Nodes.Count == 0)
										ret = 0;
									else
										node.Expand();
								}

								tv.MarkForExpansion(node);
							}
							else
							{
								node.Collapse();
								tv.RemoveMarkForExpansion(node);
							}
						}
						else if (opt.Equals("Select", StringComparison.OrdinalIgnoreCase))
						{
#if WINDOWS
							tv.SelectedNode = node;
#else
							tv.SelectNode(node, true);
#endif
						}
						else if (opt.Equals("Vis", StringComparison.OrdinalIgnoreCase)) { node.EnsureVisible(); }
						else if (opt.Equals("VisFirst", StringComparison.OrdinalIgnoreCase)) { node.EnsureVisible(); tv.TopNode = node; }
						else if (Options.TryParse(opt, "Icon", ref icon))
						{
							if (tv.ImageList != null)
							{
								if (icon < tv.ImageList.Images.Count)
								{
									node.ImageIndex = icon - 1;
									node.SelectedImageIndex = node.ImageIndex;
								}
								else
								{
									node.ImageIndex = -1;
									node.SelectedImageIndex = node.ImageIndex;
								}
							}
						}
						else if (opt.Equals("Sort", StringComparison.OrdinalIgnoreCase))
						{
#if WINDOWS
							if (node.NextNode == null)
								ret = 0;
							else
								_ = tv.BeginInvoke(new System.Windows.Forms.MethodInvoker(tv.Sort));//BeginInvoke() is needed to avoid an infinte loop: https://stackoverflow.com/questions/808696/c-sharp-windows-form-treeview-sort-after-labeledit
#else
							if (node.Nodes.Count == 0)
								ret = 0;
							else
							{
								_ = tv.BeginInvoke(new Action(() =>
								{
									node.Nodes.SortByText();
									tv.ReloadData();
								}));
							}
#endif
						}
					}
				}
			}

			return ret;
		}
	}
}
