using Keysharp.Builtins;
#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>Error trapping for script-owned GTK window mapping and the test UI loop.</summary>
	internal static class X11Server
	{
		// The X11 Winforms implementation uses this lock, so we serialize against it.
		internal static Lock xLibLock = new ();

		// XSetErrorHandler installs a PROCESS-GLOBAL handler, not a per-display one. Installing a fresh
		// per-call delegate (rooted only for that call) races with GTK/GDK's own Xlib error handling on
		// other threads — on a Wayland+XWayland session the GUI toolkit drives Xlib concurrently and juggles
		// the global handler via its own error traps. That interleaving can leave a Keysharp per-call
		// delegate installed as the global handler AFTER the call returned and the delegate was collected;
		// the next X error (e.g. a BadWindow during a later XGetWindowProperty _XReply) then dispatches to a
		// freed thunk -> SIGSEGV. A single, permanently GC-rooted handler plus a [ThreadStatic] flag removes
		// that: the globally-installed delegate is never collected, and each thread reads only its own flag
		// (an X error is dispatched on the thread that owns the connection it occurred on). All uses stay
		// under xLibLock, so BeginErrorTrap/EndErrorTrap on this side never overlap each other.
		[ThreadStatic] private static bool xErrorTrapped;
		private static readonly XErrorHandler sharedErrorHandler = SharedErrorHandler;

		private static int SharedErrorHandler(nint displayHandle, ref XErrorEvent errorEvent)
		{
			xErrorTrapped = true;
			return 0;
		}

		/// <summary>Whether an X error was trapped on the current thread since the last <see cref="BeginErrorTrap"/>.</summary>
		internal static bool ErrorTrapped => xErrorTrapped;

		/// <summary>Installs the shared (permanently rooted) X error handler and clears this thread's error
		/// flag. Must be called under <see cref="xLibLock"/> and paired with <see cref="EndErrorTrap"/> in a
		/// finally. Returns the previous handler to restore.</summary>
		internal static XErrorHandler BeginErrorTrap()
		{
			xErrorTrapped = false;
			return Xlib.XSetErrorHandler(sharedErrorHandler);
		}

		/// <summary>Restores the handler that was installed before <see cref="BeginErrorTrap"/>.</summary>
		internal static void EndErrorTrap(XErrorHandler oldHandler) => _ = Xlib.XSetErrorHandler(oldHandler);

		private static bool testLoopErrorHandlerInstalled;
		private static readonly XErrorHandler testLoopErrorHandler = HandleTestLoopXError;

		private static int HandleTestLoopXError(nint displayHandle, ref XErrorEvent errorEvent)
		{
			_ = displayHandle;

			if (errorEvent.error_code == 3)
			{
				Diagnostics.Debug.WriteLine($"Suppressed X11 BadWindow during test UI loop: request={errorEvent.request_code} resource=0x{errorEvent.resourceid.ToInt64():x}");
				return 0;
			}

			Diagnostics.Debug.WriteLine($"Suppressed X11 error during test UI loop: code={errorEvent.error_code} request={errorEvent.request_code} resource=0x{errorEvent.resourceid.ToInt64():x}");
			return 0;
		}

		internal static void InstallTestLoopXErrorHandler()
		{
			lock (xLibLock)
			{
				if (testLoopErrorHandlerInstalled)
					return;

				_ = Xlib.XSetErrorHandler(testLoopErrorHandler);
				testLoopErrorHandlerInstalled = true;
			}
		}

	}
}
#endif
