namespace Keysharp.Tests;

public partial class ExternalTests : TestRunner
{
	[Test, Category("External")]
	public void CallbackCreate() => Assert.IsTrue(TestScript("external-callbackcreate", false));

	[Test, Category("External")]
	public void Clr() => Assert.IsTrue(TestScript("external-clr", false));

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
	private static float SpilledArguments(
		int a, int b, int c, int d, int e, int f, int g, int h, int i, short j, sbyte k, byte l,
		float m, float n, float o, float p, float q, float r, float s, float t, float u, double v)
		=> (float)(a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t + u + v);

	[StructLayout(LayoutKind.Sequential)]
	private struct ShortPair(short first, short second)
	{
		public short First = first;
		public short Second = second;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
	private static long SpilledAggregate(long a, long b, long c, long d, long e, long f, long g, long h,
		sbyte before, ShortPair pair, sbyte after) => before + pair.First + 2 * pair.Second + after;

	[Test, Category("External"), Category("Internal"), Category("Curated")]
	public unsafe void DllCall_StackArguments()
	{
		delegate* unmanaged[Cdecl]<int, int, int, int, int, int, int, int, int, short, sbyte, byte,
			float, float, float, float, float, float, float, float, float, double, float> function = &SpilledArguments;
		var result = Dll.DllCall((long)function,
			"Int", 1L, "Int", 2L, "Int", 3L, "Int", 4L, "Int", 5L, "Int", 6L, "Int", 7L, "Int", 8L,
			"Int", -9L, "Short", -10L, "Char", -11L, "UChar", 200L,
			"Float", 0.25, "Float", 0.5, "Float", 0.75, "Float", 1.0, "Float", 1.25,
			"Float", 1.5, "Float", 1.75, "Float", 2.0, "Float", 2.25, "Double", 0.5, "CDecl Float");
		Assert.That(result, Is.EqualTo(217.75));

		delegate* unmanaged[Cdecl]<long, long, long, long, long, long, long, long, sbyte, ShortPair, sbyte, long> aggregate = &SpilledAggregate;
		var output = RunScript($$"""
			#NoTrayIcon
			#ErrorStdOut
			#Warn All, StdOut
			#Include <assert>
			struct STACK_PAIR {
				First: Int16
				Second: Int16
			}
			pair := STACK_PAIR()
			pair.First := 7
			pair.Second := 11
			AssertEq(DllCall({{(long)aggregate}},
				"Int64", 1, "Int64", 2, "Int64", 3, "Int64", 4,
				"Int64", 5, "Int64", 6, "Int64", 7, "Int64", 8,
				"Char", -1, STACK_PAIR, pair, "Char", -2, "Cdecl Int64"), 26, A_LineNumber)
			FileAppend "pass", "*"
			""", "stack-aggregate", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
	private static unsafe long CallStackCallback(nint address)
	{
		var callback = (delegate* unmanaged[Cdecl]<long, long, long, long, long, long, long, long, int, short, sbyte, byte, long>)address;
		return callback(1, 2, 3, 4, 5, 6, 7, 8, -100, -200, -5, 250);
	}

	[Test, Category("External"), Category("Internal"), Category("Curated")]
	public unsafe void CallbackStackArguments()
	{
		delegate* unmanaged[Cdecl]<nint, long> invoke = &CallStackCallback;
		var output = RunScript($$"""
			#NoTrayIcon
			#ErrorStdOut
			#Warn All, StdOut
			#Include <assert>
			Check(args*) {
				for i, expected in [1, 2, 3, 4, 5, 6, 7, 8, -100, -200, -5, 250]
					AssertEq(args[i], expected, A_LineNumber)
				return args[9] + args[10] + args[11] + args[12]
			}
			callback := CallbackCreate(Check, "Fast C", [Int64, Int64, Int64, Int64, Int64, Int64, Int64, Int64,
				Int32, Int16, Int8, UInt8, Int64])
			AssertEq(DllCall({{(long)invoke}}, "Ptr", callback, "Cdecl Int64"), -55, A_LineNumber)
			CallbackFree(callback)
			FileAppend "pass", "*"
			""", "stack-callback", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
	}

#if WINDOWS

	[Test, Category("External")]
	public void DllCall_()
	{
		var desktop = WindowsAPI.GetDesktopWindow();
		var buf = new Keysharp.Builtins.Buffer(16, 0);
		_ = Dll.DllCall("user32.dll\\GetWindowRect", "ptr", desktop, "ptr", buf);
		_ = WindowsAPI.GetWindowRect((nint)desktop, out var rect);
		var l = (long)NumGet(buf, 0, "UInt");
		var t = (long)NumGet(buf, 4, "UInt");
		var r = (long)NumGet(buf, 8, "UInt");
		var b = (long)NumGet(buf, 12, "UInt");
		Assert.IsTrue(r > 0 && b > 0);
		Assert.That(l, Is.EqualTo(rect.Left));
		Assert.That(r, Is.EqualTo(rect.Right));
		Assert.That(t, Is.EqualTo(rect.Top));
		Assert.That(b, Is.EqualTo(rect.Bottom));
		var str = "lower";
		var len = str.Length;
		var strbuf = new StringBuffer(str);
		_ = Dll.DllCall("user32.dll\\CharUpperBuff", "ptr", strbuf.Ptr, "UInt", len);
		Assert.That(str.ToUpper(), Is.EqualTo(strbuf.ToString()));
		Assert.IsTrue(TestScript("external-dllcall", false));
	}

	[Test, Category("External")]
	public void NumPutNumGet() => Assert.IsTrue(TestScript("external-numput-numget", true));

	[Test, Category("External")]
	public void COM() => Assert.IsTrue(TestScript("external-com", false));

	[Test, Category("External")]
	public void COMEvents() => Assert.IsTrue(TestScript("external-com-events", false));

	[Test, Category("External")]
	public void COMInvoke() => Assert.IsTrue(TestScript("external-com-invoke", false));

	[Test, Category("External"), Category("Curated")]
	public void COMEnumeration() => Assert.IsTrue(TestScript("external-com-atp", false));

	// Native reference counts expose leaks when argument conversion fails before invoking the server.
	[Test, Category("External"), Category("Internal")]
	public void ComPackingFailure()
	{
		Assert.That(Marshal.SizeOf<VARIANT>(), Is.EqualTo(8 + (2 * IntPtr.Size)));
		using var target = (Keysharp.Builtins.COM.ComObject)Keysharp.Builtins.COM.ComObject.staticCall(null, "Scripting.Dictionary");
		using var invalid = new Keysharp.Builtins.COM.ComValue { vt = VarEnum.VT_CY, item = 1.0e30 };
		var pointer = (nint)(long)target.Ptr;
		_ = Marshal.AddRef(pointer);
		var before = Marshal.Release(pointer);

		// The interface is copied first because COM arguments are packed in reverse order.
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		var failure = Assert.Throws<KeysharpException>(() => target.RawInvoke(1, ComTypes.INVOKEKIND.INVOKE_FUNC, [invalid, target], out _));
		Assert.IsInstanceOf<OSError>(failure.UserError);
		Assert.That(((OSError)failure.UserError).Number, Is.EqualTo(0x8002000AL));
		_ = Marshal.AddRef(pointer);
		Assert.That(Marshal.Release(pointer), Is.EqualTo(before));
	}

	[Test, Category("External"), Category("Internal")]
	public void ComSafeArrayLockedStorage()
	{
		using var inner = new ComObjArray(VarEnum.VT_BSTR, 1);
		using var outer = new ComObjArray(VarEnum.VT_VARIANT, 1);
		Assert.IsTrue(outer.TryPutElementAtIndices([0], inner, out var hr));
		Assert.That(hr, Is.Zero);
		var pointer = (nint)(long)outer.Ptr;
		Assert.That(OleAuto.SafeArrayLock(pointer), Is.Zero);
		nint nested;
		try
		{
			Assert.That(OleAuto.SafeArrayPtrOfIndex(pointer, [0], out var cell), Is.Zero);
			nested = VariantHelper.ReadStorage(cell, VarEnum.VT_VARIANT).ptrVal;
		}
		finally { _ = OleAuto.SafeArrayUnlock(pointer); }

		Assert.That(OleAuto.SafeArrayLock(nested), Is.Zero);
		try
		{
			Assert.IsTrue(outer.TryPutElementAtIndices([0], "replacement", out hr));
			Assert.That(hr, Is.EqualTo(unchecked((int)0x8002000D)));
		}
		finally { _ = OleAuto.SafeArrayUnlock(nested); }

		Assert.That(OleAuto.SafeArrayLock(pointer), Is.Zero);
		try
		{
			Assert.That(OleAuto.SafeArrayPtrOfIndex(pointer, [0], out var cell), Is.Zero);
			Assert.That(VariantHelper.ReadStorage(cell, VarEnum.VT_VARIANT).ptrVal, Is.EqualTo(nested));
			outer.Dispose();
			Assert.That(outer.Ptr, Is.EqualTo((long)pointer));
		}
		finally { _ = OleAuto.SafeArrayUnlock(pointer); }

		using var caught = Keysharp.Runtime.Flow.EnterTry();
		_ = Assert.Throws<KeysharpException>(() => outer.GetElementAtIndices([1]));
		outer.Dispose();
		Assert.IsNull(outer.Ptr);
	}

	// Scripting.Dictionary's own interface supplies its members, a nameless lookup finds its default member, and
	// every object of the type shares one scope.
	[Test, Category("External"), Category("Internal")]
	public void ComTypeScopeReadsTheObjectsInterface()
	{
		const ComTypes.INVOKEKIND FuncOrGet = ComTypes.INVOKEKIND.INVOKE_FUNC | ComTypes.INVOKEKIND.INVOKE_PROPERTYGET;
		var data = new Keysharp.Builtins.COM.ComMethodData(null);
		var dictionaries = new[] { NewDictionary(), NewDictionary() };
		var pointers = dictionaries.Select(Marshal.GetIDispatchForObject).ToArray();

		try
		{
			var scope = data.ScopeOf(pointers[0]);
			Assert.That(data.ScopeOf(pointers[1]), Is.SameAs(scope));
			Assert.That(scope.Resolve(1, "add", FuncOrGet)?.expectedTypes.Length, Is.EqualTo(2));   // IDictionary.Add is DISPID 1
			Assert.That(scope.Resolve(0, null, FuncOrGet)?.name, Is.EqualTo("Item"));
			Assert.IsNull(scope.Resolve(1, "NoSuchMember", FuncOrGet));
		}
		finally
		{
			foreach (var p in pointers)
				_ = Marshal.Release(p);

			foreach (var d in dictionaries)
				_ = Marshal.ReleaseComObject(d);

			data.Dispose();
		}

		static object NewDictionary() => Activator.CreateInstance(Type.GetTypeFromProgID("Scripting.Dictionary"));
	}

	// Every _Type.InvokeMember overload in the registered mscorlib library takes (BSTR, BindingFlags, _Binder*, VARIANT,
	// SAFEARRAY(VARIANT), ...): the SAFEARRAY keeps its element type, and the vtable view's [retval] is no caller's. The
	// library, as the last resort, finds an overload by its DISPID and name only.
	[Test, Category("External"), Category("Internal")]
	public void ComTypeScopeReadsDeclaredSafeArrays() =>
		WithRegisteredType(new("BED7F4EA-1A96-11D2-8F08-00A0C9A6186D"), 2, 4, new("BCA8B44D-AAD6-3A86-8AB7-03349F4F2DA2"), (members, library) =>
		{
			var overloads = members.Where(m => m.name.StartsWith("InvokeMember", StringComparison.Ordinal)).ToList();
			Assert.That(overloads.Count, Is.EqualTo(3));
			Assert.IsTrue(overloads.All(m => m.expectedTypes[0] == typeof(string) && m.expectedTypes[4] == typeof(object[])));
			Assert.IsTrue(overloads.Exists(m => m.expectedTypes.Length == 5));

			var first = overloads[0];
			Assert.That(library.Resolve(first.dispId, first.name.ToLowerInvariant(), ComTypes.INVOKEKIND.INVOKE_FUNC)?.expectedTypes.Length, Is.EqualTo(first.expectedTypes.Length));
			Assert.IsNull(library.Resolve(first.dispId, "NoSuchMember", ComTypes.INVOKEKIND.INVOKE_FUNC));
			Assert.IsNull(library.Resolve(0x7FFF0000, first.name, ComTypes.INVOKEKIND.INVOKE_FUNC));
		});

	// stdole's Font dispinterface declares its properties as variables, each read, and assigned by its own type.
	[Test, Category("External"), Category("Internal")]
	public void ComTypeScopeReadsDispinterfaceProperties() =>
		WithRegisteredType(new("00020430-0000-0000-C000-000000000046"), 2, 0, new("BEF6E003-A874-101A-8BBA-00AA00300CAB"), (members, _) =>
		{
			Assert.IsTrue(members.Exists(m => m.name == "Bold" && m.invokeKind == ComTypes.INVOKEKIND.INVOKE_PROPERTYGET));
			Assert.That(members.Single(m => m.name == "Bold" && m.invokeKind == ComTypes.INVOKEKIND.INVOKE_PROPERTYPUT).expectedTypes[0], Is.EqualTo(typeof(bool)));
		});

	// The members of a type in a registered library, and the library as the last resort of a lookup sees it.
	private static void WithRegisteredType(Guid libId, short major, short minor, Guid typeId, Action<List<Keysharp.Builtins.COM.ComMethodInfo>, Keysharp.Builtins.COM.ComTypeLibrary> check)
	{
		if (Keysharp.Builtins.COM.OleAuto.LoadRegTypeLib(in libId, major, minor, 0, out var pLib) < 0)
			Assert.Ignore($"Type library {libId} is not registered.");

		var library = new Keysharp.Builtins.COM.ComTypeLibrary(pLib);
		var lib = (ComTypes.ITypeLib)Marshal.GetUniqueObjectForIUnknown(pLib);
		ComTypes.ITypeInfo typeInfo = null;

		try
		{
			lib.GetTypeInfoOfGuid(ref typeId, out typeInfo);
			check([.. Keysharp.Builtins.COM.ComTypeScope.MembersOf(typeInfo, Keysharp.Builtins.COM.ComTypeScope.TypeAttrOf(typeInfo))], library);
		}
		finally
		{
			if (typeInfo != null)
				_ = Marshal.ReleaseComObject(typeInfo);

			_ = Marshal.ReleaseComObject(lib);
			library.Release();
		}
	}

	// An Array nested twice converts twice, and one nested within itself stays the object where it recurs.
	[Test, Category("External"), Category("Internal")]
	public void SafeArrayElementsOfNestedArrays()
	{
		var inner = new Keysharp.Builtins.Array(1L, 2L);
		var outer = new Keysharp.Builtins.Array(inner, inner);
		_ = outer.Push(outer);
		var elements = Keysharp.Builtins.COM.VariantHelper.SafeArrayElements(outer);

		Assert.That(elements[0], Is.EqualTo(new object[] { 1L, 2L }));
		Assert.That(elements[1], Is.EqualTo(new object[] { 1L, 2L }));
		Assert.That(elements[2], Is.SameAs(outer));
	}

	[Test, Category("External")]
	public void OnMessage() => Assert.IsTrue(TestScript("external-onmessage", false));

#endif
}
