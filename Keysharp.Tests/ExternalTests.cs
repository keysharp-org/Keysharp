using static Keysharp.Builtins.External;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Keysharp.Tests
{
	public partial class ExternalTests : TestRunner
	{
		[Test, Category("External")]
		public void CallbackCreate() => Assert.IsTrue(TestScript("external-callbackcreate", false));

		[Test, Category("External")]
		public void Clr() => Assert.IsTrue(TestScript("external-clr", false));

#if WINDOWS
		[Test, Category("External")]
		public void DllCall_()
		{
			var desktop = WindowsAPI.GetDesktopWindow();
			var rect = new RECT();
			var buf = new Keysharp.Builtins.Buffer(16, 0);
			_ = Dll.DllCall("user32.dll\\GetWindowRect", "ptr", desktop, "ptr", buf);
			_ = WindowsAPI.GetWindowRect((nint)desktop, out rect);
			var l = (long)NumGet(buf, 0, "UInt");
			var t = (long)NumGet(buf, 4, "UInt");
			var r = (long)NumGet(buf, 8, "UInt");
			var b = (long)NumGet(buf, 12, "UInt");
			Assert.IsTrue(r > 0 && b > 0);
			Assert.AreEqual(rect.Left, l);
			Assert.AreEqual(rect.Right, r);
			Assert.AreEqual(rect.Top, t);
			Assert.AreEqual(rect.Bottom, b);
			var str = "lower";
			var len = str.Length;
			var strbuf = new StringBuffer(str);
			_ = Dll.DllCall("user32.dll\\CharUpperBuff", "ptr", strbuf.Ptr, "UInt", len);
			Assert.AreEqual(strbuf.ToString(), str.ToUpper());
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

		// Native reference counts expose leaks when argument conversion fails before invoking the server.
		[Test, Category("External"), Category("Internal")]
		public void ComPackingFailure()
		{
			using var target = (Keysharp.Builtins.COM.ComObject)Keysharp.Builtins.COM.ComObject.staticCall(null, "Scripting.Dictionary");
			using var invalid = new Keysharp.Builtins.COM.ComValue { vt = VarEnum.VT_CY, item = 1.0e30 };
			var pointer = (nint)(long)target.Ptr;
			_ = Marshal.AddRef(pointer);
			var before = Marshal.Release(pointer);

			// The interface is copied first because COM arguments are packed in reverse order.
			Assert.Throws<OverflowException>(() => target.RawInvoke(1, ComTypes.INVOKEKIND.INVOKE_FUNC, [invalid, target], out _));
			_ = Marshal.AddRef(pointer);
			Assert.AreEqual(before, Marshal.Release(pointer));
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
				Assert.AreSame(scope, data.ScopeOf(pointers[1]));
				Assert.AreEqual(2, scope.Resolve(1, "add", FuncOrGet)?.expectedTypes.Length);   // IDictionary.Add is DISPID 1
				Assert.AreEqual("Item", scope.Resolve(0, null, FuncOrGet)?.name);
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
				Assert.AreEqual(3, overloads.Count);
				Assert.IsTrue(overloads.All(m => m.expectedTypes[0] == typeof(string) && m.expectedTypes[4] == typeof(object[])));
				Assert.IsTrue(overloads.Exists(m => m.expectedTypes.Length == 5));

				var first = overloads[0];
				Assert.AreEqual(first.expectedTypes.Length, library.Resolve(first.dispId, first.name.ToLowerInvariant(), ComTypes.INVOKEKIND.INVOKE_FUNC)?.expectedTypes.Length);
				Assert.IsNull(library.Resolve(first.dispId, "NoSuchMember", ComTypes.INVOKEKIND.INVOKE_FUNC));
				Assert.IsNull(library.Resolve(0x7FFF0000, first.name, ComTypes.INVOKEKIND.INVOKE_FUNC));
			});

		// stdole's Font dispinterface declares its properties as variables, each read, and assigned by its own type.
		[Test, Category("External"), Category("Internal")]
		public void ComTypeScopeReadsDispinterfaceProperties() =>
			WithRegisteredType(new("00020430-0000-0000-C000-000000000046"), 2, 0, new("BEF6E003-A874-101A-8BBA-00AA00300CAB"), (members, _) =>
			{
				Assert.IsTrue(members.Exists(m => m.name == "Bold" && m.invokeKind == ComTypes.INVOKEKIND.INVOKE_PROPERTYGET));
				Assert.AreEqual(typeof(bool), members.Single(m => m.name == "Bold" && m.invokeKind == ComTypes.INVOKEKIND.INVOKE_PROPERTYPUT).expectedTypes[0]);
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

			Assert.AreEqual(new object[] { 1L, 2L }, elements[0]);
			Assert.AreEqual(new object[] { 1L, 2L }, elements[1]);
			Assert.AreSame(outer, elements[2]);
		}

		[Test, Category("External")]
		public void OnMessage() => Assert.IsTrue(TestScript("external-onmessage", false));
#endif
	}
}
