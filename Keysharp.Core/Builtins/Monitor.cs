using Keysharp.Internals;

namespace Keysharp.Builtins
{
	/// <summary>Public interface for monitor-related functions.</summary>
	public static class Monitor
	{
		// Platform.Screen is the only monitor-topology source. It returns a fresh snapshot because monitor
		// identity, bounds, work areas and scale can all change while a script is running.
		internal static DisplayInfo[] AllDisplays => Platform.Screen.GetDisplays().ToArray();

		/// <summary>The 1-based monitor number of one display within a snapshot it came from, or 1 when it somehow
		/// is not in there. Every monitor lookup ends with this step, so it lives in one place.</summary>
		internal static long IndexOf(DisplayInfo[] displays, DisplayInfo display)
		{
			for (var i = 0; i < displays.Length; i++)
				if (displays[i].Equals(display))
					return i + 1L;

			return 1L;
		}

		private static DisplayInfo ResolvePrimaryDisplay(DisplayInfo[] displays)
		{
			// An OSError rather than a raw InvalidOperationException so a script can catch it: only a Keysharp
			// Error is unwrapped for `catch`. As above, a suppressing OnError handler falls through to a zero
			// display rather than leaving the caller with nothing.
			if (displays.Length == 0)
			{
				_ = Errors.OSErrorOccurredWithMessage("No monitors are available.");
				return default;
			}

			foreach (var display in displays)
				if (display.IsPrimary)
					return display;

			// GDK/Wayland does not always expose a designated primary output. Compositors conventionally put it
			// at the logical origin; the first output is the final deterministic fallback.
			foreach (var display in displays)
				if (display.Bounds.X == 0 && display.Bounds.Y == 0)
					return display;

			return displays[0];
		}

		internal static (DisplayInfo Display, long MonitorIndex) ResolveDisplay(object n)
			=> ResolveDisplay(n, AllDisplays);

		/// <summary>
		/// Selects one display out of an already-taken snapshot. Unset or 0 selects the primary monitor;
		/// any other out-of-range index is a ValueError, matching AHK's <c>FR_E_ARG(0)</c> in
		/// <c>MonitorGet</c>/<c>MonitorGetName</c> rather than silently substituting the primary.
		/// </summary>
		internal static (DisplayInfo Display, long MonitorIndex) ResolveDisplay(object n, DisplayInfo[] displays)
		{
			var monitorIndex = n.Al(0L);

			if (monitorIndex > 0 && monitorIndex <= displays.Length)
				return (displays[monitorIndex - 1], monitorIndex);

			// A script may install an OnError handler that swallows the error and continues, so keep the old
			// primary-monitor fallback on that path rather than leaving the caller with nothing.
			if (monitorIndex != 0)
				_ = Errors.ValueErrorOccurred(displays.Length == 0
					? $"Invalid monitor index of {monitorIndex}. No monitors are available."
					: $"Invalid monitor index of {monitorIndex}. Valid monitor indices are 1 to {displays.Length}.");

			var primary = ResolvePrimaryDisplay(displays);
			return (primary, IndexOf(displays, primary));
		}

		internal static (DisplayInfo Display, long MonitorIndex) ResolveDisplayForPoint(int x, int y)
			=> ResolveDisplayForRect(new ScreenRect(x, y, 0, 0), AllDisplays);

		/// <summary>
		/// The display a rectangle belongs to — the one it overlaps most, or the nearest when it overlaps none
		/// (which is what makes a bare point in a gap between monitors resolve sensibly). Takes the snapshot so a
		/// caller that already enumerated does not enumerate again.
		/// </summary>
		internal static (DisplayInfo Display, long MonitorIndex) ResolveDisplayForRect(ScreenRect rect,
			DisplayInfo[] displays)
		{
			if (!DisplayTopology.TryFind(displays, rect, out var selected))
			{
				_ = Errors.OSErrorOccurredWithMessage("No monitors are available.");
				return (selected, 1L);
			}

			return (selected, IndexOf(displays, selected));
		}

		internal static (long Width, long Height) GetPrimaryScreenSize()
		{
			var (display, _) = ResolveDisplay(null);
			return (display.Bounds.Width, display.Bounds.Height);
		}

		/// <summary>Returns one display's bounds in native screen coordinates.</summary>
		internal static (long Left, long Top, long Width, long Height) GetMonitorBounds(object n)
		{
			var (display, _) = ResolveDisplay(n);
			var bounds = display.Bounds;
			return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
		}

		internal static (long Width, long Height) GetPrimaryWorkAreaSize()
		{
			var wa = GetPrimaryWorkArea();
			return (wa.Width, wa.Height);
		}

		/// <summary>The primary display's working area in native screen coordinates.</summary>
		internal static Rectangle GetPrimaryWorkArea()
		{
			var (display, _) = ResolveDisplay(null);
			return display.WorkArea.ToRectangle();
		}

		/// <summary>
		/// Returns the union of all displays, including its possibly-negative origin, in native screen coordinates.
		/// </summary>
		internal static (long Left, long Top, long Width, long Height) GetVirtualScreenBounds()
		{
			var displays = AllDisplays;

			if (displays.Length == 0)
				return (0L, 0L, 0L, 0L);

			var left = displays.Min(s => s.Bounds.X);
			var top = displays.Min(s => s.Bounds.Y);
			var right = displays.Max(s => s.Bounds.Right);
			var bottom = displays.Max(s => s.Bounds.Bottom);
			return (left, top, right - left, bottom - top);
		}

		/// <summary>Gets one monitor's native screen-coordinate bounds.</summary>
		public static object MonitorGet(object n = null, [ByRef] object left = null, [ByRef] object top = null,
			[ByRef] object right = null, [ByRef] object bottom = null)
		{
			var (display, monitorIndex) = ResolveDisplay(n);
			var bounds = display.Bounds;

			if (left != null) Script.SetPropertyValue(left, "__Value", (long)bounds.X);
			if (top != null) Script.SetPropertyValue(top, "__Value", (long)bounds.Y);
			if (right != null) Script.SetPropertyValue(right, "__Value", (long)bounds.Right);
			if (bottom != null) Script.SetPropertyValue(bottom, "__Value", (long)bounds.Bottom);
			return monitorIndex;
		}

		/// <summary>Returns the current number of displays.</summary>
		public static long MonitorGetCount() => AllDisplays.Length;

		/// <summary>Returns the platform's stable name for one display snapshot.</summary>
		public static string MonitorGetName(object n = null)
		{
			var (display, _) = ResolveDisplay(n);
			return display.Name ?? "";
		}

		/// <summary>Returns the current primary monitor index.</summary>
		public static long MonitorGetPrimary()
		{
			var displays = AllDisplays;
			return IndexOf(displays, ResolvePrimaryDisplay(displays));
		}

		/// <summary>Gets one monitor's work-area bounds in native screen coordinates.</summary>
		public static object MonitorGetWorkArea(object n = null, [ByRef] object left = null, [ByRef] object top = null,
			[ByRef] object right = null, [ByRef] object bottom = null)
		{
			var (display, monitorIndex) = ResolveDisplay(n);
			var workArea = display.WorkArea;

			if (left != null) Script.SetPropertyValue(left, "__Value", (long)workArea.X);
			if (top != null) Script.SetPropertyValue(top, "__Value", (long)workArea.Y);
			if (right != null) Script.SetPropertyValue(right, "__Value", (long)workArea.Right);
			if (bottom != null) Script.SetPropertyValue(bottom, "__Value", (long)workArea.Bottom);
			return monitorIndex;
		}
	}

}
