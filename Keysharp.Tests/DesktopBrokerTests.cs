#if LINUX
using Keysharp.Internals.Linux;
using Keysharp.Internals.Window.Linux.Wayland;
using FormWindowState = Eto.Forms.WindowState;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class DesktopBrokerTests
	{
		[TestCase((int)NativeClientStatus.Ok, 0, false, false, true)]
		[TestCase((int)NativeClientStatus.Unavailable, 0, true, false, false)]
		[TestCase((int)NativeClientStatus.Timeout, 110, true, false, false)]
		[TestCase((int)NativeClientStatus.Timeout, 0, false, true, true)]
		[TestCase((int)NativeClientStatus.Denied, 13, false, false, false)]
		[TestCase((int)NativeClientStatus.Revoked, 0, false, false, false)]
		public void NativeResultDistinguishesConnectionFailuresFromPollTimeouts(
			int statusCode, int systemError, bool shouldReconnect,
			bool isExpectedPollTimeout, bool shouldContinueEventPolling)
		{
			var result = new DesktopClient.CallResult((NativeClientStatus)statusCode, 0, systemError,
				string.Empty, "test operation");

			Assert.Multiple(() =>
			{
				Assert.That(result.ShouldReconnect, Is.EqualTo(shouldReconnect));
				Assert.That(result.IsExpectedPollTimeout, Is.EqualTo(isExpectedPollTimeout));
				Assert.That(result.ShouldContinueEventPolling,
					Is.EqualTo(shouldContinueEventPolling));
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
			Assert.That(DesktopBackend.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
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
				(Name: "X11 append_window", Backend: DesktopBackend.X11, WorkspaceKnown: true, Json:
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

			Assert.That(DesktopBackend.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
				out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			var window = windows[0];
			Assert.Multiple(() =>
			{
				Assert.That(window.Title, Is.EqualTo("Editor"));
				Assert.That(window.ClassName, Is.EqualTo("Example.Editor"));
				Assert.That(window.PID, Is.Zero);
				Assert.That(window.Bounds, Is.EqualTo(Rectangle.Empty));
				Assert.That(window.ClientBounds, Is.EqualTo(Rectangle.Empty));
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

		[TestCase("null")]
		[TestCase("[]")]
		[TestCase("{\"ok\":false,\"windows\":[]}")]
		[TestCase("{\"ok\":true,\"windows\":null}")]
		public void InvalidWindowRepliesFailClosed(string json)
			=> Assert.That(DesktopBackend.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json), out _),
				Is.False);

		[Test]
		public void InvalidHandlesAndGeometryDoNotWrap()
		{
			const string json = """
				{"ok":true,"windows":[null,{"id":"-1"},{"id":"4294967296"},
				{"id":"24","frame":{"x":4294967297,"y":0,"width":10,"height":20}}]}
				""";
			Assert.That(DesktopBackend.X11.TryParseWindowList(Encoding.UTF8.GetBytes(json),
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
	}
}
#endif
