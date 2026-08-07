#if WINDOWS
using Microsoft.Win32;

namespace Keysharp.Internals.Window.Windows
{
	/// <summary>
	/// Windows <see cref="IMonitorEventBackend"/> built on <c>SystemEvents.DisplaySettingsChanged</c>, which is
	/// raised for the whole family of changes this API cares about: resolution and refresh changes, monitors being
	/// attached or detached, arrangement and primary-monitor changes, and dock/undock. It is the managed wrapper
	/// around <c>WM_DISPLAYCHANGE</c>, so subscribing to it costs no window of our own.
	/// </summary>
	internal sealed class MonitorEventBackend : IMonitorEventBackend
	{
		private readonly EventHandler handler;
		private readonly Lock gate = new();
		private bool subscribed;
		private bool disposed;

		internal MonitorEventBackend() => handler = OnDisplaySettingsChanged;

		public Action Sink { get; set; }

		public void Start()
		{
			lock (gate)
			{
				if (subscribed || disposed)
					return;

				// SystemEvents captures the ambient SynchronizationContext to marshal its callbacks. Binding that to
				// a per-script WinForms UI thread which is later torn down is what makes Application.SetColorMode()
				// block forever (the same hazard Script.InitializeScreenSystemEventsOnNeutralContext works around),
				// so subscribe under a neutral context. Our handler only signals the sink, so it is happy on any
				// thread.
				var oldContext = System.ComponentModel.AsyncOperationManager.SynchronizationContext;

				try
				{
					System.ComponentModel.AsyncOperationManager.SynchronizationContext = new SynchronizationContext();
					SystemEvents.DisplaySettingsChanged += handler;
					subscribed = true;
				}
				finally
				{
					System.ComponentModel.AsyncOperationManager.SynchronizationContext = oldContext;
				}
			}
		}

		public void Stop()
		{
			lock (gate)
			{
				if (!subscribed)
					return;

				SystemEvents.DisplaySettingsChanged -= handler;
				subscribed = false;
			}
		}

		public void Dispose()
		{
			// SystemEvents is a static event, i.e. a root the CLR holds for the life of the process: leaving the
			// handler attached would keep this backend (and through it the whole Script) alive forever, and a
			// subsequent Script would attach a second handler and double-report every change.
			Stop();
			disposed = true;
			Sink = null;
		}

		private void OnDisplaySettingsChanged(object sender, EventArgs e) => Sink?.Invoke();
	}
}
#endif
