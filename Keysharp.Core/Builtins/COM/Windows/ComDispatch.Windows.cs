#if WINDOWS
#nullable enable
using ct = System.Runtime.InteropServices.ComTypes;

namespace Keysharp.Builtins.COM
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
		private ct.ITypeInfo? typeInfo;
		private readonly object typeInfoGate = new();
		private readonly ConcurrentDictionary<int, string> eventNames = [];
		private readonly WeakReference<ComValue> source;

		public ComValue? Co => source.TryGetTarget(out var target) ? target : null;

		internal Guid InterfaceId => interfaceID;
		internal bool IsConnected => connection != null;

		internal Dispatcher(ComValue? cobj)
		{
			ArgumentNullException.ThrowIfNull(cobj);
			source = new(cobj);

			Reflections.TryGetPtrProperty(cobj, out var cobjAddr);
			var pUnk = new nint(cobjAddr);
			var containerObj = Marshal.GetObjectForIUnknown(pUnk);
			ct.ITypeInfo? classTi = null;
			ct.ITypeInfo? chosenTi = null;
			try
			{
				if (containerObj is not ct.IConnectionPointContainer cpContainer)
				{
					_ = Errors.ValueErrorOccurred(
						$"The passed in COM object of type {containerObj.GetType()} was not of type IConnectionPointContainer.");
					return;
				}


				// Try to obtain a *class* typeinfo first; if not, we may get an *interface* TI.
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

							nint pAttr2 = 0;
							try
							{
								eventTi.GetTypeAttr(out pAttr2);
								iid = Marshal.PtrToStructure<ct.TYPEATTR>(pAttr2).guid;
								sinkTi = eventTi; // keep (we own this reference)
								return true;
							}
							finally
							{
								if (pAttr2 != 0) eventTi.ReleaseTypeAttr(pAttr2);
								if (!ReferenceEquals(eventTi, sinkTi)) Marshal.ReleaseComObject(eventTi);
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
						try
						{
							if (enumPts.Next(1, arr, 0) == 0)
							{
								var cp = arr[0];
								try
								{
									cp.GetConnectionInterface(out var iid);
									chosenIid = iid;

									// Try to resolve ITypeInfo for this IID using any containing type-lib we can get.
									chosenTi = ResolveTypeInfoForIID(iid, classTi);
								}
								finally
								{
									Marshal.ReleaseComObject(cp);
								}
							}
						}
						finally { Marshal.ReleaseComObject(enumPts); }
					}
				}

				// Finally, connect
				if (chosenIid is Guid g)
				{
					cpContainer.FindConnectionPoint(ref g, out var cp);
					if (cp != null)
					{
						interfaceID = g;
						typeInfo = chosenTi;
						chosenTi = null;
						try { cp.Advise(this, out cookie); }
						catch { Marshal.ReleaseComObject(cp); throw; }
						connection = cp;
						return;
					}
				}

				_ = Errors.ErrorOccurred("Failed to connect dispatcher to COM interface.");
			}
			catch { Dispose(); throw; }
			finally
			{
				if (classTi != null) Marshal.ReleaseComObject(classTi);
				if (chosenTi != null) Marshal.ReleaseComObject(chosenTi);
				if (Marshal.IsComObject(containerObj)) Marshal.ReleaseComObject(containerObj);
			}
		}

		// Try to fetch a typeinfo for an IID from any type-lib we can reach.
		private static ct.ITypeInfo? ResolveTypeInfoForIID(Guid iid, ct.ITypeInfo? anyTi)
		{
			ct.ITypeLib? tl = null;

			try
			{
				if (anyTi == null) return null;
				anyTi.GetContainingTypeLib(out tl, out _);
				if (tl == null) return null;
				tl.GetTypeInfoOfGuid(ref iid, out var ti);
				return ti;
			}
			catch (COMException) { return null; }
			finally
			{
				if (tl != null) Marshal.ReleaseComObject(tl);
			}
		}


		public void Dispose()
		{
			ct.ITypeInfo? info;
			lock (typeInfoGate)
			{
				if (disposedValue) return;
				disposedValue = true;
				info = typeInfo;
				typeInfo = null;
			}
			var point = Interlocked.Exchange(ref connection, null);
			try
			{
				if (point != null && cookie != 0) point.Unadvise(cookie);
			}
			finally
			{
				cookie = 0;
				if (point != null) Marshal.ReleaseComObject(point);
				if (info != null) Marshal.ReleaseComObject(info);
			}
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

		public unsafe int Invoke(int dispIdMember, ref Guid riid, int lcid,
						  ct.INVOKEKIND wFlags, ref ct.DISPPARAMS pDispParams,
						  nint pVarResult, nint pExcepInfo, nint puArgErr)
		{
			const int S_OK = 0;
			const int DISP_E_EXCEPTION = unchecked((int)0x80020009);

			try
			{
				using var caught = Keysharp.Runtime.Flow.EnterTry();
				// Preserve BYREF pointers for synchronous write-back; queued handlers keep only copied values.
				int n = pDispParams.cArgs;
				var args = n > 0 ? new object[n] : [];
				List<(VARIANT Variant, VarRef Reference)>? byrefCells = null;

				int sizeVARIANT = Marshal.SizeOf<VARIANT>();
				for (int i = 0; i < n; i++)
				{
					// rgvarg is right-to-left; destination is left-to-right
					int dst = n - 1 - i;
					nint pVar = pDispParams.rgvarg + i * sizeVARIANT;
					var v = *(VARIANT*)pVar;
					var current = VariantHelper.FromVariant(ref v, Ownership.Borrowed);

					if (((VarEnum)v.vt & VarEnum.VT_BYREF) != 0)
					{
						var vr = new VarRef(current);
						args[dst] = vr;

						(byrefCells ??= []).Add((v, vr));
					}
					else
						args[dst] = current;
				}

				if (!eventNames.TryGetValue(dispIdMember, out var name))
				{
					lock (typeInfoGate)
					{
						if (typeInfo != null)
						{
							var names = new string[1];
							try { typeInfo.GetNames(dispIdMember, names, 1, out _); } catch (COMException) { }
							name = names[0];
						}
					}
					eventNames[dispIdMember] = name ??= $"DISPID_{dispIdMember}";
				}

				var evt = new DispatcherEventArgs(dispIdMember, name, args);
				EventReceived?.Invoke(this, evt);
				object? result = evt.Result;

				if (evt.IsHandled && byrefCells != null)
					foreach (var (byrefV, vr) in byrefCells)
						VariantHelper.WriteByRefVariant(byrefV, vr.__Value);

				// 5) Only produce a VARIANT result for FUNC/GET (not PUT/PUTREF)
				bool wantsResult = pVarResult != 0 &&
					(wFlags & (ct.INVOKEKIND.INVOKE_FUNC | ct.INVOKEKIND.INVOKE_PROPERTYGET)) != 0 &&
					(wFlags & (ct.INVOKEKIND.INVOKE_PROPERTYPUT | ct.INVOKEKIND.INVOKE_PROPERTYPUTREF)) == 0;

				if (wantsResult)
				{
					VariantHelper.VariantInit(pVarResult);
					if (!VariantHelper.TryToVariant(result, out var variant)) return S_OK;
					*(VARIANT*)pVarResult = variant;
				}

				return S_OK;
			}
			catch (Exception ex) when (CallStack.RememberAndCatch(ex))
			{
				//As in AutoHotkey, a caller which takes no exception information cannot pass the error on, so it is reported.
				if (pExcepInfo == 0)
				{
					_ = Errors.ReportUncaught(ex);
					return unchecked((int)0x80004005);//E_FAIL
				}

				try
				{
					var ei = new EXCEPINFO { scode = DISP_E_EXCEPTION, bstrDescription = ex.Message };
					Marshal.StructureToPtr(ei, pExcepInfo, false);
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
