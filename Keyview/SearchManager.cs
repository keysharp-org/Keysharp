namespace Keyview;

internal class SearchManager
{
	public static string LastSearch = "";
	public static TextBox SearchBox;
	public static ScintillaNET.Scintilla TextArea;

	public static void Find(bool next, bool incremental)
	{
		LastSearch = SearchBox.Text;

		if (string.IsNullOrEmpty(LastSearch) || TextArea == null)
		{
			_ = SearchBox?.Focus();
			return;
		}

		if (next)
		{
			TextArea.TargetStart = incremental ? TextArea.SelectionStart : TextArea.SelectionEnd;
			TextArea.TargetEnd = TextArea.TextLength;
			TextArea.SearchFlags = SearchFlags.None;

			// Search, and if not found..
			if (TextArea.SearchInTarget(LastSearch) == -1)
			{
				TextArea.TargetStart = 0;
				TextArea.TargetEnd = TextArea.TextLength;

				if (TextArea.SearchInTarget(LastSearch) == -1)
				{
					TextArea.ClearSelections();
					return;
				}
			}
		}
		else
		{
			// Reversed target bounds make Scintilla search backwards.
			TextArea.TargetStart = TextArea.SelectionStart;
			TextArea.TargetEnd = 0;
			TextArea.SearchFlags = SearchFlags.None;

			// Search, and if not found..
			if (TextArea.SearchInTarget(LastSearch) == -1)
			{
				// Wrap to the final match.
				TextArea.TargetStart = TextArea.TextLength;
				TextArea.TargetEnd = 0;

				// Search, and if not found..
				if (TextArea.SearchInTarget(LastSearch) == -1)
				{
					// clear selection and exit
					TextArea.ClearSelections();
					return;
				}
			}
		}

		// Select the occurrence
		TextArea.SetSelection(TextArea.TargetEnd, TextArea.TargetStart);
		TextArea.ScrollCaret();

		_ = SearchBox.Focus();
	}
}