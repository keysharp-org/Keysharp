using Keysharp.Builtins;
#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// Process-wide X11 server helpers shared by the Linux window/event/capture code: the Xlib serialization
	/// lock, a guarded <c>XGetWindowProperty</c>, the EWMH client-message senders, and the test-loop error
	/// handler. These were the X11 internals of the old per-OS WindowManager; they live here now that window
	/// query/control is owned by <see cref="Platform.Window"/> and the neutral <c>WindowSearch</c>.
	/// </summary>
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

		/// <summary>Whether an X error was trapped on the current thread since the last <see cref="BeginErrorTrap"/>
		/// or <see cref="ClearErrorTrap"/>.</summary>
		internal static bool ErrorTrapped => xErrorTrapped;

		/// <summary>Resets this thread's trapped-error flag mid-sequence, for callers that re-check individual
		/// Xlib operations without reinstalling the handler.</summary>
		internal static void ClearErrorTrap() => xErrorTrapped = false;

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

		private static XDisplay Display => XDisplay.Default;

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

		internal static bool TryGetWindowProperty(nint displayHandle, long window, nint atom, nint longOffset, nint longLength,
			bool delete, nint reqType, out nint actualType, out int actualFormat, out nint nitems, out nint bytesAfter, out nint prop)
		{
			actualType = 0;
			actualFormat = 0;
			nitems = 0;
			bytesAfter = 0;
			prop = 0;

			if (!Platform.Desktop.IsX11Available || displayHandle == 0 || window == 0 || atom == 0)
				return false;

			lock (xLibLock)
			{
				var oldHandler = BeginErrorTrap();

				try
				{
					var result = Xlib.XGetWindowProperty(displayHandle, window, atom, longOffset, longLength, delete, reqType,
						out actualType, out actualFormat, out nitems, out bytesAfter, ref prop);
					_ = Xlib.XSync(displayHandle, false);

					if (ErrorTrapped || result != 0)
					{
						if (prop != 0)
						{
							_ = Xlib.XFree(prop);
							prop = 0;
						}

						actualType = 0;
						actualFormat = 0;
						nitems = 0;
						bytesAfter = 0;
						return false;
					}

					return true;
				}
				finally
				{
					EndErrorTrap(oldHandler);
				}
			}
		}

		internal static void SendNetClientMessage(nint window, nint message_type, nint l0, nint l1, nint l2)
		{
			var xev = new XEvent();
			xev.ClientMessageEvent.type = XEventName.ClientMessage;
			xev.ClientMessageEvent.send_event = true;
			xev.ClientMessageEvent.window = window;
			xev.ClientMessageEvent.message_type = message_type;
			xev.ClientMessageEvent.format = 32;
			xev.ClientMessageEvent.ptr1 = l0;
			xev.ClientMessageEvent.ptr2 = l1;
			xev.ClientMessageEvent.ptr3 = l2;
			_ = Xlib.XSendEvent(Display.Handle, window, false, EventMasks.NoEvent, ref xev);
		}

		internal static void SendNetWMMessage(nint window, nint message_type, nint l0, nint l1, nint l2, nint l3)
		{
			var xev = new XEvent();
			xev.ClientMessageEvent.type = XEventName.ClientMessage;
			xev.ClientMessageEvent.send_event = true;
			xev.ClientMessageEvent.window = window;
			xev.ClientMessageEvent.message_type = message_type;
			xev.ClientMessageEvent.format = 32;
			xev.ClientMessageEvent.ptr1 = l0;
			xev.ClientMessageEvent.ptr2 = l1;
			xev.ClientMessageEvent.ptr3 = l2;
			xev.ClientMessageEvent.ptr4 = l3;
			_ = Xlib.XSendEvent(Display.Handle, Display.Root.ID, false, EventMasks.SubstructureRedirect | EventMasks.SubstructureNofity, ref xev);
		}
	}
}
#endif
