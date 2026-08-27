#if OSX
namespace Keysharp.Internals.Window.MacOS
{
	/// <summary>
	/// macOS <see cref="IWindowEventBackend"/>. macOS has no single global window-event hook, so the actual work is
	/// done by <see cref="MacAccessibility"/>'s per-application <c>AXObserver</c> stream (see
	/// <c>MacWindowEventObserver.cs</c>), which reports every <see cref="WindowEventType"/> — Active (app switches
	/// and focused-window changes), Create, Close, Move, Show, Minimize, Restore and TitleChange — as CGWindowID
	/// handles. Because that stream is a single shared AX/run-loop installation rather than per-category hooks, this
	/// backend treats the mask as all-or-nothing: it spins the stream up when the first category is requested and
	/// tears it down when the last one is removed. CaretMove is the sole exception — its AXSelectedTextChanged
	/// notification fires per keystroke, so it is registered and removed on its own. The full Accessibility permission
	/// is required; without it the observer stream logs guidance and stays empty.
	/// </summary>
	internal sealed class WindowEventBackend : IWindowEventBackend
	{
		private readonly Script owner;
		private readonly Lock gate = new();
		private WindowEventMask installed = WindowEventMask.None;
		private bool disposed;

		internal WindowEventBackend(Script owner)
			=> this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

		public Action<WindowEventRaw> Sink { get; set; }

		// Start/Stop run under WinEventManager's lock; the observer install/teardown itself is posted to the main
		// thread (AX observer and NSWorkspace registration must happen there, and a synchronous round trip under the
		// lock could deadlock against the main-thread callback, which re-enters the manager).
		public void Start(WindowEventMask mask)
		{
			if (mask == WindowEventMask.None)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				installed |= mask;
			}

			owner.PostToUIThread(ReconcileOnUIThread);
		}

		public void Stop(WindowEventMask mask)
		{
			lock (gate)
				installed &= ~mask;

			owner.PostToUIThread(ReconcileOnUIThread);
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				installed = WindowEventMask.None;
			}

			owner.InvokeOnUIThread(ReconcileOnUIThread);
			Sink = null;
		}

		private void ReconcileOnUIThread()
		{
			WindowEventMask wanted;
			Action<WindowEventRaw> sink;
			bool isDisposed;

			lock (gate)
			{
				wanted = installed;
				sink = Sink;
				isDisposed = disposed;
			}

			if (isDisposed || wanted == WindowEventMask.None)
			{
				MacAccessibility.SetCaretEvents(owner, false);
				MacAccessibility.StopWindowEvents(owner);
				return;
			}

			MacAccessibility.StartWindowEvents(owner, sink);
			MacAccessibility.SetCaretEvents(owner, (wanted & WindowEventMask.CaretMove) != 0);
		}
	}
}
#endif
