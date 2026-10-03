#if WINDOWS
using Keysharp.Builtins.COM;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A COM wrapper around a native SAFEARRAY, exposing AHK-friendly APIs.
	/// </summary>
	public class ComObjArray : ComValue, I__Enum, IEnumerable<object>
	{
		internal nint _psa;
		internal int _dimensions;   // number of dimensions
		internal VarEnum _baseType; // element VARTYPE, e.g. VT_VARIANT

		public long Dimensions => _dimensions;
		internal long Vt => (long)_baseType;

		/// <summary>
		/// Creates a new SafeArray of the specified element type and dimension sizes.
		/// </summary>
		/// <param name="baseType">The element VARTYPE (e.g. VT_VARIANT).</param>
		/// <param name="counts">Sizes for each dimension (1 to 8 values).</param>
		public ComObjArray(VarEnum baseType, params int[] counts) : base()
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

		public ComObjArray(VarEnum baseType, nint psa, bool takeOwnership) : base()
		{
			_baseType = baseType;
			_dimensions = OleAuto.SafeArrayGetDim(psa);
			_psa = psa;
			this.vt = VarEnum.VT_ARRAY | baseType;
			this.Flags = takeOwnership ? F_OWNVALUE : 0;
			this.Ptr = _psa.ToInt64();
		}

		public static object staticCall(object @this, object varType, object count1, params object[] args)
		{
			if (!varType.CoerceInt(out var vtRaw) || !count1.CoerceInt(out var dim1Size))
				return DefaultObject;

			var vt = (VarEnum)vtRaw;
			var lengths = new int[args != null ? args.Length + 1 : 1];
			var t = typeof(object);

			if (lengths.Length > 8)
				return Errors.ErrorOccurred($"COM array dimensions of {lengths.Length} is greater than the maximum allowed number of 8.");

			lengths[0] = dim1Size;

			for (var i = 0; i < args.Length; i++)
			{
				if (!args[i].CoerceInt(out var len))
					return DefaultObject;

				lengths[i + 1] = len;
			}

			return new ComObjArray(vt, lengths);
		}

		public KeysharpFunc __Enum(object count)
		{
			_ = count.TryCoerceInt(out var c);
			return CreateEnumerator(c);
		}

		IEnumerator<object> IEnumerable<object>.GetEnumerator() => CreateEnumerator(1);

		IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(2);

		/// <summary>
		/// Gets the upper bound (inclusive) of the specified dimension.
		/// </summary>
		public object MaxIndex(object dim = null)
		{
			if (!dim.CoerceInt(out var d, 1))
				return DefaultObject;

			if (d < 1 || d > _dimensions)
				return Errors.ValueErrorOccurred($"Argument out of range.");

			var hr = OleAuto.SafeArrayGetUBound(_psa, (uint)d, out var bound);
			return hr < 0 ? Errors.OSErrorOccurredForHR(hr) : (long)bound;
		}

		/// <summary>
		/// Gets the lower bound (inclusive) of the specified dimension (1-based).
		/// </summary>
		public object MinIndex(object dim = null)
		{
			if (!dim.CoerceInt(out var d, 1))
				return DefaultObject;

			if (d < 1 || d > _dimensions)
				return Errors.ValueErrorOccurred($"Argument out of range.");

			var hr = OleAuto.SafeArrayGetLBound(_psa, (uint)d, out var bound);
			return hr < 0 ? Errors.OSErrorOccurredForHR(hr) : (long)bound;
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
				if (idx == null)
					return DefaultObject;
				object val = GetElementAtIndices(idx);
				return val;
			}
			set
			{
				int[] idx = ConvertIndices(indices);
				if (idx == null)
					return;
				if (TryPutElementAtIndices(idx, value!, out var hr))
					_ = Errors.OSErrorOccurredForHR(hr);
			}
		}

		/// <summary>
		/// Returns a new wrapper around a copy of this SafeArray.
		/// </summary>
		public object Clone()
		{
			int hr = OleAuto.SafeArrayCopy(_psa, out nint psaCopy);
			if (hr < 0)
				return Errors.OSErrorOccurred(hr);
			return new ComObjArray(_baseType, psaCopy, takeOwnership: true);
		}

		[PublicHiddenFromUser]
		public override void Dispose()
		{
			if ((Flags & F_OWNVALUE) != 0 && _psa != 0)
			{
				if (OleAuto.SafeArrayDestroy(_psa) < 0)
					return;
			}

			_psa = 0;
			base.Dispose();
		}


		#region Helpers
		/// <summary>
		/// Converts indices to ints and handles negative indices
		/// </summary>
		private int[] ConvertIndices(object[] indices)
		{
			if (indices == null || indices.Length != _dimensions)
				return Errors.ErrorOccurred(new Error($"Expected {_dimensions} index(es), got {indices?.Length ?? 0}."), (int[])null);

			int[] idx = new int[indices.Length];

			for (int i = 0; i < idx.Length; i++)
			{
				if (!indices[i].CoerceInt(out var temp))
					return null;

				var hr = OleAuto.SafeArrayGetLBound(_psa, (uint)i + 1, out var lb);
				var ub = 0;
				if (hr >= 0) hr = OleAuto.SafeArrayGetUBound(_psa, (uint)i + 1, out ub);
				if (hr < 0)
				{
					_ = Errors.OSErrorOccurredForHR(hr);
					return null;
				}

				if (temp < 0)              // negative from end
					temp = ub + temp + 1;  // e.g. -1 -> ub
				else                       // 0-based to SAFEARRAY base
					temp = lb + temp;

				if (temp < lb || temp > ub)
					return Errors.ErrorOccurred(new Error($"Index {i} out of range [{lb}, {ub}]"), (int[])null);

				idx[i] = temp;
			}

			return idx;
		}

		private Enumerator CreateEnumerator(int count)
		{
			var indices = new int[_dimensions];
			var highs = new int[_dimensions];
			var lows = new int[_dimensions];
			var done = false;
			var empty = false;
			var index = -1L;
			object current = null;

			for (var i = 0; i < _dimensions; i++)
			{
				var hr = OleAuto.SafeArrayGetLBound(_psa, (uint)i + 1, out lows[i]);
				if (hr >= 0) hr = OleAuto.SafeArrayGetUBound(_psa, (uint)i + 1, out highs[i]);
				if (hr < 0)
				{
					_ = Errors.OSErrorOccurredForHR(hr);
					return null;
				}
				if (highs[i] < lows[i]) done = empty = true;
				indices[i] = lows[i];
			}

			indices[_dimensions - 1] = lows[_dimensions - 1] - 1;

			return new Enumerator(
					   this,
					   count,
					   MoveNext,
					   () => current,
					   () => (index, current),
					   Reset);

			bool MoveNext()
			{
				if (done)
					return false;

				for (var dim = _dimensions - 1; dim >= 0; dim--)
				{
					var next = indices[dim] + 1;

					if (next <= highs[dim])
					{
						indices[dim] = next;

						for (var j = dim + 1; j < _dimensions; j++)
							indices[j] = lows[j];

						index = indices[0] - lows[0];
						current = GetElementAtIndices(indices);
						return true;
					}

					if (dim == 0)
					{
						done = true;
						return false;
					}
				}

				done = true;
				return false;
			}

			void Reset()
			{
				for (var i = 0; i < _dimensions; i++)
					indices[i] = lows[i];

				indices[_dimensions - 1] = lows[_dimensions - 1] - 1;
				index = -1L;
				done = empty;
			}
		}

		internal unsafe object GetElementAtIndices(int[] indices)
		{
			// Release the lock before script code can reenter or dispose the array.
			var array = _psa;
			VARIANT value = default;
			var hr = OleAuto.SafeArrayLock(array);
			if (hr < 0) return Errors.OSErrorOccurredForHR(hr);
			try
			{
				hr = OleAuto.SafeArrayPtrOfIndex(array, indices, out var data);
				if (hr >= 0)
				{
					var borrowed = VariantHelper.ReadStorage(data, _baseType);
					hr = VariantHelper.VariantCopy(ref value, in borrowed);
				}
			}
			finally { _ = OleAuto.SafeArrayUnlock(array); }
			try { return hr < 0 ? Errors.OSErrorOccurredForHR(hr) : VariantHelper.FromVariant(ref value, Ownership.Owned); }
			finally { _ = VariantHelper.VariantClear(ref value); GC.KeepAlive(this); }
		}

		internal unsafe bool TryPutElementAtIndices(int[] indices, object value, out int hr)
		{
			if (_baseType == VarEnum.VT_BSTR && value != null)
			{
				if (!value.CoerceString(out var text))
				{
					hr = 0;
					return false;
				}
				value = text;
			}
			if (!VariantHelper.TryToTypedVariant(value, _baseType, out var variant))
			{
				hr = 0;
				return false;
			}
			try
			{
				var array = _psa;
				hr = OleAuto.SafeArrayLock(array);
				if (hr < 0) return true;
				try
				{
					hr = OleAuto.SafeArrayPtrOfIndex(array, indices, out var data);
					if (hr < 0) return true;
					var previous = VariantHelper.ReadStorage(data, _baseType);
					hr = VariantHelper.VariantClear(ref previous);
					if (hr < 0) return true;
					VariantHelper.WriteStorage(data, _baseType, in variant);
					variant = default;
					return true;
				}
				finally { _ = OleAuto.SafeArrayUnlock(array); }
			}
			finally { _ = VariantHelper.VariantClear(ref variant); GC.KeepAlive(this); }
		}

		#endregion
	}
}
#endif
