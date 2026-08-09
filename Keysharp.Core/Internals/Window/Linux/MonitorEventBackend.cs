#if LINUX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// Linux <see cref="IMonitorEventBackend"/> built on GDK's monitor signals rather than on XRandR or
	/// <c>wl_registry</c> directly. GDK already tracks the display configuration under BOTH session types — it is
	/// the same abstraction <c>EtoScreen.GetDisplays</c> reads — so one implementation covers X11 and every Wayland
	/// compositor, exactly as the shared <c>LinuxMonitorDetails</c> does for EDID. Going native would mean an
	/// <c>XRRSelectInput</c> pump for X11 plus per-compositor output tracking for Wayland, to learn the same fact.
	/// <para>
	/// Four signals are connected because no single one covers everything: <c>monitor-added</c>/<c>monitor-removed</c>
	/// on the GdkDisplay report hotplug, while GdkScreen's <c>monitors-changed</c>/<c>size-changed</c> report
	/// geometry and arrangement changes to monitors that were already attached. They overlap heavily and several
	/// fire for one user-visible change; that is harmless by the backend contract, since MonitorEventManager diffs
	/// topology snapshots and drops notifications that changed nothing.
	/// </para>
	/// <para>Threading: GDK signals are emitted on the GTK main-loop (UI) thread and must be connected from it, so
	/// connect/disconnect are marshalled there. The handlers only signal the sink, which enqueues onto a scheduler,
	/// so no script code runs on the UI thread.</para>
	/// </summary>
	internal sealed class MonitorEventBackend : IMonitorEventBackend
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void GSignalHandler(nint instance, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void GSignalHandlerWithArg(nint instance, nint arg1, nint userData);

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_display_get_default();

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_screen_get_default();

		// GLib takes its strings as UTF-8; the DllImport default (ANSI) happens to agree for the ASCII signal names
		// used here, but saying so explicitly keeps that from being an accident.
		[DllImport("libgobject-2.0.so.0")]
		private static extern ulong g_signal_connect_data(nint instance,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal, nint handler,
			nint data, nint destroyData, int connectFlags);

		[DllImport("libgobject-2.0.so.0")]
		private static extern void g_signal_handler_disconnect(nint instance, ulong handlerId);

		// Held for the backend lifetime so the GC cannot collect a delegate GObject still holds a pointer to.
		private readonly GSignalHandler screenHandler;
		private readonly GSignalHandlerWithArg displayHandler;
		private readonly nint screenHandlerPtr;
		private readonly nint displayHandlerPtr;
		private readonly Lock gate = new();

		// (instance, handler id) pairs for every connected signal; only touched on the UI thread.
		private readonly List<(nint Instance, ulong Id)> connections = new();
		private bool wanted;
		private bool disposed;

		internal MonitorEventBackend()
		{
			screenHandler = OnScreenChanged;
			displayHandler = OnDisplayMonitorChanged;
			screenHandlerPtr = Marshal.GetFunctionPointerForDelegate(screenHandler);
			displayHandlerPtr = Marshal.GetFunctionPointerForDelegate(displayHandler);
		}

		public Action Sink { get; set; }

		public void Start()
		{
			lock (gate)
			{
				if (wanted || disposed)
					return;

				wanted = true;
			}

			Script.PostToUIThread(ConnectOnUI);
		}

		public void Stop()
		{
			lock (gate)
			{
				if (!wanted)
					return;

				wanted = false;
			}

			Script.PostToUIThread(DisconnectOnUI);
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				wanted = false;
			}

			// Synchronous, unlike Stop: Script.Dispose() tears the UI thread down right after this, so a posted
			// disconnect would never run and GObject would keep calling a collected delegate.
			Script.InvokeOnUIThread(DisconnectOnUI);
			Sink = null;
		}

		private void ConnectOnUI()
		{
			// `connections` is UI-thread-only, so it needs no lock here; `wanted`/`disposed` are set from arbitrary
			// threads by Start/Stop/Dispose and do.
			bool go;

			lock (gate)
				go = wanted && !disposed;

			if (!go || connections.Count > 0)
				return;

			try
			{
				var display = gdk_display_get_default();

				if (display != 0)
				{
					Connect(display, "monitor-added", displayHandlerPtr);
					Connect(display, "monitor-removed", displayHandlerPtr);
				}

				var screen = gdk_screen_get_default();

				if (screen != 0)
				{
					// Deprecated in GTK 3.22 in favour of per-GdkMonitor notifies, but still emitted, and far
					// simpler than re-connecting notify handlers to a monitor set that changes underneath us.
					Connect(screen, "monitors-changed", screenHandlerPtr);
					Connect(screen, "size-changed", screenHandlerPtr);
				}
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"GDK monitor signal connection failed: {ex.Message}");
			}
		}

		private void Connect(nint instance, string signal, nint handlerPtr)
		{
			var id = g_signal_connect_data(instance, signal, handlerPtr, 0, 0, 0);

			if (id != 0)
				connections.Add((instance, id));
		}

		private void DisconnectOnUI()
		{
			try
			{
				foreach (var (instance, id) in connections)
					g_signal_handler_disconnect(instance, id);
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"GDK monitor signal disconnection failed: {ex.Message}");
			}
			finally
			{
				connections.Clear();
			}
		}

		private void OnScreenChanged(nint instance, nint userData) => Sink?.Invoke();

		private void OnDisplayMonitorChanged(nint instance, nint arg1, nint userData) => Sink?.Invoke();
	}
}
#endif
