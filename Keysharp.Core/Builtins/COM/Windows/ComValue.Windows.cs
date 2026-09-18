#if WINDOWS
using Keysharp.Builtins.COM;
using Keysharp.Runtime;

namespace Keysharp.Builtins.COM
{
	public unsafe class ComValue : Any, IDisposable, IMetaObject
	{
		internal static readonly long F_OWNVALUE = 1;
		internal static readonly int MaxVtableLen = 16;
		internal readonly CallbackRegistry handlers = new();
		internal object item;
		// Where this object's members are looked up in type info, found on the first lookup; see ComTypeScope.
		private ComTypeScope typeScope;

		private nint NintPtr => Ptr switch { long lp => (nint)lp, nint ip => ip, _ => 0 };
		public object Ptr
		{
			get => item;
			set
			{
				typeScope = null;

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
								? Com.DispatchPointer(value)
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

		public ComValue(params object[] args) : base(args)
		{
			if (args.Length == 0 || args[0] == null) return;
			var value = args[1];
			//Internal callers pass a VarEnum, which the script-integer coercion in Al() does not recognize.
			vt = args[0] is VarEnum ve ? ve : (VarEnum)args[0].Al();
			Ptr = value;
			var flags = args.Length > 2 ? args[2] : null;
			Flags = flags != null ? flags.Al() : 0L;

			if (this.vt == VarEnum.VT_BSTR && value is not long)
				Flags |= F_OWNVALUE;
		}

		public static object staticCall(object @this, object varType, object value, object flags = null)
		{
			var vt = (VarEnum)varType.Al();
			if ((vt & VarEnum.VT_ARRAY) != 0)
			{
				Reflections.TryGetPtrProperty(value, out var psaAddr);
				nint psa = new nint(psaAddr);
				return new ComObjArray(vt & ~VarEnum.VT_ARRAY, psa, flags.Ab());
			}
			if ((vt & VarEnum.VT_BYREF) != 0)
				return new ComValueRef(varType, value, flags);
			return vt == VarEnum.VT_DISPATCH ? new ComObject(varType, value, flags) : new ComValue(varType, value, flags);
		}

		object IMetaObject.Get(string name, object[] args) => RawGetProperty(name, args);

		void IMetaObject.Set(string name, object[] args, object value) => RawSetProperty(name, args, value);

		object IMetaObject.Call(string name, object[] args) => RawInvokeMethod(name, args);

		// IDispatch resolves argument names to parameter DISPIDs; see RawInvokeMethod.
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

		[PublicHiddenFromUser]
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
			}

			Ptr = null;
			HasFinalizer = false;
		}

		internal void CallEvents()
		{
			handlers.InvokeEventHandlers(this);
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
					// A numeric Ptr (e.g. ComValue(VT_UNKNOWN, 0)) is a raw interface pointer value:
					// 0 means a null interface, so leave v.ptrVal = 0. Only wrap genuine managed
					// objects in a CCW — wrapping a boxed integer would yield a CCW around the number,
					// which fails to QI to the expected interface (E_NOINTERFACE).
					else if (Ptr != null && Ptr is not long && Ptr is not nint)
						v.ptrVal = (vtype == VarEnum.VT_DISPATCH)
							? Com.DispatchPointer(Ptr) // our ref, VariantClear will Release
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

		private const int DISP_E_MEMBERNOTFOUND = unchecked((int)0x80020003);
		private const int DISP_E_PARAMNOTFOUND = unchecked((int)0x80020004);
		private const int DISP_E_TYPEMISMATCH = unchecked((int)0x80020005);
		private const int DISP_E_UNKNOWNNAME = unchecked((int)0x80020006);
		private const int DISP_E_BADVARTYPE = unchecked((int)0x80020008);
		private const int DISP_E_EXCEPTION = unchecked((int)0x80020009);
		private const int DISP_E_OVERFLOW = unchecked((int)0x8002000A);
		private const int DISP_E_BADPARAMCOUNT = unchecked((int)0x8002000E);
		// What a call or a property read may invoke: its second attempt, as AHK makes it, and the kinds looked up in type info.
		private const INVOKEKIND FuncOrGet = INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET;

		// The server could not take an argument as passed, which its type info may fix by coercing the argument.
		private static bool IsConversionFailure(int hr) =>
			hr is DISP_E_TYPEMISMATCH or DISP_E_BADVARTYPE or DISP_E_PARAMNOTFOUND or DISP_E_OVERFLOW;

		// This object's member as its type info declares it, or null; see ComTypeScope. Read only after a conversion
		// failure or for an Array argument: MSHTML makes a new wrapper for every element a property returns, and reading
		// a wrapper's type info costs most of a millisecond.
		private ComMethodInfo MemberInfo(int dispId, string name, INVOKEKIND kinds) =>
			(typeScope ??= Script.TheScript.ComMethodData.ScopeOf(NintPtr)).Resolve(dispId, name, kinds);

		// An Array goes as the object it is, as in AHK, except where the member's type info declares a SAFEARRAY.
		private object[] SafeArraysWhereDeclared(int dispId, string name, object[] args, INVOKEKIND kinds)
		{
			if (!System.Array.Exists(args, static a => a is Array) || MemberInfo(dispId, name, kinds)?.expectedTypes is not Type[] types)
				return args;

			object[] passed = null;

			for (var i = 0; i < args.Length && i < types.Length; i++)
				if (args[i] is Array ksarr && types[i].IsArray)
					(passed ??= (object[])args.Clone())[i] = ConvertArgumentToExpectedType(ksarr, types[i]);

			return passed ?? args;
		}

		// A server that rejected an Array object gets its elements as a SAFEARRAY on the second attempt.
		private static object[] RejectedArraysAsSafeArrays(object[] args)
		{
			object[] passed = null;

			for (var i = 0; i < args.Length; i++)
				if (args[i] is Array ksarr)
					(passed ??= (object[])args.Clone())[i] = VariantHelper.SafeArrayElements(ksarr);

			return passed ?? args;
		}

		// After a conversion failure, the call again with the arguments coerced to the types info declares and any Array as
		// a SAFEARRAY, unless neither changes it; byRefs, when given, stands for what info declares by reference.
		private int RetryCoerced(int hr, int dispId, ComMethodInfo info, INVOKEKIND kinds, ref object[] args, ref object result, bool[] byRefs = null)
		{
			var retried = RejectedArraysAsSafeArrays(args);

			return info?.expectedTypes is { Length: > 0 } || !ReferenceEquals(retried, args)
				? RawInvoke(dispId, info?.invokeKind ?? kinds, args = retried, out result, info?.expectedTypes, byRefs ?? info?.byRefs)
				: hr;
		}

		internal unsafe object RawInvokeMethod(string methodName, object[] inputParameters)
		{
			bool[] byRefs = null;
			Dictionary<int, object> refs = null;
			int hr, dispId;
			int[] namedDispIds = null;
			// Named arguments (`obj.Method(Key: v)`) travel as a trailing container. IDispatch takes them
			// natively, so resolve each name to its parameter DISPID alongside the member name and unwrap the values
			// in place -- the trailing layout is exactly right, because rgvarg is filled in REVERSE, which puts the
			// named values at the front of rgvarg where DISPPARAMS.rgdispidNamedArgs expects them.
			inputParameters = Keysharp.Internals.Invoke.NamedArgBinder.StripNames(inputParameters, out var argNames);

			// No name is a nameless call, `obj()`: the default member, DISPID_VALUE, as AHK invokes it.
			if (methodName == null)
			{
				if (argNames.Length != 0)
					return Errors.ErrorOccurred($"A call to a COM object's default member cannot name its arguments ({string.Join(", ", argNames)}).");

				dispId = 0;
			}
			else if (argNames.Length != 0)
			{
				hr = RawGetIDsOfNames(methodName, argNames, out dispId, out var ids);

				if (hr < 0)
					return Errors.ErrorOccurred($"Method '{methodName}' or one of its named arguments ({string.Join(", ", argNames)}) was not found (HRESULT: 0x{hr:X8})");

				// rgdispidNamedArgs is parallel to the FRONT of rgvarg, which holds the named values in reverse of
				// their source order -- so the DISPIDs are reversed to match.
				namedDispIds = ids.Reverse().ToArray();
			}
			else
			{
				hr = RawGetIDsOfNames(methodName, out dispId);

				if (hr < 0)
					return Errors.ErrorOccurred($"Method '{methodName}' not found (HRESULT: 0x{hr:X8})");
			}

			for (int i = 0; i < inputParameters.Length; i++)
			{
				// Nothing has said which parameters are by-reference yet -- that is what this pass guesses from the
				// arguments -- so a value must prove it carries a __Value. A ComValue or ComObject argument is
				// being passed, not written back into.
				if (Refs.DeclaresValue(inputParameters[i]))
				{
					(byRefs ??= new bool[inputParameters.Length])[i] = true;
					(refs ??= [])[i] = inputParameters[i]; // remember for write-back
					inputParameters[i] = Refs.GetValueOrNull(inputParameters[i]); // unwrap to the current value for packing
				}
			}

			if (namedDispIds == null)
				inputParameters = SafeArraysWhereDeclared(dispId, methodName, inputParameters, FuncOrGet);

			hr = RawInvoke(dispId, INVOKEKIND.INVOKE_FUNC, inputParameters, out object result, expectedTypes: null, byRefs: byRefs, namedDispIds: namedDispIds);
			if (hr == DISP_E_MEMBERNOTFOUND)
				hr = RawInvoke(dispId, FuncOrGet, inputParameters, out result, expectedTypes: null, byRefs: byRefs, namedDispIds: namedDispIds);

			// SLOW PATH (only on conversion-ish failures): query type info and retry. Positional calls only: the
			// coercion below applies expectedTypes[i]/byRefs[i] to the argument at SOURCE index i, but a named
			// argument's value does not bind parameter i -- its DISPID says where it goes -- so the retry would
			// coerce values against the wrong parameters' types. The target already saw the un-coerced values once;
			// for a named call its verdict stands.
			if (namedDispIds == null && IsConversionFailure(hr))
			{
				var info = MemberInfo(dispId, methodName, FuncOrGet);

				// The first attempt passed by reference whatever carries a __Value, which the type info overrules for the
				// parameters it declares: by value, a VarRef's value goes as it is and any other object as itself. A
				// vararg tail keeps the guess.
				if (byRefs != null && info?.byRefs is bool[] declared)
					for (var i = 0; i < declared.Length && i < byRefs.Length; i++)
					{
						if (!declared[i] && refs.Remove(i, out var passed) && passed is not VarRef)
							inputParameters[i] = passed;

						byRefs[i] = declared[i];
					}

				hr = RetryCoerced(hr, dispId, info, FuncOrGet, ref inputParameters, ref result, byRefs);
			}

			if (hr >= 0 && refs != null)
			{
				// Update byref parameters
				foreach (var kvp in refs)
				{
					Refs.SetValue(kvp.Value, inputParameters[kvp.Key]);
				}
			}

			return hr >= 0 ? result : Errors.ErrorOccurred($"Invoke failed for '{methodName}' ({result})", DefaultObject);
		}

		internal unsafe object RawGetProperty(string propertyName, object[] args)
		{
			if (RawGetIDsOfNames(propertyName, out int dispId) < 0)
				return Errors.MissingPropertyErrorOccurred(this, propertyName);

			var hr = InvokeGet(dispId, propertyName, args, out var result);

			// `obj.member[args]` on a property with no parameters of its own indexes its value, as it does a field's. The type
			// info, read already by a conversion failure's retry, says whether it has none; without any, a refused count does.
			if (args.Length > 0 && (hr == DISP_E_BADPARAMCOUNT || IsConversionFailure(hr))
					&& MemberInfo(dispId, propertyName, FuncOrGet) is var info && (info == null ? hr == DISP_E_BADPARAMCOUNT : info.expectedTypes == null)
					&& InvokeGet(dispId, propertyName, [], out var value) >= 0)
				return GetIndexOrNull(value, args);

			return hr >= 0 ? result : Errors.ErrorOccurred($"Get property failed for '{propertyName}' ({result})");
		}

		// The flag matching the syntax first, so a server exposing a member both ways reads it, then both, for a server
		// describing a property as a method.
		private int InvokeGet(int dispId, string name, object[] args, out object result)
		{
			var callArgs = args.Length > 0 ? SafeArraysWhereDeclared(dispId, name, args, FuncOrGet) : null;
			var hr = RawInvoke(dispId, INVOKEKIND.INVOKE_PROPERTYGET, callArgs, out result);

			if (hr == DISP_E_MEMBERNOTFOUND)
				hr = RawInvoke(dispId, FuncOrGet, callArgs, out result);

			if (callArgs != null && IsConversionFailure(hr))
				hr = RetryCoerced(hr, dispId, MemberInfo(dispId, name, FuncOrGet), FuncOrGet, ref callArgs, ref result);

			return hr;
		}

		internal unsafe void RawSetProperty(string propertyName, object[] args, object value)
		{
			int hr = RawGetIDsOfNames(propertyName, out int dispId);

			if (hr < 0)
			{
				// A bare COMException here reaches the script as an uncatchable CLR exception; a name this value
				// does not expose is the same PropertyError the read path raises.
				_ = Errors.MissingPropertyErrorOccurred(this, propertyName);
				return;
			}

			var callArgs = SafeArraysWhereDeclared(dispId, propertyName, [.. args, value], INVOKEKIND.INVOKE_PROPERTYPUT | INVOKEKIND.INVOKE_PROPERTYPUTREF);
			// An object goes by reference, then by value when the server has no putref (it may then read the object's
			// default member for the value), as AHK assigns a VT_DISPATCH.
			var sent = callArgs[^1];
			var kind = (sent is ComValue cv ? cv.vt == VarEnum.VT_DISPATCH : sent is Any) ? INVOKEKIND.INVOKE_PROPERTYPUTREF : INVOKEKIND.INVOKE_PROPERTYPUT;
			hr = RawInvoke(dispId, kind, callArgs, out var result);

			if (hr == DISP_E_MEMBERNOTFOUND && kind == INVOKEKIND.INVOKE_PROPERTYPUTREF)
				hr = RawInvoke(dispId, kind = INVOKEKIND.INVOKE_PROPERTYPUT, callArgs, out result);

			if (IsConversionFailure(hr))
			{
				// The retry sends an Array value as a SAFEARRAY, which goes by value.
				if (sent is Array)
					kind = INVOKEKIND.INVOKE_PROPERTYPUT;

				hr = RetryCoerced(hr, dispId, MemberInfo(dispId, propertyName, kind), kind, ref callArgs, ref result);
			}

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

		/// <summary>
		/// Resolves a member name plus the names of its named arguments in one IDispatch::GetIDsOfNames call, which
		/// is how the interface is meant to be used: the member name is names[0] and each argument name follows,
		/// yielding the per-parameter DISPIDs that go into DISPPARAMS.rgdispidNamedArgs.
		/// <para>
		/// A target that cannot resolve a name (no type information, or simply no such parameter) fails here rather
		/// than silently mis-binding, which is the behaviour named arguments need.
		/// </para>
		/// </summary>
		internal unsafe int RawGetIDsOfNames(string memberName, IReadOnlyList<string> argNames, out int dispId, out int[] argDispIds)
		{
			dispId = 0;
			argDispIds = null;
			var vtbl = GetDispatchVtbl();

			if (vtbl == null)
				return -1;

			var count = 1 + argNames.Count;
			var ids = new int[count];
			var ptrs = new nint[count];
			nint ptr = new((long)Ptr);
			Guid iidNull = Guid.Empty;

			try
			{
				ptrs[0] = Marshal.StringToCoTaskMemUni(memberName);

				for (var i = 0; i < argNames.Count; i++)
					ptrs[i + 1] = Marshal.StringToCoTaskMemUni(argNames[i]);

				int hr;

				fixed (nint* pNames = ptrs)
				fixed (int* pIds = ids)
					hr = vtbl->GetIDsOfNames(ptr, &iidNull, (nint*)pNames, (uint)count, Com.LOCALE_USER_DEFAULT, pIds);

				if (hr < 0)
					return hr;

				dispId = ids[0];
				argDispIds = ids[1..];
				return hr;
			}
			finally
			{
				foreach (var p in ptrs)
					if (p != 0) Marshal.FreeCoTaskMem(p);
			}
		}

		internal unsafe int RawGetIDsOfNames(string name, out int dispId)
		{
			dispId = 0;
			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return -1;

			nint ptr = new((long)Ptr);

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
					if (hr == DISP_E_UNKNOWNNAME || hr == DISP_E_MEMBERNOTFOUND)
					{
						// QI for IDispatchEx and call GetDispID on that *interface pointer*
						var ex = TryGetDispatchExVtbl(out nint pEx);
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

		// Type info declares no flag for a vararg tail, so byRefs can be shorter than the arguments.
		private static bool IsByRef(bool[] byRefs, int index) => byRefs != null && index < byRefs.Length && byRefs[index];

		/// <param name="namedDispIds">
		/// Parameter DISPIDs for a call using named arguments, already ordered to match the FRONT of rgvarg (which
		/// is filled in reverse, so these are the reverse of the arguments' source order). Null for a purely
		/// positional call. See RawInvokeMethod.
		/// </param>
		internal unsafe int RawInvoke(
			int dispId,
			INVOKEKIND flags,
			object[] args,
			out object result,
			Type[] expectedTypes = null,
			bool[] byRefs = null,
			int[] namedDispIds = null)
		{
			result = null;
			bool isPut = (flags & INVOKEKIND.INVOKE_PROPERTYPUT) != 0;
			bool isPutRef = (flags & INVOKEKIND.INVOKE_PROPERTYPUTREF) != 0;

			if ((isPut || isPutRef) && namedDispIds is { Length: > 0 })
				return Errors.ErrorOccurred(new ValueError("Named arguments are not supported when setting a COM property."), -1);

			var vtbl = GetDispatchVtbl();
			if (vtbl == null)
				return -1;

			nint ptr = Ptr is nint ip ? ip : new nint((long)Ptr);
			Guid iidNull = Guid.Empty;

			bool wantsResult = (flags & (INVOKEKIND.INVOKE_PROPERTYGET | INVOKEKIND.INVOKE_FUNC)) != 0
				   && (flags & (INVOKEKIND.INVOKE_PROPERTYPUT | INVOKEKIND.INVOKE_PROPERTYPUTREF)) == 0;

			nint pArgs = 0;
			nint pDispParams = Marshal.AllocHGlobal(Marshal.SizeOf<DISPPARAMS>());
			// Zeroed IMMEDIATELY: the finally below reads DISPPARAMS back from this block to free the variants, and
			// any throw before the real StructureToPtr (argument conversion, the named+PROPERTYPUT guard) would
			// otherwise have it walk a garbage cArgs over wild rgvarg pointers.
			Marshal.StructureToPtr(new DISPPARAMS(), pDispParams, false);
			nint pResult = wantsResult ? Marshal.AllocHGlobal(Marshal.SizeOf<VARIANT>()) : 0;
			nint pExcepInfo = Marshal.AllocHGlobal(Marshal.SizeOf<EXCEPINFO>());
			nint pArgErr = Marshal.AllocHGlobal(sizeof(uint));
			nint pNamed = 0;

			if (pResult != 0)
				VariantHelper.VariantInit(pResult);
			Marshal.StructureToPtr(new EXCEPINFO(), pExcepInfo, false);
			Marshal.WriteInt32(pArgErr, 0);

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

						bool isByRef = IsByRef(byRefs, sourceIndex);

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
							// A parameter its type info declares as a boolean gets one; anywhere else a boolean is an integer.
							variant = arg is bool flag && expectedTypes != null && sourceIndex < expectedTypes.Length && expectedTypes[sourceIndex] == typeof(bool)
									  ? VariantHelper.CreateVariantFromBool(flag)
									  : VariantHelper.ValueToVariant(arg);
						}
						Marshal.StructureToPtr(variant, variantPtr, false);
					}

					dispParams.rgvarg = pArgs;
					dispParams.cArgs = argCount;
				}

				dispParams.rgdispidNamedArgs = 0;
				dispParams.cNamedArgs = 0;

				// ---- Named arguments (`obj.Method(Key: v)`) ----
				// rgvarg is filled in reverse above, so the trailing named values landed at its front, which is
				// exactly where DISPPARAMS requires the named ones; namedDispIds is already reversed to match.
				if (namedDispIds != null && namedDispIds.Length > 0)
				{
					pNamed = Marshal.AllocHGlobal(sizeof(int) * namedDispIds.Length);

					for (var i = 0; i < namedDispIds.Length; i++)
						Marshal.WriteInt32(pNamed, i * sizeof(int), namedDispIds[i]);

					dispParams.rgdispidNamedArgs = pNamed;
					dispParams.cNamedArgs = namedDispIds.Length;
				}

				// ---- SPECIAL: PROPERTYPUT / PROPERTYPUTREF ----
				if (isPut || isPutRef)
				{
					// A property put owns rgdispidNamedArgs for DISPID_PROPERTYPUT, so it cannot also carry
					// per-parameter named DISPIDs. Nothing forwards both today; assert rather than leak the block
					// allocated above and silently discard the names if a future caller tries.
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
						var resultVt = (VarEnum)resultVariant.vt & ~VarEnum.VT_BYREF;
						result = (resultVt == VarEnum.VT_NULL || resultVt == VarEnum.VT_EMPTY)
							? DefaultObject
							: VariantHelper.VariantToValue(resultVariant);
					}

					// Handle byref out parameters
					if (byRefs != null && pArgs != 0)
					{
						for (int i = 0; i < argCount; i++)
						{
							int sourceIndex = argCount - 1 - i;
							if (IsByRef(byRefs, sourceIndex))
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

						_ = VariantHelper.VariantClear(variantPtr);
					}
					Marshal.FreeHGlobal(pArgs);
				}

				if (wantsResult && pResult != 0)
				{
					_ = VariantHelper.VariantClear(pResult);
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
				// Always normalize: an Array -> its elements, a scalar -> single-element array
				if (arg is not System.Array arr)
					arr = arg is Array ksarr ? VariantHelper.SafeArrayElements(ksarr) : new object[] { arg };

				// If the expected element type is not object, coerce each element when possible.
				var elemClr = expectedType.GetElementType() ?? typeof(object);
				if (elemClr == typeof(object)) return arr; // leave heterogenous as-is (will become VT_ARRAY|VT_VARIANT)

				int n = arr.Length;
				var coerced = System.Array.CreateInstance(elemClr, n);
				for (int i = 0; i < n; i++)
				{
					var value = arr.GetValue(i);
					// The numeric conversions give 0 for what is not a number, and none gives a nested array or an object its own value.
					var element = value is not (System.Array or Any) && (!elemClr.IsPrimitive || elemClr == typeof(bool) || value.TryCoerceLong(out _))
						? ConvertArgumentToExpectedType(value, elemClr)
						: null;

					// A string parameter takes an integer as it is, but a SAFEARRAY(BSTR) element must be a string.
					if (element is long && elemClr == typeof(string))
						element = element.As();

					// An element that does not convert leaves the array as VARIANTs, for the server to judge.
					if (!elemClr.IsInstanceOfType(element))
						return arr;

					coerced.SetValue(element, i);
				}
				return coerced;
			}

			// No scalar conversion gives an array its own value, so a scalar parameter's server judges it as it is.
			if (arg is System.Array)
				return arg;

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
			nint ptr = new((long)Ptr);
			nint pTypeInfo = 0;
			if (vtbl->GetTypeInfo(ptr, index, Com.LOCALE_USER_DEFAULT, &pTypeInfo) == 0 && pTypeInfo != 0)
			{
				typeInfo = (ITypeInfo)Marshal.GetObjectForIUnknown(pTypeInfo);
				Marshal.Release(pTypeInfo);
				return true;
			}
			return false;
		}
	}
}

#endif
