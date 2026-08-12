using Keysharp.Builtins;
#if OSX
using System.Runtime.InteropServices;
using MonoMac.AppKit;
using MonoMac.Foundation;

namespace Keysharp.Internals.Window.MacOS
{
	/// <summary>
	/// The macOS window-event source behind <c>Ks.WinEvent</c>. macOS has no single global window-event hook
	/// (the Win32 <c>SetWinEventHook</c> equivalent does not exist), so this builds the stream out of per-application
	/// <c>AXObserver</c>s: every running application (sans LSUIElement-prohibited background processes) gets one
	/// observer, kept in sync with launches/terminations via <c>NSWorkspace</c> notifications. Each observer reports
	/// window create/destroy/move/resize/minimize/restore/title-change for that app's windows, plus app activation
	/// and focused-window changes, which together drive every <see cref="WindowEventType"/> — and, only while a
	/// CaretMove subscription exists (see <see cref="SetCaretEvents"/>), selected-text changes for CaretMove.
	/// <para>
	/// All Accessibility, observer and <c>NSWorkspace</c> calls here run on the main thread (the backend posts
	/// <see cref="StartWindowEvents"/>/<see cref="StopWindowEvents"/> there, and both the AX run-loop-source callback
	/// and the NSWorkspace notifications are delivered on the main run loop), so the mutable observer state needs no
	/// locking. Every native window handle produced is a CGWindowID, matching the rest of the macOS window layer.
	/// </para>
	/// </summary>
	internal static partial class MacAccessibility
	{
		private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
		private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		// AXObserver notification names (their documented constant string values). Created once and reused for both
		// AXObserverAddNotification and CFEqual dispatch in the callback.
		private static readonly nint nApplicationActivated   = CreateCFString("AXApplicationActivated");
		private static readonly nint nFocusedWindowChanged   = CreateCFString("AXFocusedWindowChanged");
		private static readonly nint nWindowCreated          = CreateCFString("AXWindowCreated");
		private static readonly nint nUIElementDestroyed     = CreateCFString("AXUIElementDestroyed");
		private static readonly nint nWindowMoved            = CreateCFString("AXWindowMoved");
		private static readonly nint nWindowResized          = CreateCFString("AXWindowResized");
		private static readonly nint nWindowMiniaturized     = CreateCFString("AXWindowMiniaturized");
		private static readonly nint nWindowDeminiaturized   = CreateCFString("AXWindowDeminiaturized");
		private static readonly nint nTitleChanged           = CreateCFString("AXTitleChanged");
		private static readonly nint nSelectedTextChanged    = CreateCFString("AXSelectedTextChanged");

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void AXObserverCallbackDelegate(nint observer, nint element, nint notification, nint refcon);

		[LibraryImport(ApplicationServices)]
		private static partial int AXObserverCreate(int application, nint callback, out nint outObserver);

		[LibraryImport(ApplicationServices)]
		private static partial int AXUIElementSetMessagingTimeout(nint element, float timeoutInSeconds);

		[LibraryImport(ApplicationServices)]
		private static partial int AXObserverAddNotification(nint observer, nint element, nint notification, nint refcon);

		[LibraryImport(ApplicationServices)]
		private static partial int AXObserverRemoveNotification(nint observer, nint element, nint notification);

		[LibraryImport(ApplicationServices)]
		private static partial nint AXObserverGetRunLoopSource(nint observer);

		[LibraryImport(CoreFoundationPath)]
		private static partial nint CFRunLoopGetMain();

		[LibraryImport(CoreFoundationPath)]
		private static partial void CFRunLoopAddSource(nint rl, nint source, nint mode);

		[LibraryImport(CoreFoundationPath)]
		private static partial void CFRunLoopRemoveSource(nint rl, nint source, nint mode);

		[LibraryImport(CoreFoundationPath)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static partial bool CFEqual(nint cf1, nint cf2);

		/// <summary>One application's observer, the app-level AX element it is attached to, and the set of its
		/// windows we have registered per-window notifications for (tracked by CGWindowID for terminate cleanup).</summary>
		private sealed class AppObserverState
		{
			internal readonly int pid;
			internal nint appElement;
			internal nint observer;
			internal nint runLoopSource;
			internal readonly HashSet<uint> windowIds = new();

			internal AppObserverState(int pid) => this.pid = pid;
		}

		// --- observer state (main-thread only) ----------------------------------------------------------------
		private static Action<WindowEventRaw> eventSink;
		private static readonly Dictionary<int, AppObserverState> appObservers = new();
		private static readonly Dictionary<uint, nint> windowElements = new();   // CGWindowID -> retained AXUIElementRef
		private static NSObject appLaunchObserver;
		private static NSObject appTerminateObserver;
		private static NSObject appActivatedObserver;
		private static AXObserverCallbackDelegate axCallbackDelegate;            // kept alive for the process lifetime
		private static nint axCallbackPtr;
		private static nint runLoopMode;                                        // kCFRunLoopCommonModes
		private static nint lastActiveHwnd;                                     // de-dupes overlapping Active signals
		private static bool windowEventsRunning;
		private static bool caretEventsEnabled;                                 // AXSelectedTextChanged registered too?

		/// <summary>Installs the AX-observer window-event stream (idempotent). Requires Accessibility permission;
		/// returns having logged guidance if it is not granted. Must be called on the main thread.</summary>
		internal static void StartWindowEvents(Action<WindowEventRaw> sink)
		{
			eventSink = sink;

			if (windowEventsRunning)
				return;

			// AXObserverCreate fails outright without Accessibility trust, so there is no usable degraded mode here.
			if (!EnsureAccessibilityAccess("monitor window events", prompt: true))
				return;

			try
			{
				if (axCallbackPtr == 0)
				{
					axCallbackDelegate = ObserverCallback;
					axCallbackPtr = Marshal.GetFunctionPointerForDelegate(axCallbackDelegate);
				}

				if (runLoopMode == 0)
					runLoopMode = ResolveCoreFoundationConstant("kCFRunLoopCommonModes");

				var wsnc = NSWorkspace.SharedWorkspace.NotificationCenter;
				// On launch, add the new app's observer straight from the notification: NSWorkspace's
				// RunningApplications list can still be momentarily stale at this point, so a plain reconcile here
				// races and may miss the app (and thus every event from its first window). Activation is the safety
				// net — by the time an app is activated it is reliably listed, so reconcile then catches anything the
				// launch path missed (e.g. apps already running before the stream started but only now seen).
				appLaunchObserver = wsnc.AddObserver("NSWorkspaceDidLaunchApplicationNotification", OnAppLaunched);
				appTerminateObserver = wsnc.AddObserver("NSWorkspaceDidTerminateApplicationNotification", _ => ReconcileApps());
				appActivatedObserver = wsnc.AddObserver("NSWorkspaceDidActivateApplicationNotification", _ => ReconcileApps());

				windowEventsRunning = true;
				ReconcileApps();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS window-event observer start failed: {ex.Message}");
			}
		}

		/// <summary>Turns the caret half of the stream (AXSelectedTextChanged, behind <c>WinEvent.CaretMove</c>) on or
		/// off across every application observer, adding or removing that one notification in place so the rest of the
		/// stream is untouched. It is registered separately from the always-on notifications because it is the only
		/// one whose traffic is per-keystroke, and no script should pay for it without a CaretMove subscription. Any
		/// observer created later picks the current setting up in <see cref="AddAppObserver"/>. Must be called on the
		/// main thread; safe to call before the stream is running (the flag alone then decides).</summary>
		internal static void SetCaretEvents(bool enabled)
		{
			if (caretEventsEnabled == enabled)
				return;

			caretEventsEnabled = enabled;

			try
			{
				foreach (var state in appObservers.Values)
				{
					if (state.observer == 0 || state.appElement == 0)
						continue;

					if (enabled)
						_ = AXObserverAddNotification(state.observer, state.appElement, nSelectedTextChanged, (nint)state.pid);
					else
						_ = AXObserverRemoveNotification(state.observer, state.appElement, nSelectedTextChanged);
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS caret-event registration failed: {ex.Message}");
			}
		}

		/// <summary>Tears the whole stream down (idempotent). Must be called on the main thread.</summary>
		internal static void StopWindowEvents()
		{
			windowEventsRunning = false;
			eventSink = null;

			try
			{
				var wsnc = NSWorkspace.SharedWorkspace.NotificationCenter;

				if (appLaunchObserver != null)
				{
					wsnc.RemoveObserver(appLaunchObserver);
					appLaunchObserver = null;
				}

				if (appTerminateObserver != null)
				{
					wsnc.RemoveObserver(appTerminateObserver);
					appTerminateObserver = null;
				}

				if (appActivatedObserver != null)
				{
					wsnc.RemoveObserver(appActivatedObserver);
					appActivatedObserver = null;
				}

				foreach (var pid in appObservers.Keys.ToArray())
					RemoveAppObserver(pid, emitCloseForWindows: false);

				appObservers.Clear();
				windowElements.Clear();
				lastActiveHwnd = 0;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS window-event observer stop failed: {ex.Message}");
			}
		}

		// --- application tracking --------------------------------------------------------------------------------

		/// <summary>Brings the per-app observer set in line with the currently running applications. Called on
		/// start and on every launch/terminate notification (diff rather than reacting to a single app keeps it
		/// robust against missed/coalesced notifications).</summary>
		/// <summary>Adds an observer for a just-launched app straight from the notification's
		/// <c>NSWorkspaceApplicationKey</c> (avoiding the stale-RunningApplications race), then reconciles as a
		/// fallback in case the key wasn't present.</summary>
		private static void OnAppLaunched(NSNotification note)
		{
			if (!windowEventsRunning)
				return;

			try
			{
				var value = note?.UserInfo?["NSWorkspaceApplicationKey"];
				var app = value == null ? null : MonoMac.ObjCRuntime.Runtime.GetNSObject<NSRunningApplication>(value.Handle);

				if (app != null && app.ActivationPolicy != NSApplicationActivationPolicy.Prohibited)
				{
					var pid = app.ProcessIdentifier;

					if (pid > 0 && !appObservers.ContainsKey(pid))
						AddAppObserver(pid);
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS window-event launch handler failed: {ex.Message}");
			}

			ReconcileApps();
		}

		private static void ReconcileApps()
		{
			if (!windowEventsRunning)
				return;

			var current = new HashSet<int>();

			try
			{
				foreach (var app in NSWorkspace.SharedWorkspace.RunningApplications)
				{
					if (app == null || app.ActivationPolicy == NSApplicationActivationPolicy.Prohibited)
						continue;   // background/agent processes with no user-facing windows

					var pid = app.ProcessIdentifier;

					if (pid <= 0)
						continue;

					_ = current.Add(pid);

					if (!appObservers.ContainsKey(pid))
						AddAppObserver(pid);
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS window-event app reconcile failed: {ex.Message}");
			}

			foreach (var pid in appObservers.Keys.Where(p => !current.Contains(p)).ToArray())
				RemoveAppObserver(pid, emitCloseForWindows: true);
		}

		private static void AddAppObserver(int pid)
		{
			var appElement = AXUIElementCreateApplication(pid);

			if (appElement == 0)
				return;

			// Attribute reads below are synchronous cross-process IPC that block until the target app's main thread
			// answers; a single hung/unresponsive app would otherwise freeze our main run loop. A short messaging
			// timeout (set on the app element, which also bounds attribute calls to that app's window elements) caps
			// the stall at a fraction of a second so a wedged app is skipped rather than deadlocking us. Note this
			// governs copy/attribute calls but not AXObserverAddNotification itself; those are fast in practice, so a
			// fully wedged app remains a (narrow, unavoidable) risk there.
			_ = AXUIElementSetMessagingTimeout(appElement, 0.2f);

			if (AXObserverCreate(pid, axCallbackPtr, out var observer) != kAXErrorSuccess || observer == 0)
			{
				CFRelease(appElement);
				return;
			}

			var state = new AppObserverState(pid) { appElement = appElement, observer = observer };
			var refconPid = (nint)pid;

			// Only application-scoped notifications are registered on the application element. The window-lifetime
			// notifications (move/resize/minimize/restore/title-change/destroy) must be registered per window element
			// (see TrackWindow) — macOS does not deliver them when they are registered on the application element.
			// refcon is the pid so the callback can find the owning state.
			_ = AXObserverAddNotification(observer, appElement, nApplicationActivated, refconPid);
			_ = AXObserverAddNotification(observer, appElement, nFocusedWindowChanged, refconPid);
			_ = AXObserverAddNotification(observer, appElement, nWindowCreated, refconPid);

			// Caret movement is only observed while a CaretMove subscription exists (see SetCaretEvents) — it is the
			// one notification here that fires per keystroke. It is app-scoped like the three above: the notification
			// is posted by whichever text element the user is editing, and registering it on the application element
			// catches all of them without having to find and track that element.
			if (caretEventsEnabled)
				_ = AXObserverAddNotification(observer, appElement, nSelectedTextChanged, refconPid);

			state.runLoopSource = AXObserverGetRunLoopSource(observer);

			if (state.runLoopSource != 0)
				CFRunLoopAddSource(CFRunLoopGetMain(), state.runLoopSource, runLoopMode);

			appObservers[pid] = state;

			// Seed tracking for the app's already-open windows so their move/close events are caught (without
			// firing Create for windows that existed before we attached).
			EnumerateAppWindows(state);
		}

		private static void RemoveAppObserver(int pid, bool emitCloseForWindows)
		{
			if (!appObservers.Remove(pid, out var state))
				return;

			foreach (var id in state.windowIds)
			{
				if (windowElements.Remove(id, out var element))
					CFRelease(element);

				// An app quitting destroys its windows; surface a confirmed Close for the ones we were tracking (the
				// manager de-dupes against any per-window destroyed notifications that also fired).
				if (emitCloseForWindows)
					Emit(WindowEventType.Close, (nint)id, destroyConfirmed: true);
			}

			if (state.runLoopSource != 0)
				CFRunLoopRemoveSource(CFRunLoopGetMain(), state.runLoopSource, runLoopMode);

			if (state.observer != 0)
				CFRelease(state.observer);

			if (state.appElement != 0)
				CFRelease(state.appElement);
		}

		private static void EnumerateAppWindows(AppObserverState state)
		{
			if (!TryCopyAttributeValue(state.appElement, attrWindows, out var windowsArray))
				return;

			try
			{
				var count = CFArrayGetCount(windowsArray);

				for (nint i = 0; i < count; i++)
				{
					var entry = CFArrayGetValueAtIndex(windowsArray, i);

					if (entry != 0)
						_ = TrackWindow(state, entry, emitCreateShow: false);
				}
			}
			finally
			{
				CFRelease(windowsArray);
			}
		}

		// --- window tracking -------------------------------------------------------------------------------------

		/// <summary>Registers the per-window notifications (move/resize/minimize/restore/title-change/destroy) for a
		/// window element, keyed by its CGWindowID (passed as the refcon so they can be attributed in the callback —
		/// essential for destroy, whose element is already dead), and optionally emits Create+Show for a freshly
		/// created window. Returns the resolved CGWindowID (0 if it couldn't be determined — the create path then
		/// schedules a bounded retry via <see cref="ScheduleTrackRetry"/>); already-tracked windows are returned as-is
		/// without re-registering or re-emitting.</summary>
		private static uint TrackWindow(AppObserverState state, nint windowElement, bool emitCreateShow)
		{
			if (!TryResolveWindowId(windowElement, out var id))
				return 0;

			if (windowElements.ContainsKey(id))
				return id;

			var retained = CFRetain(windowElement);

			if (retained == 0)
				return 0;

			windowElements[id] = retained;
			_ = state.windowIds.Add(id);

			var refcon = (nint)id;
			_ = AXObserverAddNotification(state.observer, retained, nWindowMoved, refcon);
			_ = AXObserverAddNotification(state.observer, retained, nWindowResized, refcon);
			_ = AXObserverAddNotification(state.observer, retained, nWindowMiniaturized, refcon);
			_ = AXObserverAddNotification(state.observer, retained, nWindowDeminiaturized, refcon);
			_ = AXObserverAddNotification(state.observer, retained, nTitleChanged, refcon);
			_ = AXObserverAddNotification(state.observer, retained, nUIElementDestroyed, refcon);

			if (emitCreateShow)
			{
				Emit(WindowEventType.Create, (nint)id);
				Emit(WindowEventType.Show, (nint)id);
			}

			return id;
		}

		/// <summary>Resolves a window AX element to its CGWindowID, preferring the (undocumented but usually present)
		/// AXWindowNumber and falling back to a centre-point hit-test against the CG window list.</summary>
		private static bool TryResolveWindowId(nint windowElement, out uint id)
		{
			id = 0;

			if (windowElement == 0)
				return false;

			if (TryReadInt32(windowElement, attrWindowNumber, out var number) && number > 0)
			{
				id = (uint)number;
				return true;
			}

			if (TryReadRect(windowElement, out var rect) && !rect.IsEmpty)
			{
				var centre = new POINT(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

				if (MacNativeWindows.TryGetWindowAtPoint(centre, out var native) && native.WindowNumber != 0)
				{
					id = native.WindowNumber;
					return true;
				}
			}

			return false;
		}

		// A just-created window whose CGWindowID isn't resolvable yet is retried a few times at a short interval before
		// being given up on (a window that never resolves — e.g. a transient that closed immediately — must not retry
		// forever). ~5 * 60ms covers the typical AXWindowNumber-population lag without a perceptible Create/Show delay.
		private const int WindowCreateResolveRetries = 5;
		private const int WindowCreateResolveRetryMs = 60;

		/// <summary>Schedules a bounded, short-delay retry to resolve and track a just-created window whose CGWindowID
		/// wasn't available at AXWindowCreated time (AXWindowNumber not yet populated / window still offscreen). The
		/// element is retained across the delay; a managed one-shot timer hops back to the main thread (all AX access
		/// must happen there) before re-attempting via <see cref="TrackWindow"/>, which emits Create/Show once it
		/// resolves. Without this the window would be invisible to WinEvent — no Create/Show and no per-window
		/// move/close/etc. — for its whole life.</summary>
		private static void ScheduleTrackRetry(AppObserverState state, nint windowElement, int remaining)
		{
			if (remaining <= 0 || windowElement == 0 || !windowEventsRunning)
				return;

			// Keep the element alive across the delay (the AX framework owns it only for the callback's duration).
			// Called on the main thread, so this CFRetain — like the matching CFRelease below — stays main-thread.
			var retained = CFRetain(windowElement);

			if (retained == 0)
				return;

			System.Threading.Timer timer = null;
			timer = new System.Threading.Timer(_ =>
			{
				timer.Dispose();   // one-shot

				// The timer callback is on a thread-pool thread; all AX/observer state is main-thread-only, so hop back.
				Script.PostToUIThread(() =>
				{
					try
					{
						// Only retry while this exact observer is still live — the app may have terminated meanwhile,
						// which removes/replaces its state and releases its element registrations.
						if (windowEventsRunning
							&& appObservers.TryGetValue(state.pid, out var live)
							&& ReferenceEquals(live, state)
							&& TrackWindow(live, retained, emitCreateShow: true) == 0)
							ScheduleTrackRetry(live, retained, remaining - 1);
					}
					finally
					{
						CFRelease(retained);
					}
				});
			}, null, WindowCreateResolveRetryMs, System.Threading.Timeout.Infinite);
		}

		// --- the run-loop callback -------------------------------------------------------------------------------

		private static void ObserverCallback(nint observer, nint element, nint notification, nint refcon)
		{
			try
			{
				if (eventSink == null)
					return;

				// The per-window notifications carry the CGWindowID in refcon (set at registration); the app-element
				// ones (created/focused/activated) carry the pid and resolve the window from the element.
				if (CFEqual(notification, nUIElementDestroyed))
				{
					var id = (uint)refcon;

					// AXUIElementDestroyed normally means the window is really gone, so the raw Close is marked
					// confirmed: the manager then forces it out of every Exist/NotExist matching set regardless of
					// DetectHiddenWindows and ahead of any CGWindowList lag (which can still list a just-destroyed
					// window briefly). The exception is one of our own windows ordered out of the window server by a
					// WinHide: that also fires this notification but the window still exists, so it is emitted
					// unconfirmed — letting the manager re-evaluate membership (NotExist then fires only with
					// DetectHiddenWindows off, where a hidden window counts as gone).
					var hidden = id != 0 && MacNativeWindows.IsHiddenOwnWindow(id);

					// Only drop tracking for a genuine destruction. A hidden own-window's AX element is still alive
					// and will be re-shown (WinShow re-orders it in without firing AXWindowCreated), so keep its
					// registration so later move/minimize/close events for it aren't lost after a hide/show cycle.
					if (!hidden && id != 0 && windowElements.Remove(id, out var dead))
					{
						CFRelease(dead);

						// Forget a recycled active window so the next genuine activation isn't deduped away if the
						// window server later reuses this CGWindowID.
						if ((nint)id == lastActiveHwnd)
							lastActiveHwnd = 0;
					}

					Emit(WindowEventType.Close, (nint)id, destroyConfirmed: !hidden);
				}
				else if (CFEqual(notification, nWindowMoved) || CFEqual(notification, nWindowResized))
				{
					var bounds = TryReadRect(element, out var rect) ? rect : (Rectangle?)null;
					Emit(WindowEventType.Move, (nint)(uint)refcon, bounds);
				}
				else if (CFEqual(notification, nWindowMiniaturized))
				{
					Emit(WindowEventType.Minimize, (nint)(uint)refcon);
				}
				else if (CFEqual(notification, nWindowDeminiaturized))
				{
					Emit(WindowEventType.Restore, (nint)(uint)refcon);
					Emit(WindowEventType.Show, (nint)(uint)refcon);
				}
				else if (CFEqual(notification, nTitleChanged))
				{
					Emit(WindowEventType.TitleChange, (nint)(uint)refcon);
				}
				else if (CFEqual(notification, nSelectedTextChanged))
				{
					OnCaretMoved(element, (int)refcon);
				}
				else if (CFEqual(notification, nWindowCreated))
				{
					// TrackWindow returns 0 when the CGWindowID isn't resolvable yet (AXWindowNumber not populated for a
					// brand-new/offscreen window). Dropping it here would leave the window invisible to WinEvent for its
					// whole life, so schedule a bounded short retry (by which time AXWindowNumber is normally set).
					if (appObservers.TryGetValue((int)refcon, out var state) && TrackWindow(state, element, emitCreateShow: true) == 0)
						ScheduleTrackRetry(state, element, WindowCreateResolveRetries);
				}
				else if (CFEqual(notification, nFocusedWindowChanged))
				{
					// element is the newly focused window.
					if (TryResolveWindowId(element, out var id))
						Emit(WindowEventType.Active, (nint)id);
				}
				else if (CFEqual(notification, nApplicationActivated))
				{
					// element is the application; its AXFocusedWindow is the window that is now active.
					if (appObservers.TryGetValue((int)refcon, out var state)
						&& TryCopyAttributeValue(state.appElement, attrFocusedWindow, out var focused))
					{
						try
						{
							if (TryResolveWindowId(focused, out var id))
								Emit(WindowEventType.Active, (nint)id);
						}
						finally
						{
							CFRelease(focused);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS window-event callback failed: {ex.Message}");
			}
		}

		/// <summary>Turns an AXSelectedTextChanged notification into a CaretMove event. macOS has no caret-specific
		/// notification: a caret move is a selection change to a zero-length range, so the caret rectangle is read
		/// back from the element that posted it (collapsing whatever it now has selected — the manager suppresses the
		/// repeats that a plain selection change therefore produces at an unchanged caret). The event is reported
		/// against the element's containing window, falling back to the app's focused window for elements that don't
		/// expose AXWindow; without a resolvable window there is nothing a WinTitle criteria could match, so it is
		/// dropped.</summary>
		private static void OnCaretMoved(nint element, int pid)
		{
			if (!TryGetCaretRect(element, out var caret))
				return;

			if (!TryResolveContainingWindowId(element, pid, out var id))
				return;

			Emit(WindowEventType.CaretMove, (nint)id, caret);
		}

		/// <summary>The CGWindowID of the window an element lives in: its AXWindow if it has one, else the focused
		/// window of the owning application.</summary>
		private static bool TryResolveContainingWindowId(nint element, int pid, out uint id)
		{
			id = 0;

			if (element != 0 && TryCopyAttributeValue(element, attrWindow, out var window))
			{
				try
				{
					if (TryResolveWindowId(window, out id))
						return true;
				}
				finally
				{
					CFRelease(window);
				}
			}

			if (!appObservers.TryGetValue(pid, out var state)
				|| !TryCopyAttributeValue(state.appElement, attrFocusedWindow, out var focused))
				return false;

			try
			{
				return TryResolveWindowId(focused, out id);
			}
			finally
			{
				CFRelease(focused);
			}
		}

		private static void Emit(WindowEventType type, nint hwnd, Rectangle? bounds = null, bool destroyConfirmed = false)
		{
			var sink = eventSink;

			if (sink == null || hwnd == 0)
				return;

			// Active is sourced from two overlapping signals (app activation + focused-window change); collapsing
			// consecutive reports of the same window removes the duplicate an app switch produces while still
			// firing whenever the active window genuinely changes.
			if (type == WindowEventType.Active)
			{
				if (hwnd == lastActiveHwnd)
					return;

				lastActiveHwnd = hwnd;
			}

			// The 3rd callback arg follows the locked cross-platform contract: a 64-bit monotonic milliseconds-since-boot
			// timestamp (Environment.TickCount64), stamped at delivery — the same clock the Linux backend uses and what
			// the Windows backend reconstructs from its native 32-bit event time. AX notifications carry no usable event
			// time, so delivery time is the closest we can capture.
			sink(new WindowEventRaw(type, hwnd, Environment.TickCount64) { Bounds = bounds, DestroyConfirmed = destroyConfirmed });
		}

		private static nint ResolveCoreFoundationConstant(string symbolName)
		{
			if (!NativeLibrary.TryLoad(CoreFoundationPath, out var coreFoundation))
				return 0;

			try
			{
				return NativeLibrary.TryGetExport(coreFoundation, symbolName, out var symbol) && symbol != 0
					? Marshal.ReadIntPtr(symbol)
					: 0;
			}
			finally
			{
				NativeLibrary.Free(coreFoundation);
			}
		}
	}
}
#endif
