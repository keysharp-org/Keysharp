#if LINUX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// AT-SPI caret-movement notifications, the Linux source behind <c>WinEvent.CaretMove</c>. Kept in the same
	/// partial class as the caret <em>query</em> (see <c>LinuxAccessibility.cs</c>) so all libatspi interop and the
	/// caret-rectangle logic live together and the event and <c>CaretGetPos</c> always agree on a position.
	/// </summary>
	internal static partial class LinuxAccessibility
	{
		private const string CaretMovedEvent = "object:text-caret-moved";

		// void (*AtspiEventListenerCB) (AtspiEvent *event, void *user_data)
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void AtspiEventListenerCB(nint atspiEvent, nint userData);

		[LibraryImport("libatspi.so.0")]
		private static partial nint atspi_event_listener_new(nint callback, nint userData, nint callbackDestroyed);

		[LibraryImport("libatspi.so.0", StringMarshalling = StringMarshalling.Utf8)]
		private static partial int atspi_event_listener_register(nint listener, string eventType, ref nint error);

		[LibraryImport("libatspi.so.0", StringMarshalling = StringMarshalling.Utf8)]
		private static partial int atspi_event_listener_deregister(nint listener, string eventType, ref nint error);

		/// <summary>
		/// Subscribes to AT-SPI's <c>object:text-caret-moved</c> signal and turns each notification into a
		/// <see cref="WindowEventType.CaretMove"/> event. Unlike the X11/Wayland window-event backends this is
		/// display-server agnostic — accessibility runs over its own bus — so both of them own one of these and
		/// forward the CaretMove category to it.
		/// <para>
		/// Threading: registration and delivery both happen on the GDK main-loop (UI) thread. AT-SPI dispatches on
		/// the main context that was current when the listener was registered, so <see cref="Start"/> and
		/// <see cref="Stop"/> post their work there; the UI-thread-only state below then needs no locking beyond the
		/// attach/detach bookkeeping.
		/// </para>
		/// <para>
		/// Coverage is whatever the toolkit exposes to accessibility: GTK/Qt/Electron text widgets report, apps that
		/// draw their own caret without an ATK/AT-SPI bridge do not, and nothing arrives at all when accessibility is
		/// switched off for the session (no <c>atk-bridge</c>). Every one of those degrades to silence, never an error.
		/// </para>
		/// </summary>
		internal sealed class CaretEventSource : IDisposable
		{
			private readonly Script owner;

			/// <summary>How long a resolved active window is reused for. Resolving it is a cheap property read on X11
			/// but a synchronous D-Bus round trip on Wayland, and caret events arrive per keystroke — while the caret
			/// itself follows focus, so a briefly stale answer can only affect the first event after an app switch.</summary>
			private const int ActiveWindowCacheMs = 200;

			private readonly Lock gate = new();
			private AtspiEventListenerCB callback;   // kept alive for as long as AT-SPI can call it
			private nint callbackPtr;
			private nint listener;                   // AtspiEventListener*
			private Action<WindowEventRaw> sink;
			private bool wanted;                     // a CaretMove subscription exists
			private bool attached;
			private bool disposed;

			internal CaretEventSource(Script owner)
				=> this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

			// UI-thread-only (the AT-SPI callback and attach/detach all run on the GDK main loop).
			private ActiveWindowSnapshot activeCached;
			private long activeCachedAt;

			internal void Start(Action<WindowEventRaw> eventSink)
			{
				lock (gate)
				{
					if (disposed)
						return;

					sink = eventSink;
					wanted = true;

					if (attached)
						return;
				}

				owner.PostToUIThread(AttachOnUI);
			}

			internal void Stop()
			{
				lock (gate)
					wanted = false;

				owner.PostToUIThread(DetachOnUI);
			}

			public void Dispose()
			{
				lock (gate)
				{
					disposed = true;
					wanted = false;
				}

				owner.PostToUIThread(DetachOnUI);
			}

			// ---- UI-thread attach/detach --------------------------------------------------------

			private void AttachOnUI()
			{
				lock (gate)
					// wanted == false means a Stop landed before this queued attach ran; the queued detach settles it.
					if (disposed || attached || !wanted)
						return;

				if (!EnsureInitialized())
				{
					Diagnostics.Debug.WriteLine("WinEvent.CaretMove: AT-SPI is unavailable, so caret movement cannot be observed.");
					return;
				}

				callback ??= OnCaretMoved;

				if (callbackPtr == 0)
					callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);

				var created = atspi_event_listener_new(callbackPtr, 0, 0);

				if (created == 0)
				{
					Diagnostics.Debug.WriteLine("WinEvent.CaretMove: creating the AT-SPI event listener failed.");
					return;
				}

				var error = (nint)0;
				var registered = atspi_event_listener_register(created, CaretMovedEvent, ref error) != 0;

				if (ConsumeError(ref error) || !registered)
				{
					Unref(created);
					Diagnostics.Debug.WriteLine($"WinEvent.CaretMove: registering for {CaretMovedEvent} failed.");
					return;
				}

				listener = created;

				lock (gate)
					attached = true;
			}

			private void DetachOnUI()
			{
				nint toRelease;

				lock (gate)
				{
					if (!attached)
						return;

					// A Start that re-enabled the category after the Stop that queued this detach wins (it saw
					// attached == true and skipped posting its own attach, relying on this check).
					if (!disposed && wanted)
						return;

					toRelease = listener;
					listener = 0;
					attached = false;
				}

				if (toRelease == 0)
					return;

				try
				{
					var error = (nint)0;
					_ = atspi_event_listener_deregister(toRelease, CaretMovedEvent, ref error);
					_ = ConsumeError(ref error);
					Unref(toRelease);
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"WinEvent.CaretMove: releasing the AT-SPI event listener failed: {ex.Message}");
				}

				activeCachedAt = 0;
			}

			// ---- AT-SPI callback (GDK main-loop thread) -----------------------------------------

			private void OnCaretMoved(nint atspiEvent, nint userData)
			{
				try
				{
					var currentSink = sink;

					if (currentSink == null || atspiEvent == 0)
						return;

					// AtspiEvent is { gchar *type; AtspiAccessible *source; gint detail1; gint detail2; GValue any_data;
					// AtspiAccessible *sender; }. Only source is needed, so it is read straight from its offset rather
					// than marshalling a struct whose tail (a GValue) would have to be described as well. The event and
					// everything in it belong to the AT-SPI marshaller, which frees them after this returns — so source
					// is used but never unreffed. The caret offset (detail1) is deliberately ignored in favour of
					// re-reading it inside TryGetCaretRect, which is what CaretGetPos does and also handles the
					// caret-past-the-last-character case.
					var source = Marshal.ReadIntPtr(atspiEvent, nint.Size);

					if (source == 0 || !TryGetCaretRect(source, out var caret) || !TryResolveWindow(source, ref caret, out var handle))
						return;

					currentSink(new WindowEventRaw(WindowEventType.CaretMove, handle, Environment.TickCount64)
					{
						Bounds = new Rectangle(caret.X, caret.Y, caret.Width, caret.Height)
					});
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"WinEvent.CaretMove: AT-SPI caret event failed: {ex.Message}");
				}
			}

			/// <summary>Attributes a caret event to a window handle and converts its rectangle to screen coordinates.
			/// The caret lives in whatever has keyboard focus, so the active window owns it; the process id confirms
			/// that when both sides know it, so a background application still posting caret events is never
			/// attributed to the window a subscription is actually watching.</summary>
			private bool TryResolveWindow(nint source, ref AtspiRect caret, out nint handle)
			{
				handle = 0;
				var active = GetActiveWindow();

				if (active.Handle == 0)
					return false;

				var pid = GetProcessId(source);

				if (pid > 0 && active.Pid > 0 && pid != active.Pid)
					return false;

				if (!TryNormalizeCoordinates(0, active, ref caret))
					return false;

				handle = active.Handle;
				return true;
			}

			/// <summary>Returns the short-lived active-window snapshot used to attribute this event.</summary>
			private ActiveWindowSnapshot GetActiveWindow()
			{
				var now = Environment.TickCount64;

				if (activeCachedAt != 0 && now - activeCachedAt < ActiveWindowCacheMs)
					return activeCached;

				activeCachedAt = now;
				activeCached = default;

				try
				{
					activeCached = CaptureActiveWindow();
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"WinEvent.CaretMove: resolving the active window failed: {ex.Message}");
				}

				return activeCached;
			}
		}
	}
}
#endif
