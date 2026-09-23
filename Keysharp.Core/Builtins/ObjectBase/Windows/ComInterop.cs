#if WINDOWS
#nullable enable
using Keysharp.Builtins.COM;
using static Keysharp.Runtime.Script;

namespace Keysharp.Builtins
{
	/// <summary>
	/// How a COM client sees a Keysharp object: through an IDispatch of the object's own, as AutoHotkey's objects have,
	/// in place of the one the runtime would derive from the C# members of its class. Every name a client asks for gets
	/// a DISPID, and what the object does with it, a meta-function included, is decided when it is invoked.
	/// </summary>
	public partial class Any : IDispatch, ICustomQueryInterface
	{
		private const int S_OK = 0;
		private const int E_NOTIMPL = unchecked((int)0x80004001);
		private const int DISP_E_MEMBERNOTFOUND = unchecked((int)0x80020003);
		private const int DISP_E_UNKNOWNNAME = unchecked((int)0x80020006);
		private const int DISP_E_EXCEPTION = unchecked((int)0x80020009);
		private const int DISPID_VALUE = 0;
		private const int DISPID_UNKNOWN = -1;
		private const int DISPID_NEWENUM = -4;

		CustomQueryInterfaceResult ICustomQueryInterface.GetInterface(ref Guid iid, out nint ppv)
		{
			if (iid == Com.IID_IDispatch)
			{
				ppv = Marshal.GetComInterfaceForObject(this, typeof(IDispatch), CustomQueryInterfaceMode.Ignore);
				return CustomQueryInterfaceResult.Handled;
			}

			if (iid == Com.IID_KeysharpObject)
			{
				ppv = Marshal.GetIUnknownForObject(this);
				return CustomQueryInterfaceResult.Handled;
			}

			ppv = 0;
			return CustomQueryInterfaceResult.NotHandled;
		}

		int IDispatch.GetTypeInfoCount(out uint info)
		{
			info = 0;
			return S_OK;
		}

		int IDispatch.GetTypeInfo(int iTInfo, int lcid, out ITypeInfo? ppTInfo)
		{
			ppTInfo = null;
			return E_NOTIMPL;
		}

		int IDispatch.GetIDsOfNames(ref Guid guid, string[] names, int cNames, int lcid, int[] rgDispId)
		{
			for (var i = 1; i < cNames; i++)
				rgDispId[i] = DISPID_UNKNOWN;

			rgDispId[0] = ComDispatchNames.IdOf(names[0]);
			// Only the member is named: the names of arguments are not resolved, as in AHK.
			return cNames == 1 ? S_OK : DISP_E_UNKNOWNNAME;
		}

		int IDispatch.Invoke(int dispIdMember, ref Guid riid, int lcid, INVOKEKIND wFlags, ref DISPPARAMS pDispParams, nint pVarResult, nint pExcepInfo, nint puArgErr)
		{
			string? name = null;
			var newEnum = false;

			if (dispIdMember > 0)
			{
				if ((name = ComDispatchNames.NameOf(dispIdMember)) == null)
					return DISP_E_MEMBERNOTFOUND;
			}
			else if (dispIdMember == DISPID_NEWENUM && (wFlags & (INVOKEKIND.INVOKE_FUNC | INVOKEKIND.INVOKE_PROPERTYGET)) != 0)
			{
				name = "__Enum";
				newEnum = true;
			}
			else if (dispIdMember != DISPID_VALUE)
				return DISP_E_MEMBERNOTFOUND;

			var exit = new Threads.ExitState(Threads.Current);

			try
			{
				// An error goes back to the caller, which is the one to handle it, rather than to a dialog.
				using var caught = Keysharp.Runtime.Flow.EnterTry();
				var args = ReadArguments(ref pDispParams, out var byRefs);
				object? result;

				if ((wFlags & (INVOKEKIND.INVOKE_PROPERTYPUT | INVOKEKIND.INVOKE_PROPERTYPUTREF)) != 0)
				{
					// The value is the last argument, after any index.
					_ = name == null ? SetObject(this, args) : SetPropertyValue(this, name, args);
					return S_OK;
				}

				if (newEnum)
					result = new ComEnumerator(Invoke(this, name, 1L));
				else
				{
					var callable = Functions.HasMethod(this, name) != 0L;
					var readable = Functions.HasProp(this, name ?? "__Item") != 0L;
					// A client that cannot tell a call from an indexed read, as VBScript's obj.X(y) cannot, asks for both:
					// a method is called, and failing that a property is read.
					var call = (wFlags & INVOKEKIND.INVOKE_FUNC) != 0 && ((wFlags & INVOKEKIND.INVOKE_PROPERTYGET) == 0 || callable || !readable);

					// What the object neither has nor answers for through a meta-function is not found, which is how a
					// client tells a member that is missing from one that raised an error.
					if (call ? !callable && !readable && !Answers(name, "__Call") : !readable && !Answers(name, "__Get"))
						return DISP_E_MEMBERNOTFOUND;

					result = call ? DispatchCall(name, args, byRefs) : name == null ? GetIndex(this, args) : GetPropertyValue(this, name, args);
				}

				if (pVarResult != 0)
				{
					VariantHelper.VariantInit(pVarResult);
					var variant = result is ComEnumerator enumerator ? enumerator.ToVariant() : VariantHelper.ResultToVariant(result);
					Marshal.StructureToPtr(variant, pVarResult, false);
				}

				return S_OK;
			}
			// Exit ends only the call, which AutoHotkey reports to the caller as success; ExitApp goes on unwinding.
			catch (Exception ex) when (!TheScript.hasExited && Keysharp.Internals.Flow.TryGetException<Flow.UserRequestedExitException>(ex, out _))
			{
				exit.Restore();
				return S_OK;
			}
			catch (Exception ex)
			{
				// As in AutoHotkey, a caller which takes no exception information cannot pass the error on, so it is reported.
				if (pExcepInfo == 0)
				{
					_ = Errors.ReportUncaught(ex);
					return unchecked((int)0x80004005);//E_FAIL
				}

				var error = ex is KeysharpException ? ex : ex.InnerException ?? ex;
				var info = new EXCEPINFO
				{
					scode = DISP_E_EXCEPTION,
					bstrDescription = error.Message,
					bstrSource = (error as KeysharpException)?.DiagnosticError?.What ?? ""
				};
				Marshal.StructureToPtr(info, pExcepInfo, false);
				return DISP_E_EXCEPTION;
			}
		}

		// Whether a meta-function answers for a named member the object does not have; the default member has none.
		private bool Answers(string? name, string meta) => name != null && Functions.HasMethod(this, meta) != 0L;

		// rgvarg runs right to left; a by-reference argument is read through, and remembered for the write back.
		private static object?[] ReadArguments(ref DISPPARAMS dispParams, out VARIANT[]? byRefs)
		{
			byRefs = null;
			var count = dispParams.cArgs;

			if (count == 0)
				return [];

			var args = new object?[count];
			var size = Marshal.SizeOf<VARIANT>();

			for (var i = 0; i < count; i++)
			{
				var variant = Marshal.PtrToStructure<VARIANT>(dispParams.rgvarg + (i * size));
				var index = count - 1 - i;

				if (((VarEnum)variant.vt & VarEnum.VT_BYREF) != 0)
				{
					(byRefs ??= new VARIANT[count])[index] = variant;
					args[index] = VariantHelper.ReadByRefVariant(variant);
				}
				else
					args[index] = VariantHelper.ArgumentToValue(variant);
			}

			return args;
		}

		private object? DispatchCall(string? name, object?[] args, VARIANT[]? byRefs)
		{
			var slots = ByRefSlots(name, args.Length);

			if (slots == null)
				return InvokeOrNull(this, name, args!);

			for (var i = 0; i < slots.Length; i++)
				if (slots[i])
					args[i] = new VarRef(args[i]!);

			var result = InvokeOrNull(this, name, args!);

			// Only an argument the caller passed by reference has anywhere to be written back to.
			for (var i = 0; i < slots.Length; i++)
				if (slots[i] && byRefs != null && byRefs[i].vt != 0)
					VariantHelper.WriteByRefVariant(byRefs[i], ((VarRef)args[i]!).__Value);

			return result;
		}

		// Which argument slots the callee writes back through, or null when none. A caller's VT_BYREF alone does not
		// say: VBScript passes every variable that way. The callee's own [ByRef] marks do, and a marked parameter gets
		// a VarRef to write through whether or not the caller can receive the value.
		private bool[]? ByRefSlots(string? name, int argCount)
		{
			KeysharpFunc? fo;
			object? receiver = null;

			if (argCount == 0)
				return null;

			if (name == null)
				fo = this as KeysharpFunc;
			else
			{
				var (owner, member) = GetMethodOrProperty(this, name, -1, checkBase: true, throwIfMissing: false, invokeMeta: false);
				fo = member as KeysharpFunc;
				receiver = owner;
			}

			// An ObjBindMethod reference does not resolve its target until it runs, so its placeholder MPH carries no
			// signature to read marks off -- KeysharpFunc.IsByRef answers false for the same reason.
			if (fo?.Mph?.mi == null)
				return null;

			var prms = fo.Mph.mi.GetParameters();
			// A caller's argument slot is not a parameter index. Two things shift it: the receiver may be carried as
			// parameters[0] (the explicit `object @this` a lowered class method declares), which is what ArgBase
			// measures; and Bind may already have filled slots, which this call's arguments flow PAST rather than into,
			// so the holes have to be walked exactly as BoundFunc.CreateArgs walks them when it merges the two. A method
			// resolved by name carries no Inst of its own -- the receiver comes from the resolution, exactly as
			// KeysharpFunc.CallInst takes `Inst ?? inst`.
			var argBase = NamedArgBinder.ArgBase(fo.Mph, fo.Inst ?? receiver);
			var boundargs = (fo as BoundFunc)?.boundargs;
			bool[]? slots = null;

			for (int i = 0, slot = 0; i < argCount; i++, slot++)
			{
				if (boundargs != null)
					while (slot < boundargs.Length && boundargs[slot] != null)
						slot++;

				var p = slot - argBase;

				if (p < 0)   // the caller prepended the receiver, which is never an out-parameter
					continue;

				if (p >= prms.Length)
					break;

				if (!prms[p].IsDefined(typeof(ByRefAttribute)))
					continue;

				slots ??= new bool[argCount];

				// A [ByRef] `params object[]` marks everything it absorbs, so the tail is all out-parameters from here
				// on -- see Enumerator.Call, which stores each argument through __Value.
				if (prms[p].IsDefined(typeof(ParamArrayAttribute), false))
				{
					for (var j = i; j < slots.Length; j++)
						slots[j] = true;

					break;
				}

				slots[i] = true;
			}

			return slots;
		}
	}

	/// <summary>
	/// The DISPIDs handed out for member names, shared by every object as in AHK. A name keeps the case it was asked
	/// for in, so that a meta-function or a new property sees it as written; finding the member ignores case anyway.
	/// </summary>
	internal static class ComDispatchNames
	{
		private static readonly Lock gate = new();
		private static readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
		private static readonly List<string> names = [];

		internal static int IdOf(string name)
		{
			lock (gate)
			{
				if (!ids.TryGetValue(name, out var id))
				{
					names.Add(name);
					ids[name] = id = names.Count;   // from 1, which keeps clear of DISPID_VALUE
				}

				return id;
			}
		}

		internal static string? NameOf(int id)
		{
			lock (gate)
				return id > 0 && id <= names.Count ? names[id - 1] : null;
		}
	}

	[ComImport]
	[Guid("00020404-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IEnumVariantRaw
	{
		[PreserveSig]
		int Next(uint celt, nint rgVar, nint pCeltFetched);

		[PreserveSig]
		int Skip(uint celt);

		[PreserveSig]
		int Reset();

		[PreserveSig]
		int Clone(out nint ppEnum);
	}

	/// <summary>
	/// A Keysharp enumerator as the IEnumVARIANT a client gets from an object's _NewEnum, which is what VBScript's
	/// For Each and JScript's Enumerator drive. Each item is the enumerator's first variable, as in AHK.
	/// </summary>
	internal sealed class ComEnumerator(object enumerator) : IEnumVariantRaw
	{
		private const int S_FALSE = 1;
		private const int E_NOTIMPL = unchecked((int)0x80004001);

		internal VARIANT ToVariant() => new()
		{
			vt = (ushort)VarEnum.VT_UNKNOWN,
			ptrVal = Marshal.GetComInterfaceForObject(this, typeof(IEnumVariantRaw))
		};

		public int Next(uint celt, nint rgVar, nint pCeltFetched)
		{
			var size = Marshal.SizeOf<VARIANT>();
			uint fetched = 0;

			try
			{
				using var caught = Keysharp.Runtime.Flow.EnterTry();

				for (; fetched < celt && TryNext(out var item); fetched++)
				{
					var slot = rgVar + (nint)(fetched * size);
					VariantHelper.VariantInit(slot);
					Marshal.StructureToPtr(VariantHelper.ResultToVariant(item), slot, false);
				}
			}
			catch (Exception)
			{
				// An enumerator that fails has nothing more to give, which is all this interface can say.
			}

			if (pCeltFetched != 0)
				Marshal.WriteInt32(pCeltFetched, (int)fetched);

			return fetched == celt ? 0 : S_FALSE;
		}

		public int Skip(uint celt)
		{
			try
			{
				using var caught = Keysharp.Runtime.Flow.EnterTry();

				for (; celt > 0 && TryNext(out _); celt--)
				{
				}
			}
			catch (Exception)
			{
			}

			return celt == 0 ? 0 : S_FALSE;
		}

		public int Reset() => E_NOTIMPL;

		public int Clone(out nint ppEnum)
		{
			ppEnum = 0;
			return E_NOTIMPL;
		}

		private bool TryNext(out object? item)
		{
			var variable = new VarRef((object)null!);
			var more = ForceBool(InvokeOrNull(enumerator, null, variable));
			item = more ? variable.__Value : null;
			return more;
		}
	}
}
#endif
