#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// Everything the marshaller needs that is not in the value itself: the terminology that turns names into
	/// keywords, and the target a replied object specifier belongs to.
	/// </summary>
	internal sealed class AEContext
	{
		internal AESdefDictionary Dictionary;
		internal AETarget Target;
		internal string Suite;
	}

	/// <summary>
	/// Converts between script values and Apple event descriptors. Unlike D-Bus, the wire is not strictly typed:
	/// applications coerce most things themselves, so this leans on the sdef's declared type only where it
	/// disambiguates (an enumerator name, a record key), and lets ComValue pin the rest.
	/// </summary>
	internal static class AEMarshal
	{
		internal static readonly uint UserFieldKeyword = AEFourCharCode.Pack("usrf");
		internal static readonly uint TypeMissingValue = AEFourCharCode.Pack("msng");

		// ---- writing ---------------------------------------------------------------------------

		/// <summary>
		/// Builds the descriptor for one script value. <paramref name="typeName"/> is the sdef type declared for
		/// the slot, which is what lets a bare string become an enumerator rather than text.
		/// </summary>
		internal static AEValue ToDescriptor(object value, AEContext context, string typeName)
		{
			var dict = context?.Dictionary;

			switch (value)
			{
				case null:
					return AE.Null();

				case ComValue cv:
					return FromComValue(cv, context);

				case ComObject co:
					return co.BuildSpecifier();

				case bool b:
					return AE.FromBool(b);

				case string s:
				{
					// A slot whose declared type is an enumeration takes the enumerator's code, not its text.
					if (TryResolveEnumerator(dict, typeName, s, out var code))
						return AE.FromCode(AE.TypeEnumerated, code);

					// The other two declared types a plain string cannot satisfy on its own. Applications take a
					// path as a file reference, not as text, so honouring the declaration is what makes passing
					// one work at all.
					if (string.Equals(typeName, "file", StringComparison.Ordinal))
						return FromFilePath(s);

					if (string.Equals(typeName, "date", StringComparison.Ordinal))
						return FromTimestamp(s);

					return AE.FromString(s);
				}

				case double or float or decimal:
					return AE.FromDouble(value.Ad());

				case Keysharp.Builtins.Array arr:
					return ToList(arr, context, typeName);

				case Keysharp.Builtins.Map map:
					return ToRecord(map, context);

				default:
					if (IsIntegral(value))
					{
						var l = value.Al();
						return l >= int.MinValue && l <= int.MaxValue ? AE.FromInt32((int)l) : AE.FromInt64(l);
					}

					throw new ArgumentException($"Cannot send a value of type '{value.GetType().Name}' as an Apple event parameter; wrap it in ComValue.");
			}
		}

		private static AEValue FromComValue(ComValue cv, AEContext context)
		{
			var type = AEFourCharCode.Pack(cv.DescType);
			var value = cv.Value;

			// The declared type says how to lay the bytes out; the ones with a fixed width are built directly and
			// anything else is written naturally and then coerced, which is what the application would do anyway.
			if (type == AE.TypeSInt32)
				return AE.FromInt32(checked((int)value.Al()));

			if (type == AE.TypeSInt16)
				return AE.FromBytes(AE.TypeSInt16, BitConverter.GetBytes(checked((short)value.Al())));

			if (type == AE.TypeSInt64)
				return AE.FromInt64(value.Al());

			if (type == AE.TypeUInt32)
				return AE.FromBytes(AE.TypeUInt32, BitConverter.GetBytes(unchecked((uint)value.Al())));

			if (type == AE.TypeIEEE64BitFloatingPoint)
				return AE.FromDouble(value.Ad());

			if (type == AE.TypeBoolean)
				return AE.FromBool(value is bool b ? b : value.Al() != 0L);

			if (type == AE.TypeUnicodeText)
				return AE.FromString(value.As());

			if (type == AE.TypeUTF8Text)
				return AE.FromBytes(AE.TypeUTF8Text, Encoding.UTF8.GetBytes(value.As()));

			if (type == AE.TypeType || type == AE.TypeEnumerated)
			{
				// A name the dictionary knows wins over reading the text as a literal code, because plenty of
				// terms are themselves four characters long ("open", "name") and the name is what a script means.
				var text = value.As();

				if (type == AE.TypeEnumerated && TryResolveEnumerator(context?.Dictionary, null, text, out var code))
					return AE.FromCode(type, code);

				if (type == AE.TypeType && context?.Dictionary?.ResolveClass(text) is AESdefClass cls && cls.Code != 0)
					return AE.FromCode(type, cls.Code);

				if (AEFourCharCode.TryPack(text.AsSpan(), out var packed))
					return AE.FromCode(type, packed);

				throw new ArgumentException($"'{text}' is neither a four-character code nor a name this application defines.");
			}

			if (type == AE.TypeFileURL || type == AE.TypeAlias)
				return FromFilePath(value.As(), type);

			if (type == AE.TypeLongDateTime)
				return FromTimestamp(value.As());

			if (type == AE.TypeNull)
				return AE.Null();

			// Anything else: write the value in its natural shape and let the Apple Event Manager coerce it.
			using var natural = ToDescriptor(value, context, null);

			if (AE.TryCoerce(ref natural.Desc, type, out var coerced))
				return coerced;

			throw new ArgumentException($"Cannot represent the value as '{cv.DescType}'.");
		}

		private static AEValue ToList(Keysharp.Builtins.Array arr, AEContext context, string typeName)
		{
			var list = AE.NewList();

			try
			{
				// The backing list directly: the public indexer is one-based, which is an easy off-by-one here.
				if (arr.array != null)
					foreach (var item in arr.array)
					{
						using var element = ToDescriptor(item, context, typeName);
						AE.Append(list, element);
					}
			}
			catch
			{
				list.Dispose();
				throw;
			}

			return list;
		}

		/// <summary>
		/// Writes a Map as an Apple event record. Keys the dictionary knows become that property's keyword; the
		/// rest go into the user field, which is the record's documented home for names outside the terminology.
		/// </summary>
		private static AEValue ToRecord(Keysharp.Builtins.Map map, AEContext context)
		{
			var record = AE.NewRecord();
			AEValue userFields = null;

			try
			{
				if (map.map != null)
					foreach (var kv in map.map)
					{
						var name = kv.Key.As();
						using var value = ToDescriptor(kv.Value, context, null);

						if (context?.Dictionary != null
								&& context.Dictionary.PropertyCodesByKey.TryGetValue(AESdef.Key(name), out var code))
						{
							AE.PutKey(record, code, value);
							continue;
						}

						userFields ??= AE.NewList();
						using var keyText = AE.FromString(name);
						AE.Append(userFields, keyText);
						AE.Append(userFields, value);
					}

				if (userFields != null)
					AE.PutKey(record, UserFieldKeyword, userFields);
			}
			catch
			{
				record.Dispose();
				throw;
			}
			finally
			{
				userFields?.Dispose();
			}

			return record;
		}

		private static bool TryResolveEnumerator(AESdefDictionary dict, string typeName, string text, out uint code)
		{
			code = 0;

			if (dict == null || string.IsNullOrEmpty(text))
				return false;

			var key = AESdef.Key(text);

			// A declared enumeration type narrows the search to its own enumerators.
			if (!string.IsNullOrEmpty(typeName) && dict.EnumerationsByName.TryGetValue(typeName, out var enumeration))
				return enumeration.Enumerators.TryGetValue(key, out code);

			if (!string.IsNullOrEmpty(typeName))
				return false;

			foreach (var candidate in dict.EnumerationsByName.Values)
				if (candidate.Enumerators.TryGetValue(key, out code))
					return true;

			return false;
		}

		/// <summary>
		/// A POSIX path as a file reference. This is the write side of what <see cref="FromFile"/> reads, so a
		/// path a script was handed can be passed straight back to the application.
		/// </summary>
		private static AEValue FromFilePath(string path, uint type = 0)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentException("A file reference needs a path.");

			if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile)
			{
				if (!Uri.TryCreate("file://" + path, UriKind.Absolute, out uri))
					throw new ArgumentException($"'{path}' is not a usable file path.");
			}

			var url = AE.FromBytes(AE.TypeFileURL, Encoding.UTF8.GetBytes(uri.AbsoluteUri));

			if (type == 0 || type == AE.TypeFileURL)
				return url;

			// A path is expressible as a URL and nothing else; the older reference types are reached by asking
			// the Apple Event Manager to convert one, which is the only thing that can resolve them.
			using (url)
			{
				if (AE.TryCoerce(ref url.Desc, type, out var coerced))
					return coerced;
			}

			throw new ArgumentException($"'{path}' could not be expressed as '{AEFourCharCode.Unpack(type)}'.");
		}

		/// <summary>The write side of <see cref="FromDate"/>: an AutoHotkey timestamp, in local time as the Apple
		/// Events epoch counts it.</summary>
		private static AEValue FromTimestamp(string timestamp)
		{
			var text = (timestamp ?? "").Trim();
			// The shorter forms an AutoHotkey timestamp may be truncated to, longest first.
			string[] formats = ["yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd", "yyyyMM", "yyyy"];

			if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
				throw new ArgumentException($"'{timestamp}' is not a timestamp in YYYYMMDDHH24MISS form.");

			var seconds = (long)(when - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)).TotalSeconds;
			return AE.FromBytes(AE.TypeLongDateTime, BitConverter.GetBytes(seconds));
		}

		private static bool IsIntegral(object value) => value is long or int or short or sbyte or byte or ushort or uint or ulong;

		// ---- reading ---------------------------------------------------------------------------

		/// <summary>Converts a reply descriptor into the script value it stands for.</summary>
		internal static object FromDescriptor(ref AEDesc desc, AEContext context)
		{
			var type = desc.DescriptorType;

			if (type == AE.TypeNull || type == TypeMissingValue)
				return "";

			if (type == AE.TypeBoolean)
				return AE.GetBool(ref desc);

			if (type == AE.TypeSInt16 || type == AE.TypeSInt32 || type == AE.TypeSInt64 || type == AE.TypeUInt32)
				return AE.GetInt64(ref desc);

			if (type == AE.TypeIEEE64BitFloatingPoint)
				return AE.GetDouble(ref desc);

			if (type == AE.TypeUnicodeText || type == AE.TypeUTF8Text || type == AE.TypeChar)
				return AE.GetString(ref desc);

			if (type == AE.TypeEnumerated)
			{
				var code = AE.GetCode(ref desc);

				if (context?.Dictionary != null && context.Dictionary.EnumeratorNamesByCode.TryGetValue(code, out var name))
					return name;

				return AEFourCharCode.Unpack(code);
			}

			if (type == AE.TypeType)
			{
				var code = AE.GetCode(ref desc);

				// "missing value" travels as a type whose payload is 'msng', not as its own descriptor type; a
				// script sees the same empty string it gets for anything else that is absent.
				if (code == TypeMissingValue)
					return "";

				if (context?.Dictionary != null && context.Dictionary.ClassesByCode.TryGetValue(code, out var cls))
					return cls.Name;

				return AEFourCharCode.Unpack(code);
			}

			if (type == AE.TypeObjectSpecifier)
				return FromSpecifier(ref desc, context);

			if (type == AE.TypeFileURL || type == AE.TypeAlias)
				return FromFile(ref desc);

			if (type == AE.TypeLongDateTime)
				return FromDate(ref desc);

			if (type == AE.TypeAEList)
				return FromList(ref desc, context);

			if (type == AE.TypeAERecord)
				return FromRecord(ref desc, context);

			// Anything unrecognised is worth one attempt as text before giving up on it.
			if (AE.TryCoerce(ref desc, AE.TypeUnicodeText, out var text))
				using (text)
					return Encoding.Unicode.GetString(AE.GetData(ref text.Desc));

			return "";
		}

		private static object FromList(ref AEDesc desc, AEContext context)
		{
			var arr = new Keysharp.Builtins.Array();
			var count = AE.Count(ref desc);

			for (var i = 1L; i <= count; i++)
			{
				using var item = AE.Nth(ref desc, i, out _);
				_ = arr.Push(FromDescriptor(ref item.Desc, context));
			}

			return arr;
		}

		private static object FromRecord(ref AEDesc desc, AEContext context)
		{
			var map = new Keysharp.Builtins.Map();
			var count = AE.Count(ref desc);

			for (var i = 1L; i <= count; i++)
			{
				using var item = AE.Nth(ref desc, i, out var keyword);

				// The user field holds name/value pairs the terminology did not cover; flattening it back keeps
				// a round trip through a record lossless from the script's point of view.
				if (keyword == UserFieldKeyword && item.Type == AE.TypeAEList)
				{
					var pairs = AE.Count(ref item.Desc);

					for (var p = 1L; p + 1 <= pairs; p += 2)
					{
						using var key = AE.Nth(ref item.Desc, p, out _);
						using var value = AE.Nth(ref item.Desc, p + 1, out _);
						_ = map.Set(AE.GetString(ref key.Desc), FromDescriptor(ref value.Desc, context));
					}

					continue;
				}

				var name = context?.Dictionary != null && context.Dictionary.PropertyNamesByCode.TryGetValue(keyword, out var known)
						   ? known
						   : AEFourCharCode.Unpack(keyword);
				_ = map.Set(name, FromDescriptor(ref item.Desc, context));
			}

			return map;
		}

		private static object FromFile(ref AEDesc desc)
		{
			// A file reference is most useful to a script as the POSIX path, which is what every file function takes.
			if (!AE.TryCoerce(ref desc, AE.TypeFileURL, out var url))
				return "";

			using (url)
			{
				var text = Encoding.UTF8.GetString(AE.GetData(ref url.Desc));

				if (System.Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile)
					return uri.LocalPath;

				return text;
			}
		}

		/// <summary>
		/// typeLongDateTime counts seconds from the classic Mac epoch, already in local time, so it is formatted
		/// as it stands rather than converted. It comes back in the timestamp shape the file and time functions
		/// use, so it can be handed straight to them.
		/// </summary>
		private static object FromDate(ref AEDesc desc)
		{
			var data = AE.GetData(ref desc);

			if (data.Length < 8)
				return "";

			var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

			try
			{
				return epoch.AddSeconds(BitConverter.ToInt64(data)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
			}
			catch (ArgumentOutOfRangeException)
			{
				return "";
			}
		}

		/// <summary>
		/// Turns a replied object specifier back into a script object. The reply carries the whole chain, so it is
		/// walked from the outside in and rebuilt in root-to-leaf order.
		/// </summary>
		private static object FromSpecifier(ref AEDesc desc, AEContext context)
		{
			var steps = new List<AESpecifierStep>();

			if (!TryReadSpecifier(ref desc, context, steps, 0) || context?.Target == null)
				return AE.GetString(ref desc);

			return new ComObject(context.Target, steps, context.Suite);
		}

		private static bool TryReadSpecifier(ref AEDesc desc, AEContext context, List<AESpecifierStep> steps, int depth)
		{
			if (depth > 32)
				return false;

			if (desc.DescriptorType == AE.TypeNull)
				return true;

			if (desc.DescriptorType != AE.TypeObjectSpecifier)
				return false;

			// A specifier reads as a record of four fields; coercing makes those fields addressable by keyword.
			if (!AE.TryCoerce(ref desc, AE.TypeAERecord, out var record))
				return false;

			using (record)
			{
				if (!AE.TryGetKey(ref record.Desc, AE.KeyAEContainer, out var container))
					return false;

				using (container)
					if (!TryReadSpecifier(ref container.Desc, context, steps, depth + 1))
						return false;

				if (!AE.TryGetKey(ref record.Desc, AE.KeyAEDesiredClass, out var wantDesc))
					return false;

				uint want;

				using (wantDesc)
					want = AE.GetCode(ref wantDesc.Desc);

				if (!AE.TryGetKey(ref record.Desc, AE.KeyAEKeyForm, out var formDesc))
					return false;

				uint form;

				using (formDesc)
					form = AE.GetCode(ref formDesc.Desc);

				if (!AE.TryGetKey(ref record.Desc, AE.KeyAEKeyData, out var keyData))
					return false;

				using (keyData)
					return TryReadStep(want, form, ref keyData.Desc, context, steps);
			}
		}

		private static bool TryReadStep(uint want, uint form, ref AEDesc keyData, AEContext context, List<AESpecifierStep> steps)
		{
			var dict = context?.Dictionary;
			var step = new AESpecifierStep { ClassCode = want };

			if (dict != null && dict.ClassesByCode.TryGetValue(want, out var cls))
				step.ClassName = cls.Name;
			else
				step.ClassName = AEFourCharCode.Unpack(want);

			if (form == AE.FormPropertyID)
			{
				step.Kind = AESpecifierKind.Property;
				step.PropertyCode = AE.GetCode(ref keyData);
				step.PropertyName = dict != null && dict.PropertyNamesByCode.TryGetValue(step.PropertyCode, out var name)
									? name
									: AEFourCharCode.Unpack(step.PropertyCode);
			}
			else if (form == AE.FormName)
			{
				step.Kind = AESpecifierKind.ElementByName;
				step.Name = AE.GetString(ref keyData);
			}
			else if (form == AE.FormUniqueID)
			{
				step.Kind = AESpecifierKind.ElementById;
				step.Id = FromDescriptor(ref keyData, context);
			}
			else if (form == AE.FormAbsolutePosition)
			{
				if (keyData.DescriptorType == AE.TypeAbsoluteOrdinal)
					step.Kind = AESpecifierKind.AllElements;
				else
				{
					step.Kind = AESpecifierKind.ElementByIndex;
					step.Index = AE.GetInt64(ref keyData);
				}
			}
			else
			{
				// Ranges and tests have no equivalent in the object model, so the value stays a plain description.
				return false;
			}

			steps.Add(step);
			return true;
		}
	}
}
#endif
