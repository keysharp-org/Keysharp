#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// Shared GDK and layer-shell topology invalidation. Native callbacks advance the generation immediately;
	/// subscribers run later on the UI thread and MonitorEventManager discards duplicate topology notifications.
	/// </summary>
	internal static class LinuxDisplayChanges
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ScreenSignal(nint instance, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void DisplaySignal(nint instance, nint monitor, nint userData);

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_display_get_default();

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_screen_get_default();

		[DllImport("libgobject-2.0.so.0")]
		private static extern ulong g_signal_connect_data(nint instance,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal, nint handler,
			nint data, nint destroyData, int connectFlags);

		private const int NotRequested = 0, Requested = 1, Connected = 2;

		// Connected for the life of the process, so the delegates GObject calls are static.
		private static readonly ScreenSignal onScreenChanged = (_, _) => Raise();
		private static readonly DisplaySignal onMonitorChanged = (_, _, _) => Raise();
		private static readonly Action raiseChanged = RaiseChanged;
		private static long generation;
		private static int connection;

		/// <summary>Raised on the UI thread after each change.</summary>
		internal static event Action Changed;

		internal static long Generation => Interlocked.Read(ref generation);

		// Without connected signals, a snapshot must be read fresh.
		internal static bool IsCurrent(long snapshotGeneration)
		{
			if (Volatile.Read(ref connection) != Connected)
			{
				EnsureConnected();
				return false;
			}

			return snapshotGeneration == Generation;
		}

		internal static void EnsureConnected()
		{
			if (Volatile.Read(ref connection) != NotRequested || Application.Instance is not { } app
					|| Interlocked.CompareExchange(ref connection, Requested, NotRequested) != NotRequested)
				return;

			if (app.IsUIThread)
				Connect();
			else
				app.AsyncInvoke(Connect);
		}

		private static void Connect()
		{
			try
			{
				var display = gdk_display_get_default();
				var screen = gdk_screen_get_default();

				// A failed connection stays requested, so snapshots keep being read fresh rather than trusted.
				if (display == 0 || screen == 0
						|| !TryConnect(display, "monitor-added", onMonitorChanged)
						|| !TryConnect(display, "monitor-removed", onMonitorChanged)
						// These still cover geometry changes without tracking per-monitor notify handlers.
						|| !TryConnect(screen, "monitors-changed", onScreenChanged)
						|| !TryConnect(screen, "size-changed", onScreenChanged))
					return;

				// A snapshot taken before this point saw no signal, so it must not count as current.
				_ = Interlocked.Increment(ref generation);
				Volatile.Write(ref connection, Connected);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"GDK monitor signal connection failed: {ex.Message}");
			}
		}

		private static bool TryConnect(nint instance, string signal, Delegate handler)
			=> g_signal_connect_data(instance, signal, Marshal.GetFunctionPointerForDelegate(handler), 0, 0, 0) != 0;

		/// <summary>Records a display change. Callable from any thread; <see cref="Changed"/> always runs later on the
		/// UI thread, never inside the native callback that reported the change.</summary>
		internal static void Raise()
		{
			_ = Interlocked.Increment(ref generation);

			if (Changed != null)
				Application.Instance?.AsyncInvoke(raiseChanged);
		}

		private static void RaiseChanged()
		{
			foreach (Action handler in Changed?.GetInvocationList() ?? [])
			{
				try { handler(); }
				catch (Exception ex) { Diagnostics.Debug.WriteLine($"Display change notification failed: {ex.Message}"); }
			}
		}
	}
}
#endif
