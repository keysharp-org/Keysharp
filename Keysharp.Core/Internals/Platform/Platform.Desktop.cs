namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>
		/// Session type and desktop-environment detection, read once at startup from the freedesktop
		/// environment variables. Immutable; safe to read from any thread. Consumed by the host's resolution
		/// and the input/window layers — it is detection state, not a dispatch surface.
		/// </summary>
		internal static class Desktop
		{
#if WINDOWS
			internal static bool IsWaylandSession => false;
			internal static bool IsX11Available => false;
			internal static bool IsGnome => false;
			internal static bool IsKde => false;
			internal static bool IsXfce => false;
			internal static bool IsMate => false;
			internal static bool IsCinnamon => false;
			internal static bool IsLxqt => false;
			internal static bool IsLxde => false;
#else
			private static readonly bool isGnome, isKde, isXfce, isMate, isCinnamon, isLxqt, isLxde;

			internal static bool IsGnome => isGnome;
			internal static bool IsKde => isKde;
			internal static bool IsXfce => isXfce;
			internal static bool IsMate => isMate;
			internal static bool IsCinnamon => isCinnamon;
			internal static bool IsLxqt => isLxqt;
			internal static bool IsLxde => isLxde;

			internal static bool IsX11Available =>
#if OSX
				false;
#else
				!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
#endif

			// XDG_SESSION_TYPE is authoritative: an X11 session can inherit a stray WAYLAND_DISPLAY (a nested
			// XWayland/leaked env var), and OR-ing WAYLAND_DISPLAY unconditionally would then wrongly route that
			// X11 session down the Wayland window/mouse-injection paths. Only trust WAYLAND_DISPLAY when the
			// session type is unset (some minimal/embedded sessions don't set it).
			internal static readonly bool IsWaylandSession = IsWaylandSessionType();

			private static bool IsWaylandSessionType()
			{
				var type = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");

				if (string.Equals(type, "wayland", StringComparison.OrdinalIgnoreCase))
					return true;

				return string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
			}

			static Desktop()
			{
#if LINUX
				// Detect the desktop environment from the standard freedesktop variables. XDG_CURRENT_DESKTOP is
				// the canonical source (a colon-separated list, e.g. "ubuntu:GNOME"); fall back to the older
				// session variables. An unrecognized DE leaves them all false.
				var de = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")
						  ?? Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP")
						  ?? Environment.GetEnvironmentVariable("DESKTOP_SESSION")
						  ?? string.Empty).ToLowerInvariant();

				if (de.Contains("gnome") || de.Contains("unity"))
					isGnome = true;
				else if (de.Contains("kde") || de.Contains("plasma"))
					isKde = true;
				else if (de.Contains("xfce"))
					isXfce = true;
				else if (de.Contains("mate"))
					isMate = true;
				else if (de.Contains("cinnamon"))
					isCinnamon = true;
				else if (de.Contains("lxqt"))
					isLxqt = true;
				else if (de.Contains("lxde"))
					isLxde = true;
#endif
			}
#endif
		}
	}
}
