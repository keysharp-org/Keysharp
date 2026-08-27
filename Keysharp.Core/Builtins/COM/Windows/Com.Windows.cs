#if WINDOWS
namespace Keysharp.Builtins.COM
{
	internal class ComMethodData(Script script) : IDisposable
	{
		private readonly Lock comEventGate = new();
		private readonly HashSet<ComEvent> comEvents = [];
		private bool disposed;
		private ConcurrentLfu<nint, Dictionary<string, ComMethodInfo>> methodCache;

		/// <summary>
		/// Type info for COM members, keyed by interface pointer. Built on the first COM member call rather than
		/// with the Script: it measures ~32KB, and most scripts never touch COM.
		/// </summary>
		internal ConcurrentLfu<nint, Dictionary<string, ComMethodInfo>> MethodCache
		{
			get
			{
				var current = methodCache;

				if (current == null)
				{
					current = new(Caching.DefaultCacheCapacity);
					current = Interlocked.CompareExchange(ref methodCache, current, null) ?? current;
				}

				return current;
			}
		}

		/// <summary>Drops a released interface pointer's entry. Used from ComValue's dispose path, so it must not
		/// build the cache just to find it empty.</summary>
		internal void ForgetMethods(nint ptr) => methodCache?.TryRemove(ptr);

		internal void Connect(ComObject comObject, object sink, bool log)
		{
			ComEvent replacement = null;

			if (sink != null)
			{
				var dispatcher = new Dispatcher(comObject);

				try
				{
					replacement = new ComEvent(script, dispatcher, sink, log);
				}
				catch
				{
					dispatcher.Dispose();
					throw;
				}
			}

			ComEvent existing;
			var rejected = false;

			lock (comEventGate)
			{
				if (disposed || script.IsDisposed)
				{
					existing = null;
					rejected = true;
				}
				else
				{
					existing = comEvents.FirstOrDefault(ce => ReferenceEquals(ce.dispatcher.Co, comObject));

					if (existing != null)
						_ = comEvents.Remove(existing);

					if (replacement != null)
						_ = comEvents.Add(replacement);
				}
			}

			if (existing != null)
			{
				existing.Unwire();
				existing.dispatcher.Dispose();
			}

			if (rejected)
			{
				if (replacement != null)
				{
					replacement.Unwire();
					replacement.dispatcher.Dispose();
				}

				throw new ObjectDisposedException(nameof(Script));
			}
		}

		public void Dispose()
		{
			ComEvent[] all;

			lock (comEventGate)
			{
				if (disposed)
					return;

				disposed = true;
				all = [.. comEvents];
				comEvents.Clear();
			}

			// Unadvise can enter arbitrary COM code, so it must not run while the registry gate is held.
			foreach (var comEvent in all)
			{
				comEvent.Unwire();
				comEvent.dispatcher.Dispose();
			}
		}
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
		internal const int DISPID_PROPERTYPUT = -3;

		[DllImport(WindowsAPI.ole32, CharSet = CharSet.Unicode)]
		internal static extern int CoCreateInstance(ref Guid clsid,
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
			var script = Script.TheScript;

			if (comObj is ComObject co)
			{
				if (co.vt != VarEnum.VT_DISPATCH && co.vt != VarEnum.VT_UNKNOWN)// || Marshal.GetIUnknownForObject(co.Ptr) == 0)
					return Errors.ValueErrorOccurred($"COM object type of {co.vt} was not VT_DISPATCH or VT_UNKNOWN, and was not IUnknown.");

				script.ComMethodData.Connect(co, prefixOrSink, debug != null ? debug.Ab() : false);

				return DefaultObject;
			}

			return Errors.TypeErrorOccurred(comObj, typeof(ComObject));
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
			if (Reflections.TryGetPtrProperty(dispPtr, out var ptr))
				return new ComObject(VarEnum.VT_DISPATCH, ptr);

			return Errors.TypeErrorOccurred(dispPtr, typeof(IDispatch), DefaultObject);
		}

		public static object ComObjGet(object name)
		{
			var com = Marshal.BindToMoniker(name.As());
			if (com is IDispatch id)
			{
				var ptr = Marshal.GetIDispatchForObject(id);
				return new ComObject()
				{
					vt = VarEnum.VT_DISPATCH,
					Ptr = ptr
				};
			}
			else if (Marshal.IsComObject(com))
			{
				var ptr = Marshal.GetIUnknownForObject(com);
				return new ComValue()
				{
					vt = VarEnum.VT_UNKNOWN,
					Ptr = ptr
				};
			}
			return Errors.ErrorOccurred("Unknown COM object type");
		}

		public static object ComObjQuery(object comObj, object sid = null, object iid = null)
		{
			nint ptr;

			if (comObj is Any kso && Script.GetPropertyValueOrNull(kso, "ptr") is object kptr)
				comObj = kptr;

			if (comObj is long l)
				ptr = new nint(l);
			else
				return Errors.ValueErrorOccurred($"The passed in object {comObj} of type {comObj.GetType()} was not a ComObject or a raw COM interface.");

			nint resultPtr = 0;
			Guid id = Guid.Empty;
			int hr = 0;

			if (sid != null && iid != null)
			{
				var sidstr = sid.As();
				var iidstr = iid.As();

				if (CLSIDFromString(sidstr, out var sidGuid) >= 0 && CLSIDFromString(iidstr, out id) >= 0)
				{
					// Query for a service: use IServiceProvider::QueryService.
					IServiceProvider sp = (IServiceProvider)Marshal.GetObjectForIUnknown(ptr);
					hr = sp.QueryService(ref sidGuid, ref id, out resultPtr);
				}
			}
			else if (sid != null)
			{
				var iidstr = sid.As();

				if (CLSIDFromString(iidstr, out id) >= 0)
				{
					hr = Marshal.QueryInterface(ptr, id, out resultPtr);
				}
			}

			if (hr < 0)
				return Errors.OSErrorOccurredForHR(hr);

			if (resultPtr == 0)
				return Errors.ErrorOccurred($"Unable to get COM interface with arguments {sid}, {iid}.");

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

			Reflections.TryGetPtrProperty(comObj, out var comObjAddr);
			var pUnk = new nint(comObjAddr);

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
				return Errors.TypeErrorOccurred(ptr, typeof(ComValue), DefaultObject);

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

			if (comObj is Any kso && Script.GetPropertyValueOrNull(comObj, "ptr") is object propPtr)
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

			var argCount = parameters.Length / 2;

			if (argCount >= NativeInvoker.MaxArguments)//The object pointer takes a slot of its own ahead of them.
				return Errors.ValueErrorOccurred($"A ComCall cannot take more than {NativeInvoker.MaxArguments - 1} arguments.");

			//Checked before the dereference below rather than after it, which is where the equivalent guard
			//used to sit -- reading the vtable out of a null pointer faults before any check can report it.
			if (pUnk == 0)
				throw new Error("Invalid object pointer or vtable number");

			var vtbl = Marshal.ReadIntPtr(nint.Add(Marshal.ReadIntPtr(pUnk), idx * sizeof(nint)));

			if (vtbl == 0)
				throw new Error("Invalid object pointer or vtable number");

			//A COM method takes the object pointer as its first argument, so the list is one longer than the
			//script's arguments and is built with that slot already in front: the helper fills the tail in
			//place, which is why nothing here has to be copied into or out of a second buffer.
			Span<long> args = stackalloc long[argCount + 1];
			Span<ArgumentSlot> slots = stackalloc ArgumentSlot[argCount];
			using var helper = new ArgumentHelper(parameters, args[1..], slots, isCom: true);

			if (helper.Failed)//The error was reported and suppressed; the argument list is incomplete.
				return DefaultObject;

			args[0] = pUnk;
			//Called through the same generated invoker DllCall uses rather than anything COM-aware, because the
			//vtable entry need not belong to a real COM object at all: a script can build one out of Buffers and
			//a CallbackCreate thunk, with no AddRef or Release behind it, and ComCall must still work.
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
			//Every argument sits one slot further along than the helper numbered it, and so does the return
			//value, so the whole mask shifts with them.
			var value = NativeInvoker.NativeInvoke(vtbl, args, helper.floatingTypeMask << 1);
			helper.CopyBack(parameters);
			var result = helper.ConvertReturnValue(value);
			return result;
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
