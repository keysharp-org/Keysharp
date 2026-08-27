#if LINUX
using Keysharp.Builtins.COM;
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using DBusMessage = Tmds.DBus.Protocol.Message;

namespace Keysharp.Tests
{
	/// <summary>
	/// Exercises the D-Bus primitive layer against a real bus. Everything needed is self-contained: the tests talk
	/// to org.freedesktop.DBus (always present) and to an object this fixture serves itself, so no desktop
	/// environment is required and the fixture runs headless.
	/// </summary>
	[TestFixture, Category("Internal"), Category("Curated")]
	public class DBusTests : TestRunner
	{
		private const string ServiceName = "io.keysharp.CoreTest";
		private const string ObjPath = "/io/keysharp/CoreTest";
		private const string Iface = "io.keysharp.CoreTest";

		private DBusConnection server;
		private bool busAvailable;

		[OneTimeSetUp]
		public void StartPeer()
		{
			try
			{
				server = new DBusConnection(DBusAddresses.Session);
				server.ConnectAsync().AsTask().GetAwaiter().GetResult();
				server.AddMethodHandler(new TestPeer());
				server.RequestNameAsync(ServiceName, default).GetAwaiter().GetResult();
				busAvailable = true;
			}
			catch (Exception)
			{
				busAvailable = false;   // no session bus in this environment
			}
		}

		[OneTimeTearDown]
		public void StopPeer()
		{
			try { server?.Dispose(); } catch { }

			DBusConnections.Reset();
		}

		private void RequireBus()
		{
			if (!busAvailable)
				Assert.Ignore("No D-Bus session bus is available.");
		}

		// ---- signature parsing (no bus needed) --------------------------------------------------

		[Test]
		public void SignatureParsesCompoundTypes()
		{
			var nodes = DBusSignature.Parse("sa{sv}(ii)ao");
			Assert.That(nodes.Length, Is.EqualTo(4));
			Assert.That(nodes[0].Code, Is.EqualTo(DBusTypeCode.String));
			Assert.That(nodes[1].IsDictArray, Is.True);
			Assert.That(nodes[1].Element.Key.Code, Is.EqualTo(DBusTypeCode.String));
			Assert.That(nodes[1].Element.Element.Code, Is.EqualTo(DBusTypeCode.Variant));
			Assert.That(nodes[2].Code, Is.EqualTo(DBusTypeCode.Struct));
			Assert.That(nodes[2].Fields.Length, Is.EqualTo(2));
			Assert.That(nodes[3].Code, Is.EqualTo(DBusTypeCode.Array));
			Assert.That(nodes[3].Element.Code, Is.EqualTo(DBusTypeCode.ObjectPath));
		}

		[Test]
		public void SignatureRejectsMalformedInput()
		{
			Assert.Throws<FormatException>(() => DBusSignature.Parse("a{sv"));
			Assert.Throws<FormatException>(() => DBusSignature.Parse("(ii"));
			Assert.Throws<FormatException>(() => DBusSignature.Parse("Z"));
		}

		[Test]
		public void IntrospectionParsesMembersAndDefaultsDirectionToIn()
		{
			var node = DBusIntrospection.Parse("""
				<node>
				  <interface name="a.b">
				    <method name="M">
				      <arg type="s"/>
				      <arg direction="in" type="i"/>
				      <arg direction="out" type="b"/>
				    </method>
				    <property name="P" type="u" access="readwrite"/>
				    <signal name="S"><arg type="s"/><arg type="i"/></signal>
				  </interface>
				  <node name="child"/>
				</node>
				""");
			var iface = node.Interfaces["a.b"];
			// The spec's default direction is "in", so the unmarked arg belongs to the input signature.
			Assert.That(iface.Methods["M"].InSignature, Is.EqualTo("si"));
			Assert.That(iface.Methods["M"].OutSignature, Is.EqualTo("b"));
			Assert.That(iface.Properties["P"].CanRead, Is.True);
			Assert.That(iface.Properties["P"].CanWrite, Is.True);
			Assert.That(iface.Signals["S"].Signature, Is.EqualTo("si"));
			Assert.That(node.Children, Is.EqualTo(new[] { "child" }));
		}

		// ---- live bus ---------------------------------------------------------------------------

		[Test]
		public void DynamicCallReachesTheDaemon()
		{
			RequireBus();
			var names = DBusCalls.Call(DBusBus.Session, DBusCalls.DBusService, DBusCalls.DBusPath,
									   DBusCalls.DBusInterface, "ListNames", "", [], "as");
			Assert.That(names.Length, Is.EqualTo(1));
			// The backing list: the script enumerator needs an initialized Script, and the public indexer is 1-based.
			Assert.That(((Keysharp.Builtins.Array)names[0]).array, Has.Member(ServiceName));
		}

		[Test]
		public void IntrospectionRoundTripsThroughTheBus()
		{
			RequireBus();
			var node = DBusIntrospection.Get(DBusBus.Session, ServiceName, ObjPath);
			Assert.That(node.Interfaces.ContainsKey(Iface), Is.True);
			Assert.That(node.Interfaces[Iface].Methods.Keys, Is.SupersetOf(new[] { "Echo", "Add", "TakeDict", "Fail" }));
		}

		[Test]
		public void MethodCallsMarshalBothDirections()
		{
			RequireBus();
			var echo = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "Echo", "s", ["hello"], "s");
			Assert.That(echo[0], Is.EqualTo("echo:hello"));
			var sum = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "Add", "ii", [2L, 3L], "i");
			Assert.That(sum[0], Is.EqualTo(5L));
		}

		[Test]
		public void DictionaryOfVariantsMarshalsOut()
		{
			RequireBus();
			var map = new Keysharp.Builtins.Map();
			_ = map.Set("alpha", new Keysharp.Builtins.COM.ComValue("u", 1L));
			_ = map.Set("beta", "x");
			var res = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "TakeDict", "a{sv}", [map], "us");
			Assert.That(res[0], Is.EqualTo(2L));
			Assert.That(res[1], Is.EqualTo("alpha,beta"));
		}

		[Test]
		public void DictionaryOfVariantsMarshalsBack()
		{
			RequireBus();
			var res = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "GiveDict", "", [], "a{sv}");
			var map = (Keysharp.Builtins.Map)res[0];
			// Values inside a{sv} arrive already unwrapped (the VariantValue's Type is the inner type, not
			// Variant); PortalScreenCapture's "uri" lookup depends on that, so it is pinned here.
			Assert.That(map.map["text"], Is.EqualTo("hello"));
			Assert.That(map.map["number"], Is.EqualTo(42L));
			Assert.That(map.map["flag"], Is.EqualTo(true));
		}

		[Test]
		public void ErrorRepliesSurfaceTheDBusErrorName()
		{
			RequireBus();
			var ex = Assert.Throws<DBusErrorReplyException>(() =>
					 DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "Fail", "", [], ""));
			Assert.That(ex.ErrorName, Is.EqualTo("io.keysharp.Error.Deliberate"));
			Assert.That(ex.ErrorMessage, Does.Contain("boom"));
		}

		[Test]
		public void WireTypesAreStrict()
		{
			RequireBus();
			// 'i' where the peer published 's' must be rejected: this is why ComValue exists.
			Assert.Throws<DBusErrorReplyException>(() =>
				DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "Echo", "i", [42L], "s"));
		}

		[Test]
		public void PropertiesReadThroughTheStandardInterface()
		{
			RequireBus();
			var features = DBusCalls.GetProperty(DBusBus.Session, DBusCalls.DBusService, DBusCalls.DBusPath,
												 DBusCalls.DBusInterface, "Features");
			Assert.That(features, Is.InstanceOf<Keysharp.Builtins.Array>());
		}

		[Test]
		public void SignalsAreReceivedAndMarshalled()
		{
			RequireBus();
			using var received = new System.Threading.SemaphoreSlim(0);
			object[] got = null;
			using var sub = DBusCalls.WatchSignal(DBusBus.Session, null, ObjPath, Iface, "Ping", "s",
												  args => { got = args; _ = received.Release(); });
			EmitPing("ping-payload");
			Assert.That(received.Wait(TimeSpan.FromSeconds(5)), Is.True, "signal was not delivered");
			Assert.That(got[0], Is.EqualTo("ping-payload"));
		}

		[Test]
		public void AnExceptionInAHandlerDoesNotKillTheConnection()
		{
			RequireBus();
			using var received = new System.Threading.SemaphoreSlim(0);
			using var throwing = DBusCalls.WatchSignal(DBusBus.Session, null, ObjPath, Iface, "Ping", "s",
													   _ => throw new InvalidOperationException("handler blew up"));
			using var ok = DBusCalls.WatchSignal(DBusBus.Session, null, ObjPath, Iface, "Ping", "s",
												 _ => received.Release());
			EmitPing("first");
			Assert.That(received.Wait(TimeSpan.FromSeconds(5)), Is.True);
			// The connection must still work after a handler threw — otherwise every later call goes dead.
			var echo = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "Echo", "s", ["still alive"], "s");
			Assert.That(echo[0], Is.EqualTo("echo:still alive"));
		}

		[Test]
		public void OwnerChangesAreReportedWithoutPolling()
		{
			RequireBus();
			const string transient = "io.keysharp.CoreTest.Transient";
			var connection = DBusConnections.Get(DBusBus.Session);
			var watcher = connection.WatchNameOwnerAsync(transient).GetAwaiter().GetResult();

			try
			{
				using var appeared = new System.Threading.SemaphoreSlim(0);
				using var vanished = new System.Threading.SemaphoreSlim(0);
				// The tracker must push changes on its own: WatchedDbusService relies on this so a compositor
				// extension appearing or dying reaches AvailabilityChanged subscribers with nobody polling.
				DBusNameOwner.Track(watcher,
									owner => _ = string.IsNullOrEmpty(owner) ? vanished.Release() : appeared.Release(),
									() => true);
				var peer = new DBusConnection(DBusAddresses.Session);
				peer.ConnectAsync().AsTask().GetAwaiter().GetResult();
				peer.RequestNameAsync(transient, default).GetAwaiter().GetResult();
				Assert.That(appeared.Wait(TimeSpan.FromSeconds(5)), Is.True, "owner appearing was not reported");
				peer.Dispose();
				Assert.That(vanished.Wait(TimeSpan.FromSeconds(5)), Is.True, "owner vanishing was not reported");
			}
			finally
			{
				watcher.Dispose();
			}
		}

		[Test]
		public void NameOwnerLookupDistinguishesRunningServices()
		{
			RequireBus();
			Assert.That(DBusCalls.GetNameOwner(DBusBus.Session, ServiceName), Is.Not.Null);
			Assert.That(DBusCalls.GetNameOwner(DBusBus.Session, "io.keysharp.DefinitelyNotRunning"), Is.Null);
		}

		// ---- ComObject script surface -----------------------------------------------------------

		[Test]
		public void TargetParsingSplitsBusPrefixAndPath()
		{
			Assert.That(ComObject.ParseTarget("org.a.B"), Is.EqualTo((DBusBus.Session, "org.a.B", (string)null)));
			Assert.That(ComObject.ParseTarget("session:org.a.B"), Is.EqualTo((DBusBus.Session, "org.a.B", (string)null)));
			Assert.That(ComObject.ParseTarget("system:org.a.B"), Is.EqualTo((DBusBus.System, "org.a.B", (string)null)));
			Assert.That(ComObject.ParseTarget("system:org.a.B:/x/y"), Is.EqualTo((DBusBus.System, "org.a.B", "/x/y")));
			Assert.That(ComObject.DerivePath("org.freedesktop.NetworkManager"), Is.EqualTo("/org/freedesktop/NetworkManager"));
		}

		[Test]
		public void ComObjectCallsMethodsByName()
		{
			RequireBus();
			var obj = (ComObject)ComObject.Create($"{ServiceName}:{ObjPath}", "", activate: false);
			var meta = (Keysharp.Builtins.IMetaObject)obj;
			Assert.That(meta.Call("Echo", ["hi"]), Is.EqualTo("echo:hi"));
			Assert.That(meta.Call("Add", [2L, 3L]), Is.EqualTo(5L));
		}

		[Test]
		public void DiscoveryWorksFromAScript()
		{
			RequireBus();
			// Runs through the real script dispatcher rather than IMetaObject directly. That distinction matters:
			// deriving ComObject from KeysharpObject instead of Any made every script-level member access recurse
			// until the stack was exhausted, which no C#-level test could see.
			Assert.That(TestScript("dbus-discovery", false), Is.True);
		}

		[Test]
		public void ComObjectReachesTheBusDaemonAtItsDerivedPath()
		{
			RequireBus();
			// The example on the ComObject documentation page: no object path is given, so it is derived from
			// the service name. The bus daemon is always present, which is what makes the example safe to publish.
			var bus = ComObject.Create("org.freedesktop.DBus", "", activate: true);
			var names = ((Keysharp.Builtins.IMetaObject)bus).Call("ListNames", []);
			Assert.That(((Keysharp.Builtins.Array)names).array, Has.Member("org.freedesktop.DBus"));
		}

		[Test]
		public void ComObjectReturnsMultipleOutArgsAsAnArray()
		{
			RequireBus();
			var obj = (ComObject)ComObject.Create($"{ServiceName}:{ObjPath}", "", activate: false);
			var map = new Keysharp.Builtins.Map();
			_ = map.Set("k", "v");
			var result = ((Keysharp.Builtins.IMetaObject)obj).Call("TakeDict", [map]);
			Assert.That(((Keysharp.Builtins.Array)result).array, Is.EqualTo(new object[] { 1L, "k" }));
		}

		[Test]
		public void ArrayArgumentsMarshalEveryElement()
		{
			RequireBus();
			// Guards the 1-based/0-based trap: the public Array indexer starts at 1, so writing an array through
			// it would silently drop the last element and prepend a bogus one.
			var arr = new Keysharp.Builtins.Array("a", "b", "c");
			var joined = DBusCalls.Call(DBusBus.Session, ServiceName, ObjPath, Iface, "JoinStrings", "as", [arr], "s");
			Assert.That(joined[0], Is.EqualTo("a|b|c"));
		}

		[Test]
		public void ComValueValidatesItsSignature()
		{
			Assert.That(new Keysharp.Builtins.COM.ComValue("u", 1L).DBusSignature, Is.EqualTo("u"));
			Assert.That(new Keysharp.Builtins.COM.ComValue("a{sv}", null).DBusSignature, Is.EqualTo("a{sv}"));
			Assert.That(new Keysharp.Builtins.COM.ComValue(19L, 1L).DBusSignature, Is.EqualTo("u"));   // VT_UI4
			Assert.Throws<FormatException>(() => new Keysharp.Builtins.COM.ComValue("a{sv", 1L));
			Assert.Throws<ArgumentException>(() => new Keysharp.Builtins.COM.ComValue("ss", 1L));      // not one type
		}

		[Test]
		public void VariantInferenceMatchesTheDocumentedRule()
		{
			Assert.That(DBusMarshal.InferVariantSignature("s"), Is.EqualTo("s"));
			Assert.That(DBusMarshal.InferVariantSignature(true), Is.EqualTo("b"));
			Assert.That(DBusMarshal.InferVariantSignature(1.5), Is.EqualTo("d"));
			Assert.That(DBusMarshal.InferVariantSignature(5L), Is.EqualTo("i"));
			Assert.That(DBusMarshal.InferVariantSignature(long.MaxValue), Is.EqualTo("x"));
		}

		private void EmitPing(string payload)
		{
			var writer = server.GetMessageWriter();

			try
			{
				writer.WriteSignalHeader(null, ObjPath, Iface, "Ping", "s");
				writer.WriteString(payload);
				_ = server.TrySendMessage(writer.CreateMessage());
			}
			finally
			{
				writer.Dispose();
			}
		}

		/// <summary>The peer under test. HandleMethodAsync delegates to a sync frame because Reader is a ref struct.</summary>
		private sealed class TestPeer : IPathMethodHandler
		{
			public string Path => ObjPath;
			public bool HandlesChildPaths => false;

			public ValueTask HandleMethodAsync(MethodContext context)
			{
				Handle(context);
				return default;
			}

			private static void Handle(MethodContext context)
			{
				var req = context.Request;

				switch (req.InterfaceAsString, req.MemberAsString)
				{
					case ("org.freedesktop.DBus.Introspectable", "Introspect"):
					{
						var w = context.CreateReplyWriter("s");
						w.WriteString("""
							<node>
							  <interface name="io.keysharp.CoreTest">
							    <method name="Echo"><arg direction="in" type="s"/><arg direction="out" type="s"/></method>
							    <method name="Add"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
							    <method name="TakeDict"><arg direction="in" type="a{sv}"/><arg direction="out" type="u"/><arg direction="out" type="s"/></method>
							    <method name="JoinStrings"><arg direction="in" type="as"/><arg direction="out" type="s"/></method>
							    <method name="GiveDict"><arg direction="out" type="a{sv}"/></method>
							    <method name="Fail"/>
							    <property name="Label" type="s" access="read"/>
							    <signal name="Ping"><arg type="s"/></signal>
							  </interface>
							</node>
							""");
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "Echo") when req.SignatureAsString == "s":
					{
						var r = req.GetBodyReader();
						var s = r.ReadString();
						var w = context.CreateReplyWriter("s");
						w.WriteString("echo:" + s);
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "Echo"):
						context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Echo expects 's', got '{req.SignatureAsString}'");
						break;

					case ("io.keysharp.CoreTest", "Add"):
					{
						var r = req.GetBodyReader();
						int a = r.ReadInt32(), b = r.ReadInt32();
						var w = context.CreateReplyWriter("i");
						w.WriteInt32(a + b);
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "TakeDict"):
					{
						var r = req.GetBodyReader();
						var dict = r.ReadDictionaryOfStringToVariantValue();
						var w = context.CreateReplyWriter("us");
						w.WriteUInt32((uint)dict.Count);
						w.WriteString(string.Join(",", dict.Keys.OrderBy(k => k)));
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "JoinStrings"):
					{
						var r = req.GetBodyReader();
						var parts = r.ReadArrayOfString();
						var w = context.CreateReplyWriter("s");
						w.WriteString(string.Join("|", parts));
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "GiveDict"):
					{
						var w = context.CreateReplyWriter("a{sv}");
						var start = w.WriteDictionaryStart();
						w.WriteDictionaryEntryStart();
						w.WriteString("text");
						w.WriteVariantString("hello");
						w.WriteDictionaryEntryStart();
						w.WriteString("number");
						w.WriteVariantInt32(42);
						w.WriteDictionaryEntryStart();
						w.WriteString("flag");
						w.WriteVariantBool(true);
						w.WriteDictionaryEnd(start);
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					case ("io.keysharp.CoreTest", "Fail"):
						context.ReplyError("io.keysharp.Error.Deliberate", "boom");
						break;

					case ("org.freedesktop.DBus.Properties", "Get"):
					{
						var w = context.CreateReplyWriter("v");
						w.WriteVariantString("test-label");
						context.Reply(w.CreateMessage());
						w.Dispose();
						break;
					}

					default:
						context.ReplyUnknownMethodError();
						break;
				}
			}
		}
	}
}
#endif
