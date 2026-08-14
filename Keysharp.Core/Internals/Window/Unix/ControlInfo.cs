using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Window.Unix
{
	/// <summary>
	/// Lightweight wrapper that exposes an Eto control as a WindowInfoBase so Control-related APIs can match and
	/// read it. Unlike <see cref="WindowInfo"/> (a pure read-only snapshot whose actions go by handle through
	/// Platform.Window), a control isn't a foreign window, so its few mutating ops live here as concrete methods
	/// driven directly by <c>Platform.Control</c> via the concrete <see cref="ControlInfo"/> type.
	/// </summary>
	internal sealed class ControlInfo : WindowInfoBase
	{
		private readonly Control control;

		internal ControlInfo(nint handle) : base(handle)
		{
			control = Control.FromHandle(handle);
		}

		internal ControlInfo(Control control) : base(control?.Handle ?? nint.Zero)
		{
			this.control = control;
		}

		internal Control Control => control;

		internal override bool Active => control?.HasFocus ?? false;

		internal override bool AlwaysOnTop => false;

		internal override HashSet<WindowInfoBase> ChildWindows
		{
			get
			{
				var set = new HashSet<WindowInfoBase>();
				if (control == null)
					return set;

				var seen = new HashSet<Control>();

				void AddChildren(Control parent)
				{
					foreach (var child in parent.VisualControls)
					{
						if (!seen.Add(child))
							continue;

						// Skip pure layout containers as search results, but keep walking through them.
						if (child is not Layout)
							set.Add(new ControlInfo(child));

						AddChildren(child);
					}
				}

				AddChildren(control);

				return set;
			}
		}

		internal override string ClassName => control?.GetType().Name ?? DefaultErrorString;

		internal override string ClassNN
		{
			get
			{
				if (control == null)
					return ClassName;

				var parentForm = ParentWindow;
				if (parentForm == null || !parentForm.IsSpecified)
					return ClassName;

				Form form = Forms.Control.FromHandle(parentForm.Handle) as Form;

				// Walk immediate children of the parent form (including nested controls on the same branch) to determine ordinal.
				var targetClass = ClassName;
				var ordinal = 0;

				if (control.Parent != null)
				{
					foreach (var sibling in form.Children)
					{
						if (sibling is PixelLayout)
							continue;

						if (sibling.GetType().Name == targetClass)
							ordinal++;

						if (ReferenceEquals(sibling, control))
							break;
					}
				}

				if (ordinal <= 0)
					ordinal = 1;

				return $"{targetClass}{ordinal}";
			}
		}

		internal override Rectangle ClientBounds => control?.GetClientScreenRect() ?? Rectangle.Empty;

		internal override bool Enabled => control?.Enabled ?? false;

		internal override bool Exists => control != null;

		internal override long ExStyle => 0;

		internal override bool IsHung => false;

		// Screen-relative, matching WindowInfo (see WinPosHelper). Eto's own bounds are relative to the parent,
		// so WinGetPos on a control used to report its Gui-relative offset as if it were a desktop position.
		internal override Rectangle Bounds => control?.GetScreenBounds() ?? Rectangle.Empty;

		internal override WindowInfoBase NonChildParentWindow => ParentWindow;

		internal override WindowInfoBase ParentWindow
		{
			get
			{
				if (control == null)
					return null;

				var form = control as Form ?? control.ParentWindow as Form ?? control.Parent as Form;
#if OSX
				return form != null ? new ControlInfo(form) : null;
#else
				return form != null ? WindowQuery.CreateWindow(form.Handle) : null;
#endif
			}
		}

		internal override long PID => ParentWindow?.PID ?? 0;

		internal override long Style => 0;

		internal override List<string> Text => control?.Text is string s && !string.IsNullOrEmpty(s) ? new List<string> { s } : new List<string>();
		internal override List<string> GetText(WindowSearchOptions options) => Text;

		internal override string Title => control?.Text ?? string.Empty;

		internal override object Transparency => null;

		internal override object TransparentColor => null;

		internal override bool Visible => control?.Visible ?? false;

		internal override FormWindowState WindowState => FormWindowState.Normal;

		internal override POINT ClientToScreen()
		{
			if (control == null)
				return new POINT();

			var pt = control.PointToScreen(Point.Empty);
			return new POINT(Convert.ToInt32(pt.X), Convert.ToInt32(pt.Y));
		}

		internal static bool TryFindPoint(Control root, PointAndHwnd pah)
		{
			if (root == null)
				return false;

			var seen = new HashSet<Control>();

			void Visit(Control parent)
			{
				foreach (var child in parent.VisualControls)
				{
					if (!seen.Add(child))
						continue;

					if (!child.Visible)
						continue;

					if (pah.ignoreDisabled && !child.Enabled)
						continue;

					Visit(child);

					if (child is Layout || child.Handle == 0)
						continue;

					var rect = child.GetScreenBounds();

					if (pah.pt.X < rect.Left || pah.pt.X >= rect.Right || pah.pt.Y < rect.Top || pah.pt.Y >= rect.Bottom)
						continue;

					var centerx = rect.Left + ((double)rect.Width / 2);
					var centery = rect.Top + ((double)rect.Height / 2);
					var distance = Math.Sqrt(Math.Pow(pah.pt.X - centerx, 2.0) + Math.Pow(pah.pt.Y - centery, 2.0));
					var updateIt = pah.hwndFound == 0;

					if (!updateIt)
					{
						if (rect.Left >= pah.rectFound.Left && rect.Right <= pah.rectFound.Right
							&& rect.Top >= pah.rectFound.Top && rect.Bottom <= pah.rectFound.Bottom)
							updateIt = true;
						else if (distance < pah.distanceFound &&
								 (pah.rectFound.Left < rect.Left || pah.rectFound.Right > rect.Right
								  || pah.rectFound.Top < rect.Top || pah.rectFound.Bottom > rect.Bottom))
							updateIt = true;
					}

					if (updateIt)
					{
						pah.hwndFound = child.Handle;
						pah.rectFound = rect;
						pah.distanceFound = distance;
					}
				}
			}

			Visit(root);
			return pah.hwndFound != 0;
		}

		// === control-specific mutators (Platform.Control drives these on the concrete ControlInfo) ===

		// Eto controls have no Win32-style style words; keep these as accepted no-ops so the toggle paths compile.
		internal void SetStyle(long value) { }

		internal void SetExStyle(long value) { }

		internal void Focus() => control?.Focus();

		internal void ChildFindPoint(PointAndHwnd pah) => TryFindPoint(control, pah);

		internal bool TryInvokeDefaultClick(Point location, int clickCount)
		{
			if (control == null || clickCount < 1 || !control.Visible || !control.Enabled)
				return false;

			var invoked = false;

			for (var i = 0; i < clickCount; i++)
			{
				control.Focus();

				if (control is Button button)
				{
					button.PerformClick();
					invoked = true;
				}
				else if (control is CheckBox checkBox)
				{
					//No NotifyGuiClick: toggling raises CheckedChanged, which the Click handlers are wired to,
					//so reporting it here too would deliver one simulated click twice.
					Toggle(checkBox);
					invoked = true;
				}
				else if (control is RadioButton radioButton)
				{
					radioButton.Checked = true;
					NotifyGuiClick(location);
					invoked = true;
				}
				else if (NotifyGuiClick(location))
					invoked = true;
			}

			return invoked;
		}

		private static void Toggle(CheckBox checkBox)
		{
			if (checkBox.ThreeState)
			{
				checkBox.Checked = checkBox.Checked switch
				{
					false => true,
					true => null,
					_ => false
				};
			}
			else
				checkBox.Checked = !(checkBox.Checked ?? false);
		}

		private bool NotifyGuiClick(Point location)
		{
			if (control.GetGuiControl() is not Gui.Control guiControl)
				return false;

			guiControl._control_Click(control, new MouseEventArgs(MouseButtons.Primary, Forms.Keys.None, new PointF(location.X, location.Y)));
			return true;
		}
	}
}
#endif
