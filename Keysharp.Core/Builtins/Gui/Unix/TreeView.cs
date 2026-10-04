#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.TreeView"/>. The tree keeps a map from ID to node. Top-level items reach the
		/// view as they are added and removed; any other change to the nodes reaches it with the tree's next reload.
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

				if (parent != null)
					Tv.InvalidateModel();

				Tv.DelayedExpandParent(node);
				return node;
			}

			private partial void RemoveNode(TreeNode node)
			{
				var tv = Tv;
				var nested = node.Parent is TreeNode;
				ForgetExpansion(node);
				node.Remove();

				if (ReferenceEquals(tv.SelectedNode, node))
					tv.SelectedNode = null;

				if (nested)
					tv.InvalidateModel();
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
			}

			private partial int NodeCount() => Tv.NodeCount;

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
				var selected = Tv.SelectedNode;
				(node?.Nodes ?? Tv.Nodes).SortByText();
				Tv.InvalidateModel();
				Tv.SelectedNode = selected;
			}

			partial void ShowItemChange(TreeNode node) => Tv.ShowItemChange(node);
		}
	}
}
#endif
