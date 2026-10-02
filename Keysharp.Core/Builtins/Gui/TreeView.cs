namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The holder for a TreeView control. An item is identified by its ID, which <see cref="Add"/> returns, and
		/// ID 0 is the tree's root where AutoHotkey reads it so: as the parent of <see cref="Add"/>, the start of
		/// <see cref="GetNext"/> and <see cref="GetChild"/>, and the item whose children Modify(0, "Sort") sorts.
		/// Everywhere else 0 is no item. The semantics are shared; each toolkit's half, the partial members at the end,
		/// is in Windows/TreeView.cs and Unix/TreeView.cs. Every walk through the tree goes through that
		/// half's links, since on Windows only the native control knows the order a sort leaves.
		/// </summary>
		public partial class TreeView
		{
			/// <summary>
			/// The item options of Add and Modify. A state is null when the options do not name it, which Modify leaves
			/// as it is.
			/// </summary>
			internal struct TreeViewItemOptions
			{
				internal bool? bold, check, expand;
				internal bool first, select, sort, vis, visFirst;
				internal int? icon;//1-based.
				internal long insertAfter;//An item ID, or 0.

				/// <param name="adding">Whether the options are Add's, which alone take First and an item ID.</param>
				/// <returns>False when an option was not one, whose ValueError the script continued.</returns>
				internal static bool TryParse(string options, bool adding, out TreeViewItemOptions o)
				{
					o = new TreeViewItemOptions();

					foreach (Range r in options.AsSpan().SplitAny(Spaces))
					{
						var word = options.AsSpan(r).Trim();
						var plus = true;

						if (word.Length > 0 && word[0] is '-' or '+')
						{
							plus = word[0] == '+';
							word = word.Slice(1);
						}

						if (word.Length == 0)
							continue;

						if (word.Equals("Select", StringComparison.OrdinalIgnoreCase))
							o.select |= plus;
						else if (word.Equals("Vis", StringComparison.OrdinalIgnoreCase))
							o.vis = plus;
						else if (word.Equals("VisFirst", StringComparison.OrdinalIgnoreCase))
							o.visFirst = plus;
						else if (word.StartsWith("Bold", StringComparison.OrdinalIgnoreCase))
							o.bold = ApplySuffixFlag(word.Slice(4), plus);
						else if (word.StartsWith("Expand", StringComparison.OrdinalIgnoreCase))
							o.expand = ApplySuffixFlag(word.Slice(6), plus);
						else if (word.StartsWith("Check", StringComparison.OrdinalIgnoreCase))
							o.check = ApplySuffixFlag(word.Slice(5), plus);
						else if (word.StartsWith("Icon", StringComparison.OrdinalIgnoreCase))
						{
							if (plus)
								o.icon = (int)Strings.Atoi(word.Slice(4));
						}
						else if (word.Equals("Sort", StringComparison.OrdinalIgnoreCase))
							o.sort = true;
						else if (adding && word.Equals("First", StringComparison.OrdinalIgnoreCase))
							o.first = true;
						else if (adding && long.TryParse(word, out var after) && after > 0)
							o.insertAfter = after;
						else
						{
							_ = Errors.ValueErrorOccurred("Invalid option.", word.ToString());
							return false;
						}
					}

					return true;
				}
			}

			private KeysharpTreeView Tv => (KeysharpTreeView)Ctrl;

			/// <summary>
			/// Adds an item under a parent, or at the top level when the parent is 0, and returns its ID. 0 when the
			/// parent does not exist.
			/// </summary>
			public long Add(object name, object parentItemID = null, object options = null)
			{
				if (!name.CoerceString(out var text) || !parentItemID.CoerceLong(out var parentId) || !options.CoerceString(out var opts))
					return 0L;

				TreeNode parent = null;

				if (parentId != 0 && (parent = NodeFromId(parentId)) == null)
					return 0L;

				if (!TreeViewItemOptions.TryParse(opts, true, out var o))
					return 0L;

				TreeNode after;

				if (o.first)
					after = null;
				else if (o.sort)
					after = SortedPredecessor(parent, text);
				else if (o.insertAfter != 0 && NodeFromId(o.insertAfter) is TreeNode sibling && ParentOf(sibling) == parent)
					after = sibling;
				else
					after = LastChild(parent);

				eventHandlerActive = false;

				try
				{
					var node = InsertNode(parent, after, text);
					Apply(node, o, true);
					return node.Handle.ToInt64();
				}
				finally
				{
					eventHandlerActive = true;
				}
			}

			/// <summary>
			/// Deletes an item and its descendants, or every item when the ID is omitted, and returns 1. As in
			/// AutoHotkey, 0 is refused rather than read as "all", so that Delete(GetSelection()) with nothing selected
			/// deletes nothing, and an item that does not exist fails.
			/// </summary>
			public long Delete(object itemID = null)
			{
				if (itemID is null)
				{
					ClearNodes();
					return 1L;
				}

				if (!itemID.CoerceLong(out var id))
					return 0L;

				if (id == 0)
					return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.TreeView.Prototype.Delete", itemID, 0L);

				if (NodeFromId(id) is not TreeNode node)
					return (long)Errors.ErrorOccurred("Failed", 0L);

				RemoveNode(node);
				return 1L;
			}

			/// <summary>
			/// The item's ID when it has the attribute named by the first letter of the attribute, E(xpanded), C(hecked)
			/// or B(old), else 0.
			/// </summary>
			public long Get(object itemID, object attribute)
			{
				if (!itemID.CoerceLong(out var id) || !attribute.CoerceString(out var attr))
					return 0L;

				if (NodeFromId(id) is not TreeNode node)
					return 0L;

				var word = attr.AsSpan().TrimStart();

				var set = !word.IsEmpty && char.ToUpperInvariant(word[0]) switch
				{
					'E' => node.IsExpanded,
					'C' => node.Checked,
					'B' => IsBold(node),
					_ => false
				};

				return set ? id : 0L;
			}

			/// <summary>The ID of the item's first child, or of the first top-level item when the ID is 0; else 0.</summary>
			public long GetChild(object itemID)
			{
				if (!itemID.CoerceLong(out var id))
					return 0L;

				if (id == 0)
					return Id(FirstChild(null));

				return NodeFromId(id) is TreeNode node ? Id(FirstChild(node)) : 0L;
			}

			/// <summary>The number of items in the whole tree.</summary>
			public long GetCount() => NodeCount();

			/// <summary>
			/// The ID of the item's next sibling, or of the first top-level item when the ID is 0. With "Full", the next
			/// item of a walk over the whole tree, children before siblings, which starts at the first item when the ID
			/// is 0; with "Checked", the next such item that is checked. 0 when there is none.
			/// </summary>
			public long GetNext(object itemID = null, object itemType = null)
			{
				if (!itemID.CoerceLong(out var id) || !itemType.CoerceString(out var type))
					return 0L;

				TreeNode node = null;

				if (id != 0 && (node = NodeFromId(id)) == null)
					return 0L;

				if (itemType is null)
					return Id(node == null ? FirstChild(null) : NextSibling(node));

				//A type given, even an empty one, has to be one of the two, as in AutoHotkey.
				var word = type.AsSpan().TrimStart();
				var kind = word.IsEmpty ? '\0' : char.ToUpperInvariant(word[0]);

				if (kind is not ('C' or 'F'))
					return (long)Errors.InvalidParameterErrorOccurred(2, "Gui.TreeView.Prototype.GetNext", itemType, 0L);

				do
					node = NextInTree(node);
				while (kind == 'C' && node != null && !node.Checked);

				return Id(node);
			}

			/// <summary>The raw toolkit node with the ID, a Keysharp extension, or "" when there is none.</summary>
			public object GetNode(object itemID)
			{
				if (!itemID.CoerceLong(out var id))
					return DefaultObject;

				return (object)NodeFromId(id) ?? DefaultObject;
			}

			/// <summary>The ID of the item's parent, or 0 for a top-level item or none.</summary>
			public long GetParent(object itemID) => itemID.CoerceLong(out var id) && NodeFromId(id) is TreeNode node ? Id(ParentOf(node)) : 0L;

			/// <summary>The ID of the item's previous sibling, or 0.</summary>
			public long GetPrev(object itemID) => itemID.CoerceLong(out var id) && NodeFromId(id) is TreeNode node ? Id(PrevSibling(node)) : 0L;

			/// <summary>The ID of the selected item, or 0.</summary>
			public long GetSelection() => Id(Tv.SelectedNode);

			/// <summary>The item's text. An item that does not exist, ID 0 among them, fails, as in AutoHotkey.</summary>
			public string GetText(object itemID)
			{
				if (!itemID.CoerceLong(out var id))
					return DefaultErrorString;

				return NodeFromId(id) is TreeNode node ? node.Text : (string)Errors.ErrorOccurred("Failed", "");
			}

			/// <summary>
			/// With only the ID, selects the item, or clears the selection for 0. Otherwise applies the options and,
			/// when given, the new name; an empty name blanks the item. Returns the ID, or 0 when the item does not exist.
			/// </summary>
			public long Modify(object itemID, object options = null, object newName = null)
			{
				if (!itemID.CoerceLong(out var id))
					return 0L;

				TreeNode node = null;

				if (id != 0 && (node = NodeFromId(id)) == null)
					return 0L;

				string name = null;

				if (!options.CoerceString(out var opts) || (newName is not null && !newName.CoerceString(out name)))
					return 0L;

				var o = default(TreeViewItemOptions);

				if ((options is not null || newName is not null) && !TreeViewItemOptions.TryParse(opts, false, out o))
					return 0L;

				eventHandlerActive = false;

				try
				{
					if (options is null && newName is null)
						SelectNode(node);
					else if (node == null)
					{
						if (o.sort)
							SortChildren(null);
					}
					else
					{
						if (name != null)
							node.Text = name;

						Apply(node, o, false);
					}
				}
				finally
				{
					eventHandlerActive = true;
				}

				return id;
			}

			/// <summary>
			/// Sets the image list the items take their icons from. Returns the ID of the list it replaces, or 0.
			/// </summary>
			public long SetImageList(object imageListID, object iconType = null)
			{
				if (!imageListID.CoerceLong(out var id) || !iconType.CoerceLong(out _) || ImageLists.IL_Get(id) is not ImageList il)
					return 0L;

				var old = ImageLists.IL_GetId(Tv.ImageList);
				Tv.ImageList = il;
				return old;
			}

			private void Apply(TreeNode node, in TreeViewItemOptions o, bool adding)
			{
				if (o.bold is bool bold)
					SetBold(node, bold);

				if (o.check is bool check)
					node.Checked = check;

				if (o.expand is bool expand)
					SetExpanded(node, expand, adding);

				if (o.icon is int icon)
				{
					var il = Tv.ImageList;
					node.ImageIndex = node.SelectedImageIndex = il != null && icon >= 1 && icon <= il.Images.Count ? icon - 1 : -1;
				}

				if (o.sort && !adding)
					SortChildren(node);

				if (o.vis)
					node.EnsureVisible();

				if (o.visFirst)
				{
					node.EnsureVisible();
					Tv.TopNode = node;
				}

				if (o.select)
					SelectNode(node);
			}

			//The sibling a sorted insertion goes after: the last whose text sorts no later, as TVI_SORT places it.
			private TreeNode SortedPredecessor(TreeNode parent, string text)
			{
				TreeNode after = null;

				for (var node = FirstChild(parent); node != null && string.Compare(node.Text, text, StringComparison.CurrentCultureIgnoreCase) <= 0; node = NextSibling(node))
					after = node;

				return after;
			}

			/// <summary>
			/// The item after the node in a walk over the whole tree, children first, then siblings, then the next sibling
			/// of the nearest ancestor that has one; the first top-level item when the node is null. AutoHotkey's
			/// GetNextTreeItem.
			/// </summary>
			private TreeNode NextInTree(TreeNode node)
			{
				if (node == null)
					return FirstChild(null);

				if (FirstChild(node) is TreeNode child)
					return child;

				for (; node != null; node = ParentOf(node))
					if (NextSibling(node) is TreeNode next)
						return next;

				return null;
			}

			private static long Id(TreeNode node) => node?.Handle.ToInt64() ?? 0L;

			//Each toolkit's half.

			/// <summary>The node with the ID, or null.</summary>
			private partial TreeNode NodeFromId(long id);

			/// <summary>The node's first child, or the first top-level node when it is null; null when there is none.</summary>
			private partial TreeNode FirstChild(TreeNode parent);

			/// <summary>The last child of a node, or the last top-level node when it is null; null when there is none.</summary>
			private partial TreeNode LastChild(TreeNode parent);

			private partial TreeNode NextSibling(TreeNode node);

			private partial TreeNode PrevSibling(TreeNode node);

			/// <summary>The node's parent, or null for a top-level node.</summary>
			private partial TreeNode ParentOf(TreeNode node);

			/// <summary>Adds a node under a parent, or at the top level when it is null, right after a sibling, or first when it is null.</summary>
			private partial TreeNode InsertNode(TreeNode parent, TreeNode after, string text);

			private partial void RemoveNode(TreeNode node);

			private partial void ClearNodes();

			private partial int NodeCount();

			private partial bool IsBold(TreeNode node);

			private partial void SetBold(TreeNode node, bool bold);

			/// <summary>
			/// Expands or collapses the node. A node being added has no children yet, so it is marked to show them
			/// expanded once it has.
			/// </summary>
			private partial void SetExpanded(TreeNode node, bool expand, bool adding);

			/// <summary>Selects the node, scrolling it into view, or clears the selection when it is null.</summary>
			private partial void SelectNode(TreeNode node);

			/// <summary>Sorts the node's children by text, one level and at once, or the top-level nodes when it is null.</summary>
			private partial void SortChildren(TreeNode node);
		}
	}
}
