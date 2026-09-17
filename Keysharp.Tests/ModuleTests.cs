using Microsoft.CodeAnalysis;
using static Keysharp.Builtins.External;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ModuleTests : TestRunner
	{
		[Test, Category("Module")]
		public void Basic() => Assert.IsTrue(TestScript("module-basic", false));

		[Test, Category("Module")]
		public void Import() => Assert.IsTrue(TestScript("module-import", false));

		[Test, Category("Module")]
		public void ImportFile() => Assert.IsTrue(TestScript("module-import-file", false));

		[Test, Category("Module")]
		public void Export() => Assert.IsTrue(TestScript("module-export", false));

		[Test, Category("Module")]
		public void Order() => Assert.IsTrue(TestScript("module-order", false));

		[Test, Category("Module")]
		public void CompatibilityMode() => Assert.IsTrue(TestScript("module-compatibility-mode", false));

		[Test, Category("Module")]
		public void ImportUnknownMember()
		{
			// Importing a name a built-in module does not expose is a load-time error, as in AutoHotkey.

			// Real Ks members still compile cleanly (the fix must not over-reject) — both a method (Cosh) and a
			// type exposed under a [UserDeclaredName] (Image, whose CLR type is KeysharpImage).
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import \"Ks\" { Cosh }\n"), "a valid built-in method import should not error");
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import KS { Image }\n"), "a valid built-in type import (UserDeclaredName) should not error");

			// A name the module does not expose is rejected, and the diagnostic names the offending member.
			var bad = LoweringDiagnostics.Diagnostics("#import \"Ks\" { NotARealKsMember123 }\n");
			Assert.IsTrue(System.Array.Exists(bad, d => d.Contains("has no exported member") && d.Contains("NotARealKsMember123")),
				"expected a 'has no exported member' diagnostic, got: " + string.Join("; ", bad));
		}

		[Test, Category("Module")]
		public void ScopedImport() => Assert.IsTrue(TestScript("module-scoped-import", false));

		[Test, Category("Module")]
		public void DynamicNames() => Assert.IsTrue(TestScript("module-dynamic-names", false));

		[Test, Category("Module")]
		public void Extends() => Assert.IsTrue(TestScript("module-extends", false));

		[Test, Category("Module")]
		public void ObjectMembers() => Assert.IsTrue(TestScript("module-object-members", false));

		/// <summary>
		/// A script without #Module is one module, and a wildcard import supplies no name it assigns or declares, as in
		/// AutoHotkey: each is the module's own variable, so assigning one leaves the imported module's alone, and one
		/// only declared is unset.
		/// </summary>
		[Test, Category("Module")]
		public void WildcardSkipsOwnVariables() => Assert.IsTrue(TestScript("module-wildcard-own-vars", false));

		// A class of the Ks module or of another script module is a base class only through an import.
		[Test, Category("Module")]
		public void ExtendsNeedsImport()
		{
			foreach (var (src, name) in new[]
			{
				("class X extends HashMap {\n}\n", "HashMap"),
				("class X extends Ks.HashMap {\n}\n", "Ks.HashMap"),
				("class X extends Font {\n}\n", "Font"),
				("class X extends OtherClass {\n}\n#Module Other\nclass OtherClass {\n}\n", "OtherClass"),
				("#Import Other\nclass X extends Other.Nope {\n}\n#Module Other\nclass OtherClass {\n}\n", "Other.Nope"),
				("#Import Other { OtherF }\nclass X extends OtherF {\n}\n#Module Other\nOtherF() => 1\n", "OtherF"),
				// An import that renames the class leaves its own name unbound in the importing module.
				("#Import Relay\nclass X extends Relay.OtherClass {\n}\n#Module Relay\n#Import Other { OtherClass as Renamed }\n#Module Other\nclass OtherClass {\n}\n", "Relay.OtherClass"),
			})
			{
				var diags = LoweringDiagnostics.Diagnostics(src);
				Assert.IsTrue(System.Array.Exists(diags, d => d.Contains("Invalid base class") && d.Contains(name)),
					$"expected an 'Invalid base class' diagnostic for {name}, got: " + string.Join("; ", diags));
			}

			foreach (var src in new[]
			{
				"#Import Ks { HashMap }\nclass X extends HashMap {\n}\n",
				"#Import Ks\nclass X extends Ks.HashMap {\n}\n",
				"#Import Ks { * }\nclass X extends HashMap {\n}\n",
				"class X extends Map {\n}\nclass Y extends Gui.Control {\n}\n",
				"#Import __Main\nclass X extends __Main.Y {\n}\nclass Y {\n}\n",
			})
				Assert.IsEmpty(LoweringDiagnostics.Diagnostics(src), src);
		}

		/// <summary>
		/// A module name reaches a script only through an import, as in AutoHotkey, where a module is named by no
		/// variable until #Import binds one. This asserts on the GENERATED CODE rather than on diagnostics: an
		/// unimported `Ks` produces no diagnostic either way — it is simply an unset global — so a compile-only
		/// check cannot tell the two bindings apart, which is exactly how the leak survived.
		/// </summary>
		[Test, Category("Module")]
		public void ModuleNameNeedsImport()
		{
			static string Emit(string src)
			{
				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
				var unit = lowerer.Build(prog, "Test");
				Assert.IsEmpty(lowerer.Diagnostics, "unexpected diagnostics: " + string.Join("; ", lowerer.Diagnostics));
				return unit.NormalizeWhitespace().ToFullString();
			}

			// Unimported, a module name binds to nothing. Binding it to the Statics entry instead would compile and
			// then fail at run time, because that entry is the members-less class object Script.InitClass leaves
			// behind for a Module type, not the IMetaObject a member access needs.
			foreach (var (name, type) in new[] { ("Ks", "Keysharp.Builtins.Ks"), ("Ahk", "Keysharp.Runtime.Ahk") })
			{
				var bare = Emit($"x := {name}.App\n");
				Assert.IsTrue(bare.Contains($"{name.ToLowerInvariant()} = null"),
							  $"an unimported {name} must bind to null, got: " + bare);
				Assert.IsFalse(bare.Contains($"Statics[typeof({type})]"),
							   $"an unimported {name} must not bind to the Statics class object");
			}

			// Imported, the name binds to the module OBJECT, which dispatches member access through IMetaObject.
			Assert.IsTrue(Emit("#import KS\nx := Ks.App\n").Contains("new Keysharp.Builtins.Ks()"),
						  "an imported Ks must bind to the module object");
		}

		[Test, Category("Module")]
		public void ImportDiagnostics()
		{
			// Writing an imported function is a load-time error (not a silent local), in a function and at module scope.
			foreach (var (source, message) in new[]
			{
				("Foo() {\n#import KS { Cosh }\nCosh := 5\n}\n", "This Func cannot be used as an output variable: Cosh"),
				("Foo() {\n#import KS { Cosh }\nCosh++\n}\n", "This Func cannot be assigned a value: Cosh"),
				("#import KS { Cosh }\nCosh := 5\n", "This Func cannot be used as an output variable: Cosh"),
				("#import KS { Cosh }\nr := &Cosh\n", "This Func cannot have its reference taken: Cosh"),
				// A #CSharp method is a function whether or not the module exports it.
				("#Import Helper { CsMethod }\nCsMethod := 1\n#Module Helper\n#CSharp\npublic static object CsMethod() => 1;\n#EndCSharp\n",
					"This Func cannot be used as an output variable: CsMethod"),
			})
			{
				var wr = LoweringDiagnostics.Diagnostics(source);
				Assert.IsTrue(System.Array.Exists(wr, d => d.Contains(message)), $"expected '{message}', got: " + string.Join("; ", wr));
			}

			// Built-in properties with public setters write through explicit imports; getter-only properties remain
			// protected just like imported functions and types.
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("Foo() {\n#import KS { A_PeekFrequency }\nA_PeekFrequency := 10\n}\n"),
				"a writable KS property import should accept assignment");
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("Foo() {\n#import AHK { A_DetectHiddenWindows }\nA_DetectHiddenWindows := true\n}\n"),
				"a writable AHK property import should accept assignment");
			var readOnly = LoweringDiagnostics.Diagnostics("Foo() {\n#import KS { A_KsVersion }\nA_KsVersion := \"invalid\"\n}\n");
			Assert.IsTrue(System.Array.Exists(readOnly, d => d.EndsWith("This built-in variable cannot be used as an output variable: A_KsVersion")),
				"expected a read-only built-in property assignment diagnostic, got: " + string.Join("; ", readOnly));
			// A custom AHK module's variable is writable although a built-in function has its name.
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import AHK { StrLen }\nStrLen := 5\n#Module AHK\nStrLen := 1\n"),
				"a custom AHK module's variable import should accept assignment");
			// A scoped import names a module, as a module-scope one does.
			var notModule = LoweringDiagnostics.Diagnostics("Foo() {\n#import Array { Length }\n}\n");
			Assert.IsTrue(System.Array.Exists(notModule, d => d.Contains("module not found: Array")),
				"expected a scoped 'module not found' diagnostic, got: " + string.Join("; ", notModule));

			// A bad member name is still rejected eagerly when the import sits inside a function body (nested position).
			var nested = LoweringDiagnostics.Diagnostics("Foo() {\n#import \"Ks\" { NotARealKsMember123 }\nreturn NotARealKsMember123\n}\n");
			Assert.IsTrue(System.Array.Exists(nested, d => d.Contains("has no exported member") && d.Contains("NotARealKsMember123")),
				"expected a nested-position 'has no exported member' diagnostic, got: " + string.Join("; ", nested));

			// A well-formed scoped import produces no diagnostics (and no false #Warn-unset baked into codegen).
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("Foo() {\n#import KS { Cosh }\nreturn Cosh(0)\n}\nFoo()\n"),
				"a valid function-scoped import should not error");
		}

		[Test, Category("Module")]
		public void ModuleInClass()
		{
			// `#Module` is only meaningful at the top level; inside a class body it is a parse error, not a silent drop.
			var (_, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("class C {\n#Module Nope\n}\n");
			Assert.IsTrue(System.Array.Exists(diags.ToArray(), d => d.Contains("#Module")),
				"expected a '#Module' parse diagnostic, got: " + string.Join("; ", diags));
		}
	}
}
