namespace Keyview
{
	internal class SearchManager
	{
		public static string LastSearch = "";
		public static int LastSearchIndex;
		public static TextBox SearchBox;
#if WINDOWS
		public static ScintillaNET.Scintilla TextArea;
#else
		public static Forms.RichTextBox TextArea;
#endif

		public static void Find(bool next, bool incremental)
		{
			LastSearch = SearchBox.Text;

			if (string.IsNullOrEmpty(LastSearch) || TextArea == null)
			{
				_ = SearchBox?.Focus();
				return;
			}

#if WINDOWS
			if (next)
			{
				// SEARCH FOR THE NEXT OCCURANCE
				// Search the document at the last search index
				TextArea.TargetStart = LastSearchIndex - 1;
				TextArea.TargetEnd = LastSearchIndex + (LastSearch.Length + 1);
				TextArea.SearchFlags = SearchFlags.None;

				// Search, and if not found..
				if (!incremental || TextArea.SearchInTarget(LastSearch) == -1)
				{
					// Search the document from the caret onwards
					TextArea.TargetStart = TextArea.CurrentPosition;
					TextArea.TargetEnd = TextArea.TextLength;
					TextArea.SearchFlags = SearchFlags.None;

					// Search, and if not found..
					if (TextArea.SearchInTarget(LastSearch) == -1)
					{
						// Search again from top
						TextArea.TargetStart = 0;
						TextArea.TargetEnd = TextArea.TextLength;

						// Search, and if not found..
						if (TextArea.SearchInTarget(LastSearch) == -1)
						{
							// clear selection and exit
							TextArea.ClearSelections();
							return;
						}
					}
				}
			}
			else
			{
				// SEARCH FOR THE PREVIOUS OCCURANCE
				// Search the document from the beginning to the caret
				TextArea.TargetStart = 0;
				TextArea.TargetEnd = TextArea.CurrentPosition;
				TextArea.SearchFlags = SearchFlags.None;

				// Search, and if not found..
				if (TextArea.SearchInTarget(LastSearch) == -1)
				{
					// Search again from the caret onwards
					TextArea.TargetStart = TextArea.CurrentPosition;
					TextArea.TargetEnd = TextArea.TextLength;

					// Search, and if not found..
					if (TextArea.SearchInTarget(LastSearch) == -1)
					{
						// clear selection and exit
						TextArea.ClearSelections();
						return;
					}
				}
			}

			// Select the occurance
			LastSearchIndex = TextArea.TargetStart;
			TextArea.SetSelection(TextArea.TargetEnd, TextArea.TargetStart);
			TextArea.ScrollCaret();
#else
			var comparison = StringComparison.CurrentCulture;
			var selectionStart = TextArea.SelectionStart;
			var selectionLength = TextArea.SelectionLength;
			var searchStart = next
				? (incremental ? Math.Max(0, LastSearchIndex - 1) : selectionStart + selectionLength)
				: Math.Max(0, selectionStart - 1);

			int index;

			if (next)
			{
				index = TextArea.Text.IndexOf(LastSearch, searchStart, comparison);

				if (index == -1 && searchStart > 0)
					index = TextArea.Text.IndexOf(LastSearch, 0, comparison);
			}
			else
			{
				index = TextArea.Text.LastIndexOf(LastSearch, searchStart, comparison);

				if (index == -1)
					index = TextArea.Text.LastIndexOf(LastSearch, TextArea.Text.Length - 1, comparison);
			}

			if (index == -1)
			{
				TextArea.SelectionLength = 0;
				return;
			}

			LastSearchIndex = index + LastSearch.Length;
			TextArea.SelectionStart = index;
			TextArea.SelectionLength = LastSearch.Length;
			TextArea.Focus();
#endif

			_ = SearchBox.Focus();
		}
	}
}
