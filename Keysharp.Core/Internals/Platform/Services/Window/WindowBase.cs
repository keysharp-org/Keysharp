namespace Keysharp.Internals
{

	/// <summary>
	/// Base for the per-OS <see cref="IWindow"/> implementations. Required query members throw until a platform
	/// implementation overrides them. Optional Try* capabilities return false by default so user-facing callers
	/// can report unsupported operations consistently.
	/// </summary>
	internal abstract class WindowBase : IWindow
	{
		private static T NotYet<T>([System.Runtime.CompilerServices.CallerMemberName] string m = "")
			=> throw new NotImplementedException($"Platform.Window.{m} is not routed through the host yet.");

		protected static bool Unsupported() => false;

#if !WINDOWS
		protected static bool TryOwnControl(nint h, out Control control)
		{
			control = h != 0 ? Control.FromHandle(h) : null;
			return control != null;
		}

		private static IReadOnlyList<nint> OwnChildHandles(Control root)
		{
			var handles = new List<nint>();
			var seen = new HashSet<Control>();

			void AddChildren(Control control)
			{
				foreach (var child in control.VisualControls)
				{
					if (!seen.Add(child))
						continue;

					if (child is not Layout && child.Handle != 0)
						handles.Add(child.Handle);

					AddChildren(child);
				}
			}

			AddChildren(root);
			return handles;
		}
#endif

		// --- query (item factories: build the neutral WindowInfo, seeded where the platform batches) ---
		public virtual Keysharp.Internals.Window.WindowInfoBase CreateWindow(nint id)
		{
#if !WINDOWS
			if (TryOwnControl(id, out var control))
				return control is Eto.Forms.Window ? new WindowInfo(id) : new ControlInfo(control);
#endif
			return NotYet<Keysharp.Internals.Window.WindowInfoBase>();
		}
		public virtual Keysharp.Internals.Window.WindowInfoBase ActiveWindow() => NotYet<Keysharp.Internals.Window.WindowInfoBase>();
		public virtual IReadOnlyList<Keysharp.Internals.Window.WindowInfoBase> Enumerate(bool includeHidden) => NotYet<IReadOnlyList<Keysharp.Internals.Window.WindowInfoBase>>();
		public virtual bool TryGetAt(int x, int y, out nint child) { child = default; return Unsupported(); }

		// At-point query returning the built item. Wayland overrides this to hand back the payload its
		// at-point IPC already parsed instead of re-fetching the same window by handle.
		public virtual Keysharp.Internals.Window.WindowInfoBase WindowAt(int x, int y)
			=> TryGetAt(x, y, out var child) ? CreateWindow(child) : null;

		// No native by-class fast path by default (X11/Wayland/macOS enumerate); Windows overrides this.
		public virtual bool TryFindWindow(string className, string title, out nint handle) { handle = default; return Unsupported(); }
		public virtual bool IsWindow(nint h)
		{
#if !WINDOWS
			return TryOwnControl(h, out _);
#else
			return NotYet<bool>();
#endif
		}
		public virtual nint GetForegroundHandle() => NotYet<nint>();
		public virtual bool TryGetParent(nint h, out nint parent)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				var form = control as Form ?? control.ParentWindow as Form ?? control.Parent as Form;
				parent = form != null && form.Handle != h ? form.Handle : default;
				return parent != 0;
			}
#endif
			parent = default;
			return Unsupported();
		}
		public virtual bool TryGetTopLevel(nint h, out nint top)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				var form = control as Form ?? control.ParentWindow as Form ?? control.Parent as Form;
				top = form?.Handle ?? default;
				return top != 0;
			}
#endif
			top = default;
			return Unsupported();
		}
		public virtual bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				children = OwnChildHandles(control);
				return true;
			}
#endif
			children = [];
			return Unsupported();
		}
		public virtual bool TryClientToScreen(nint h, ref Point pt)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
			{
				var origin = ClientToScreen(h);
				pt = new Point(pt.X + origin.X, pt.Y + origin.Y);
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual uint GetFocusedControlThread(nint window = 0) => NotYet<uint>();
		public virtual bool IncludeInGroups(nint h) => true;   // default: every window; Windows narrows it
		public virtual bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				text = string.IsNullOrEmpty(control.Text) ? [] : [control.Text];
				return true;
			}
#endif
			text = [];
			return Unsupported();
		}
		public virtual void ChildFindPoint(nint h, PointAndHwnd pah)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				_ = ControlInfo.TryFindPoint(control, pah);
#endif
		}
		public virtual string GetTitle(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.Text ?? DefaultObject;
#endif
			return NotYet<string>();
		}
		public virtual string GetClassName(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.GetType().Name;
#endif
			return NotYet<string>();
		}
		public virtual long GetPid(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return Environment.ProcessId;
#endif
			return NotYet<long>();
		}
		public virtual Rectangle GetBounds(nint h)
		{
#if !WINDOWS
			// Screen-relative, like GetClientBounds below: own *windows* are resolved by the platform backend
			// before they reach here, so this branch is an own child control, whose Eto bounds are parent-relative.
			if (TryOwnControl(h, out var control))
				return control.GetScreenBounds();
#endif
			return NotYet<Rectangle>();
		}
		public virtual Rectangle GetClientBounds(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.GetClientScreenRect();
#endif
			return NotYet<Rectangle>();
		}
		public virtual long GetStyle(nint h)
		{
#if !WINDOWS
			// Projects the toolkit's window/control state onto the Win32 WS_* bits scripts actually test.
			// (This used to hand back Eto's own WindowStyle enum value, where Default is 0 and None is 1 —
			// numbers that collide meaninglessly with the WS_* constants.)
			if (TryOwnControl(h, out var control))
				return EtoWindowStyles.For(control);
#endif
			return NotYet<long>();
		}
		public virtual long GetExStyle(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return 0L;
#endif
			return NotYet<long>();
		}
		public virtual bool GetActive(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.HasFocus || control is Form form && form.ActiveControl != null;
#endif
			return NotYet<bool>();
		}
		public virtual bool GetVisible(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.Visible;
#endif
			return NotYet<bool>();
		}
		public virtual bool GetEnabled(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control.Enabled;
#endif
			return NotYet<bool>();
		}
		public virtual bool GetHung(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return false;
#endif
			return NotYet<bool>();
		}
		public virtual bool GetExists(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return true;
#endif
			return NotYet<bool>();
		}
		public virtual FormWindowState GetWindowState(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control is Form form ? form.WindowState : FormWindowState.Normal;
#endif
			return NotYet<FormWindowState>();
		}
		public virtual bool GetAlwaysOnTop(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
				return control is Form form && form.TopMost;
#endif
			return NotYet<bool>();
		}
		public virtual object GetTransparency(nint h)
		{
#if !WINDOWS
			// -1L is the cross-platform "no explicit transparency set" sentinel (WinGetTransparent maps it to "").
			// A fully-opaque own-GUI window that never had opacity assigned must report -1, not 255, to match the
			// Windows/X11 backends.
			if (TryOwnControl(h, out _))
				return -1L;
#endif
			return NotYet<object>();
		}
		public virtual object GetTransparentColor(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return 0L;
#endif
			return NotYet<object>();
		}
		public virtual POINT ClientToScreen(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				var sp = control.PointToScreen(Point.Empty);
				return new POINT(Convert.ToInt32(sp.X), Convert.ToInt32(sp.Y));
			}
#endif
			return NotYet<POINT>();
		}

		// --- control ---
		public virtual bool TrySetAlwaysOnTop(nint h, bool onTop)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control) && control is Form form)
			{
				form.TopMost = onTop;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				if (setSize)
				{
					var size = control.Size;

					if (bounds.Width != WindowInfoBase.Unchanged)
						size.Width = bounds.Width;

					if (bounds.Height != WindowInfoBase.Unchanged)
						size.Height = bounds.Height;

					control.SetSize(size);
				}

				if (setPos)
				{
					var location = control.GetLocation();

					if (bounds.X != WindowInfoBase.Unchanged)
						location.X = bounds.X;

					if (bounds.Y != WindowInfoBase.Unchanged)
						location.Y = bounds.Y;

					control.SetLocation(location);
				}

				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetState(nint h, FormWindowState state)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control) && control is Form form)
			{
				form.WindowState = state;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetStyle(nint h, long style)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return true;
#endif
			return Unsupported();
		}
		public virtual bool TrySetExStyle(nint h, long exStyle)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return true;
#endif
			return Unsupported();
		}
		public virtual bool TrySetTransparency(nint h, object alpha) => Unsupported();
		public virtual bool TryActivate(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				if (control is Eto.Forms.Window window)
					window.Visible = true;

				control.Focus();
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetZOrder(nint h, ZOrder z) => Unsupported();
		public virtual bool TryClose(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control) && control is Form form)
			{
				form.Close();
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TryKill(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out _))
				return TryClose(h);
#endif
			return Unsupported();
		}
		public virtual bool TryHide(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Visible = false;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TryShow(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Visible = true;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TryRedraw(nint h)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Invalidate();
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TryClick(nint h, Point at, uint button, int count) => Unsupported();
		public virtual bool TrySetTitle(nint h, string title)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Text = title;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetVisible(nint h, bool visible)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Visible = visible;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetEnabled(nint h, bool enabled)
		{
#if !WINDOWS
			if (TryOwnControl(h, out var control))
			{
				control.Enabled = enabled;
				return true;
			}
#endif
			return Unsupported();
		}
		public virtual bool TrySetTransparentColor(nint h, object color) => Unsupported();
	}
}
