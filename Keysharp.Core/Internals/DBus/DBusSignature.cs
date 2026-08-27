#if LINUX
namespace Keysharp.Internals.DBus
{
	internal enum DBusTypeCode
	{
		Byte, Boolean, Int16, UInt16, Int32, UInt32, Int64, UInt64, Double,
		String, ObjectPath, Signature, Variant, UnixFd, Array, Struct, DictEntry
	}

	/// <summary>One complete type from a D-Bus signature string.</summary>
	internal sealed class DBusSigNode
	{
		internal DBusTypeCode Code;
		internal DBusSigNode Element;      // Array element, or DictEntry value
		internal DBusSigNode Key;          // DictEntry key
		internal DBusSigNode[] Fields;     // Struct members
		internal string Text;              // this node's own signature text

		internal bool IsDictArray => Code == DBusTypeCode.Array && Element != null && Element.Code == DBusTypeCode.DictEntry;
	}

	/// <summary>
	/// Parses D-Bus signature strings into type trees. Results are cached because every call and signal marshals
	/// against a signature that came from introspection and therefore repeats verbatim.
	/// </summary>
	internal static class DBusSignature
	{
		private static readonly ConcurrentDictionary<string, DBusSigNode[]> cache = new ();

		internal static DBusSigNode[] Parse(string signature)
		{
			if (string.IsNullOrEmpty(signature))
				return [];

			return cache.GetOrAdd(signature, static s =>
			{
				var pos = 0;
				var list = new List<DBusSigNode>();

				while (pos < s.Length)
					list.Add(ParseOne(s, ref pos));

				return [.. list];
			});
		}

		/// <summary>The DBusType byte Tmds uses for array-start/element alignment.</summary>
		internal static Tmds.DBus.Protocol.DBusType ToDBusType(DBusSigNode node) => node.Code switch
		{
			DBusTypeCode.Byte => Tmds.DBus.Protocol.DBusType.Byte,
			DBusTypeCode.Boolean => Tmds.DBus.Protocol.DBusType.Bool,
			DBusTypeCode.Int16 => Tmds.DBus.Protocol.DBusType.Int16,
			DBusTypeCode.UInt16 => Tmds.DBus.Protocol.DBusType.UInt16,
			DBusTypeCode.Int32 => Tmds.DBus.Protocol.DBusType.Int32,
			DBusTypeCode.UInt32 => Tmds.DBus.Protocol.DBusType.UInt32,
			DBusTypeCode.Int64 => Tmds.DBus.Protocol.DBusType.Int64,
			DBusTypeCode.UInt64 => Tmds.DBus.Protocol.DBusType.UInt64,
			DBusTypeCode.Double => Tmds.DBus.Protocol.DBusType.Double,
			DBusTypeCode.String => Tmds.DBus.Protocol.DBusType.String,
			DBusTypeCode.ObjectPath => Tmds.DBus.Protocol.DBusType.ObjectPath,
			DBusTypeCode.Signature => Tmds.DBus.Protocol.DBusType.Signature,
			DBusTypeCode.Variant => Tmds.DBus.Protocol.DBusType.Variant,
			DBusTypeCode.UnixFd => Tmds.DBus.Protocol.DBusType.UnixFd,
			DBusTypeCode.Array => Tmds.DBus.Protocol.DBusType.Array,
			DBusTypeCode.Struct => Tmds.DBus.Protocol.DBusType.Struct,
			DBusTypeCode.DictEntry => Tmds.DBus.Protocol.DBusType.DictEntry,
			_ => Tmds.DBus.Protocol.DBusType.Invalid
		};

		private static DBusSigNode ParseOne(string s, ref int pos)
		{
			if (pos >= s.Length)
				throw new FormatException($"Truncated D-Bus signature '{s}'.");

			var start = pos;
			var c = s[pos++];

			switch (c)
			{
				case 'y': return Simple(DBusTypeCode.Byte, "y");
				case 'b': return Simple(DBusTypeCode.Boolean, "b");
				case 'n': return Simple(DBusTypeCode.Int16, "n");
				case 'q': return Simple(DBusTypeCode.UInt16, "q");
				case 'i': return Simple(DBusTypeCode.Int32, "i");
				case 'u': return Simple(DBusTypeCode.UInt32, "u");
				case 'x': return Simple(DBusTypeCode.Int64, "x");
				case 't': return Simple(DBusTypeCode.UInt64, "t");
				case 'd': return Simple(DBusTypeCode.Double, "d");
				case 's': return Simple(DBusTypeCode.String, "s");
				case 'o': return Simple(DBusTypeCode.ObjectPath, "o");
				case 'g': return Simple(DBusTypeCode.Signature, "g");
				case 'v': return Simple(DBusTypeCode.Variant, "v");
				case 'h': return Simple(DBusTypeCode.UnixFd, "h");

				case 'a':
				{
					var elem = ParseOne(s, ref pos);
					return new DBusSigNode { Code = DBusTypeCode.Array, Element = elem, Text = s[start..pos] };
				}

				case '(':
				{
					var fields = new List<DBusSigNode>();

					while (pos < s.Length && s[pos] != ')')
						fields.Add(ParseOne(s, ref pos));

					if (pos >= s.Length)
						throw new FormatException($"Unterminated struct in D-Bus signature '{s}'.");

					pos++; // ')'
					return new DBusSigNode { Code = DBusTypeCode.Struct, Fields = [.. fields], Text = s[start..pos] };
				}

				case '{':
				{
					var key = ParseOne(s, ref pos);
					var val = ParseOne(s, ref pos);

					if (pos >= s.Length || s[pos] != '}')
						throw new FormatException($"Unterminated dict entry in D-Bus signature '{s}'.");

					pos++; // '}'
					return new DBusSigNode { Code = DBusTypeCode.DictEntry, Key = key, Element = val, Text = s[start..pos] };
				}

				default:
					throw new FormatException($"Unsupported D-Bus type code '{c}' in signature '{s}'.");
			}

			DBusSigNode Simple(DBusTypeCode code, string text) => new () { Code = code, Text = text };
		}
	}
}
#endif
