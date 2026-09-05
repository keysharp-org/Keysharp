using Keysharp.Builtins;
#if LINUX
namespace Keysharp.Internals.Window.Linux.X11
{
	/// <summary>The Xlib surface Keysharp still calls directly. Foreign window, capture, pointer and keyboard
	/// work belongs to keysharp-desktop; what remains is waiting for the script's own GTK window to map,
	/// trapping the X errors that wait can raise, and three non-X11 imports that have no better home.</summary>
	internal class Xlib
	{
		private const string libCName = "libc";
		private const string libGdiPlusName = "libgdiplus";
		private const string libX11Name = "libX11.so.6";

		[DllImport(libX11Name)]
		internal static extern int XInitThreads();

		[DllImport(libX11Name)]
		internal static extern nint XOpenDisplay(nint from);

		[DllImport(libX11Name)]
		internal static extern int XSync(nint display, bool discard);

		[DllImport(libX11Name)]
		internal static extern int XGetWindowAttributes(nint display, long window, ref XWindowAttributes attributes);

		[DllImport(libX11Name)]
		internal static extern XErrorHandler XSetErrorHandler(XErrorHandler handler);

		[DllImport(libCName)]
		internal static extern int gettid();

		[DllImport(libCName)]
		internal static extern uint geteuid();

		[DllImport(libGdiPlusName, ExactSpelling = true)]
		internal static extern int GdipDisposeImage(nint image);
	}

	internal delegate int XErrorHandler(nint displayHandle, ref XErrorEvent errorEvent);
}
#endif
