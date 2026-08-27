#if LINUX
using Keysharp.Builtins;
using System.Globalization;
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Cin = Keysharp.Internals.DBus.Generated.Cinnamon;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	// The Cinnamon, CinnamonShell1, IdleMonitor and DBus proxies are generated from
	// Internals/Linux/DBus/Interfaces/Cinnamon.xml and FreedesktopDBus.xml.

	/// <summary>
	/// Static bridge to Cinnamon's org.Cinnamon.Eval D-Bus method. Mirrors
	/// <see cref="GnomeShellBridge"/>'s lazy, thread-safe, timeout-guarded connection
	/// handling. Each query runs a small self-contained JS snippet whose return value is
	/// a JSON string; Cinnamon JSON-encodes that return value again, so results come back
	/// double-encoded and are unwrapped in <see cref="EvalJson"/>.
	/// </summary>
	internal static class CinnamonShellBridge
	{
		private const string ServiceName = "org.Cinnamon";
		private const string ObjectPath  = "/org/Cinnamon";
		private const string ExtensionServiceName = "io.github.keysharp.CinnamonShell";
		private const string ExtensionObjectPath  = "/io/github/keysharp/CinnamonShell";
		private const string IdleMonitorServiceName = "org.cinnamon.Muffin.IdleMonitor";
		private const string IdleMonitorObjectPath = "/org/cinnamon/Muffin/IdleMonitor/Core";
		private const int    TimeoutMs   = 2000;
		// The first image-overlay upload is cold and large (full-resolution PNG); give it a generous deadline so
		// it isn't classified TimedOut before the shell finishes decoding + uploading it. Mirrors GnomeShellBridge.
		private const int    ImageOverlayTimeoutMs = 10_000;
		private const int    ExtensionMissingCacheMs = 5000;
		private const int    ExtensionPresentCacheMs = 1000;
		// Shared JS helpers injected into every query: window-type filter, window->info
		// serializer (identical shape to the GNOME extension so the parser is shared in
		// spirit), and a stable_sequence lookup. Uses single-quoted JS string literals so
		// the surrounding C# string needs no escaping.
		private const string JsHelpers =
			"const Meta=imports.gi.Meta;" +
			"function tracked(w){switch(w.window_type){case Meta.WindowType.NORMAL:case Meta.WindowType.DIALOG:case Meta.WindowType.MODAL_DIALOG:case Meta.WindowType.UTILITY:return true;default:return false;}}" +
			"function clamp255(v){v=Number(v);if(!isFinite(v))v=255;if(v<0)v=0;if(v>255)v=255;return Math.round(v);}" +
			// -1 = the compositor has no explicit opacity for this window (no actor / read failed); it is preserved as
			// the cross-platform "no transparency set" sentinel (WinGetTransparent -> ""), NOT clamped to 0.
			"function opacity(w){try{const a=w.get_compositor_private?w.get_compositor_private():null;return a?(a.get_opacity?a.get_opacity():a.opacity):-1;}catch(e){return -1;}}" +
			// An unmanaged window stays in global.get_window_actors() for the length of the shell's close
			// animation, and Muffin's meta_window_wayland_get_client_pid() dereferences the already-freed
			// wl_resource without a NULL check: asking a dying window for its pid segfaults the whole shell.
			// Nothing about such a window is worth reporting either, so every walk below skips it.
			"function live(w){try{const a=w?w.get_compositor_private():null;if(!a)return false;return typeof a.is_destroyed!=='function'||!a.is_destroyed();}catch(e){return false;}}" +
			"function pid(w){try{return live(w)?(w.get_pid()>0?w.get_pid():(w.get_client_pid?w.get_client_pid():-1)):-1;}catch(e){return -1;}}" +
			// buffer = the surface as the client drew it, shadow included. It is the only origin a GTK client
			// can be located against: on Wayland it is never told where its surface is, so every coordinate it
			// reports is relative to that rectangle. Absent on a compositor too old to answer, which leaves the
			// consumer uncorrected rather than wrong.
			"function buffer(w){try{const b=w.get_buffer_rect();return b?{x:b.x,y:b.y,width:b.width,height:b.height}:null;}catch(e){return null;}}" +
			"function info(w){const f=w.get_frame_rect();return{id:String(w.get_stable_sequence()),buffer:buffer(w),title:w.get_title()||'',appId:w.get_wm_class()||w.get_wm_class_instance()||'',pid:pid(w),frame:{x:f.x,y:f.y,width:f.width,height:f.height},client:{x:f.x,y:f.y,width:f.width,height:f.height},active:!!w.appears_focused,minimized:!!w.minimized,maximized:!!(w.maximized_horizontally&&w.maximized_vertically),visible:!w.minimized,alwaysOnTop:(w.is_above?w.is_above():!!w.above),decorated:w.decorated!==false,transparency:(function(){const o=opacity(w);return o<0?-1:clamp255(o);})()};}" +
			"function find(s){const a=global.get_window_actors();for(let i=0;i<a.length;i++){const w=a[i].get_meta_window();if(w&&live(w)&&w.get_stable_sequence()===s)return w;}return null;}";

		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<Cin.Cinnamon> cinnamonService;
		private static WatchedDbusService<Cin.CinnamonShell1> extension;
		private static RetryGate highlightOwnerRegistration;
		private static Cin.IdleMonitor idleMonitorProxy;
		private static DbusSession idleMonitorSession;
		private static long clipboardSupportCacheUntil;
		private static bool clipboardSupportCached;
		private static RetryGate clipboardProbes;
		private static string connectionLocalName = "";
		private static string registeredHighlightOwnerBusName = "";
		private static readonly string HighlightOwnerKey = WaylandOverlayOwner.Key;

		static CinnamonShellBridge()
			=> Initialize();

		private static void Initialize()
		{
			sessions = new RecoverableService<DbusSession>(ConnectSessionBus,
				initialRetryDelay: TimeSpan.FromMilliseconds(500),
				maximumRetryDelay: TimeSpan.FromSeconds(5));
			cinnamonService = new WatchedDbusService<Cin.Cinnamon>(sessions, ServiceName, new ObjectPath(ObjectPath), TimeoutMs,
				(c, d, p) => new Cin.Cinnamon(c, d, p));
			extension = new WatchedDbusService<Cin.CinnamonShell1>(sessions, ExtensionServiceName,
				new ObjectPath(ExtensionObjectPath), TimeoutMs, (c, d, p) => new Cin.CinnamonShell1(c, d, p));
			highlightOwnerRegistration = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(500), maximumRetryDelay: TimeSpan.FromSeconds(5));
			clipboardProbes = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));
			extension.AvailabilityChanged += ExtensionAvailabilityChanged;
		}

		internal static string QueryActiveWindow()
		{
			var json = RunExtension(p => p.GetActiveWindowAsync());
			return JsonOk(json)
				? json
				: EvalJson("(function(){try{" + JsHelpers + "const w=global.display.get_focus_window();return JSON.stringify({ok:true,window:(w&&live(w)&&tracked(w))?info(w):null});}catch(e){return JSON.stringify({ok:false});}})()");
		}

		internal static bool QueryIdleTime(out long milliseconds)
		{
			milliseconds = 0;
			using var lease = sessions.TryAcquire();

			if (lease == null)
				return false;

			try
			{
				if (!ReferenceEquals(idleMonitorSession, lease.Value))
				{
					idleMonitorSession = lease.Value;
					idleMonitorProxy = new Cin.IdleMonitor(lease.Value.Connection,
						IdleMonitorServiceName, new ObjectPath(IdleMonitorObjectPath));
				}

				var task = idleMonitorProxy.GetIdletimeAsync();

				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon idle monitor", "GetIdletime", $"timed out after {TimeoutMs} ms");
					return false;
				}

				var value = task.GetAwaiter().GetResult();
				milliseconds = value > long.MaxValue ? long.MaxValue : (long)value;
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon idle monitor", "GetIdletime", WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		internal static string QueryWindowList(bool includeHidden)
		{
			var json = RunExtension(p => p.GetWindowListAsync(includeHidden));
			return JsonOk(json)
				? json
				: EvalJson("(function(){try{" + JsHelpers + "const a=global.get_window_actors();const out=[];for(let i=0;i<a.length;i++){const w=a[i].get_meta_window();if(!w||!live(w)||!tracked(w))continue;if(!" + (includeHidden ? "true" : "false") + "&&w.minimized)continue;out.push(info(w));}return JSON.stringify({ok:true,windows:out});}catch(e){return JSON.stringify({ok:false,windows:[]});}})()");
		}

		internal static bool QueryCursorPosition(out int x, out int y)
		{
			x = 0;
			y = 0;

			if (QueryExtensionCursorPosition(out x, out y))
				return true;

			var json = EvalJson("(function(){try{const p=global.get_pointer();return JSON.stringify({ok:true,x:Math.round(p[0]),y:Math.round(p[1])});}catch(e){return JSON.stringify({ok:false});}})()");

			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				if (!GetBool(root, "ok"))
					return false;

				x = GetInt(root, "x");
				y = GetInt(root, "y");
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static bool QueryWorkArea(out Rectangle area)
		{
			area = Rectangle.Empty;

			if (!TryRunExtension(p => p.GetWorkAreaAsync(), out (int X, int Y, int Width, int Height) result))
				return false;

			if (result.Width <= 0 || result.Height <= 0)
				return false;

			area = new Rectangle(result.X, result.Y, result.Width, result.Height);
			return true;
		}

		internal static bool SendFocusWindow(ulong seq)
			=> RunExtensionBool(p => p.FocusWindowAsync(seq))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w){if(w.minimized)w.unminimize();w.activate(global.get_current_time());}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendRaiseWindow(ulong seq)
			=> RunExtensionBool(p => p.RaiseWindowAsync(seq))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w){if(w.minimized)w.unminimize();w.activate(global.get_current_time());}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendLowerWindow(ulong seq)
			=> RunExtensionBool(p => p.LowerWindowAsync(seq))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");let ok=false;if(w){try{if(typeof w.lower_with_transients==='function'){w.lower_with_transients();ok=true;}else if(typeof w.lower==='function'){w.lower();ok=true;}}catch(e){ok=false;}}return JSON.stringify({ok:ok});}catch(e){return JSON.stringify({ok:false});}})()");

		// Ask the shell to place the NEXT window this process creates, before it is first painted. There is no
		// Eval fallback: this needs a live window-created hook in the extension, which an Eval snippet cannot
		// register safely. Fails closed against an extension that predates the method, leaving the caller on
		// the normal correlate-then-move path.
		internal static bool SendReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> RunExtensionBool(p => p.ReserveWindowAsync(Environment.ProcessId, cookie, x, y, ttlMs));

		// The compositor window a reservation landed on, or "" if it was never consumed.
		internal static string SendGetReservedWindow(ulong cookie)
			=> RunExtension(p => p.GetReservedWindowAsync(Environment.ProcessId, cookie)) ?? "";

		internal static bool SendMoveResize(ulong seq, int x, int y, int width, int height)
			=> RunExtensionBool(p => p.MoveResizeWindowAsync(seq, x, y, width, height))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w){if(w.maximized_horizontally||w.maximized_vertically)w.unmaximize(3);const f=w.get_frame_rect();w.move_resize_frame(true," + x + "===-2147483648?f.x:" + x + "," + y + "===-2147483648?f.y:" + y + "," + width + ">0?" + width + ":f.width," + height + ">0?" + height + ":f.height);}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		// Move/resize by X11 window id (X11 sessions). Tries the extension method, then an org.Cinnamon.Eval
		// fallback that finds the window by get_xwindow() — so this works even against an installed extension
		// that predates MoveResizeWindowByXid. user_op = true is what lets it reach off-screen (see the
		// extension comment). Position sentinel int.MinValue = unchanged; size <= 0 = unchanged.
		internal static bool SendMoveResizeByXid(ulong xid, int x, int y, int width, int height)
			=> RunExtensionBool(p => p.MoveResizeWindowByXidAsync(xid, x, y, width, height))
			   || RunOk("(function(){try{" + JsHelpers + "let w=null;for(const a of global.get_window_actors()){const m=a.get_meta_window();if(m&&live(m)&&typeof m.get_xwindow==='function'&&Number(m.get_xwindow())===" + xid + "){w=m;break;}}if(w){if(w.maximized_horizontally||w.maximized_vertically)w.unmaximize(3);const f=w.get_frame_rect();w.move_resize_frame(true," + x + "===-2147483648?f.x:" + x + "," + y + "===-2147483648?f.y:" + y + "," + width + ">0?" + width + ":f.width," + height + ">0?" + height + ":f.height);}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendSetWindowState(ulong seq, int state)
			=> RunExtensionBool(p => p.SetWindowStateAsync(seq, state))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w){if(" + state + "===1){w.minimize();}else if(" + state + "===2){if(w.minimized)w.unminimize();w.maximize(Meta.MaximizeFlags.BOTH);}else{if(w.minimized)w.unminimize();w.unmaximize(Meta.MaximizeFlags.BOTH);}}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendCloseWindow(ulong seq)
			=> RunExtensionBool(p => p.CloseWindowAsync(seq))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w)w.delete(global.get_current_time());return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendKillWindow(ulong seq)
			=> RunExtensionBool(p => p.KillWindowAsync(seq))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");let ok=false;if(w){try{if(typeof w.kill==='function'){w.kill();ok=true;}else if(typeof w.delete==='function'){w.delete(global.get_current_time());ok=true;}}catch(e){ok=false;}}return JSON.stringify({ok:ok});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendSetAlwaysOnTop(ulong seq, bool above)
			=> RunExtensionBool(p => p.SetWindowAboveAsync(seq, above))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w){if(" + (above ? "true" : "false") + "){if(!w.is_above())w.make_above();}else{if(w.is_above())w.unmake_above();}}return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendSetNoBorder(ulong seq, bool noBorder)
			=> RunExtensionBool(p => p.SetWindowDecoratedAsync(seq, !noBorder))
			   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");if(w)w.decorated=" + (noBorder ? "false" : "true") + ";return JSON.stringify({ok:!!w});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendSetOpacity(ulong seq, object value)
		{
			var alpha = value is string s && s.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255
				: Math.Clamp((int)value.Al(), 0, 255);

			return RunExtensionBool(p => p.SetWindowOpacityAsync(seq, alpha))
				   || RunOk("(function(){try{" + JsHelpers + "const w=find(" + seq + ");const a=w&&w.get_compositor_private?w.get_compositor_private():null;if(a){if(a.set_opacity)a.set_opacity(" + alpha.ToString(CultureInfo.InvariantCulture) + ");else a.opacity=" + alpha.ToString(CultureInfo.InvariantCulture) + ";}return JSON.stringify({ok:!!a});}catch(e){return JSON.stringify({ok:false});}})()");
		}

		// Whether the extension actually answers clipboard calls. The overlay path can gate on cheap name
		// ownership because it reacts to a per-call tri-state (a definitive failure falls back to Eto), but the
		// the recovering clipboard router uses this as its current liveness signal, so a stale/incompatible extension
		// that owns the D-Bus name yet no longer speaks the clipboard protocol must not be treated as usable. Verify
		// with a cheap, side-effect-free
		// GetClipboardMimetypes round-trip (null = absent/failed, any array = answered) and cache the result.
		internal static bool SupportsClipboard()
		{
			var now = Environment.TickCount64;

			if (!ExtensionServiceHasOwner())
				return false;

			if (now < clipboardSupportCacheUntil)
				return clipboardSupportCached;

			using var attempt = clipboardProbes.TryBegin();

			if (attempt == null)
				return false;

			var ok = RunExtension(p => p.GetClipboardMimetypesAsync()) != null;
			clipboardSupportCached = ok;
			clipboardSupportCacheUntil = now + (ok ? ExtensionPresentCacheMs : ExtensionMissingCacheMs);

			if (ok)
				attempt.Succeed();

			return ok;
		}

			internal static OverlayShowResult SendShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
				=> pngBytes is { Length: > 0 }
				   ? RunShow(p => p.ShowImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height, pngBytes),
							 ImageOverlayTimeoutMs)
				   : OverlayShowResult.Failed;

			internal static OverlayShowResult SendShowImageOverlayShm(uint id, int x, int y, int width, int height,
				string shmPath, int pixelWidth, int pixelHeight, int stride)
				=> !shmPath.IsNullOrEmpty()
				   ? RunShow(p => p.ShowImageOverlayShmAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height,
															 shmPath, pixelWidth, pixelHeight, stride),
							 ImageOverlayTimeoutMs)
				   : OverlayShowResult.Failed;

			internal static bool SendMoveImageOverlay(uint id, int x, int y, int width, int height)
				=> RunExtensionBool(p => p.MoveImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height));

			internal static bool SendHideImageOverlay(uint id)
				=> RunExtensionBool(p => p.HideImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName));

			// Clipboard access runs only through the bundled extension (Muffin exposes no data-control
			// protocol, so a background app otherwise can't read/write/monitor the clipboard). Content is raw
			// MIME-type <-> bytes so every format round-trips. Getters return null when the extension is
			// absent/failed (vs an empty array/"" for a legitimately empty clipboard).
			internal static string[] GetClipboardMimetypes()
				=> RunExtension(p => p.GetClipboardMimetypesAsync());

			internal static byte[] GetClipboardContent(string mimetype)
				=> RunExtension(p => p.GetClipboardContentAsync(mimetype));

			internal static bool SetClipboardContent(string mimetype, byte[] bytes)
				=> RunExtensionBool(p => p.SetClipboardContentAsync(mimetype, bytes ?? System.Array.Empty<byte>()));

			internal static string GetClipboardText()
				=> RunExtension(p => p.GetClipboardTextAsync());

			internal static bool SetClipboardText(string text)
				=> RunExtensionBool(p => p.SetClipboardTextAsync(text ?? string.Empty));

			internal static IDisposable WatchClipboardChanged(Action<string, string[]> handler, Action<Exception> onError = null)
				=> TryRunExtension(p => p.WatchClipboardChangedAsync(
						DBusSignals.Adapt<(string, string[])>(e => handler(e.Item1, e.Item2), onError),
						DBusSignals.FlagsFor(onError), emitOnCapturedContext: false).AsTask(), out IDisposable subscription)
					? subscription : null;

		// Lazily creates a Clutter virtual pointer (Muffin is Clutter-based, same API as the
		// GNOME extension) and stashes it on `global` so it persists across Eval calls.
		private const string JsVPointer =
			"const Clutter=imports.gi.Clutter;const GLib=imports.gi.GLib;" +
			"if(!global._ksVPointer){global._ksVPointer=Clutter.get_default_backend().get_default_seat().create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);}" +
			"const vp=global._ksVPointer;";

		internal static bool SendMouseMoveAbsolute(int x, int y)
			=> RunExtensionBool(p => p.SendMouseMoveAbsoluteAsync(x, y))
			   || RunOk("(function(){try{" + JsVPointer + "vp.notify_absolute_motion(GLib.get_monotonic_time()," + x + "," + y + ");return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendMouseMoveRelative(int dx, int dy)
			=> RunExtensionBool(p => p.SendMouseMoveRelativeAsync(dx, dy))
			   || RunOk("(function(){try{" + JsVPointer + "const p=global.get_pointer();vp.notify_absolute_motion(GLib.get_monotonic_time(),p[0]+(" + dx + "),p[1]+(" + dy + "));return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendMouseButton(uint button, bool pressed)
			=> RunExtensionBool(p => p.SendMouseButtonAsync(button, pressed))
			   || RunOk("(function(){try{" + JsVPointer + "vp.notify_button(GLib.get_monotonic_time()," + button + ",Clutter.ButtonState." + (pressed ? "PRESSED" : "RELEASED") + ");return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false});}})()");

		internal static bool SendMouseScroll(int delta, bool vertical)
		{
			var dir = vertical ? (delta > 0 ? "UP" : "DOWN") : (delta > 0 ? "RIGHT" : "LEFT");
			var notches = Math.Max(1, Math.Abs((int)Math.Round(delta / 120.0)));
			return RunExtensionBool(p => p.SendMouseScrollAsync(delta, vertical))
				   || RunOk("(function(){try{" + JsVPointer + "const t=GLib.get_monotonic_time();for(let i=0;i<" + notches + ";i++)vp.notify_discrete_scroll(t,Clutter.ScrollDirection." + dir + ",Clutter.ScrollSource.WHEEL);return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false});}})()");
		}

		internal static IDisposable WatchWindowEvent(Action<string, string> handler, Action<Exception> onError = null)
			=> TryRunExtension(p => p.WatchWindowEventAsync(
					DBusSignals.Adapt<(string, string)>(e => handler(e.Item1, e.Item2), onError),
					DBusSignals.FlagsFor(onError), emitOnCapturedContext: false).AsTask(),
				out IDisposable subscription) ? subscription : null;

		private static bool RunOk(string js)
		{
			var json = EvalJson(js);

			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				return GetBool(doc.RootElement, "ok");
			}
			catch
			{
				return false;
			}
		}

		// Runs JS via Eval and returns the inner JSON string. Cinnamon returns
		// [success, JSON.stringify(returnValue)]; since our snippets already return a JSON
		// string, the D-Bus payload is that string encoded a second time — decode one layer.
		private static string EvalJson(string js)
		{
			var raw = Eval(js);

			if (raw == null)
				return null;

			try
			{
				return JsonSerializer.Deserialize<string>(raw);
			}
			catch
			{
				// Not double-encoded (older Cinnamon); use as-is.
				return raw;
			}
		}

		private static string Eval(string js)
		{
			try
			{
				if (!cinnamonService.TryUse(p => p.EvalAsync(js), out Task<(bool Item1, string Item2)> task))
					return null;

				if (!task.WaitWithoutInterruption(TimeoutMs))
					return null;

				var (ok, result) = task.GetAwaiter().GetResult();
				return ok ? result : null;
			}
			catch
			{
				return null;
			}
		}

		private static bool QueryExtensionCursorPosition(out int x, out int y)
		{
			x = 0;
			y = 0;

			if (!TryRunExtension(p => p.GetCursorPositionAsync(), out (int X, int Y) result))
				return false;

			x = result.X;
			y = result.Y;
			return true;
		}

		private static T RunExtension<T>(Func<Cin.CinnamonShell1, Task<T>> call,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
			=> TryRunExtension(call, out T result, TimeoutMs, operation) ? result : default;

		private static bool TryRunExtension<T>(Func<Cin.CinnamonShell1, Task<T>> call, out T result,
			int timeoutMs = TimeoutMs,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
		{
			result = default;

			try
			{
				if (!extension.TryUse((p, session) =>
				{
					connectionLocalName = session.LocalName;
					RegisterHighlightOwner(p);
					return call(p);
				}, out Task<T> task))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, "extension service is unavailable");
					return false;
				}

				// Pump the message queue while waiting on the extension's reply instead of freezing it — these
				// calls run from hotkey actions / timers on the main thread, and a slow (cold-channel) reply must
				// not stall hotkey processing and the UI.
				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, $"timed out after {timeoutMs} ms");
					return false;
				}

				result = task.GetAwaiter().GetResult();
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		private static bool RunExtensionBool(Func<Cin.CinnamonShell1, Task<bool>> call,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
			=> TryRunExtension(call, out bool result, TimeoutMs, operation) && result;

		// Show an image overlay, classifying the outcome (see OverlayShowResult) so the caller can distinguish an
		// ambiguous timeout — the shell most likely still created the actor, so commit to the compositor — from a
		// definitive failure that may safely fall back to Eto. A plain RunExtensionBool collapses both to false,
		// which is what let a slow first upload spawn a duplicate Eto overlay.
		private static OverlayShowResult RunShow(Func<Cin.CinnamonShell1, Task<bool>> call, int timeoutMs)
		{
			try
			{
				if (!extension.TryUse((p, session) =>
				{
					connectionLocalName = session.LocalName;
					RegisterHighlightOwner(p);
					return call(p);
				}, out Task<bool> task))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", "extension service is unavailable");
					return OverlayShowResult.Failed;
				}

				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay",
						$"timed out after {timeoutMs} ms; the compositor result is ambiguous");
					return OverlayShowResult.TimedOut;
				}

				if (task.GetAwaiter().GetResult())
					return OverlayShowResult.Shown;

				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", "extension returned false");
				return OverlayShowResult.Failed;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", WaylandBridgeDiagnostics.Describe(ex));
				return OverlayShowResult.Failed;
			}
		}


		private static bool JsonOk(string json)
		{
			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				return doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
			}
			catch
			{
				return false;
			}
		}

		private static void RegisterHighlightOwner(Cin.CinnamonShell1 p)
		{
			if (p == null || connectionLocalName.IsNullOrEmpty() || registeredHighlightOwnerBusName == connectionLocalName)
				return;

			using var attempt = highlightOwnerRegistration.TryBegin();

			if (attempt == null)
				return;

			try
			{
				var task = Task.Run(() => p.RegisterHighlightOwnerAsync(HighlightOwnerKey, connectionLocalName));

				// Pump the message loop while waiting (never a plain .Wait) — this runs from hotkey/timer
				// actions and must not stall the pump on a cold D-Bus channel.
				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", $"timed out after {TimeoutMs} ms");
					return;
				}

				if (task.GetAwaiter().GetResult())
				{
					registeredHighlightOwnerBusName = connectionLocalName;
					attempt.Succeed();
				}
				else
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", "extension returned false");
				}
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", WaylandBridgeDiagnostics.Describe(ex));
				attempt.Fail(ex);
			}
		}

		internal static bool ExtensionServiceHasOwner() => extension.HasOwner;

		internal static IDisposable SubscribeExtensionAvailability(Action handler)
		{
			if (handler == null)
				return null;

			extension.AvailabilityChanged += handler;
			return new CallbackDisposable(() => extension.AvailabilityChanged -= handler);
		}

		private static DbusSession ConnectSessionBus()
			=> DbusSession.Connect(DBusBus.Session, TimeoutMs, "Cinnamon Shell",
								   (session, reason) => sessions.Invalidate(session, reason));

		private static void ExtensionAvailabilityChanged()
		{
			registeredHighlightOwnerBusName = "";
			highlightOwnerRegistration.Rearm();
			clipboardSupportCached = false;
			clipboardSupportCacheUntil = 0;
			clipboardProbes.Rearm();
		}

		internal static void Reset()
		{
			extension.Dispose();
			cinnamonService.Dispose();
			sessions.Dispose();
			idleMonitorProxy = null;
			idleMonitorSession = null;
			connectionLocalName = registeredHighlightOwnerBusName = "";
			Initialize();
		}

		private static bool GetBool(JsonElement e, string name)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

		private static int GetInt(JsonElement e, string name)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
	}

	/// <summary>
	/// Wayland window-management backend for the Cinnamon desktop (Muffin compositor),
	/// driven through <see cref="CinnamonShellBridge"/>. Provides the introspection that
	/// Wayland denies foreign clients (active window, window list/geometry, cursor) so
	/// WinActive/WinExist/WinGetTitle("A")/WinGetPos work on Cinnamon-Wayland sessions.
	/// </summary>
	internal sealed class CinnamonBackend : IWaylandBackend
	{
		// Bit 60 marks a handle as Cinnamon's; keeps it above the 32-bit X11 XID range and
		// distinct from GNOME (bit 61) and KWin (bit 62).
		private const long CinnamonBit = unchecked((long)0x1000_0000_0000_0000L);

		public string Name => "Cinnamon";

		// Inject pointer events through Muffin's Clutter virtual device (same approach as the
		// GNOME/KWin backends). The inputd sender prefers this for absolute moves/clicks/scroll
		// because uinput's normalized coordinates don't map reliably on Wayland; relative moves
		// still use inputd. Falls back to inputd automatically if any Eval call fails.
		public bool SupportsMouse => true;

		internal static bool IsAvailable()
			=> EnvContains("XDG_CURRENT_DESKTOP", "cinnamon")
			   || EnvContains("DESKTOP_SESSION", "cinnamon")
			   || EnvContains("XDG_SESSION_DESKTOP", "cinnamon");

		public bool SupportsWindowEvents => true;

		public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
		{
			if (sink == null)
				return null;

			void OnEvent(string type, string json)
			{
				var kind = MapEventKind(type);

				if (kind == null || json.IsNullOrEmpty())
					return;

				try
				{
					using var doc = JsonDocument.Parse(json);

					if (TryParseEventWindow(doc.RootElement, out var info) && info.Handle != 0)
					{
						var bounds = info.FrameGeometry.Width > 0 && info.FrameGeometry.Height > 0 ? info.FrameGeometry : (Rectangle?)null;
						sink(new WaylandWindowEvent(kind.Value, info.Handle) { Bounds = bounds });
					}
				}
				catch
				{
				}
			}

			return RecoveringSubscription.Create(
				onError => CinnamonShellBridge.WatchWindowEvent(OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				CinnamonShellBridge.ExtensionServiceHasOwner,
				CinnamonShellBridge.SubscribeExtensionAvailability);
		}

		public bool TryGetCursorPos(out int x, out int y)
			=> CinnamonShellBridge.QueryCursorPosition(out x, out y);

		public bool TryGetIdleTime(out long milliseconds)
			=> CinnamonShellBridge.QueryIdleTime(out milliseconds);

		public bool TryGetWorkArea(out Rectangle area)
			=> CinnamonShellBridge.QueryWorkArea(out area);

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];
			var json = CinnamonShellBridge.QueryWindowList(includeHidden);

			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				if (!JsonBool(root, "ok") || !root.TryGetProperty("windows", out var arr) || arr.ValueKind != JsonValueKind.Array)
					return false;

				var parsed = new List<WaylandWindowInfo>();

				foreach (var item in arr.EnumerateArray())
					if (TryParseWindow(item, out var info))
						parsed.Add(info);

				windows = parsed;
				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool TryGetActiveWindow(out WaylandWindowInfo window)
			=> TryParseSingleWindow(CinnamonShellBridge.QueryActiveWindow(), out window);

		public bool IsKnown(nint handle) => TryHandleToSeq(handle, out _);

		/// <summary>The raw stable_sequence for a Cinnamon-tagged handle (the id the extension keys on).</summary>
		internal bool TryGetWindowSeq(nint handle, out ulong seq) => TryHandleToSeq(handle, out seq);

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			window = null;

			if (!TryHandleToSeq(handle, out _) || !TryListWindows(true, out var all))
				return false;

			window = all.FirstOrDefault(w => w.Handle == handle);
			return window != null;
		}

		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
		{
			window = null;

			if (!TryListWindows(false, out var all))
				return false;

			// List is bottom-to-top; walk top-down so the topmost window at the point wins. Skip windows
			// parked on other workspaces — the actor list spans all of them.
			for (var i = all.Count - 1; i >= 0; i--)
			{
				if (all[i].OnCurrentWorkspace && all[i].FrameGeometry.Contains(x, y))
				{
					window = all[i];
					return true;
				}
			}

			return false;
		}

		public bool TryActivateWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendFocusWindow(seq);

		public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> CinnamonShellBridge.SendReserveWindow(cookie, x, y, ttlMs);

		public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			compositorId = CinnamonShellBridge.SendGetReservedWindow(cookie);
			handle = compositorId.Length > 0 && ulong.TryParse(compositorId, out var seq) ? SeqToHandle(seq) : 0;
			return handle != 0;
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryHandleToSeq(handle, out var seq)
			   && CinnamonShellBridge.SendMoveResize(
				   seq,
				   setPosition ? bounds.X : int.MinValue,
				   setPosition ? bounds.Y : int.MinValue,
				   setSize && bounds.Width > 0 ? bounds.Width : 0,
				   setSize && bounds.Height > 0 ? bounds.Height : 0);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryHandleToSeq(handle, out var seq)
			   && CinnamonShellBridge.SendSetWindowState(seq, WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetAlwaysOnTop(seq, onTop);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetNoBorder(seq, noBorder);

		public bool TrySetTransparency(nint handle, object alpha)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetOpacity(seq, alpha);

		public bool SupportsTransparency => true;

		public bool TrySetZOrder(nint handle, ZOrder z)
			=> TryHandleToSeq(handle, out var seq)
			   && (z == ZOrder.Top
				   ? CinnamonShellBridge.SendRaiseWindow(seq)
				   : z == ZOrder.Bottom && CinnamonShellBridge.SendLowerWindow(seq));

		public bool TryCloseWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendCloseWindow(seq);

		public bool TryKillWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendKillWindow(seq);

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> CinnamonShellBridge.SendMouseMoveAbsolute(x, y);

		public bool TrySendMouseMoveRelative(int dx, int dy)
			=> CinnamonShellBridge.SendMouseMoveRelative(dx, dy);

		public bool TrySendMouseButton(uint button, bool pressed)
			=> CinnamonShellBridge.SendMouseButton(button, pressed);

		public bool TrySendMouseScroll(int delta, bool vertical)
			=> CinnamonShellBridge.SendMouseScroll(delta, vertical);

			// The Keysharp extension owns the compositor-drawn overlay + clipboard surface, so its D-Bus
			// service ownership is the single capability gate for both. A stale/broken extension that owns
			// the name but errors on the actual overlay call is handled reactively by TryShowImageOverlay's
			// tri-state result (a definitive Failed falls back to Eto), not by a separate up-front probe.
			public bool SupportsImageOverlay => CinnamonShellBridge.ExtensionServiceHasOwner();

			// Cinnamon was selected from the desktop/session itself, so attempt the authoritative Show RPC even if
			// the cached service-owner hint momentarily misses during startup.
			public bool CanAttemptImageOverlay => true;

			public OverlayShowResult TryShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
				=> CinnamonShellBridge.SendShowImageOverlay(id, x, y, width, height, pngBytes);

			public OverlayShowResult TryShowImageOverlayShm(uint id, int x, int y, int width, int height,
				string shmPath, int pixelWidth, int pixelHeight, int stride)
				=> CinnamonShellBridge.SendShowImageOverlayShm(id, x, y, width, height, shmPath, pixelWidth, pixelHeight, stride);

			public bool TryMoveImageOverlay(uint id, int x, int y, int width, int height)
				=> CinnamonShellBridge.SendMoveImageOverlay(id, x, y, width, height);

			public bool TryHideImageOverlay(uint id)
				=> CinnamonShellBridge.SendHideImageOverlay(id);

			// Clipboard runs only through the extension (Muffin exposes no data-control protocol). Because the
			// the recovering clipboard router can promote/demote at runtime, this remains a real liveness probe (not
			// mere name ownership). Raw MIME <-> bytes; higher layers map formats onto it.
			public bool SupportsClipboard => CinnamonShellBridge.SupportsClipboard();

			public string[] GetClipboardMimetypes()
				=> CinnamonShellBridge.GetClipboardMimetypes();

			public byte[] GetClipboardContent(string mimetype)
				=> CinnamonShellBridge.GetClipboardContent(mimetype);

			public bool SetClipboardContent(string mimetype, byte[] bytes)
				=> CinnamonShellBridge.SetClipboardContent(mimetype, bytes);

			public string GetClipboardText()
				=> CinnamonShellBridge.GetClipboardText();

			public bool SetClipboardText(string text)
				=> CinnamonShellBridge.SetClipboardText(text);

			public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler, Action<Exception> onError = null)
				=> handler == null ? null : CinnamonShellBridge.WatchClipboardChanged(handler, onError);

			public IDisposable SubscribeClipboardAvailability(Action handler)
				=> CinnamonShellBridge.SubscribeExtensionAvailability(handler);

		// ---- helpers ------------------------------------------------

		private static bool EnvContains(string variable, string token)
		{
			var value = Environment.GetEnvironmentVariable(variable);
			return !string.IsNullOrEmpty(value) && value.Contains(token, StringComparison.OrdinalIgnoreCase);
		}

		private static bool TryParseWindow(JsonElement item, out WaylandWindowInfo info)
		{
			info = null;

			if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty()
				|| !ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
				return false;

			info = new WaylandWindowInfo(
				handle: SeqToHandle(seq),
				compositorId: id,
				title: JsonString(item, "title"),
				appId: JsonString(item, "appId"),
				pid: JsonLong(item, "pid"),
				frameGeometry: JsonRectangle(item, "frame"),
				clientGeometry: JsonRectangle(item, "client"),
				surfaceGeometry: JsonRectangle(item, "buffer"),
				active: JsonBool(item, "active"),
				minimized: JsonBool(item, "minimized"),
				maximized: JsonBool(item, "maximized"),
				visible: JsonBool(item, "visible"),
				alwaysOnTop: JsonBool(item, "alwaysOnTop"),
				decorated: !item.TryGetProperty("decorated", out _) || JsonBool(item, "decorated"),
				transparency: item.TryGetProperty("transparency", out _) ? JsonLong(item, "transparency") : -1L,
				onCurrentWorkspace: !item.TryGetProperty("onCurrentWorkspace", out _) || JsonBool(item, "onCurrentWorkspace"));
			return true;
		}

		private static bool TryParseEventWindow(JsonElement item, out WaylandWindowInfo info)
		{
			if (TryParseWindow(item, out info))
				return true;

			info = null;

			if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty()
				|| !ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
				return false;

			info = new WaylandWindowInfo(handle: SeqToHandle(seq), compositorId: id);
			return true;
		}

		private static bool TryParseSingleWindow(string json, out WaylandWindowInfo window)
		{
			window = null;

			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				if (!JsonBool(root, "ok")
					|| !root.TryGetProperty("window", out var item)
					|| item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
					return false;

				return TryParseWindow(item, out window);
			}
			catch
			{
				return false;
			}
		}

		private static nint SeqToHandle(ulong seq)
			=> new((long)((seq & 0xFFFF_FFFF) | (ulong)CinnamonBit));

		private static bool TryHandleToSeq(nint handle, out ulong seq)
		{
			var h = handle.ToInt64();

			if ((h & unchecked((long)0x7000_0000_0000_0000L)) == CinnamonBit)
			{
				seq = (ulong)(h & 0xFFFF_FFFF);
				return true;
			}

			seq = 0;
			return false;
		}

		private static WaylandWindowEventKind? MapEventKind(string type) => type switch
		{
			"create"   => WaylandWindowEventKind.Created,
			"close"    => WaylandWindowEventKind.Closed,
			"active"   => WaylandWindowEventKind.Activated,
			"title"    => WaylandWindowEventKind.TitleChanged,
			"minimize" => WaylandWindowEventKind.Minimized,
			"restore"  => WaylandWindowEventKind.Restored,
			"move"     => WaylandWindowEventKind.MoveResized,
			_          => null
		};

		private static bool JsonBool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

		private static long JsonLong(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

		private static string JsonString(JsonElement element, string property)
			=> JsonString(element, property, out var value) ? value : string.Empty;

		private static bool JsonString(JsonElement element, string property, out string value)
		{
			if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
			{
				value = prop.GetString() ?? string.Empty;
				return true;
			}

			value = string.Empty;
			return false;
		}

		private static Rectangle JsonRectangle(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var rect) || rect.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;

			return new Rectangle(
				RectInt(rect, "x"),
				RectInt(rect, "y"),
				RectInt(rect, "width"),
				RectInt(rect, "height"));
		}

		private static int RectInt(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
	}
}
#endif
