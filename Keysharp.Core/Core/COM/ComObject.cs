#if WINDOWS
namespace Keysharp.Core
{
	public unsafe class ComObject : ComValue, IDisposable//ComValue
	{
		public ComObject(params object[] args) : base(args) { }
		internal ComObject(object varType, object value, object flags = null) : base(varType, value, flags) { }

		public static object Call(object @this, object clsid, object iid = null)//progId, string iid)
		{
			var cls = clsid.As();
			var iidStr = iid.As();
			var hr = 0;
			var clsId = Guid.Empty;
			var id = Guid.Empty;

			while (true)
			{
				// It has been confirmed on Windows 10 that both CLSIDFromString and CLSIDFromProgID
				// were unable to resolve a ProgID starting with '{', like "{Foo", though "Foo}" works.
				// There are probably also guidelines and such that prohibit it.
				if (cls[0] == '{')
					hr = Com.CLSIDFromString(cls, out clsId);
				else
					// CLSIDFromString is known to be able to resolve ProgIDs via the registry,
					// but fails on registration-free classes such as "Microsoft.Windows.ActCtx".
					// CLSIDFromProgID works for that, but fails when given a CLSID string
					// (consistent with VBScript and JScript in both cases).
					hr = Com.CLSIDFromProgIDEx(cls, out clsId);

				if (hr < 0)
					break;

				if (iidStr.Length > 0)
				{
					hr = Com.CLSIDFromString(iidStr, out id);

					if (hr < 0)
						break;
				}
				else
					id = Com.IID_IDispatch;

				hr = Com.CoCreateInstance(ref clsId, null, Com.CLSCTX_SERVER, ref id, out var inst);

				if (hr < 0)
					break;

				nint ptr = 0;

				//If it was a specific interface, make sure we are pointing to that interface, otherwise the vtable
				//will be off in ComCall() and the program will crash.
				if (id != Guid.Empty && id != Com.IID_IDispatch)
				{
					var iptr = Marshal.GetIUnknownForObject(inst);
					if (Marshal.QueryInterface(iptr, in id, out ptr) != 0)
						return Errors.ErrorOccurred("Unable to query for the requested interface");
					Marshal.Release(iptr);
				} 
				else if (id == Com.IID_IDispatch)
				{
					ptr = Marshal.GetIDispatchForObject(inst);
				} else
				{
					ptr = Marshal.GetIUnknownForObject(inst);
				}
				if (Marshal.IsComObject(inst)) Marshal.ReleaseComObject(inst);

				return id == Com.IID_IDispatch ?
					new ComObject()
					{
						vt = VarEnum.VT_DISPATCH,
						Ptr = ptr
					}
					: new ComValue()
					{
						vt = VarEnum.VT_UNKNOWN,
						Ptr = ptr
					};
			}

			return Errors.OSErrorOccurredForHR(hr);
		}
	}
}

#endif