#if LINUX
namespace Keysharp.Core.Linux
{
	public class NotifyIcon : IDisposable
	{
		private const int DoubleClickThresholdMs = 400;
		private readonly TrayIndicator indicator = new TrayIndicator();
		private long lastClickTicks;
		private ContextMenuStrip contextMenuStrip;
		private string text = "";
		private Image icon;

		public ContextMenuStrip ContextMenuStrip
		{
			get => contextMenuStrip;
			set
			{
				contextMenuStrip = value;
				indicator.Menu = contextMenuStrip?.EtoMenu;
			}
		}

		public string Text
		{
			get => text;
			set
			{
				text = value ?? "";
				indicator.Title = text;
			}
		}

		public Image Icon
		{
			get => icon;
			set
			{
				icon = value;
				if (value is Icon etoIcon)
					indicator.Icon = etoIcon;
				else if (value is Image etoImage)
					indicator.Image = etoImage;
			}
		}

		public bool Visible
		{
			get => indicator.Visible;
			set
			{
				if (value)
					indicator.Show();
				else
					indicator.Hide();
			}
		}

		public object Tag { get; set; }

		public event EventHandler<MouseEventArgs> MouseClick;
		public event EventHandler<MouseEventArgs> MouseDoubleClick;

		public NotifyIcon()
		{
			indicator.Activated += Indicator_Activated;
		}

		public void Dispose()
		{
			indicator.Activated -= Indicator_Activated;
			indicator.Hide();
			indicator.Dispose();
		}

		public void ShowBalloonTip(int timeout, string title, string text, object icon)
		{
			var notification = new Notification
			{
				Title = title ?? "",
				Message = text ?? ""
			};

			if (notification.RequiresTrayIndicator)
				notification.Show(indicator);
			else
				notification.Show();
		}

		private void Indicator_Activated(object sender, EventArgs e)
		{
			var now = DateTime.UtcNow.Ticks;
			var args = new MouseEventArgs(Eto.Forms.MouseButtons.Primary, Eto.Forms.Keys.None, new PointF(0, 0), null, 0f);

			MouseClick?.Invoke(this, args);

			if (lastClickTicks != 0 && (now - lastClickTicks) <= TimeSpan.FromMilliseconds(DoubleClickThresholdMs).Ticks)
			{
				lastClickTicks = 0;
				MouseDoubleClick?.Invoke(this, args);
				return;
			}

			lastClickTicks = now;
		}
	}
}
#endif
