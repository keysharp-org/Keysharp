using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class ClassTests : TestRunner
	{
		[Test, Category("Class")]
		public void ClassBasic() => Assert.IsTrue(TestScript("class", false));

		[Test, Category("Class")]
		public void ClassWithStaticVar() => Assert.IsTrue(TestScript("class-static", false));

		[Test, Category("Class")]
		public void ClassWithMemberFuncs() => Assert.IsTrue(TestScript("class-member-funcs", false));

		[Test, Category("Class")]
		public void ClassExtends() => Assert.IsTrue(TestScript("class-extends", false));

		[Test, Category("Class")]
		public void ClassParams() => Assert.IsTrue(TestScript("class-params", false));

		[Test, Category("Class")]
		public void ClassProperties() => Assert.IsTrue(TestScript("class-props", false));

		[Test, Category("Class")]
		public void ClassOwnProperties() => Assert.IsTrue(TestScript("class-ownprops", false));

		[Test, Category("Class")]
		public void ClassSpecialFunctions()
		{
			Assert.IsTrue(TestScript("class-special-funcs", false));
		}

		[Test, Category("Class")]
		public void ClassPrototype() => Assert.IsTrue(TestScript("class-prototype", false));

		[Test, Category("Class")]
		public void ClassNested() => Assert.IsTrue(TestScript("class-nested", false));

		[Test, Category("Class")]
		public void BuiltInTypeVisibility() => Assert.IsTrue(TestScript("class-builtin-visibility", false));

		[Test, Category("Class")]
		public void StructBasic() => Assert.IsTrue(TestScript("struct-basic", false));

		[Test, Category("Class")]
		public void StructPack() => Assert.IsTrue(TestScript("struct-pack", false));

		[Test, Category("Class")]
		public void StructArray() => Assert.IsTrue(TestScript("struct-array", false));

		// A struct extends only a struct class and a class only a class which is not one, reported at the class's line.
		[Test, Category("Class")]
		public void InvalidBaseClass()
		{
			foreach (var (src, name, line) in new[]
			{
				("struct S {\n\ta : Int32\n}\nclass C extends S {\n}\n", "S", 4),
				("class C {\n}\nstruct S extends C {\n\ta : Int32\n}\n", "C", 3),
				("class C extends Struct {\n}\n", "Struct", 1),
				("class C extends Int32 {\n}\n", "Int32", 1),
				("struct S extends Object {\n\ta : Int32\n}\n", "Object", 1),
				("struct S {\n\ta : Int32\n}\nclass Outer {\n\tclass Inner extends S {\n\t}\n}\n", "S", 5),
				("class Outer {\n\tclass Inner extends Nope {\n\t}\n}\n", "Nope", 2),
			})
			{
				var diags = LoweringDiagnostics.Diagnostics(src);
				Assert.IsTrue(System.Array.Exists(diags, d => d.StartsWith($"{line}:") && d.EndsWith($"Invalid base class: {name}")),
					$"expected an 'Invalid base class' diagnostic for {name} at line {line}, got: " + string.Join("; ", diags));
			}

			foreach (var src in new[]
			{
				"struct S {\n\ta : Int32\n}\nstruct T extends S {\n\tb : Int32\n}\nstruct U extends Int32 {\n}\nstruct V extends Struct {\n\tc : Int32\n}\n",
				"class C {\n}\nclass D extends C {\n}\nclass E extends Map {\n}\n",
				"struct S {\n\ta : Int32\n}\nclass Outer {\n\tstruct Inner extends S {\n\t}\n}\n",
			})
				Assert.IsEmpty(LoweringDiagnostics.Diagnostics(src), src);
		}
	}
}
