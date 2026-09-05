#if LINUX
namespace Keysharp.Internals.Window.Linux.Proxies
{
	/// <summary>The GUI thread's connection for checking whether its own GTK window has mapped.</summary>
	internal sealed class XDisplay
	{
		[ThreadStatic] private static XDisplay current;
		internal nint Handle { get; }
		private XDisplay(nint handle) => Handle = handle;

		internal static XDisplay Default
		{
			get
			{
				if (current != null) return current;
				var handle = Xlib.XOpenDisplay(0);
				// Keep this thread-owned connection until process exit; finalizer-thread close can deadlock Xlib.
				return handle == 0 ? null : current = new XDisplay(handle);
			}
		}
	}
}
#endif
