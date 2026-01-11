#if WINDOWS
namespace Keysharp.Core
{
	/// <summary>
	/// Describes the bounds (element count and lower bound) of a single dimension of a SAFEARRAY.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	struct SAFEARRAYBOUND
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
	/// Contains P/Invoke declarations for OLE Automation SafeArray APIs.
	/// </summary>
	static partial class OleAuto
	{
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
		/// <summary>
		/// Retrieves an element from a SafeArray by index.
		/// </summary>
		[DllImport(WindowsAPI.oleaut)]
		public static extern int SafeArrayGetElement(
			nint psa,
			[In] int[] rgIndices,
			out object pv);
		/// <summary>
		/// Stores an element into a SafeArray by index.
		/// </summary>
		[DllImport(WindowsAPI.oleaut, PreserveSig = true)]
		public static extern int SafeArrayPutElement(
			nint psa,
			[In] int[] rgIndices,
			[MarshalAs(UnmanagedType.Struct)] object pv
		);

		// Raw-pointer version: for all non-VARIANT base types.
		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayGetElement")]
		public static partial int SafeArrayGetElementPtr(nint psa, [In] int[] rgIndices, IntPtr pv);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayPutElement")]
		public static partial int SafeArrayPutElementPtr(nint psa, [In] int[] rgIndices, IntPtr pv);

		[LibraryImport(WindowsAPI.oleaut)] 
		public static partial int SysStringLen(nint bstr);

		[LibraryImport(WindowsAPI.oleaut)]
		public static partial nint SysAllocStringLen(nint src, int len);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayAccessData")]
		internal static partial int SafeArrayAccessData(nint psa, out nint ppvData);

		[LibraryImport(WindowsAPI.oleaut, EntryPoint = "SafeArrayUnaccessData")]
		internal static partial int SafeArrayUnaccessData(nint psa);
	}

	/// <summary>
	/// Enumerator for iterating (index, value) pairs in a COM SafeArray.
	/// </summary>
	public class ComArrayIndexValueEnumerator : KeysharpEnumerator, IEnumerator<object>
	{
		private readonly ComObjArray _owner;
		private readonly int _count;
		private readonly int[] _indices;
		private readonly int[] _flows; // upper bounds per dimension
		private readonly int[] _lows;  // lower bounds per dimension
		private bool _done;
		private int _idx = -1;

		/// <summary>
		/// Initializes a new enumerator over the specified ComObjArray.
		/// </summary>
		/// <param name="owner">The array wrapper to enumerate.</param>
		/// <param name="c">Number of returned items per iteration (1 for value, 2 for index+value).</param>
		public ComArrayIndexValueEnumerator(ComObjArray owner, int c)
			: base(null, c)
		{
			_owner = owner;
			_count = c;
			int d = owner._dimensions;
			_indices = new int[d];
			_flows = new int[d];
			_lows = new int[d];

			for (int i = 0; i < d; i++)
			{
				_flows[i] = (int)(long)owner.MaxIndex(i + 1);
				_lows[i] = (int)(long)owner.MinIndex(i + 1);
			}

			// Initialize counter to just before the first element:
			// set all but the last dimension to their low bound;
			// set the last dimension to (low - 1) so first MoveNext() brings it to 'low'.
			for (int i = 0; i < d; i++)
				_indices[i] = _lows[i];

			_indices[d - 1] = _lows[d - 1] - 1;

			var script = Script.TheScript;
			var p = c <= 1 ? script.ComArrayIndexValueEnumeratorData.p1 : script.ComArrayIndexValueEnumeratorData.p2;
			fo = (FuncObj)p.Clone();
			fo.Inst = this;
		}

		public override object Call([ByRef] object pos)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(pos, "__Value", Current);
				return true;
			}

			return false;
		}

		public override object Call([ByRef] object pos, [ByRef] object val)
		{
			if (MoveNext())
			{
				Script.SetPropertyValue(pos, "__Value", (long)_idx);
				Script.SetPropertyValue(val, "__Value", Current);
				return true;
			}

			return false;
		}

		public object Current
		{
			get
			{
				return _owner.GetElementAtIndices(_indices);
			}
		}

		object IEnumerator.Current => Current;

		public bool MoveNext()
		{
			if (_done) return false;

			int d = _owner._dimensions;

			// Increment the n-dimensional counter (row-major):
			for (int dim = d - 1; dim >= 0; dim--)
			{
				int next = _indices[dim] + 1;

				if (next <= _flows[dim])
				{
					_indices[dim] = next;
					// Reset trailing dimensions to their low bound.
					for (int j = dim + 1; j < d; j++)
						_indices[j] = _lows[j];

					_idx = _indices[0] - _lows[0];

					return true;
				}

				if (dim == 0)
				{
					_done = true;
					return false;
				}
			}

			_done = true;
			return false;
		}

		public void Reset()
		{
			int d = _owner._dimensions;

			for (int i = 0; i < d; i++)
				_indices[i] = _lows[i];

			_indices[d - 1] = _lows[d - 1] - 1;
			_done = false;
		}

		public void Dispose() => Reset();
	}

	internal class ComArrayIndexValueEnumeratorData : BaseIteratorData<ComArrayIndexValueEnumerator>
	{
	}

	/// <summary>
	/// A COM wrapper around a native SAFEARRAY, exposing AHK-friendly APIs.
	/// </summary>
	public class ComObjArray : ComValue, I__Enum, IEnumerable<object>
	{
		internal nint _psa;         // pointer to the native SAFEARRAY
		internal int _dimensions;   // number of dimensions
		internal VarEnum _baseType; // element VARTYPE, e.g. VT_VARIANT

		public long Dimensions => _dimensions;
		public long Vt => (long)_baseType;

		/// <summary>
		/// Creates a new SafeArray of the specified element type and dimension sizes.
		/// </summary>
		/// <param name="baseType">The element VARTYPE (e.g. VT_VARIANT).</param>
		/// <param name="counts">Sizes for each dimension (1 to 8 values).</param>
		public ComObjArray(VarEnum baseType, params int[] counts)
		{
			if (counts == null || counts.Length == 0 || counts.Length > 8)
				throw new ArgumentException("Must supply 1–8 dimension sizes", nameof(counts));

			_baseType = baseType;
			_dimensions = counts.Length;
			// Build SAFEARRAYBOUND array, all zero‐based:
			var sab = new SAFEARRAYBOUND[_dimensions];

			for (int i = 0; i < _dimensions; i++)
			{
				sab[i].cElements = (uint)counts[i];
				sab[i].lLbound = 0;
			}

			// Create the native SafeArray:
			_psa = OleAuto.SafeArrayCreate(
					   (short)baseType,
					   (uint)_dimensions,
					   sab
				   );

			if (_psa == 0)
			{
				_ = Errors.OSErrorOccurred(Marshal.GetLastWin32Error());
				return;
			}

			// Tell ComObject to own and destroy the SafeArray:
			this.vt = VarEnum.VT_ARRAY | baseType;
			this.Flags = F_OWNVALUE;
			this.Ptr = _psa.ToInt64();
		}

		public ComObjArray(VarEnum baseType, nint psa, bool takeOwnership)
		{
			_baseType = baseType;
			_dimensions = OleAuto.SafeArrayGetDim(psa);
			_psa = psa;
			this.vt = VarEnum.VT_ARRAY | baseType;
			this.Flags = takeOwnership ? F_OWNVALUE : 0; ;
			this.Ptr = _psa.ToInt64();
		}

		public static object Call(object @this, object varType, object count1, params object[] args)
		{
			var vt = (VarEnum)varType.Ai();
			var dim1Size = count1.Ai();
			var lengths = new int[args != null ? args.Length + 1 : 1];
			var t = typeof(object);

			if (lengths.Length > 8)
				return Errors.ErrorOccurred($"COM array dimensions of {lengths.Length} is greater than the maximum allowed number of 8.");

			lengths[0] = dim1Size;

			for (var i = 0; i < args.Length; i++)
				lengths[i + 1] = args[i].Ai();

			return new ComObjArray(vt, lengths);
		}

		public IFuncObj __Enum(object count) => new ComArrayIndexValueEnumerator(this, count.Ai()).fo;

		public IEnumerator<object> GetEnumerator() => new ComArrayIndexValueEnumerator(this, 1);

		IEnumerator IEnumerable.GetEnumerator() => new ComArrayIndexValueEnumerator(this, 2);

		/// <summary>
		/// Gets the upper bound (inclusive) of the specified dimension.
		/// </summary>
		public object MaxIndex(object dim = null)
		{
			int d = dim.Ai(1);

			if (d < 1 || d > _dimensions)
				return Errors.ValueErrorOccurred($"Argument out of range.");

			_ = OleAuto.SafeArrayGetUBound(_psa, (uint)d, out int ub);
			return (long)ub;
		}

		/// <summary>
		/// Gets the lower bound (inclusive) of the specified dimension (1-based).
		/// </summary>
		public object MinIndex(object dim = null)
		{
			int d = dim.Ai(1);

			if (d < 1 || d > _dimensions)
				return Errors.ValueErrorOccurred($"Argument out of range.");

			_ = OleAuto.SafeArrayGetLBound(_psa, (uint)d, out int lb);
			return (long)lb;
		}

		/// <summary>
		/// Indexer for direct element access using 0-based indices.
		/// Negative indices count from the end.
		/// </summary>
		public object this[params object[] indices]
		{
			get
			{
				int[] idx = ConvertIndices(indices);
				object val = GetElementAtIndices(idx);
				return val;
			}
			set
			{
				int[] idx = ConvertIndices(indices);
				int hr = PutElementAtIndices(idx, value!);
				_ = Errors.OSErrorOccurredForHR(hr);
			}
		}

		/// <summary>
		/// Returns a new wrapper around a copy of this SafeArray.
		/// </summary>
		public new object Clone()
		{
			int hr = OleAuto.SafeArrayCopy(_psa, out nint psaCopy);
			if (hr < 0)
				return Errors.OSErrorOccurred(hr);
			var copy = (ComObjArray)RuntimeHelpers
					   .GetUninitializedObject(typeof(ComObjArray));
			copy.vt = this.vt;
			copy.Flags = this.Flags;
			copy._dimensions = this._dimensions;
			copy._baseType = this._baseType;
			copy._psa = psaCopy;
			copy.Ptr = psaCopy.ToInt64();
			return copy;
		}

		public override void Dispose()
		{
			if ((Flags & F_OWNVALUE) != 0 && _psa != 0)
			{
				_ = OleAuto.SafeArrayDestroy(_psa);
				_psa = 0;
				// Clear the flag so we don't double‐destroy:
				Flags &= ~F_OWNVALUE;
			}

			base.Dispose();
		}


		#region Helpers
		/// <summary>
		/// Converts indices to ints and handles negative indices
		/// </summary>
		private int[] ConvertIndices(object[] indices)
		{
			if (indices == null || indices.Length != _dimensions)
				throw new Error($"Expected {_dimensions} index(es), got {indices?.Length ?? 0}.");

			int[] idx = new int[indices.Length];

			for (int i = 0; i < idx.Length; i++)
			{
				int temp = indices[i].Ai();

				int lb = (int)(long)MinIndex(i + 1); // SAFEARRAY is 1-based for the dim parameter
				int ub = (int)(long)MaxIndex(i + 1);

				if (temp < 0)              // negative from end
					temp = ub + temp + 1;  // e.g. -1 -> ub
				else                       // 0-based to SAFEARRAY base
					temp = lb + temp;

				if (temp < lb || temp > ub)
					throw new Error($"Index {i} out of range [{lb}, {ub}]");

				idx[i] = temp;
			}

			return idx;
		}

		internal object GetElementAtIndices(int[] idx)
		{
			int bytes = ByteSizeForVarType(_baseType);
			IntPtr pv = Marshal.AllocCoTaskMem(bytes);

			try
			{
				int hr = OleAuto.SafeArrayGetElementPtr(_psa, idx, pv);

				if (hr < 0)
					return Errors.OSErrorOccurredForHR(hr);

				return VariantHelper.ReadVariant(pv, _baseType);
			}
			finally
			{
				Marshal.FreeCoTaskMem(pv);
			}
		}

		// Build a VARIANT for storing into a VT_VARIANT SAFEARRAY element.
		// Returns a flag telling you whether it's safe to VariantClear(&v) afterwards.
		private static VARIANT BuildVariantForElement(object value, out bool canClearAfterPut)
		{
			// Default: we can clear (to release BSTRs/interfaces we allocate)
			canClearAfterPut = true;

			if (value is null)
				return new VARIANT { vt = (ushort)VarEnum.VT_EMPTY };

			// If it's a ComObjArray, encode as VT_ARRAY|baseType pointing to its SAFEARRAY.
			// DO NOT clear this later, or you'd destroy the caller's array.
			if (value is ComObjArray coa)
			{
				canClearAfterPut = false; // we don't own coa._psa
				return new VARIANT
				{
					vt = (ushort)(VarEnum.VT_ARRAY | coa._baseType),
					ptrVal = coa._psa
				};
			}

			// If it's a ComValue that already represents a SAFEARRAY, use it as-is (we don't own it).
			if (value is ComValue cv && (cv.vt & VarEnum.VT_ARRAY) != 0)
			{
				nint psa = cv.Ptr is nint ip ? ip
						 : cv.Ptr is long lp ? (nint)lp
						 : 0;
				canClearAfterPut = false; // don't destroy external SAFEARRAY
				return new VARIANT
				{
					vt = (ushort)cv.vt,
					ptrVal = psa
				};
			}

			// Otherwise, let your existing builder create a proper VARIANT.
			// This allocates BSTRs / grabs interface pointers etc.
			// We DO want to VariantClear this after SafeArrayPutElement to avoid leaks.
			return VariantHelper.ValueToVariant(value);
		}

		internal int PutElementAtIndices(int[] idx, object value)
		{
			// VT_VARIANT arrays, let the marshaller coerce the type
			if (_baseType == VarEnum.VT_VARIANT)
			{
				unsafe
				{
					VARIANT v = BuildVariantForElement(value, out bool canClear);
					int hr = OleAuto.SafeArrayPutElementPtr(_psa, idx, (nint)(&v));

					// Only clear when safe: never clear a VARIANT that aliases a caller-owned SAFEARRAY.
					if (canClear)
						_ = VariantHelper.VariantClear((nint)(&v));

					_ = Errors.OSErrorOccurredForHR(hr);
					return hr;
				}
			}

			// Pointer element types: pass the pointer value directly (no staging buffer).
			if (_baseType == VarEnum.VT_UNKNOWN || _baseType == VarEnum.VT_DISPATCH)
			{
				nint pIface = 0;
				bool releaseInterface = false;

				// Accept Ptr properties or a plain RCW
				if (Marshal.IsComObject(value))
				{
					object src = value is ComValue c ? c.Ptr : value;
					// Get a temporary COM pointer we own; SafeArray will AddRef its own copy.
					pIface = (_baseType == VarEnum.VT_DISPATCH)
							 ? Marshal.GetIDispatchForObject(src)
							 : Marshal.GetIUnknownForObject(src);
					releaseInterface = true;
				}
				else
				{
					// raw interface pointer → pass as-is; SafeArrayPutElement will AddRef.
					pIface = (nint)Reflections.GetPtrProperty(value);
					releaseInterface = false;
				}

				try
				{
					int hr = OleAuto.SafeArrayPutElementPtr(_psa, idx, pIface);
					_ = Errors.OSErrorOccurredForHR(hr);
					return hr;
				}
				finally
				{
					if (releaseInterface && pIface != 0)
					{
						try { Marshal.Release(pIface); } catch { }
					}
				}
			}

			if (_baseType == VarEnum.VT_BSTR)
			{
				// For BSTR, pass the BSTR pointer directly; the array will own & free it.
				nint bstr = value == null ? 0 : Marshal.StringToBSTR(value.As());
				int hr = OleAuto.SafeArrayPutElementPtr(_psa, idx, bstr);
				_ = Errors.OSErrorOccurredForHR(hr);
				return hr;
			}

			// All other (non-pointer) element types need to be put in a temporary buffer
			// used by SafeArrayPutElement.
			int bytes = ByteSizeForVarType(_baseType);
			IntPtr pv = Marshal.AllocCoTaskMem(bytes);
			try
			{
				WriteValueToBuffer(pv, _baseType, value);
				int hr = OleAuto.SafeArrayPutElementPtr(_psa, idx, pv);
				_ = Errors.OSErrorOccurredForHR(hr);
				return hr;
			}
			finally
			{
				Marshal.FreeCoTaskMem(pv);
			}
		}

		private static int ByteSizeForVarType(VarEnum vt)
		{
			switch (vt)
			{
				case VarEnum.VT_I1:
				case VarEnum.VT_UI1:
					return 1;
				case VarEnum.VT_I2:
				case VarEnum.VT_UI2:
				case VarEnum.VT_BOOL:    // VARIANT_BOOL (short)
					return 2;
				case VarEnum.VT_I4:
				case VarEnum.VT_UI4:
					return 4;
				case VarEnum.VT_R4:
					return Marshal.SizeOf<float>();
				case VarEnum.VT_I8:
				case VarEnum.VT_UI8:
					return 8;
				case VarEnum.VT_R8:
				case VarEnum.VT_DATE:    // DATE is a double (8 bytes)
					return Marshal.SizeOf<double>();
				case VarEnum.VT_DECIMAL:
					return Marshal.SizeOf<decimal>();
				case VarEnum.VT_BSTR:
				case VarEnum.VT_UNKNOWN:
				case VarEnum.VT_DISPATCH:
					return IntPtr.Size; // pointer-sized storage
				case VarEnum.VT_VARIANT:
					return Marshal.SizeOf<VARIANT>();
				default:
					// Fallback for other pointer-like types if ever needed.
					return IntPtr.Size;
			}
		}

		private static void WriteValueToBuffer(IntPtr pv, VarEnum vt, object value)
		{
			switch (vt)
			{
				case VarEnum.VT_I1:
					Marshal.WriteByte(pv, unchecked((byte)Convert.ToSByte(value)));
					break;
				case VarEnum.VT_UI1:
					Marshal.WriteByte(pv, Convert.ToByte(value));
					break;
				case VarEnum.VT_I2:
					Marshal.WriteInt16(pv, Convert.ToInt16(value));
					break;
				case VarEnum.VT_UI2:
					Marshal.WriteInt16(pv, unchecked((short)Convert.ToUInt16(value)));
					break;
				case VarEnum.VT_I4:
					Marshal.WriteInt32(pv, Convert.ToInt32(value));
					break;
				case VarEnum.VT_UI4:
					Marshal.WriteInt32(pv, unchecked((int)Convert.ToUInt32(value)));
					break;
				case VarEnum.VT_I8:
					Marshal.WriteInt64(pv, Convert.ToInt64(value));
					break;
				case VarEnum.VT_UI8:
					Marshal.WriteInt64(pv, unchecked((long)Convert.ToUInt64(value)));
					break;
				case VarEnum.VT_R4:
					Marshal.StructureToPtr(Convert.ToSingle(value), pv, false);
					break;
				case VarEnum.VT_R8:
					Marshal.StructureToPtr(Convert.ToDouble(value), pv, false);
					break;
				case VarEnum.VT_BOOL:
					{
						bool b = value is bool bb ? bb : (Convert.ToInt32(value) != 0);
						Marshal.WriteInt16(pv, (short)(b ? -1 : 0));
						break;
					}
				case VarEnum.VT_DATE:
					{
						double oa = value is DateTime dt ? dt.ToOADate() : Convert.ToDouble(value);
						Marshal.StructureToPtr(oa, pv, false);
						break;
					}
				case VarEnum.VT_DECIMAL:
					Marshal.StructureToPtr(Convert.ToDecimal(value), pv, false);
					break;
				default:
					{
						// For any other pointer-like types, try to write pointer-sized data.
						if (value is IntPtr ip)
							Marshal.WriteIntPtr(pv, ip);
						else if (value is nint nip)
							Marshal.WriteIntPtr(pv, (IntPtr)nip);
						else
							Marshal.WriteIntPtr(pv, IntPtr.Zero);
						break;
					}
			}
		}
		#endregion
	}
}
#endif