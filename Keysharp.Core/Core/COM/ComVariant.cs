#if WINDOWS
namespace Keysharp.Core.COM
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

		// Record pointer
		[FieldOffset(8)]
		internal nint pvRecord;     // For VT_RECORD
		[FieldOffset(16)]
		internal nint pRecInfo;     // IRecordInfo*
	}

	internal static class VariantConstants
	{
		internal const ushort VT_EMPTY = 0;
		internal const ushort VT_NULL = 1;
		internal const ushort VT_I2 = 2;
		internal const ushort VT_I4 = 3;
		internal const ushort VT_R4 = 4;
		internal const ushort VT_R8 = 5;
		internal const ushort VT_CY = 6;
		internal const ushort VT_DATE = 7;
		internal const ushort VT_BSTR = 8;
		internal const ushort VT_DISPATCH = 9;
		internal const ushort VT_ERROR = 10;
		internal const ushort VT_BOOL = 11;
		internal const ushort VT_VARIANT = 12;
		internal const ushort VT_UNKNOWN = 13;
		//Extend as needed…
	}
	internal static partial class VariantHelper
	{
		//Clears the VARIANT and frees any allocated memory (such as BSTRs).
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantClear")]
		internal static partial int VariantClear(ref VARIANT variant);
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantClear")]
		internal static partial int VariantClear(nint pvarg);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantChangeTypeEx")]
		internal static partial int VariantChangeTypeEx(nint pvargDest, nint pvarSrc, int lcid, ushort wFlags, ushort vt);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "VariantInit")]
		internal static partial void VariantInit(nint pvar);

		internal static int VariantChangeTypeEx(out object dest, object src, int lcid, ushort wFlags, ushort vt /* VARTYPE */)
		{
			nint pSrc = Marshal.AllocHGlobal(16 * 2);   // big enough for VARIANT (x86/x64). Oversize for simplicity.
			nint pDst = Marshal.AllocHGlobal(16 * 2);
			try
			{
				VariantInit(pSrc);
				VariantInit(pDst);

				// Fill native VARIANT from managed object
				Marshal.GetNativeVariantForObject(src, pSrc);

				int hr = VariantChangeTypeEx(pDst, pSrc, lcid, wFlags, vt);
				if (hr != 0) // FAILED(hr)
				{
					dest = null;
					return hr;
				}

				// Convert resulting native VARIANT back to managed object
				dest = Marshal.GetObjectForNativeVariant(pDst);

			}
			finally
			{
				// Clean up native VARIANTs
				_ = VariantHelper.VariantClear(pSrc);
				_ = VariantHelper.VariantClear(pDst);
				Marshal.FreeHGlobal(pSrc);
				Marshal.FreeHGlobal(pDst);
			}
			return 0;
		}


		internal static VARIANT CreateVariantFromInt(int value)
		{
			VARIANT variant = new VARIANT();
			variant.vt = VariantConstants.VT_I4;
			variant.lVal = value;
			return variant;
		}

		internal static VARIANT CreateVariantFromDouble(double value)
		{
			VARIANT variant = new VARIANT();
			variant.vt = VariantConstants.VT_R8;
			variant.dblVal = value;
			return variant;
		}

		internal static VARIANT CreateVariantFromBool(bool value)
		{
			VARIANT variant = new VARIANT();
			variant.vt = VariantConstants.VT_BOOL;
			//In COM, VARIANT_TRUE is -1 and VARIANT_FALSE is 0.
			variant.boolVal = (short)(value ? -1 : 0);
			return variant;
		}

		internal static VARIANT CreateVariantFromString(string value)
		{
			VARIANT variant = new VARIANT();
			variant.vt = VariantConstants.VT_BSTR;
			//Marshal the string to a BSTR. Remember to clear the VARIANT later to free this memory.
			variant.ptrVal = Marshal.StringToBSTR(value);
			return variant;
		}

		internal static void ClearVariant(ref VARIANT variant)
		{
			_ = VariantClear(ref variant);
		}

		internal static VARIANT ValueToVariant(object value)
		{
			if (value is ComValue co) return co.ToVariant();

			var variant = new VARIANT();

			if (value == null)
			{
				variant.vt = (ushort)VarEnum.VT_EMPTY;
				return variant;
			}
			if (value is Array ksarr)
				value = ksarr.array.ToArray();

			switch (value)
			{
				case string s:
					variant.vt = (ushort)VarEnum.VT_BSTR;
					variant.ptrVal = Marshal.StringToBSTR(s);
					break;

				case int i:
					variant.vt = (ushort)VarEnum.VT_I4;
					variant.lVal = i;
					break;

				case long l when l >= int.MinValue && l <= int.MaxValue:
					variant.vt = (ushort)VarEnum.VT_I4;
					variant.lVal = (int)l;
					break;

				case long l:
					variant.vt = (ushort)VarEnum.VT_I8;
					variant.llVal = l;
					break;

				case double d:
					variant.vt = (ushort)VarEnum.VT_R8;
					variant.dblVal = d;
					break;

				case float f:
					variant.vt = (ushort)VarEnum.VT_R4;
					variant.fltVal = f;
					break;

				case bool b:
					variant.vt = (ushort)VarEnum.VT_BOOL;
					variant.boolVal = (short)(b ? -1 : 0);
					break;

				case short sh:
					variant.vt = (ushort)VarEnum.VT_I2;
					variant.iVal = sh;
					break;

				case ushort us:
					variant.vt = (ushort)VarEnum.VT_UI2;
					variant.uiVal = us;
					break;

				case byte by:
					variant.vt = (ushort)VarEnum.VT_UI1;
					variant.bVal = by;
					break;

				case sbyte sb:
					variant.vt = (ushort)VarEnum.VT_I1;
					variant.cVal = sb;
					break;

				case System.Array arr:
					return CreateVariantFromManagedArray(arr);

				default:
					// Try to wrap as IDispatch for Keysharp objects
					try
					{
						variant.vt = (ushort)VarEnum.VT_DISPATCH;
						variant.ptrVal = Marshal.GetIDispatchForObject(value);
					}
					catch
					{
						// If that fails, set as VT_EMPTY
						variant.vt = (ushort)VarEnum.VT_EMPTY;
					}
					break;
			}

			return variant;
		}
		private static VARIANT CreateVariantFromManagedArray(System.Array arr)
		{
			// Decide element VT from the CLR element type. Heterogenous/unknown → VARIANT.
			Type elemClr = arr?.GetType().GetElementType() ?? typeof(object);

			VarEnum elemVt = CLRTypeToVarEnum(elemClr);
			if (elemVt == VarEnum.VT_EMPTY || elemClr == typeof(object))
				elemVt = VarEnum.VT_VARIANT;

			return CreateVariantFromManagedArray(arr, elemVt);
		}

		internal unsafe static VARIANT CreateVariantFromManagedArray(System.Array arr, VarEnum elemVt)
		{
			// 1D only (common for Automation). Extend similarly for multi-d if you need.
			uint len = (uint)(arr?.Length ?? 0);

			nint psa = OleAuto.SafeArrayCreateVectorEx((ushort)elemVt, 0, len, 0);

			if (psa == 0)
				return new VARIANT { vt = (ushort)VarEnum.VT_EMPTY };

			if (arr != null)
			{
				for (int i = 0; i < arr.Length; i++)
				{
					int[] idx = { i };
					object v = arr.GetValue(i);

					int hr;
					switch (elemVt)
					{
						case VarEnum.VT_VARIANT:
							{
								// Build element VARIANT and pass pointer
								VARIANT ev = ValueToVariant(v);
								try { hr = OleAuto.SafeArrayPutElementPtr(psa, idx, (nint)(&ev)); }
								finally { _ = VariantClear((nint)(&ev)); }
								break;
							}

						case VarEnum.VT_BSTR:
							{
								nint b = v == null ? 0 : Marshal.StringToBSTR(v.ToString());
								hr = OleAuto.SafeArrayPutElementPtr(psa, idx, b);
								if (b != 0) WindowsAPI.SysFreeString(b);
								break;
							}

						case VarEnum.VT_DISPATCH:
						case VarEnum.VT_UNKNOWN:
							{
								nint p = 0;
								bool rel = false;
								if (v is long lp) p = (nint)lp;
								else if (v is nint ip) p = ip;
								else if (v != null)
								{
									p = elemVt == VarEnum.VT_DISPATCH ? Marshal.GetIDispatchForObject(v) : Marshal.GetIUnknownForObject(v);
									rel = true; // we own this temp
								}
								try { hr = OleAuto.SafeArrayPutElementPtr(psa, idx, p); }
								finally { if (rel && p != 0) try { Marshal.Release(p); } catch { } }
								break;
							}

						case VarEnum.VT_DATE:
							{
								double oa = v is DateTime dt ? dt.ToOADate() : Convert.ToDouble(v);
								hr = OleAuto.SafeArrayPutElement(psa, idx, oa);
								break;
							}

						case VarEnum.VT_BOOL:
							{
								bool b = v is bool bb ? bb : Script.ForceBool(v);
								// Using the object overload lets OleAut do VARIANT_BOOL conversion.
								hr = OleAuto.SafeArrayPutElement(psa, idx, b);
								break;
							}

						// Numerics & decimal – use object overload; OleAut will coerce.
						case VarEnum.VT_I1: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToSByte(v)); break;
						case VarEnum.VT_UI1: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToByte(v)); break;
						case VarEnum.VT_I2: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToInt16(v)); break;
						case VarEnum.VT_UI2: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToUInt16(v)); break;
						case VarEnum.VT_I4:
						case VarEnum.VT_INT: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToInt32(v)); break;
						case VarEnum.VT_UI4:
						case VarEnum.VT_UINT: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToUInt32(v)); break;
						case VarEnum.VT_I8: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToInt64(v)); break;
						case VarEnum.VT_UI8: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToUInt64(v)); break;
						case VarEnum.VT_R4: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToSingle(v)); break;
						case VarEnum.VT_R8: hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToDouble(v)); break;
						case VarEnum.VT_DECIMAL:
							hr = OleAuto.SafeArrayPutElement(psa, idx, Convert.ToDecimal(v)); break;

						default:
							// Fallback to VARIANT elements for exotic types.
							VARIANT ev2 = ValueToVariant(v);
							try { hr = OleAuto.SafeArrayPutElementPtr(psa, idx, (nint)(&ev2)); }
							finally { _ = VariantClear((nint)(&ev2)); }
							break;
					}

					_ = Errors.OSErrorOccurredForHR(hr);
					if (hr < 0) { OleAuto.SafeArrayDestroy(psa); return new VARIANT { vt = (ushort)VarEnum.VT_EMPTY }; }
				}
			}

			return new VARIANT { vt = (ushort)(VarEnum.VT_ARRAY | elemVt), ptrVal = psa };
		}


		internal static unsafe VARIANT CreateByRefVariant(object value)
		{
			// Create a VT_BYREF|VT_VARIANT that points at a heap VARIANT holding 'value'.
			VARIANT inner = ValueToVariant(value);

			nint pInner = Marshal.AllocHGlobal(Marshal.SizeOf<VARIANT>());
			Marshal.StructureToPtr(inner, pInner, false);

			return new VARIANT
			{
				vt = (ushort)(VarEnum.VT_BYREF | VarEnum.VT_VARIANT),
				ptrVal = pInner
			};
		}

		internal static VARIANT CreateByRefVariantTyped(object value, VarEnum baseVt)
		{
			// Special-case: BYREF|VARIANT uses nested-variant storage (your existing method).
			if (baseVt == VarEnum.VT_VARIANT)
				return CreateByRefVariant(value); // allocates VARIANT and sets ptrVal

			// BYREF SAFEARRAY: allocate pointer-to-SAFEARRAY storage and write initial pointer.
			if ((baseVt & VarEnum.VT_ARRAY) != 0)
			{
				var v = new VARIANT { vt = (ushort)(baseVt | VarEnum.VT_BYREF) };
				nint pStorage = Marshal.AllocHGlobal(IntPtr.Size);
				nint psa = 0;

				if (value is ComObjArray coa) psa = coa._psa;
				else if (value is long lp) psa = (nint)lp;
				else if (value is nint ip) psa = ip;
				else if (value is System.Array arr) psa = CreateVariantFromManagedArray(arr).ptrVal;
				// else leave psa = 0 (pure OUT)

				Marshal.WriteIntPtr(pStorage, psa);
				v.ptrVal = pStorage;
				return v;
			}

			// Normal scalars, BSTR, interfaces: allocate pointer-sized storage cell and fill it.
			var byref = new VARIANT { vt = (ushort)(baseVt | VarEnum.VT_BYREF) };
			nint cell = Marshal.AllocHGlobal(IntPtr.Size);

			switch (baseVt)
			{
				// Integers
				case VarEnum.VT_I1: Marshal.WriteByte(cell, unchecked((byte)Convert.ToSByte(value ?? 0))); break;
				case VarEnum.VT_UI1: Marshal.WriteByte(cell, Convert.ToByte(value ?? 0)); break;
				case VarEnum.VT_I2: Marshal.WriteInt16(cell, Convert.ToInt16(value ?? 0)); break;
				case VarEnum.VT_UI2: Marshal.WriteInt16(cell, unchecked((short)Convert.ToUInt16(value ?? 0))); break;
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: Marshal.WriteInt32(cell, Convert.ToInt32(value ?? 0)); break;
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: Marshal.WriteInt32(cell, unchecked((int)Convert.ToUInt32(value ?? 0))); break;
				case VarEnum.VT_I8: Marshal.WriteInt64(cell, Convert.ToInt64(value ?? 0)); break;
				case VarEnum.VT_UI8: Marshal.WriteInt64(cell, unchecked((long)Convert.ToUInt64(value ?? 0))); break;

				// Floating point
				case VarEnum.VT_R4:
					{
						float f = Convert.ToSingle(value ?? 0);
						var bytes = BitConverter.GetBytes(f);
						Marshal.Copy(bytes, 0, cell, bytes.Length);
						break;
					}
				case VarEnum.VT_R8:
					{
						double d = Convert.ToDouble(value ?? 0);
						var bytes = BitConverter.GetBytes(d);
						Marshal.Copy(bytes, 0, cell, bytes.Length);
						break;
					}

				// DATE (double, OADate)
				case VarEnum.VT_DATE:
					{
						double od = value is DateTime dt ? dt.ToOADate() : Convert.ToDateTime(value ?? default(DateTime)).ToOADate();
						var bytes = BitConverter.GetBytes(od);
						Marshal.Copy(bytes, 0, cell, bytes.Length);
						break;
					}

				// BOOL (VARIANT_BOOL: short -1/0)
				case VarEnum.VT_BOOL:
					{
						short vb = (short)((value is bool b ? b : Convert.ToBoolean(value ?? false)) ? -1 : 0);
						Marshal.WriteInt16(cell, vb);
						break;
					}

				// BSTR*
				case VarEnum.VT_BSTR:
					{
						nint bstr = 0;
						if (value is string s && s != null)
							bstr = Marshal.StringToBSTR(s);
						Marshal.WriteIntPtr(cell, bstr);
						break;
					}

				// Interfaces: IDispatch** / IUnknown**
				case VarEnum.VT_DISPATCH:
				case VarEnum.VT_UNKNOWN:
					{
						nint p = 0;
						if (value is long lp) p = (nint)lp;
						else if (value is nint ip) p = ip;
						else if (value != null)
							p = baseVt == VarEnum.VT_DISPATCH
								? Marshal.GetIDispatchForObject(value)
								: Marshal.GetIUnknownForObject(value);
						Marshal.WriteIntPtr(cell, p);
						break;
					}

				default:
					// Fallback: nested-variant BYREF (covers DECIMAL, etc.)
					Marshal.FreeHGlobal(cell);
					return CreateByRefVariant(value);
			}

			byref.ptrVal = cell;

			return byref;
		}

		// ---------- Read value back from a BYREF variant (typed AND nested-variant) ----------
		internal static object ReadByRefVariant(in VARIANT v)
		{
			var vt = (VarEnum)v.vt;
			if ((vt & VarEnum.VT_BYREF) == 0)
				throw new ArgumentException("ReadByRefVariant called on non-BYREF VARIANT.");

			var baseVt = vt & ~VarEnum.VT_BYREF;

			switch (baseVt)
			{
				// ---- Strings ----
				case VarEnum.VT_BSTR:
					unsafe
					{
						if (v.ptrVal == 0) return "";
						var pBstr = *((nint*)v.ptrVal);
						if (pBstr == 0) return "";
						return Marshal.PtrToStringBSTR(pBstr) ?? "";
					}

				// ---- Interfaces ----
				case VarEnum.VT_DISPATCH:
					unsafe
					{
						if (v.ptrVal == 0) return new ComObject((long)vt, 0L);
						var p = *((nint*)v.ptrVal);
						return new ComObject((long)VarEnum.VT_DISPATCH, (long)p);
					}

				case VarEnum.VT_UNKNOWN:
					unsafe
					{
						if (v.ptrVal == 0) return new ComValue((long)vt, 0L);
						var p = *((nint*)v.ptrVal);
						return new ComValue((long)VarEnum.VT_UNKNOWN, (long)p);
					}

				// ---- VARIANT byref: expose the contained value, not the pointer ----
				case VarEnum.VT_VARIANT:
					unsafe
					{
						if (v.ptrVal == 0) return new ComValue((long)vt, 0L);
						var inner = Marshal.PtrToStructure<VARIANT>(v.ptrVal);
						// Return primitive if possible, else ComValue(inner_vt, payload)
						var obj = VariantToValue(inner);
						return (obj is long || obj is double || obj is bool || obj is string)
							? obj
							: new ComValue((long)inner.vt, obj is ComValue cv ? cv.Ptr : obj);
					}

				// ---- Numerics & logicals ----
				case VarEnum.VT_BOOL: unsafe { if (v.ptrVal == 0) return false; return *((short*)v.ptrVal) != 0; }
				case VarEnum.VT_I1: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((sbyte*)v.ptrVal); }
				case VarEnum.VT_UI1: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((byte*)v.ptrVal); }
				case VarEnum.VT_I2: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((short*)v.ptrVal); }
				case VarEnum.VT_UI2: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((ushort*)v.ptrVal); }
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((int*)v.ptrVal); }
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((uint*)v.ptrVal); }
				case VarEnum.VT_I8: unsafe { if (v.ptrVal == 0) return 0L; return *((long*)v.ptrVal); }
				case VarEnum.VT_UI8: unsafe { if (v.ptrVal == 0) return 0L; return (long)*((ulong*)v.ptrVal); }
				case VarEnum.VT_R4: unsafe { if (v.ptrVal == 0) return 0.0; return (double)*((float*)v.ptrVal); }
				case VarEnum.VT_R8: unsafe { if (v.ptrVal == 0) return 0.0; return *((double*)v.ptrVal); }

				case VarEnum.VT_CY: unsafe { if (v.ptrVal == 0) return 0L; return *((long*)v.ptrVal); } // keep scaled int (×10,000)
				case VarEnum.VT_DATE: unsafe { if (v.ptrVal == 0) return 0.0; return *((double*)v.ptrVal); } // keep OADate (double)

				// ---- Exotic: SAFEARRAY/DECIMAL/RECORD/... → wrap as ComValue(vt, Ptr) ----
				default:
					unsafe
					{
						// Use the generic byref slot to surface the address (or 0)
						nint payload = v.ptrVal;
						return new ComValue((long)vt, (long)payload);
					}
			}
		}


		internal static void WriteByRefVariant(in VARIANT byrefVar, object value)
		{
			var vt = (VarEnum)byrefVar.vt;
			if ((vt & VarEnum.VT_BYREF) == 0) return;

			var baseVt = vt & ~VarEnum.VT_BYREF;

			switch (baseVt)
			{
				// ---- BYREF|VARIANT: clear old, write a new VARIANT ----
				case VarEnum.VT_VARIANT:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						VariantClear(byrefVar.ptrVal);
						var nv = ValueToVariant(value);
						Marshal.StructureToPtr(nv, byrefVar.ptrVal, false);
					}
					return;

				// ---- BYREF|BSTR: free old BSTR, store new BSTR ----
				case VarEnum.VT_BSTR:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						var pBstr = (nint*)byrefVar.ptrVal;
						if (*pBstr != 0) WindowsAPI.SysFreeString(*pBstr);

						string s = value switch
						{
							string ss => ss,
							ComValue cv when cv.vt == VarEnum.VT_BSTR && cv.Ptr is long lp && lp != 0 =>
								Marshal.PtrToStringBSTR((nint)lp) ?? "",
							_ => Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty
						};

						*pBstr = Marshal.StringToBSTR(s);
					}
					return;

				// ---- BYREF|IDispatch*: Release old, assign new (with a +1 ref) ----
				case VarEnum.VT_DISPATCH:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						var p = (nint*)byrefVar.ptrVal;

						if (*p != 0) Marshal.Release(*p);

						nint newPtr = 0;
						if (value is null)
						{
							newPtr = 0;
						}
						else if (value is ComValue cv && cv.vt == VarEnum.VT_DISPATCH && cv.Ptr is long lp)
						{
							newPtr = (nint)lp;
							if (newPtr != 0) Marshal.AddRef(newPtr); // ensure independent lifetime
						}
						else
						{
							newPtr = Marshal.GetIDispatchForObject(value);
						}

						*p = newPtr;
					}
					return;

				// ---- BYREF|IUnknown*: Release old, assign new (with a +1 ref) ----
				case VarEnum.VT_UNKNOWN:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						var p = (nint*)byrefVar.ptrVal;

						if (*p != 0) Marshal.Release(*p);

						nint newPtr = 0;
						if (value is null)
						{
							newPtr = 0;
						}
						else if (value is ComValue cv && cv.vt == VarEnum.VT_UNKNOWN && cv.Ptr is long lp)
						{
							newPtr = (nint)lp;
							if (newPtr != 0) Marshal.AddRef(newPtr);
						}
						else
						{
							newPtr = Marshal.GetIUnknownForObject(value);
						}

						*p = newPtr;
					}
					return;

				// ---- Numerics & logicals ----
				case VarEnum.VT_BOOL: unsafe { if (byrefVar.ptrVal == 0) return; *((short*)byrefVar.ptrVal) = (short)(Script.ForceBool(value) ? -1 : 0); } return;
				case VarEnum.VT_I1: unsafe { if (byrefVar.ptrVal == 0) return; *((sbyte*)byrefVar.ptrVal) = (sbyte)value.Al(); } return;
				case VarEnum.VT_UI1: unsafe { if (byrefVar.ptrVal == 0) return; *((byte*)byrefVar.ptrVal) = (byte)value.Aui(); } return;
				case VarEnum.VT_I2: unsafe { if (byrefVar.ptrVal == 0) return; *((short*)byrefVar.ptrVal) = (short)value.Al(); } return;
				case VarEnum.VT_UI2: unsafe { if (byrefVar.ptrVal == 0) return; *((ushort*)byrefVar.ptrVal) = (ushort)value.Aui(); } return;
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: unsafe { if (byrefVar.ptrVal == 0) return; *((int*)byrefVar.ptrVal) = (int)value.Al(); } return;
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: unsafe { if (byrefVar.ptrVal == 0) return; *((uint*)byrefVar.ptrVal) = (uint)value.Aui(); } return;
				case VarEnum.VT_I8: unsafe { if (byrefVar.ptrVal == 0) return; *((long*)byrefVar.ptrVal) = value.Al(); } return;
				case VarEnum.VT_UI8: unsafe { if (byrefVar.ptrVal == 0) return; *((ulong*)byrefVar.ptrVal) = (ulong)value.Al(); } return;
				case VarEnum.VT_R4: unsafe { if (byrefVar.ptrVal == 0) return; *((float*)byrefVar.ptrVal) = (float)value.Ad(); } return;
				case VarEnum.VT_R8: unsafe { if (byrefVar.ptrVal == 0) return; *((double*)byrefVar.ptrVal) = value.Ad(); } return;

				case VarEnum.VT_CY:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						long cy = value switch
						{
							long l => l,
							decimal m => checked((long)Math.Round(m * 10000m)),
							double d => checked((long)Math.Round(d * 10000.0)),
							_ => checked((long)Math.Round(Convert.ToDouble(value) * 10000.0)),
						};
						*((long*)byrefVar.ptrVal) = cy;
					}
					return;

				case VarEnum.VT_DATE:
					unsafe
					{
						if (byrefVar.ptrVal == 0) return;
						double dd = value switch
						{
							double d => d,
							DateTime dt => dt.ToOADate(),
							_ => Convert.ToDateTime(value).ToOADate(),
						};
						*((double*)byrefVar.ptrVal) = dd;
					}
					return;

				case var t when (t & VarEnum.VT_ARRAY) != 0:
					{
						unsafe
						{
							if (byrefVar.ptrVal == 0) return;
							var ppsa = (nint*)byrefVar.ptrVal;

							// Extract SAFEARRAY* from the incoming value
							nint srcPsa = (nint)Reflections.GetPtrProperty(value);
							if (srcPsa == 0)
								return;

							// Make an independent copy of the source SAFEARRAY to avoid aliasing/lifetime issues
							nint copiedPsa = 0;
							int hr = OleAuto.SafeArrayCopy(srcPsa, out copiedPsa);
							if (hr < 0 || copiedPsa == 0)
								return;

							// Destroy the old one (if any) to avoid leaks, then store the copy
							if (*ppsa != 0 && *ppsa != copiedPsa)
								_ = OleAuto.SafeArrayDestroy(*ppsa);

							*ppsa = copiedPsa;
						}
						return;
					}

				// ---- DECIMAL / RECORD / etc. ----
				default:
					// Intentionally do nothing by default to avoid corrupting memory.
					// If you need to support writes for these, add explicit cases:
					//  - DECIMAL: write through pdecVal
					//  - RECORD: requires IRecordInfo helpers
					return;
			}
		}



		// ---------- Free BYREF storage allocated by CreateByRefVariantTyped / CreateByRefVariant ----------
		internal static void CleanupByRefVariant(VARIANT byRefVariant)
		{
			if (((VarEnum)byRefVariant.vt & VarEnum.VT_BYREF) == 0)
				return;

			VarEnum baseVt = (VarEnum)byRefVariant.vt & ~VarEnum.VT_BYREF;

			// Nested-variant path (your old helper)
			if (baseVt == VarEnum.VT_VARIANT && byRefVariant.ptrVal != 0)
			{
				VariantClear(byRefVariant.ptrVal);
				Marshal.FreeHGlobal(byRefVariant.ptrVal);
				return;
			}

			// SAFEARRAY**: free just the pointer cell (do NOT destroy SAFEARRAY here)
			if ((baseVt & VarEnum.VT_ARRAY) != 0)
			{
				if (byRefVariant.ptrVal != 0)
					Marshal.FreeHGlobal(byRefVariant.ptrVal);
				return;
			}

			// Typed storage cells (we always allocated the cell)
			switch (baseVt)
			{
				case VarEnum.VT_BSTR:
					{
						if (byRefVariant.ptrVal != 0)
						{
							nint bstr = Marshal.ReadIntPtr(byRefVariant.ptrVal);
							if (bstr != 0) WindowsAPI.SysFreeString(bstr);
							Marshal.FreeHGlobal(byRefVariant.ptrVal);
						}
						break;
					}

				default:
					// Unknown typed case → free as generic cell if we can identify it
					if (byRefVariant.ptrVal != 0) Marshal.FreeHGlobal(byRefVariant.ptrVal);
					break;
			}
		}

		internal static object VariantToValue(VARIANT variant)
		{
			var vt = (VarEnum)variant.vt;

			// ── SAFEARRAYs ─────────────────────────────────────────────────────────────
			// By-value SAFEARRAY: VT_ARRAY | T, union member is VARIANT.parray (SAFEARRAY*)
			if ((vt & VarEnum.VT_ARRAY) != 0 && (vt & VarEnum.VT_BYREF) == 0)
			{
				var elemVt = vt & ~VarEnum.VT_ARRAY;
				nint psa = variant.ptrVal;               // SAFEARRAY*
				if (psa == 0)
					return null;

				int hr = OleAuto.SafeArrayCopy(psa, out nint psaCopy);
				if (hr < 0) return Errors.OSErrorOccurredForHR(hr);

				// We do take ownership because we made a copy.
				return new ComObjArray(elemVt, psaCopy, takeOwnership: true);
			}

			// By-ref SAFEARRAY: VT_BYREF | VT_ARRAY | T, union member is VARIANT.ptrVal (SAFEARRAY**)
			if ((vt & VarEnum.VT_ARRAY) != 0 && (vt & VarEnum.VT_BYREF) != 0)
			{
				var elemVt = vt & ~(VarEnum.VT_ARRAY | VarEnum.VT_BYREF);
				nint ppsa = variant.ptrVal;             // SAFEARRAY**
				if (ppsa == 0)
					return null;

				nint psa = Marshal.ReadIntPtr(ppsa);     // deref to SAFEARRAY*
				if (psa == 0)
					return null;

				return new ComObjArray(elemVt, psa, takeOwnership: false);
			}

			// ── BYREF (non-array) ─────────────────────────────────────────────────────
			if ((vt & VarEnum.VT_BYREF) != 0)
			{
				if (variant.ptrVal != 0)
				{
					// Only correct for VT_BYREF|VT_VARIANT
					var innerVariant = Marshal.PtrToStructure<VARIANT>(variant.ptrVal);
					return VariantToValue(innerVariant);
				}

				// Optional: handle other BYREF scalars precisely (plVal, pboolVal, etc.)
				// If you already have typed BYREF readback elsewhere, you can keep this as-is.
				return null;
			}

			// ── Scalars / pointers ────────────────────────────────────────────────────
			switch (vt)
			{
				case VarEnum.VT_EMPTY:
				case VarEnum.VT_NULL:
					return null;

				case VarEnum.VT_BSTR:
					return variant.ptrVal == 0 ? "" : Marshal.PtrToStringBSTR(variant.ptrVal);

				case VarEnum.VT_I1: return (long)variant.cVal;
				case VarEnum.VT_UI1: return (long)variant.bVal;
				case VarEnum.VT_I2: return (long)variant.iVal;
				case VarEnum.VT_UI2: return (long)variant.uiVal;
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: return (long)variant.lVal;
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: return (long)variant.ulVal;
				case VarEnum.VT_I8: return variant.llVal;
				case VarEnum.VT_UI8: return (long)variant.ullVal;

				case VarEnum.VT_R4: return (double)variant.fltVal;
				case VarEnum.VT_R8: return variant.dblVal;

				case VarEnum.VT_BOOL: return variant.boolVal != 0;

				case VarEnum.VT_DATE: return DateTime.FromOADate(variant.dblVal);

				case VarEnum.VT_UNKNOWN:
					if (variant.ptrVal != 0)
					{
						Guid IID__Object = new Guid("65074F7F-63C0-304E-AF0A-D51741CB4A8D");
						Guid IID_IEnumVARIANT = new Guid("00020404-0000-0000-c000-000000000046");
						if (Marshal.QueryInterface(variant.ptrVal, in IID_IEnumVARIANT, out var pEnumVar) == 0)
						{
							return new ComObject { vt = VarEnum.VT_DISPATCH, Ptr = pEnumVar };
						}
						if (Marshal.QueryInterface(variant.ptrVal, in IID__Object, out var pObj) == 0)
						{
							return new ComObject { vt = VarEnum.VT_DISPATCH, Ptr = pObj };
						}
					}
					goto case VarEnum.VT_DISPATCH;
				case VarEnum.VT_DISPATCH:
					if (variant.ptrVal != 0)
					{
						Marshal.AddRef(variant.ptrVal);
						return vt == VarEnum.VT_DISPATCH ? new ComObject { vt = vt, Ptr = variant.ptrVal } : new ComValue { vt = vt, Ptr = variant.ptrVal };
					}
					return DefaultObject;

				default:
					// Fallback: leave pointer-ish value wrapped
					return new ComValue { vt = vt, Ptr = variant.llVal };
			}
		}

		internal static object ReadVariant(long ptrValue, VarEnum vtRaw)
		{
			nint dataPtr = (nint)ptrValue;
			VarEnum vt = vtRaw & ~VarEnum.VT_BYREF;

			if ((vt & VarEnum.VT_ARRAY) != 0)
			{
				VarEnum elemVt = vt & ~VarEnum.VT_ARRAY;
				nint parray = Marshal.ReadIntPtr(dataPtr);
				if (parray == 0)
					return DefaultErrorObject;

				// Wrap without owning: the VARIANT will destroy it on VariantClear.
				return new ComObjArray(elemVt, parray, takeOwnership: false);
			}

			switch (vt)
			{
				// ── Integers → long ───────────────────────────────────────────────
				case VarEnum.VT_I1:
					return (long)(sbyte)Marshal.ReadByte(dataPtr);

				case VarEnum.VT_UI1:
					return (long)Marshal.ReadByte(dataPtr);

				case VarEnum.VT_I2:
					return (long)Marshal.ReadInt16(dataPtr);

				case VarEnum.VT_UI2:
					return (long)(ushort)Marshal.ReadInt16(dataPtr);

				case VarEnum.VT_I4:
				case VarEnum.VT_INT:
					return (long)Marshal.ReadInt32(dataPtr);

				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT:
					return (long)(uint)Marshal.ReadInt32(dataPtr);

				case VarEnum.VT_I8:
					return Marshal.ReadInt64(dataPtr);

				case VarEnum.VT_UI8:
					return (long)(ulong)Marshal.ReadInt64(dataPtr);

				// ── Boolean → bool ───────────────────────────────────────────────
				case VarEnum.VT_BOOL:
					// COM VARIANT_BOOL is a 16-bit short: 0 or −1
					return Marshal.ReadInt16(dataPtr) != 0;

				// ── Floating point / date → double ───────────────────────────────
				case VarEnum.VT_R4:
					// Read 4-byte float, then promote
					float f = Marshal.PtrToStructure<float>(dataPtr);
					return (double)f;

				case VarEnum.VT_R8:
				case VarEnum.VT_DATE:
					// VT_DATE is also stored as an 8-byte IEEE double
					return Marshal.PtrToStructure<double>(dataPtr);

				// ── BSTR → string ────────────────────────────────────────────────
				case VarEnum.VT_BSTR:
					{
						nint bstr = Marshal.ReadIntPtr(dataPtr);
						return bstr == 0
							   ? string.Empty
							   : Marshal.PtrToStringBSTR(bstr);
					}

				case VarEnum.VT_VARIANT:
					{
						VarEnum innerVt = (VarEnum)Marshal.ReadInt16(dataPtr);
						return ReadVariant(ptrValue + 8, innerVt);
					}

				case VarEnum.VT_UNKNOWN:
				case VarEnum.VT_DISPATCH:
					{
						nint punk = Marshal.ReadIntPtr(dataPtr);
						return Objects.ObjFromPtr(punk);
					}

				default:
					{
						nint unk = Marshal.ReadIntPtr(dataPtr);

						if (unk == 0)
							return DefaultErrorObject;

						return new ComValue
						{
							vt = vtRaw & ~VarEnum.VT_BYREF,
							Ptr = unk,
						};
					}
			}
		}

		/// <summary>
		/// Write a primitive value back into a COM VARIANT payload.
		/// Supports VT_I1/UI1/I2/UI2/I4/UI4/I8/UI8, VT_BOOL, VT_R4, VT_R8/VT_DATE, VT_VARIANT.
		/// </summary>
		internal static void WriteVariant(long ptrValue, VarEnum vtRaw, object value)
		{
			nint dataPtr = new nint(ptrValue);
			VarEnum vt = vtRaw & ~VarEnum.VT_BYREF;

			if (value is IPointable ip)
				value = ip.Ptr;

			switch (vt)
			{
				// ── Integers ────────────────────────────────────────────────────
				case VarEnum.VT_I1:
					// sbyte → marshal as one signed byte
					sbyte sb = Convert.ToSByte(value);
					Marshal.WriteByte(dataPtr, unchecked((byte)sb));
					break;

				case VarEnum.VT_UI1:
					// byte
					byte ub = Convert.ToByte(value);
					Marshal.WriteByte(dataPtr, ub);
					break;

				case VarEnum.VT_I2:
					short i2 = Convert.ToInt16(value);
					Marshal.WriteInt16(dataPtr, i2);
					break;

				case VarEnum.VT_UI2:
					ushort ui2 = Convert.ToUInt16(value);
					// Marshal.WriteInt16 writes a signed short, so reinterpret
					Marshal.WriteInt16(dataPtr, unchecked((short)ui2));
					break;

				case VarEnum.VT_I4:
				case VarEnum.VT_INT:
					int i4 = Convert.ToInt32(value);
					Marshal.WriteInt32(dataPtr, i4);
					break;

				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT:
					uint ui4 = Convert.ToUInt32(value);
					Marshal.WriteInt32(dataPtr, unchecked((int)ui4));
					break;

				case VarEnum.VT_I8:
					long i8 = Convert.ToInt64(value);
					Marshal.WriteInt64(dataPtr, i8);
					break;

				case VarEnum.VT_UI8:
					ulong ui8 = Convert.ToUInt64(value);
					// Marshal.WriteInt64 writes signed; reinterpret
					Marshal.WriteInt64(dataPtr, unchecked((long)ui8));
					break;

				// ── Boolean ────────────────────────────────────────────────────
				case VarEnum.VT_BOOL:
					// VARIANT_BOOL is a 16-bit short: -1 (TRUE) or 0 (FALSE)
					bool b = Convert.ToBoolean(value);
					short vb = (short)(b ? -1 : 0);
					Marshal.WriteInt16(dataPtr, vb);
					break;

				// ── Floating-point / Date ─────────────────────────────────────
				case VarEnum.VT_R4:
					// store 4-byte float
					float f = Convert.ToSingle(value);
					byte[] fb = BitConverter.GetBytes(f);
					Marshal.Copy(fb, 0, dataPtr, fb.Length);
					break;

				case VarEnum.VT_R8:
				case VarEnum.VT_DATE:
					// store 8-byte double
					double d = Convert.ToDouble(value);
					byte[] db = BitConverter.GetBytes(d);
					Marshal.Copy(db, 0, dataPtr, db.Length);
					break;

				// ── BSTR → string ─────────────────────────────────────────────────
				case VarEnum.VT_BSTR:
					{
						// free old BSTR
						nint oldBstr = Marshal.ReadIntPtr(dataPtr);

						if (oldBstr != 0)
							WindowsAPI.SysFreeString(oldBstr);

						// allocate new BSTR (null → zero pointer)
						string s = value as string;
						IntPtr newBstr = string.IsNullOrEmpty(s)
										 ? IntPtr.Zero
										 : Marshal.StringToBSTR(s);
						Marshal.WriteIntPtr(dataPtr, newBstr);
					}
					break;

				// ── COM interfaces → IUnknown pointer ─────────────────────────────
				case VarEnum.VT_DISPATCH:
				case VarEnum.VT_UNKNOWN:
					{
						// release old pointer
						nint oldPtr = Marshal.ReadIntPtr(dataPtr);

						if (oldPtr != 0)
							_ = Marshal.Release(oldPtr);

						// get new pointer (allow passing either IntPtr or RCW)

						nint newPtr = value switch
						{
							long ptr => (nint)ptr,
							null => 0,
							_ => vt == VarEnum.VT_DISPATCH ? Marshal.GetIDispatchForObject(value) : Marshal.GetIUnknownForObject(value)
						};

						Marshal.WriteIntPtr(dataPtr, newPtr);
					}
					break;

				case VarEnum.VT_VARIANT:
					{
						// 1) Choose the right VarEnum for "value"
						VarEnum innerVt;

						if (value is string)
						{
							innerVt = VarEnum.VT_BSTR;
						}
						else if (value is KeysharpObject)
						{
							innerVt = VarEnum.VT_DISPATCH;
						}
						else if (value is ComObjArray coa)
						{
							innerVt = VarEnum.VT_ARRAY | coa._baseType;
						}
						else if (value is double)
						{
							innerVt = VarEnum.VT_R8;
						}
						else if (value is long l)
						{
							innerVt = (l >= int.MinValue && l <= int.MaxValue)
									  ? VarEnum.VT_I4
									  : VarEnum.VT_I8;
						}
						else
						{
							throw new NotSupportedException(
								$"Cannot wrap a {value?.GetType().Name} as VT_VARIANT");
						}

						// 2) Clear previous contents, release BSTRs etc
						_ = VariantHelper.VariantClear(dataPtr);
						// 3) Write the VT and clear the four reserved words
						//    [vt:2][res1:2][res2:2][res3:2]  <-- totals 8 bytes header
						Marshal.WriteInt16(dataPtr, (short)innerVt);
						// 4) Write the payload into the union at offset 8
						//    we simply recurse into our existing writer,
						//    passing the address + 8 and the bare innerVt
						WriteVariant(ptrValue + 8, innerVt, value);
					}
					break;

				// ── SAFEARRAYs (VT_ARRAY | T) ─────────────────────────────────────────────
				case var _ when (vt & VarEnum.VT_ARRAY) != 0:
					{
						// vt holds VT_ARRAY|elemVT, payload is a SAFEARRAY*
						VarEnum elemVt = vt & ~VarEnum.VT_ARRAY;

						nint psaToStore = 0;

						if (value is ComObjArray coa)
						{
							// Defensive: if element type differs, still allow but clone regardless.
							// Clone so that the VARIANT owns its *own* SAFEARRAY (avoids double-destroy).
							int hr = OleAuto.SafeArrayCopy(coa._psa, out nint psaCopy);
							if (hr < 0)
								throw Errors.OSError("SafeArrayCopy failed.", hr);

							psaToStore = psaCopy;
						}
						else if (value is object[] managedArr)
						{
							// Build a fresh SAFEARRAY (default to VARIANT elements; robust for mixed content)
							VARIANT vArr = CreateVariantFromManagedArray(managedArr, VarEnum.VT_VARIANT);
							psaToStore = vArr.ptrVal;
							// We just need the SAFEARRAY* from vArr; prevent double-free by not clearing vArr.
							// (The array will now be owned by the receiver VARIANT we’re writing to.)
						}
						else if (value is long lp)
						{
							psaToStore = (nint)lp; // caller gave us a raw SAFEARRAY*
						}
						else if (value is nint ip2)
						{
							psaToStore = ip2;
						}
						else
						{
							throw Errors.Error($"Cannot write VT_ARRAY payload from {value?.GetType().Name}.");
						}

						Marshal.WriteIntPtr(dataPtr, psaToStore);
						break;
					}
				// ── Unsupported ────────────────────────────────────────────────
				default:
					throw Errors.Error($"Writing VARTYPE {vt} is not supported.");
			}
		}

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
			if (t == typeof(DateTime)) return VarEnum.VT_DATE;
			if (t == typeof(object)) return VarEnum.VT_VARIANT;
			// Fallback for COM-ref types:
			if (typeof(Any).IsAssignableFrom(t)) return VarEnum.VT_DISPATCH;

			// Unknown → let the old path (nested VT_VARIANT) handle it.
			return VarEnum.VT_EMPTY;
		}
	}

	internal static class ComDebug
	{
		static string Flags(VarEnum vt)
		{
			var parts = new List<string>();
			if ((vt & VarEnum.VT_BYREF) != 0) parts.Add("BYREF");
			if ((vt & VarEnum.VT_ARRAY) != 0) parts.Add("ARRAY");
			parts.Add((vt & ~(VarEnum.VT_BYREF | VarEnum.VT_ARRAY)).ToString());
			return string.Join("|", parts);
		}

		public static void DumpVariant(string label, Keysharp.Core.COM.VARIANT v)
		{
			var vt = (VarEnum)v.vt;
			System.Diagnostics.Debug.WriteLine($"{label}: vt=0x{v.vt:X4} ({Flags(vt)})");

			// --- BYREF cases ---
			if ((vt & VarEnum.VT_BYREF) != 0)
			{
				var baseVt = vt & ~VarEnum.VT_BYREF;

				// BYREF SAFEARRAY: ptrVal is SAFEARRAY**
				if ((baseVt & VarEnum.VT_ARRAY) != 0)
				{
					nint ppsa = v.ptrVal;
					nint psa = ppsa != 0 ? Marshal.ReadIntPtr(ppsa) : 0;
					System.Diagnostics.Debug.WriteLine($"  BYREF SAFEARRAY** ppsa=0x{(long)ppsa:X}, psa=0x{(long)psa:X}");
					DumpSafeArray("  ", psa, baseVt & ~VarEnum.VT_ARRAY);
					return;
				}

				// BYREF|VARIANT: ptrVal points at an inner VARIANT
				if ((baseVt == VarEnum.VT_VARIANT) && v.ptrVal != 0)
				{
					var inner = Marshal.PtrToStructure<Keysharp.Core.COM.VARIANT>(v.ptrVal);
					DumpVariant("  *inner", inner);
					return;
				}

				// BYREF scalar/BSTR/IDispatch/IUnknown
				System.Diagnostics.Debug.WriteLine($"  BYREF payload cell: 0x{(long)v.ptrVal:X} for base {baseVt}");
				return;
			}

			// --- By-value SAFEARRAY ---
			if ((vt & VarEnum.VT_ARRAY) != 0)
			{
				nint psa = v.ptrVal;
				System.Diagnostics.Debug.WriteLine($"  SAFEARRAY* parray=0x{(long)psa:X}");
				DumpSafeArray("  ", psa, vt & ~VarEnum.VT_ARRAY);
				return;
			}

			// --- Scalars / pointers ---
			switch (vt)
			{
				case VarEnum.VT_BSTR:
					{
						var s = v.ptrVal == 0 ? "" : Marshal.PtrToStringBSTR(v.ptrVal);
						var prev = s is null ? "null" : (s.Length > 80 ? s.Substring(0, 80) + "…" : s);
						System.Diagnostics.Debug.WriteLine($"  BSTR=\"{prev}\"");
						break;
					}
				case VarEnum.VT_DISPATCH:
				case VarEnum.VT_UNKNOWN:
					System.Diagnostics.Debug.WriteLine($"  COM ptr=0x{(long)v.ptrVal:X}");
					break;
				default:
					System.Diagnostics.Debug.WriteLine($"  scalar payload at union: 0x{(long)v.llVal:X}");
					break;
			}
		}

		static void DumpSafeArray(string indent, nint psa, VarEnum elemFromHeader)
		{
			if (psa == 0)
			{
				System.Diagnostics.Debug.WriteLine($"{indent}(psa is NULL)");
				return;
			}

			// Element VARTYPE from array header
			int hr = Keysharp.Core.OleAuto.SafeArrayGetVartype(psa, out ushort vtElemHdrRaw);
			var vtElemHdr = (VarEnum)vtElemHdrRaw;
			int dims = Keysharp.Core.OleAuto.SafeArrayGetDim(psa);

			System.Diagnostics.Debug.WriteLine($"{indent}elemVT(header)=0x{vtElemHdrRaw:X4} ({vtElemHdr}), dims={dims}");

			// Bounds
			for (uint d = 1; d <= dims; d++)
			{
				_ = Keysharp.Core.OleAuto.SafeArrayGetLBound(psa, d, out int lb);
				_ = Keysharp.Core.OleAuto.SafeArrayGetUBound(psa, d, out int ub);
				System.Diagnostics.Debug.WriteLine($"{indent}dim{d}: [{lb}..{ub}] (len={ub - lb + 1})");
			}

			// Peek first element’s VT (especially useful for VT_VARIANT arrays)
			if (dims == 1)
			{
				_ = Keysharp.Core.OleAuto.SafeArrayGetLBound(psa, 1, out int lb);
				_ = Keysharp.Core.OleAuto.SafeArrayGetUBound(psa, 1, out int ub);
				int count = ub - lb + 1;
				if (count > 0)
				{
					var vtElem = vtElemHdr;
					if (vtElem == VarEnum.VT_VARIANT)
					{
						// Access raw data and read first VARIANT
						hr = Keysharp.Core.OleAuto.SafeArrayAccessData(psa, out nint pData);
						if (hr >= 0)
						{
							try
							{
								var elem0 = Marshal.PtrToStructure<Keysharp.Core.COM.VARIANT>(pData);
								System.Diagnostics.Debug.WriteLine($"{indent}elem[0] vt=0x{elem0.vt:X4} ({(VarEnum)elem0.vt})");
								// Optional: preview BSTR
								if ((VarEnum)elem0.vt == VarEnum.VT_BSTR && elem0.ptrVal != 0)
								{
									var s = Marshal.PtrToStringBSTR(elem0.ptrVal) ?? "";
									var prev = s.Length > 80 ? s.Substring(0, 80) + "…" : s;
									System.Diagnostics.Debug.WriteLine($"{indent}elem[0] BSTR=\"{prev}\"");
								}
							}
							finally
							{
								_ = Keysharp.Core.OleAuto.SafeArrayUnaccessData(psa);
							}
						}
						else
						{
							System.Diagnostics.Debug.WriteLine($"{indent}SafeArrayAccessData failed: hr=0x{hr:X8}");
						}
					}
					else
					{
						// For typed arrays we know element VT; still try reading value via GetElement for convenience
						try
						{
							int[] idx = { lb };
							_ = Keysharp.Core.OleAuto.SafeArrayGetElement(psa, idx, out object pv);
							System.Diagnostics.Debug.WriteLine($"{indent}elem[0] (typed {vtElem}) = {Preview(pv)}");
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"{indent}elem[0] read failed: {ex.Message}");
						}
					}
				}
			}
		}

		static string Preview(object pv)
		{
			if (pv is string s) return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
			if (pv is null) return "null";
			return pv.ToString();
		}
	}

}
#endif