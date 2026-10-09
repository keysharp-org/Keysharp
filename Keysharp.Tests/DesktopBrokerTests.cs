#if LINUX
using FormWindowState = Eto.Forms.WindowState;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class DesktopBrokerTests
	{
		private LinuxServices services;
		[SetUp] public void SetUp() => services = new();
		[TearDown] public void TearDown() => services.Dispose();

		[Test]
		public void X11UsesPushWindowEventsWhenOffered()
		{
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			var capabilities = typeof(DesktopClient).GetField("capabilities", flags).GetValue(services.Desktop);
			var operation = typeof(DesktopClient).GetNestedType("Operation", BindingFlags.NonPublic);
			capabilities.GetType().GetMethod("Learn", flags).Invoke(capabilities,
				[DesktopClient.Backend.X11, Enum.Parse(operation, "WindowWatch")]);
			Assert.That(services.X11.SupportsPushWindowEvents, Is.True);
		}

		/// <summary>A broker left running by a previous X11 login reports that session, not this one. The
		/// selection only runs on a Wayland session, so an X11 report is a contradiction rather than an
		/// answer, and must not be cached as this script's backend for the rest of its life.</summary>
		[Test]
		public void StaleX11BrokerReportIsRefusedOnWayland()
			=> Assert.That(WaylandBackend.Select(DesktopClient.Backend.X11), Is.Null);

		[TestCase((int)DesktopClient.Backend.Kwin, "kwin")]
		[TestCase((int)DesktopClient.Backend.Gnome, "gnome")]
		[TestCase((int)DesktopClient.Backend.Cinnamon, "cinnamon")]
		[TestCase((int)DesktopClient.Backend.Generic, "generic")]
		public void ReportedBackendSelectsItsHandler(int reported, string expectedKey)
		{
			var backend = WaylandBackend.Select((DesktopClient.Backend)reported);

			Assert.That(backend, Is.Not.Null);
			Assert.That(backend.BackendKey, Is.EqualTo(expectedKey));
			(backend as IDisposable)?.Dispose();
		}

		/// <summary>Only a reply that names the window answers it, and only NotFound says that it is gone.</summary>
		[TestCase((int)NativeClientStatus.Ok, "24", true, false)]
		[TestCase((int)NativeClientStatus.Ok, "25", false, false)]
		[TestCase((int)NativeClientStatus.Ok, null, false, false)]
		[TestCase((int)NativeClientStatus.NotFound, null, false, true)]
		[TestCase((int)NativeClientStatus.Unavailable, null, false, false)]
		[TestCase((int)NativeClientStatus.Denied, null, false, false)]
		public void WindowRepliesSayWhetherTheWindowIsGone(int status, string id, bool found, bool gone)
		{
			byte[] json = id == null ? [] : Encoding.UTF8.GetBytes(
				$"{{\"ok\":true,\"window\":{{\"id\":\"{id}\",\"validFields\":[\"id\"]}}}}");
			Assert.Multiple(() =>
			{
				Assert.That(services.X11.TryReadWindow(24, json, (NativeClientStatus)status, out var window,
					out var notFound), Is.EqualTo(found));
				Assert.That(window != null, Is.EqualTo(found));
				Assert.That(notFound, Is.EqualTo(gone));
			});
		}

		[TestCase((int)NativeClientStatus.Ok, 0, false, false)]
		[TestCase((int)NativeClientStatus.Unavailable, 0, true, false)]
		[TestCase((int)NativeClientStatus.Timeout, 110, true, false)]
		[TestCase((int)NativeClientStatus.Timeout, 0, true, true)]
		[TestCase((int)NativeClientStatus.Denied, 13, false, false)]
		[TestCase((int)NativeClientStatus.Revoked, 0, false, false)]
		public void NativeResultDistinguishesConnectionFailuresFromPollTimeouts(
			int statusCode, int systemError, bool shouldReconnect,
			bool isExpectedPollTimeout)
		{
			var result = new DesktopClient.CallResult((NativeClientStatus)statusCode, 0, systemError,
				string.Empty, "test operation");

			Assert.Multiple(() =>
			{
				Assert.That(result.ShouldReconnect, Is.EqualTo(shouldReconnect));
				Assert.That(result.IsExpectedPollTimeout, Is.EqualTo(isExpectedPollTimeout));
			});
		}

		[Test]
		public void X11SnapshotsPreserveNativeHandlesAndClientGeometry()
		{
			const string json = """
				{"ok":true,"windows":[{"id":"4026531841","title":"Café","appId":"Editor",
				"pid":123,"frame":{"x":-900,"y":-20,"width":600,"height":400},
				"client":{"x":-894,"y":10,"width":588,"height":364},
				"visible":true,"decorated":true,"transparency":127,
				"validFields":["frame","client","title","appId","visible","transparency"]}]}
				""";
			Assert.That(services.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
				out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			var window = windows[0];
			Assert.Multiple(() =>
			{
				Assert.That(window.Handle.ToInt64(), Is.EqualTo(4026531841L));
				Assert.That(window.CompositorId, Is.EqualTo("4026531841"));
				Assert.That(window.Title, Is.EqualTo("Café"));
				Assert.That(window.ClassName, Is.EqualTo("Editor"));
				Assert.That(window.Bounds.X, Is.EqualTo(-900));
				Assert.That(window.ClientBounds.Width, Is.EqualTo(588));
				Assert.That(window.ClientToScreen().Y, Is.EqualTo(10));
				Assert.That(window.Transparency, Is.EqualTo(127L));
				Assert.That(window.PID, Is.Zero,
					"a pid placeholder omitted from validFields must remain unknown");
			});
		}

		[Test]
		public void ProviderSnapshotsDeclareEveryValueTheyEmit()
		{
			var fixtures = new[]
			{
				(Name: "X11 append_window", Backend: services.X11, WorkspaceKnown: true, Json:
					"{\"ok\":true,\"windows\":[{\"id\":\"24\",\"title\":\"Editor\",\"appId\":\"Example.Editor\",\"pid\":123,\"frame\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"client\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"active\":true,\"minimized\":true,\"maximized\":false,\"visible\":false,\"alwaysOnTop\":true,\"decorated\":false,\"transparency\":127,\"onCurrentWorkspace\":false,\"validFields\":[\"frame\",\"client\",\"visible\",\"transparency\",\"title\",\"appId\",\"pid\",\"minimized\",\"maximized\",\"alwaysOnTop\",\"active\",\"decorated\",\"onCurrentWorkspace\"]}]}"),
				(Name: "GNOME _windowInfo", Backend: new DesktopBackend("gnome-test", "GNOME"), WorkspaceKnown: true, Json:
					"{\"ok\":true,\"windows\":[{\"id\":\"24\",\"title\":\"Editor\",\"appId\":\"Example.Editor\",\"pid\":123,\"frame\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"client\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"buffer\":null,\"active\":true,\"minimized\":true,\"maximized\":false,\"visible\":false,\"alwaysOnTop\":true,\"decorated\":false,\"transparency\":127,\"onCurrentWorkspace\":false,\"validFields\":[\"id\",\"title\",\"appId\",\"frame\",\"client\",\"active\",\"minimized\",\"maximized\",\"visible\",\"alwaysOnTop\",\"transparency\",\"pid\",\"decorated\",\"onCurrentWorkspace\"]}]}"),
				(Name: "Cinnamon _windowInfo", Backend: new DesktopBackend("cinnamon-test", "Cinnamon"), WorkspaceKnown: true, Json:
					"{\"ok\":true,\"windows\":[{\"id\":\"24\",\"title\":\"Editor\",\"appId\":\"Example.Editor\",\"pid\":123,\"frame\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"client\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"buffer\":null,\"active\":true,\"minimized\":true,\"maximized\":false,\"visible\":false,\"alwaysOnTop\":true,\"decorated\":false,\"transparency\":127,\"workspace\":2,\"monitor\":1,\"onCurrentWorkspace\":false,\"validFields\":[\"id\",\"title\",\"appId\",\"frame\",\"client\",\"active\",\"minimized\",\"maximized\",\"visible\",\"alwaysOnTop\",\"transparency\",\"pid\",\"decorated\",\"onCurrentWorkspace\",\"workspace\",\"monitor\"]}]}"),
				(Name: "KWin windowJson", Backend: new DesktopBackend("kwin-test", "KWin"), WorkspaceKnown: false, Json:
					"{\"ok\":true,\"windows\":[{\"validFields\":[\"id\",\"title\",\"captureId\",\"appId\",\"pid\",\"frame\",\"client\",\"minimized\",\"maximized\",\"active\",\"visible\",\"alwaysOnTop\",\"decorated\",\"transparency\"],\"id\":\"24\",\"captureId\":\"capture-24\",\"title\":\"Editor\",\"appId\":\"Example.Editor\",\"pid\":123,\"frame\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"client\":{\"x\":1,\"y\":2,\"width\":300,\"height\":200},\"minimized\":true,\"active\":true,\"maximized\":false,\"visible\":false,\"alwaysOnTop\":true,\"decorated\":false,\"transparency\":127}]}"),
			};

			foreach (var fixture in fixtures)
			{
				Assert.That(fixture.Backend.TryParseWindowList(Encoding.UTF8.GetBytes(fixture.Json),
					out var windows), Is.True, fixture.Name);
				Assert.That(windows, Has.Count.EqualTo(1), fixture.Name);
				var window = windows[0];
				Assert.Multiple(() =>
				{
					Assert.That(window.ClientBounds, Is.EqualTo(new Rectangle(1, 2, 300, 200)), fixture.Name);
					Assert.That(window.TryGetBounds(true, out var bounds), Is.True, fixture.Name);
					Assert.That(bounds, Is.EqualTo(window.ClientBounds), fixture.Name);
					Assert.That(window.Active, Is.True, fixture.Name);
					Assert.That(window.Visible, Is.False, fixture.Name);
					Assert.That(window.Decorated, Is.False, fixture.Name);
					Assert.That(window.Transparency, Is.EqualTo(127L), fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.Client), Is.True, fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.Active), Is.True, fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.Visible), Is.True, fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.Decorated), Is.True, fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.Transparency), Is.True, fixture.Name);
					Assert.That(window.HasKnownField(WaylandWindowFields.OnCurrentWorkspace),
						Is.EqualTo(fixture.WorkspaceKnown), fixture.Name);
					Assert.That(window.OnCurrentWorkspace, Is.EqualTo(!fixture.WorkspaceKnown), fixture.Name);
				});
			}
		}

		[Test]
		public void WindowSnapshotsIgnorePlaceholdersOutsideValidFields()
		{
			const string json = """
				{"ok":true,"windows":[{"id":"24","title":"Editor","appId":"Example.Editor",
				"pid":999,"frame":{"x":1,"y":2,"width":300,"height":200},
				"client":{"x":3,"y":4,"width":290,"height":180},
				"buffer":{"x":5,"y":6,"width":280,"height":160},
				"active":true,"minimized":true,"maximized":true,"visible":false,
				"alwaysOnTop":true,"decorated":false,"transparency":127,
				"onCurrentWorkspace":false,
				"validFields":["id","title","appId"]}]}
				""";

			Assert.That(services.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
				out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			var window = windows[0];
			Assert.Multiple(() =>
			{
				Assert.That(window.Title, Is.EqualTo("Editor"));
				Assert.That(window.ClassName, Is.EqualTo("Example.Editor"));
				Assert.That(window.PID, Is.Zero);
				Assert.That(window.ProcessName, Is.Empty);
				Assert.That(window.Path, Is.Empty);
				Assert.That(window.Bounds, Is.EqualTo(Rectangle.Empty));
				Assert.That(window.ClientBounds, Is.EqualTo(Rectangle.Empty));
				Assert.That(window.TryGetBounds(false, out _), Is.False);
				Assert.That(window.TryGetBounds(true, out _), Is.False);
				Assert.That(window.SurfaceGeometry, Is.EqualTo(Rectangle.Empty));
				Assert.That(window.Active, Is.False);
				Assert.That(window.Visible, Is.True);
				Assert.That(window.AlwaysOnTop, Is.False);
				Assert.That(window.WindowState, Is.EqualTo(FormWindowState.Normal));
				Assert.That(window.Decorated, Is.True);
				Assert.That(window.Transparency, Is.EqualTo(-1L));
				Assert.That(window.OnCurrentWorkspace, Is.True);
			});
		}

		[TestCase(true, true)]
		[TestCase(true, false)]
		[TestCase(false, true)]
		[TestCase(false, false)]
		public void WindowProcessImageWithoutIdentity(bool nameOnly, bool placeholder)
		{
			var json = """
				{"ok":true,"windows":[{"id":"24","title":"Editor","appId":"Example.Editor",
				"pid":999,"validFields":["id","title","appId"]}]}
				""";
			if (!placeholder)
				json = json.Replace("\"pid\":999,", "");
			var backend = new DesktopBackend("generic", "generic Wayland");
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(json), out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			Assert.That(Keysharp.Builtins.WindowX.ProcessImageOrError(windows[0], nameOnly), Is.Empty);
		}

		[TestCase("generic", "focused")]
		[TestCase("gnome", "background")]
		public void WindowAtPreference(string key, string expected)
		{
			const string json = """
				{"ok":true,"windows":[
				{"id":"focused","active":true,"frame":{"x":0,"y":0,"width":100,"height":100},"validFields":["active","frame"]},
				{"id":"background","frame":{"x":-50,"y":-50,"width":200,"height":200},"validFields":["frame"]}]}
				""";
			var backend = new DesktopBackend(key, key);
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(json), out var windows), Is.True);
			Assert.That(backend.FindWindowAt(windows, 20, 20)?.CompositorId, Is.EqualTo(expected));
			Assert.That(backend.FindWindowAt(windows, -20, -20)?.CompositorId, Is.EqualTo("background"));
			Assert.That(backend.FindWindowAt(windows, 500, 500), Is.Null);
		}

		[Test]
		public void WindowAtExclusions([Values("generic", "gnome")] string key,
			[Values("minimized", "hidden", "other-workspace", "unknown-frame")] string state)
		{
			var bounds = new Rectangle(0, 0, 100, 100);
			var visible = new WaylandWindowInfo(new nint(1), frameGeometry: bounds, visible: true);
			var excluded = new WaylandWindowInfo(new nint(2), frameGeometry: bounds, active: true,
				minimized: state == "minimized", visible: state != "hidden", onCurrentWorkspace: state != "other-workspace",
				knownFields: state == "unknown-frame" ? WaylandWindowFields.All & ~WaylandWindowFields.Frame : WaylandWindowFields.All);
			var backend = new DesktopBackend(key, key);
			Assert.That(backend.FindWindowAt([visible, excluded], 20, 20), Is.SameAs(visible));
			Assert.That(backend.FindWindowAt([excluded], 20, 20), Is.Null);
		}

		[TestCase("null")]
		[TestCase("[]")]
		[TestCase("{\"ok\":false,\"windows\":[]}")]
		[TestCase("{\"ok\":true,\"windows\":null}")]
		public void InvalidWindowRepliesFailClosed(string json)
			=> Assert.That(services.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json), out _),
				Is.False);

		[Test]
		public void InvalidHandlesAndGeometryDoNotWrap()
		{
			const string json = """
				{"ok":true,"windows":[null,{"id":"-1"},{"id":"4294967296"},
				{"id":"24","frame":{"x":4294967297,"y":0,"width":10,"height":20}}]}
				""";
			Assert.That(services.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
				out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			Assert.That(windows[0].Bounds.Width, Is.Zero);
		}

		[Test]
		public void SnapshotParentWrappersAreMemoized()
		{
			const string json =
				"{\"ok\":true,\"window\":{\"id\":\"12\",\"parent\":\"24\",\"topLevel\":\"42\",\"validFields\":[\"id\"]}}";
			Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(json),
				id => new nint(long.Parse(id, CultureInfo.InvariantCulture)), out var window), Is.True);

			Assert.Multiple(() =>
			{
				Assert.That(window.ParentWindow, Is.SameAs(window.ParentWindow));
				Assert.That(window.ParentWindow.Handle, Is.EqualTo(new nint(24)));
				Assert.That(window.NonChildParentWindow, Is.SameAs(window.NonChildParentWindow));
				Assert.That(window.NonChildParentWindow.Handle, Is.EqualTo(new nint(42)));
			});
		}

		[Test]
		public void ProviderWindowEventsParseWithoutRpcEnvelope()
		{
			const string json = """
				{"id":"24","title":"Editor","active":true,
				"frame":{"x":1,"y":2,"width":300,"height":200},
				"validFields":["id","title","active","frame"]}
				""";

			using var document = System.Text.Json.JsonDocument.Parse(json);
			Assert.That(DesktopWindowParser.TryParse(document.RootElement,
				id => new nint(long.Parse(id, CultureInfo.InvariantCulture)), out var window), Is.True);
			Assert.Multiple(() =>
			{
				Assert.That(window.Handle, Is.EqualTo(new nint(24)));
				Assert.That(window.Title, Is.EqualTo("Editor"));
				Assert.That(window.Active, Is.True);
				Assert.That(window.Bounds, Is.EqualTo(new Rectangle(1, 2, 300, 200)));
			});
		}

		[Test]
		public void GenericSnapshotsRetainOpaqueIdentity()
		{
			const string json = """
				{"ok":true,"windows":[{"id":"ext-toplevel:editor","title":"Editor",
				"appId":"example.editor","validFields":["id","title","appId"]}]}
				""";
			var backend = new DesktopBackend("generic", "generic Wayland");

			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(json), out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			Assert.That(backend.TryGetNativeWindowId(windows[0].Handle, out var nativeId), Is.True);
			Assert.Multiple(() =>
			{
				Assert.That(backend.IsKnown(windows[0].Handle), Is.True);
				Assert.That(nativeId, Is.EqualTo("ext-toplevel:editor"));
				Assert.That(windows[0].Title, Is.EqualTo("Editor"));
				Assert.That(windows[0].ClassName, Is.EqualTo("example.editor"));
			});
		}

		[TestCase("generic", "c811cb87-a9bf-4207-bfd6-382d9c0b74f9")]
		[TestCase("kwin", "{c811cb87-a9bf-4207-bfd6-382d9c0b74f9}")]
		public void CaptureIdsFollowWindowLifetime(string key, string expected)
		{
			const string json = """
				{"ok":true,"windows":[{"id":"24","captureId":"c811cb87-a9bf-4207-bfd6-382d9c0b74f9",
				"validFields":["id","captureId"]}]}
				""";
			var backend = key == "kwin" ? new KWinBrokerBackend() : new DesktopBackend(key, key);
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(json), out var windows), Is.True);
			var handle = windows[0].Handle;
			Assert.That(backend.TryGetNativeWindowId(handle, out var captureId), Is.True);
			Assert.That(captureId, Is.EqualTo(expected));
			Assert.That(windows[0].CompositorId, Is.EqualTo("24"));
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes("{\"ok\":true,\"windows\":[]}"), out _), Is.True);
			Assert.That(backend.TryGetNativeWindowId(handle, out _), Is.False);
		}

		[Test]
		public void ReservedWindowHandlesSurviveSnapshotsUntilPublication()
		{
			var backend = new DesktopBackend("reservation-test", "test");
			var empty = Encoding.UTF8.GetBytes("{\"ok\":true,\"windows\":[]}");
			backend.RememberReservation(1, 60_000);
			Assert.That(backend.TryReadReservedWindow(1, "24", out var reserved), Is.True);
			Assert.That(backend.TryParseWindowList(empty, out _), Is.True);
			Assert.That(backend.TryParseWindowList(empty, out _), Is.True);
			Assert.That(backend.IsKnown(reserved), Is.True);
			Assert.That(backend.TryGetNativeWindowId(reserved, out var id), Is.True);
			Assert.That(id, Is.EqualTo("24"));

			var published = Encoding.UTF8.GetBytes("{\"ok\":true,\"windows\":[{\"id\":\"24\",\"validFields\":[\"id\"]}]}");
			Assert.That(backend.TryParseWindowList(published, out var windows), Is.True);
			Assert.That(windows[0].Handle, Is.EqualTo(reserved));
			Assert.That(backend.TryParseWindowList(empty, out _), Is.True);
			Assert.That(backend.IsKnown(reserved), Is.False);

			Assert.That(backend.TryReadReservedWindow(1, "24", out _), Is.True);
			Assert.That(backend.TryParseWindowList(empty, out _), Is.True);
			Assert.That(backend.IsKnown(reserved), Is.False, "reading a consumed reservation must not renew its pin");
		}

		[Test]
		public void ExpiredReservationsDoNotPinWindowHandles()
		{
			var backend = new DesktopBackend("reservation-expiry-test", "test");
			backend.RememberReservation(1, 0);
			Assert.That(backend.TryReadReservedWindow(1, "24", out var reserved), Is.True);
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes("{\"ok\":true,\"windows\":[]}"), out _), Is.True);
			Assert.That(backend.IsKnown(reserved), Is.False);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void TypedWindowRecordsRespectValidFields(bool known)
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var type = typeof(DesktopClient).GetNestedType("NativeWindowRecord", BindingFlags.NonPublic);
			var record = Activator.CreateInstance(type);
			void Set(string name, object value) => type.GetField(name, flags).SetValue(record, value);
			Set("Handle", 42UL);
			Set("ValidFields", known ? 0x9ffffUL : 131072UL); //A pixel buffer alone supplies no surface origin.
			Set("Flags", 30u);
			Set("Pid", 123u);
			Set("Transparency", 127u);
			Set("FrameWidth", known ? 300u : uint.MaxValue);
			Set("FrameHeight", 200u);
			Set("SurfaceWidth", known ? 300u : uint.MaxValue);
			Set("SurfaceHeight", 200u);
			Set("Parent", 7UL);
			Set("StackingOrder", 9UL);
			var window = (WaylandWindowInfo)typeof(DesktopClient).GetMethod("ReadWindow",
				BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [record]);
			Assert.Multiple(() =>
			{
				Assert.That(window.PID, Is.EqualTo(known ? 123L : 0L));
				Assert.That(window.Active, Is.EqualTo(known));
				Assert.That(window.Minimized, Is.EqualTo(known));
				Assert.That(window.Maximized, Is.EqualTo(known));
				Assert.That(window.AlwaysOnTop, Is.EqualTo(known));
				Assert.That(window.Visible, Is.EqualTo(!known));
				Assert.That(window.Decorated, Is.EqualTo(!known));
				Assert.That(window.OnCurrentWorkspace, Is.EqualTo(!known));
				Assert.That(window.Transparency, Is.EqualTo(known ? 127L : -1L));
				Assert.That(window.Bounds, Is.EqualTo(known ? new Rectangle(0, 0, 300, 200) : Rectangle.Empty));
				Assert.That(window.SurfaceGeometry, Is.EqualTo(known ? new Rectangle(0, 0, 300, 200) : Rectangle.Empty));
				Assert.That(window.ServiceParentHandle, Is.EqualTo(known ? 7UL : 0UL));
				Assert.That(window.StackingOrder, Is.EqualTo(known ? 9UL : 0UL));
			});
		}

		[Test]
		public void FullWindowDeltaPreservesCombinedEvents()
		{
			var mirror = Snapshot();
			var minimized = new WaylandWindowInfo(42, title: "first", visible: false, minimized: true);
			var events = new List<WaylandWindowEventKind>();
			void Dispatch(uint kind, WaylandWindowInfo current, WaylandWindowInfo previous)
			{
				events.Clear();
				DesktopClient.DispatchWindowEvents(kind, current, previous, (change, _) => events.Add(change));
			}
			Assert.That(mirror.Apply(7, 1, 11, minimized, out var before), Is.True);
			Dispatch(7, minimized, before);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Hidden, WaylandWindowEventKind.Minimized }));
			var restored = Window();
			Assert.That(mirror.Apply(6, 1, 12, restored, out before), Is.True);
			Assert.That(before, Is.SameAs(minimized));
			Dispatch(6, restored, before);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Shown, WaylandWindowEventKind.Restored }));
			Dispatch(11, minimized, restored);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Hidden, WaylandWindowEventKind.Minimized }));
			var moved = new WaylandWindowInfo(42, title: "edited", visible: true, active: true,
				frameGeometry: new Rectangle(1, 2, 3, 4));
			Dispatch(8, moved, restored);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.MoveResized,
				WaylandWindowEventKind.Activated, WaylandWindowEventKind.TitleChanged }));
			Dispatch(5, restored, minimized);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Closed }));
			Dispatch(11, minimized, new WaylandWindowInfo(42, knownFields: WaylandWindowFields.None));
			Assert.That(events, Is.Empty, "unknown flags cannot invent a transition");
		}

		[Test]
		public void SnapshotRefreshPreservesWindowEvents()
		{
			var mirror = new DesktopWindowMirror();
			var events = new List<WaylandWindowEventKind>();
			ulong sequence = 0;
			void Refresh(params WaylandWindowInfo[] windows)
			{
				events.Clear();
				Assert.That(mirror.Apply(1, 1, ++sequence, null, out _), Is.True);
				foreach (var window in windows)
					Assert.That(mirror.Apply(2, 1, sequence, window, out _), Is.True);
				Assert.That(mirror.Apply(3, 1, sequence, null, out _, out var changes), Is.True);
				foreach (var change in changes)
					DesktopClient.DispatchWindowEvents(change.Kind, change.Window, change.Previous,
						(kind, _) => events.Add(kind));
			}
			Refresh(Window());
			Assert.That(events, Is.Empty, "the initial snapshot seeds state without reporting existing windows");
			Refresh(Window());
			Assert.That(events, Is.Empty, "an unchanged compositor refresh must stay silent");
			var minimized = new WaylandWindowInfo(42, title: "edited", visible: true, minimized: true);
			Refresh(minimized);
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.TitleChanged, WaylandWindowEventKind.Minimized }));
			Assert.That(mirror.TryRead(out var current), Is.True);
			Assert.That(current.Single(), Is.SameAs(minimized), "refresh events describe committed window state");
			Refresh(minimized);
			Assert.That(events, Is.Empty, "replaying a changed snapshot must not repeat its events");
			Refresh(Window(title: "edited"));
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Restored }));
			Refresh(new WaylandWindowInfo(42, title: "unknown", minimized: true, knownFields: WaylandWindowFields.None));
			Assert.That(events, Is.Empty, "unknown fields cannot invent snapshot transitions");
			Refresh(Window(), new WaylandWindowInfo(43));
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Created }));
			Refresh(Window());
			Assert.That(events, Is.EqualTo(new[] { WaylandWindowEventKind.Closed }));
			mirror.Invalidate();
			Refresh(Window(title: "recovered"));
			Assert.That(events, Is.Empty, "a fresh stream must seed state again after invalidation");
		}

		[Test]
		public void IdOnlyWindowCloseKeepsItsPreviousLifetimeIdentity()
		{
			const string json = """
				{"ok":true,"window":{"id":"4200","compositorId":"0xabcd",
				"frame":{"x":10,"y":20,"width":200,"height":100}}}
				""";
			Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(json),
				_ => new nint(42), out var full), Is.True);
			try
			{
				BindOwnEventWindow(full, 101);
				var mirror = new DesktopWindowMirror();
				mirror.Apply(1, 1, 0, null, out _);
				mirror.Apply(2, 1, 0, full, out _);
				mirror.Apply(3, 1, 0, null, out _);
				var closed = new WaylandWindowInfo(0, compositorId: "4200",
					serviceHandle: 4200, knownFields: WaylandWindowFields.None);
				Assert.That(mirror.Apply(5, 1, 1, closed, out var previous), Is.True);
				Assert.That(previous, Is.SameAs(full));
				DesktopClient.DispatchWindowEvents(5, closed, previous, (kind, reported) =>
				{
					Assert.That(kind, Is.EqualTo(WaylandWindowEventKind.Closed));
					Assert.That(reported, Is.SameAs(full), "An id-only close retains the previous lifetime identity.");
				});
				Assert.That(WaylandOwnToplevels.ResolveEventHandle(full.Handle), Is.EqualTo(new nint(101)));
				WaylandOwnToplevels.RetireEventAlias(previous ?? closed);
				Assert.That(WaylandOwnToplevels.ResolveEventHandle(full.Handle), Is.EqualTo(full.Handle));
			}
			finally { WaylandOwnToplevels.Reset(); }
		}

		[Test]
		public void CloseIdentitySurvivesSnapshotPruning()
		{
			const string json = """
				{"ok":true,"windows":[{"id":"4200","compositorId":"0xabcd",
				"frame":{"x":10,"y":20,"width":200,"height":100}}]}
				""";
			var backend = new DesktopBackend("close-test", "test");
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(json), out var windows), Is.True);
			var original = windows.Single();
			try
			{
				BindOwnEventWindow(original, 101);
				var empty = Encoding.UTF8.GetBytes("{\"ok\":true,\"windows\":[]}");
				backend.TryParseWindowList(empty, out _);
				backend.TryParseWindowList(empty, out _);
				Assert.That(backend.IsKnown(original.Handle), Is.False);
				var closed = new WaylandWindowInfo(0, serviceHandle: 4200, knownFields: WaylandWindowFields.None);
				var normalized = backend.ResolveWindowEvent(WaylandWindowEventKind.Closed, closed);
				Assert.That(normalized.Handle, Is.EqualTo(new nint(101)),
					"A queued close keeps the form handle after queries retire its synthetic handle.");
				WaylandOwnToplevels.RetireEventAlias(original);
				Assert.That(backend.ResolveWindowEvent(WaylandWindowEventKind.Closed, closed).Handle,
					Is.Not.EqualTo(new nint(101)));
			}
			finally { WaylandOwnToplevels.Reset(); }
		}

		[Test]
		public void PollingCloseRetiresOwnIdentityAfterCapture()
		{
			var original = new WaylandWindowInfo(42, "poll-first", serviceHandle: 4200,
				frameGeometry: new Rectangle(10, 20, 200, 100));
			var tracker = new WaylandWindowSnapshotTracker();
			try
			{
				BindOwnEventWindow(original, 101);
				tracker.Update([original], _ => { }, WaylandOwnToplevels.RetireEventAlias);
				tracker.Update([new WaylandWindowInfo(42, "4200", serviceHandle: 4200,
					knownFields: WaylandWindowFields.None)], _ => { }, WaylandOwnToplevels.RetireEventAlias);
				tracker.Update([], windowEvent =>
				{
					Assert.That(windowEvent.Kind, Is.EqualTo(WaylandWindowEventKind.Closed));
					Assert.That(WaylandOwnToplevels.ResolveEventHandle(windowEvent.Handle), Is.EqualTo(new nint(101)));
				}, WaylandOwnToplevels.RetireEventAlias);
				Assert.That(WaylandOwnToplevels.ResolveEventHandle(original.Handle), Is.EqualTo(original.Handle));

				var oldMap = new WaylandWindowInfo(42, "poll-second", serviceHandle: 4201,
					frameGeometry: original.FrameGeometry);
				var newer = new WaylandWindowInfo(42, "poll-third", serviceHandle: 4202,
					frameGeometry: original.FrameGeometry);
				BindOwnEventWindow(oldMap, 102);
				tracker.Update([oldMap], _ => { }, WaylandOwnToplevels.RetireEventAlias);
				tracker.Update([], windowEvent =>
				{
					Assert.That(WaylandOwnToplevels.ResolveEventHandle(windowEvent.Handle), Is.EqualTo(new nint(102)));
					BindOwnEventWindow(newer, 103);
				}, WaylandOwnToplevels.RetireEventAlias);
				Assert.That(WaylandOwnToplevels.ResolveEventHandle(newer.Handle), Is.EqualTo(new nint(103)),
					"closing an older map cannot retire a newer alias");
			}
			finally { WaylandOwnToplevels.Reset(); }
		}

		private static void BindOwnEventWindow(WaylandWindowInfo window, nint formHandle)
		{
			// Exercise map identity without creating a GTK window or contacting a compositor.
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			var stateType = typeof(WaylandOwnToplevels).GetNestedType("FormState", BindingFlags.NonPublic);
			var state = Activator.CreateInstance(stateType, nonPublic: true);
			stateType.GetField("FormHandle", flags).SetValue(state, formHandle);
			stateType.GetField("Mapped", flags).SetValue(state, true);
			stateType.GetField("MapGeneration", flags).SetValue(state, 1);
			var claim = typeof(WaylandOwnToplevels).GetMethod("Claim", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(claim.Invoke(null, [state, 1, window]), Is.SameAs(window));
		}

		[Test]
		public void RpcRetirementPreservesANewerLease()
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var type = typeof(DesktopClient).GetNestedType("DesktopRpcSession", BindingFlags.NonPublic);
			var session = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, [services.Desktop], null);
			var lease = type.GetField("leaseId", flags);
			lease.SetValue(session, 99UL);
			var stop = type.GetMethod("Dispose", flags);
			stop.Invoke(session, [42UL]);
			Assert.That(lease.GetValue(session), Is.EqualTo(99UL));
			Assert.That(type.GetField("generation", flags).GetValue(session), Is.EqualTo(0));
			stop.Invoke(session, [99UL]);
			Assert.That(lease.GetValue(session), Is.EqualTo(0UL));
		}

		[Test]
		public void RpcSessionShutdownBypassesABlockedRequest()
		{
			var type = typeof(DesktopClient).GetNestedType("DesktopRpcSession", BindingFlags.NonPublic);
			var session = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, [services.Desktop], null);
			var requestLock = type.GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var request = Task.Run(() => { lock (requestLock) { entered.Set(); return release.Wait(2_000); } });
			try
			{
				Assert.That(entered.Wait(1_000), Is.True);
				var stop = Task.Run(() => type.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, [0UL]));
				Assert.That(stop.Wait(500), Is.True, "Shutdown must retire the channel while its native RPC is pending.");
			}
			finally { release.Set(); }
			Assert.That(request.Wait(1_000), Is.True);
		}

		[Test]
		public void GrantSequenceRejectsGapsAndUnannouncedRestarts()
		{
			var cursor = new DesktopClient.GrantCursor();
			Assert.That(cursor.Apply(12, 1, 1), Is.False);
			Assert.That(cursor.Apply(1, 1, 10), Is.True);
			Assert.That(cursor.Apply(2, 1, 10), Is.True);
			Assert.That(cursor.Apply(3, 1, 10), Is.True);
			Assert.That(cursor.Apply(12, 1, 11), Is.True);
			Assert.That(cursor.Apply(12, 1, 13), Is.False);
			Assert.That(cursor.Apply(12, 2, 12), Is.False);
			Assert.That(cursor.Apply(12, 0, 0), Is.False);
			Assert.That(cursor.Apply(1, 2, 20), Is.True);
			Assert.That(cursor.Apply(3, 2, 20), Is.True);
			Assert.That(cursor.Apply(12, 2, 21), Is.True);
		}

		private static WaylandWindowInfo Window(bool visible = true, string title = "first")
			=> new(42, title: title, visible: visible);

		private static DesktopWindowMirror Snapshot(ulong epoch = 1, ulong sequence = 10)
		{
			var mirror = new DesktopWindowMirror();
			Assert.That(mirror.Apply(1, epoch, sequence, null, out _), Is.True);
			Assert.That(mirror.Apply(2, epoch, sequence, Window(), out _), Is.True);
			Assert.That(mirror.TryRead(out _), Is.False, "a partial snapshot cannot answer a query");
			Assert.That(mirror.Apply(3, epoch, sequence, null, out _), Is.True);
			return mirror;
		}

		[Test]
		public void SnapshotThenDeltas()
		{
			var mirror = Snapshot();
			Assert.That(mirror.TryRead(out var windows), Is.True);
			Assert.That(windows.Single().Title, Is.EqualTo("first"));
			Assert.That(mirror.Apply(9, 1, 11, Window(title: "second"), out _), Is.True);
			Assert.That(mirror.TryRead(out windows), Is.True);
			Assert.That(windows.Single().Title, Is.EqualTo("second"));
		}

		[Test]
		public void HideKeepsWindowUntilDestroy()
		{
			var mirror = Snapshot();
			Assert.That(mirror.Apply(7, 1, 11, Window(false), out _), Is.True);
			Assert.That(mirror.TryRead(out var windows), Is.True);
			Assert.That(windows.Single().Visible, Is.False);
			Assert.That(mirror.Apply(6, 1, 12, Window(), out _), Is.True);
			Assert.That(mirror.TryRead(out windows), Is.True);
			Assert.That(windows.Single().Visible, Is.True);
			Assert.That(mirror.Apply(5, 1, 13, Window(), out _), Is.True);
			Assert.That(mirror.TryRead(out windows), Is.True);
			Assert.That(windows, Is.Empty);
			Assert.That(mirror.KnowsWindow(42), Is.True, "a destroyed toplevel must not fall back to an X11 child query");
			Assert.That(mirror.KnowsWindow(43), Is.False, "an X11 child can be queried without belonging to the toplevel snapshot");
		}

		[TestCase(1UL, 12UL)]
		[TestCase(2UL, 11UL)]
		public void GapOrRestartRequiresSnapshot(ulong epoch, ulong sequence)
		{
			var mirror = Snapshot();
			Assert.That(mirror.Apply(9, epoch, sequence, Window(title: "untrusted"), out _), Is.False);
			Assert.That(mirror.TryRead(out _), Is.False);
			Assert.That(mirror.Apply(1, 2, 20, null, out _), Is.True);
			Assert.That(mirror.Apply(2, 2, 20, Window(title: "recovered"), out _), Is.True);
			Assert.That(mirror.Apply(3, 2, 20, null, out _), Is.True);
			Assert.That(mirror.TryRead(out var windows), Is.True);
			Assert.That(windows.Single().Title, Is.EqualTo("recovered"));
		}

		[Test]
		public void ReadAfterWriteWaitsForAcknowledgedSequence()
		{
			var mirror = Snapshot();
			using var started = new ManualResetEventSlim();
			var wait = Task.Run(() => { started.Set(); return mirror.WaitUntil(1, 11, 1_000); });
			Assert.That(started.Wait(1_000), Is.True);
			Assert.That(wait.IsCompleted, Is.False);
			Assert.That(mirror.Apply(9, 1, 11, Window(title: "written"), out _), Is.True);
			Assert.That(wait.Wait(1_000), Is.True);
			Assert.That(wait.Result, Is.True);
			Assert.That(mirror.WaitUntil(1, 12, 20), Is.False, "a missing delta has a bounded wait");
			mirror.Invalidate();
			Assert.That(mirror.WaitUntil(1, 11, 1_000), Is.False, "old epoch acknowledgements cannot satisfy a new connection");
		}

		[Test]
		public void SnapshotOrderRejectsMismatchedSequence()
		{
			var mirror = Snapshot();
			Assert.That(mirror.Apply(1, 1, 12, null, out _), Is.True);
			Assert.That(mirror.Apply(2, 1, 13, Window(), out _), Is.False);
			Assert.That(mirror.TryRead(out _), Is.False);
		}

		[Test]
		public void ServiceIdentifiersKeepTheirFullWidthAndStackingOrder()
		{
			Assert.That(DesktopWindowParser.TrySingle(
				Encoding.UTF8.GetBytes("{\"ok\":true,\"window\":{\"id\":\"4294967338\"}}"),
				_ => new nint(24), out var queried), Is.True);
			Assert.That(queried.Handle, Is.EqualTo(new nint(24)));
			Assert.That(queried.ServiceHandle, Is.EqualTo(0x10000002AUL));
			var mirror = new DesktopWindowMirror();
			mirror.Apply(1, 1, 0, null, out _);
			mirror.Apply(2, 1, 0, new WaylandWindowInfo(0, serviceHandle: 0x10000002A, stackingOrder: 2), out _);
			mirror.Apply(2, 1, 0, new WaylandWindowInfo(0, serviceHandle: 42, stackingOrder: 1), out _);
			mirror.Apply(3, 1, 0, null, out _);
			Assert.That(mirror.TryRead(out var windows), Is.True);
			Assert.That(windows.Select(window => window.ServiceHandle), Is.EqualTo(new[] { 42UL, 0x10000002AUL }));
		}
	}
}
#endif
