namespace Keysharp.Builtins
{
	public partial class Gui
	{
		public partial class ListView
		{
			private readonly Dictionary<(int Row, int Column), (Color? Text, Color? Back)> colors = [];
			internal bool HasColors => colors.Count > 0;

			/// <summary>Sets a row's colors. Omitted, empty, or Default colors inherit the control's colors.</summary>
			/// <param name="row">The existing, 1-based row number.</param>
			/// <param name="textColor">A GUI color name, hexadecimal string, or RGB integer.</param>
			/// <param name="backColor">A GUI color name, hexadecimal string, or RGB integer.</param>
			/// <returns>This control.</returns>
			public object SetRowColor(object row, object textColor = null, object backColor = null) =>
				SetColors(row, null, textColor, backColor);

			/// <summary>Sets a cell's colors. Omitted, empty, or Default colors inherit the row's colors.</summary>
			/// <param name="row">The existing, 1-based row number.</param>
			/// <param name="column">The existing, 1-based column number.</param>
			/// <param name="textColor">A GUI color name, hexadecimal string, or RGB integer.</param>
			/// <param name="backColor">A GUI color name, hexadecimal string, or RGB integer.</param>
			/// <returns>This control.</returns>
			public object SetCellColor(object row, object column, object textColor = null, object backColor = null) =>
				SetColors(row, column, textColor, backColor);

			/// <summary>Removes all row and cell colors. Structural edits and sorting also clear these index-based colors.</summary>
			/// <returns>This control.</returns>
			public object ClearColors()
			{
				if (colors.Count > 0)
				{
					colors.Clear();
					(Ctrl as KeysharpListView)?.RefreshColors();
				}

				return this;
			}

			internal (Color? Text, Color? Back) GetColors(int row, int column)
			{
				_ = colors.TryGetValue((row, 0), out var rowColors);
				_ = colors.TryGetValue((row, column), out var cellColors);
				return (cellColors.Text ?? rowColors.Text, cellColors.Back ?? rowColors.Back);
			}

			private object SetColors(object row, object column, object textColor, object backColor)
			{
				if (Ctrl is not KeysharpListView lv)
					return Errors.ErrorOccurred("GUI control is no longer available.");

				var rowNumber = row.Al();
				var columnNumber = column is null ? 0 : column.Al();

				if (rowNumber < 1 || rowNumber > lv.Items.Count)
					return Errors.ValueErrorOccurred($"Invalid ListView row {rowNumber}; expected 1 through {lv.Items.Count}.");

				if (column is not null && (columnNumber < 1 || columnNumber > lv.Columns.Count))
					return Errors.ValueErrorOccurred($"Invalid ListView column {columnNumber}; expected 1 through {lv.Columns.Count}.");

				if (!TryColor(textColor, out var text) || !TryColor(backColor, out var back))
					return DefaultObject;

				var key = ((int)rowNumber, (int)columnNumber);

				if (text is null && back is null)
					_ = colors.Remove(key);
				else
					colors[key] = (text, back);

				lv.RefreshColors((int)rowNumber - 1);
				return this;
			}

			private static bool TryColor(object value, out Color? color)
			{
				color = null;

				if (value is null)
					return true;

				if (value is string text)
				{
					var name = text.AsSpan().Trim();

					if (name.IsEmpty || name.Equals("Default", StringComparison.OrdinalIgnoreCase))
						return true;

					if (Conversions.TryParseColor(text, out var parsed))
					{
						color = parsed;
						return true;
					}

					_ = Errors.ValueErrorOccurred($"Invalid ListView color {text}.");
					return false;
				}

				color = Color.FromArgb((int)(value.Al() | 0xFF000000));
				return true;
			}
		}
	}
}
