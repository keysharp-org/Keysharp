#if WINDOWS
namespace Keysharp.Builtins.COM
{
	internal partial class ComMethodData
	{
		// Both built on first use, as most scripts never read type info. A library's entry is null when it is not registered.
		private ConcurrentDictionary<Guid, ComTypeScope> scopes;
		private ConcurrentDictionary<(Guid guid, int lcid, short major, short minor), ComTypeLibrary> libraries;

		/// <summary>
		/// Where the members of the object behind <paramref name="pDispatch"/> are looked up, from the type info its
		/// IDispatch reports or, failing that, its class's. Type info is only a hint, so failing to read it means none.
		/// </summary>
		internal ComTypeScope ScopeOf(nint pDispatch)
		{
			var pTypeInfo = TypeInfoOf(pDispatch);

			if (pTypeInfo == 0)
				return ComTypeScope.None;

			try
			{
				// Every object a property returns gets a new wrapper, so a scope already built is found by the type's GUID,
				// read through the raw vtable because an RCW would cost more than the lookup saves.
				var guid = GuidOf(pTypeInfo);

				if (guid != Guid.Empty && scopes != null && scopes.TryGetValue(guid, out var scope))
					return scope;

				var typeInfo = (ITypeInfo)Marshal.GetUniqueObjectForIUnknown(pTypeInfo);

				try
				{
					scope = new ComTypeScope(typeInfo, LibraryOf(typeInfo));
				}
				finally
				{
					_ = Marshal.ReleaseComObject(typeInfo);
				}

				// A type info made at run time has no GUID to share its scope under.
				return guid == Guid.Empty ? scope : LazyInitializer.EnsureInitialized(ref scopes).GetOrAdd(guid, scope);
			}
			catch
			{
				// A failing HRESULT surfaces as any of several exception types, COMException and NotImplementedException among them.
				return ComTypeScope.None;
			}
			finally
			{
				_ = Marshal.Release(pTypeInfo);
			}
		}

		// IDispatch::GetTypeInfo, else IProvideClassInfo::GetClassInfo, the first method after IUnknown's.
		private static unsafe nint TypeInfoOf(nint pDispatch)
		{
			nint pTypeInfo = 0;

			if (pDispatch == 0)
				return 0;

			if ((*(ComValue.IDispatchVtbl**)pDispatch)->GetTypeInfo(pDispatch, 0, Com.LOCALE_USER_DEFAULT, &pTypeInfo) >= 0 && pTypeInfo != 0)
				return pTypeInfo;

			if (Marshal.QueryInterface(pDispatch, in Com.IID_IProvideClassInfo, out var pClassInfo) < 0)
				return 0;

			try
			{
				pTypeInfo = 0;
				return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pClassInfo)[3])(pClassInfo, &pTypeInfo) >= 0 ? pTypeInfo : 0;
			}
			finally
			{
				_ = Marshal.Release(pClassInfo);
			}
		}

		// TYPEATTR.guid, the struct's first field, through ITypeInfo::GetTypeAttr and ReleaseTypeAttr (vtable slots 3 and 19).
		private static unsafe Guid GuidOf(nint pTypeInfo)
		{
			var vtbl = *(nint**)pTypeInfo;
			nint pAttr = 0;

			if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[3])(pTypeInfo, &pAttr) < 0 || pAttr == 0)
				return Guid.Empty;

			var guid = *(Guid*)pAttr;
			((delegate* unmanaged[Stdcall]<nint, nint, void>)vtbl[19])(pTypeInfo, pAttr);
			return guid;
		}

		// The registered copy of the library containing typeInfo, one per library. It is in-process whatever the server
		// is, so holding it keeps no server running and any thread may use it.
		private ComTypeLibrary LibraryOf(ITypeInfo typeInfo)
		{
			TYPELIBATTR attr;

			try
			{
				typeInfo.GetContainingTypeLib(out var lib, out _);

				try
				{
					lib.GetLibAttr(out var pAttr);
					attr = Marshal.PtrToStructure<TYPELIBATTR>(pAttr);
					lib.ReleaseTLibAttr(pAttr);
				}
				finally
				{
					_ = Marshal.ReleaseComObject(lib);
				}
			}
			catch
			{
				return null;   // E_NOTIMPL and the like: not in a library
			}

			if (Volatile.Read(ref disposed) || attr.guid == Guid.Empty)
				return null;

			var libs = LazyInitializer.EnsureInitialized(ref libraries);
			var key = (attr.guid, attr.lcid, attr.wMajorVerNum, attr.wMinorVerNum);

			if (libs.TryGetValue(key, out var library))
				return library;

			var loaded = OleAuto.LoadRegTypeLib(in attr.guid, attr.wMajorVerNum, attr.wMinorVerNum, attr.lcid, out var pLib) >= 0 ? new ComTypeLibrary(pLib) : null;
			library = libs.GetOrAdd(key, loaded);

			if (!ReferenceEquals(library, loaded))
				loaded?.Release();

			// Dispose may have released the libraries before this one was added; Release is idempotent.
			if (Volatile.Read(ref disposed))
			{
				library?.Release();
				return null;
			}

			return library;
		}
	}

	internal class ComMethodInfo
	{
		internal int dispId;
		internal string name;
		internal Type[] expectedTypes;
		internal bool[] byRefs;
		internal INVOKEKIND invokeKind;
	}

	/// <summary>
	/// Where an object's members are looked up: what its type info declares, with what the interfaces it derives from
	/// declare and, for a class, its implemented interfaces; the registered library containing it is the last resort.
	/// Built once per type and shared by every object reporting it, so a lookup is a dictionary read.
	/// </summary>
	internal sealed class ComTypeScope
	{
		internal static readonly ComTypeScope None = new(null, null);
		// A type info with no GUID escapes the seen set, so a chain of more of them than this is taken for a cycle.
		private const int MaxUnidentifiedDepth = 8;
		private readonly Dictionary<int, List<ComMethodInfo>> members = [];
		private readonly ComTypeLibrary library;

		internal ComTypeScope(ITypeInfo typeInfo, ComTypeLibrary library)
		{
			try
			{
				if (typeInfo != null)
					Collect(typeInfo, [], 0);
			}
			catch
			{
				// As in CollectImplType: what was read before a type failed to describe itself stands, and is shared.
			}

			this.library = library;
		}

		/// <summary>The member's info, or null when nothing in scope declares it with one of <paramref name="kinds"/>.</summary>
		internal ComMethodInfo Resolve(int dispId, string name, INVOKEKIND kinds)
		{
			if (members.TryGetValue(dispId, out var declared) && FirstMatch(declared, name, kinds) is ComMethodInfo member)
				return member;

			// Only a name ties a DISPID found elsewhere in the library to this object: most of its types declare a default member.
			return name == null ? null : library?.Resolve(dispId, name, kinds);
		}

		// No name is a call to the default member, whatever its declaration names it.
		internal static ComMethodInfo FirstMatch(List<ComMethodInfo> members, string name, INVOKEKIND kinds)
		{
			foreach (var member in members)
				if ((member.invokeKind & kinds) != 0 && (name == null || string.Equals(member.name, name, StringComparison.OrdinalIgnoreCase)))
					return member;

			return null;
		}

		internal static TYPEATTR TypeAttrOf(ITypeInfo typeInfo)
		{
			typeInfo.GetTypeAttr(out var pAttr);

			try
			{
				return Marshal.PtrToStructure<TYPEATTR>(pAttr);
			}
			finally
			{
				typeInfo.ReleaseTypeAttr(pAttr);
			}
		}

		/// <summary>
		/// The members <paramref name="typeInfo"/> declares, or only those with <paramref name="dispId"/>: naming a member
		/// is the costly part of reading it. A restricted one, such as IDispatch's own in a dual interface's dispatch view,
		/// is no script's member.
		/// </summary>
		internal static IEnumerable<ComMethodInfo> MembersOf(ITypeInfo typeInfo, TYPEATTR attr, int? dispId = null)
		{
			for (var i = 0; i < attr.cFuncs; i++)
			{
				typeInfo.GetFuncDesc(i, out var pFuncDesc);

				try
				{
					var funcDesc = Marshal.PtrToStructure<FUNCDESC>(pFuncDesc);

					if (dispId is int only && funcDesc.memid != only || (funcDesc.wFuncFlags & (short)FUNCFLAGS.FUNCFLAG_FRESTRICTED) != 0)
						continue;

					typeInfo.GetDocumentation(funcDesc.memid, out var name, out _, out _, out _);
					yield return MemberOf(funcDesc, name);
				}
				finally
				{
					typeInfo.ReleaseFuncDesc(pFuncDesc);
				}
			}

			// A dispinterface may declare a property as a variable, which reads and, unless read-only, assigns.
			for (var i = 0; attr.typekind == TYPEKIND.TKIND_DISPATCH && i < attr.cVars; i++)
			{
				typeInfo.GetVarDesc(i, out var pVarDesc);

				try
				{
					var varDesc = Marshal.PtrToStructure<VARDESC>(pVarDesc);

					if (dispId is int only && varDesc.memid != only || (varDesc.wVarFlags & (short)VARFLAGS.VARFLAG_FRESTRICTED) != 0)
						continue;

					typeInfo.GetDocumentation(varDesc.memid, out var name, out _, out _, out _);
					yield return new ComMethodInfo { dispId = varDesc.memid, name = name, invokeKind = INVOKEKIND.INVOKE_PROPERTYGET };

					if ((varDesc.wVarFlags & (short)VARFLAGS.VARFLAG_FREADONLY) == 0)
						yield return new ComMethodInfo
						{
							dispId = varDesc.memid, name = name, invokeKind = INVOKEKIND.INVOKE_PROPERTYPUT,
							expectedTypes = [TypeOf(varDesc.elemdescVar.tdesc)], byRefs = [false]
						};
				}
				finally
				{
					typeInfo.ReleaseVarDesc(pVarDesc);
				}
			}
		}

		// The parameters a dispatch caller supplies: an interface's vtable view also lists its [retval] and [lcid]
		// parameters, which IDispatch::Invoke handles itself.
		private static ComMethodInfo MemberOf(FUNCDESC funcDesc, string name)
		{
			var info = new ComMethodInfo { dispId = funcDesc.memid, name = name, invokeKind = funcDesc.invkind };
			var types = new List<Type>(funcDesc.cParams);
			var outs = new List<bool>(funcDesc.cParams);

			for (var i = 0; i < funcDesc.cParams; i++)
			{
				var elem = Marshal.PtrToStructure<ELEMDESC>(funcDesc.lprgelemdescParam + (i * Marshal.SizeOf<ELEMDESC>()));
				var flags = (PARAMFLAG)elem.desc.paramdesc.wParamFlags;

				if ((flags & (PARAMFLAG.PARAMFLAG_FRETVAL | PARAMFLAG.PARAMFLAG_FLCID)) == 0)
				{
					outs.Add((flags & PARAMFLAG.PARAMFLAG_FOUT) != 0);
					types.Add(TypeOf(elem.tdesc));
				}
			}

			// A vararg member's last parameter is one SAFEARRAY(VARIANT) for its tail, which a dispatch caller passes as
			// arguments of their own; they are packed for it.
			if (funcDesc.cParamsOpt == -1 && types.Count > 0)
			{
				types.RemoveAt(types.Count - 1);
				outs.RemoveAt(outs.Count - 1);
			}

			if (types.Count == 0)
			{
				// Empty rather than null, so that a member taking only a vararg tail is not read as taking no arguments.
				if (funcDesc.cParamsOpt == -1)
					info.expectedTypes = [];

				return info;
			}

			info.expectedTypes = [.. types];
			info.byRefs = [.. outs];
			return info;
		}

		// Through a pointer to what it points at: many DISP TLBs use VT_PTR -> VT_VARIANT for by-value VARIANTs, and a
		// by-reference SAFEARRAY keeps its element type behind the pointer.
		private static Type TypeOf(TYPEDESC desc) => (VarEnum)desc.vt switch
		{
			VarEnum.VT_PTR when desc.lpValue != 0 => TypeOf(Marshal.PtrToStructure<TYPEDESC>(desc.lpValue)),
			VarEnum.VT_SAFEARRAY when desc.lpValue != 0 => TypeOf(Marshal.PtrToStructure<TYPEDESC>(desc.lpValue)).MakeArrayType(),
			var vt => VariantHelper.VarEnumToCLRType(vt),
		};

		// Derived before base, and a class's default interface before its others, so the first match Resolve meets is
		// the declaration the object's own interface makes.
		private void Collect(ITypeInfo typeInfo, HashSet<Guid> seen, int depth)
		{
			var attr = TypeAttrOf(typeInfo);

			// IUnknown's and IDispatch's own members are never script members, and they end every chain.
			if (attr.guid == Com.IID_IUnknown || attr.guid == Com.IID_IDispatch || (attr.guid != Guid.Empty && !seen.Add(attr.guid)))
				return;

			if (attr.guid == Guid.Empty && ++depth > MaxUnidentifiedDepth)
				return;

			switch (attr.typekind)
			{
				case TYPEKIND.TKIND_COCLASS:
					// The default interface first, then the rest; a source interface is the event sink's, not the object's.
					for (var pass = 0; pass < 2; pass++)
						for (var i = 0; i < attr.cImplTypes; i++)
						{
							typeInfo.GetImplTypeFlags(i, out var flags);

							if ((flags & IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE) == 0 && ((flags & IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT) != 0) == (pass == 0))
								CollectImplType(typeInfo, i, seen, depth);
						}

					break;

				// A dispinterface lists what it inherits; an interface lists its own members, its base as impl type 0.
				case TYPEKIND.TKIND_DISPATCH:
					AddMembers(typeInfo, attr);
					break;

				case TYPEKIND.TKIND_INTERFACE:
					AddMembers(typeInfo, attr);

					for (var i = 0; i < attr.cImplTypes; i++)
						CollectImplType(typeInfo, i, seen, depth);

					break;
			}
		}

		private void CollectImplType(ITypeInfo typeInfo, int index, HashSet<Guid> seen, int depth)
		{
			try
			{
				typeInfo.GetRefTypeOfImplType(index, out var href);
				typeInfo.GetRefTypeInfo(href, out var implemented);

				try
				{
					Collect(implemented, seen, depth);
				}
				finally
				{
					_ = Marshal.ReleaseComObject(implemented);
				}
			}
			catch
			{
				// A type the library cannot resolve or describe, such as one from a library that is not registered, adds nothing.
			}
		}

		private void AddMembers(ITypeInfo typeInfo, TYPEATTR attr)
		{
			foreach (var member in MembersOf(typeInfo, attr))
			{
				if (!members.TryGetValue(member.dispId, out var declared))
					members[member.dispId] = declared = [];

				declared.Add(member);
			}
		}
	}

	/// <summary>
	/// A registered type library, the last resort for a named member an object's own type info does not declare: an
	/// object may answer a DISPID that only another interface in its library declares. The members declaring a DISPID
	/// are read on its first lookup and kept.
	/// </summary>
	internal sealed class ComTypeLibrary(nint pLib)
	{
		private static readonly List<ComMethodInfo> none = [];
		// Guards pLib and typesByDispId; the members found for each DISPID are read without it.
		private readonly Lock gate = new();
		private readonly ConcurrentDictionary<int, List<ComMethodInfo>> membersByDispId = new();
		private Dictionary<int, List<int>> typesByDispId;
		private nint pLib = pLib;

		internal ComMethodInfo Resolve(int dispId, string name, INVOKEKIND kinds)
		{
			if (!membersByDispId.TryGetValue(dispId, out var members))
			{
				try
				{
					members = membersByDispId.GetOrAdd(dispId, MembersDeclaring(dispId));
				}
				catch
				{
					return null;   // as in ComMethodData.ScopeOf; an exception is not kept as a miss
				}
			}

			return ComTypeScope.FirstMatch(members, name, kinds);
		}

		// Teardown; a scope still holding this library then finds nothing more in it.
		internal void Release()
		{
			nint p;

			lock (gate)
			{
				p = pLib;
				pLib = 0;
			}

			if (p != 0)
				_ = Marshal.Release(p);
		}

		private List<ComMethodInfo> MembersDeclaring(int dispId)
		{
			ITypeLib lib = null;

			try
			{
				List<int> types;

				// The RCW holds a reference of its own, so a Release after this cannot free the library under it.
				lock (gate)
				{
					if (pLib == 0 || typesByDispId?.ContainsKey(dispId) == false)
						return none;

					lib = (ITypeLib)Marshal.GetUniqueObjectForIUnknown(pLib);

					if (!(typesByDispId ??= IndexOf(lib)).TryGetValue(dispId, out types))
						return none;
				}

				var members = new List<ComMethodInfo>();

				foreach (var index in types)
				{
					ITypeInfo typeInfo = null;

					try
					{
						lib.GetTypeInfo(index, out typeInfo);
						members.AddRange(ComTypeScope.MembersOf(typeInfo, ComTypeScope.TypeAttrOf(typeInfo), dispId));
					}
					catch
					{
						// As in IndexOf: a type the library cannot describe does not hide the others.
					}
					finally
					{
						if (typeInfo != null)
							_ = Marshal.ReleaseComObject(typeInfo);
					}
				}

				return members;
			}
			finally
			{
				if (lib != null)
					_ = Marshal.ReleaseComObject(lib);
			}
		}

		// Which types declare each DISPID. Only the memid, the first field of a FUNCDESC and of a VARDESC alike, is read, so
		// a library the size of MSHTML's indexes in a fraction of a second.
		private static unsafe Dictionary<int, List<int>> IndexOf(ITypeLib lib)
		{
			var map = new Dictionary<int, List<int>>();

			for (int i = 0, count = lib.GetTypeInfoCount(); i < count; i++)
			{
				ITypeInfo typeInfo = null;

				try
				{
					lib.GetTypeInfo(i, out typeInfo);
					var attr = ComTypeScope.TypeAttrOf(typeInfo);

					for (var j = 0; j < attr.cFuncs; j++)
					{
						typeInfo.GetFuncDesc(j, out var pFuncDesc);
						var memid = *(int*)pFuncDesc;
						typeInfo.ReleaseFuncDesc(pFuncDesc);
						Add(memid, i);
					}

					for (var j = 0; attr.typekind == TYPEKIND.TKIND_DISPATCH && j < attr.cVars; j++)
					{
						typeInfo.GetVarDesc(j, out var pVarDesc);
						var memid = *(int*)pVarDesc;
						typeInfo.ReleaseVarDesc(pVarDesc);
						Add(memid, i);
					}
				}
				catch
				{
					// A type the library cannot describe is not one an object reports.
				}
				finally
				{
					if (typeInfo != null)
						_ = Marshal.ReleaseComObject(typeInfo);
				}
			}

			return map;

			void Add(int memid, int type)
			{
				if (!map.TryGetValue(memid, out var types))
					map[memid] = types = [];

				if (types.Count == 0 || types[^1] != type)
					types.Add(type);
			}
		}
	}
}
#endif
