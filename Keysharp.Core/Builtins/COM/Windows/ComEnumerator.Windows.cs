#if WINDOWS
namespace Keysharp.Builtins.COM
{
	internal static class ComEnumeration
	{
		internal static unsafe Enumerator CreateEnumerator(object source, int count)
		{
			using var wrapper = source is ComObject ? null : new ComObject(VarEnum.VT_DISPATCH, (long)Marshal.GetIDispatchForObject(source));
			var dispatch = source as ComObject ?? wrapper;
			var hr = dispatch.RawInvoke(Com.DISPID_NEWENUM, INVOKEKIND.INVOKE_FUNC, null, out var result);

			if (hr == ComValue.DISP_E_MEMBERNOTFOUND)
				hr = dispatch.RawInvoke(Com.DISPID_NEWENUM, INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET, null, out result);

			if (hr < 0 || result is not ComValue value || value.vt is not (VarEnum.VT_UNKNOWN or VarEnum.VT_DISPATCH)
					|| value.Ptr is not long address || address == 0)
			{
				(result as IDisposable)?.Dispose();
				_ = Errors.ErrorOccurred($"The COM object does not provide an IEnumVARIANT enumerator (HRESULT: 0x{hr:X8}).");
				return new Enumerator(source, count, () => false, null, null, null);
			}

			nint pointer;
			using (value)
				hr = Marshal.QueryInterface((nint)address, in Com.IID_IEnumVARIANT, out pointer);

			if (hr < 0)
			{
				_ = Errors.OSErrorOccurredForHR(hr);
				return new Enumerator(source, count, () => false, null, null, null);
			}

			var owner = new ComValue(VarEnum.VT_UNKNOWN, (long)pointer);
			// IEnumVARIANT follows IUnknown's three vtable slots: Next is 3 and Reset is 5.
			var vtable = *(nint**)pointer;
			object current = null;
			long type = 0;

			bool MoveNext()
			{
				if (owner.Ptr is null)
					return false;

				VARIANT variant = default;
				uint fetched = 0;

				try
				{
					var next = (delegate* unmanaged[Stdcall]<nint, uint, VARIANT*, uint*, int>)vtable[3];
					var nextHr = next(pointer, 1, &variant, &fetched);

					if (nextHr < 0)
					{
						_ = Errors.OSErrorOccurredForHR(nextHr);
						return false;
					}

					if (fetched == 0)
						return false;

					type = variant.vt;
					current = VariantHelper.VariantToValue(variant);
					return true;
				}
				finally
				{
					_ = VariantHelper.VariantClear(ref variant);
				}
			}

			void Reset()
			{
				if (owner.Ptr is not null)
				{
					var reset = (delegate* unmanaged[Stdcall]<nint, int>)vtable[5];
					var resetHr = reset(pointer);

					if (resetHr < 0)
						_ = Errors.OSErrorOccurredForHR(resetHr);
				}
			}

			return new Enumerator(source, count, MoveNext, () => current, () => (current, type), Reset, owner.Dispose);
		}
	}
}
#endif
