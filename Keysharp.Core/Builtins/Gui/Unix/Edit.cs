#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		//Of Eto's controls only a single-line entry has placeholder text, which it shows whether or not it has the focus.
		public partial class Edit
		{
			private partial void ShowCue(string text, bool whenFocused)
			{
				if (Ctrl is TextBox tb)
					tb.PlaceholderText = text;
				else
					_ = Errors.ErrorOccurred("A multi-line or password Edit has no cue text off Windows.");
			}
		}

		public partial class ComboBox
		{
			private partial void ShowCue(string text) => _ = Errors.ErrorOccurred("A ComboBox has no cue text off Windows.");
		}
	}
}
#endif
