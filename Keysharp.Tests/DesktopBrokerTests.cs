#if LINUX
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class DesktopBrokerTests
	{
		[Test]
		public void X11SnapshotsPreserveNativeHandlesAndClientGeometry()
		{
			const string json = """
				{"ok":true,"windows":[{"id":"4026531841","title":"Café","appId":"Editor",
				"pid":123,"frame":{"x":-900,"y":-20,"width":600,"height":400},
				"client":{"x":-894,"y":10,"width":588,"height":364},
				"visible":true,"decorated":true,"transparency":127,
				"validFields":["frame","client","title","appId","visible"]}]}
				""";
			Assert.That(X11BrokerBackend.TryParseWindowList(json, out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			var window = windows[0];
			Assert.Multiple(() =>
			{
				Assert.That(window.Handle.ToInt64(), Is.EqualTo(4026531841L));
				Assert.That(window.Title, Is.EqualTo("Café"));
				Assert.That(window.ClassName, Is.EqualTo("Editor"));
				Assert.That(window.Bounds.X, Is.EqualTo(-900));
				Assert.That(window.ClientBounds.Width, Is.EqualTo(588));
				Assert.That(window.ClientToScreen().Y, Is.EqualTo(10));
				Assert.That(window.Transparency, Is.EqualTo(127L));
				Assert.That(window.ValidFields.Contains("pid"), Is.False);
			});
		}

		[TestCase("null")]
		[TestCase("[]")]
		[TestCase("{\"ok\":false,\"windows\":[]}")]
		[TestCase("{\"ok\":true,\"windows\":null}")]
		public void InvalidWindowRepliesFailClosed(string json)
			=> Assert.That(X11BrokerBackend.TryParseWindowList(json, out _), Is.False);

		[Test]
		public void InvalidHandlesAndGeometryDoNotWrap()
		{
			const string json = """
				{"ok":true,"windows":[null,{"id":"-1"},{"id":"4294967296"},
				{"id":"24","frame":{"x":4294967297,"y":0,"width":10,"height":20}}]}
				""";
			Assert.That(X11BrokerBackend.TryParseWindowList(json, out var windows), Is.True);
			Assert.That(windows, Has.Count.EqualTo(1));
			Assert.That(windows[0].Bounds.Width, Is.Zero);
		}
	}
}
#endif
