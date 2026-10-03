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

		// The parent/top-level links, the text lines, and client-origin all route by handle through Platform.Window,
		// so they are shared here by every subtype (the lazy WindowInfo, the seeded Wayland/Mac subtypes).
		// ControlInfo overrides them to read its Eto control instead. The scalar getters below stay abstract — each
		// subtype reads them its own way (lazily via Platform.Window, or from a held batch).
		internal abstract string ClassName { get; }

		/// <summary>The control's ClassNN, numbered within its top-level window; a top-level window's class name.</summary>
		internal string ClassNN
			=> Platform.Window.TryGetTopLevel(Handle, out var top) && top != Handle && Platform.Window.TryEnumerateChildren(top, out var controls)
			   ? WindowQuery.ClassNNs(controls).FirstOrDefault(c => c.Control == Handle).ClassNN ?? ClassName
			   : ClassName;

		internal abstract Rectangle ClientBounds { get; }

		/// <summary>Bounds or client bounds, both screen-relative: false when the platform cannot report that geometry
		/// for this window.</summary>
		internal virtual bool TryGetBounds(bool client, out Rectangle bounds)
		{
			bounds = client ? ClientBounds : Bounds;
			return true;
		}

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

		/// <summary>The process's executable path, or "" when the process cannot be queried.</summary>
		internal virtual string Path => processPath ??= QueryProcessImage(false);

		internal abstract long PID { get; }

		/// <summary>The process's executable file name, or "" when the process cannot be queried.</summary>
		internal virtual string ProcessName => processName ??= QueryProcessImage(true);

		/// <summary>The window's outer (decorated) size.</summary>
		internal Size Size => Bounds.Size;
		internal abstract long Style { get; }

		private List<string> textCache;
		private bool textCacheHidden, textCacheFast;
		internal List<string> Text => GetText(ThreadAccessors.A_DetectHiddenText, ThreadAccessors.A_TitleMatchModeSpeed);
		internal virtual List<string> GetText(bool detectHidden, bool fast)
		{
			if (textCache != null && textCacheHidden == detectHidden && textCacheFast == fast)
				return textCache;

			textCache = Platform.Window.TryGetText(Handle, detectHidden, fast, out var t) ? t : [];
			textCacheHidden = detectHidden;
			textCacheFast = fast;
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

		/// <summary>Whether this window satisfies <paramref name="criteria"/> under <paramref name="settings"/>, which
		/// already include the criteria's own ahk_opt.</summary>
		internal bool Equals(SearchCriteria criteria, WindowSearchSettings settings)
		{
			if (!IsSpecified || criteria.IsEmpty || criteria.MatchesNothing)
				return false;

			if (criteria.Active && !Active)
				return false;

			// Reject hidden top-levels (unless detecting hidden). Test !Visible BEFORE ParentWindow so a visible
			// window — the common case, and all that enumeration even yields — short-circuits and never pays the
			// ParentWindow (GetParent) round-trip; the parent check only runs for the rare not-visible candidate.
			// A bare handle is exempt: it addresses the window directly rather than searching for it.
			if (criteria.HasNonGroupCriteria && !criteria.IsPureID && !settings.DetectHiddenWindows && !Visible && ParentWindow?.IsSpecified != true)
				return false;

			if (criteria.ID != 0 && Handle != criteria.ID)
				return false;

			if (criteria.PID != 0L && PID != criteria.PID)
				return false;

			// Read the title only when a title criterion needs it. Title is the single most expensive read on
			// Windows (a cross-process GetWindowText that can stall), so matching by class/PID alone must NOT pay
			// it across every enumerated window.
			var windowTitle = !string.IsNullOrEmpty(criteria.Title) || !string.IsNullOrEmpty(criteria.ExcludeTitle) ? Title : null;

			if (!string.IsNullOrEmpty(criteria.Title) && !TitleMatches(windowTitle, criteria.Title, settings.TitleMatchMode))
				return false;

			if (!string.IsNullOrEmpty(criteria.ClassName) && !ClassMatches(ClassName, criteria.ClassName, settings.TitleMatchMode))
				return false;

			if (!string.IsNullOrEmpty(criteria.Path) && !ProcessMatches(criteria.Path, settings.TitleMatchMode))
				return false;

			if (!string.IsNullOrEmpty(criteria.Text) && !AnyTextMatches(criteria.Text, settings))
				return false;

			if (!string.IsNullOrEmpty(criteria.ExcludeTitle) && TitleMatches(windowTitle, criteria.ExcludeTitle, settings.TitleMatchMode))
				return false;

			if (!string.IsNullOrEmpty(criteria.ExcludeText) && AnyTextMatches(criteria.ExcludeText, settings))
				return false;

			//Potentially the slowest, so match it last. An empty group matches every window. A member's own ahk_opt
			//applies over this evaluation's settings, as AutoHotkey's IsMember passes them on.
			if (!string.IsNullOrEmpty(criteria.Group) && TheScript.WindowGroups.TryGetValue(criteria.Group, out var group) && group.sc.Count > 0)
			{
				foreach (var member in group.sc)
					if (Equals(member, settings.With(member)))
						return true;

				return false;
			}

			return true;
		}

		/// <summary>AutoHotkey's IsTextMatch: case-sensitive, under the given title match mode.</summary>
		internal static bool TitleMatches(string text, string criterion, long titleMatchMode)
		{
			if (string.IsNullOrEmpty(text))
				return false;

			return titleMatchMode switch
			{
				1 => text.StartsWith(criterion, StringComparison.Ordinal),
				2 => text.Contains(criterion, StringComparison.Ordinal),
				3 => text.Equals(criterion, StringComparison.Ordinal),
				4 => RegExMatches(text, criterion),
				_ => false
			};
		}

		private static void DoDelay(long delay)
		{
			if (delay >= 0)
				Keysharp.Internals.Flow.Sleep((int)delay);
		}

		private bool AnyTextMatches(string criterion, WindowSearchSettings settings)
		{
			foreach (var text in GetText(settings.DetectHiddenText, settings.TitleMatchModeSpeed))
				if (TitleMatches(text, criterion, settings.TitleMatchMode))
					return true;

			return false;
		}

		// A RegEx or a criterion naming a folder compares the full path and anything else the file name, so only that
		// one is looked up.
		private bool ProcessMatches(string criterion, long titleMatchMode)
		{
			if (titleMatchMode == 4)
				return RegExMatches(Path, criterion);

			return criterion.IndexOfAny(['\\', '/']) != -1
				   ? string.Equals(Path, criterion, StringComparison.OrdinalIgnoreCase)
				   : string.Equals(ProcessName, criterion, StringComparison.OrdinalIgnoreCase);
		}

		private string QueryProcessImage(bool nameOnly)
		{
			var pid = PID;

			if (pid <= 0)
				return "";

#if WINDOWS
			return Processes.GetProcessImage((uint)pid, nameOnly);
#else
			try
			{
				//MainModule is slow, since it enumerates the process's modules.
				using var proc = Process.GetProcessById((int)pid);
				using var module = proc.MainModule;
				return nameOnly ? module.ModuleName : module.FileName;
			}
			catch
			{
				return "";
			}
#endif
		}

		private static bool ClassMatches(string className, string criterion, long titleMatchMode)
			=> !string.IsNullOrEmpty(className)
			   && (titleMatchMode == 4 ? RegExMatches(className, criterion) : className.Equals(criterion, StringComparison.OrdinalIgnoreCase));

		private static bool RegExMatches(string text, string pattern) => Keysharp.Builtins.RegEx.RegExMatch(text, pattern) is long ll && ll > 0L;
	}
}
