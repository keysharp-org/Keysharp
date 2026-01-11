#if WINDOWS
namespace Keysharp.Core.COM
{
	internal class ComMethodData
	{
		internal ConcurrentLfu<nint, Dictionary<string, ComMethodInfo>> comMethodCache = new(Caching.DefaultCacheCapacity);
	}

	internal class ComMethodInfo
	{
		internal Type[] expectedTypes;
		internal ParameterModifier[] modifiers;
		internal INVOKEKIND invokeKind;
	}

	unsafe public static partial class Com
	{
		public const int variantTypeMask = 0xfff;
		internal static Guid IID_IDispatch = new (0x00020400, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
		internal static Guid IID_IServiceProvider = new ("6d5140c1-7436-11ce-8034-00aa006009fa");
		internal const int CLSCTX_INPROC_SERVER = 0x1;
		internal const int CLSCTX_INPROC_HANDLER = 0x2;
		internal const int CLSCTX_LOCAL_SERVER = 0x4;
		internal const int CLSCTX_INPROC_SERVER16 = 0x8;
		internal const int CLSCTX_REMOTE_SERVER = 0x10;
		internal const int CLSCTX_SERVER = CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER; //16;
		internal const int LOCALE_SYSTEM_DEFAULT = 0x800;
		internal const int LOCALE_USER_DEFAULT = 0x400;
		internal static HashSet<ComEvent> comEvents = [];

		internal const int DISPID_PROPERTYPUT = -3;

		[DllImport(WindowsAPI.ole32, CharSet = CharSet.Unicode)]
		public static extern int CoCreateInstance(ref Guid clsid,
				[MarshalAs(UnmanagedType.IUnknown)] object inner,
				uint context,
				ref Guid uuid,
				[MarshalAs(UnmanagedType.IUnknown)] out object rReturnedComObject);

		public static object ComObjActive(object clsid) => GetActiveObject(clsid.As());

		internal static object ConvertToCOMType(object ret)
		{
			if (ret is long ll && ll < int.MaxValue)
				ret = (int)ll;
			else if (ret is bool bl)
				ret = bl ? -1 : 0;

			return ret;
		}

		public static object ComObjConnect(object comObj, object prefixOrSink = null, object debug = null)
		{
			if (comObj is ComObject co)
			{
				if (co.vt != VarEnum.VT_DISPATCH && co.vt != VarEnum.VT_UNKNOWN)// || Marshal.GetIUnknownForObject(co.Ptr) == 0)
					return Errors.ValueErrorOccurred($"COM object type of {co.vt} was not VT_DISPATCH or VT_UNKNOWN, and was not IUnknown.");

				//If it existed, whether obj1 was null or not, remove it.
				if (comEvents.FirstOrDefault(ce => ReferenceEquals(ce.dispatcher.Co, co)) is ComEvent ev)
				{
					_ = comEvents.Remove(ev);
					ev.Unwire();
					ev.dispatcher.Dispose();
				}

				if (prefixOrSink != null)//obj1 not being null means add it.
					_ = comEvents.Add(new ComEvent(new Dispatcher(co), prefixOrSink, debug != null ? debug.Ab() : false));
			}

			return DefaultErrorObject;
		}

		public static object ComObjFlags(object comObj, object newFlags = null, object mask = null)
		{
			if (comObj is ComObject co)
			{
				var flags = newFlags != null ? newFlags.Al() : 0L;
				var m = mask != null ? mask.Al() : 0L;

				if (newFlags == null && mask == null)
				{
					if (flags < 0)
					{
						flags = 0;
						m = -flags;
					}
					else
						m = flags;
				}

				co.Flags = (co.Flags & ~m) | (flags & m);
				return co.Flags;
			}

			return 0L;
		}

		public static object ComObjFromPtr(object dispPtr)
		{
			var ptr = Reflections.GetPtrProperty(dispPtr);
			if (ptr != 0L)
				return new ComObject(VarEnum.VT_DISPATCH, ptr);

			return Errors.TypeErrorOccurred(dispPtr, typeof(IDispatch), DefaultErrorObject);
		}

		public static object ComObjGet(object name) => Marshal.BindToMoniker(name.As());

		public static object ComObjQuery(object comObj, object sidiid = null, object iid = null)
		{
			nint ptr;

			if (comObj is Any kso && Script.TryGetPropertyValue(out object kptr, kso, "ptr"))
				comObj = kptr;

			if (comObj is long l)
				ptr = new nint(l);
			else
				return Errors.ValueErrorOccurred($"The passed in object {comObj} of type {comObj.GetType()} was not a ComObject or a raw COM interface.");

			nint resultPtr = 0;
			Guid id = Guid.Empty;
			int hr = 0;

			if (sidiid != null && iid != null)
			{
				var sidstr = sidiid.As();
				var iidstr = iid.As();

				if (CLSIDFromString(sidstr, out var sid) >= 0 && CLSIDFromString(iidstr, out id) >= 0)
				{
					// Query for a service: use IServiceProvider::QueryService.
					IServiceProvider sp = (IServiceProvider)Marshal.GetObjectForIUnknown(ptr);
					hr = sp.QueryService(ref sid, ref id, out resultPtr);
				}
			}
			else if (sidiid != null)
			{
				var iidstr = sidiid.As();

				if (CLSIDFromString(iidstr, out id) >= 0)
				{
					hr = Marshal.QueryInterface(ptr, id, out resultPtr);
				}
			}

			if (hr < 0)
				return Errors.OSErrorOccurredForHR(hr);

			if (resultPtr == 0)
				return Errors.ErrorOccurred($"Unable to get COM interface with arguments {sidiid}, {iid}.");

			return id == IID_IDispatch ? new ComObject(VarEnum.VT_DISPATCH, (long)resultPtr) : new ComValue(VarEnum.VT_UNKNOWN, (long)resultPtr);
		}

		public static object ComObjType(object comObj, object infoType = null)
		{
			var s = infoType.As().ToLower();
			var co = comObj as ComObject;

			if (s == "" && co != null)
			{
				return (long)co.vt;
			}

			var pUnk = (nint)Reflections.GetPtrProperty(comObj);

			ITypeInfo typeInfo = null;
			
			var rcw = Marshal.GetObjectForIUnknown(pUnk);
			try
			{
				if (s.StartsWith('c'))
				{
					if (rcw is IProvideClassInfo ipci)
						_ = ipci.GetClassInfo(out typeInfo);

					if (s == "class")
						s = "name";
					else if (s == "clsid")
						s = "iid";
				}
				else if (co != null && co.vt == VarEnum.VT_DISPATCH && co.TryGetITypeInfo(out typeInfo))
				{
				}
				else if (rcw is IDispatch idisp)
					_ = idisp.GetTypeInfo(0, 0, out typeInfo);

				if (typeInfo != null)
				{
					try
					{
						if (s == "name")
						{
							typeInfo.GetDocumentation(-1, out var typeName, out var documentation, out var helpContext, out var helpFile);
							return typeName;
						}
						else if (s == "iid")
						{
							typeInfo.GetTypeAttr(out var typeAttr);
							var attr = Marshal.PtrToStructure<TYPEATTR>(typeAttr);
							var guid = attr.guid.ToString("B").ToUpper();
							typeInfo.ReleaseTypeAttr(typeAttr);
							return guid;
						}
					}
					finally
					{
						if (Marshal.IsComObject(typeInfo)) Marshal.ReleaseComObject(typeInfo);
					}
				}
				else if (rcw is IInspectable insp)
				{
					if (s == "name")
					{
						insp.GetRuntimeClassName(out var hstr);
						if (hstr != 0)
						{
							nint buf = WindowsAPI.WindowsGetStringRawBuffer(hstr, out uint length);
							string clsName = Marshal.PtrToStringUni(buf, (int)length) ?? string.Empty;
							WindowsAPI.WindowsDeleteString(hstr);
							return clsName;
						}
						return "";
					}
					else if (s == "iid")
					{
						insp.GetIids(out var count, out var pIids);
						try
						{
							int sz = Marshal.SizeOf<Guid>();
							// Iterate IIDs, QI, and compare pointers
							for (uint i = 0; i < count; i++)
							{
								nint pIid = pIids + (int)(i * (uint)sz);
								Guid iid = Marshal.PtrToStructure<Guid>(pIid);
									
								var hr = Marshal.QueryInterface(pUnk, in iid, out nint pIface);
								if (hr >= 0 && pIface != 0)
								{
									try
									{
										if (pIface == pUnk)
											return iid.ToString("B").ToUpper();
									}
									finally { Marshal.Release(pIface); }
								}
							}
						}
						finally { Marshal.FreeCoTaskMem(pIids); }
					}
				}
			} 
			finally
			{
				if (Marshal.IsComObject(rcw)) Marshal.ReleaseComObject(rcw);
			}

			return Errors.ErrorOccurred($"Unable to get COM object type information with argument {infoType}.");
		}

		public static object ComObjValue(object comObj)
		{
			if (comObj is ComValue co)
			{
				return co.Ptr;
			}
			else//Unsure if this logic even makes sense.
			{
				var gch = GCHandle.Alloc(comObj, GCHandleType.Pinned);
				var val = gch.AddrOfPinnedObject();
				gch.Free();
				return val;
			}
		}

		public static object ObjAddRef(object ptr)
		{
			nint unk = 0;

			if (ptr is ComValue co)
				ptr = co.Ptr;

			if (ptr is long l)
			{
				unk = new nint(l);
			}
			else
			{
				unk = Marshal.GetIUnknownForObject(ptr);
				_ = Marshal.AddRef(unk);
				return (long)Marshal.Release(unk);//GetIUnknownForObject already added 1.
			}

			return (long)Marshal.AddRef(unk);
		}

		public static object ObjRelease(object ptr)
		{
			if (ptr is ComValue co)
				ptr = co.Ptr;

			if (ptr is long l)
				ptr = new nint(l);
			else
				return Errors.TypeErrorOccurred(ptr, typeof(ComValue), DefaultErrorObject);

			return (long)Marshal.Release((nint)ptr);
		}

		/// <summary>
		/// Gotten loosely from https://social.msdn.microsoft.com/Forums/vstudio/en-US/cbb92470-979c-4d9e-9555-f4de7befb42e/how-to-directly-access-the-virtual-method-table-of-a-com-interface-pointer?forum=csharpgeneral
		/// </summary>
		public static object ComCall(object index, object comObj, params object[] parameters)
		{
			var idx = index.Ai();

			if (idx < 0)
				return Errors.ValueErrorOccurred($"Index value of {idx} was less than zero.");

			nint pUnk = 0;

			if (comObj is Any kso && Script.TryGetPropertyValue(out object propPtr, comObj, "ptr"))
				comObj = propPtr;

			if (Marshal.IsComObject(comObj))
			{
				pUnk = Marshal.GetIUnknownForObject(comObj);
				_ = Marshal.Release(pUnk);
			}
			else if (comObj is long l)
				pUnk = new nint(l);
			else
				return Errors.ValueErrorOccurred($"The passed in object was not a ComObject or a raw COM interface.");

			var pVtbl = Marshal.ReadIntPtr(pUnk);
			var helper = new ComArgumentHelper(parameters);
			var value = NativeInvoke(pUnk.ToInt64(), Marshal.ReadIntPtr(nint.Add(pVtbl, idx * sizeof(nint))), helper.args, helper.floatingTypeMask);
			Dll.FixParamTypesAndCopyBack(parameters, helper);
			var result = helper.ConvertReturnValue(value);
			helper.Dispose();
			return result;
		}

		internal static object NativeInvoke(long objPtr, nint vtbl, long[] args, ulong mask)
		{
			if (objPtr == 0L || vtbl == 0)
				throw new Error("Invalid object pointer or vtable number");

			object ret = 0L;
			//First attempt to call the normal way. This will succeed with any normal COM call.
			//However, it will throw an exception if we've passed a fake COM function using DelegateHolder.
			//This can be reproduced with the following Script.TheScript.
			/*
			    ReturnInt() => 123

			    ; Create dummy vtable without a defined AddRef, Release etc
			    vtbl := Buffer(4*A_PtrSize)
			    NumPut("ptr", CallbackCreate(ReturnInt), vtbl, 3*A_PtrSize)
			    ; Add the vtbl to our COM object
			    dummyCOM := Buffer(A_PtrSize, 0)
			    NumPut("ptr", vtbl.Ptr, dummyCOM)

			    MsgBox ComCall(3, dummyCOM.Ptr, "int")
			*/
			// This could potentially be optimized by compiling a specific delegate
			// for the ComCall scenario with the signature Func<nint, long, long[], long>
			long[] newArgs = new long[args.Length + 1];
			newArgs[0] = objPtr;
			System.Array.Copy(args, 0, newArgs, 1, args.Length);
			mask = mask << 1; // since we inserted an extra argument at the beginning
			ret = Dll.NativeInvoke(vtbl, newArgs, mask);
			// Copy back.
			System.Array.Copy(newArgs, 1, args, 0, args.Length);
			return ret;
		}

		[LibraryImport(WindowsAPI.ole32, EntryPoint = "CLSIDFromProgIDEx", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int CLSIDFromProgIDEx(string lpszProgID, out Guid clsid);

		[LibraryImport(WindowsAPI.ole32, EntryPoint = "CLSIDFromString", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int CLSIDFromString(string lpsz, out Guid guid);

		/// <summary>
		/// This used to be a built in function in earlier versions of .NET but now needs to be added manually.
		/// Gotten from: https://stackoverflow.com/questions/64823199/is-there-a-substitue-for-system-runtime-interopservices-marshal-getactiveobject
		/// </summary>
		/// <param name="progId"></param>
		/// <param name="throwOnError"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentNullException"></exception>
		internal static object GetActiveObject(string progId)
		{
			if (!Guid.TryParse(progId, out var clsid))
				_ = CLSIDFromProgIDEx(progId, out clsid);

			GetActiveObject(ref clsid, 0, out var pUnk);
			if (Marshal.QueryInterface(pUnk, in IID_IDispatch, out nint pDisp) == 0)
			{
				Marshal.Release(pUnk);
				return new ComObject(9L, (long)pDisp);
			}
			return new ComValue(13L, (long)pUnk);
		}

		[DllImport(WindowsAPI.oleaut, CharSet = CharSet.Unicode, PreserveSig = false)]
		internal static extern void GetActiveObject(ref Guid rclsid, nint pvReserved, out nint ppunk);
	}
}
#endif
