#if !WINDOWS
namespace Keysharp.Internals
{
	/// <summary>
	/// Synthesizes a Win32-shaped <c>WS_*</c> style word for an Eto window or control.
	/// <para>
	/// Neither X11, Wayland nor macOS has a real Win32 style word. Returning Eto's own
	/// <see cref="Eto.Forms.WindowStyle"/> enum value (which is what the Linux backend used to do) is worse
	/// than useless: <c>Default</c> is 0 and <c>None</c> is 1, so a script testing <c>style &amp; WS_CAPTION</c>
	/// reads a bit that means nothing. Every toolkit property below does have an unambiguous Win32
	/// counterpart, so mapping them produces a value scripts can actually test with the usual constants.
	/// </para>
	/// <para>
	/// This is a one-way projection for reading. Bits with no toolkit equivalent are simply never set;
	/// writing a style back is handled per-platform (see <c>MacWindow.TrySetStyle</c>) and covers fewer bits
	/// still, so a read/modify/write round trip is not lossless.
	/// </para>
	/// </summary>
	internal static class EtoWindowStyles
	{
		internal const long WS_OVERLAPPED = 0x00000000;
		internal const long WS_POPUP = unchecked((long)0x80000000);
		internal const long WS_CHILD = 0x40000000;
		internal const long WS_MINIMIZE = 0x20000000;
		internal const long WS_VISIBLE = 0x10000000;
		internal const long WS_DISABLED = 0x08000000;
		internal const long WS_MAXIMIZE = 0x01000000;
		internal const long WS_CAPTION = 0x00C00000;   // WS_BORDER | WS_DLGFRAME
		internal const long WS_BORDER = 0x00800000;
		internal const long WS_SYSMENU = 0x00080000;
		internal const long WS_THICKFRAME = 0x00040000;
		internal const long WS_MINIMIZEBOX = 0x00020000;
		internal const long WS_MAXIMIZEBOX = 0x00010000;
		internal const long WS_TABSTOP = 0x00010000;   // same bit as WS_MAXIMIZEBOX; child controls only

		internal const long WS_EX_TOPMOST = 0x00000008;
		internal const long WS_EX_TRANSPARENT = 0x00000020;
		internal const long WS_EX_TOOLWINDOW = 0x00000080;
		internal const long WS_EX_LAYERED = 0x00080000;

		/// <summary>
		/// The extended style word, from the window state the toolkit knows: always-on-top, and a Gui's
		/// +ToolWindow and +ClickThrough, the last as WS_EX_TRANSPARENT | WS_EX_LAYERED, as Windows sets it.
		/// A child control has none of these, so it reports 0.
		/// </summary>
		internal static long ExFor(Eto.Forms.Control control)
		{
			if (control is not Eto.Forms.Window window)
				return 0L;

			var exStyle = 0L;

			try
			{
				if (window.Topmost)
					exStyle |= WS_EX_TOPMOST;

				if (window is Keysharp.Builtins.KeysharpForm form)
				{
					if (form.IsToolWindow)
						exStyle |= WS_EX_TOOLWINDOW;

					if (form.clickThrough)
						exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
				}
			}
			catch
			{
				// As in ForWindow: a window torn down mid-read reports what was collected.
			}

			return exStyle;
		}

		/// <summary>
		/// The style word for any Eto control, dispatching to the window or child-control mapping.
		/// </summary>
		internal static long For(Eto.Forms.Control control)
			=> control switch
			{
				null => 0L,
				Eto.Forms.Window window => ForWindow(window),
				_ => ForControl(control)
			};

		/// <summary>
		/// Top-level windows: frame furniture plus visible/disabled/minimized/maximized state.
		/// </summary>
		internal static long ForWindow(Eto.Forms.Window window)
		{
			if (window == null)
				return 0L;

			var style = 0L;

			try
			{
				// A borderless window is the closest thing to a bare WS_POPUP; anything else carries the
				// caption bits, which is what scripts test for when they add or remove a title bar.
				if (window.WindowStyle == Eto.Forms.WindowStyle.None)
					style |= WS_POPUP;
				else
					style |= WS_CAPTION;

				if (window.Closeable)
					style |= WS_SYSMENU;

				if (window.Resizable)
					style |= WS_THICKFRAME;

				if (window.Minimizable)
					style |= WS_MINIMIZEBOX;

				if (window.Maximizable)
					style |= WS_MAXIMIZEBOX;

				if (window.Visible)
					style |= WS_VISIBLE;

				if (!window.Enabled)
					style |= WS_DISABLED;

				style |= window.WindowState switch
				{
					Eto.Forms.WindowState.Minimized => WS_MINIMIZE,
					Eto.Forms.WindowState.Maximized => WS_MAXIMIZE,
					_ => 0L
				};
			}
			catch
			{
				// A window torn down mid-read (or a backend that refuses a property before the native
				// window exists) reports whatever was collected rather than throwing out of a getter.
			}

			return style;
		}

		/// <summary>
		/// Child controls: only the handful of bits that are meaningful without a native style word.
		/// </summary>
		internal static long ForControl(Eto.Forms.Control control)
		{
			if (control == null)
				return 0L;

			var style = WS_CHILD;

			try
			{
				if (control.Visible)
					style |= WS_VISIBLE;

				if (!control.Enabled)
					style |= WS_DISABLED;
			}
			catch
			{
			}

			return style;
		}
	}
}
#endif
