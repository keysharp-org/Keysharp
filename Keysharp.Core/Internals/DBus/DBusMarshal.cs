#if LINUX
using Tmds.DBus.Protocol;

namespace Keysharp.Internals.DBus
{
	/// <summary>
	/// Converts between script values and the D-Bus wire format, driven by a signature obtained from introspection.
	/// D-Bus is strictly typed on the wire (no implicit widening), so anything the signature cannot pin down —
	/// notably the value side of the ubiquitous a{sv} — must be typed explicitly by the script via ComValue.
	/// MessageWriter and Reader are ref structs, so every method here is synchronous by construction.
	/// </summary>
	internal static class DBusMarshal
	{
		/// <summary>Signature used for a bare script value placed inside a variant. See InferVariantSignature.</summary>
		internal const string DefaultIntegerVariantSignature = "i";

		// ---- writing ---------------------------------------------------------------------------

		internal static void WriteArguments(ref MessageWriter writer, DBusSigNode[] sig, object[] args)
		{
			var supplied = args?.Length ?? 0;

			if (supplied != sig.Length)
				throw new ArgumentException($"Expected {sig.Length} argument(s) for signature, got {supplied}.");

			for (var i = 0; i < sig.Length; i++)
				Write(ref writer, sig[i], args[i]);
		}

		internal static void Write(ref MessageWriter writer, DBusSigNode node, object value)
		{
			// A ComValue carries an explicit wire type; honour it wherever the target is a variant, and otherwise
			// just unwrap it (the declared type still has to match the signature the peer published).
			if (value is Keysharp.Builtins.COM.ComValue cv && node.Code != DBusTypeCode.Variant)
				value = cv.Value;

			switch (node.Code)
			{
				case DBusTypeCode.Byte: writer.WriteByte(checked((byte)ToLong(value))); break;
				case DBusTypeCode.Boolean: writer.WriteBool(ToBool(value)); break;
				case DBusTypeCode.Int16: writer.WriteInt16(checked((short)ToLong(value))); break;
				case DBusTypeCode.UInt16: writer.WriteUInt16(checked((ushort)ToLong(value))); break;
				case DBusTypeCode.Int32: writer.WriteInt32(checked((int)ToLong(value))); break;
				case DBusTypeCode.UInt32: writer.WriteUInt32(checked((uint)ToLong(value))); break;
				case DBusTypeCode.Int64: writer.WriteInt64(ToLong(value)); break;
				case DBusTypeCode.UInt64: writer.WriteUInt64(unchecked((ulong)ToLong(value))); break;
				case DBusTypeCode.Double: writer.WriteDouble(ToDouble(value)); break;
				case DBusTypeCode.String: writer.WriteString(value.As()); break;
				case DBusTypeCode.ObjectPath: writer.WriteObjectPath(ValidatePath(value.As())); break;
				case DBusTypeCode.Signature: writer.WriteSignature(value.As()); break;

				case DBusTypeCode.UnixFd:
					throw new NotSupportedException("Passing file descriptors ('h') to D-Bus is not supported.");

				case DBusTypeCode.Variant:
					WriteVariant(ref writer, value);
					break;

				case DBusTypeCode.Array when node.IsDictArray:
					WriteDictionary(ref writer, node, value);
					break;

				case DBusTypeCode.Array:
					WriteArray(ref writer, node, value);
					break;

				case DBusTypeCode.Struct:
					WriteStruct(ref writer, node, value);
					break;

				default:
					throw new NotSupportedException($"Cannot write D-Bus type '{node.Text}'.");
			}
		}

		private static void WriteArray(ref MessageWriter writer, DBusSigNode node, object value)
		{
			var items = ToList(value, node.Text);
			var start = writer.WriteArrayStart(DBusSignature.ToDBusType(node.Element));

			foreach (var item in items)
				Write(ref writer, node.Element, item);

			writer.WriteArrayEnd(start);
		}

		private static void WriteDictionary(ref MessageWriter writer, DBusSigNode node, object value)
		{
			var entry = node.Element;   // the {kv}
			var start = writer.WriteDictionaryStart();

			foreach (var (k, v) in ToPairs(value, node.Text))
			{
				writer.WriteDictionaryEntryStart();
				Write(ref writer, entry.Key, k);
				Write(ref writer, entry.Element, v);
			}

			writer.WriteDictionaryEnd(start);
		}

		private static void WriteStruct(ref MessageWriter writer, DBusSigNode node, object value)
		{
			var items = ToList(value, node.Text);

			if (items.Count != node.Fields.Length)
				throw new ArgumentException($"Struct '{node.Text}' needs {node.Fields.Length} element(s), got {items.Count}.");

			writer.WriteStructureStart();

			for (var i = 0; i < node.Fields.Length; i++)
				Write(ref writer, node.Fields[i], items[i]);
		}

		/// <summary>
		/// A variant carries its own signature, so the type has to come from the value. ComValue states it
		/// explicitly; anything else is inferred (see InferVariantSignature).
		/// </summary>
		private static void WriteVariant(ref MessageWriter writer, object value)
		{
			string sigText;

			if (value is Keysharp.Builtins.COM.ComValue cv)
			{
				sigText = cv.DBusSignature;
				value = cv.Value;
			}
			else
				sigText = InferVariantSignature(value);

			var nodes = DBusSignature.Parse(sigText);

			if (nodes.Length != 1)
				throw new ArgumentException($"A variant needs exactly one complete type, got '{sigText}'.");

			writer.WriteSignature(sigText);
			Write(ref writer, nodes[0], value);
		}

		/// <summary>
		/// The wire type used for a script value written into a variant with no ComValue to pin it down.
		/// Integers become 'i' when they fit and 'x' otherwise: D-Bus APIs overwhelmingly use 32-bit integers,
		/// and a script that needs 'u'/'t'/'y' says so with ComValue.
		/// </summary>
		internal static string InferVariantSignature(object value) => value switch
		{
			null => throw new ArgumentException("Cannot write an unset value into a D-Bus variant."),
			bool => "b",
			string => "s",
			double or float or decimal => "d",
			Keysharp.Builtins.Map => "a{sv}",
			Keysharp.Builtins.Array => "av",
			_ when IsIntegral(value) => ToLong(value) is var l && l >= int.MinValue && l <= int.MaxValue ? DefaultIntegerVariantSignature : "x",
			_ => throw new ArgumentException($"Cannot infer a D-Bus type for a value of type '{value.GetType().Name}'; wrap it in ComValue.")
		};

		// ---- reading ---------------------------------------------------------------------------

		internal static object[] ReadArguments(ref Reader reader, DBusSigNode[] sig)
		{
			if (sig.Length == 0)
				return [];

			var result = new object[sig.Length];

			for (var i = 0; i < sig.Length; i++)
				result[i] = Read(ref reader, sig[i]);

			return result;
		}

		internal static object Read(ref Reader reader, DBusSigNode node)
		{
			switch (node.Code)
			{
				case DBusTypeCode.Byte: return (long)reader.ReadByte();
				case DBusTypeCode.Boolean: return reader.ReadBool();
				case DBusTypeCode.Int16: return (long)reader.ReadInt16();
				case DBusTypeCode.UInt16: return (long)reader.ReadUInt16();
				case DBusTypeCode.Int32: return (long)reader.ReadInt32();
				case DBusTypeCode.UInt32: return (long)reader.ReadUInt32();
				case DBusTypeCode.Int64: return reader.ReadInt64();
				case DBusTypeCode.UInt64: return unchecked((long)reader.ReadUInt64());
				case DBusTypeCode.Double: return reader.ReadDouble();
				case DBusTypeCode.String: return reader.ReadString();
				case DBusTypeCode.ObjectPath: return reader.ReadObjectPathAsString();
				case DBusTypeCode.Signature: return reader.ReadSignatureAsString();
				case DBusTypeCode.Variant: return FromVariantValue(reader.ReadVariantValue());

				case DBusTypeCode.UnixFd:
					_ = reader.ReadHandleRaw();
					return null;   // fds are not exposed to scripts; consume the slot so later args stay aligned

				case DBusTypeCode.Array when node.IsDictArray:
				{
					var map = new Keysharp.Builtins.Map();
					var end = reader.ReadArrayStart(DBusType.Struct);   // dict entries align like structs

					while (reader.HasNext(end))
					{
						var k = Read(ref reader, node.Element.Key);
						var v = Read(ref reader, node.Element.Element);
						_ = map.Set(k, v);
					}

					return map;
				}

				case DBusTypeCode.Array:
				{
					var arr = new Keysharp.Builtins.Array();
					var end = reader.ReadArrayStart(DBusSignature.ToDBusType(node.Element));

					while (reader.HasNext(end))
						_ = arr.Push(Read(ref reader, node.Element));

					return arr;
				}

				case DBusTypeCode.Struct:
				{
					reader.AlignStruct();
					var arr = new Keysharp.Builtins.Array();

					foreach (var field in node.Fields)
						_ = arr.Push(Read(ref reader, field));

					return arr;
				}

				default:
					throw new NotSupportedException($"Cannot read D-Bus type '{node.Text}'.");
			}
		}

		/// <summary>Converts an already-decoded VariantValue (the property path returns these) to a script value.</summary>
		internal static object FromVariantValue(VariantValue v)
		{
			switch (v.Type)
			{
				case VariantValueType.Invalid: return null;
				case VariantValueType.Byte: return (long)v.GetByte();
				case VariantValueType.Bool: return v.GetBool();
				case VariantValueType.Int16: return (long)v.GetInt16();
				case VariantValueType.UInt16: return (long)v.GetUInt16();
				case VariantValueType.Int32: return (long)v.GetInt32();
				case VariantValueType.UInt32: return (long)v.GetUInt32();
				case VariantValueType.Int64: return v.GetInt64();
				case VariantValueType.UInt64: return unchecked((long)v.GetUInt64());
				case VariantValueType.Double: return v.GetDouble();
				case VariantValueType.String: return v.GetString();
				case VariantValueType.ObjectPath: return v.GetObjectPathAsString();
				case VariantValueType.Signature: return v.GetSignature().ToString();
				case VariantValueType.Variant: return FromVariantValue(v.GetVariantValue());

				case VariantValueType.Dictionary:
				{
					var map = new Keysharp.Builtins.Map();

					for (var i = 0; i < v.Count; i++)
					{
						var kv = v.GetDictionaryEntry(i);
						_ = map.Set(FromVariantValue(kv.Key), FromVariantValue(kv.Value));
					}

					return map;
				}

				case VariantValueType.Array:
				case VariantValueType.Struct:
				{
					var arr = new Keysharp.Builtins.Array();

					for (var i = 0; i < v.Count; i++)
						_ = arr.Push(FromVariantValue(v.GetItem(i)));

					return arr;
				}

				default:
					return null;
			}
		}

		// ---- value coercion --------------------------------------------------------------------

		private static bool IsIntegral(object value) =>
			value is long or int or short or sbyte or byte or ushort or uint or ulong;

		private static long ToLong(object value) => value switch
		{
			null => throw new ArgumentException("Cannot write an unset value to D-Bus."),
			bool b => b ? 1L : 0L,
			_ => value.Al()
		};

		private static double ToDouble(object value) => value?.Ad() ?? throw new ArgumentException("Cannot write an unset value to D-Bus.");

		private static bool ToBool(object value) => value switch
		{
			null => throw new ArgumentException("Cannot write an unset value to D-Bus."),
			bool b => b,
			_ => value.Al() != 0L
		};

		private static string ValidatePath(string path)
		{
			if (string.IsNullOrEmpty(path) || path[0] != '/')
				throw new ArgumentException($"'{path}' is not a valid D-Bus object path (it must start with '/').");

			return path;
		}

		private static List<object> ToList(object value, string sigText)
		{
			switch (value)
			{
				// The backing list directly: the script enumerator resolves through Reflections (needless work on a
				// per-call path) and the public indexer is 1-based, which is an easy off-by-one to write here.
				case Keysharp.Builtins.Array a:
					return a.array != null ? new List<object>(a.array) : [];
				case null:
					throw new ArgumentException($"Cannot write an unset value as D-Bus type '{sigText}'.");
				default:
					throw new ArgumentException($"D-Bus type '{sigText}' needs an Array, got '{value.GetType().Name}'.");
			}
		}

		private static List<(object Key, object Value)> ToPairs(object value, string sigText)
		{
			switch (value)
			{
				case Keysharp.Builtins.Map m:
				{
					var list = new List<(object, object)>();

					if (m.map != null)
						foreach (var kv in m.map)
							list.Add((kv.Key, kv.Value));

					return list;
				}
				case null:
					throw new ArgumentException($"Cannot write an unset value as D-Bus type '{sigText}'.");
				default:
					throw new ArgumentException($"D-Bus type '{sigText}' needs a Map, got '{value.GetType().Name}'.");
			}
		}
	}
}
#endif
