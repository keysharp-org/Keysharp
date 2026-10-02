#if WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The WinForms half of <see cref="Gui.TreeView"/>. An item's ID is its native handle, and its links, its sort
		/// and the bold and added-expanded states are native, as AutoHotkey has them. WinForms keeps each node's
		/// children in its own list, which a native sort leaves in the order they were added, so order is always read
		/// from the native control.
		/// </summary>
		public partial class TreeView
		{
			private const uint TVM_GETCOUNT = WindowsAPI.TV_FIRST + 5;
			private const uint TVM_GETNEXTITEM = WindowsAPI.TV_FIRST + 10;
			private const uint TVM_GETITEMSTATE = WindowsAPI.TV_FIRST + 39;
			private const uint TVM_SETITEMW = WindowsAPI.TV_FIRST + 63;
			private const int TVGN_ROOT = 0, TVGN_NEXT = 1, TVGN_PREVIOUS = 2, TVGN_PARENT = 3, TVGN_CHILD = 4;
			private const nint TVI_ROOT = -0x10000;
			private const uint TVIF_STATE = 0x0008;
			private const uint TVIS_BOLD = 0x0010;
			private const uint TVIS_EXPANDED = 0x0020;

			[StructLayout(LayoutKind.Sequential)]
			private struct TVITEMW
			{
				internal uint mask;
				internal nint hItem;
				internal uint state;
				internal uint stateMask;
				internal nint pszText;
				internal int cchTextMax;
				internal int iImage;
				internal int iSelectedImage;
				internal int cChildren;
				internal nint lParam;
			}

			private partial TreeNode NodeFromId(long id) => TreeNode.FromHandle(Tv, (nint)id);

			private partial TreeNode FirstChild(TreeNode parent) => Related(parent == null ? TVGN_ROOT : TVGN_CHILD, parent);

			private partial TreeNode LastChild(TreeNode parent)
			{
				//Any child leads to the last one; the one WinForms added last is usually it already.
				var siblings = parent?.Nodes ?? Tv.Nodes;
				var last = siblings.Count > 0 ? siblings[siblings.Count - 1] : null;

				for (var next = last; next != null; next = NextSibling(next))
					last = next;

				return last;
			}

			private partial TreeNode NextSibling(TreeNode node) => Related(TVGN_NEXT, node);

			private partial TreeNode PrevSibling(TreeNode node) => Related(TVGN_PREVIOUS, node);

			private partial TreeNode ParentOf(TreeNode node) => Related(TVGN_PARENT, node);

			private TreeNode Related(int relation, TreeNode node) =>
				NodeFromId(WindowsAPI.SendMessage(Tv.Handle, TVM_GETNEXTITEM, relation, node?.Handle ?? 0));

			private partial TreeNode InsertNode(TreeNode parent, TreeNode after, string text)
			{
				//With the tree's handle created first, each node is created natively as it is inserted, and so has its ID
				//at once. WinForms inserts it natively after the node before it in its own list, so it goes in that list
				//right after the node it is to follow.
				_ = Tv.Handle;
				var siblings = parent?.Nodes ?? Tv.Nodes;
				return siblings.Insert(after == null ? 0 : after.Index + 1, text);
			}

			private partial void RemoveNode(TreeNode node) => node.Remove();

			private partial void ClearNodes() => Tv.Nodes.Clear();

			private partial int NodeCount() => (int)WindowsAPI.SendMessage(Tv.Handle, TVM_GETCOUNT, 0, 0);

			private partial bool IsBold(TreeNode node) => (WindowsAPI.SendMessage(Tv.Handle, TVM_GETITEMSTATE, node.Handle, (nint)TVIS_BOLD) & TVIS_BOLD) != 0;

			private partial void SetBold(TreeNode node, bool bold) => SetState(node, bold ? TVIS_BOLD : 0, TVIS_BOLD);

			private partial void SetExpanded(TreeNode node, bool expand, bool adding)
			{
				//Expanding a node with no children has no effect, so a new one gets the state bit, which its children
				//then show under.
				if (adding)
				{
					if (expand)
						SetState(node, TVIS_EXPANDED, TVIS_EXPANDED);
				}
				else if (expand)
					node.Expand();
				else
					node.Collapse(true);
			}

			private partial void SelectNode(TreeNode node) => Tv.SelectedNode = node;

			private partial void SortChildren(TreeNode node) => _ = WindowsAPI.SendMessage(Tv.Handle, (uint)WindowsAPI.TVM_SORTCHILDREN, 0, node?.Handle ?? TVI_ROOT);

			private unsafe void SetState(TreeNode node, uint state, uint mask)
			{
				var item = new TVITEMW
				{
					mask = TVIF_STATE,
					hItem = node.Handle,
					state = state,
					stateMask = mask
				};
				_ = WindowsAPI.SendMessage(Tv.Handle, TVM_SETITEMW, 0, (nint)(&item));
			}
		}
	}
}
#endif
