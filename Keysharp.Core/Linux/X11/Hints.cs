#if LINUX
namespace Keysharp.Core.Linux.X11
{
	[Flags]
	internal enum XSizeHintsFlags
	{
		USPosition = 1 << 0,
		USSize = 1 << 1,
		PPosition = 1 << 2,
		PSize = 1 << 3,
		PMinSize = 1 << 4,
		PMaxSize = 1 << 5,
		PResizeInc = 1 << 6,
		PAspect = 1 << 7,
		PAllHints = PPosition | PSize | PMinSize | PMaxSize | PResizeInc | PAspect,
		PBaseSize = 1 << 8,
		PWinGravity = 1 << 9,
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XSizeHints
	{
		internal nint flags;
		internal int x;
		internal int y;
		internal int width;
		internal int height;
		internal int min_width;
		internal int min_height;
		internal int max_width;
		internal int max_height;
		internal int width_inc;
		internal int height_inc;
		internal int min_aspect_x;
		internal int min_aspect_y;
		internal int max_aspect_x;
		internal int max_aspect_y;
		internal int base_width;
		internal int base_height;
		internal int win_gravity;
	}

	[Flags]
	internal enum XWMHintsFlags
	{
		InputHint = 1 << 0,
		StateHint = 1 << 1,
		IconPixmapHint = 1 << 2,
		IconWindowHint = 1 << 3,
		IconPositionHint = 1 << 4,
		IconMaskHint = 1 << 5,
		WindowGroupHint = 1 << 6,
		AllHints = InputHint | StateHint | IconPixmapHint | IconWindowHint | IconPositionHint | IconMaskHint | WindowGroupHint
	}


	[StructLayout(LayoutKind.Sequential)]
	internal struct XWMHints
	{
		internal nint flags;
		internal bool input;
		internal XInitialState initial_state;
		internal nint icon_pixmap;
		internal nint icon_window;
		internal int icon_x;
		internal int icon_y;
		internal nint icon_mask;
		internal nint window_group;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct XClassHint
	{
		public nint resName;
		public nint resClass;

		public static XClassHint Zero = new XClassHint();
	}
}
#endif
