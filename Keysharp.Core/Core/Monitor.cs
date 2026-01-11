namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for monitor-related functions.
	/// </summary>
	public static class Monitor
	{

#if WINDOWS
		private static Forms.Screen[] AllScreens => Forms.Screen.AllScreens;
#else
		private static Forms.Screen[] AllScreens => Forms.Screen.Screens.ToArray();
#endif
		/// <summary>
		/// <see cref="MonitorGet(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGet()
		{
			object left = null, top = null, right = null, bottom = null;
			return MonitorGet(null, left, top, right, bottom);
		}

		/// <summary>
		/// <see cref="MonitorGet(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGet(object obj)
		{
			object left = null, top = null, right = null, bottom = null;
			return MonitorGet(obj, left, top, right, bottom);
		}

		/// <summary>
		/// <see cref="MonitorGet(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGet(object obj, [ByRef] object left)
		{
			object top = null, right = null, bottom = null;
			return MonitorGet(obj, left, top, right, bottom);
		}

		/// <summary>
		/// <see cref="MonitorGet(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGet(object obj, [ByRef] object left, [ByRef] object top)
		{
			object right = null, bottom = null;
			return MonitorGet(obj, left, top, right, bottom);
		}

		/// <summary>
		/// <see cref="MonitorGet(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGet(object obj, [ByRef] object left, [ByRef] object top, [ByRef] object right)
		{
			object bottom = null;
			return MonitorGet(obj, left, top, right, bottom);
		}

		/// <summary>
		/// Checks if the specified monitor exists and optionally retrieves its bounding coordinates.
		/// </summary>
		/// <param name="n">If omitted, the primary monitor will be used. Otherwise, specify the monitor number, between 1 and the number returned by <see cref="MonitorGetCount"/>.</param>
		/// <param name="left">The left bounding coordinate of the specified monitor.</param>
		/// <param name="top">The top bounding coordinate of the specified monitor.</param>
		/// <param name="right">The right bounding coordinate of the specified monitor.</param>
		/// <param name="bottom">The bottom bounding coordinate of the specified monitor.</param>
		/// <returns>The monitor number which is the same as n unless n was omitted.</returns>
		public static object MonitorGet(object n, [ByRef] object left, [ByRef] object top, [ByRef] object right, [ByRef] object bottom)
		{
			left ??= VarRef.Empty; top ??= VarRef.Empty; right ??= VarRef.Empty; bottom ??= VarRef.Empty;
			var monitorIndex = n.Al(-1L);
			Forms.Screen screen;

			if (monitorIndex > 0 && monitorIndex <= AllScreens.Length)
				screen = AllScreens[monitorIndex - 1];
			else
				screen = Forms.Screen.PrimaryScreen;

			Script.SetPropertyValue(left, "__Value", (long)screen.Bounds.Left);
            Script.SetPropertyValue(top, "__Value", (long)screen.Bounds.Top);
            Script.SetPropertyValue(right, "__Value", (long)screen.Bounds.Right);
            Script.SetPropertyValue(bottom, "__Value", (long)screen.Bounds.Bottom);
			return monitorIndex > 0L ? monitorIndex : 1L;
		}

		/// <summary>
		/// Returns the total number of monitors.
		/// </summary>
		/// <returns>The total number of monitors.</returns>
		public static long MonitorGetCount() => AllScreens.Length;

		/// <summary>
		/// Returns the operating system's name of the specified monitor.
		/// </summary>
		/// <param name="n">If omitted, the primary monitor will be used. Otherwise, specify the monitor number, between 1 and the number returned by <see cref="MonitorGetCount"/>.</param>
		/// <returns>A string</returns>
		public static string MonitorGetName(object n = null)
		{
			var monitorIndex = n.Al(-1L);
#if WINDOWS
			if (monitorIndex > 0 && monitorIndex <= AllScreens.Length)
				return AllScreens[monitorIndex - 1].DeviceName ?? "";

			return System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
#else
			if (monitorIndex > 0 && monitorIndex <= AllScreens.Length)
				return AllScreens[monitorIndex - 1].ID ?? "";

			return Forms.Screen.PrimaryScreen.ID ?? "";
#endif
		}

		/// <summary>
		/// Returns the number of the primary monitor.
		/// </summary>
		/// <returns>The number of the primary monitor. In a single-monitor system, this will be always 1.</returns>
		public static long MonitorGetPrimary()
		{
			long i;

			var allScreens = AllScreens;
			for (i = 0L; i < allScreens.Length; i++)
			{
				if (allScreens[i] == Forms.Screen.PrimaryScreen)
					break;
			}

			return i + 1L;
		}

		/// <summary>
		/// <see cref="MonitorGetWorkArea(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGetWorkArea()
		{
			object l = null, t = null, r = null, b = null;
			return MonitorGetWorkArea(null, l, t, r, b);
		}

		/// <summary>
		/// <see cref="MonitorGetWorkArea(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGetWorkArea(object n)
		{
			object l = null, t = null, r = null, b = null;
			return MonitorGetWorkArea(n, l, t, r, b);
		}

		/// <summary>
		/// <see cref="MonitorGetWorkArea(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGetWorkArea(object obj, [ByRef] object left)
		{
			object t = null, r = null, b = null;
			return MonitorGetWorkArea(obj, left, t, r, b);
		}

		/// <summary>
		/// <see cref="MonitorGetWorkArea(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGetWorkArea(object obj, [ByRef] object left, [ByRef] object top)
		{
			object r = null, b = null;
			return MonitorGetWorkArea(obj, left, top, r, b);
		}

		/// <summary>
		/// <see cref="MonitorGetWorkArea(object, object, object, object, object)"/>
		/// </summary>
		public static object MonitorGetWorkArea(object obj, [ByRef] object left, [ByRef] object top, [ByRef] object right)
		{
			object b = null;
			return MonitorGetWorkArea(obj, left, top, right, b);
		}

		/// <summary>
		/// Checks if the specified monitor exists and optionally retrieves the bounding coordinates of its working area.
		/// </summary>
		/// <param name="n">If omitted, the primary monitor will be used. Otherwise, specify the monitor number, between 1 and the number returned by <see cref="MonitorGetCount"/>.</param>
		/// <param name="left">The left bounding coordinate of the work area of the specified monitor.</param>
		/// <param name="top">The top bounding coordinate of the work area of the specified monitor.</param>
		/// <param name="right">The right bounding coordinate of the work area of the specified monitor.</param>
		/// <param name="bottom">The bottom bounding coordinate of the work area of the specified monitor.</param>
		/// <returns>The monitor number which is the same as n unless n was omitted.</returns>
		public static object MonitorGetWorkArea(object n, [ByRef] object left, [ByRef] object top, [ByRef] object right, [ByRef] object bottom)
		{
			var monitorIndex = n.Al(-1L);
			Forms.Screen screen;

			if (monitorIndex > 0 && monitorIndex <= AllScreens.Length)
				screen = AllScreens[monitorIndex - 1];
			else
				screen = Forms.Screen.PrimaryScreen;

			Script.SetPropertyValue(left, "__Value", (long)screen.WorkingArea.Left);
            Script.SetPropertyValue(top, "__Value", (long)screen.WorkingArea.Top);
            Script.SetPropertyValue(right, "__Value", (long)screen.WorkingArea.Right);
            Script.SetPropertyValue(bottom, "__Value", (long)screen.WorkingArea.Bottom);
			return monitorIndex > 0L ? monitorIndex : 1L;
		}
	}
}