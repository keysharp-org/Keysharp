#if LINUX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Small per-operation throttle for bridge diagnostics. A dead compositor connection can be exercised
	/// by every overlay frame, so logging every failure would make the useful first error impossible to find.</summary>
	internal sealed class WaylandDiagnosticThrottle
	{
		private readonly long intervalMs;
		private readonly Dictionary<string, (long Last, int Suppressed)> entries = [];
		private readonly object gate = new();

		internal WaylandDiagnosticThrottle(long intervalMs) => this.intervalMs = Math.Max(0, intervalMs);

		internal bool TryAcquire(string key, long now, out int suppressed)
		{
			lock (gate)
			{
				if (entries.TryGetValue(key, out var entry) && now >= entry.Last && now - entry.Last < intervalMs)
				{
					entries[key] = (entry.Last, entry.Suppressed + 1);
					suppressed = 0;
					return false;
				}

				suppressed = entry.Suppressed;
				entries[key] = (now, 0);
				return true;
			}
		}
	}

	/// <summary>Failure diagnostics for compositor D-Bus bridges, sent to the same debug output used by inputd.</summary>
	internal static class WaylandBridgeDiagnostics
	{
		private static readonly WaylandDiagnosticThrottle throttle = new(5000);

		internal static void Failure(string bridge, string operation, string detail = null)
		{
			var key = $"{bridge}:{operation}";

			if (!throttle.TryAcquire(key, Environment.TickCount64, out var suppressed))
				return;

			var message = $"Keysharp {bridge} bridge: {operation} failed";

			if (!string.IsNullOrWhiteSpace(detail))
				message += $": {detail}";

			if (suppressed > 0)
				message += $" ({suppressed} repeated failure{(suppressed == 1 ? "" : "s")} suppressed)";

			try { Diagnostics.Debug.WriteLine(message); } catch { }
		}

		internal static string Describe(Exception ex)
			=> ex == null ? "unknown error" : $"{ex.GetType().Name}: {ex.Message}";
	}
}
#endif
