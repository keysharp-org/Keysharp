using static Keysharp.Builtins.WindowHelper;

namespace Keysharp.Builtins
{
	internal static class WindowSearch
	{
		/// <summary>
		/// The control a search names, or the window itself for a blank control. Null when there is none, having
		/// raised a TargetError if <paramref name="throwifnull"/>, or when a parameter raised an error the script
		/// continued; <see cref="TrySearchControl"/> tells the two apart.
		/// </summary>
		internal static WindowInfoBase SearchControl(object ctrl, object title, object text, object excludeTitle, object excludeText, bool throwifnull = true)
			=> TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, throwifnull) ? item : null;

		/// <summary>
		/// As <see cref="SearchControl"/>. False when an error was raised and the script continued it: a
		/// parameter's, the TargetError for a missing window, or with <paramref name="throwifnull"/> the one for a
		/// missing control. The caller then returns its empty value at once without raising another.
		/// </summary>
		internal static bool TrySearchControl(object ctrl, object title, object text, object excludeTitle, object excludeText, out WindowInfoBase item, bool throwifnull = false)
		{
			item = null;
			EnsureWindowMonitoringPermission("control query");

			if (!TryCtrlTonint(ctrl, out var parsed, out var ptr))
				return false;

			var script = Script.TheScript;

			if (parsed)
			{
#if !WINDOWS
				if (Control.FromHandle(ptr) is Control ks)
				{
					item = new ControlInfo(ks);
					return true;
				}
#endif
				if (WindowQuery.IsWindow(ptr))
					item = WindowQuery.CreateWindow(ptr);
				else if (throwifnull && !script.IsTearingDown)
				{
					_ = Errors.TargetErrorOccurred($"Could not find child control with handle: {ptr}");
					return false;
				}

				return true;
			}

			if (SearchWindow(title, text, excludeTitle, excludeText, true) is not WindowInfoBase parent)
				return false;

			var s = ctrl as string;

			if (string.IsNullOrEmpty(s))
			{
				item = parent;
				return true;
			}

			var found = WindowQuery.ControlExist(parent.Handle, s);

			if (found == 0 && throwifnull && !script.IsTearingDown)
			{
				_ = Errors.TargetErrorOccurred("Could not find child control using text or class name match \"" + s + $"\"", title, text, excludeTitle, excludeText);//Can't use interpolated string here because the AStyle formatter misinterprets it.
				return false;
			}

			item = found != 0 ? WindowQuery.CreateWindow(found) : null;
			return true;
		}

		/// <summary>
		/// The window a search names. Null when there is none, having raised a TargetError if
		/// <paramref name="throwifnull"/>, or when a parameter raised an error the script continued;
		/// <see cref="TrySearchWindow"/> tells the two apart.
		/// </summary>
		internal static WindowInfoBase SearchWindow(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				bool throwifnull,
				bool last = false)
			=> TrySearchWindow(winTitle, winText, excludeTitle, excludeText, out var win, throwifnull, last) ? win : null;

		/// <summary>
		/// As <see cref="SearchWindow"/>. False when an error was raised and the script continued it: a
		/// parameter's, or with <paramref name="throwifnull"/> the TargetError for a missing window. The caller then
		/// returns its empty value at once without raising another.
		/// </summary>
		internal static bool TrySearchWindow(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				out WindowInfoBase win,
				bool throwifnull = false,
				bool last = false)
		{
			EnsureWindowMonitoringPermission("window query");

			if (!WindowQuery.TryFindWindow(winTitle, winText, excludeTitle, excludeText, out win, last))
				return false;

			if (win == null && throwifnull && !Script.TheScript.IsTearingDown)
			{
				_ = Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);
				return false;
			}

			return true;
		}

		/// <summary>
		/// AutoHotkey's WinActive: the active window when it matches <paramref name="criteria"/>, which makes it the
		/// Last Found Window, or for null criteria when it already is the Last Found Window.
		/// </summary>
		internal static WindowInfoBase SearchActiveWindow(SearchCriteria criteria, bool updateLastFound = true)
		{
			EnsureWindowMonitoringPermission("active window query");
			var activeWindow = WindowQuery.ActiveWindow;

			if (activeWindow == null || !activeWindow.IsSpecified)
				return null;

			if (criteria == null)
				return WindowQuery.LastFound?.Handle == activeWindow.Handle ? activeWindow : null;

			var settings = WindowSearchSettings.Current.With(criteria);

			if (criteria.IsOnlyActive ? !settings.DetectHiddenWindows && !activeWindow.Visible : !activeWindow.Equals(criteria, settings))
				return null;

			if (updateLastFound)
				WindowQuery.LastFound = activeWindow;

			return activeWindow;
		}

		/// <summary>
		/// Every window a search names. False when a parameter raised an error the script continued, and the
		/// caller then returns its empty value at once.
		/// </summary>
		internal static bool TrySearchWindows(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				out List<WindowInfoBase> windows)
		{
			EnsureWindowMonitoringPermission("window list query");
			return WindowQuery.TryFindWindowGroup(winTitle, winText, excludeTitle, excludeText, out windows, out _, true);
		}

		internal static object WinGetControlsHelper(bool nn,
				object winTitle,
				object winText,
				object excludeTitle,
				object excludeText)
		{
			EnsureWindowMonitoringPermission("window control list query");
			var script = Script.TheScript;

			if (!WindowQuery.TryFindWindow(winTitle, winText, excludeTitle, excludeText, out var win))
				return DefaultObject;

			if (win != null)
			{
				if (!Platform.Window.TryEnumerateChildren(win.Handle, out var controls) || controls.Count == 0)
					return DefaultObject;

				var arr = new Array()
				{
					Capacity = controls.Count
				};
				var il = arr as IList;

				if (nn)
				{
					foreach (var (_, classNN) in WindowQuery.ClassNNs(controls))
						il.Add(classNN);
				}
				else
				{
					foreach (var ctrl in controls)
						il.Add(ctrl.ToInt64());
				}

				return arr;
			}
			else if (!script.IsTearingDown)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);

			return DefaultObject;
		}
	}
}
