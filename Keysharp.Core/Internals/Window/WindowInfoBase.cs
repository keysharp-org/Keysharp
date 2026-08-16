using Keysharp.Builtins;
namespace Keysharp.Internals.Window
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct POINT
	{
		internal int X;
		internal int Y;

		internal POINT(int x, int y) { X = x; Y = y; }
		internal POINT(Point p) { X = p.X; Y = p.Y; }
	}

	internal class PointAndHwnd
	{
		internal double distanceFound = 0.0;
		internal nint hwndFound = 0;
		internal bool ignoreDisabled = false;
		internal POINT pt;
		internal Rectangle rectFound = new ();

		internal PointAndHwnd(POINT p) => pt = p;
		internal PointAndHwnd(Point p) => pt = new POINT(p);
	}

	/// <summary>
	/// Abstraction of a single Platform independend Window
	/// </summary>
	internal abstract class WindowInfoBase
	{
		// These properties are cached on first access because fetching them is slow on all platforms.
		// This is safe because WindowInfoBase instances are short-lived: each window enumeration creates
		// fresh instances, and WinWait re-enumerates on every iteration. Do NOT store WindowInfoBase
		// instances across separate Keysharp method calls; the cached values will be stale.
		protected string processPath = null;
		protected string processName = null;
		protected string title = null;
		protected string className = null;

		internal abstract bool Active { get; }
		internal abstract bool AlwaysOnTop { get; }

		// Child windows, the parent/top-level links, the text lines, and client-origin all route by handle through
		// Platform.Window, so they are shared here by every subtype (the lazy WindowInfo, the seeded Wayland/Mac
		// subtypes). ControlInfo overrides them to read its Eto control instead. The scalar getters below stay
		// abstract — each subtype reads them its own way (lazily via Platform.Window, or from a held batch).
		internal virtual HashSet<WindowInfoBase> ChildWindows
		{
			get
			{
				var set = new HashSet<WindowInfoBase>();

				if (IsSpecified && Platform.Window.TryEnumerateChildren(Handle, out var kids))
					foreach (var k in kids)
						//Built through the factory rather than as a bare WindowInfo so a child that is one of our
						//own toolkit controls comes back as the ControlInfo that can read it. A plain WindowInfo
						//answers by handle, which off Windows reaches nothing for a widget - every control found
						//by a search rather than by handle then reported no text and a zero rect.
						set.Add(Platform.Window.CreateWindow(k));

				return set;
			}
		}

		internal abstract string ClassName { get; }

		/// <summary>
		/// Get the ClassName + number of occurrence of this window (control)
		/// </summary>
		internal virtual string ClassNN
		{
			get
			{
				var classNN = ClassName;
				var parent = ParentWindow;

				if (parent?.IsSpecified == true)
					return GetClassNN(parent.ChildWindows);

				return classNN;
			}
		}

		internal abstract Rectangle ClientBounds { get; }
		internal int Delay { get; set; } = 100;
		internal abstract bool Enabled { get; }
		internal abstract bool Exists { get; }
		internal abstract long ExStyle { get; }
		public nint Handle { get; set; } = 0;
		internal abstract bool IsHung { get; }
		internal bool IsSpecified => Handle != 0;
		/// <summary>
		/// Sentinel for the <see cref="Bounds"/> setter: any field equal to this value is left unchanged.
		/// </summary>
		internal const int Unchanged = int.MinValue;

		/// <summary>
		/// The outer (decorated) window rectangle in logical coordinates. The getter returns the full
		/// bounds; the setter applies them, treating any field equal to <see cref="Unchanged"/> as
		/// "leave that field as-is" (so it can move, resize, or do both in a single platform call).
		/// Throws OSError if the underlying platform call reports failure — but not when the call
		/// succeeds yet the window manager repositions/clamps the window (AHK documents that success
		/// may be reported even if the window has not moved).
		/// </summary>
		internal abstract Rectangle Bounds { get; }

		/// <summary>The window's top-left position, in screen coordinates.</summary>
		internal Point Location => Bounds.Location;
		internal virtual string NetClassName
		{
			get
			{
				if (Control.FromHandle(Handle) is Control c)
					return c.GetType().Name;

				return DefaultErrorString;
			}
		}
		internal virtual string NetClassNN
		{
			get
			{
				if (Control.FromHandle(Handle) is Control ctrl)
				{
					var className = ctrl.GetType().Name;
					var classNN = className;
					var parent = ctrl.Parent;

					if (parent != null)
					{
						var nn = 1; // Class NN counter

						// now we must know the position of our "control"
						foreach (var c in parent.GetAllControlsRecursive<Control>())
						{
							if (c.GetType().Name == className)
							{
								if (c == ctrl)
									break;
								else
									++nn;  // if its the same class but not our control
							}
						}

						classNN += nn.ToString(); // if its the same class and our control
					}

					return classNN;
				}

				return DefaultErrorString;
			}
		}
		internal virtual WindowInfoBase NonChildParentWindow
			=> Platform.Window.TryGetTopLevel(Handle, out var top)
				? (top == Handle ? this : new WindowInfo(top))
				: null;

		internal virtual WindowInfoBase ParentWindow
			=> Platform.Window.TryGetParent(Handle, out var parent)
				? new WindowInfo(parent)
				: new WindowInfo((nint)0);
		internal virtual bool IsIconic => WindowState == FormWindowState.Minimized;

		internal virtual string Path
		{
			get
			{
				if (!processPath.IsNullOrEmpty())
					return processPath;

				var pid = PID;

				if (pid <= 0)
					return DefaultErrorString;

#if WINDOWS
				// OpenProcess(QUERY_LIMITED) + GetProcessImageFileName: cheap in matcher loops and
				// works for elevated processes, where Process.MainModule throws access-denied.
				if (Processes.GetProcessName((uint)pid, out processPath, false) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);

				return processPath;
#else
				try
				{
					using (var proc = Process.GetProcessById((int)pid))
					{
						//This will be extremely slow in a loop because MainModule calls an underlying method GetModules()
						//which does a lot of processing.
						using var module = proc.MainModule;
						return processPath = module.FileName;
					}
				}
				catch
				{
					return DefaultErrorString;
				}
#endif
			}
		}

		internal abstract long PID { get; }

		internal virtual string ProcessName
		{
			get
			{
				if (!processName.IsNullOrEmpty())
					return processName;

				var pid = PID;

				if (pid <= 0)
					return processName = string.Empty;

#if WINDOWS
				if (Processes.GetProcessName((uint)pid, out processName) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);
#else
				try
				{
					using (var proc = Process.GetProcessById((int)pid))
					{
						using var module = proc.MainModule;
						processName = module.ModuleName;
					}
				}
				catch
				{
				}
#endif

				return processName;
			}
		}

		/// <summary>The window's outer (decorated) size.</summary>
		internal Size Size => Bounds.Size;
		internal abstract long Style { get; }

		private List<string> textCache;
		private bool textCacheHidden, textCacheSet;
		internal virtual List<string> Text => GetText(null);
		internal virtual List<string> GetText(WindowSearchOptions options)
		{
			var detectHidden = options?.DetectHiddenText ?? ThreadAccessors.A_DetectHiddenText;

			if (textCacheSet && textCacheHidden == detectHidden)
				return textCache;

			textCache = Platform.Window.TryGetText(Handle, detectHidden, out var t) ? t : [];
			textCacheHidden = detectHidden;
			textCacheSet = true;
			return textCache;
		}
		internal abstract string Title { get; }
		internal abstract object Transparency { get; }
		internal abstract object TransparentColor { get; }
		internal abstract bool Visible { get; }
		internal abstract FormWindowState WindowState { get; }

		internal WindowInfoBase(nint handle) => Handle = handle;

		/// <summary>
		/// Define Standard Equalty Opertaor
		/// </summary>
		/// <param name="obj"></param>
		/// <returns></returns>
		public override bool Equals(object obj) => obj is WindowInfoBase window ? window.Handle == Handle : base.Equals(obj);

		public override int GetHashCode() => Handle.GetHashCode();

		public override string ToString() => $"{Handle.ToInt64()}";

		internal static void DoControlDelay()
			=> DoDelay(ThreadAccessors.A_ControlDelay);//These cause out of order execution bugs with threads and are not needed anyway.

		//public override string ToString() => IsSpecified ? Title : "not specified window";
		internal static void DoWinDelay()
			 => DoDelay(ThreadAccessors.A_WinDelay);

		internal virtual POINT ClientToScreen() => IsSpecified ? Platform.Window.ClientToScreen(Handle) : new POINT(0, 0);

		internal virtual void ClientToScreen(ref POINT pt)
		{
			var screenPt = ClientToScreen();
			pt.X += screenPt.X;
			pt.Y += screenPt.Y;
		}

		internal bool Equals(SearchCriteria criteria, WindowSearchOptions inheritedOptions = null)//Make internal to avoid dupes.
		{
			if (!IsSpecified)
				return false;

			var options = WindowSearchOptions.Merge(criteria.Options, inheritedOptions);

			if (criteria.IsEmpty)
				return false;

			if (criteria.Active && !Active)
				return false;

			// Reject hidden top-levels (unless detecting hidden). Test !Visible BEFORE ParentWindow so a visible
			// window — the common case, and all that enumeration even yields — short-circuits and never pays the
			// ParentWindow (GetParent) round-trip; the parent check only runs for the rare not-visible candidate.
			// A bare handle is exempt: it addresses the window directly rather than searching for it.
			if (criteria.HasNonGroupCriteria && !criteria.IsPureID && !GetDetectHiddenWindows(options) && !Visible && ParentWindow?.IsSpecified != true)
				return false;

			if (criteria.ID != 0 && Handle != criteria.ID)
				return false;

			if (criteria.PID != 0L && PID != criteria.PID)
				return false;

			// Read the title only when a title criterion needs it. Title is the single most expensive read on
			// Windows (a cross-process GetWindowText that can stall), so matching by class/PID alone must NOT pay
			// it across every enumerated window.
			var windowTitle = !string.IsNullOrEmpty(criteria.Title) || !string.IsNullOrEmpty(criteria.ExcludeTitle) ? Title : null;

			if (!string.IsNullOrEmpty(criteria.Title))//Put title first because it's the most likely.
			{
				if (!TitleCompare(windowTitle, criteria.Title, options))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.ClassName))
			{
				if (!ExactOrRegExCompare(ClassName, criteria.ClassName, options, StringComparison.OrdinalIgnoreCase))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.Path))
			{
				if (!ProcessCompare(Path, ProcessName, criteria.Path, options))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.Text))
			{
				foreach (var text in GetText(options))
					if (TitleCompare(text, criteria.Text, options))
						return true;

				return false;
			}

			if (!string.IsNullOrEmpty(criteria.ExcludeTitle))
			{
				if (TitleCompare(windowTitle, criteria.ExcludeTitle, options))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.ExcludeText))
			{
				foreach (var text in GetText(options))
					if (TitleCompare(text, criteria.ExcludeText, options))
						return false;
			}

			//Potentially the slowest, so match it last
			if (!string.IsNullOrEmpty(criteria.Group))
			{
				if (TheScript.WindowGroups.TryGetValue(criteria.Group, out var stack))
				{
					if (stack.sc.Count > 0)//An empty group is assumed to want to match all windows.
					{
						var anypassed = false;

						foreach (var crit in stack.sc)
						{
							if (Equals(crit, options))//If any criteria in the group matched something, then it's considered a valid match.
							{
								anypassed = true;
								break;
							}
						}

						if (!anypassed)
							return false;
					}
				}
			}

			return true;
		}

		internal virtual WindowInfoBase FirstChild(SearchCriteria sc)
		{
			WindowInfoBase item = null;

			foreach (var child in ChildWindows)
			{
				if (child.Equals(sc))
				{
					item = child;
					break;
				}
			}

			return item;
		}

		internal string GetClassNN(HashSet<WindowInfoBase> childWindows)
		{
			var className = ClassName;
			var classNN = className;
			// to get the classNN we must know the enumeration
			// of our parent window:
			var nn = 1; // Class NN counter

			// now we must know the position of our "control"
			foreach (var c in childWindows)
			{
				if (c.IsSpecified)
				{
					if (c.ClassName == className)
					{
						if (c.Equals(this))
							break;
						else
							++nn;  // if its the same class but not our control
					}
				}
			}

			classNN += nn.ToString(); // if its the same class and our control
			return classNN;
		}

		// WinClose/WinKill close the window, then wait for it to actually go away. The condition reads LIVE
		// existence straight from Platform.Window (NOT the memoizing item's Exists, which freezes on first read) —
		// own Guis are recognised there via Control.FromHandle, so this is one handle-keyed path for every window
		// kind. (The WinWait*/WinWaitActive builtins re-resolve a fresh item each poll, so they don't use this.)
		internal bool WaitClose(double seconds) => WindowWaits.Until(() => !Platform.Window.GetExists(Handle), seconds, Delay);

		private static void DoDelay(long delay)
		{
			if (delay >= 0)
				Keysharp.Internals.Flow.Sleep((int)delay);
		}

		private static bool ProcessCompare(string processPath, string processName, string criteriaPath, WindowSearchOptions options)
		{
			if ((options?.TitleMatchMode ?? ThreadAccessors.A_TitleMatchMode) == 4)
				return Keysharp.Builtins.RegEx.RegExMatch(processPath, criteriaPath) is long ll && ll > 0L;

			return criteriaPath.IndexOfAny(['\\', '/']) != -1
				   ? string.Equals(processPath, criteriaPath, StringComparison.OrdinalIgnoreCase)
				   : string.Equals(processName, criteriaPath, StringComparison.OrdinalIgnoreCase);
		}

		private static bool ExactOrRegExCompare(string a, string b, WindowSearchOptions options, StringComparison comp)
		{
			if (string.IsNullOrEmpty(a))
				return false;

			if ((options?.TitleMatchMode ?? ThreadAccessors.A_TitleMatchMode) == 4)
				return Keysharp.Builtins.RegEx.RegExMatch(a, b) is long ll && ll > 0L;

			return a.Equals(b, comp);
		}

		private static bool TitleCompare(string a, string b, WindowSearchOptions options, StringComparison comp = StringComparison.Ordinal)
		{
			if (string.IsNullOrEmpty(a))
				return false;

			switch (options?.TitleMatchMode ?? ThreadAccessors.A_TitleMatchMode)
			{
				case 1:
					return a.StartsWith(b, comp);

				case 2:
					return a.Contains(b, comp);

				case 3:
					return a.Equals(b, comp);

				case 4:
				{
					return Keysharp.Builtins.RegEx.RegExMatch(a, b) is long ll && ll > 0L;
				}
			}

			return false;
		}

		private static bool GetDetectHiddenWindows(WindowSearchOptions options)
			=> options?.DetectHiddenWindows ?? ThreadAccessors.A_DetectHiddenWindows;
	}
}
