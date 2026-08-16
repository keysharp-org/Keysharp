using static Keysharp.Builtins.WindowHelper;

namespace Keysharp.Builtins
{
	internal static class WindowSearch
	{
		internal static WindowInfoBase SearchControl(object ctrl, object title, object text, object excludeTitle, object excludeText, bool throwifnull = true)
		{
			var (parsed, ptr) = CtrlTonint(ctrl);
			var script = Script.TheScript;

			if (parsed)
			{
#if !WINDOWS
				if (Control.FromHandle(ptr) is Control ks)
					return new ControlInfo(ks);
#endif
				if (WindowQuery.IsWindow(ptr))
					return WindowQuery.CreateWindow(ptr);
				else if (throwifnull && !script.IsMainWindowClosing)
					_ = Errors.TargetErrorOccurred($"Could not find child control with handle: {ptr}");

				return null;
			}

			var parent = SearchWindow(title, text, excludeTitle, excludeText, true);

			if (ctrl == null)
				return parent;

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

			if (childitem == null && throwifnull && !script.IsMainWindowClosing)
			{
				_ = Errors.TargetErrorOccurred("Could not find child control using text or class name match \"" + s + $"\"", title, text, excludeTitle, excludeText);//Can't use interpolated string here because the AStyle formatter misinterprets it.
				return default;
			}

			return childitem;
		}

		internal static WindowInfoBase SearchWindow(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				bool throwifnull,
				bool last = false)
		{
			var script = Script.TheScript;
			var win = WindowQuery.FindWindow(winTitle, winText, excludeTitle, excludeText, last);

			if (win == null && throwifnull && !script.IsMainWindowClosing)
			{
				_ = Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);
				return default;
			}

			return win;
		}

		internal static WindowInfoBase SearchActiveWindow(SearchCriteria criteria, bool emptyMatchesActive = false)
		{
			var activeWindow = WindowQuery.ActiveWindow;

			if (activeWindow == null || !activeWindow.IsSpecified)
				return null;

			return (emptyMatchesActive && criteria.IsEmpty) || activeWindow.Equals(criteria) ? activeWindow : null;
		}

		internal static List<WindowInfoBase> SearchWindows(object winTitle = null,
				object winText = null,
				object excludeTitle = null,
				object excludeText = null)
		{
			var (windows, _) = WindowQuery.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);
			return windows;
		}

		internal static object WinGetControlsHelper(bool nn,
				object winTitle,
				object winText,
				object excludeTitle,
				object excludeText)
		{
			var script = Script.TheScript;
			var win = WindowQuery.FindWindow(winTitle, winText, excludeTitle, excludeText);

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
			else if (!script.IsMainWindowClosing)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);

			return DefaultObject;
		}
	}
}
