#if LINUX
namespace Keysharp.Core.Linux
{
	/// <summary>
	/// Lightweight wrapper that exposes an Eto control as a WindowItemBase so Control-related APIs can operate on it.
	/// </summary>
	internal sealed class ControlItem : WindowItemBase
	{
		private readonly Control control;

		internal ControlItem(nint handle) : base(handle)
		{
			control = Control.FromHandle(handle);
		}

		internal ControlItem(Control control) : base(control?.Handle ?? nint.Zero)
		{
			this.control = control;
		}

		internal Control Control => control;

		internal override bool Active
		{
			get => control?.HasFocus ?? false;
			set
			{
				if (value)
					control?.Focus();
			}
		}

		internal override bool AlwaysOnTop
		{
			get => false;
			set { }
		}

		internal override bool Bottom
		{
			set { }
		}

		internal override HashSet<WindowItemBase> ChildWindows
		{
			get
			{
				var set = new HashSet<WindowItemBase>();
				if (control == null)
					return set;

				foreach (var child in control.VisualControls)
				{
					// Skip pure layout containers; wrap actual controls.
					if (child is Layout)
						continue;
					set.Add(new ControlItem(child));
				}

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

		internal override Rectangle ClientLocation => Location;

		internal override bool Enabled
		{
			get => control?.Enabled ?? false;
			set
			{
				if (control != null)
					control.Enabled = value;
			}
		}

		internal override bool Exists => control != null;

		internal override long ExStyle
		{
			get => 0;
			set { }
		}

		internal override bool IsHung => false;

		internal override Rectangle Location
		{
			get
			{
				if (control == null)
					return Rectangle.Empty;

				return control.GetBounds();
			}
			set
			{
				if (control != null) 
				{
					control.SetBounds(value);
				}
			}
		}

		internal override WindowItemBase NonChildParentWindow => ParentWindow;

		internal override WindowItemBase ParentWindow
		{
			get
			{
				if (control == null)
					return null;

				var form = control as Form ?? control.ParentWindow as Form ?? control.Parent as Form;
				return form != null ? WindowManager.CreateWindow(form.Handle) : null;
			}
		}

		internal override long PID => ParentWindow?.PID ?? 0;

		internal override Size Size
		{
			get => new Size(Location.Width, Location.Height);
			set => Location = new Rectangle(Location.X, Location.Y, value.Width, value.Height);
		}

		internal override long Style
		{
			get => 0;
			set { }
		}

		internal override List<string> Text => control?.Text is string s && !string.IsNullOrEmpty(s) ? new List<string> { s } : new List<string>();

		internal override string Title
		{
			get => control?.Text ?? string.Empty;
			set
			{
				if (control != null)
					control.Text = value;
			}
		}

		internal override object Transparency
		{
			get => null;
			set { }
		}

		internal override object TransparentColor
		{
			get => null;
			set { }
		}

		internal override bool Visible
		{
			get => control?.Visible ?? false;
			set
			{
				if (control != null)
					control.Visible = value;
			}
		}

		internal override FormWindowState WindowState
		{
			get => FormWindowState.Normal;
			set { }
		}

		internal override void ChildFindPoint(PointAndHwnd pah) { }

		internal override void Click(Point? location = null) { }

		internal override void ClickRight(Point? location = null) { }

		internal override POINT ClientToScreen()
		{
			if (control == null)
				return new POINT();

			var pt = control.PointToScreen(Point.Empty);
			return new POINT(pt.X.Ai(), pt.Y.Ai());
		}

		internal override bool Close() => false;

		internal override bool Hide()
		{
			if (control == null)
				return false;
			control.Visible = false;
			return true;
		}

		internal override bool Kill() => false;

		internal override bool Redraw() 
		{ 
			control.Invalidate();
			return true;
		}

		internal override bool Show()
		{
			if (control == null)
				return false;
			control.Visible = true;
			return true;
		}
	}
}
#endif
