namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The holder for an Edit control. Each toolkit's half of its members is in Windows/Edit.cs and
		/// Unix/Edit.cs.
		/// </summary>
		public partial class Edit
		{
			/// <summary>
			/// Shows a cue text while the field is empty, also while it has the focus when ShowWhenFocused is true.
			/// </summary>
			public object SetCue(object cueText, object showWhenFocused = null)
			{
				if (cueText.CoerceString(out var text))
					ShowCue(text, showWhenFocused.Ab());

				return DefaultObject;
			}

			private partial void ShowCue(string text, bool whenFocused);
		}

		public partial class ComboBox
		{
			/// <summary>Shows a cue text while the combo box's field is empty.</summary>
			public object SetCue(object cueText)
			{
				if (cueText.CoerceString(out var text))
					ShowCue(text);

				return DefaultObject;
			}

			private partial void ShowCue(string text);
		}
	}
}
