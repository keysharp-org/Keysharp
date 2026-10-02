#if WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		public partial class Edit
		{
			private partial void ShowCue(string text, bool whenFocused)
			{
				if (Ctrl is not KeysharpTextBox tb)
					return;

				//The native cue banner is single-line only; WinForms draws a multi-line box's placeholder itself.
				if (tb.Multiline)
					tb.PlaceholderText = text;
				else
					_ = WindowsAPI.SendMessage(tb.Handle, WindowsAPI.EM_SETCUEBANNER, whenFocused ? 1 : 0, text);
			}
		}

		public partial class ComboBox
		{
			private const uint CB_SETCUEBANNER = 0x1703;

			private partial void ShowCue(string text) => _ = WindowsAPI.SendMessage(Ctrl.Handle, CB_SETCUEBANNER, 0, text);
		}
	}
}
#endif
