#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.TreeView"/>. The tree keeps a map from ID to node, and a change to the
		/// nodes reaches the view when it next reloads, which -Redraw defers.
		/// </summary>
		public partial class TreeView
		{
			//One bold font for the whole tree, which every bold item shares.
			private Font boldFont;

			private partial TreeNode NodeFromId(long id) => Tv.FindNode(id);

			private partial TreeNode FirstChild(TreeNode parent) => (parent?.Nodes ?? Tv.Nodes) is { Count: > 0 } nodes ? nodes[0] : null;

			private partial TreeNode LastChild(TreeNode parent) => (parent?.Nodes ?? Tv.Nodes) is { Count: > 0 } nodes ? nodes[^1] : null;

			private partial TreeNode NextSibling(TreeNode node) => node.NextNode;

			private partial TreeNode PrevSibling(TreeNode node) => node.PrevNode;

			private partial TreeNode ParentOf(TreeNode node) => node.Parent as TreeNode;

			private partial TreeNode InsertNode(TreeNode parent, TreeNode after, string text)
			{
				var siblings = parent?.Nodes ?? Tv.Nodes;
				var index = after == null ? 0 : ReferenceEquals(after, siblings[^1]) ? siblings.Count : siblings.IndexOf(after) + 1;
				var node = siblings.Insert(index, text);
				Tv.ReloadDataIfActive();
				Tv.DelayedExpandParent(node);
				return node;
			}

			private partial void RemoveNode(TreeNode node)
			{
				var tv = Tv;
				ForgetExpansion(node);
				node.Remove();

				if (ReferenceEquals(tv.SelectedNode, node))
					tv.SelectedNode = null;

				tv.ReloadDataIfActive();
			}

			//An item marked to expand once it has children keeps no mark after it is gone.
			private void ForgetExpansion(TreeNode node)
			{
				Tv.RemoveMarkForExpansion(node);

				foreach (var child in node.Nodes)
					ForgetExpansion(child);
			}

			private partial void ClearNodes()
			{
				var tv = Tv;
				tv.Nodes.Clear();
				tv.ClearMarksForExpansion();
				tv.SelectedNode = null;
				tv.TopNode = null;
				tv.ReloadDataIfActive();
			}

			private partial int NodeCount() => CountNodes(Tv.Nodes);

			private static int CountNodes(TreeNodeCollection nodes)
			{
				var count = nodes.Count;

				foreach (var node in nodes)
					count += CountNodes(node.Nodes);

				return count;
			}

			private partial bool IsBold(TreeNode node) => node.NodeFont?.Bold == true;

			private partial void SetBold(TreeNode node, bool bold) =>
				node.NodeFont = bold ? (boldFont ??= new Font(Tv.Font.FamilyName, Tv.Font.Size, FontStyle.Bold)) : null;

			private partial void SetExpanded(TreeNode node, bool expand, bool adding)
			{
				if (!expand)
				{
					node.Collapse();
					Tv.RemoveMarkForExpansion(node);
					return;
				}

				if (!adding)
					node.Expand();

				Tv.MarkForExpansion(node);
			}

			private partial void SelectNode(TreeNode node)
			{
				if (node == null)
					Tv.SelectedNode = null;
				else
					Tv.SelectNode(node, true);
			}

			private partial void SortChildren(TreeNode node)
			{
				(node?.Nodes ?? Tv.Nodes).SortByText();
				Tv.ReloadDataIfActive();
			}
		}
	}
}
#endif
