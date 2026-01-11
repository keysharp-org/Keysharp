#if WINDOWS
#nullable enable
using ct = System.Runtime.InteropServices.ComTypes;

namespace Keysharp.Core.COM
{
	/// <summary>
	/// The IDispatch interface.
	/// This was taken loosely from https://github.com/PowerShell/PowerShell/blob/master/src/System.Management.Automation/engine/COM/
	/// under the MIT license.
	/// </summary>
	[ComImport]
	[Guid("00020400-0000-0000-c000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IDispatch
	{
		[PreserveSig]
		int GetTypeInfoCount(out uint info);

		[PreserveSig]
		int GetTypeInfo(int iTInfo, int lcid, out ct.ITypeInfo? ppTInfo);

		[PreserveSig]
		int GetIDsOfNames(
			[In] ref Guid guid,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 2)]
			string[] names,
			int cNames, int lcid,
			[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
			int[] rgDispId);

		[PreserveSig]
		int Invoke(int dispIdMember,
				   [In] ref Guid riid,
				   int lcid,
				   ct.INVOKEKIND wFlags,
				   ref ct.DISPPARAMS pDispParams,
				   nint pVarResult, nint pExcepInfo, nint puArgErr);
	}

	[ComImport]
	[Guid("B196B283-BAB4-101A-B69C-00AA00341D07")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IProvideClassInfo
	{
		[PreserveSig]
		int GetClassInfo(out ct.ITypeInfo typeInfo);
	}

	[ComImport]
	[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IServiceProvider
	{
		[return: MarshalAs(UnmanagedType.I4)]
		[PreserveSig]
		int QueryService(
			[In] ref Guid guidService,
			[In] ref Guid riid,
			[Out] out nint ppvObject);
	}

	[ComImport]
	[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")] // IID_IInspectable
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IInspectable
	{
		// HRESULT GetIids(ULONG* iidCount, IID** iids)
		int GetIids(out uint iidCount, out nint iids);

		// HRESULT GetRuntimeClassName(HSTRING* className)
		int GetRuntimeClassName(out nint className);

		// HRESULT GetTrustLevel(TrustLevel* trustLevel)
		int GetTrustLevel(out int trustLevel);
	}

	/// <summary>
	/// Solution for event handling taken from the answer to my post at:
	/// https://stackoverflow.com/questions/77010721/how-to-late-bind-an-event-sink-for-a-com-object-of-unknown-type-at-runtime-in-c
	/// </summary>
	internal class Dispatcher : IDisposable, IDispatch, ICustomQueryInterface
	{
		private const int E_NOTIMPL = unchecked((int)0x80004001);
		private static readonly Guid IID_IManagedObject = new ("{C3FCC19E-A970-11D2-8B5A-00A0C9B7C9C4}");
		internal static readonly Guid IID_IDispatch = new ("{00020400-0000-0000-c000-000000000046}");
		private ct.IConnectionPoint? connection;
		private int cookie;
		private bool disposedValue;
		private readonly Guid interfaceID;
		private readonly ct.ITypeInfo? typeInfo;

		public ComValue? Co { get; }

		internal Guid InterfaceId => interfaceID;

		internal Dispatcher(ComValue? cobj)
		{
			ArgumentNullException.ThrowIfNull(cobj);

			var pUnk = (nint)Reflections.GetPtrProperty(cobj);
			var containerObj = Marshal.GetObjectForIUnknown(pUnk);
			if (containerObj is not ct.IConnectionPointContainer cpContainer)
			{
				_ = Errors.ValueErrorOccurred(
					$"The passed in COM object of type {containerObj.GetType()} was not of type IConnectionPointContainer.");
				return;
			}

			Co = cobj;

			// Try to obtain a *class* typeinfo first; if not, we may get an *interface* TI.
			ct.ITypeInfo? classTi = null;
			if (containerObj is IProvideClassInfo ipci)
			{
				if (ipci.GetClassInfo(out classTi) < 0) classTi = null;
			}
			else if (containerObj is IDispatch disp)
			{
				// NB: This is often an *interface* TI, not a coclass – flags may all be 0.
				_ = disp.GetTypeInfo(0, 0, out classTi);
			}

			Guid? chosenIid = null;
			ct.ITypeInfo? chosenTi = null;

			bool TryPickSourceFromImplTypes(ct.ITypeInfo ti, bool preferDefault, out Guid iid, out ct.ITypeInfo? sinkTi)
			{
				iid = Guid.Empty; sinkTi = null;

				ti.GetTypeAttr(out var pAttr);
				var ta = Marshal.PtrToStructure<ct.TYPEATTR>(pAttr);
				try
				{
					for (int j = 0; j < ta.cImplTypes; j++)
					{
						ti.GetImplTypeFlags(j, out var flags);

						bool isSource = flags.HasFlag(ct.IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE);
						bool isDefault = flags.HasFlag(ct.IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT);

						if (!isSource) continue;
						if (preferDefault && !isDefault) continue; // pass 1: prefer default
																   // pass 2: accept any source

						ti.GetRefTypeOfImplType(j, out int href);
						ti.GetRefTypeInfo(href, out var eventTi);

						nint pAttr2;
						eventTi.GetTypeAttr(out pAttr2);
						var ta2 = Marshal.PtrToStructure<ct.TYPEATTR>(pAttr2);
						try
						{
							iid = ta2.guid;
							sinkTi = eventTi; // keep (we own this reference)
							eventTi = null;   // prevent release in finally
							return true;
						}
						finally
						{
							if (pAttr2 != 0) eventTi?.ReleaseTypeAttr(pAttr2);
							if (eventTi != null) Marshal.ReleaseComObject(eventTi);
						}
					}
				}
				finally
				{
					ti.ReleaseTypeAttr(pAttr);
				}
				return false;
			}

			// 1) Try default+source first, 2) then any source
			if (classTi != null)
			{
				if (!TryPickSourceFromImplTypes(classTi, preferDefault: true, out var iid1, out var ti1))
					TryPickSourceFromImplTypes(classTi, preferDefault: false, out iid1, out ti1);
				if (ti1 != null)
				{
					chosenIid = iid1;
					chosenTi = ti1; // keep it
				}
			}

			// 3) Fallback: enumerate connection points
			if (chosenIid == null)
			{
				cpContainer.EnumConnectionPoints(out var enumPts);
				if (enumPts != null)
				{
					var arr = new ct.IConnectionPoint[1];
					while (true)
					{
						int hr = enumPts.Next(1, arr, 0);
						if (hr != 0) break;
						var cp = arr[0];
						try
						{
							cp.GetConnectionInterface(out var iid);
							chosenIid = iid;

							// Try to resolve ITypeInfo for this IID using any containing type-lib we can get.
							chosenTi = ResolveTypeInfoForIID(iid, classTi);
							break; // take the first CP
						}
						finally
						{
							Marshal.ReleaseComObject(cp);
						}
					}
				}
			}

			// Finally, connect
			if (chosenIid is Guid g)
			{
				cpContainer.FindConnectionPoint(ref g, out var cp);
				if (cp != null)
				{
					interfaceID = g;
					typeInfo = chosenTi; // keep this alive for name lookup (may be null)
					cp.Advise(this, out cookie);
					connection = cp;
					return;
				}
			}

			if (chosenTi != null) Marshal.ReleaseComObject(chosenTi);
			_ = Errors.ErrorOccurred("Failed to connect dispatcher to COM interface.");
		}

		// Try to fetch a typeinfo for an IID from any type-lib we can reach.
		private static ct.ITypeInfo? ResolveTypeInfoForIID(Guid iid, ct.ITypeInfo? anyTi)
		{
			ct.ITypeInfo? ti = null;
			ct.ITypeLib? tl = null;
			int index;

			try
			{
				if (anyTi != null)
				{
					anyTi.GetContainingTypeLib(out tl, out index);
					if (tl != null)
					{
						tl.GetTypeInfoOfGuid(ref iid, out ti);
						if (ti != null) return ti;
					}
				}
			}
			catch { /* ignore */ }
			finally
			{
				if (tl != null) Marshal.ReleaseComObject(tl);
			}

			return null; // okay – we’ll still connect, names will be "DISPID_N"
		}


		~Dispatcher()
		{
			Dispose(disposing: false);
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		[PreserveSig]
		public int GetIDsOfNames(
			[In] ref Guid guid,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 2)]
			string[] names,
			int cNames, int lcid,
			[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
			int[] rgDispId) => E_NOTIMPL;

		public int GetTypeInfo(int iTInfo, int lcid, out ct.ITypeInfo? ppTInfo)
		{ ppTInfo = null; return E_NOTIMPL; }

		public int GetTypeInfoCount(out uint pctinfo)
		{ pctinfo = 0; return 0; }

		public int Invoke(int dispIdMember, ref Guid riid, int lcid,
						  ct.INVOKEKIND wFlags, ref ct.DISPPARAMS pDispParams,
						  nint pVarResult, nint pExcepInfo, nint puArgErr)
		{
			const int S_OK = 0;
			const int DISP_E_EXCEPTION = unchecked((int)0x80020009);

			try
			{
				// 1) Decode rgvarg manually so we can see VT_BYREF and preserve backing pointers
				int n = pDispParams.cArgs;
				var args = n > 0 ? new object[n] : System.Array.Empty<object>();
				var byrefCells = new List<(int argIndex, VARIANT v, VarRef)>(); // for write-back

				int sizeVARIANT = Marshal.SizeOf<VARIANT>();
				for (int i = 0; i < n; i++)
				{
					// rgvarg is right-to-left; destination is left-to-right
					int dst = n - 1 - i;
					nint pVar = pDispParams.rgvarg + i * sizeVARIANT;
					var v = Marshal.PtrToStructure<VARIANT>(pVar);
					var vt = (VarEnum)v.vt;

					if ((vt & VarEnum.VT_BYREF) != 0)
					{
						// Read current value
						object current = VariantHelper.ReadByRefVariant(v);
						var vr = new VarRef(() => current, val => current = val);
						args[dst] = vr;

						// We'll write back after the handler returns
						byrefCells.Add((dst, v, vr));
					}
					else
					{
						// Non-byref value: use the general converter
						args[dst] = VariantHelper.VariantToValue(v);
					}
				}

				// 2) Name (best-effort)
				string name = default!;
				if (typeInfo != null)
				{
					var names = new string[1];
					try { typeInfo.GetNames(dispIdMember, names, 1, out _); } catch { }
					name = names[0];
				}
				name ??= $"DISPID_{dispIdMember}";

				// 3) Dispatch to the script, the event handler marshals to the main thread
				var evt = new DispatcherEventArgs(dispIdMember, name, args);
				OnEvent(this, evt);
				object result = evt.Result;

				if (evt.IsHandled)
				{
					// 4) Write back any byref VARIANTs
					foreach (var (argIndex, byrefV, vr) in byrefCells)
					{
						try
						{
							VariantHelper.WriteByRefVariant(byrefV, vr.__Value);
						}
						catch (Exception) { /* avoid tearing down the call for a single write-back issue */ }
					}
				}

				// 5) Only produce a VARIANT result for FUNC/GET (not PUT/PUTREF)
				bool wantsResult = pVarResult != 0 &&
					(wFlags & (ct.INVOKEKIND.INVOKE_FUNC | ct.INVOKEKIND.INVOKE_PROPERTYGET)) != 0 &&
					(wFlags & (ct.INVOKEKIND.INVOKE_PROPERTYPUT | ct.INVOKEKIND.INVOKE_PROPERTYPUTREF)) == 0;

				if (wantsResult)
				{
					VariantHelper.VariantInit(pVarResult);
					if (result is Keysharp.Core.ComValue cv)
					{
						var v = cv.ToVariant();
						Marshal.StructureToPtr(v, pVarResult, false);
					}
					else
					{
						Marshal.GetNativeVariantForObject(result, pVarResult);
					}
				}

				return S_OK;
			}
			catch (Exception ex)
			{
				try
				{
					if (pExcepInfo != 0)
					{
						var ei = new EXCEPINFO { bstrDescription = ex.Message };
						Marshal.StructureToPtr(ei, pExcepInfo, false);
					}
				}
				catch { }
				return DISP_E_EXCEPTION;
			}
		}


		CustomQueryInterfaceResult ICustomQueryInterface.GetInterface(ref Guid iid, out nint ppv)
		{
			if (iid == typeof(IDispatch).GUID || iid == InterfaceId)
			{
				ppv = Marshal.GetComInterfaceForObject(this, typeof(IDispatch), CustomQueryInterfaceMode.Ignore);
				return CustomQueryInterfaceResult.Handled;
			}

			ppv = 0;
			return iid == IID_IManagedObject ? CustomQueryInterfaceResult.Failed : CustomQueryInterfaceResult.NotHandled;
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				var connection = Interlocked.Exchange(ref this.connection, null);

				if (connection != null)
				{
					connection.Unadvise(cookie);
					cookie = 0;
					//_ = Marshal.ReleaseComObject(connection);
				}
				if (typeInfo != null)
					Marshal.ReleaseComObject(typeInfo);

				disposedValue = true;
			}
		}

		protected virtual void OnEvent(object sender, DispatcherEventArgs e) => EventReceived?.Invoke(sender, e);

		internal event EventHandler<DispatcherEventArgs>? EventReceived;
	}

	internal class DispatcherEventArgs : EventArgs
	{
		internal object?[] Arguments { get; }

		internal int DispId { get; }

		internal string Name { get; }

		internal bool IsHandled { get; set; }

		internal object? Result { get; set; }

		internal DispatcherEventArgs(int dispId, string name, params object?[] arguments)
		{
			DispId = dispId;
			Name = name;
			Arguments = arguments ?? [];
		}
	}
}
#endif