#if LINUX
using Tmds.DBus.Protocol;
// Keysharp.Internals.Window.Unix also defines a Message; every use here means the D-Bus one.
using DBusMessage = Tmds.DBus.Protocol.Message;

namespace Keysharp.Internals.DBus
{
	/// <summary>
	/// The one place that touches MessageWriter/Reader. Both are ref structs, so a message must be fully built
	/// (and a reply fully read) inside a synchronous frame — nothing here may hold one across an await.
	/// Script-visible calls block on the scheduler pump rather than the thread, so a slow peer cannot freeze
	/// timers, hotkeys or the GUI.
	/// </summary>
	internal static class DBusCalls
	{
		internal const string DBusService = "org.freedesktop.DBus";
		internal const string DBusPath = "/org/freedesktop/DBus";
		internal const string DBusInterface = "org.freedesktop.DBus";
		internal const string PropertiesInterface = "org.freedesktop.DBus.Properties";
		internal const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";

		/// <summary>The reference implementation's default method timeout.</summary>
		internal const int DefaultTimeoutMs = 25_000;

		// ---- method calls ----------------------------------------------------------------------

		/// <summary>Calls a method and returns its out-arguments as script values.</summary>
		internal static object[] Call(DBusBus bus, string destination, string path, string iface, string member,
									  string inSignature, object[] args, string outSignature, int timeoutMs = DefaultTimeoutMs)
			=> CallOn(DBusConnections.Get(bus), destination, path, iface, member, inSignature, args, outSignature, timeoutMs);

		/// <summary>As <see cref="Call"/>, but on a caller-owned connection (the screenshot portal keeps its own).</summary>
		internal static object[] CallOn(DBusConnection connection, string destination, string path, string iface, string member,
										string inSignature, object[] args, string outSignature, int timeoutMs = DefaultTimeoutMs)
		{
			var inNodes = DBusSignature.Parse(inSignature);
			var outNodes = DBusSignature.Parse(outSignature);
			var task = connection.CallMethodAsync(
						   BuildCall(connection, destination, path, iface, member, inSignature, inNodes, args),
						   static (DBusMessage m, object state) =>
			{
				var reader = m.GetBodyReader();
				return DBusMarshal.ReadArguments(ref reader, (DBusSigNode[])state);
			}, outNodes);
			return Await(task, timeoutMs, $"{iface}.{member}");
		}

		/// <summary>Calls a method whose reply shape is not known ahead of time, decoding it from the reply's own signature.</summary>
		internal static object[] CallDynamic(DBusBus bus, string destination, string path, string iface, string member,
											 string inSignature, object[] args, int timeoutMs = DefaultTimeoutMs)
		{
			var connection = DBusConnections.Get(bus);
			var inNodes = DBusSignature.Parse(inSignature);
			var task = connection.CallMethodAsync(
						   BuildCall(connection, destination, path, iface, member, inSignature, inNodes, args),
						   static (DBusMessage m, object state) =>
			{
				var reader = m.GetBodyReader();
				return DBusMarshal.ReadArguments(ref reader, DBusSignature.Parse(m.SignatureAsString ?? ""));
			}, null);
			return Await(task, timeoutMs, $"{iface}.{member}");
		}

		private static MessageBuffer BuildCall(DBusConnection connection, string destination, string path, string iface,
											   string member, string inSignature, DBusSigNode[] inNodes, object[] args)
		{
			// Not `using`: WriteArguments takes the writer by ref, which a using-variable forbids.
			var writer = connection.GetMessageWriter();

			try
			{
				writer.WriteMethodCallHeader(
					destination: destination,
					path: path,
					@interface: iface,
					member: member,
					signature: string.IsNullOrEmpty(inSignature) ? null : inSignature);
				DBusMarshal.WriteArguments(ref writer, inNodes, args);
				return writer.CreateMessage();
			}
			finally
			{
				writer.Dispose();
			}
		}

		// ---- standard interfaces ---------------------------------------------------------------

		internal static string Introspect(DBusBus bus, string destination, string path, int timeoutMs = DefaultTimeoutMs)
		{
			var results = Call(bus, destination, path, IntrospectableInterface, "Introspect", "", [], "s", timeoutMs);
			return results.Length > 0 ? results[0] as string : "";
		}

		internal static object GetProperty(DBusBus bus, string destination, string path, string iface, string name, int timeoutMs = DefaultTimeoutMs)
			=> GetPropertyOn(DBusConnections.Get(bus), destination, path, iface, name, timeoutMs);

		internal static object GetPropertyOn(DBusConnection connection, string destination, string path, string iface,
											 string name, int timeoutMs = DefaultTimeoutMs)
		{
			var results = CallOn(connection, destination, path, PropertiesInterface, "Get", "ss", [iface, name], "v", timeoutMs);
			return results.Length > 0 ? results[0] : null;
		}

		internal static void SetProperty(DBusBus bus, string destination, string path, string iface, string name,
										 string type, object value, int timeoutMs = DefaultTimeoutMs)
		{
			var connection = DBusConnections.Get(bus);
			var valueNodes = DBusSignature.Parse(type);

			if (valueNodes.Length != 1)
				throw new ArgumentException($"Property '{name}' has an unusable type '{type}'.");

			var task = connection.CallMethodAsync(Build());
			_ = Await(task, timeoutMs, $"{PropertiesInterface}.Set({name})");

			MessageBuffer Build()
			{
				var writer = connection.GetMessageWriter();

				try
				{
					writer.WriteMethodCallHeader(
						destination: destination, path: path, @interface: PropertiesInterface,
						member: "Set", signature: "ssv");
					writer.WriteString(iface);
					writer.WriteString(name);
					writer.WriteSignature(type);
					DBusMarshal.Write(ref writer, valueNodes[0], value);
					return writer.CreateMessage();
				}
				finally
				{
					writer.Dispose();
				}
			}
		}

		internal static string GetNameOwner(DBusBus bus, string name, int timeoutMs = DefaultTimeoutMs)
		{
			try
			{
				var results = Call(bus, DBusService, DBusPath, DBusInterface, "GetNameOwner", "s", [name], "s", timeoutMs);
				return results.Length > 0 ? results[0] as string : null;
			}
			catch (DBusErrorReplyException)
			{
				return null;   // NameHasNoOwner: nobody is offering the service
			}
		}

		internal static bool StartServiceByName(DBusBus bus, string name, int timeoutMs = DefaultTimeoutMs)
		{
			try
			{
				_ = Call(bus, DBusService, DBusPath, DBusInterface, "StartServiceByName", "su", [name, 0L], "u", timeoutMs);
				return true;
			}
			catch (DBusErrorReplyException)
			{
				return false;
			}
		}

		// ---- signals ---------------------------------------------------------------------------

		/// <summary>
		/// Subscribes to a signal. The handler runs on a Tmds dispatch thread; an exception escaping it kills the
		/// connection's whole dispatch loop, so it is wrapped here and never allowed to propagate.
		/// </summary>
		internal static IDisposable WatchSignal(DBusBus bus, string sender, string path, string iface, string member,
												string signature, Action<object[]> handler)
		{
			var connection = DBusConnections.Get(bus);
			var nodes = DBusSignature.Parse(signature);
			var rule = new MatchRule
			{
				Type = MessageType.Signal,
				Sender = sender,
				Path = path,
				Interface = iface,
				Member = member
			};
			var task = connection.AddMatchAsync(
						   rule,
						   (DBusMessage m, object state) =>
			{
				var reader = m.GetBodyReader();
				return DBusMarshal.ReadArguments(ref reader, nodes);
			},
			(Notification<object[]> n) =>
			{
				if (!n.HasValue)
					return;

				try
				{
					handler(n.Value);
				}
				catch (Exception ex)
				{
					// An exception escaping here kills the connection's entire dispatch loop — every later call and
					// every other subscription goes silently dead — so even the reporting is guarded.
					try
					{
						_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
					}
					catch
					{
					}
				}
			},
			emitOnCapturedContext: false);
			return task.AsTask().GetAwaiter().GetResult();
		}

		// ---- the sync bridge -------------------------------------------------------------------

		/// <summary>
		/// Waits for a D-Bus reply while pumping the Keysharp message loop, so timers/hotkeys/GUI keep running.
		/// Unwraps AggregateException so callers see the D-Bus error, not the Task plumbing.
		/// </summary>
		private static object[] Await(Task<object[]> task, int timeoutMs, string what)
		{
			WaitOrThrow(task, timeoutMs, what);
			return task.GetAwaiter().GetResult();
		}

		private static object[] Await(Task task, int timeoutMs, string what)
		{
			WaitOrThrow(task, timeoutMs, what);
			task.GetAwaiter().GetResult();
			return [];
		}

		/// <summary>
		/// Task.Wait reports a faulted task by throwing AggregateException, so the unwrapping has to happen around
		/// the wait itself — GetResult is never reached for a failed call.
		/// </summary>
		private static void WaitOrThrow(Task task, int timeoutMs, string what)
		{
			bool completed;

			try
			{
				completed = task.WaitInterruptible(timeoutMs);
			}
			catch (AggregateException ae) when (ae.InnerException != null)
			{
				throw ae.InnerException;
			}

			if (!completed)
				throw new TimeoutException($"The D-Bus call to {what} did not complete within {timeoutMs} ms.");
		}
	}
}
#endif
