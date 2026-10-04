using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Window.Unix
{
	public class NotifyIcon : IDisposable
	{
		private const int DoubleClickThresholdMs = 400;
		private readonly TrayIndicator indicator = new TrayIndicator();
		private bool disposed;
		private long lastClickTicks;
		private ContextMenuStrip contextMenuStrip;
		private string text = "";
		private Image icon;

		public ContextMenuStrip ContextMenuStrip
		{
			get => contextMenuStrip;
			set
			{
				if (disposed)
					return;

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

				if (!disposed)
					indicator.Title = text;
			}
		}

		public Image Icon
		{
			get => icon;
			set
			{
				icon = value;

				if (disposed)
					return;

				indicator.Image = value;
			}
		}

		public bool Visible
		{
			get => !disposed && indicator.Visible;
			set
			{
				if (disposed)
					return;

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
			if (disposed)
				return;

			disposed = true;
			indicator.Activated -= Indicator_Activated;
			try
			{
				// Withdraw the item before releasing its image and menu; an empty image becomes a fallback icon.
				indicator.Hide();
			}
			finally
			{
				try { indicator.Dispose(); }
				finally
				{
					Tag = null;
					contextMenuStrip = null;
					icon = null;
					text = "";
				}
			}
		}

		public void ShowBalloonTip(int timeout, string title, string text, Image icon)
		{
			if (disposed)
				return;

			var notification = new Notification
			{
				Title = title ?? "",
				Message = text ?? "",
				ContentImage = icon
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
