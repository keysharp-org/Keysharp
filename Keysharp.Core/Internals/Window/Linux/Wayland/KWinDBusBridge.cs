#if LINUX
using System.Threading.Channels;

using Tmds.DBus;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	// These interfaces must be public because Tmds.DBus generates proxy classes in a
	// separate dynamic assembly (Tmds.DBus.Emit) and that assembly cannot implement
	// internal interfaces.
#pragma warning disable IDE1006 // D-Bus member names are case-sensitive.
	[DBusInterface("org.kde.kwin.Scripting")]
	public interface IKWinScripting : IDBusObject
	{
		Task<int> loadScriptAsync(string filePath, string pluginName);
		Task<bool> unloadScriptAsync(string pluginName);
	}

	[DBusInterface("org.kde.kwin.Script")]
	public interface IKWinScript : IDBusObject
	{
		Task runAsync();
	}

	[DBusInterface("org.keysharp.KWinBridge")]
	public interface IKWinBridge : IDBusObject
	{
		Task ReportJsonAsync(string requestId, string json);

		/// <summary>Called repeatedly by a persistent event script: <paramref name="token"/> identifies the
		/// subscription, <paramref name="eventType"/> is "create"/"close"/"active"/"title"/"minimize"/"restore"/
		/// "move", and <paramref name="json"/> is the affected window's info (at least an "id").</summary>
		Task ReportEventAsync(string token, string eventType, string json);
	}

	/// <summary>
	/// Operation channel served to the persistent KWin script. The script keeps one GetQueries call
	/// outstanding (a long poll); Keysharp completes it with queued {id, op, args} envelopes the moment work
	/// arrives, or with an empty batch before the D-Bus call timeout so the script's poll loop never dies.
	/// Results come back through ReportJson keyed by the envelope id.
	/// </summary>
	[DBusInterface("org.keysharp.KWinQueryBridge")]
	public interface IKWinQueryBridge : IDBusObject
	{
		Task<string[]> GetQueriesAsync();
		Task ReportJsonAsync(string requestId, string json);
	}

	/// <summary>
	/// KWin's compositor-native input injection interface. Requires a one-time
	/// authentication call that KWin grants silently for local D-Bus clients.
	/// Object path: /org/kde/KWin/FakeInput
	/// </summary>
	[DBusInterface("org.kde.kwin.FakeInput")]
	public interface IKWinFakeInput : IDBusObject
	{
		/// <summary>Returns true if KWin grants the input-injection permission.</summary>
		Task<bool> authenticateAsync(string applicationName, string reason);
		Task pointerMotionAsync(double dx, double dy);
		Task pointerMotionAbsoluteAsync(double x, double y);
		/// <summary>button is a Linux evdev BTN_* code; state 0 = released, 1 = pressed.</summary>
		Task pointerButtonAsync(uint button, uint state);
		/// <summary>axis 0 = vertical, 1 = horizontal; delta positive = scroll down/right.</summary>
		Task pointerAxisAsync(uint axis, double delta);
	}

#pragma warning restore IDE1006


	/// <summary>
	/// D-Bus bridge to KWin's scripting subsystem. KWin exposes compositor internals to
	/// scripts, so Keysharp keeps a resident script loaded and sends it typed operation requests
	/// through a process-local D-Bus service. The session-bus connection and bridge service are
	/// opened once and reused for every request.
	/// </summary>
	internal static class KWinDBusBridge
	{
		private const int QueryTimeoutMs = 2000;
		private const int ProbeTimeoutMs = 2000;
		private static readonly object initLock = new();
		private static readonly SemaphoreSlim initSemaphore = new(1, 1);
		private static readonly RetryGate initRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static readonly string bridgeServiceName = $"org.keysharp.KWinBridge_{Environment.ProcessId}";
		internal static readonly ObjectPath BridgeObjectPath = new("/keysharp/kwin");

		private static Connection connection;
		private static KWinBridgeService bridgeService;
		private static IKWinScripting scriptingProxy;
		private static IDisposable kwinOwnerWatch;
		private static event Action availabilityChanged;

		// Persistent operation channel: one resident KWin script answers typed compositor operations instead of
		// the load/run/report/unload lifecycle (which costs 4 D-Bus round trips + a QJS compile + temp-file I/O
		// per query). The script long-polls GetQueries on a DEDICATED connection/service so its parked reply
		// can never delay ReportEvent delivery or outgoing proxy calls, whatever Tmds.DBus's dispatch policy.
		private static readonly string queryServiceName = $"org.keysharp.KWinQuery_{Environment.ProcessId}";
		private static readonly string persistentPluginName = $"keysharp_query_{Environment.ProcessId}";
		internal static readonly ObjectPath QueryObjectPath = new("/keysharp/kwinquery");
		private static readonly TimeSpan persistentRetryBackoff = TimeSpan.FromSeconds(5);
		private const int MaxPersistentFailures = 3;   // consecutive failed (re)loads latch the channel off
		private static readonly SemaphoreSlim persistentLock = new(1, 1);
		private static Connection queryConnection;
		private static KWinQueryService queryService;
		private static volatile bool persistentReady;
		private static volatile int persistentFailures;
		private static DateTime lastPersistentAttempt = DateTime.MinValue;
		private static bool persistentCleanupRegistered;
		private static string kwinOwner;
		private static long kwinOwnerChanges;
		private static long requestCounter;

		// KWin 6 uses "/Scripting/Script{id}" as the per-script object path; KWin 5
		// uses just "/{id}". We don't know which we're on until the first run fails;
		// cache the format once the right one is identified so we don't pay the
		// failed-call cost on every query.
		private static string scriptPathFormat;

		internal static bool QueryCursorPosition(out int x, out int y)
		{
			x = 0;
			y = 0;

			try
			{
				var json = RunJsonOperation("cursorPos", null, "cursor");

				if (json.IsNullOrEmpty())
					return false;

				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				if (!JsonBool(root, "ok") || !JsonInt(root, "x", out x) || !JsonInt(root, "y", out y))
					return false;

				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static string RunJsonOperation(string operation, object args = null, string requestPrefix = null)
		{
			try
			{
				return RunJsonOperationAsync(operation, args, requestPrefix).GetAwaiter().GetResult();
			}
			catch
			{
				return null;
			}
		}

		internal static bool RunCommandOperation(string operation, object args = null, string requestPrefix = null)
		{
			try
			{
				return JsonOk(RunJsonOperationAsync(operation, args, requestPrefix ?? operation).GetAwaiter().GetResult());
			}
			catch
			{
				return false;
			}
		}

		// Move/resize a KWin-managed window identified by its X11 window id, for X11 sessions where the
		// caller (X11Window) has an XID rather than a KWin uuid. Position axes use int.MinValue for
		// "unchanged"; size axes use <= 0. Returns false if KWin isn't reachable or the id isn't found,
		// so the caller falls back to XMoveWindow.
		internal static bool SendMoveResizeByXid(ulong xid, int x, int y, int width, int height)
			=> RunCommandOperation("moveResizeByXid", new { xid, x, y, width, height }, "move_resize_by_xid");

		private static async Task<string> RunJsonOperationAsync(string operation, object args = null, string requestPrefix = null)
		{
			if (!await EnsureConnectedAsync().ConfigureAwait(false))
				return null;

			if (await EnsurePersistentChannelAsync().ConfigureAwait(false))
			{
				var (json, allowFallback) = await SubmitPersistentOperationAsync(operation, args, QueryTimeoutMs).ConfigureAwait(false);

				if (json != null)
					return json;

				// The envelope was already handed to the script: the operation may still execute once a compositor
				// stall clears (exactly like a late-landing one-shot command), so a retry could run it TWICE.
				// Report the timeout as a failure instead, matching the old path's semantics.
				if (!allowFallback)
					return null;
			}

			return await RunOneShotOperationAsync(operation, args, requestPrefix ?? operation).ConfigureAwait(false);
		}

		/// <summary>Runs an operation through the resident script: enqueue an {id, op, args} envelope, wake the parked
		/// GetQueries poll, await the ReportJson keyed by the id. On timeout Json is null and AllowFallback
		/// says whether the operation is guaranteed to never run here (safe to retry one-shot); a timeout also
		/// marks the channel suspect unless it was already rebuilt while we waited.</summary>
		private static async Task<(string Json, bool AllowFallback)> SubmitPersistentOperationAsync(string operation, object args, int timeoutMs)
		{
			var service = queryService;
			var gen = service.Generation;
			var requestId = NextRequestId();
			var waiter = service.WaitForJsonAsync(requestId, timeoutMs);
			service.Enqueue(requestId, BuildOperationEnvelope(requestId, operation, args));
			var (ok, json) = await waiter.ConfigureAwait(false);

			if (ok)
				return (json, false);

			if (service.Generation == gen)
				persistentReady = false;   // next query re-probes (with backoff); a rebuilt channel is left alone

			return (null, service.TryCancel(requestId));
		}

		/// <summary>
		/// Ensures the resident operation script is loaded and polling, (re)creating it when needed. Returns false
		/// (callers then use the one-shot path) when KWin is unreachable, the probe fails, a recent attempt
		/// failed and the backoff window hasn't elapsed, repeated failures latched the channel off, or another
		/// caller is mid-establishment (never block a query behind a multi-second attempt).
		/// </summary>
		private static async Task<bool> EnsurePersistentChannelAsync()
		{
			if (persistentReady && queryService.PollerAlive)
				return true;

			if (persistentFailures >= MaxPersistentFailures)
				return false;   // latched off (this KWin can't run the channel); a KWin owner change re-arms it

			if (!await persistentLock.WaitAsync(0).ConfigureAwait(false))
				return false;

			var attempted = false;
			var succeeded = false;

			try
			{
				if (persistentReady && queryService.PollerAlive)
					return succeeded = true;

				if (DateTime.UtcNow - lastPersistentAttempt < persistentRetryBackoff)
					return false;

				attempted = true;
				lastPersistentAttempt = DateTime.UtcNow;
				persistentReady = false;

				if (queryConnection == null)
				{
					var localConn = new Connection(Tmds.DBus.Address.Session);

					try
					{
						await localConn.ConnectAsync().ConfigureAwait(false);
						var localService = new KWinQueryService();
						await localConn.RegisterObjectAsync(localService).ConfigureAwait(false);
						await localConn.RegisterServiceAsync(queryServiceName).ConfigureAwait(false);
						queryConnection = localConn;
						queryService = localService;
					}
					catch
					{
						try { localConn.Dispose(); }
						catch { }

						return false;
					}
				}

				// Retire the previous queue: drops undelivered envelopes (no stale-command replay through the
				// new script) and wakes any poll still parked by the old script so it can't steal the probe.
				queryService.Reset();
				// A previous incarnation (pre-restart) may still be loaded under our plugin name; KWin
				// refuses to load a second script with the same name, so clear it first.
				await TryUnloadScript(persistentPluginName).ConfigureAwait(false);
				var scriptPath = WritePersistentQueryScript(persistentPluginName);

				try
				{
					if (!await TryLoadAndRunScriptAsync(scriptPath, persistentPluginName).ConfigureAwait(false))
					{
						await TryUnloadScript(persistentPluginName).ConfigureAwait(false);
						return false;
					}

					// Round-trip probe: proves the script is polling and dispatching on this KWin before we
					// commit every caller to the channel.
					RegisterPersistentCleanup();

					if ((await SubmitPersistentOperationAsync("ping", null, ProbeTimeoutMs).ConfigureAwait(false)).Json == null)
					{
						await TryUnloadScript(persistentPluginName).ConfigureAwait(false);
						return false;
					}

					persistentReady = true;
					return succeeded = true;
				}
				finally
				{
					// KWin reads the file when the script is run; after the probe (or on failure) it can go.
					SafeDelete(scriptPath);
				}
			}
			catch
			{
				return false;
			}
			finally
			{
				if (attempted)
					persistentFailures = succeeded ? 0 : persistentFailures + 1;

				persistentLock.Release();
			}
		}

		/// <summary>One-time hooks that keep the resident script from outliving us or a KWin restart: unload
		/// it on process exit, and invalidate the channel when org.kde.KWin changes bus owner (compositor
		/// restart) so the next query reloads immediately instead of eating the waiter timeout.</summary>
		private static void RegisterPersistentCleanup()
		{
			if (persistentCleanupRegistered)
				return;

			persistentCleanupRegistered = true;
			AppDomain.CurrentDomain.ProcessExit += (_, _) =>
			{
				try { Task.Run(() => TryUnloadScript(persistentPluginName)).Wait(500); }
				catch { }
			};

		}

		private static async Task<string> RunOneShotOperationAsync(string operation, object args, string requestPrefix)
		{
			var requestId = NextRequestId();
			var pluginName = $"keysharp_{SanitizePrefix(requestPrefix)}_{requestId}";
			string scriptPath = null;

			try
			{
				scriptPath = WriteOperationScript(requestId, pluginName, operation, args);
				var awaiterTask = bridgeService.WaitForJsonAsync(requestId, QueryTimeoutMs);

				if (!await TryLoadAndRunScriptAsync(scriptPath, pluginName).ConfigureAwait(false))
				{
					await TryUnloadScript(pluginName).ConfigureAwait(false);
					return null;
				}

				var (ok, json) = await awaiterTask.ConfigureAwait(false);
				await TryUnloadScript(pluginName).ConfigureAwait(false);
				return ok ? json : null;
			}
			catch
			{
				return null;
			}
			finally
			{
				SafeDelete(scriptPath);
			}
		}

		/// <summary>
		/// Loads a <em>persistent</em> KWin script that connects to compositor signals and pushes events back via
		/// <c>ReportEvent</c>. Unlike request/response operations, the script is left loaded; the returned
		/// <see cref="IDisposable"/> unloads it and unregisters the handler. <paramref name="onEvent"/> is invoked
		/// (on a D-Bus thread) as <c>(eventType, json)</c> for each event. Returns null if KWin is unreachable.
		/// </summary>
		internal static async Task<IDisposable> StartEventScriptAsync(string token, string scriptBody, Action<string, string> onEvent)
		{
			if (!await EnsureConnectedAsync().ConfigureAwait(false))
				return null;

			var pluginName = $"keysharp_events_{token}";
			string scriptPath = null;

			bridgeService.RegisterEventHandler(token, onEvent);

			try
			{
				scriptPath = WriteEventScript(token, pluginName, scriptBody);

				if (!await TryLoadAndRunScriptAsync(scriptPath, pluginName).ConfigureAwait(false))
				{
					await TryUnloadScript(pluginName).ConfigureAwait(false);
					bridgeService.UnregisterEventHandler(token);
					SafeDelete(scriptPath);
					return null;
				}

				return new EventSubscription(token, pluginName, scriptPath);
			}
			catch
			{
				bridgeService.UnregisterEventHandler(token);
				SafeDelete(scriptPath);
				return null;
			}
		}

		private static void SafeDelete(string path)
		{
			if (path == null)
				return;

			try { File.Delete(path); }
			catch { }
		}

		/// <summary>Handle returned by <see cref="StartEventScriptAsync"/>; unloads the persistent script and
		/// unregisters its handler on dispose (idempotent, best-effort).</summary>
		private sealed class EventSubscription : IDisposable
		{
			private readonly string token;
			private readonly string pluginName;
			private readonly string scriptPath;
			private int disposed;

			internal EventSubscription(string token, string pluginName, string scriptPath)
			{
				this.token = token;
				this.pluginName = pluginName;
				this.scriptPath = scriptPath;
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				bridgeService?.UnregisterEventHandler(token);

				// Fire-and-forget the unload; we don't want Dispose to block on a D-Bus round-trip.
				_ = Task.Run(async () =>
				{
					await TryUnloadScript(pluginName).ConfigureAwait(false);
					SafeDelete(scriptPath);
				});
			}
		}

		private static async Task<bool> TryLoadAndRunScriptAsync(string scriptPath, string pluginName)
		{
			try
			{
				var scriptId = await scriptingProxy.loadScriptAsync(scriptPath, pluginName).ConfigureAwait(false);
				return await TryRunScriptAsync(scriptId).ConfigureAwait(false);
			}
			catch
			{
				return false;
			}
		}

		private static async Task<bool> TryRunScriptAsync(int scriptId)
		{
			var cachedFormat = scriptPathFormat;

			if (cachedFormat != null)
				return await TryRunWithFormatAsync(cachedFormat, scriptId).ConfigureAwait(false);

			// First call on this process: probe KWin 6 path, then KWin 5 fallback.
			// Whichever responds without an UnknownObject error is the one we keep.
			foreach (var format in new[] { "/Scripting/Script{0}", "/{0}" })
			{
				if (await TryRunWithFormatAsync(format, scriptId).ConfigureAwait(false))
				{
					scriptPathFormat = format;
					return true;
				}
			}

			return false;
		}

		private static async Task<bool> TryRunWithFormatAsync(string format, int scriptId)
		{
			try
			{
				var path = string.Format(CultureInfo.InvariantCulture, format, scriptId);
				var proxy = connection.CreateProxy<IKWinScript>("org.kde.KWin", new ObjectPath(path));
				await proxy.runAsync().ConfigureAwait(false);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static async Task TryUnloadScript(string pluginName)
		{
			try
			{
				_ = await scriptingProxy.unloadScriptAsync(pluginName).ConfigureAwait(false);
			}
			catch
			{
				// Best-effort cleanup; the script already reported or failed.
			}
		}

		private static async Task<bool> EnsureConnectedAsync()
		{
			lock (initLock)
				if (connection != null)
					return scriptingProxy != null;

			Connection localConnection = null;
			KWinBridgeService localBridge = null;
			IDisposable localOwnerWatch = null;

			await initSemaphore.WaitAsync().ConfigureAwait(false);

			try
			{
				lock (initLock)
				{
					if (connection != null)
						return scriptingProxy != null;
				}

				using var attempt = initRetries.TryBegin();

				if (attempt == null)
					return false;

				localConnection = new Connection(Tmds.DBus.Address.Session);
				_ = await localConnection.ConnectAsync().ConfigureAwait(false);
				localBridge = new KWinBridgeService();
				await localConnection.RegisterObjectAsync(localBridge).ConfigureAwait(false);
				await localConnection.RegisterServiceAsync(bridgeServiceName).ConfigureAwait(false);

				lock (initLock)
				{
					if (connection != null)
					{
						localConnection.Dispose();
						return scriptingProxy != null;
					}

					connection = localConnection;
					bridgeService = localBridge;
				}

				var connected = localConnection;
				connected.StateChanged += (_, e) =>
				{
					if (e.State == Tmds.DBus.ConnectionState.Disconnected)
						InvalidateConnection(connected, e.DisconnectReason);
				};

				localOwnerWatch = await connected.ResolveServiceOwnerAsync("org.kde.KWin",
					change => KWinOwnerChanged(connected, change.NewOwner),
					error => InvalidateConnection(connected, error)).ConfigureAwait(false);

				lock (initLock)
				{
					if (!ReferenceEquals(connection, localConnection))
						return false;

					kwinOwnerWatch = localOwnerWatch;
					localOwnerWatch = null;
				}

				long changesBeforeQuery;
				lock (initLock)
					changesBeforeQuery = kwinOwnerChanges;

				var owner = await connected.ResolveServiceOwnerAsync("org.kde.KWin").ConfigureAwait(false);
				lock (initLock)
					if (kwinOwnerChanges == changesBeforeQuery)
						KWinOwnerChanged(connected, owner, false);
				attempt.Succeed();
				localConnection = null;

				lock (initLock)
					return scriptingProxy != null;
			}
			catch (Exception ex)
			{
				InvalidateConnection(localConnection, ex, false);
				return false;
			}
			finally
			{
				try { localOwnerWatch?.Dispose(); } catch { }
				try { localConnection?.Dispose(); } catch { }
				initSemaphore.Release();
			}
		}

		private static void KWinOwnerChanged(Connection source, string newOwner, bool fromWatch = true)
		{
			Action handlers = null;
			bool changed;

			lock (initLock)
			{
				if (!ReferenceEquals(connection, source))
					return;

				if (fromWatch)
					kwinOwnerChanges++;

				changed = !string.Equals(kwinOwner, newOwner, StringComparison.Ordinal);
				kwinOwner = newOwner;
				scriptingProxy = string.IsNullOrEmpty(newOwner) ? null
					: source.CreateProxy<IKWinScripting>(newOwner, new ObjectPath("/Scripting"));

				if (changed)
				{
					scriptPathFormat = null;
					persistentReady = false;
					persistentFailures = 0;
					lastPersistentAttempt = DateTime.MinValue;
					handlers = availabilityChanged;
				}
			}

			if (changed)
			{
				queryService?.Reset();
				Notify(handlers);
			}
		}

		private static void InvalidateConnection(Connection failed, Exception error, bool rearm = true)
		{
			IDisposable watch = null;
			Connection retired = null;
			Action handlers = null;

			lock (initLock)
			{
				if (failed == null || !ReferenceEquals(connection, failed))
					return;

				retired = connection;
				connection = null;
				bridgeService = null;
				scriptingProxy = null;
				kwinOwner = null;
				watch = kwinOwnerWatch;
				kwinOwnerWatch = null;
				persistentReady = false;
				handlers = availabilityChanged;
			}

			try { watch?.Dispose(); } catch { }
			try { retired?.Dispose(); } catch { }

			if (rearm)
				initRetries.Rearm();

			Notify(handlers);
		}

		private static void Notify(Action handlers)
		{
			if (handlers == null)
				return;

			foreach (Action handler in handlers.GetInvocationList())
				try { handler(); } catch { }
		}

		internal static bool HasOwner
		{
			get
			{
				_ = EnsureConnectedAsync().GetAwaiter().GetResult();
				lock (initLock)
					return scriptingProxy != null;
			}
		}

		internal static IDisposable SubscribeAvailability(Action handler)
		{
			if (handler == null)
				return null;

			availabilityChanged += handler;
			_ = EnsureConnectedAsync();
			return new AvailabilitySubscription(handler);
		}

		internal static void Reset()
		{
			Connection main;
			Connection queries;
			IDisposable watch;

			lock (initLock)
			{
				main = connection;
				queries = queryConnection;
				watch = kwinOwnerWatch;
				connection = null;
				queryConnection = null;
				bridgeService = null;
				queryService = null;
				scriptingProxy = null;
				kwinOwnerWatch = null;
				kwinOwner = null;
				kwinOwnerChanges++;
				scriptPathFormat = null;
				persistentReady = false;
				persistentFailures = 0;
				lastPersistentAttempt = DateTime.MinValue;
			}

			try { watch?.Dispose(); } catch { }
			try { main?.Dispose(); } catch { }
			try { queries?.Dispose(); } catch { }
			initRetries.Rearm();
			KWinFakeInputBridge.Reset();
		}

		private sealed class AvailabilitySubscription : IDisposable
		{
			private Action handler;

			internal AvailabilitySubscription(Action handler) => this.handler = handler;

			public void Dispose()
			{
				var remove = Interlocked.Exchange(ref handler, null);
				if (remove != null)
					availabilityChanged -= remove;
			}
		}

		// The window-identity/serialization helpers shared VERBATIM by the operation script and the event
		// script (WaylandBackend.EventScriptBody). windowId() defines the compositor id string that C#
		// folds into bit-62 handles — if the two scripts ever disagree, event handles stop resolving
		// through queries and WinEvent silently breaks, so there is exactly one copy.
		internal const string WindowHelpersJs = """
function safeRead(object, name, fallback) {
  try {
    if (!object || typeof object[name] === "undefined" || object[name] === null) return fallback;
    return object[name];
  } catch (e) {
    return fallback;
  }
}
function safeString(value) {
  if (value === null || value === undefined) return "";
  return String(value);
}
function safeBool(value) {
  try { return !!value; } catch (e) { return false; }
}
function readRect(rect) {
  if (!rect) return { x: 0, y: 0, width: 0, height: 0 };
  return {
    x: Math.round(rect.x || 0),
    y: Math.round(rect.y || 0),
    width: Math.round(rect.width || 0),
    height: Math.round(rect.height || 0)
  };
}
function sortByStackingOrder(list) {
  // Plasma 5's workspace.clientList() is NOT returned in stacking order, but every client carries a
  // numeric stackingOrder (higher = closer to the top).  Sort ascending so the result honours the
  // bottom-to-top contract callers expect from workspace.stackingOrder.  Returns a plain Array.
  try {
    var arr = [];
    for (var i = 0; i < list.length; ++i) arr.push(list[i]);
    arr.sort(function(a, b) {
      var sa = (a && typeof a.stackingOrder === "number") ? a.stackingOrder : 0;
      var sb = (b && typeof b.stackingOrder === "number") ? b.stackingOrder : 0;
      return sa - sb;
    });
    return arr;
  } catch (e) { return list; }
}
function windowListCompat() {
  // Plasma 6 exposes workspace.stackingOrder already sorted bottom-to-top; use it verbatim.
  try {
    if (workspace.stackingOrder && workspace.stackingOrder.length !== undefined) return workspace.stackingOrder;
  } catch (e) {}
  // Plasma 5 has neither workspace.stackingOrder nor windowAt(); windowList()/clientList() exist but
  // return windows in an arbitrary (unstacked) order, so sort them into stacking order ourselves.
  try {
    if (typeof workspace.windowList === "function") return sortByStackingOrder(workspace.windowList());
  } catch (e) {}
  try {
    if (typeof workspace.clientList === "function") return sortByStackingOrder(workspace.clientList());
  } catch (e) {}
  try {
    if (workspace.clientList && workspace.clientList.length !== undefined) return sortByStackingOrder(workspace.clientList);
  } catch (e) {}
  return [];
}
function activeWindowCompat() {
  try {
    if (workspace.activeWindow) return workspace.activeWindow;
  } catch (e) {}
  try {
    if (workspace.activeClient) return workspace.activeClient;
  } catch (e) {}
  return null;
}
function firstNonEmpty() {
  for (var i = 0; i < arguments.length; ++i) {
    var value = safeString(arguments[i]);
    if (value.length > 0) return value;
  }
  return "";
}
function windowId(w) {
  try {
    var id = safeString(w.internalId);
    if (id.length > 0) return id;
  } catch (e) {}
  try {
    var windowId = safeString(w.windowId);
    if (windowId.length > 0) return "windowId:" + windowId;
  } catch (e) {}
  var frame = readRect(safeRead(w, "frameGeometry", safeRead(w, "geometry", null)));
  var pid = safeString(safeRead(w, "pid", 0));
  var cls = firstNonEmpty(safeRead(w, "resourceClass", ""), safeRead(w, "desktopFileName", ""), safeRead(w, "resourceName", ""));
  var caption = safeString(safeRead(w, "caption", ""));
  if (pid.length > 0 || cls.length > 0 || caption.length > 0) {
    return "fallback:" + pid + "|" + cls + "|" + caption + "|" + frame.x + "," + frame.y + "," + frame.width + "," + frame.height;
  }
  return "";
}
function isMaximized(w) {
  try {
    if (typeof w.maximized !== "undefined") return !!w.maximized;
  } catch (e) {}
  // KWin 5 exposes maximizeMode (0=Restore, 1=Vertical, 2=Horizontal, 3=Full) instead of a maximized bool.
  try {
    if (typeof w.maximizeMode !== "undefined") return Number(w.maximizeMode) === 3;
  } catch (e) {}
  try {
    var area = workspace.clientArea(KWin.MaximizeArea, w);
    var g = w.frameGeometry;
    return Math.round(g.x) === Math.round(area.x)
      && Math.round(g.y) === Math.round(area.y)
      && Math.round(g.width) === Math.round(area.width)
      && Math.round(g.height) === Math.round(area.height);
  } catch (e) {}
  return false;
}
// A maximized (or edge-tiled) window ignores a frameGeometry assignment until it is restored, so clear the
// maximize state first — the compositor equivalent of dragging the titlebar to un-maximize before moving.
// Handles the KWin 6 `maximized` bool and the KWin 5 `maximizeMode` (0=Restore) so a partial tile is cleared too.
function clearMaximize(w) {
  try {
    var maxed = false;
    if (typeof w.maximized !== "undefined") maxed = !!w.maximized;
    if (!maxed && typeof w.maximizeMode !== "undefined") maxed = Number(w.maximizeMode) !== 0;
    if (maxed && typeof w.setMaximize === "function") w.setMaximize(false, false);
  } catch (e) {}
}
function opacityToAlpha(value) {
  var opacity = Number(value);
  if (!isFinite(opacity)) opacity = 1;
  if (opacity < 0) opacity = 0;
  if (opacity > 1) opacity = 1;
  return Math.round(opacity * 255);
}
function windowInfo(w, includeSpecial) {
  if (!w) return null;
  try {
    if (safeBool(w.deleted)) return null;
    if (!includeSpecial) {
      if (typeof w.managed !== "undefined" && !safeBool(w.managed)) return null;
      if (safeBool(w.specialWindow)) return null;
    }
    var id = windowId(w);
    if (!id) return null;
    var frame = readRect(safeRead(w, "frameGeometry", safeRead(w, "geometry", null)));
    // clientGeometry isn't exposed to scripts on every KWin build; when absent it would collapse to the frame.
    // Try it first, then the clientPos (inset relative to the frame) + clientSize pair, then fall back to frame.
    var client = readRect(safeRead(w, "clientGeometry", null));
    if (!client || client.width === 0) {
      var cp = safeRead(w, "clientPos", null);
      var cs = safeRead(w, "clientSize", null);
      if (cp && cs && (cs.width || 0) > 0)
        client = { x: frame.x + Math.round(cp.x || 0), y: frame.y + Math.round(cp.y || 0),
                   width: Math.round(cs.width || 0), height: Math.round(cs.height || 0) };
      else
        client = frame;
    }
    // The surface as the client drew it, shadow included: the origin a Wayland client's own coordinates are
    // relative to. Not exposed on every KWin build, and absent leaves the consumer uncorrected rather than wrong.
    var buffer = readRect(safeRead(w, "bufferGeometry", null));
    if (buffer && buffer.width === 0) buffer = null;
    return {
      id: id,
      buffer: buffer,
      title: safeString(safeRead(w, "caption", "")),
      appId: firstNonEmpty(safeRead(w, "resourceClass", ""), safeRead(w, "desktopFileName", ""), safeRead(w, "resourceName", "")),
      pid: Math.round(safeRead(w, "pid", 0) || 0),
      frame: frame,
      client: client,
      active: safeBool(w.active),
      minimized: safeBool(w.minimized),
      maximized: isMaximized(w),
      visible: !safeBool(w.hidden),
      alwaysOnTop: safeBool(w.keepAbove),
      decorated: !safeBool(w.noBorder),
      transparency: opacityToAlpha(safeRead(w, "opacity", 1))
    };
  } catch (e) {
    return null;
  }
}
function findWindow(id) {
  var order = windowListCompat();
  for (var i = 0; i < order.length; ++i) {
    if (windowId(order[i]) === id) return order[i];
  }
  var active = activeWindowCompat();
  if (active && windowId(active) === id) return active;
  return null;
}
""";

		private const string KWinOperationScript = WindowHelpersJs + "\n" + """
function argBool(args, name, fallback) {
  try {
    if (!args || typeof args[name] === "undefined" || args[name] === null) return fallback;
    return !!args[name];
  } catch (e) {
    return fallback;
  }
}
function argNumber(args, name, fallback) {
  try {
    var value = Number(args ? args[name] : undefined);
    return isFinite(value) ? value : fallback;
  } catch (e) {
    return fallback;
  }
}
function argString(args, name) {
  return safeString(args ? args[name] : "");
}
function readWorkArea() {
  function rd(r) {
    return r ? { x: Math.round(r.x || 0), y: Math.round(r.y || 0), width: Math.round(r.width || 0), height: Math.round(r.height || 0) } : null;
  }
  var area = null;
  try { area = workspace.clientArea(KWin.MaximizeArea, workspace.activeScreen, workspace.currentDesktop); } catch (e) { area = null; }
  if (!area) { try { area = workspace.clientArea(KWin.PlacementArea, workspace.activeScreen, workspace.currentDesktop); } catch (e) {} }
  return { ok: !!area, area: rd(area) };
}
function windowAtPoint(px, py, ownPid) {
  function isOwnPassiveSurface(cand) {
    // Keysharp's input-empty layer overlays deliberately use the on-screen-display scope.  Match
    // both role and owner so another process's potentially interactive special window is preserved.
    return ownPid > 0
        && Math.round(safeRead(cand, "pid", 0) || 0) === ownPid
        && safeBool(safeRead(cand, "onScreenDisplay", false));
  }

  function usableAtPoint(cand) {
    if (!cand) return null;
    if (safeBool(cand.deleted) || safeBool(cand.hidden) || safeBool(cand.minimized)) return null;
    if (isOwnPassiveSurface(cand)) return null;
    var g = safeRead(cand, "frameGeometry", safeRead(cand, "geometry", null));
    if (!g || g.width <= 0 || g.height <= 0) return null;
    if (px < g.x || py < g.y || px >= g.x + g.width || py >= g.y + g.height) return null;
    return cand;
  }

  try {
    if (typeof workspace.windowAt === "function") {
      // A negative count asks KWin for every input hit, topmost first.  Current KWin applies each
      // Wayland surface's input region in this query.  Walking the full result also lets us skip a
      // compositor OSD defensively and continue to the real receiver underneath.
      var hits = workspace.windowAt(Qt.point(px, py), -1);
      if (hits && typeof hits.length !== "undefined") {
        for (var i = 0; i < hits.length; ++i) {
          var hit = usableAtPoint(hits[i]);
          if (hit) return hit;
        }
      } else {
        var hit = usableAtPoint(hits);
        if (hit) return hit;
      }
    }
  } catch (e) {}

  // Compatibility path for KWin versions without workspace.windowAt() (notably Plasma 5, where
  // windowAt() does not exist).  windowListCompat() returns windows sorted bottom-to-top, so scan
  // it backwards and take the first eligible hit.  The old active-window/smallest-area heuristic
  // was not a z-order operation and selected obscured windows.
  try {
    var order = windowListCompat();
    for (var i = order.length - 1; i >= 0; --i) {
      var hit = usableAtPoint(order[i]);
      if (hit) return hit;
    }
  } catch (e) {}
  return null;
}
// Windows this process asked to have claimed and placed before they exist. A client cannot recognise
// its own window at creation nor place it before it has been painted at the compositor's chosen spot;
// reserving ahead of Show() lets the script do both from windowAdded. FIFO per pid: Show() is synchronous
// client-side, so the oldest live reservation belongs to the next window that pid maps.
var reservations = [];
var placedByCookie = {};
var reservationHookInstalled = false;

function sweepReservations() {
  var now = Date.now();
  var keep = [];
  for (var i = 0; i < reservations.length; ++i)
    if (reservations[i].expires > now) keep.push(reservations[i]);
  reservations = keep;
  for (var key in placedByCookie)
    if (placedByCookie[key].expires <= now) delete placedByCookie[key];
}

function onReservedWindowAdded(w) {
  try {
    sweepReservations();
    if (!reservations.length) return;
    // Same window classes the shell extensions consume for: a reservation must not be stolen by this
    // process's own overlays/OSD surfaces, which windowAdded also reports.
    var tracked = safeBool(safeRead(w, "normalWindow", false))
      || safeBool(safeRead(w, "dialog", false))
      || safeBool(safeRead(w, "utility", false));
    if (!tracked) return;
    var pid = Math.round(safeRead(w, "pid", 0) || 0);
    if (pid <= 0) return;
    var idx = -1;
    for (var i = 0; i < reservations.length; ++i)
      if (reservations[i].pid === pid) { idx = i; break; }
    if (idx < 0) return;
    var r = reservations.splice(idx, 1)[0];
    if (r.cookie) placedByCookie[r.pid + ":" + r.cookie] = { id: windowId(w), expires: r.expires };
    if (r.x === -2147483648 && r.y === -2147483648) return;
    var apply = function() {
      try {
        var g = safeRead(w, "frameGeometry", safeRead(w, "geometry", null));
        if (!g || Math.round(g.width || 0) <= 0) return false;
        // Position only, never a size: the size the client chose is already on its way, and forcing a
        // different one stalls the move behind a resize the client never acks.
        w.frameGeometry = {
          x: Math.round(r.x !== -2147483648 ? r.x : g.x),
          y: Math.round(r.y !== -2147483648 ? r.y : g.y),
          width: Math.round(g.width),
          height: Math.round(g.height)
        };
        return true;
      } catch (e) { return false; }
    };
    if (!apply()) {
      // A Wayland window can reach windowAdded before its first commit, with no geometry to move yet.
      try {
        var once = function() {
          try { w.windowShown.disconnect(once); } catch (e) {}
          apply();
        };
        w.windowShown.connect(once);
      } catch (e) {}
    }
  } catch (e) {}
}

// Called by the RESIDENT script only. This file is also embedded into one-shot fallback scripts, and a
// hook auto-connected there would live just long enough to steal a reservation from the resident copy.
function installReservationHook() {
  try { workspace.windowAdded.connect(onReservedWindowAdded); reservationHookInstalled = true; }
  catch (e) { try { workspace.clientAdded.connect(onReservedWindowAdded); reservationHookInstalled = true; } catch (e2) {} }
}

var keysharpOps = {
  reserveWindow: function(args) {
    // In a one-shot fallback script the state below is discarded on unload and the hook is never
    // installed, so accepting would silently lose the reservation; refusing keeps the client on its
    // normal correlate-and-move path.
    if (!reservationHookInstalled) return { ok: false };
    var pid = Math.round(argNumber(args, "pid", 0));
    if (pid <= 0) return { ok: false };
    sweepReservations();
    reservations.push({
      pid: pid,
      cookie: argString(args, "cookie"),
      x: Math.round(argNumber(args, "x", -2147483648)),
      y: Math.round(argNumber(args, "y", -2147483648)),
      expires: Date.now() + Math.max(1, Math.round(argNumber(args, "ttlMs", 2000)))
    });
    return { ok: true };
  },
  getReservedWindow: function(args) {
    sweepReservations();
    // Keyed by pid too: the cookie is a per-process pointer, so two Keysharp processes can mint the same one.
    var key = Math.round(argNumber(args, "pid", 0)) + ":" + argString(args, "cookie");
    var hit = placedByCookie[key];
    if (!hit) return { ok: true, id: "" };
    delete placedByCookie[key];
    return { ok: true, id: hit.id };
  },
  ping: function(args) {
    return { ok: true };
  },
  cursorPos: function(args) {
    var pos = workspace.cursorPos;
    return { ok: true, x: Math.round(pos.x), y: Math.round(pos.y) };
  },
  listWindows: function(args) {
    var includeHidden = argBool(args, "includeHidden", false);
    var result = [];
    var order = windowListCompat();
    for (var i = 0; i < order.length; ++i) {
      var info = windowInfo(order[i]);
      if (!info) continue;
      if (!includeHidden && !info.visible) continue;
      result.push(info);
    }
    return { ok: true, windows: result };
  },
  activeWindow: function(args) {
    var w = activeWindowCompat();
    if (!w) {
      var order = windowListCompat();
      for (var i = order.length - 1; i >= 0; --i) {
        if (safeBool(order[i].active)) { w = order[i]; break; }
      }
    }
    return { ok: true, window: windowInfo(w, true) };
  },
  getWindow: function(args) {
    var w = findWindow(argString(args, "id"));
    // A handle obtained from WinFromPoint may identify a desktop, panel or menu.  Direct handle
    // lookup must round-trip those even though normal window enumeration omits special windows.
    return { ok: true, window: windowInfo(w, true) };
  },
  windowAt: function(args) {
    var px = Math.round(argNumber(args, "x", 0));
    var py = Math.round(argNumber(args, "y", 0));
    var ownPid = Math.round(argNumber(args, "ownPid", 0));
    return { ok: true, window: windowInfo(windowAtPoint(px, py, ownPid), true) };
  },
  workArea: function(args) {
    return readWorkArea();
  },
  activate: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    try { w.minimized = false; } catch (e) {}
    var ok = false;
    try { workspace.activeWindow = w; ok = true; } catch (e) {}
    try { workspace.activeClient = w; ok = true; } catch (e) {}
    try { workspace.raiseWindow(w); } catch (e) {}
    return { ok: ok };
  },
  moveResize: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    clearMaximize(w);
    var g = safeRead(w, "frameGeometry", safeRead(w, "geometry", null));
    if (!g) return { ok: false };
    var setPosition = argBool(args, "setPosition", false);
    var setSize = argBool(args, "setSize", false);
    var nx = setPosition ? Math.round(argNumber(args, "x", g.x || 0)) : Math.round(g.x || 0);
    var ny = setPosition ? Math.round(argNumber(args, "y", g.y || 0)) : Math.round(g.y || 0);
    var nw = setSize ? Math.round(argNumber(args, "width", g.width || 0)) : Math.round(g.width || 0);
    var nh = setSize ? Math.round(argNumber(args, "height", g.height || 0)) : Math.round(g.height || 0);
    if (nw <= 0) nw = Math.round(g.width || 0);
    if (nh <= 0) nh = Math.round(g.height || 0);
    w.frameGeometry = { x: nx, y: ny, width: nw, height: nh };
    return { ok: true };
  },
  moveResizeByXid: function(args) {
    // X11-session move: the caller has an X11 window id (not a KWin uuid), so match on the client's
    // X11 windowId. Setting frameGeometry from a script is not clamped on screen the way an external
    // XMoveWindow ConfigureRequest is, so this reaches off-screen placement Xlib cannot. Position
    // uses the -2147483648 (INT32_MIN) "unchanged" sentinel; size uses <= 0.
    var xid = Math.round(argNumber(args, "xid", 0));
    if (!xid) return { ok: false };
    var order = windowListCompat();
    var w = null;
    for (var i = 0; i < order.length; ++i) {
      var wid = safeRead(order[i], "windowId", 0);
      if (wid && Math.round(wid) === xid) { w = order[i]; break; }
    }
    if (!w) return { ok: false };
    clearMaximize(w);
    var g = safeRead(w, "frameGeometry", safeRead(w, "geometry", null));
    if (!g) return { ok: false };
    var ax = Math.round(argNumber(args, "x", -2147483648));
    var ay = Math.round(argNumber(args, "y", -2147483648));
    var aw = Math.round(argNumber(args, "width", 0));
    var ah = Math.round(argNumber(args, "height", 0));
    var nx = (ax !== -2147483648) ? ax : Math.round(g.x || 0);
    var ny = (ay !== -2147483648) ? ay : Math.round(g.y || 0);
    var nw = (aw > 0) ? aw : Math.round(g.width || 0);
    var nh = (ah > 0) ? ah : Math.round(g.height || 0);
    w.frameGeometry = { x: nx, y: ny, width: nw, height: nh };
    return { ok: true };
  },
  setNoBorder: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    try { w.noBorder = argBool(args, "noBorder", false); } catch (e) {}
    return { ok: true };
  },
  setWindowState: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    var state = argString(args, "state");
    if (state === "minimized") {
      w.minimized = true;
    } else if (state === "maximized") {
      w.minimized = false;
      if (typeof w.setMaximize === "function") w.setMaximize(true, true);
    } else {
      w.minimized = false;
      if (typeof w.setMaximize === "function") w.setMaximize(false, false);
    }
    return { ok: true };
  },
  setAlwaysOnTop: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    w.keepAbove = argBool(args, "onTop", false);
    return { ok: true };
  },
  setSkipTaskbar: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    var skip = argBool(args, "skip", false);
    // Pager and switcher travel with the taskbar: a tool window has no business in any of the three.
    try { w.skipTaskbar = skip; w.skipPager = skip; w.skipSwitcher = skip; } catch (e) {}
    return { ok: true };
  },
  raise: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    var ok = false;
    try {
      if (typeof workspace.raiseWindow === "function") { workspace.raiseWindow(w); ok = true; }
    } catch (e) {
      ok = false;
    }
    return { ok: ok };
  },
  setOpacity: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    var opacity = argNumber(args, "opacity", 1);
    var ok = false;
    try {
      if (typeof w.opacity !== "undefined") { w.opacity = opacity; ok = true; }
    } catch (e) {
      ok = false;
    }
    return { ok: ok };
  },
  close: function(args) {
    var w = findWindow(argString(args, "id"));
    if (!w) return { ok: false };
    w.closeWindow();
    return { ok: true };
  }
};
function executeOperation(op, args) {
  var name = String(op);
  var fn = keysharpOps[name];
  if (typeof fn !== "function") return { ok: false, error: "unknown operation: " + name };
  return fn(args || {});
}
""";

		private static string WriteOperationScript(string requestId, string pluginName, string operation, object args)
		{
			var serviceName = bridgeServiceName;
			var operationJson = JsonSerializer.Serialize(operation ?? string.Empty);
			var argsJson = JsonSerializer.Serialize(args ?? new { });
			var script =
				"(function() {\n"
				+ $"  var __keysharpRequestId = \"{requestId}\";\n"
				+ KWinOperationScript
				+ "\n  function reportJson(json) {\n"
				+ $"    callDBus(\"{serviceName}\", \"{BridgeObjectPath}\", \"org.keysharp.KWinBridge\", \"ReportJson\", __keysharpRequestId, String(json));\n"
				+ "  }\n"
				+ "  function report(value) { reportJson(JSON.stringify(value)); }\n"
				+ "  function reportError(error) { report({ ok: false, error: String(error) }); }\n"
				+ "  try {\n"
				+ $"    var result = executeOperation({operationJson}, {argsJson});\n"
				+ "    if (typeof result === \"undefined\") result = { ok: true };\n"
				+ "    report(result);\n"
				+ "  } catch (e) {\n"
				+ "    reportError(e);\n"
				+ "  }\n"
				+ "})();\n";
			return WriteTempScript(pluginName, script);
		}

		// Resident operation script: keeps one GetQueries long poll outstanding against the dedicated query
		// service; each returned envelope {id, op, args} is dispatched to a prewritten operation table.
		// Keysharp always answers GetQueries before the D-Bus call timeout (empty batch when idle), so the
		// poll callback — and with it the loop — is guaranteed to fire; an error still ends the chain, which
		// the C# side detects via PollerAlive/waiter timeout and heals by reloading the script.
		private static string WritePersistentQueryScript(string pluginName)
		{
			var call = $"callDBus(\"{queryServiceName}\", \"{QueryObjectPath}\", \"org.keysharp.KWinQueryBridge\"";
			var script =
				"(function() {\n"
				+ KWinOperationScript
				+ "\n"
				+ "  installReservationHook();\n"
				+ "  function reportTo(id, json) {\n"
				+ $"    try {{ {call}, \"ReportJson\", id, String(json)); }} catch (e) {{}}\n"
				+ "  }\n"
				+ "  function execute(id, op, args) {\n"
				+ "    try {\n"
				+ "      var result = executeOperation(String(op), args || {});\n"
				+ "      if (typeof result === \"undefined\") result = { ok: true };\n"
				+ "      reportTo(id, JSON.stringify(result));\n"
				+ "    } catch (e) {\n"
				+ "      reportTo(id, JSON.stringify({ ok: false, error: String(e) }));\n"
				+ "    }\n"
				+ "  }\n"
				+ "  function poll() {\n"
				+ $"    try {{ {call}, \"GetQueries\", function(queries) {{\n"
				+ "      try {\n"
				+ "        for (var i = 0; i < queries.length; ++i) {\n"
				+ "          try {\n"
				+ "            var q = JSON.parse(queries[i]);\n"
				+ "            execute(String(q.id), String(q.op), q.args || {});\n"
				+ "          } catch (e) {}\n"
				+ "        }\n"
				+ "      } catch (e) {}\n"
				+ "      poll();\n"
				+ "    }); } catch (e) {}\n"
				+ "  }\n"
				+ "  poll();\n"
				+ "})();\n";
			return WriteTempScript(pluginName, script);
		}

		// Persistent event script: exposes emit(type, value) which pushes (token, type, JSON) to ReportEvent.
		// Unlike WriteScript there is no per-call request id and the body is expected to install signal handlers
		// (it keeps running until the plugin is unloaded).
		private static string WriteEventScript(string token, string pluginName, string scriptBody)
		{
			var serviceName = bridgeServiceName;
			var script =
				"(function() {\n"
				+ $"  var __keysharpToken = \"{token}\";\n"
				+ "  function emit(type, value) {\n"
				+ "    try {\n"
				+ $"      callDBus(\"{serviceName}\", \"{BridgeObjectPath}\", \"org.keysharp.KWinBridge\", \"ReportEvent\", __keysharpToken, String(type), JSON.stringify(value));\n"
				+ "    } catch (e) {}\n"
				+ "  }\n"
				+ "  try {\n"
				+ scriptBody
				+ "\n  } catch (e) {}\n"
				+ "})();\n";
			return WriteTempScript(pluginName, script);
		}

		private static string WriteTempScript(string pluginName, string script)
		{
			var path = Path.Combine(Path.GetTempPath(), $"{pluginName}.js");
			File.WriteAllText(path, script);
			return path;
		}

		private static string SanitizePrefix(string prefix)
		{
			if (prefix.IsNullOrEmpty())
				return "request";

			var sb = new StringBuilder(prefix.Length);

			foreach (var ch in prefix)
				sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

			return sb.ToString();
		}

		private static string NextRequestId()
			=> $"{Environment.ProcessId:x}_{Interlocked.Increment(ref requestCounter):x}";

		private static string BuildOperationEnvelope(string requestId, string operation, object args)
		{
			var op = JsonSerializer.Serialize(operation ?? string.Empty);
			var argJson = args == null ? "{}" : JsonSerializer.Serialize(args);
			return "{\"id\":\"" + requestId + "\",\"op\":" + op + ",\"args\":" + argJson + "}";
		}

		private static bool JsonOk(string json)
		{
			if (json.IsNullOrEmpty())
				return false;

			using var doc = JsonDocument.Parse(json);
			return JsonBool(doc.RootElement, "ok");
		}

		private static bool JsonBool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value)
			   && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) && i != 0));

		private static bool JsonInt(JsonElement element, string property, out int result)
		{
			result = 0;

			if (!element.TryGetProperty(property, out var value))
				return false;

			if (value.ValueKind == JsonValueKind.Number)
				return value.TryGetInt32(out result);

			if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
				return true;

			return false;
		}

		/// <summary>Request-id-keyed result waiters shared by both bridge services: a waiter is registered
		/// before the query is dispatched, completed by the matching ReportJson, or resolved (false, null)
		/// by its timeout.</summary>
		private sealed class PendingJsonTable
		{
			private readonly object gate = new();
			private readonly Dictionary<string, PendingJsonEntry> pending = new();

			internal Task<(bool Ok, string Json)> WaitAsync(string requestId, int timeoutMs)
			{
				var entry = new PendingJsonEntry(this, requestId);

				lock (gate)
					pending[requestId] = entry;

				entry.StartTimer(timeoutMs);
				return entry.Task;
			}

			private void Timeout(string requestId)
			{
				PendingJsonEntry entry = null;

				lock (gate)
				{
					if (pending.TryGetValue(requestId, out entry))
						pending.Remove(requestId);
				}

				entry?.Complete(false, null);
			}

			internal void Complete(string requestId, string json)
			{
				PendingJsonEntry entry = null;

				lock (gate)
				{
					if (pending.TryGetValue(requestId, out entry))
						pending.Remove(requestId);
				}

				entry?.Complete(true, json);
			}

			private sealed class PendingJsonEntry
			{
				private readonly PendingJsonTable owner;
				private readonly string requestId;
				private readonly TaskCompletionSource<(bool, string)> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
				private readonly Timer timer;

				internal PendingJsonEntry(PendingJsonTable owner, string requestId)
				{
					this.owner = owner;
					this.requestId = requestId;
					timer = new Timer(static state =>
					{
						var entry = (PendingJsonEntry)state;
						entry.owner.Timeout(entry.requestId);
					}, this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
				}

				internal Task<(bool Ok, string Json)> Task => tcs.Task;

				internal void StartTimer(int timeoutMs) => timer.Change(timeoutMs, System.Threading.Timeout.Infinite);

				internal void Complete(bool ok, string json)
				{
					timer.Dispose();
					tcs.TrySetResult((ok, json));
				}
			}
		}

		private sealed class KWinBridgeService : IKWinBridge
		{
			private readonly PendingJsonTable pending = new();
			private readonly ConcurrentDictionary<string, Action<string, string>> eventHandlers = new();

			public ObjectPath ObjectPath => BridgeObjectPath;

			public Task ReportJsonAsync(string requestId, string json)
			{
				pending.Complete(requestId, json);
				return Task.CompletedTask;
			}

			public Task ReportEventAsync(string token, string eventType, string json)
			{
				if (eventHandlers.TryGetValue(token, out var handler))
				{
					try { handler(eventType, json); }
					catch { }   // never let a consumer fault break the D-Bus dispatch loop
				}

				return Task.CompletedTask;
			}

			internal void RegisterEventHandler(string token, Action<string, string> handler) => eventHandlers[token] = handler;

			internal void UnregisterEventHandler(string token) => eventHandlers.TryRemove(token, out _);

			internal Task<(bool Ok, string Json)> WaitForJsonAsync(string requestId, int timeoutMs)
				=> pending.WaitAsync(requestId, timeoutMs);
		}

		/// <summary>
		/// The resident operation script's counterpart: GetQueries parks until an operation envelope is enqueued (or
		/// an idle timeout well under the D-Bus call timeout, so the script's poll callback always fires and
		/// re-polls), then returns the queued batch. ReportJson completes the matching waiter. Lives on its
		/// own connection so a parked poll can't interact with the event/one-shot service.
		/// </summary>
		private sealed class KWinQueryService : IKWinQueryBridge
		{
			private const int IdleReplyMs = 15000;   // safely under the ~25s default D-Bus method-call timeout
			private const int MaxBatch = 16;

			private readonly PendingJsonTable pending = new();
			// The queue is swapped wholesale by Reset(): a poll parked by a DEAD script's final GetQueries is
			// still awaiting the old channel, so completing that channel keeps it from stealing envelopes
			// (probe included) meant for the reloaded script.
			private volatile Channel<(string Id, string Json)> queue = Channel.CreateUnbounded<(string, string)>();
			private readonly object stateGate = new();
			private readonly HashSet<string> cancelled = [];   // timed out before delivery: skip at dequeue
			private readonly HashSet<string> delivered = [];   // handed to the script, no ReportJson yet
			private int generation;
			private int activePollers;
			private long lastPollTicks;

			public ObjectPath ObjectPath => QueryObjectPath;

			public async Task<string[]> GetQueriesAsync()
			{
				_ = Interlocked.Increment(ref activePollers);
				var q = queue;   // pin this poll to the current channel generation

				try
				{
					if (!q.Reader.TryRead(out var first))
					{
						using var cts = new CancellationTokenSource(IdleReplyMs);

						try
						{
							first = await q.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
						}
						catch (Exception)   // idle timeout, or the channel was retired by Reset()
						{
							return [];   // empty batch keeps a live script's poll loop alive
						}
					}

					var list = new List<string>();

					do
					{
						lock (stateGate)
						{
							if (cancelled.Remove(first.Id))
								continue;   // its waiter already timed out and fell back one-shot

							_ = delivered.Add(first.Id);
						}

						list.Add(first.Json);
					}
					while (list.Count < MaxBatch && q.Reader.TryRead(out first));

					return [.. list];
				}
				finally
				{
					_ = Interlocked.Decrement(ref activePollers);
					_ = Interlocked.Exchange(ref lastPollTicks, DateTime.UtcNow.Ticks);
				}
			}

			public Task ReportJsonAsync(string requestId, string json)
			{
				lock (stateGate)
					_ = delivered.Remove(requestId);

				pending.Complete(requestId, json);
				return Task.CompletedTask;
			}

			/// <summary>Whether the resident script is still polling: either a GetQueries is parked right now,
			/// or one completed recently enough that the next poll must still be in flight.</summary>
			internal bool PollerAlive
				=> Volatile.Read(ref activePollers) > 0
				|| DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPollTicks) < TimeSpan.FromMilliseconds(IdleReplyMs + 5000).Ticks;

			/// <summary>The channel generation a submit runs against; bumped by Reset() so a stale timeout
			/// can't invalidate a channel that was rebuilt while it waited.</summary>
			internal int Generation => Volatile.Read(ref generation);

			internal Task<(bool Ok, string Json)> WaitForJsonAsync(string requestId, int timeoutMs)
				=> pending.WaitAsync(requestId, timeoutMs);

			internal void Enqueue(string requestId, string envelope) => queue.Writer.TryWrite((requestId, envelope));

			/// <summary>Called on a timed-out waiter. True when the envelope had NOT yet been handed to the
			/// script — it is tombstoned so it never will be, making a one-shot retry safe. False when the
			/// script already received it: the operation may still execute (a compositor stall, matching the old
			/// one-shot behavior of a late-landing command), so retrying would risk running it twice.</summary>
			internal bool TryCancel(string requestId)
			{
				lock (stateGate)
				{
					if (delivered.Remove(requestId))
						return false;

					_ = cancelled.Add(requestId);
					return true;
				}
			}

			/// <summary>Retires the current queue before a script (re)load: undelivered envelopes are dropped
			/// (their waiters time out and fall back one-shot), parked polls from the previous script wake with
			/// an empty batch, and the reloaded script starts from a clean, replay-free queue.</summary>
			internal void Reset()
			{
				var old = queue;
				queue = Channel.CreateUnbounded<(string, string)>();
				_ = old.Writer.TryComplete();
				_ = Interlocked.Increment(ref generation);

				lock (stateGate)
				{
					cancelled.Clear();
					delivered.Clear();
				}
			}
		}
	}

	/// <summary>
	/// Thin wrapper around KWin's FakeInput D-Bus interface for mouse simulation.
	/// The first call authenticates with KWin; subsequent calls reuse the proxy.
	/// Authentication is scoped to the current KWin owner and retried as a bounded burst.
	/// </summary>
	internal static class KWinFakeInputBridge
	{
		private const string ServiceName    = "org.kde.KWin";
		private const string FakeInputPath  = "/org/kde/KWin/FakeInput";
		private const int    TimeoutMs      = 1000;

		private static readonly object authLock = new();
		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<IKWinFakeInput> service;
		private static RetryGate authentication;
		private static long authenticatedGeneration = -1;

		static KWinFakeInputBridge() => Initialize();

		internal static bool TryMoveAbsolute(double x, double y)
			=> Run(p => p.pointerMotionAbsoluteAsync(x, y));

		internal static bool TryMoveRelative(double dx, double dy)
			=> Run(p => p.pointerMotionAsync(dx, dy));

		/// <param name="evdevButton">Linux BTN_* code (e.g. 0x110 = left).</param>
		internal static bool TryButton(uint evdevButton, bool pressed)
			=> Run(p => p.pointerButtonAsync(evdevButton, pressed ? 1u : 0u));

		/// <param name="axis">0 = vertical, 1 = horizontal.</param>
		/// <param name="delta">Scroll amount in notch units (positive = down/right).</param>
		internal static bool TryAxis(uint axis, double delta)
			=> Run(p => p.pointerAxisAsync(axis, delta));

		private static bool Run(Func<IKWinFakeInput, Task> call)
		{
			(IKWinFakeInput Proxy, long Generation) target;

			if (!service.TryUse(proxy => (proxy, service.Generation), out target)
				|| !EnsureAuthenticated(target.Proxy, target.Generation))
				return false;

			try
			{
				call(target.Proxy).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs)).GetAwaiter().GetResult();
				return true;
			}
			catch
			{
				// The connection watcher owns transport recovery. An individual input call can fail for
				// operation-specific reasons and must not evict an otherwise healthy bus session.
				return false;
			}
		}

		private static bool EnsureAuthenticated(IKWinFakeInput proxy, long generation)
		{
			lock (authLock)
			{
				if (authenticatedGeneration == generation)
					return true;

				using var attempt = authentication.TryBegin();

				if (attempt == null)
					return false;

				try
				{
					var granted = proxy.authenticateAsync("Keysharp", "Keysharp mouse automation")
						.WaitAsync(TimeSpan.FromMilliseconds(2000))
						.GetAwaiter()
						.GetResult();

					if (!granted)
					{
						authentication.Suspend();
						return false;
					}

					authenticatedGeneration = generation;
					attempt.Succeed();
					return true;
				}
				catch (Exception ex)
				{
					attempt.Fail(ex);
					return false;
				}
			}
		}

		private static void Initialize()
		{
			sessions = new RecoverableService<DbusSession>(ConnectSessionBus);
			service = new WatchedDbusService<IKWinFakeInput>(sessions, ServiceName,
				new ObjectPath(FakeInputPath), TimeoutMs);
			authentication = new RetryGate(maximumAttempts: 3, initialRetryDelay: TimeSpan.FromMilliseconds(250),
				maximumRetryDelay: TimeSpan.FromSeconds(2));
			service.AvailabilityChanged += () =>
			{
				lock (authLock)
					authenticatedGeneration = -1;

				authentication.Rearm();
			};
		}

		private static DbusSession ConnectSessionBus()
		{
			Connection connection = null;

			try
			{
				connection = new Connection(Tmds.DBus.Address.Session);
				var task = connection.ConnectAsync();

				if (!task.WaitWithoutInterruption(TimeoutMs))
					throw new TimeoutException($"Connecting to the D-Bus session bus timed out after {TimeoutMs} ms.");

				var info = task.GetAwaiter().GetResult();
				var session = new DbusSession(connection, info?.LocalName);
				connection.StateChanged += (_, e) =>
				{
					if (e.State == Tmds.DBus.ConnectionState.Disconnected)
						sessions.Invalidate(session, e.DisconnectReason);
				};
				return session;
			}
			catch
			{
				try { connection?.Dispose(); } catch { }
				throw;
			}
		}

		internal static void Reset()
		{
			service.Dispose();
			sessions.Dispose();
			Initialize();
		}
	}
}
#endif
