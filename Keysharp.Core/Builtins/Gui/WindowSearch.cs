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

			if (ctrl == null)
			{
				item = parent;
				return true;
			}

			var sc = new SearchCriteria();
			string classortext = null;
			string s = ctrl as string;

			if (!string.IsNullOrEmpty(s))
			{
				if (char.IsDigit(s[^1]))
					sc.ClassName = s;
				else
					sc.Text = s;

				classortext = s;
			}

			var childitem = parent.FirstChild(sc);

			//AHK addresses a control by its ClassNN - the class name followed by its 1-based ordinal among the
			//siblings sharing that class, which is exactly what WinGetControls reports - but criteria matching
			//compares the bare class name, so "Edit1" never matches a control whose class is "Edit". Done here
			//rather than in the criteria match because only a control is ever addressed this way: computing a
			//ClassNN walks the candidate's siblings, which no top-level window search should have to pay.
			if (childitem == null && !string.IsNullOrEmpty(sc.ClassName))
			{
				foreach (var child in parent.ChildWindows)
				{
					if (string.Equals(child.ClassNN, sc.ClassName, StringComparison.OrdinalIgnoreCase))
					{
						childitem = child;
						break;
					}
				}
			}

			if (classortext != null && childitem == null)
			{
				if (string.IsNullOrEmpty(sc.Text))
				{
					sc.Text = sc.ClassName;
					sc.ClassName = "";
				}
				else
				{
					sc.ClassName = sc.Text;
					sc.Text = "";
				}

				childitem = parent.FirstChild(sc);

				if (childitem == null)//Final attempt, just use title.
				{
					//Set DHW unconditionally to true, because otherwise matching will fail
					//if the parent window was matched by pure hWnd and DHW was false
					var tv = Script.TheScript.Threads.CurrentThread.configData;
					var savedDHW = tv.detectHiddenWindows;
					tv.detectHiddenWindows = true;

					try
					{
						if (string.IsNullOrEmpty(sc.Text))
						{
							sc.Title = sc.ClassName;
							sc.ClassName = "";
						}
						else
						{
							sc.Title = sc.Text;
							sc.Text = "";
						}

						childitem = parent.FirstChild(sc);
					}
					finally
					{
						tv.detectHiddenWindows = savedDHW;
					}
				}
			}

			if (childitem == null && throwifnull && !script.IsTearingDown)
			{
				_ = Errors.TargetErrorOccurred("Could not find child control using text or class name match \"" + s + $"\"", title, text, excludeTitle, excludeText);//Can't use interpolated string here because the AStyle formatter misinterprets it.
				return false;
			}

			item = childitem;
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

		internal static WindowInfoBase SearchActiveWindow(SearchCriteria criteria, bool emptyMatchesActive = false)
		{
			EnsureWindowMonitoringPermission("active window query");
			var activeWindow = WindowQuery.ActiveWindow;

			if (activeWindow == null || !activeWindow.IsSpecified)
				return null;

			return (emptyMatchesActive && criteria.IsEmpty) || activeWindow.Equals(criteria) ? activeWindow : null;
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
				var controls = win.ChildWindows;

				if (controls.Count == 0)
					return DefaultObject;

				var arr = new Array()
				{
					Capacity = controls.Count
				};
				var il = arr as IList;

				if (nn)
				{
					foreach (var ctrl in controls)
						il.Add(ctrl.GetClassNN(controls));
				}
				else
				{
					foreach (var ctrl in controls)
						il.Add(ctrl.Handle.ToInt64());
				}

				return arr;
			}
			else if (!script.IsTearingDown)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);

			return DefaultObject;
		}
	}
}
