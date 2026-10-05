#if LINUX
using Keysharp.Internals.Linux;
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
			=> DesktopClient.Current.TryProbeBackend(out var backend) ? Select(backend) : null;

		/// <summary>Maps a broker-reported backend onto its handler. Only ever called on a Wayland session
		/// (see <see cref="Probe"/>), which is what makes the X11 report below mean what it does. Separate from
		/// the probe so the rule can be exercised without a live broker.</summary>
		internal static IWaylandBackend Select(DesktopClient.Backend backend)
		{
			// A broker reporting X11 here is describing a session that is not this one. Its process was started
			// under an earlier X11 login and a systemd user manager that outlived the logout carried it into
			// this Wayland session, where its own session identity is frozen at the values it was exec'd with.
			// Taking it at its word would pin this script to XWayland for as long as it runs, because a backend
			// is probed once and then cached for the life of the process. Report none instead and let the
			// backoff re-probe: the broker restarts itself once it notices the same mismatch.
			if (backend == DesktopClient.Backend.X11)
			{
				WaylandBridgeDiagnostics.Failure("keysharp-desktop", "session backend probe",
					"the broker reports an X11 session while this one is Wayland, so it is left over from a "
					+ "previous session. It should restart itself; if it does not, run "
					+ "\"systemctl --user restart keysharp-desktop.service\".");
				return null;
			}

			return backend switch
			{
				DesktopClient.Backend.Kwin => new KWinBrokerBackend(),
				DesktopClient.Backend.Gnome => new GnomeBackend(),
				DesktopClient.Backend.Cinnamon => new CinnamonBackend(),
				DesktopClient.Backend.Generic => new DesktopBackend("generic",
					"generic Wayland (keysharp-desktop)"),
				_ => null,
			};
		}
	}
}
#endif
