#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// An Apple event descriptor: a four-character type code and an opaque handle to the data. The same struct
	/// serves as an event, an address, a list and a record, which is why every native entry point below takes it.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal struct AEDesc
	{
		internal uint DescriptorType;
		internal nint DataHandle;
	}

	/// <summary>An error reply, or a failure of the Apple Events machinery itself. The number is the OSStatus,
	/// which scripts see so they can branch on the documented values (-600, -1743 and friends).</summary>
	internal sealed class AEException : Exception
	{
		internal int Number { get; }

		internal AEException(int number, string message) : base(message) => Number = number;
	}

	/// <summary>
	/// Owns one descriptor, and disposes it exactly once. A descriptor's data is reference counted by the Apple
	/// Event Manager, so ownership is never shared by assigning the struct around: whoever creates a descriptor
	/// wraps it here, and everything else borrows it by reference for the length of a call.
	/// </summary>
	internal sealed class AEValue : IDisposable
	{
		// A field rather than a property so it can be handed to the native calls by reference.
		internal AEDesc Desc;
		private bool disposed;

		internal AEValue()
		{
		}

		internal AEValue(AEDesc desc) => Desc = desc;

		internal uint Type => Desc.DescriptorType;

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			_ = AE.AEDisposeDesc(ref Desc);
			Desc = default;
		}
	}

	/// <summary>
	/// The native Apple Events surface and the descriptor helpers built on it. Two shapes here are easy to get
	/// wrong and silently corrupt results: most entry points return OSErr, which is 16-bit, while AESendMessage
	/// and the permission check return a 32-bit OSStatus; and sizes and timeouts are C long, which is 64-bit on
	/// every Mac this runs on.
	/// </summary>
	internal static partial class AE
	{
		private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

		// ---- type codes ------------------------------------------------------------------------

		/// <summary>Asks for a descriptor exactly as it arrived. Zero is not a type, so a read that means
		/// "whatever it already is" has to say so with this.</summary>
		internal static readonly uint TypeWildCard = AEFourCharCode.Pack("****");

		internal static readonly uint TypeNull = AEFourCharCode.Pack("null");
		internal static readonly uint TypeBoolean = AEFourCharCode.Pack("bool");
		internal static readonly uint TypeSInt16 = AEFourCharCode.Pack("shor");
		internal static readonly uint TypeSInt32 = AEFourCharCode.Pack("long");
		internal static readonly uint TypeSInt64 = AEFourCharCode.Pack("comp");
		internal static readonly uint TypeUInt32 = AEFourCharCode.Pack("magn");
		internal static readonly uint TypeIEEE64BitFloatingPoint = AEFourCharCode.Pack("doub");
		internal static readonly uint TypeUnicodeText = AEFourCharCode.Pack("utxt");
		internal static readonly uint TypeUTF8Text = AEFourCharCode.Pack("utf8");
		internal static readonly uint TypeChar = AEFourCharCode.Pack("TEXT");
		internal static readonly uint TypeType = AEFourCharCode.Pack("type");
		internal static readonly uint TypeEnumerated = AEFourCharCode.Pack("enum");
		internal static readonly uint TypeObjectSpecifier = AEFourCharCode.Pack("obj ");
		internal static readonly uint TypeAEList = AEFourCharCode.Pack("list");
		internal static readonly uint TypeAERecord = AEFourCharCode.Pack("reco");
		internal static readonly uint TypeFileURL = AEFourCharCode.Pack("furl");
		internal static readonly uint TypeAlias = AEFourCharCode.Pack("alis");
		internal static readonly uint TypeLongDateTime = AEFourCharCode.Pack("ldt ");
		internal static readonly uint TypeApplicationBundleID = AEFourCharCode.Pack("bund");
		internal static readonly uint TypeKernelProcessID = AEFourCharCode.Pack("kpid");
		internal static readonly uint TypeAbsoluteOrdinal = AEFourCharCode.Pack("abso");

		// ---- keywords and key forms -------------------------------------------------------------

		internal static readonly uint KeyDirectObject = AEFourCharCode.Pack("----");
		internal static readonly uint KeyErrorNumber = AEFourCharCode.Pack("errn");
		internal static readonly uint KeyErrorString = AEFourCharCode.Pack("errs");
		internal static readonly uint KeyAEData = AEFourCharCode.Pack("data");
		internal static readonly uint KeyAEObjectClass = AEFourCharCode.Pack("kocl");
		internal static readonly uint KeyAEDesiredClass = AEFourCharCode.Pack("want");
		internal static readonly uint KeyAEContainer = AEFourCharCode.Pack("from");
		internal static readonly uint KeyAEKeyForm = AEFourCharCode.Pack("form");
		internal static readonly uint KeyAEKeyData = AEFourCharCode.Pack("seld");

		internal static readonly uint FormPropertyID = AEFourCharCode.Pack("prop");
		internal static readonly uint FormAbsolutePosition = AEFourCharCode.Pack("indx");
		internal static readonly uint FormName = AEFourCharCode.Pack("name");
		internal static readonly uint FormUniqueID = AEFourCharCode.Pack("ID  ");

		internal static readonly uint KAEAll = AEFourCharCode.Pack("all ");
		internal static readonly uint CProperty = AEFourCharCode.Pack("prop");

		// ---- events ------------------------------------------------------------------------------

		internal static readonly uint CoreSuite = AEFourCharCode.Pack("core");
		internal static readonly uint EventGetData = AEFourCharCode.Pack("getd");
		internal static readonly uint EventSetData = AEFourCharCode.Pack("setd");
		internal static readonly uint EventCountElements = AEFourCharCode.Pack("cnte");
		internal static readonly uint ClassApplication = AEFourCharCode.Pack("capp");

		// ---- send modes and well-known errors ---------------------------------------------------

		internal const int KAEWaitReply = 0x00000003;
		internal const int KAECanInteract = 0x00000020;
		internal const short KAutoGenerateReturnID = -1;
		internal const int KAnyTransactionID = 0;

		internal const int ErrAEProcNotFound = -600;
		internal const int ErrAEConnectionInvalid = -609;
		internal const int ErrAETimeout = -1712;
		internal const int ErrAEEventNotPermitted = -1743;
		internal const int ErrAEEventNotHandled = -1708;

		// ---- native entry points -----------------------------------------------------------------
		// Every parameter here is blittable on purpose: Boolean crosses as a byte and text as bytes we lay out
		// ourselves, which keeps the source-generated marshalling free of string and bool conversions.

		[LibraryImport(CoreServices)]
		private static partial short AECreateDesc(uint typeCode, nint dataPtr, nint dataSize, out AEDesc result);

		[LibraryImport(CoreServices)]
		internal static partial short AEDisposeDesc(ref AEDesc theAEDesc);

		[LibraryImport(CoreServices)]
		private static partial short AECreateList(nint factoringPtr, nint factoredSize, byte isRecord, out AEDesc resultList);

		[LibraryImport(CoreServices)]
		private static partial short AECountItems(ref AEDesc theAEDescList, out nint theCount);

		[LibraryImport(CoreServices)]
		private static partial short AEPutDesc(ref AEDesc theAEDescList, nint index, ref AEDesc theAEDesc);

		[LibraryImport(CoreServices)]
		private static partial short AEGetNthDesc(ref AEDesc theAEDescList, nint index, uint desiredType, out uint theAEKeyword, out AEDesc result);

		[LibraryImport(CoreServices)]
		private static partial short AEPutKeyDesc(ref AEDesc theAERecord, uint theAEKeyword, ref AEDesc theAEDesc);

		[LibraryImport(CoreServices)]
		private static partial short AEGetKeyDesc(ref AEDesc theAERecord, uint theAEKeyword, uint desiredType, out AEDesc result);

		[LibraryImport(CoreServices)]
		private static partial short AECreateAppleEvent(uint theAEEventClass, uint theAEEventID, ref AEDesc target,
				short returnID, int transactionID, out AEDesc result);

		[LibraryImport(CoreServices)]
		private static partial short AEPutParamDesc(ref AEDesc theAppleEvent, uint theAEKeyword, ref AEDesc theAEDesc);

		[LibraryImport(CoreServices)]
		private static partial short AEGetParamDesc(ref AEDesc theAppleEvent, uint theAEKeyword, uint desiredType, out AEDesc result);

		[LibraryImport(CoreServices)]
		private static partial short AECoerceDesc(ref AEDesc theAEDesc, uint toType, out AEDesc result);

		[LibraryImport(CoreServices)]
		private static partial nint AEGetDescDataSize(ref AEDesc theAEDesc);

		[LibraryImport(CoreServices)]
		private static partial short AEGetDescData(ref AEDesc theAEDesc, nint dataPtr, nint maximumSize);

		// OSStatus, not OSErr: reading this as 16 bits loses the high half of every failure.
		[LibraryImport(CoreServices)]
		internal static partial int AESendMessage(ref AEDesc @event, out AEDesc reply, int sendMode, nint timeOutInTicks);

		[LibraryImport(CoreServices)]
		internal static partial int AEDeterminePermissionToAutomateTarget(ref AEDesc target, uint theAEEventClass,
				uint theAEEventID, byte askUserIfNeeded);

		[LibraryImport(CoreServices)]
		private static partial short CreateObjSpecifier(uint desiredClass, ref AEDesc theContainer, uint keyForm,
				ref AEDesc keyData, byte disposeInputs, out AEDesc objSpecifier);

		[LibraryImport(CoreServices)]
		internal static partial short AEInstallEventHandler(uint theAEEventClass, uint theAEEventID, nint handler,
				nint handlerRefcon, byte isSysHandler);

		[LibraryImport(CoreServices)]
		internal static partial short AERemoveEventHandler(uint theAEEventClass, uint theAEEventID, nint handler, byte isSysHandler);

		// ---- descriptor construction --------------------------------------------------------------

		/// <summary>The empty container every specifier chain is rooted in, and the application object itself.</summary>
		internal static AEValue Null() => new (new AEDesc { DescriptorType = TypeNull, DataHandle = 0 });

		internal static AEValue FromBytes(uint type, ReadOnlySpan<byte> data)
		{
			unsafe
			{
				fixed (byte* p = data)
				{
					Check(AECreateDesc(type, (nint)p, data.Length, out var desc), "AECreateDesc");
					return new AEValue(desc);
				}
			}
		}

		internal static AEValue FromInt32(int value) => FromBytes(TypeSInt32, BitConverter.GetBytes(value));

		internal static AEValue FromInt64(long value) => FromBytes(TypeSInt64, BitConverter.GetBytes(value));

		internal static AEValue FromDouble(double value) => FromBytes(TypeIEEE64BitFloatingPoint, BitConverter.GetBytes(value));

		internal static AEValue FromBool(bool value) => FromBytes(TypeBoolean, [value ? (byte)1 : (byte)0]);

		/// <summary>A four-character code carried as a value, which is how enumerators and class names travel.</summary>
		internal static AEValue FromCode(uint type, uint code) => FromBytes(type, BitConverter.GetBytes(code));

		/// <summary>
		/// Text goes out as typeUnicodeText, which is UTF-16 in native byte order and what every scriptable
		/// application accepts. Every Mac this runs on is little-endian.
		/// </summary>
		internal static AEValue FromString(string value) => FromBytes(TypeUnicodeText, Encoding.Unicode.GetBytes(value ?? ""));

		internal static AEValue Coerce(ref AEDesc desc, uint toType)
		{
			Check(AECoerceDesc(ref desc, toType, out var result), "AECoerceDesc");
			return new AEValue(result);
		}

		internal static bool TryCoerce(ref AEDesc desc, uint toType, out AEValue result)
		{
			if (AECoerceDesc(ref desc, toType, out var coerced) != 0)
			{
				result = null;
				return false;
			}

			result = new AEValue(coerced);
			return true;
		}

		// ---- lists and records ---------------------------------------------------------------------

		internal static AEValue NewList()
		{
			Check(AECreateList(0, 0, 0, out var list), "AECreateList");
			return new AEValue(list);
		}

		internal static AEValue NewRecord()
		{
			Check(AECreateList(0, 0, 1, out var record), "AECreateList(record)");
			return new AEValue(record);
		}

		/// <summary>Appends to a list. Apple event lists are one-based, which happens to match Keysharp's Array.</summary>
		internal static void Append(AEValue list, AEValue item)
			=> Check(AEPutDesc(ref list.Desc, 0, ref item.Desc), "AEPutDesc");

		internal static long Count(ref AEDesc list)
		{
			Check(AECountItems(ref list, out var count), "AECountItems");
			return count;
		}

		internal static AEValue Nth(ref AEDesc list, long index, out uint keyword)
		{
			Check(AEGetNthDesc(ref list, (nint)index, TypeWildCard, out keyword, out var item), "AEGetNthDesc");
			return new AEValue(item);
		}

		internal static void PutKey(AEValue record, uint keyword, AEValue value)
			=> Check(AEPutKeyDesc(ref record.Desc, keyword, ref value.Desc), "AEPutKeyDesc");

		internal static bool TryGetKey(ref AEDesc record, uint keyword, out AEValue value)
		{
			if (AEGetKeyDesc(ref record, keyword, TypeWildCard, out var desc) != 0)
			{
				value = null;
				return false;
			}

			value = new AEValue(desc);
			return true;
		}

		// ---- events ---------------------------------------------------------------------------------

		internal static AEValue NewEvent(uint eventClass, uint eventId, AEValue target)
		{
			Check(AECreateAppleEvent(eventClass, eventId, ref target.Desc, KAutoGenerateReturnID, KAnyTransactionID, out var ev),
				  "AECreateAppleEvent");
			return new AEValue(ev);
		}

		internal static void PutParam(AEValue @event, uint keyword, AEValue value)
			=> Check(AEPutParamDesc(ref @event.Desc, keyword, ref value.Desc), "AEPutParamDesc");

		internal static bool TryGetParam(ref AEDesc @event, uint keyword, out AEValue value)
		{
			if (AEGetParamDesc(ref @event, keyword, TypeWildCard, out var desc) != 0)
			{
				value = null;
				return false;
			}

			value = new AEValue(desc);
			return true;
		}

		internal static AEValue MakeSpecifier(uint desiredClass, AEValue container, uint keyForm, AEValue keyData)
		{
			// disposeInputs stays false: the AEValue wrappers own their descriptors and dispose them themselves.
			Check(CreateObjSpecifier(desiredClass, ref container.Desc, keyForm, ref keyData.Desc, 0, out var specifier),
				  "CreateObjSpecifier");
			return new AEValue(specifier);
		}

		// ---- reading data back ----------------------------------------------------------------------

		internal static byte[] GetData(ref AEDesc desc)
		{
			var size = AEGetDescDataSize(ref desc);

			if (size <= 0)
				return [];

			var buffer = new byte[size];

			unsafe
			{
				fixed (byte* p = buffer)
					Check(AEGetDescData(ref desc, (nint)p, size), "AEGetDescData");
			}

			return buffer;
		}

		/// <summary>Reads a descriptor as text, coercing whatever it holds when it is not already text.</summary>
		internal static string GetString(ref AEDesc desc)
		{
			if (desc.DescriptorType == TypeUnicodeText)
				return Encoding.Unicode.GetString(GetData(ref desc));

			if (desc.DescriptorType == TypeUTF8Text)
				return Encoding.UTF8.GetString(GetData(ref desc));

			if (!TryCoerce(ref desc, TypeUnicodeText, out var coerced))
				return "";

			using (coerced)
				return Encoding.Unicode.GetString(GetData(ref coerced.Desc));
		}

		internal static long GetInt64(ref AEDesc desc)
		{
			using var coerced = Coerce(ref desc, TypeSInt64);
			var data = GetData(ref coerced.Desc);
			return data.Length >= 8 ? BitConverter.ToInt64(data) : 0L;
		}

		internal static double GetDouble(ref AEDesc desc)
		{
			using var coerced = Coerce(ref desc, TypeIEEE64BitFloatingPoint);
			var data = GetData(ref coerced.Desc);
			return data.Length >= 8 ? BitConverter.ToDouble(data) : 0d;
		}

		internal static bool GetBool(ref AEDesc desc)
		{
			using var coerced = Coerce(ref desc, TypeBoolean);
			var data = GetData(ref coerced.Desc);
			return data.Length >= 1 && data[0] != 0;
		}

		/// <summary>A four-character code carried as data, as typeType and typeEnumerated both do.</summary>
		internal static uint GetCode(ref AEDesc desc)
		{
			var data = GetData(ref desc);
			return data.Length >= 4 ? BitConverter.ToUInt32(data) : 0u;
		}

		// ---- error handling ---------------------------------------------------------------------------

		internal static void Check(short err, string what)
		{
			if (err != 0)
				throw new AEException(err, $"{what} failed ({err}).");
		}

		/// <summary>Turns the documented failures into messages that name the fix rather than the number.</summary>
		internal static string DescribeStatus(int status, string what) => status switch
		{
			ErrAEProcNotFound => "The application is not running.",
			ErrAEConnectionInvalid => "The application stopped responding to Apple events.",
			ErrAETimeout => "The application did not answer in time.",
			ErrAEEventNotPermitted =>
			"Keysharp is not allowed to control this application. Grant it in System Settings, Privacy & Security, "
			+ "Automation. This is separate from the Accessibility permission window functions use.",
			ErrAEEventNotHandled => "The application does not handle this command.",
			_ => $"{what} failed ({status})."
		};
	}
}
#endif
