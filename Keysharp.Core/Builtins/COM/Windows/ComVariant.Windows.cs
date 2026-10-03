#if WINDOWS
namespace Keysharp.Builtins.COM
{
	//The VARIANT structure with an explicit layout.
	[StructLayout(LayoutKind.Explicit)]
	internal struct VARIANT
	{
		[FieldOffset(0)]
		internal ushort vt;
		[FieldOffset(2)]
		internal ushort wReserved1;
		[FieldOffset(4)]
		internal ushort wReserved2;
		[FieldOffset(6)]
		internal ushort wReserved3;
		// Integer types
		[FieldOffset(8)]
		internal sbyte cVal;
		[FieldOffset(8)]
		internal byte bVal;
		[FieldOffset(8)]
		internal short iVal;
		[FieldOffset(8)]
		internal ushort uiVal;
		[FieldOffset(8)]
		internal int lVal;
		[FieldOffset(8)]
		internal uint ulVal;
		[FieldOffset(8)]
		internal long llVal;
		[FieldOffset(8)]
		internal ulong ullVal;
		[FieldOffset(8)]
		internal int intVal;
		[FieldOffset(8)]
		internal uint uintVal;

		// Floating point types
		[FieldOffset(8)]
		internal float fltVal;
		[FieldOffset(8)]
		internal double dblVal;

		// Date (same as double)
		[FieldOffset(8)]
		internal double date;

		// Currency (8-byte integer in 10,000ths)
		[FieldOffset(8)]
		internal long cyVal;

		// VARIANT_BOOL is a 16-bit value: -1 (TRUE) or 0 (FALSE).
		[FieldOffset(8)]
		internal short boolVal;

		// Used for all pointer types such as BSTR, pUnk, pDisp.
		[FieldOffset(8)]
		internal nint ptrVal;

		[FieldOffset(2)]
		internal byte decimalScale;
		[FieldOffset(3)]
		internal byte decimalSign;
		[FieldOffset(4)]
		internal uint decimalHigh;

		[FieldOffset(8)]
		internal Record record;

		[StructLayout(LayoutKind.Sequential)]
		internal struct Record { internal nint Value, Info; }
	}

	internal enum Ownership { Borrowed, Owned }

	internal static unsafe partial class VariantHelper
	{
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantClear")]
		internal static partial int VariantClear(ref VARIANT variant);
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantClear")]
		internal static partial int VariantClear(nint pvarg);
		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial int VariantCopy(ref VARIANT destination, in VARIANT source);
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantChangeTypeEx")]
		internal static partial int VariantChangeTypeEx(nint destination, nint source, int lcid, ushort flags, ushort type);
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantInit")]
		internal static partial void VariantInit(nint variant);

		// Owned resources move into their wrapper; borrowed resources get an independent lifetime.
		internal static object FromVariant(ref VARIANT variant, Ownership ownership)
		{
			var vt = (VarEnum)variant.vt;
			if ((vt & VarEnum.VT_BYREF) != 0)
			{
				if (variant.ptrVal == 0)
					return null;
				// A BYREF VARIANT never owns its pointee, even when the VARIANT itself is a result.
				return FromStorage(variant.ptrVal, vt & ~VarEnum.VT_BYREF, Ownership.Borrowed);
			}

			if ((vt & VarEnum.VT_ARRAY) != 0)
			{
				var pointer = variant.ptrVal;
				if (pointer == 0)
					return null;
				if (ownership == Ownership.Borrowed)
				{
					var hr = OleAuto.SafeArrayCopy(pointer, out pointer);
					if (hr < 0)
						return Errors.OSErrorOccurredForHR(hr);
				}
				var array = new ComObjArray(vt & ~VarEnum.VT_ARRAY, pointer, takeOwnership: true);
				if (ownership == Ownership.Owned)
					variant = default;
				return array;
			}

			switch (vt)
			{
				case VarEnum.VT_EMPTY:
				case VarEnum.VT_NULL: return null;
				case VarEnum.VT_ERROR when variant.lVal == unchecked((int)0x80020004): return null;
				case VarEnum.VT_BSTR: return variant.ptrVal == 0 ? "" : Marshal.PtrToStringBSTR(variant.ptrVal);
				case VarEnum.VT_I1: return (long)variant.cVal;
				case VarEnum.VT_UI1: return (long)variant.bVal;
				case VarEnum.VT_I2: return (long)variant.iVal;
				case VarEnum.VT_UI2: return (long)variant.uiVal;
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: return (long)variant.lVal;
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: return (long)variant.ulVal;
				case VarEnum.VT_I8: return variant.llVal;
				case VarEnum.VT_UI8: return unchecked((long)variant.ullVal);
				case VarEnum.VT_R4: return (double)variant.fltVal;
				case VarEnum.VT_R8: return variant.dblVal;
				case VarEnum.VT_BOOL: return variant.boolVal != 0;
				case VarEnum.VT_DISPATCH:
				case VarEnum.VT_UNKNOWN:
				{
					var pointer = variant.ptrVal;
					if (pointer == 0)
						return DefaultObject;
					object result;
					if (vt == VarEnum.VT_UNKNOWN && Marshal.QueryInterface(pointer, in Com.IID_IEnumVARIANT, out var enumerator) >= 0)
						result = ComEnumeration.CreateEnumerator(enumerator);
					else if (Com.TryGetKeysharpObject(pointer, out var own))
						result = own;
					else if (vt == VarEnum.VT_UNKNOWN && Marshal.QueryInterface(pointer, in Com.IID_IDispatch, out var dispatch) >= 0)
						result = new ComObject { vt = VarEnum.VT_DISPATCH, Ptr = dispatch };
					else
					{
						if (ownership == Ownership.Borrowed)
							_ = Marshal.AddRef(pointer);
						result = vt == VarEnum.VT_DISPATCH
							? new ComObject { vt = vt, Ptr = pointer }
							: new ComValue { vt = vt, Ptr = pointer };
						if (ownership == Ownership.Owned)
							variant = default;
						return result;
					}
					if (ownership == Ownership.Owned)
					{
						_ = Marshal.Release(pointer);
						variant = default;
					}
					return result;
				}
				default:
				{
					VARIANT text = default;
					fixed (VARIANT* source = &variant)
						if ((int)vt < (int)VarEnum.VT_ARRAY && VariantChangeTypeEx((nint)(&text), (nint)source, Com.LOCALE_USER_DEFAULT, 0, (ushort)VarEnum.VT_BSTR) >= 0)
						{
							try { return text.ptrVal == 0 ? "" : Marshal.PtrToStringBSTR(text.ptrVal); }
							finally { _ = VariantClear(ref text); }
						}
					return new ComValue { vt = vt, item = vt == VarEnum.VT_DECIMAL ? ReadDecimal(variant) : variant.llVal };
				}
			}
		}

		internal static bool TryToVariant(object value, out VARIANT variant)
		{
			variant = default;
			if (value is ComValue wrapper)
			{
				if (wrapper.vt == VarEnum.VT_VARIANT) return TryToVariant(wrapper.Ptr, out variant);
				if (!TryBorrowArgument(wrapper, out var borrowed)) return false;
				VARIANT owned = default;
				var hr = VariantCopy(ref owned, in borrowed);
				GC.KeepAlive(wrapper);
				if (hr < 0)
				{
					_ = VariantClear(ref owned);
					_ = Errors.OSErrorOccurredForHR(hr);
					return false;
				}
				variant = owned;
				return true;
			}
			if (value is System.Array array)
				return TryCreateVariantFromManagedArray(array, CLRTypeToVarEnum(array.GetType().GetElementType()), out variant);
			variant = value switch
			{
				null => default,
				string text => new VARIANT { vt = (ushort)VarEnum.VT_BSTR, ptrVal = Marshal.StringToBSTR(text) },
				bool flag => new VARIANT { vt = (ushort)VarEnum.VT_I4, lVal = flag ? 1 : 0 },
				int number => new VARIANT { vt = (ushort)VarEnum.VT_I4, lVal = number },
				long number when number >= int.MinValue && number <= int.MaxValue => new VARIANT { vt = (ushort)VarEnum.VT_I4, lVal = (int)number },
				long number => new VARIANT { vt = (ushort)VarEnum.VT_I8, llVal = number },
				uint number => new VARIANT { vt = (ushort)VarEnum.VT_UI4, ulVal = number },
				ulong number => new VARIANT { vt = (ushort)VarEnum.VT_UI8, ullVal = number },
				short number => new VARIANT { vt = (ushort)VarEnum.VT_I2, iVal = number },
				ushort number => new VARIANT { vt = (ushort)VarEnum.VT_UI2, uiVal = number },
				byte number => new VARIANT { vt = (ushort)VarEnum.VT_UI1, bVal = number },
				sbyte number => new VARIANT { vt = (ushort)VarEnum.VT_I1, cVal = number },
				float number => new VARIANT { vt = (ushort)VarEnum.VT_R4, fltVal = number },
				double number => new VARIANT { vt = (ushort)VarEnum.VT_R8, dblVal = number },
				decimal number => DecimalVariant(number),
				DateTime date => new VARIANT { vt = (ushort)VarEnum.VT_DATE, date = date.ToOADate() },
				_ => new VARIANT { vt = (ushort)VarEnum.VT_DISPATCH, ptrVal = Com.DispatchPointer(value) }
			};
			return true;
		}

		// RawInvoke borrows wrappers; owning callers copy the encoded value.
		internal static bool TryBorrowArgument(ComValue wrapper, out VARIANT variant)
		{
			var vt = wrapper.vt;
			if ((vt & (VarEnum.VT_BYREF | VarEnum.VT_ARRAY)) != 0 || vt is VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN or VarEnum.VT_BSTR)
			{
				variant = new VARIANT { vt = (ushort)vt, ptrVal = wrapper.Ptr switch { long address => (nint)address, nint address => address, _ => 0 } };
				return true;
			}
			if (vt == VarEnum.VT_VARIANT)
				return TryToVariant(wrapper.Ptr, out variant);
			if (vt is VarEnum.VT_EMPTY or VarEnum.VT_NULL)
			{
				variant = new VARIANT { vt = (ushort)vt };
				return true;
			}
			// An explicitly tagged integer carries native bits, rather than a number to range-check or coerce.
			if (wrapper.Ptr is long integer && vt is VarEnum.VT_I1 or VarEnum.VT_UI1 or VarEnum.VT_I2 or VarEnum.VT_UI2
				or VarEnum.VT_I4 or VarEnum.VT_UI4 or VarEnum.VT_INT or VarEnum.VT_UINT or VarEnum.VT_I8 or VarEnum.VT_UI8 or VarEnum.VT_ERROR or VarEnum.VT_CY)
			{
				variant = new VARIANT { vt = (ushort)vt, llVal = integer };
				return true;
			}
			// These payloads have already been normalized by ComValue.Ptr.
			if (vt == VarEnum.VT_BOOL && wrapper.Ptr is bool flag)
				variant = new VARIANT { vt = (ushort)vt, boolVal = (short)(flag ? -1 : 0) };
			else if (vt is VarEnum.VT_R8 or VarEnum.VT_DATE && wrapper.Ptr is double number)
				variant = new VARIANT { vt = (ushort)vt, dblVal = number };
			else
				return TryToTypedVariant(wrapper.Ptr, vt, out variant);
			return true;
		}

		internal static bool TryToTypedVariant(object value, VarEnum type, out VARIANT result)
		{
			result = default;
			if (type == VarEnum.VT_VARIANT)
				return TryToVariant(value, out result);
			if (type == VarEnum.VT_BOOL)
			{
				result = new VARIANT { vt = (ushort)type, boolVal = (short)(value.Ab() ? -1 : 0) };
				return true;
			}
			if (value is long integer && type is VarEnum.VT_I1 or VarEnum.VT_UI1 or VarEnum.VT_I2 or VarEnum.VT_UI2
				or VarEnum.VT_I4 or VarEnum.VT_UI4 or VarEnum.VT_INT or VarEnum.VT_UINT or VarEnum.VT_I8 or VarEnum.VT_UI8 or VarEnum.VT_ERROR or VarEnum.VT_CY)
			{
				result = new VARIANT { vt = (ushort)type, llVal = type == VarEnum.VT_CY ? unchecked(integer * 10000) : integer };
				return true;
			}
			if (type is VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN && value == null)
				result = new VARIANT { vt = (ushort)type };
			else
			{
				if (!TryToVariant(value, out var source)) return false;
				if (source.vt == (ushort)type)
				{
					result = source;
					return true;
				}
				VARIANT converted = default;
				try
				{
					var hr = VariantChangeTypeEx((nint)(&converted), (nint)(&source), Com.LOCALE_USER_DEFAULT, 0, (ushort)type);
					if (hr < 0)
					{
						_ = VariantClear(ref converted);
						_ = Errors.OSErrorOccurredForHR(hr);
						return false;
					}
					result = converted;
				}
				finally { _ = VariantClear(ref source); }
			}
			return true;
		}

		internal static object[] SafeArrayElements(Array array, HashSet<Array> enclosing = null)
		{
			var elements = array.array.ToArray();
			for (var i = 0; i < elements.Length; i++)
				if (elements[i] is Array inner && (enclosing ??= new(ReferenceEqualityComparer.Instance) { array }).Add(inner))
				{
					elements[i] = SafeArrayElements(inner, enclosing);
					_ = enclosing.Remove(inner);
				}
			return elements;
		}

		internal static bool TryCreateVariantFromManagedArray(System.Array array, VarEnum type, out VARIANT result)
		{
			result = default;
			if (type == VarEnum.VT_EMPTY) type = VarEnum.VT_VARIANT;
			if (array.Rank != 1)
			{
				_ = Errors.ValueErrorOccurred("A CLR SAFEARRAY argument must have one dimension.");
				return false;
			}
			var pointer = OleAuto.SafeArrayCreateVectorEx((ushort)type, 0, (uint)array.Length, 0);
			if (pointer == 0)
			{
				_ = Errors.OSErrorOccurredForHR(unchecked((int)0x8007000E));
				return false;
			}
			var complete = false;
			try
			{
				var hr = OleAuto.SafeArrayAccessData(pointer, out var data);
				if (hr < 0)
				{
					_ = Errors.OSErrorOccurredForHR(hr);
					return false;
				}
				try
				{
					var stride = (int)OleAuto.SafeArrayGetElemsize(pointer);
					var lower = array.GetLowerBound(0);
					for (var i = 0; i < array.Length; i++)
					{
						if (!TryToTypedVariant(array.GetValue(lower + i), type, out var element)) return false;
						WriteStorage(data + i * stride, type, in element);
					}
				}
				finally { _ = OleAuto.SafeArrayUnaccessData(pointer); }
				complete = true;
				result = new VARIANT { vt = (ushort)(VarEnum.VT_ARRAY | type), ptrVal = pointer };
				return true;
			}
			finally { if (!complete) _ = OleAuto.SafeArrayDestroy(pointer); }
		}

		internal static bool TryCreateByRefVariant(object value, VarEnum type, out VARIANT result)
		{
			result = default;
			if (!TryToTypedVariant(value, type, out var inner)) return false;
			nint cell = 0;
			try
			{
				cell = Marshal.AllocHGlobal(sizeof(VARIANT));
				WriteStorage(cell, type, in inner);
				result = new VARIANT { vt = (ushort)(VarEnum.VT_BYREF | type), ptrVal = cell };
				return true;
			}
			catch { Marshal.FreeHGlobal(cell); _ = VariantClear(ref inner); throw; }
		}

		internal static void WriteByRefVariant(in VARIANT variant, object value)
		{
			var type = (VarEnum)variant.vt;
			if ((type & VarEnum.VT_BYREF) == 0 || variant.ptrVal == 0)
				return;
			type &= ~VarEnum.VT_BYREF;
			if (!TryToTypedVariant(value, type, out var replacement)) return;
			var previous = ReadStorage(variant.ptrVal, type);
			var hr = VariantClear(ref previous);
			if (hr < 0)
			{
				_ = VariantClear(ref replacement);
				_ = Errors.OSErrorOccurredForHR(hr);
				return;
			}
			WriteStorage(variant.ptrVal, type, in replacement);
		}

		internal static void CleanupByRefVariant(in VARIANT variant)
		{
			if (((VarEnum)variant.vt & VarEnum.VT_BYREF) == 0 || variant.ptrVal == 0)
				return;
			var value = ReadStorage(variant.ptrVal, (VarEnum)variant.vt & ~VarEnum.VT_BYREF);
			_ = VariantClear(ref value);
			Marshal.FreeHGlobal(variant.ptrVal);
		}

		// Adapters between a VARIANT and a typed cell; conversion stays in FromVariant and TryToTypedVariant.
		internal static object FromStorage(nint data, VarEnum type, Ownership ownership)
		{
			if (type == VarEnum.VT_VARIANT)
				return FromVariant(ref *(VARIANT*)data, ownership);
			var value = ReadStorage(data, type);
			var result = FromVariant(ref value, ownership);
			if (ownership == Ownership.Owned && value.vt == 0 && IsPointerType(type))
				*(nint*)data = 0;
			return result;
		}

		internal static VARIANT ReadStorage(nint data, VarEnum type)
		{
			if (type == VarEnum.VT_VARIANT) return *(VARIANT*)data;
			VARIANT value = default;
			var size = StorageSize(type);
			var destination = type == VarEnum.VT_DECIMAL ? (byte*)(&value) : (byte*)(&value) + 8;
			System.Buffer.MemoryCopy((void*)data, destination, sizeof(VARIANT), size);
			value.vt = (ushort)type;
			return value;
		}

		internal static void WriteStorage(nint data, VarEnum type, in VARIANT value)
		{
			fixed (VARIANT* source = &value)
				System.Buffer.MemoryCopy(type is VarEnum.VT_VARIANT or VarEnum.VT_DECIMAL ? (byte*)source : (byte*)source + 8,
					(void*)data, StorageSize(type), StorageSize(type));
			if (type == VarEnum.VT_DECIMAL) *(ushort*)data = 0;
		}

		internal static int StorageSize(VarEnum type) => type switch
		{
			VarEnum.VT_I1 or VarEnum.VT_UI1 => 1,
			VarEnum.VT_I2 or VarEnum.VT_UI2 or VarEnum.VT_BOOL => 2,
			VarEnum.VT_I4 or VarEnum.VT_UI4 or VarEnum.VT_INT or VarEnum.VT_UINT or VarEnum.VT_ERROR or VarEnum.VT_R4 => 4,
			VarEnum.VT_I8 or VarEnum.VT_UI8 or VarEnum.VT_R8 or VarEnum.VT_DATE or VarEnum.VT_CY => 8,
			VarEnum.VT_DECIMAL => 16,
			VarEnum.VT_VARIANT => sizeof(VARIANT),
			_ when IsPointerType(type) => IntPtr.Size,
			_ => throw new NotSupportedException($"COM storage for {type} is not supported.")
		};

		private static bool IsPointerType(VarEnum type) => (type & VarEnum.VT_ARRAY) != 0 || type is VarEnum.VT_BSTR or VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN;

		private static VARIANT DecimalVariant(decimal number)
		{
			Span<int> bits = stackalloc int[4];
			decimal.GetBits(number, bits);
			return new VARIANT { vt = (ushort)VarEnum.VT_DECIMAL, decimalScale = (byte)(bits[3] >> 16), decimalSign = (byte)(bits[3] >> 24),
				decimalHigh = (uint)bits[2], ullVal = (uint)bits[0] | ((ulong)(uint)bits[1] << 32) };
		}

		private static decimal ReadDecimal(in VARIANT variant) => new((int)variant.ullVal, (int)(variant.ullVal >> 32), (int)variant.decimalHigh, variant.decimalSign != 0, variant.decimalScale);

		internal static Type VarEnumToCLRType(VarEnum vt)
		{
			if ((vt & VarEnum.VT_ARRAY) != 0)
				return VarEnumToCLRType(vt & ~VarEnum.VT_ARRAY).MakeArrayType();

			return vt switch
			{
				VarEnum.VT_I1 => typeof(sbyte),
				VarEnum.VT_UI1 => typeof(byte),
				VarEnum.VT_I2 => typeof(short),
				VarEnum.VT_UI2 => typeof(ushort),
				VarEnum.VT_I4 or VarEnum.VT_INT => typeof(int),
				VarEnum.VT_UI4 or VarEnum.VT_UINT or VarEnum.VT_ERROR => typeof(uint),
				VarEnum.VT_I8 => typeof(long),
				VarEnum.VT_UI8 => typeof(ulong),
				VarEnum.VT_R4 => typeof(float),
				VarEnum.VT_R8 or VarEnum.VT_DATE => typeof(double),
				VarEnum.VT_DECIMAL or VarEnum.VT_CY => typeof(decimal),
				VarEnum.VT_BOOL => typeof(bool),
				VarEnum.VT_BSTR => typeof(string),
				VarEnum.VT_SAFEARRAY => typeof(object[]),   // bare VT_SAFEARRAY (no element T): treat as object[]
				_ => typeof(object),
			};
		}

		internal static VarEnum CLRTypeToVarEnum(Type t)
		{
			if (t.IsArray)
			{
				var et = t.GetElementType() ?? typeof(object);
				var baseVt = CLRTypeToVarEnum(et);
				if (baseVt == VarEnum.VT_EMPTY) baseVt = VarEnum.VT_VARIANT;
				return VarEnum.VT_ARRAY | baseVt;
			}

			if (t == typeof(string)) return VarEnum.VT_BSTR;
			if (t == typeof(bool)) return VarEnum.VT_BOOL;
			if (t == typeof(byte)) return VarEnum.VT_UI1;
			if (t == typeof(sbyte)) return VarEnum.VT_I1;
			if (t == typeof(short)) return VarEnum.VT_I2;
			if (t == typeof(ushort)) return VarEnum.VT_UI2;
			if (t == typeof(int)) return VarEnum.VT_I4;
			if (t == typeof(uint)) return VarEnum.VT_UI4;
			if (t == typeof(long)) return VarEnum.VT_I8;
			if (t == typeof(ulong)) return VarEnum.VT_UI8;
			if (t == typeof(float)) return VarEnum.VT_R4;
			if (t == typeof(double)) return VarEnum.VT_R8;
			if (t == typeof(decimal)) return VarEnum.VT_DECIMAL;
			if (t == typeof(DateTime)) return VarEnum.VT_DATE;
			if (t == typeof(object)) return VarEnum.VT_VARIANT;
			// Fallback for COM-ref types:
			if (typeof(Any).IsAssignableFrom(t)) return VarEnum.VT_DISPATCH;

			// Unknown → let the old path (nested VT_VARIANT) handle it.
			return VarEnum.VT_EMPTY;
		}
	}
	/// <summary>
	/// Describes the bounds (element count and lower bound) of a single dimension of a SAFEARRAY.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal struct SAFEARRAYBOUND
	{
		/// <summary>
		/// Number of elements in this dimension.
		/// </summary>
		public uint cElements;
		/// <summary>
		/// Lower bound (starting index) of this dimension.
		/// </summary>
		public int lLbound;
	}

	/// <summary>
	/// Contains P/Invoke declarations for OLE Automation APIs.
	/// </summary>
	internal static partial class OleAuto
	{
		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial int LoadRegTypeLib(in Guid rguid, short wVerMajor, short wVerMinor, int lcid, out nint pptlib);

		/// <summary>
		/// Creates a new SafeArray of the specified variant type and dimensions.
		/// </summary>
		/// <param name="vt">The VARTYPE of each element (e.g. VT_VARIANT).</param>
		/// <param name="cDims">The number of dimensions (1-8).</param>
		/// <param name="rgsabound">Array of bounds for each dimension.</param>
		/// <returns>A pointer to the new SAFEARRAY; IntPtr.Zero on failure.</returns>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayCreate")]
		public static partial nint SafeArrayCreate(
			short vt,
			uint cDims,
			[In] SAFEARRAYBOUND[] rgsabound);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayCreateVectorEx")]
		public static partial nint SafeArrayCreateVectorEx(ushort vt, int lLbound, uint cElements, nint pvExtra);
		/// <summary>
		/// Retrieves the number of dimensions in a SafeArray.
		/// </summary>
		/// <param name="psa">Pointer to the SAFEARRAY.</param>
		/// <returns>The number of dimensions, or a negative error code.</returns>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayGetDim")]
		public static partial int SafeArrayGetDim(
			nint psa);
		/// <summary>
		/// Retrieves the upper bound (max index) for a specified dimension.
		/// </summary>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayGetUBound")]
		public static partial int SafeArrayGetUBound(
			nint psa,
			uint nDim,
			out int plUbound);
		/// <summary>
		/// Retrieves the lower bound for a specified dimension.
		/// </summary>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayGetLBound")]
		public static partial int SafeArrayGetLBound(
			nint psa,
			uint nDim,
			out int plLbound);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayGetVartype")]
		public static partial int SafeArrayGetVartype(
			nint psa,
			out ushort vt);
		/// <summary>
		/// Creates a copy of a SafeArray.
		/// </summary>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayCopy")]
		public static partial int SafeArrayCopy(
			nint psa,
			out nint ppsaOut);
		/// <summary>
		/// Destroys a SafeArray, releasing its memory.
		/// </summary>
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayDestroy")]
		public static partial int SafeArrayDestroy(
			nint psa);
		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial int SafeArrayLock(nint psa);

		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial int SafeArrayUnlock(nint psa);

		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial int SafeArrayPtrOfIndex(nint psa, [In] int[] indices, out nint data);

		[LibraryImport(WindowsAPI.oleaut)]
		internal static partial uint SafeArrayGetElemsize(nint psa);

		[LibraryImport(WindowsAPI.oleaut)]
		public static partial int SysStringLen(nint bstr);

		[LibraryImport(WindowsAPI.oleaut)]
		public static partial nint SysAllocStringLen(nint src, int len);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayAccessData")]
		internal static partial int SafeArrayAccessData(nint psa, out nint ppvData);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayUnaccessData")]
		internal static partial int SafeArrayUnaccessData(nint psa);
	}

}
#endif
