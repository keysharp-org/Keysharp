using Keysharp.Builtins;
namespace Keysharp.Internals.Window
{
	/// <summary>A named window group (GroupAdd/GroupActivate). Per-Script state lives on <c>Script.WindowGroups</c>.</summary>
	internal class WindowGroup
	{
		internal Stack<long> activated = new ();
		internal Stack<long> deactivated = new ();
		internal bool lastWasDeactivate = false;
		internal List<SearchCriteria> sc = [];
	}

	/// <summary>
	/// The platform-neutral window search + the item-returning query facade. The per-OS query (active/enumerate/
	/// create/from-point/foreground/focused-control) now lives behind <see cref="Platform.Window"/>, which hands
	/// back the single neutral <see cref="WindowInfo"/> (seeded up front where the backend enumerates in one batch,
	/// lazy otherwise). This houses the Find/Group/LastFound logic that used to sit on the deleted per-OS
	/// WindowManager + WindowManagerBase.
	/// </summary>
	internal static class WindowQuery
	{
		// === query (the per-OS work — and the per-platform cache seeding — lives behind Platform.Window, which
		// returns the neutral WindowInfo already seeded where the backend enumerates in one batch). ===

		public static WindowInfoBase CreateWindow(nint id) => Platform.Window.CreateWindow(id);

		public static WindowInfoBase ActiveWindow => Platform.Window.ActiveWindow();

		public static IEnumerable<WindowInfoBase> EnumerateWindows(bool detectHiddenWindows) => Platform.Window.Enumerate(detectHiddenWindows);

		public static IEnumerable<WindowInfoBase> AllWindows => EnumerateWindows(ThreadAccessors.A_DetectHiddenWindows);

		public static WindowInfoBase ChildWindowFromPoint(POINT location)
			=> Platform.Window.WindowAt(location.X, location.Y);

		public static WindowInfoBase WindowFromPoint(POINT location)
		{
			var child = ChildWindowFromPoint(location);

			if (child == null || !child.IsSpecified)
				return child;

			return child.NonChildParentWindow ?? child;
		}

		public static bool IsWindow(nint handle) => Platform.Window.IsWindow(handle);

		public static nint GetForegroundWindowHandle() => Platform.Window.GetForegroundHandle();

		public static uint GetFocusedCtrlThread(nint aWindow = 0) => Platform.Window.GetFocusedControlThread(aWindow);

		public static IEnumerable<WindowInfoBase> FilterForGroups(IEnumerable<WindowInfoBase> windows)
			=> windows.Where(w => Platform.Window.IncludeInGroups(w.Handle));

		// === neutral search (moved verbatim from WindowManagerBase, repointed to this class) ===

		/// <summary>
		/// The Last Found Window, or null when it no longer exists or DetectHiddenWindows hides it, as AutoHotkey's
		/// GetValidLastUsedWindow: a child window and the script's own Gui are detected regardless.
		/// </summary>
		public static WindowInfoBase LastFound
		{
			get
			{
				var script = Script.TheScript;
				var handle = (nint)script.HwndLastUsed;

				if (handle == 0 || !IsWindow(handle))
					return null;

				var win = CreateWindow(handle);   // own Guis route through Platform.Window (the platform service recognises own-toolkit handles)
				return ThreadAccessors.A_DetectHiddenWindows || win.Visible || win.ParentWindow?.IsSpecified == true
					   || script.GuiData.allGuiHwnds.ContainsKey(handle) ? win : null;
			}
			set => Script.TheScript.HwndLastUsed = value.Handle;
		}

		public static WindowInfoBase FindWindow(SearchCriteria criteria, bool last = false)
		{
			WindowInfoBase found = null;

			if (criteria.IsEmpty || criteria.MatchesNothing)
				return found;

			var settings = WindowSearchSettings.Current.With(criteria);
			var detectHiddenWindows = ShouldDetectHiddenWindows(criteria, settings);

			if (criteria.Active)
			{
				var activeWindow = ActiveWindow;

				if (criteria.IsOnlyActive)
					return activeWindow is WindowInfoBase activeOnly && activeOnly.IsSpecified && (settings.DetectHiddenWindows || activeOnly.Visible) ? activeOnly : null;

				return activeWindow is WindowInfoBase active && active.IsSpecified && active.Equals(criteria, settings) ? active : null;
			}

			if (criteria.ID != 0)
			{
				if (!IsWindow(criteria.ID))
					return null;

				var temp = CreateWindow(criteria.ID);

				//Naming a window by id skips the enumeration that applies DetectHiddenWindows. Equals has a gate
				//of its own, but it lets anything with a parent through, which our own GUI windows report.
				if (!criteria.IsPureID && !detectHiddenWindows && !temp.Visible)
					return null;

				return temp.Equals(criteria, settings) ? temp : null;
			}

			// Fast path: match by class (or exact title in mode 3) without scanning every window — ask the
			// platform for a native direct lookup (Win32 FindWindow). A definitive miss (handle 0) means no such
			// window can exist, so the O(N) enumeration is skipped entirely; a hit still verifies the remaining
			// criteria. Only for the first match (`last` needs the full z-order scan), and only outside RegEx mode.
			if (!last && settings.TitleMatchMode < 4)
			{
				var hasTitle = !string.IsNullOrEmpty(criteria.Title);

				if (!string.IsNullOrEmpty(criteria.ClassName) || (settings.TitleMatchMode == 3 && hasTitle))
				{
					var exactTitle = settings.TitleMatchMode == 3 && hasTitle ? criteria.Title : null;

					if (Platform.Window.TryFindWindow(criteria.ClassName ?? "", exactTitle, out var fast))
					{
						if (fast == 0)
							return null;   // no window with this class/title exists → there cannot be a match

						var candidate = CreateWindow(fast);

						if (candidate.IsSpecified
							&& (settings.DetectHiddenWindows || candidate.Visible)
							&& candidate.Equals(criteria, settings))
							return candidate;

						// candidate failed the remaining criteria → fall through to the full scan
					}
				}
			}

			foreach (var window in EnumerateWindows(detectHiddenWindows))
			{
				if (window.Equals(criteria, settings))
				{
					found = window;

					if (!last)
						break;
				}
			}

			return found;
		}

		/// <summary>
		/// The window a search names, or null in <paramref name="found"/> when none matches. False when a parameter
		/// raised an error the script continued, and the caller then returns its empty value at once without
		/// reporting the window as missing.
		/// </summary>
		public static bool TryFindWindow(object winTitle, object winText, object excludeTitle, object excludeText, out WindowInfoBase found, bool last = false)
		{
			if (winTitle is Gui gui)
			{
				found = LastFound = CreateWindow((nint)gui.Hwnd);
				return true;
			}

			found = null;

			if (!TryToCriteria(winTitle, winText, excludeTitle, excludeText, out var criteria))
				return false;

			found = Exist(criteria, last);
			return true;
		}

		/// <summary>AutoHotkey's WinExist: the window <paramref name="criteria"/> name, which becomes the Last Found
		/// Window, or that window itself for null criteria.</summary>
		internal static WindowInfoBase Exist(SearchCriteria criteria, bool last = false, bool updateLastFound = true)
		{
			if (criteria == null)
				return LastFound;

			var found = FindWindow(criteria, last);

			if (updateLastFound && found != null && found.IsSpecified)
				LastFound = found;

			return found;
		}

		/// <summary>
		/// The object form of a search resolved to criteria, or null when nothing was specified at all — which
		/// means the last-found window rather than a search, as AHK's WinExist does. False means a parameter
		/// raised an error which the script continued, and the caller then returns at once.
		/// </summary>
		internal static bool TryToCriteria(object winTitle, object winText, object excludeTitle, object excludeText, out SearchCriteria criteria)
		{
			criteria = null;

			if (!winText.CoerceString(out var text) || !excludeTitle.CoerceString(out var exclTitle) || !excludeText.CoerceString(out var exclText))
				return false;

			if ((winTitle == null || winTitle is string s && string.IsNullOrEmpty(s))
					&& string.IsNullOrEmpty(text)
					&& string.IsNullOrEmpty(exclTitle)
					&& string.IsNullOrEmpty(exclText))
				return true;

			return SearchCriteria.TryFromString(winTitle, text, exclTitle, exclText, out criteria);
		}

		public static List<WindowInfoBase> FindWindowGroup(SearchCriteria criteria, bool forceAll = false)
		{
			var found = new List<WindowInfoBase>();

			if (criteria.MatchesNothing)
				return found;

			var settings = WindowSearchSettings.Current.With(criteria);
			var detectHiddenWindows = ShouldDetectHiddenWindows(criteria, settings);

			if (criteria.Active)
			{
				var activeWindow = ActiveWindow;

				if (criteria.IsOnlyActive)
				{
					if (activeWindow is WindowInfoBase activeOnly && activeOnly.IsSpecified && (settings.DetectHiddenWindows || activeOnly.Visible))
						found.Add(activeOnly);
				}
				else if (activeWindow is WindowInfoBase active && active.IsSpecified && active.Equals(criteria, settings))
					found.Add(active);

				return found;
			}

			//HasID, matching FindWindow above: enumeration cannot answer for one of our own GUI windows off
			//Windows, where it lists them under the compositor's id rather than the handle Gui.Hwnd returns.
			if (criteria.HasID)
			{
				if (IsWindow(criteria.ID))
				{
					var window = CreateWindow(criteria.ID);

					//Visibility gate as in FindWindow above.
					if ((criteria.IsPureID || detectHiddenWindows || window.Visible)
							&& window.Equals(criteria, settings)) // Other criteria may be present such as ExcludeTitle etc
						found.Add(window);
				}

				return found;
			}

			foreach (var window in EnumerateWindows(detectHiddenWindows))
			{
				if (criteria.IsEmpty || window.Equals(criteria, settings))
				{
					found.Add(window);

					if (!forceAll && string.IsNullOrEmpty(criteria.Group))//If it was a group match, or if the caller specified that they want all matched windows, add it and keep going.
						break;
				}
			}

			return found;
		}

		/// <summary>
		/// The windows a search names, with its criteria, which are null for the last-found window. False, with
		/// both null, when a parameter raised an error the script continued, and the caller then returns its empty
		/// value at once without reporting the window as missing.
		/// </summary>
		public static bool TryFindWindowGroup(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				out List<WindowInfoBase> foundWindows,
				out SearchCriteria criteria,
				bool forceAll = false)
		{
			if (!TryToCriteria(winTitle, winText, excludeTitle, excludeText, out criteria))
			{
				foundWindows = null;
				return false;
			}

			if (criteria == null)
			{
				foundWindows = [];

				if (LastFound is WindowInfoBase lastFound)
					foundWindows.Add(lastFound);
			}
			else
			{
				foundWindows = FindWindowGroup(criteria, forceAll);

				if (foundWindows.Count > 0 && foundWindows[0].IsSpecified)
					LastFound = foundWindows[0];
			}

			return true;
		}

		/// <summary>
		/// AutoHotkey's ControlExist: a name ending in a digit is first looked up as a ClassNN, then any name is
		/// matched against each control's own text under SetTitleMatchMode. Zero when none matches.
		/// </summary>
		internal static nint ControlExist(nint window, string name)
		{
			if (!Platform.Window.TryEnumerateChildren(window, out var controls))
				return 0;

			if (char.IsDigit(name[^1]))
			{
				var found = FindClassNN(controls, name);

				if (found != 0)
					return found;
			}

			var titleMatchMode = ThreadAccessors.A_TitleMatchMode;

			foreach (var control in controls)
				if (WindowInfoBase.TitleMatches(Platform.Window.GetTitle(control), name, titleMatchMode))
					return control;

			return 0;
		}

		/// <summary>
		/// The control a ClassNN names. As in AutoHotkey, a class that is a prefix of the name counts toward the
		/// number, so "SysListView321" matches the first SysListView32 and "List01" matches nothing.
		/// </summary>
		internal static nint FindClassNN(IReadOnlyList<nint> controls, string classNN)
		{
			Span<char> number = stackalloc char[11];
			var count = 0;

			foreach (var control in controls)
			{
				var className = Platform.Window.GetClassName(control);

				if (className.Length == 0 || !classNN.StartsWith(className, StringComparison.OrdinalIgnoreCase))
					continue;

				_ = (++count).TryFormat(number, out var written);

				if (classNN.AsSpan(className.Length).SequenceEqual(number[..written]))
					return control;
			}

			return 0;
		}

		/// <summary>Each control with its ClassNN: its class name followed by its position among the controls of that
		/// class, in the window's order.</summary>
		internal static IEnumerable<(nint Control, string ClassNN)> ClassNNs(IReadOnlyList<nint> controls)
		{
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (var control in controls)
			{
				var className = Platform.Window.GetClassName(control);
				var count = counts.GetValueOrDefault(className) + 1;
				counts[className] = count;
				yield return (control, className + count);
			}
		}

		/// <summary>Whether a search must enumerate hidden windows: under <paramref name="settings"/>, which already
		/// include the criteria's own ahk_opt, or for a group whose members detect them.</summary>
		internal static bool ShouldDetectHiddenWindows(SearchCriteria criteria, WindowSearchSettings settings)
		{
			if (settings.DetectHiddenWindows || criteria.HasNonGroupCriteria || string.IsNullOrEmpty(criteria.Group))
				return settings.DetectHiddenWindows;

			return RequiresHiddenWindowEnumeration(criteria, settings, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		private static bool RequiresHiddenWindowEnumeration(SearchCriteria criteria, WindowSearchSettings settings, HashSet<string> visitedGroups)
		{
			if (settings.DetectHiddenWindows)
				return true;

			if (string.IsNullOrEmpty(criteria.Group) || criteria.HasNonGroupCriteria || !visitedGroups.Add(criteria.Group))
				return false;

			if (!TheScript.WindowGroups.TryGetValue(criteria.Group, out var group))
				return false;

			foreach (var memberCrit in group.sc)
			{
				if (RequiresHiddenWindowEnumeration(memberCrit, settings.With(memberCrit), visitedGroups))
					return true;
			}

			return false;
		}
	}
}
