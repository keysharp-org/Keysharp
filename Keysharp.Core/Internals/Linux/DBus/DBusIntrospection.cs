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
		internal bool Standard;
		internal readonly Dictionary<string, DBusMethodInfo> Methods = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, DBusPropertyInfo> Properties = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, DBusSignalInfo> Signals = new (StringComparer.Ordinal);
	}

	internal sealed class DBusNodeInfo
	{
		internal readonly Dictionary<string, DBusInterfaceInfo> Interfaces = new (StringComparer.Ordinal);
		internal string[] Children = [];

		/// <summary>The object's own interfaces, without the standard ones.</summary>
		internal DBusInterfaceInfo[] UserInterfaces = [];

		// User interfaces shadow standard ones; multiple owners of the same rank are ambiguous.
		internal readonly Dictionary<string, DBusInterfaceInfo[]> MethodOwners = new (StringComparer.Ordinal);
		internal readonly Dictionary<string, DBusInterfaceInfo[]> PropertyOwners = new (StringComparer.Ordinal);
	}

	/// <summary>
	/// Fetches and parses org.freedesktop.DBus.Introspectable output. This is the type library of the D-Bus
	/// world: it supplies the signatures that drive marshalling and the member tables that make late binding work.
	/// </summary>
	internal static class DBusIntrospection
	{
		internal static DBusNodeInfo Get(DBusBus bus, string service, string path)
			=> Parse(DBusCalls.Introspect(bus, service, path));

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

				var iface = new DBusInterfaceInfo
				{
					Name = ifaceName,
					Standard = ifaceName.StartsWith("org.freedesktop.DBus.", StringComparison.Ordinal)
				};

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
			node.UserInterfaces = [.. node.Interfaces.Values.Where(static i => !i.Standard)];
			IndexMembers(node.MethodOwners, node, static i => i.Methods.Keys);
			IndexMembers(node.PropertyOwners, node, static i => i.Properties.Keys);
			return node;
		}

		private static void IndexMembers(Dictionary<string, DBusInterfaceInfo[]> owners, DBusNodeInfo node,
										 Func<DBusInterfaceInfo, IEnumerable<string>> namesOf)
		{
			foreach (var iface in node.Interfaces.Values)
				foreach (var name in namesOf(iface))
				{
					if (!owners.TryGetValue(name, out var found) || (found[0].Standard && !iface.Standard))
						owners[name] = [iface];
					else if (found[0].Standard == iface.Standard)
						owners[name] = [.. found, iface];
				}
		}
	}
}
#endif
