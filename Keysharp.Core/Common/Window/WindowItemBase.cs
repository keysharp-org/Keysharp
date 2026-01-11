namespace Keysharp.Core.Common.Window
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
	internal abstract class WindowItemBase
	{
		//Cache these on first retrival, because apparently fetching them is a slow operation in all platforms
		protected string processPath = null;
		protected string processName = null;

		internal abstract bool Active { get; set; }
		internal abstract bool AlwaysOnTop { get; set; }
		internal abstract bool Bottom { set; }
		internal abstract HashSet<WindowItemBase> ChildWindows { get; }
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

				if (parent.IsSpecified)
					return GetClassNN(parent.ChildWindows);

				return classNN;
			}
		}

		internal abstract Rectangle ClientLocation { get; }
		internal int Delay { get; set; } = 100;
		internal abstract bool Enabled { get; set; }
		internal abstract bool Exists { get; }
		internal abstract long ExStyle { get; set; }
		public nint Handle { get; set; } = 0;
		internal abstract bool IsHung { get; }
		internal bool IsSpecified => Handle != 0;
		internal abstract Rectangle Location { get; set; }
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
		internal abstract WindowItemBase NonChildParentWindow { get; }
		internal abstract WindowItemBase ParentWindow { get; }
		internal virtual bool IsIconic => WindowState == FormWindowState.Minimized;

		internal virtual string Path
		{
			get
			{
				if (!processPath.IsNullOrEmpty())
					return processPath;

				try
				{
					using (var proc = Process.GetProcessById((int)PID))
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
			}
		}

		internal abstract long PID { get; }

		internal virtual string ProcessName
		{
			get
			{
				if (!processName.IsNullOrEmpty())
					return processName;

				try
				{
					using (var proc = Process.GetProcessById((int)PID))
					{
						using var module = proc.MainModule;
						processName = module.ModuleName;
					}
				}
				catch
				{
				}

				return processName;
			}
		}

		internal abstract Size Size { get; set; }
		internal abstract long Style { get; set; }
		internal abstract List<string> Text { get; }
		internal abstract string Title { get; set; }
		internal abstract object Transparency { get; set; }
		internal abstract object TransparentColor { get; set; }
		internal abstract bool Visible { get; set; }
		internal bool Detectable => ThreadAccessors.A_DetectHiddenWindows || Visible;
		internal abstract FormWindowState WindowState { get; set; }

		internal WindowItemBase(nint handle) => Handle = handle;

		/// <summary>
		/// Define Standard Equalty Opertaor
		/// </summary>
		/// <param name="obj"></param>
		/// <returns></returns>
		public override bool Equals(object obj) => obj is WindowItemBase window ? window.Handle == Handle : base.Equals(obj);

		public override int GetHashCode() => base.GetHashCode();

		public override string ToString() => $"{Handle.ToInt64()}";

		internal static void DoControlDelay()
		{ }// => DoDelay(ThreadAccessors.A_ControlDelay);//These cause out of order execution bugs with threads and are not needed anyway.

		//public override string ToString() => IsSpecified ? Title : "not specified window";
		internal static void DoWinDelay()
		{ }// => DoDelay(ThreadAccessors.A_WinDelay);

		internal abstract void ChildFindPoint(PointAndHwnd pah);

		/// <summary>
		/// Left-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal abstract void Click(Point? location = null);

		/// <summary>
		/// Right-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal abstract void ClickRight(Point? location = null);

		internal abstract POINT ClientToScreen();

		internal virtual void ClientToScreen(ref POINT pt)
		{
			var screenPt = ClientToScreen();
			pt.X += screenPt.X;
			pt.Y += screenPt.Y;
		}

		internal abstract bool Close();

		internal bool Equals(SearchCriteria criteria)//Make internal to avoid dupes.
		{
			if (!IsSpecified)
				return false;

			if (criteria.Active)
				return Active;

			if (criteria.IsEmpty)
				return false;

			if (criteria.ID != 0 && Handle != criteria.ID)
				return false;

			if (criteria.PID != 0L && PID != criteria.PID)
				return false;

			if (!string.IsNullOrEmpty(criteria.Title))//Put title first because it's the most likely.
			{
				if (criteria.Title == "A")
					return Active;

				if (!TitleCompare(Title, criteria.Title))
					return false;

				if (!ThreadAccessors.A_DetectHiddenWindows && !Visible)
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.ClassName))
			{
				if (!TitleCompare(ClassName, criteria.ClassName, StringComparison.CurrentCultureIgnoreCase) && !TitleCompare(ClassNN, criteria.ClassName, StringComparison.CurrentCultureIgnoreCase))//Check both just to be safe because they can be slightly different.
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.Path))
			{
				var comp = Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.CurrentCulture;

				if (!TitleCompare(Path, criteria.Path, comp))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.Text))
			{
				foreach (var text in Text)
					if (TitleCompare(text, criteria.Text))
						return true;

				return false;
			}

			if (!string.IsNullOrEmpty(criteria.ExcludeTitle))
			{
				if (TitleCompare(Title, criteria.ExcludeTitle))
					return false;
			}

			if (!string.IsNullOrEmpty(criteria.ExcludeText))
			{
				foreach (var text in Text)
					if (TitleCompare(text, criteria.ExcludeText))
						return false;
			}

			//Potentially the slowest, so match it last
			if (!string.IsNullOrEmpty(criteria.Group))
			{
				if (TheScript.WindowProvider.Manager.Groups.TryGetValue(criteria.Group, out var stack))
				{
					if (stack.sc.Count > 0)//An empty group is assumed to want to match all windows.
					{
						var anypassed = false;

						foreach (var crit in stack.sc)
						{
							if (Equals(crit))//If any criteria in the group matched something, then it's considered a valid match.
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

		internal virtual WindowItemBase FirstChild(SearchCriteria sc)
		{
			WindowItemBase item = null;

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

		internal string GetClassNN(HashSet<WindowItemBase> childWindows)
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

		internal abstract bool Hide();

		internal abstract bool Kill();

		//internal abstract WindowItemBase RealChildWindowFromPoint(Point location);

		internal abstract bool Redraw();

		//internal abstract void SetTransparency(byte level, Color color);

		internal abstract bool Show();

		internal bool Wait(double seconds) => Wait(seconds, () => Exists);

		internal bool WaitActive(double seconds) => Wait(seconds, () => Active);

		internal bool WaitClose(double seconds) => Wait(seconds, () => !Exists);

		internal bool WaitNotActive(double seconds) => Wait(seconds, () => !Active);

		private static void DoDelay(long delay)
		{
			if (delay >= 0)
				_ = Flow.Sleep(delay);
		}

		private static bool TitleCompare(string a, string b, StringComparison comp = StringComparison.CurrentCulture)
		{
			if (string.IsNullOrEmpty(a))
				return false;

			switch (ThreadAccessors.A_TitleMatchMode)
			{
				case 1:
					return a.StartsWith(b, comp);

				case 2:
					return a.Contains(b, comp);

				case 3:
					return a.Equals(b, comp);

				case 4:
				{
					return RegEx.RegExMatch(a, b) is long ll && ll > 0L;
				}
			}

			return false;
		}

		private bool Wait(double seconds, Func<bool> func)
		{
			var start = DateTime.UtcNow;

			while (!func())
			{
				if (seconds != 0 && (DateTime.UtcNow - start).TotalSeconds >= seconds)
					return false;

				_ = Flow.Sleep(Delay);
			}

			return true;
		}
	}
}
