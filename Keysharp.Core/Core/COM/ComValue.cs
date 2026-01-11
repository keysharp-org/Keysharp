#if WINDOWS
namespace Keysharp.Core
{
	public unsafe class ComValue : Any, IDisposable, IMetaObject
	{
		internal static readonly long F_OWNVALUE = 1;
		internal static readonly int MaxVtableLen = 16;
		internal List<IFuncObj> handlers = [];
		internal object item;

		private nint NintPtr => Ptr switch { long lp => (nint)lp, nint ip => ip, _ => 0 };
		public object Ptr
		{
			get => item;
			set
			{
				// BYREF / ARRAY always store a raw pointer (or 0)
				if ((vt & VarEnum.VT_BYREF) != 0 || (vt & VarEnum.VT_ARRAY) != 0)
				{
					item = value switch { long lp => lp, nint ip => (long)ip, _ => 0L };
					return;
				}

				switch (vt)
				{
					case VarEnum.VT_EMPTY:
					case VarEnum.VT_NULL:
						item = null;
						return;

					// store as canonical long for integer vts
					case VarEnum.VT_I1:
					case VarEnum.VT_UI1:
					case VarEnum.VT_I2:
					case VarEnum.VT_UI2:
					case VarEnum.VT_I4:
					case VarEnum.VT_UI4:
					case VarEnum.VT_I8:
					case VarEnum.VT_UI8:
					case VarEnum.VT_INT:
					case VarEnum.VT_UINT:
					case VarEnum.VT_ERROR:
						item = value is long l ? l : Convert.ToInt64(value);
						return;

					// store as double for floats and dates
					case VarEnum.VT_R4:
					case VarEnum.VT_R8:
					case VarEnum.VT_DATE:
						item = value is double d ? d : Convert.ToDouble(value);
						return;

					case VarEnum.VT_CY:
						// keep 64-bit *scaled* integer in tenthousandths if caller already passed one,
						// else compute from double/decimal
						if (value is long cy) item = cy;
						else if (value is double dcy) item = checked((long)Math.Round(dcy * 10000.0));
						else if (value is decimal mcy) item = checked((long)Math.Round(mcy * 10000m));
						else item = checked((long)Math.Round(Convert.ToDouble(value) * 10000.0));
						return;

					case VarEnum.VT_BOOL:
						item = value is bool b ? b : (value is long lb ? lb != 0 : Convert.ToBoolean(value));
						return;

					case VarEnum.VT_BSTR:
						// allow either managed string or a raw BSTR pointer (long/nint)
						if (value is string s)
						{
							item = Marshal.StringToBSTR(s);
							Flags |= F_OWNVALUE;
						}
						else if (value is long lp) item = lp;
						else if (value is nint ip) item = (long)ip;
						else
						{
							string str = Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
							item = Marshal.StringToBSTR(str);
							Flags |= F_OWNVALUE;
						}
						return;

					case VarEnum.VT_DISPATCH:
					case VarEnum.VT_UNKNOWN:
						// store raw interface pointer if given, otherwise create one
						if (value is long ilp) item = ilp;
						else if (value is nint iip) item = (long)iip;
						else if (value is null) item = 0L;
						else
						{
							nint p = vt == VarEnum.VT_DISPATCH
								? Marshal.GetIDispatchForObject(value)
								: Marshal.GetIUnknownForObject(value);
							item = (long)p;
						}
						return;

					case VarEnum.VT_VARIANT:
						// ambiguous to store — keep value as-is; ToVariant() delegates to VariantHelper
						item = value;
						return;

					default:
						// safest fallback
						item = value;
						return;
				}
			}
		}


		public VarEnum vt;
		public long VarType
		{
			get => (long)vt;
			set => vt = (VarEnum)value.Ai();
		}
		internal long Flags { get; set; }

		public ComValue(params object[] args) : base(args) { }

		internal ComValue(object varType, object value, object flags = null) : base(varType, value, flags) { }

		public static object Call(object @this, object varType, object value, object flags = null)
		{
			var vt = (VarEnum)varType.Al();
			if ((vt & VarEnum.VT_ARRAY) != 0)
			{
				nint psa = (nint)Reflections.GetPtrProperty(value);
				return new ComObjArray(vt & ~VarEnum.VT_ARRAY, psa, flags.Ab());
			}
			return vt == VarEnum.VT_DISPATCH ? new ComObject(varType, value, flags) : new ComValue(varType, value, flags);
		}

		public override object __New(params object[] args)
		{
			if (args.Length == 0 || args[0] == null) return DefaultObject;
			var value = args[1];
			vt = (VarEnum)args[0].Al();
			Ptr = value;
			var flags = args.Length > 2 ? args[2] : null;
			Flags = flags != null ? flags.Al() : 0L;

			if (this.vt == VarEnum.VT_BSTR && value is not long)
				Flags |= F_OWNVALUE;
			return DefaultObject;
		}

		object IMetaObject.Get(string name, object[] args) => RawGetProperty(name, args);

		void IMetaObject.Set(string name, object[] args, object value) => RawSetProperty(name, args, value);

		object IMetaObject.Call(string name, object[] args) => RawInvokeMethod(name, args);

		object IMetaObject.get_Item(object[] indexArgs) => get_Item(indexArgs);
		void IMetaObject.set_Item(object[] indexArgs, object value) => set_Item(indexArgs, value);

		public object get_Item(params object[] args)
		{
			if (args.Length == 0 && (vt & VarEnum.VT_BYREF) != 0)
				return VariantHelper.ReadVariant(Ptr.Al(), vt);

			return RawInvokeMethod("Item", args);
		}
		public object set_Item(object[] args, object value)
		{
			if (args.Length == 0 && (vt & VarEnum.VT_BYREF) != 0)
				VariantHelper.WriteVariant(Ptr.Al(), vt, value);
			else
			{
				RawSetProperty("Item", args, value);
			}
			return value;
		}

		public virtual void Dispose()
		{
			if (Ptr == null) return;

			nint ip = NintPtr;
			if (ip != 0)
			{
				if (vt == VarEnum.VT_UNKNOWN || vt == VarEnum.VT_DISPATCH)
				{
					_ = Marshal.Release(ip);
				}
				else if (vt == VarEnum.VT_BSTR && (Flags & F_OWNVALUE) != 0)
				{
					WindowsAPI.SysFreeString(ip);
				}
				TheScript.ComMethodData.comMethodCache.TryRemove(NintPtr);
			}

			Ptr = null;
			HasFinalizer = false;
		}

		internal void CallEvents()
		{
			var result = handlers?.InvokeEventHandlers(this);
		}

		internal void Clear()
		{
			vt = 0;
			Ptr = null;
			Flags = 0L;
		}

		internal VARIANT ToVariant(bool copy = false)
		{
			var vtype = vt;
			var v = new VARIANT { vt = (ushort)vtype };

			// ---- BYREF: pass-through pointer to storage (no allocations here) ----
			if ((vtype & VarEnum.VT_BYREF) != 0)
			{
				VarEnum baseVt = vtype & ~VarEnum.VT_BYREF;
				v.ptrVal = NintPtr;
				return v;
			}

			// ---- Arrays by value: SAFEARRAY* goes in parray ----
			if ((vtype & VarEnum.VT_ARRAY) != 0)
			{
				v.ptrVal = NintPtr; // pass-through
				if (copy && v.ptrVal != 0 &&
					OleAuto.SafeArrayCopy(v.ptrVal, out var dst) >= 0 && dst != 0)
				{
					v.ptrVal = dst;              // our copy, VariantClear will destroy it
				}
				return v;
			}

			// ---- Scalars and interface/bstr cases by value ----
			switch (vtype)
			{
				case VarEnum.VT_EMPTY:
				case VarEnum.VT_NULL:
					return v;

				case VarEnum.VT_BOOL:
					// VARIANT_BOOL is a 16-bit short: -1 (TRUE), 0 (FALSE)
					if (Ptr is bool bl) v.boolVal = (short)(bl ? -1 : 0);
					else if (Ptr is long ll) v.boolVal = (short)((ll != 0) ? -1 : 0);
					else v.boolVal = (short)(Ptr.Ab() ? -1 : 0);
					return v;

				case VarEnum.VT_BSTR:
					if (Ptr is string s) v.ptrVal = Marshal.StringToBSTR(s);
					else if (copy && NintPtr is nint pb && pb != 0)
					{
						int len = OleAuto.SysStringLen(pb);
						v.ptrVal = OleAuto.SysAllocStringLen(pb, len); // duplicate
					}
					else v.ptrVal = NintPtr;
					return v;

				// Signed integers
				case VarEnum.VT_I1: v.cVal = (sbyte)(Ptr is long l1 ? l1 : Convert.ToSByte(Ptr)); return v;
				case VarEnum.VT_I2: v.iVal = (short)(Ptr is long l2 ? l2 : Convert.ToInt16(Ptr)); return v;
				case VarEnum.VT_I4:
				case VarEnum.VT_INT: v.lVal = (int)(Ptr is long l4 ? l4 : Convert.ToInt32(Ptr)); return v;
				case VarEnum.VT_I8: v.llVal = (Ptr is long l8 ? l8 : Convert.ToInt64(Ptr)); return v;

				// Unsigned integers
				case VarEnum.VT_UI1: v.bVal = (byte)(Ptr is long ul1 ? ul1 : Convert.ToByte(Ptr)); return v;
				case VarEnum.VT_UI2: v.uiVal = (ushort)(Ptr is long ul2 ? ul2 : Convert.ToUInt16(Ptr)); return v;
				case VarEnum.VT_UI4:
				case VarEnum.VT_UINT: v.ulVal = (uint)(Ptr is long ul4 ? ul4 : Convert.ToUInt32(Ptr)); return v;
				case VarEnum.VT_UI8: v.ullVal = (Ptr is ulong u8 ? u8 : (ulong)Convert.ToInt64(Ptr)); return v;

				// Floating-point
				case VarEnum.VT_R4: v.fltVal = (Ptr is float f ? f : Convert.ToSingle(Ptr)); return v;
				case VarEnum.VT_R8: v.dblVal = (Ptr is double d ? d : Convert.ToDouble(Ptr)); return v;

				// Currency: 64-bit integer in 1/10,000th units
				case VarEnum.VT_CY:
					if (Ptr is long cy) v.cyVal = cy;
					else if (Ptr is double dcy) v.cyVal = checked((long)Math.Round(dcy * 10000.0));
					else if (Ptr is decimal mcy) v.cyVal = checked((long)Math.Round(mcy * 10000m));
					else v.cyVal = checked((long)Math.Round(Convert.ToDouble(Ptr) * 10000.0));
					return v;

				// DATE: OLE Automation date (stored as double)
				case VarEnum.VT_DATE:
					if (Ptr is double dd) v.dblVal = dd;
					else if (Ptr is DateTime dt) v.dblVal = dt.ToOADate();
					else v.dblVal = Convert.ToDateTime(Ptr).ToOADate();
					return v;

				// Interfaces
				case VarEnum.VT_DISPATCH:
				case VarEnum.VT_UNKNOWN:
					if (NintPtr is nint p && p != 0)
					{
						if (copy) Marshal.AddRef(p); // own one ref so VariantClear can Release it
						v.ptrVal = p;
					}
					else if (Ptr != null)
						v.ptrVal = (vtype == VarEnum.VT_DISPATCH)
							? Marshal.GetIDispatchForObject(Ptr) // our ref, VariantClear will Release
							: Marshal.GetIUnknownForObject(Ptr);
					return v;

				// Avoid producing a by-value VT_VARIANT; coerce from the runtime value instead.
				case VarEnum.VT_VARIANT:
					return VariantHelper.ValueToVariant(Ptr);

				default:
					// Fallback to general converter to avoid silent mis-encoding.
					return VariantHelper.ValueToVariant(Ptr);
			}
		}

		internal unsafe object RawInvokeMethod(string methodName, object[] inputParameters)
		{
			Type[] expectedTypes = null;
			ParameterModifier[] modifiers = null;
			INVOKEKIND invokeKind = INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET;
			Dictionary<int, object> refs = new();
			object result = null;

			// Get DISPID
			int hr = RawGetIDsOfNames(methodName, out int dispId);
			if (hr < 0)
			{
				return Errors.ErrorOccurred($"Method '{methodName}' not found (HRESULT: 0x{hr:X8})");
			}

			if (inputParameters.Length > 0)
			{
				var pm = new ParameterModifier(inputParameters.Length);
				for (int i = 0; i < inputParameters.Length; i++)
				{
					if (inputParameters[i] is KeysharpObject kso && Script.TryGetPropertyValue(out object refval, kso, "__Value"))
					{
						pm[i] = true;
						refs[i] = kso; // remember for write-back
						inputParameters[i] = refval; // unwrap to the current value for packing
					}
				}
				modifiers = new[] { pm };
			}

			hr = RawInvoke(dispId, INVOKEKIND.INVOKE_FUNC, inputParameters, out result, expectedTypes: null, modifiers: modifiers);
			if (hr == unchecked((int)0x80020003) /*DISP_E_MEMBERNOTFOUND*/)
				hr = RawInvoke(dispId, INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET, inputParameters, out result, expectedTypes: null, modifiers: modifiers);

			// SLOW PATH (only on conversion-ish failures): query type info and retry.
			if (hr == unchecked((int)0x80020005) /*DISP_E_TYPEMISMATCH*/ ||
				hr == unchecked((int)0x80020008) /*DISP_E_BADVARTYPE*/   ||
				hr == unchecked((int)0x80020004) /*DISP_E_PARAMNOTFOUND*/||
				hr == unchecked((int)0x8002000A) /*DISP_E_OVERFLOW*/)
			{
				// First restore the parameter list
				foreach (var kvp in refs)
					inputParameters[kvp.Key] = kvp.Value;

				// If no cached type info, try to query it
				modifiers = null; refs = new Dictionary<int, object>(); result = null;
				TryGetTypeInfo(dispId, methodName, inputParameters?.Length ?? 0, out expectedTypes, out modifiers, out invokeKind, invokeKind);

				// Handle byref parameters with KeysharpObject wrapper
				if (modifiers != null && inputParameters != null)
				{
					for (int i = 0; i < inputParameters.Length; i++)
					{
						if (modifiers[0][i] && inputParameters[i] is KeysharpObject kso)
						{
							refs[i] = kso;
							inputParameters[i] = Script.GetPropertyValue(kso, "__Value");
						}
					}
				}

				// Invoke
				hr = RawInvoke(dispId, invokeKind, inputParameters, out result, expectedTypes, modifiers);
			}


			if (hr >= 0)
			{
				// Update byref parameters
				foreach (var kvp in refs)
				{
					Script.SetPropertyValue(kvp.Value, "__Value", inputParameters[kvp.Key]);
				}
			}

			return hr >= 0 ? result : Errors.ErrorOccurred($"Invoke failed for '{methodName}' ({result})", DefaultErrorObject);
		}

		internal unsafe object RawGetProperty(string propertyName, object[] args)
		{
			object result = null;
			int hr = RawGetIDsOfNames(propertyName, out int dispId);
			if (hr < 0)
				return Errors.ErrorOccurred($"Property '{propertyName}' not found");

			TryGetTypeInfo(dispId, propertyName, args.Length, out var expectedTypes, out var modifiers, out var invokeKind, INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET);

			if (expectedTypes != null && expectedTypes.Length > 0)
				hr = RawInvoke(dispId, invokeKind, args, out result);
			else
			{
				hr = RawInvoke(dispId, invokeKind, null, out result);
				if (hr >= 0 && args.Length > 0)
					return Index(this, args);
			}
			if (hr < 0)
				return Errors.ErrorOccurred($"Get property failed for '{propertyName}' ({result})");

			return result;
		}

		internal unsafe void RawSetProperty(string propertyName, object[] args, object value)
		{
			int hr = RawGetIDsOfNames(propertyName, out int dispId);
			if (hr < 0)
				throw new COMException($"Property '{propertyName}' not found");

			if (!TryGetTypeInfo(dispId, propertyName, args.Length, out var expectedTypes, out var modifiers, out var invokeKind, INVOKEKIND.INVOKE_PROPERTYPUT | INVOKEKIND.INVOKE_PROPERTYPUTREF))
				invokeKind = INVOKEKIND.INVOKE_PROPERTYPUT;

			if (args.Length > 0)
			{
				if (value is ComValue cv && (cv.vt == VarEnum.VT_DISPATCH || cv.vt == VarEnum.VT_UNKNOWN))
					invokeKind = INVOKEKIND.INVOKE_PROPERTYPUTREF;
			}

			hr = RawInvoke(dispId, invokeKind, [.. args, value], out object result, expectedTypes, modifiers);
			if (hr < 0)
				_ = Errors.ErrorOccurred($"Set property failed for '{propertyName}' ({result})");
		}

#pragma warning disable 0649 // fields assigned by native vtables
		internal unsafe struct IDispatchVtbl
		{
			// IUnknown methods (offsets 0-2)
			public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> QueryInterface;
			public delegate* unmanaged[Stdcall]<nint, uint> AddRef;
			public delegate* unmanaged[Stdcall]<nint, uint> Release;

			// IDispatch methods (offsets 3-6)
			public delegate* unmanaged[Stdcall]<nint, uint*, int> GetTypeInfoCount;
			public delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int> GetTypeInfo;
			public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint, uint, int*, int> GetIDsOfNames;
			public delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, nint, nint, nint, nint, int> Invoke;
		}

		internal unsafe IDispatchVtbl* GetDispatchVtbl()
		{
			nint ptr = 0;

			if (Ptr is long l)
				ptr = new nint(l);

			if (ptr == 0)
				return null;

			// First pointer is the vtable
			nint vtablePtr = *(nint*)ptr;
			return (IDispatchVtbl*)vtablePtr;
		}

		internal unsafe struct IDispatchExVtbl
		{
			// IDispatch (first 7)
			public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> QueryInterface;
			public delegate* unmanaged[Stdcall]<nint, uint> AddRef;
			public delegate* unmanaged[Stdcall]<nint, uint> Release;
			public delegate* unmanaged[Stdcall]<nint, uint*, int> GetTypeInfoCount;
			public delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int> GetTypeInfo;
			public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint, uint, int*, int> GetIDsOfNames;
			public delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, nint, nint, nint, nint, int> Invoke;

			// IDispatchEx extras
			public delegate* unmanaged[Stdcall]<nint, nint /*BSTR*/, uint /*grfdex*/,
				int* /*pid*/, int> GetDispID;
			public delegate* unmanaged[Stdcall]<nint, int /*id*/, Guid* /*riid*/,
				uint /*lcid*/, ushort /*flags*/, nint /*pdp*/, nint /*pvarRes*/,
				nint /*pei*/, nint /*pspCaller*/, int> InvokeEx;
			public delegate* unmanaged[Stdcall]<nint, int /*id*/, Guid* /*riid*/,
				nint* /*ppv*/, int> DeleteMemberByName;
			public delegate* unmanaged[Stdcall]<nint, int /*id*/, int> DeleteMemberByDispID;
			public delegate* unmanaged[Stdcall]<nint, int /*id*/, uint /*grfdexFetch*/,
				nint /*pName*/, uint* /*pgrfdex*/, int> GetMemberProperties;
			public delegate* unmanaged[Stdcall]<nint, int /*id*/, uint /*grfdex*/,
				nint /*pName*/, int* /*pid*/, int> GetMemberName;
			public delegate* unmanaged[Stdcall]<nint, uint /*grfdex*/, int /*id*/,
				int /*pdidNext*/, int*> GetNextDispID;
			public delegate* unmanaged[Stdcall]<nint, nint /*ptsi*/, nint /*pbNamingContainer*/,
				int> GetNameSpaceParent;
		}
#pragma warning restore 0649

		internal static readonly Guid IID_IDispatchEx = new("A6EF9860-C720-11D0-9337-00A0C90DCAA9");

		// flags for GetDispID (grfdex)
		internal const uint FDEX_NAME_CASE_INSENSITIVE = 0x00000001;
		internal const uint FDEX_NAME_IMMEDIATE = 0x00000002;
		internal const uint FDEX_NAME_DYNAMIC = 0x00000004;
		internal const uint FDEX_NAME_ENSURE = 0x00000002; // often used synonymously with IMMEDIATE

		internal unsafe int RawGetIDsOfNames(string name, out int dispId)
		{
			dispId = 0;
			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return -1;

			nint ptr = new nint((long)Ptr);

			Guid iidNull = Guid.Empty;
			nint strPtrName = Marshal.StringToCoTaskMemUni(name);
			nint bstrName = 0;

			try
			{
				fixed (int* dispIdPtr = &dispId)
				{
					// Call IDispatch::GetIDsOfNames
					int hr = vtbl->GetIDsOfNames(
						ptr,
						&iidNull,
						&strPtrName,
						1,
						Com.LOCALE_USER_DEFAULT,
						dispIdPtr);

					// Fallback to IDispatchEx::GetDispID
					const int DISP_E_UNKNOWNNAME = unchecked((int)0x80020006);
					const int DISP_E_MEMBERNOTFOUND = unchecked((int)0x80020003);

					if (hr == DISP_E_UNKNOWNNAME || hr == DISP_E_MEMBERNOTFOUND)
					{
						// QI for IDispatchEx and call GetDispID on that *interface pointer*
						nint pEx;
						var ex = TryGetDispatchExVtbl(out pEx);
						if (ex != null)
						{
							try
							{
								bstrName = Marshal.StringToBSTR(name);
								int id = 0;
								int hr2 = ex->GetDispID(pEx, bstrName, FDEX_NAME_CASE_INSENSITIVE | FDEX_NAME_IMMEDIATE, &id);
								if (hr2 >= 0) { dispId = id; return 0; }
							}
							finally
							{
								if (pEx != 0) Marshal.Release(pEx);
							}
						}
					}
					return hr;
				}
			}
			finally
			{
				if (strPtrName != 0) Marshal.FreeCoTaskMem(strPtrName);
				if (bstrName != 0) WindowsAPI.SysFreeString(bstrName);
			}
		}

		internal unsafe int RawInvoke(
			int dispId,
			INVOKEKIND flags,
			object[] args,
			out object result,
			Type[] expectedTypes = null,
			ParameterModifier[] modifiers = null)
		{
			result = null;
			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return -1;

			nint ptr = Ptr is nint ip ? ip : new nint((long)Ptr);
			Guid iidNull = Guid.Empty;

			bool wantsResult = (flags & (INVOKEKIND.INVOKE_PROPERTYGET | INVOKEKIND.INVOKE_FUNC)) != 0
				   && (flags & (INVOKEKIND.INVOKE_PROPERTYPUT | INVOKEKIND.INVOKE_PROPERTYPUTREF)) == 0;

			nint pArgs = 0;
			nint pDispParams = Marshal.AllocHGlobal(Marshal.SizeOf<DISPPARAMS>());
			nint pResult = wantsResult ? Marshal.AllocHGlobal(Marshal.SizeOf<VARIANT>()) : 0;
			nint pExcepInfo = Marshal.AllocHGlobal(Marshal.SizeOf<EXCEPINFO>());
			nint pArgErr = Marshal.AllocHGlobal(sizeof(uint));
			nint pNamed = 0;

			int argCount = args?.Length ?? 0;
			var allocatedByRef = argCount > 0 ? new bool[argCount] : [];   // we allocated temp BYREF storage?
			var suppressWriteback = argCount > 0 ? new bool[argCount] : []; // skip writeback for ComValue BYREFs

			try
			{
				var dispParams = new DISPPARAMS();

				// Convert arguments to VARIANTs (in reverse order for IDispatch)
				if (argCount > 0)
				{
					pArgs = Marshal.AllocHGlobal(argCount * Marshal.SizeOf<VARIANT>());

					for (int i = 0; i < argCount; i++)
					{
						// IDispatch expects arguments in reverse order
						int sourceIndex = argCount - 1 - i;
						var arg = args[sourceIndex];

						// Apply type conversion if we have type info
						if (expectedTypes != null && sourceIndex < expectedTypes.Length && arg is not ComValue)
							arg = ConvertArgumentToExpectedType(arg, expectedTypes[sourceIndex]);

						// Check if this is a byref parameter
						bool isByRef = modifiers != null &&
									  modifiers.Length > 0 &&
									  sourceIndex < argCount &&
									  modifiers[0][sourceIndex];

						nint variantPtr = pArgs + (i * Marshal.SizeOf<VARIANT>());
						VARIANT variant;

						if (arg is ComValue cv)
						{
							// If signature says BYREF, or cv is already BYREF, just pass through (no ownership)
							if (isByRef || (cv.vt & VarEnum.VT_BYREF) != 0)
							{
								variant = cv.ToVariant();
								suppressWriteback[sourceIndex] = (cv.vt & VarEnum.VT_BYREF) != 0; // we're not supposed to overwrite the wrapper object
																								  // allocatedByRef[i] remains false
							}
							else
							{
								// Non-BYREF ComValue: duplicate “dangerous” inners (BSTR/SAFEARRAY/interface) so VariantClear is safe
								variant = cv.ToVariant(copy: true);
							}
						}
						else if (isByRef)
						{
							// If the param is BYREF and the TLB says SAFEARRAY (i.e. expected CLR type is array),
							// pass VARIANT* whose inner VARIANT = VT_ARRAY | <elemVT> (not SAFEARRAY**).
							if (expectedTypes != null &&
								sourceIndex < expectedTypes.Length &&
								expectedTypes[sourceIndex]?.IsArray == true)
							{
								variant = VariantHelper.CreateByRefVariant(arg);  // <- BYREF|VARIANT (nested)
							}
							else
							{
								VarEnum baseVt = VarEnum.VT_EMPTY;
								if (expectedTypes != null && sourceIndex < expectedTypes.Length)
									baseVt = VariantHelper.CLRTypeToVarEnum(expectedTypes[sourceIndex]);

								variant = (baseVt != VarEnum.VT_EMPTY)
										  ? VariantHelper.CreateByRefVariantTyped(arg, baseVt)
										  : VariantHelper.CreateByRefVariant(arg);
								
							}
							allocatedByRef[i] = true; // we own the temp buffer
						}
						else
						{
							variant = VariantHelper.ValueToVariant(arg);
						}
						Marshal.StructureToPtr(variant, variantPtr, false);
					}

					dispParams.rgvarg = pArgs;
					dispParams.cArgs = argCount;
				}

				dispParams.rgdispidNamedArgs = 0;
				dispParams.cNamedArgs = 0;

				// ---- SPECIAL: PROPERTYPUT / PROPERTYPUTREF ----
				bool isPut = (flags & INVOKEKIND.INVOKE_PROPERTYPUT) != 0;
				bool isPutRef = (flags & INVOKEKIND.INVOKE_PROPERTYPUTREF) != 0;

				if (isPut || isPutRef)
				{
					// Must provide one named arg: DISPID_PROPERTYPUT
					pNamed = Marshal.AllocHGlobal(sizeof(int));
					Marshal.WriteInt32(pNamed, Com.DISPID_PROPERTYPUT);
					dispParams.rgdispidNamedArgs = pNamed;
					dispParams.cNamedArgs = 1;

					// Also ensure rgvarg[0] is the VALUE, rgvarg[1..] are the indexers.
					// The code above already wrote args reversed (last source arg first).
					// For a set like dict.Item(key) = value, caller should pass args = [key, value].
					// Reversed becomes [value, key] which is exactly what IDispatch requires.
				}

				Marshal.StructureToPtr(dispParams, pDispParams, false);

				/*
				for (int i = 0; i < argCount; i++)
				{
					int src = argCount - 1 - i; // due to reverse packing
					VARIANT v = Marshal.PtrToStructure<VARIANT>(pArgs + i * Marshal.SizeOf<VARIANT>());
					System.Diagnostics.Debug.WriteLine($"arg#{src}: vt=0x{v.vt:X} {(VarEnum)v.vt}, ptr=0x{(long)v.llVal:X}");
					ComDebug.DumpVariant($"arg[{src}]", v);
				}
				*/

				// Call IDispatch::Invoke
				int hr = vtbl->Invoke(
					ptr,
					dispId,
					&iidNull,
					Com.LOCALE_USER_DEFAULT,
					(ushort)flags,
					pDispParams,
					pResult,
					pExcepInfo,
					pArgErr);

				if (hr >= 0)
				{
					if (wantsResult)
					{
						// Extract result
						var resultVariant = Marshal.PtrToStructure<VARIANT>(pResult);
						result = VariantHelper.VariantToValue(resultVariant);
					}

					// Handle byref out parameters
					if (modifiers != null && modifiers.Length > 0 && pArgs != 0)
					{
						for (int i = 0; i < argCount; i++)
						{
							int sourceIndex = argCount - 1 - i;
							if (sourceIndex < argCount && modifiers[0][sourceIndex])
							{
								nint variantPtr = pArgs + (i * Marshal.SizeOf<VARIANT>());
								var variant = Marshal.PtrToStructure<VARIANT>(variantPtr);

								// If it's VT_BYREF, read the value back
								if (((VarEnum)variant.vt & VarEnum.VT_BYREF) != 0 && !suppressWriteback[sourceIndex])
								{
									args[sourceIndex] = VariantHelper.ReadByRefVariant(variant);
								}
							}
						}
					}
				}
				else
				{
					const int DISP_E_EXCEPTION = -2147352567;
					if (hr != DISP_E_EXCEPTION)
					{
						result = $"HRESULT: 0x{hr:X8}";
					}
					else
					{
						// Handle exception info
						var excepInfo = Marshal.PtrToStructure<EXCEPINFO>(pExcepInfo);
						if (excepInfo.bstrDescription != null)
						{
							result = $"{excepInfo.bstrDescription}";
						}
						else
						{
							result = $"HRESULT: 0x{hr:X8}";
						}
					}
				}

				return hr;
			}
			finally
			{
				if (pArgs != 0)
				{
					// Clean up variants
					var dispParams = Marshal.PtrToStructure<DISPPARAMS>(pDispParams);
					for (int i = 0; i < dispParams.cArgs; i++)
					{
						int sourceIndex = dispParams.cArgs - 1 - i;
						nint variantPtr = pArgs + (i * Marshal.SizeOf<VARIANT>());
						var variant = Marshal.PtrToStructure<VARIANT>(variantPtr);

						// Free temp BYREF storage only if we allocated it
						if (((VarEnum)variant.vt & VarEnum.VT_BYREF) != 0 && allocatedByRef[i])
							VariantHelper.CleanupByRefVariant(variant);

						VariantHelper.VariantClear(variantPtr);
					}
					Marshal.FreeHGlobal(pArgs);
				}

				if (wantsResult && pResult != 0)
				{
					VariantHelper.VariantClear(pResult);
					Marshal.FreeHGlobal(pResult);
				}
				Marshal.FreeHGlobal(pDispParams);
				Marshal.FreeHGlobal(pExcepInfo);
				Marshal.FreeHGlobal(pArgErr);
				if (pNamed != 0)
					Marshal.FreeHGlobal(pNamed);
			}
		}

		private static object ConvertArgumentToExpectedType(object arg, Type expectedType)
		{
			if (arg == null)
				return expectedType == typeof(string) ? "" : null;

			var currentType = arg.GetType();
			if (currentType == expectedType)
				return arg;

			if (arg is ComValue cv)
			{
				if ((cv.vt & VarEnum.VT_BYREF) != 0)
					return arg;
				arg = VariantHelper.ReadVariant(cv.Ptr.Al(), cv.vt);
			}

			// SAFEARRAY parameters become CLR arrays in our surface types.
			if (expectedType.IsArray)
			{
				// Always normalize: scalar -> single-element array
				if (arg is not System.Array arr)
					arr = new object[] { arg };

				// If the expected element type is not object, coerce each element when possible.
				var elemClr = expectedType.GetElementType() ?? typeof(object);
				if (elemClr == typeof(object)) return arr; // leave heterogenous as-is (will become VT_ARRAY|VT_VARIANT)

				int n = arr.Length;
				var coerced = System.Array.CreateInstance(elemClr, n);
				for (int i = 0; i < n; i++)
				{
					var v = arr.GetValue(i);
					try
					{
						coerced.SetValue(ConvertArgumentToExpectedType(v, elemClr), i);
					}
					catch { coerced.SetValue(v, i); }
				}
				return coerced;
			}

			try
			{
				if (expectedType == typeof(string))
					return arg is long l ? l : arg.As();
				else if (expectedType == typeof(int))
					return arg.Ai();
				else if (expectedType == typeof(uint))
					return arg.Aui();
				else if (expectedType == typeof(long))
					return arg.Al();
				else if (expectedType == typeof(ulong))
					return (ulong)arg.Al();
				else if (expectedType == typeof(double))
					return arg.Ad();
				else if (expectedType == typeof(float))
					return (float)arg.Ad();
				else if (expectedType == typeof(short))
					return (short)arg.Al();
				else if (expectedType == typeof(ushort))
					return (ushort)arg.Aui();
				else if (expectedType == typeof(bool))
					return ForceBool(arg);
				else if (expectedType == typeof(sbyte))
					return (sbyte)arg.Ai();
				else if (expectedType == typeof(byte))
					return (byte)arg.Aui();
				else if (expectedType == typeof(object[]))
				{
					if (arg is ComObjArray 
						|| arg is System.Array) 
						return arg;

					// Plain managed array → normalize to object[]
					if (arg is Array a)
						return a.array.ToArray();

					return new object[] { arg };
				}
				else
					return Convert.ChangeType(arg, expectedType, CultureInfo.CurrentCulture);
			}
			catch
			{
				return arg; // Return original if conversion fails
			}
		}

		internal nint GetIUnknownPtr() => Ptr is nint ip ? ip : new nint((long)Ptr);

		private unsafe IDispatchExVtbl* TryGetDispatchExVtbl(out nint pEx)
		{
			pEx = 0;
			nint pUnk = GetIUnknownPtr();
			if (pUnk == 0) return null;

			int hr = Marshal.QueryInterface(pUnk, in IID_IDispatchEx, out pEx);
			if (hr < 0 || pEx == 0) return null;

			nint vt = *(nint*)pEx; // first pointer is vtable
			return (IDispatchExVtbl*)vt;
		}

		internal unsafe bool TryGetITypeInfo(out ITypeInfo typeInfo, uint index = 0)
		{
			typeInfo = null;
			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return false;
			nint ptr = new nint((long)Ptr);
			nint pTypeInfo = 0;
			if (vtbl->GetTypeInfo(ptr, index, Com.LOCALE_USER_DEFAULT, &pTypeInfo) == 0 && pTypeInfo != 0)
			{
				typeInfo = (ITypeInfo)Marshal.GetObjectForIUnknown(pTypeInfo);
				Marshal.Release(pTypeInfo);
				return true;
			}
			return false;
		}

		private static bool GetMatchingTypeInfo(ITypeInfo ti, int dispId, string methodName, INVOKEKIND preferredKinds,
			out Type[] expectedTypes,
			out ParameterModifier[] modifiers,
			out INVOKEKIND invokeKind)
		{
			expectedTypes = null; modifiers = null; invokeKind = 0;
			bool found = false;
			ti.GetTypeAttr(out var pTypeAttr);
			var typeAttr = Marshal.PtrToStructure<TYPEATTR>(pTypeAttr);
			try
			{
				for (int j = 0; j < typeAttr.cFuncs; j++)
				{
					ti.GetFuncDesc(j, out var pFuncDesc);
					var funcDesc = Marshal.PtrToStructure<FUNCDESC>(pFuncDesc);
					try
					{
						if (funcDesc.memid != dispId)
							continue;

						ti.GetDocumentation(funcDesc.memid, out var name, out _, out _, out _);
						if (!name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
						{
							if (found) return true; // assume that no additional overloads follow
							continue;
						}

						ExtractParameterInfo(funcDesc,
							out var candidateExpectedTypes,
							out var candidateModifiers,
							out var candidateInvokeKind);

						if (!found || (preferredKinds & candidateInvokeKind) != 0)
						{
							expectedTypes = candidateExpectedTypes;
							modifiers = candidateModifiers;
							invokeKind = candidateInvokeKind;
							if (found) return true; // immediate return condition
						}
						found = true;
					}
					finally
					{
						ti.ReleaseFuncDesc(pFuncDesc);
					}
				}
			}
			finally
			{
				ti.ReleaseTypeAttr(pTypeAttr);
			}
			return found;
		}

		private unsafe bool TryGetTypeInfo(
			int dispId,
			string methodName,
			int paramCount, // kept for signature compatibility (not used here)
			out Type[] expectedTypes,
			out ParameterModifier[] modifiers,
			out INVOKEKIND invokeKind,
			INVOKEKIND preferredKinds // 0 => accept first found
		)
		{
			// Try to get cached type info
			nint ptr = Ptr is nint ip ? ip : new nint((long)Ptr);
			if (Script.TheScript.ComMethodData.comMethodCache.TryGet(ptr, out var objDict))
			{
				if (objDict.TryGetValue($"{methodName}_{preferredKinds}", out var cmi))
				{
					expectedTypes = cmi.expectedTypes;
					modifiers = cmi.modifiers;
					invokeKind = cmi.invokeKind;
					return true;
				}
			}
			expectedTypes = null; modifiers = null; invokeKind = preferredKinds;

			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return false;

			// Acquire a root ITypeInfo
			nint pTypeInfo = 0;
			ITypeInfo typeInfo = null;

			if (vtbl->GetTypeInfo(ptr, 0, Com.LOCALE_USER_DEFAULT, &pTypeInfo) == 0 && pTypeInfo != 0)
			{
				typeInfo = (ITypeInfo)Marshal.GetObjectForIUnknown(pTypeInfo);
				Marshal.Release(pTypeInfo);
			}
			else
			{
				try
				{
					var comObject = Marshal.GetObjectForIUnknown(ptr);
					try
					{
						if (comObject is IProvideClassInfo ipci)
							_ = ipci.GetClassInfo(out typeInfo);
					}
					finally
					{
						if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
					}
				}
				catch
				{
					// no type info available
				}
			}

			if (typeInfo == null)
				return false;

			try
			{
				// Prefer scanning the containing type library to see all related types
				typeInfo.GetContainingTypeLib(out var typeLib, out _);
				if (typeLib != null)
				{
					try
					{
						int count = typeLib.GetTypeInfoCount();
						for (int i = 0; i < count; i++)
						{
							ITypeInfo ti = null;
							try
							{
								typeLib.GetTypeInfo(i, out ti);
								if (ti == null) continue;
								if (GetMatchingTypeInfo(ti, dispId, methodName, preferredKinds, out expectedTypes, out modifiers, out invokeKind))
									return true;
							}
							finally
							{
								if (ti != null) Marshal.ReleaseComObject(ti);
							}
						}
					}
					finally
					{
						if (Marshal.IsComObject(typeLib)) Marshal.ReleaseComObject(typeLib);
					}
				}
				else
				{
					// Fallback: only this one ITypeInfo
					if (GetMatchingTypeInfo(typeInfo, dispId, methodName, preferredKinds, out expectedTypes, out modifiers, out invokeKind))
						return true;
				}
			}
			finally
			{
				if (Marshal.IsComObject(typeInfo)) Marshal.ReleaseComObject(typeInfo);

				// Cache type info if we got it
				if (expectedTypes != null || modifiers != null)
				{
					_ = Script.TheScript.ComMethodData.comMethodCache
						.GetOrAdd(ptr, key => new Dictionary<string, ComMethodInfo>(StringComparer.OrdinalIgnoreCase))
						.GetOrAdd($"{methodName}_{preferredKinds}", new ComMethodInfo
						{
							expectedTypes = expectedTypes,
							modifiers = modifiers,
							invokeKind = invokeKind
						});
				}
			}

			invokeKind = preferredKinds;
			return false;
		}


		private static void ExtractParameterInfo(FUNCDESC funcDesc, out Type[] expectedTypes, out ParameterModifier[] modifiers, out INVOKEKIND invokeKind)
		{
			invokeKind = funcDesc.invkind;
			int paramCount = funcDesc.cParams;
			if (paramCount == 0)
			{
				expectedTypes = null; modifiers = null;
				return;
			}
			expectedTypes = new Type[paramCount];
			var modifier = new ParameterModifier(paramCount);

			for (int i = 0; i < paramCount; i++)
			{
				var pElemDesc = new nint(funcDesc.lprgelemdescParam.ToInt64() + (i * Marshal.SizeOf<ELEMDESC>()));
				var elem = Marshal.PtrToStructure<ELEMDESC>(pElemDesc);

				// Look at flags to determine by-ref semantics.
				var flags = (PARAMFLAG)elem.desc.paramdesc.wParamFlags;
				bool isOut = (flags & PARAMFLAG.PARAMFLAG_FOUT) != 0;

				modifier[i] = isOut;

				// Base VARTYPE
				var vt = (VarEnum)elem.tdesc.vt;

				// Special case: many DISP TLBs use VT_PTR -> VT_VARIANT for *by-value* VARIANTs.
				if (vt == VarEnum.VT_PTR && elem.tdesc.lpValue != 0)
				{
					var pointed = Marshal.PtrToStructure<TYPEDESC>(elem.tdesc.lpValue);
					var pvt = (VarEnum)pointed.vt;
					expectedTypes[i] = VariantHelper.VarEnumToCLRType(pvt);
					continue;
				}
				if (vt == VarEnum.VT_SAFEARRAY && elem.tdesc.lpValue != 0)
				{
					var pointed = Marshal.PtrToStructure<TYPEDESC>(elem.tdesc.lpValue);
					var elemVt = (VarEnum)pointed.vt;

					// Map <T> to CLR type, then make it an array type (T[])
					var clrElem = VariantHelper.VarEnumToCLRType(elemVt);
					expectedTypes[i] = clrElem.MakeArrayType();
					continue;
				}

				// Regular case
				expectedTypes[i] = VariantHelper.VarEnumToCLRType(vt);
			}

			modifiers = new[] { modifier };
		}

	}
}

#endif