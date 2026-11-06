#if !WINDOWS
using Eto.Forms;
using Eto.Drawing;
#if LINUX
using Keysharp.Internals.Window.Linux.Wayland;
#endif

namespace Keysharp.Builtins
{
	/// <summary>
	/// The InputBox off Windows. Its result is the word InputBox reports as Result, or null when the window was closed
	/// without a button, which is a Cancel.
	/// </summary>
	internal sealed class InputDialog : Dialog<string>
	{
		private readonly TextControl input;
		private readonly int x, y;

		/// <param name="passwordChar">Null for plain text, "" to mask it with the toolkit's character.</param>
		/// <param name="width">The client area's width as InputBox's W gives it, or int.MinValue to fit the contents; likewise the others.</param>
		internal InputDialog(string title, string prompt, string defaultText, string passwordChar, int width, int height, int x, int y)
		{
			this.x = x;
			this.y = y;
			Title = title;
			Resizable = false;
			Topmost = true;

			if (Script.TheScript.scriptIcon is { } icon)//Not the tray's: that is null under #NoTrayIcon or A_IconHidden.
				Icon = icon;

			if (passwordChar == null)
				input = new TextBox { Text = defaultText };
			else
			{
				var box = new PasswordBox { Text = defaultText };

				if (passwordChar.Length > 0)
					box.PasswordChar = passwordChar[0];

				input = box;
			}

			var ok = new Button { Text = "OK" };
			ok.Click += (_, _) => Close("OK");
			var cancel = new Button { Text = "Cancel" };
			cancel.Click += (_, _) => Close("Cancel");
			DefaultButton = ok;
			AbortButton = cancel;

			//The prompt takes whatever height the field and the buttons leave.
			Content = new TableLayout
			{
				Padding = new Padding(10),
				Spacing = new Size(8, 8),
				Size = new Size(width == int.MinValue ? -1 : width, height == int.MinValue ? -1 : height),
				Rows =
				{
					new TableRow(new Forms.Label { Text = prompt, Wrap = WrapMode.Word }) { ScaleHeight = true },
					input,
					new TableLayout(new TableRow(new TableCell(null, true), ok, cancel)) { Spacing = new Size(8, 0) }
				}
			};

			// macOS editing shortcuts (Cmd+C/A/V) in the input field need an Edit menu; dialogs don't
			// inherit one reliably, so give this dialog its own standard menu.
			GuiHelper.EnsureSystemMenu(this);

			if (x != int.MinValue && y != int.MinValue)
				Location = new Point(x, y);
			else if (x != int.MinValue || y != int.MinValue)
				Shown += (_, _) => CentreOmittedCoordinate();

			Shown += (_, _) => input.Focus();
		}

		internal string Value => input.Text;

#if LINUX
		protected override void OnLoadComplete(EventArgs e)
		{
			base.OnLoadComplete(e);

			if (!WaylandOwnToplevels.IsSupported)
				return;

			WaylandOwnToplevels.Position(this, Title, x, y, Width, Height, keepAbove: Topmost);
			// GTK queues ShowModalAsync; reserve here, immediately before the native window maps.
			_ = WaylandOwnToplevels.ReserveWindow(this, x, y);
		}
#endif

		private void CentreOmittedCoordinate()
		{
#if LINUX
			// Preserve the compositor's placement on axes the script did not request.
			if (Keysharp.Internals.Platform.Desktop.IsWaylandSession)
				return;
#endif
			var area = (Screen ?? Eto.Forms.Screen.PrimaryScreen).WorkingArea;
			Location = new Point(x != int.MinValue ? x : (int)(area.X + (area.Width - Width) / 2),
								 y != int.MinValue ? y : (int)(area.Y + (area.Height - Height) / 2));
		}
	}
}
#endif
