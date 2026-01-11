#if !WINDOWS
namespace Keysharp.Core
{
	internal sealed class ToolTip : IDisposable
	{
		private sealed class TooltipWindow : Form
		{
			private readonly Forms.Label label;
			private readonly Panel panel;

			public TooltipWindow()
			{
				ShowInTaskbar = false;
				Resizable = false;
				Topmost = true;
				ShowActivated = false;
				WindowStyle = WindowStyle.None;
				Padding = new Padding(0);
				BackgroundColor = Color.FromArgb(0xFF, 0xFF, 0xF0);

				label = new Forms.Label
				{
					TextColor = Colors.Black,
					Wrap = WrapMode.None
				};

				panel = new Panel
				{
					Padding = new Padding(6),
					Content = label
				};

				Content = panel;
			}

			public nint Handle => base.NativeHandle;

			public void ShowTooltip(Form owner, string text, int x, int y)
			{
				label.Text = text ?? "";
				panel.Content = label;
				// Recalculate preferred size every time text changes so stale dimensions aren't reused.
				var preferred = panel.GetPreferredSize();
				ClientSize = new Size((int)Math.Ceiling(preferred.Width), (int)Math.Ceiling(preferred.Height));
				Location = new Point(x, y);

				if (Visible)
					return;

				Show();
			}
		}

		private readonly TooltipWindow tooltip_window = new();
		private string lastText = "";

		public bool Active { get; set; }
		public int AutomaticDelay { get; set; }
		public int InitialDelay { get; set; }
		public int ReshowDelay { get; set; }
		public bool ShowAlways { get; set; }
		public bool UseFading { get; set; }
		public bool UseAnimation { get; set; }

		public void Show(string text, Form owner, int x, int y)
		{
			lastText = text ?? "";
			if (string.IsNullOrEmpty(lastText))
			{
				tooltip_window.Hide();
				return;
			}

			tooltip_window.ShowTooltip(owner, lastText, x, y);
		}

		public string GetToolTip(Form owner) => lastText;

		public void Dispose()
		{
			Active = false;
			lastText = "";
			tooltip_window.Close();
			tooltip_window.Dispose();
		}
	}
}
#endif
