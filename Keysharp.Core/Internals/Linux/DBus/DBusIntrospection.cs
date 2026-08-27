#if LINUX
using System.Xml.Linq;

namespace Keysharp.Internals.DBus
{
	internal sealed class DBusMethodInfo
	{
		internal string Name;
		internal string InSignature;
		internal string OutSignature;
		internal string[] OutArgNames;
	}

	internal sealed class DBusPropertyInfo
	{
		internal string Name;
		internal string Type;
		internal bool CanRead;
		internal bool CanWrite;
	}

	internal sealed class DBusSignalInfo
	{
		internal string Name;
		internal string Signature;
	}

	internal sealed class DBusInterfaceInfo
	{
		internal string Name;
		internal readonly Dictionary<string, DBusMethodInfo> Methods = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, DBusPropertyInfo> Properties = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, DBusSignalInfo> Signals = new (StringComparer.Ordinal);
	}

	internal sealed class DBusNodeInfo
	{
		internal readonly Dictionary<string, DBusInterfaceInfo> Interfaces = new (StringComparer.Ordinal);
		internal string[] Children = [];

		/// <summary>Interfaces excluding the standard org.freedesktop.DBus.* ones every object carries.</summary>
		internal IEnumerable<DBusInterfaceInfo> UserInterfaces =>
			Interfaces.Values.Where(i => !i.Name.StartsWith("org.freedesktop.DBus.", StringComparison.Ordinal));
	}

	/// <summary>
	/// Fetches and caches org.freedesktop.DBus.Introspectable output. This is the type library of the D-Bus
	/// world: it supplies the signatures that drive marshalling and the member tables that make late binding work.
	/// </summary>
	internal static class DBusIntrospection
	{
		private static readonly ConcurrentDictionary<(DBusBus, string, string, long), DBusNodeInfo> cache = new ();

		internal static DBusNodeInfo Get(DBusBus bus, string service, string path)
		{
			var key = (bus, service, path, DBusConnections.Generation(bus));

			if (cache.TryGetValue(key, out var cached))
				return cached;

			var node = Parse(DBusCalls.Introspect(bus, service, path));
			_ = cache.TryAdd(key, node);
			return node;
		}

		internal static void Invalidate(DBusBus bus, string service)
		{
			foreach (var key in cache.Keys)
				if (key.Item1 == bus && string.Equals(key.Item2, service, StringComparison.Ordinal))
					_ = cache.TryRemove(key, out _);
		}

		internal static DBusNodeInfo Parse(string xml)
		{
			var node = new DBusNodeInfo();

			if (string.IsNullOrEmpty(xml))
				return node;

			XDocument doc;

			try
			{
				doc = XDocument.Parse(xml);
			}
			catch (Exception ex)
			{
				throw new FormatException($"Malformed D-Bus introspection XML: {ex.Message}", ex);
			}

			var root = doc.Root;

			if (root == null)
				return node;

			foreach (var ifaceEl in root.Elements("interface"))
			{
				var ifaceName = (string)ifaceEl.Attribute("name");

				if (string.IsNullOrEmpty(ifaceName))
					continue;

				var iface = new DBusInterfaceInfo { Name = ifaceName };

				foreach (var m in ifaceEl.Elements("method"))
				{
					var name = (string)m.Attribute("name");

					if (string.IsNullOrEmpty(name))
						continue;

					var inSig = new System.Text.StringBuilder();
					var outSig = new System.Text.StringBuilder();
					var outNames = new List<string>();

					foreach (var arg in m.Elements("arg"))
					{
						var type = (string)arg.Attribute("type") ?? "";
						// The spec's default direction for a method argument is "in".
						var dir = (string)arg.Attribute("direction") ?? "in";

						if (string.Equals(dir, "out", StringComparison.Ordinal))
						{
							_ = outSig.Append(type);
							outNames.Add((string)arg.Attribute("name") ?? "");
						}
						else
							_ = inSig.Append(type);
					}

					iface.Methods[name] = new DBusMethodInfo
					{
						Name = name,
						InSignature = inSig.ToString(),
						OutSignature = outSig.ToString(),
						OutArgNames = [.. outNames]
					};
				}

				foreach (var p in ifaceEl.Elements("property"))
				{
					var name = (string)p.Attribute("name");

					if (string.IsNullOrEmpty(name))
						continue;

					var access = (string)p.Attribute("access") ?? "read";
					iface.Properties[name] = new DBusPropertyInfo
					{
						Name = name,
						Type = (string)p.Attribute("type") ?? "",
						CanRead = access.Contains("read", StringComparison.Ordinal),
						CanWrite = access.Contains("write", StringComparison.Ordinal)
					};
				}

				foreach (var s in ifaceEl.Elements("signal"))
				{
					var name = (string)s.Attribute("name");

					if (string.IsNullOrEmpty(name))
						continue;

					var sig = new System.Text.StringBuilder();

					foreach (var arg in s.Elements("arg"))
						_ = sig.Append((string)arg.Attribute("type") ?? "");

					iface.Signals[name] = new DBusSignalInfo { Name = name, Signature = sig.ToString() };
				}

				node.Interfaces[ifaceName] = iface;
			}

			node.Children = [.. root.Elements("node")
								 .Select(n => (string)n.Attribute("name"))
								 .Where(n => !string.IsNullOrEmpty(n))];
			return node;
		}
	}
}
#endif
