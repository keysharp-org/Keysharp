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

		public static WindowInfoBase LastFound
		{
			get
			{
				var handle = (nint)Script.TheScript.HwndLastUsed;
				return CreateWindow(handle);   // own Guis route through Platform.Window (the platform service recognises own-toolkit handles)
			}
			set => Script.TheScript.HwndLastUsed = value.Handle;
		}

		public static WindowInfoBase FindWindow(SearchCriteria criteria, bool last = false)
		{
			WindowInfoBase found = null;

			if (criteria.IsEmpty)
				return found;

			var matchOptions = WindowSearchOptions.Merge(criteria.Options);
			var detectHiddenWindows = ShouldDetectHiddenWindows(criteria);

			if (criteria.Active)
			{
				var activeWindow = ActiveWindow;

				if (criteria.IsOnlyActive)
					return activeWindow is WindowInfoBase activeOnly && activeOnly.IsSpecified ? activeOnly : null;

				return activeWindow is WindowInfoBase active && active.IsSpecified && active.Equals(criteria, matchOptions) ? active : null;
			}

			if (criteria.ID != 0)
			{
				//Naming a window by id skips the enumeration that applies DetectHiddenWindows, and Equals does
				//not test visibility. A bare handle addresses the window directly and is exempt; "ahk_id <n>"
				//is an ordinary criterion and honours the setting.
				if (IsWindow(criteria.ID)
						&& CreateWindow(criteria.ID) is WindowInfoBase temp
						&& (criteria.IsPureID || detectHiddenWindows || temp.Visible)
						&& temp.Equals(criteria, matchOptions))
					return temp;

				return null;
			}

			// Fast path: match by class (or exact title in mode 3) without scanning every window — ask the
			// platform for a native direct lookup (Win32 FindWindow). A definitive miss (handle 0) means no such
			// window can exist, so the O(N) enumeration is skipped entirely; a hit still verifies the remaining
			// criteria. Only for the first match (`last` needs the full z-order scan), and only outside RegEx mode.
			if (!last)
			{
				var mm = matchOptions.TitleMatchMode ?? ThreadAccessors.A_TitleMatchMode;

				if (mm < 4)
				{
					var hasTitle = !string.IsNullOrEmpty(criteria.Title);

					if (!string.IsNullOrEmpty(criteria.ClassName) || (mm == 3 && hasTitle))
					{
						var exactTitle = mm == 3 && hasTitle ? criteria.Title : null;

						if (Platform.Window.TryFindWindow(criteria.ClassName ?? "", exactTitle, out var fast))
						{
							if (fast == 0)
								return null;   // no window with this class/title exists → there cannot be a match

							var candidate = CreateWindow(fast);

							if (candidate.IsSpecified
								&& ((matchOptions.DetectHiddenWindows ?? ThreadAccessors.A_DetectHiddenWindows) || candidate.Visible)
								&& candidate.Equals(criteria, matchOptions))
								return candidate;

							// candidate failed the remaining criteria → fall through to the full scan
						}
					}
				}
			}

			foreach (var window in EnumerateWindows(detectHiddenWindows))
			{
				if (window.Equals(criteria, matchOptions))
				{
					found = window;

					if (!last)
						break;
				}
			}

			return found;
		}

		public static WindowInfoBase FindWindow(object winTitle, object winText, object excludeTitle, object excludeText, bool last = false, bool ignorePureID = false)
		{
			WindowInfoBase foundWindow = null;
			var (parsed, ptr) = WindowHelper.CtrlTonint(winTitle);

			if (parsed)
			{
				if (!ignorePureID && IsWindow(ptr))
					return LastFound = CreateWindow(ptr);
			}

			var text = winText.As();
			var exclTitle = excludeTitle.As();
			var exclTxt = excludeText.As();

			if (winTitle is Gui gui)
				return LastFound = CreateWindow((nint)gui.Hwnd);
			else if ((winTitle == null || winTitle is string s && string.IsNullOrEmpty(s)) &&
					 string.IsNullOrEmpty(text) &&
					 string.IsNullOrEmpty(exclTitle) &&
					 string.IsNullOrEmpty(exclTxt))
			{
				foundWindow = LastFound;
			}
			else
			{
				var criteria = SearchCriteria.FromString(winTitle, text, excludeTitle, excludeText);
				foundWindow = FindWindow(criteria, last);
			}

			if (foundWindow != null && foundWindow.IsSpecified)
				LastFound = foundWindow;

			return foundWindow;
		}

		public static List<WindowInfoBase> FindWindowGroup(SearchCriteria criteria, bool forceAll = false)
		{
			var found = new List<WindowInfoBase>();
			var matchOptions = WindowSearchOptions.Merge(criteria.Options);
			var detectHiddenWindows = ShouldDetectHiddenWindows(criteria);

			if (criteria.Active)
			{
				var activeWindow = ActiveWindow;

				if (criteria.IsOnlyActive)
				{
					if (activeWindow is WindowInfoBase activeOnly && activeOnly.IsSpecified)
						found.Add(activeOnly);
				}
				else if (activeWindow is WindowInfoBase active && active.IsSpecified && active.Equals(criteria, matchOptions))
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
							&& window.Equals(criteria, matchOptions)) // Other criteria may be present such as ExcludeTitle etc
						found.Add(window);
				}

				return found;
			}

			foreach (var window in EnumerateWindows(detectHiddenWindows))
			{
				if (criteria.IsEmpty || window.Equals(criteria, matchOptions))
				{
					found.Add(window);

					if (!forceAll && string.IsNullOrEmpty(criteria.Group))//If it was a group match, or if the caller specified that they want all matched windows, add it and keep going.
						break;
				}
			}

			return found;
		}

		public static (List<WindowInfoBase>, SearchCriteria) FindWindowGroup(object winTitle,
				object winText,
				object excludeTitle,
				object excludeText,
				bool forceAll = false)
		{
			SearchCriteria criteria = null;
			var foundWindows = new List<WindowInfoBase>();
			var text = winText.As();
			var exclTitle = excludeTitle.As();
			var exclText = excludeText.As();

			if ((winTitle == null || winTitle is string s && string.IsNullOrEmpty(s)) &&
					string.IsNullOrEmpty(text) &&
					string.IsNullOrEmpty(exclTitle) &&
					string.IsNullOrEmpty(exclText))
			{
				if (LastFound != null)
					foundWindows.Add(LastFound);
			}
			else
			{
				criteria = SearchCriteria.FromString(winTitle, text, exclTitle, exclText);
				foundWindows = FindWindowGroup(criteria, forceAll);

				if (foundWindows != null && foundWindows.Count > 0 && foundWindows[0].IsSpecified)
					LastFound = foundWindows[0];
			}

			return (foundWindows, criteria);
		}

		internal static bool ShouldDetectHiddenWindows(SearchCriteria criteria, WindowSearchOptions inheritedOptions = null)
		{
			var matchOptions = WindowSearchOptions.Merge(criteria.Options, inheritedOptions);
			var detectHiddenWindows = matchOptions.DetectHiddenWindows ?? ThreadAccessors.A_DetectHiddenWindows;

			if (detectHiddenWindows || criteria.HasNonGroupCriteria || string.IsNullOrEmpty(criteria.Group))
				return detectHiddenWindows;

			if (RequiresHiddenWindowEnumeration(criteria, matchOptions, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
				return true;

			return false;
		}

		private static bool RequiresHiddenWindowEnumeration(SearchCriteria criteria, WindowSearchOptions matchOptions, HashSet<string> visitedGroups)
		{
			if (matchOptions.DetectHiddenWindows ?? ThreadAccessors.A_DetectHiddenWindows)
				return true;

			if (string.IsNullOrEmpty(criteria.Group) || criteria.HasNonGroupCriteria || !visitedGroups.Add(criteria.Group))
				return false;

			if (!TheScript.WindowGroups.TryGetValue(criteria.Group, out var group))
				return false;

			foreach (var memberCrit in group.sc)
			{
				if (RequiresHiddenWindowEnumeration(memberCrit, WindowSearchOptions.Merge(memberCrit.Options, matchOptions), visitedGroups))
					return true;
			}

			return false;
		}
	}
}
