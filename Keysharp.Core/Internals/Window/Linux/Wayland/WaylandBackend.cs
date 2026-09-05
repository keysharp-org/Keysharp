#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Selects the keysharp-desktop backend reported by the service.</summary>
	internal static class WaylandBackend
	{
		private const int InitialRetryDelayMs = 1000;
		private const int MaximumRetryDelayMs = 30_000;
		private static readonly object sync = new();
		private static IWaylandBackend current;
		private static bool probing;
		private static long nextProbeAt;
		private static int retryDelayMs = InitialRetryDelayMs;

		internal static IWaylandBackend Current
		{
			get
			{
				lock (sync)
				{
					if (current != null)
						return current;

					if (probing || Environment.TickCount64 < nextProbeAt)
						return null;

					probing = true;
				}

				IWaylandBackend candidate;

				try
				{
					candidate = Probe();
				}
				catch (Exception exception)
				{
					Diagnostics.Debug.WriteLine($"Wayland backend probe failed: {exception.Message}");
					candidate = null;
				}

				lock (sync)
				{
					probing = false;
					current = candidate;

					if (current == null && ShouldRetryProbe())
					{
						nextProbeAt = Environment.TickCount64 + retryDelayMs;
						retryDelayMs = Math.Min(retryDelayMs * 2, MaximumRetryDelayMs);
					}
					else
					{
						nextProbeAt = long.MaxValue;
						retryDelayMs = InitialRetryDelayMs;
					}

					return current;
				}
			}
		}

		/// <summary>Test/process-host lifecycle reset. Call only when no platform operation is in flight.</summary>
		internal static void Reset()
		{
			IWaylandBackend previous;

			lock (sync)
			{
				previous = current;
				current = null;
				probing = false;
				nextProbeAt = 0;
				retryDelayMs = InitialRetryDelayMs;
			}

			WaylandOwnToplevels.Reset();
			(previous as IDisposable)?.Dispose();
			WaylandLayerShellClient.Reset();
		}

		private static bool ShouldRetryProbe()
			=> Platform.Desktop.IsWaylandSession;

		private static IWaylandBackend Probe()
		{
			if (!Platform.Desktop.IsWaylandSession)
				return null;

			return ProbeReportedBackend();
		}

		private static IWaylandBackend ProbeReportedBackend()
		{
			if (!DesktopClient.TryProbeBackend(out var backend))
				return null;

			return backend switch
			{
				DesktopClient.Backend.Kwin => new KWinBrokerBackend(),
				DesktopClient.Backend.Gnome => new GnomeBackend(),
				DesktopClient.Backend.Cinnamon => new CinnamonBackend(),
				DesktopClient.Backend.X11 => DesktopBackend.X11,
				DesktopClient.Backend.Generic => new DesktopBackend("generic",
					"generic Wayland (keysharp-desktop)"),
				_ => null,
			};
		}
	}
}
#endif
