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
			// Importing a name a built-in module does not expose (e.g. `#import "Ks" { OCR }` — there is no
			// Ks.OCR) was silently dropped, so the directive compiled and the missing name surfaced only later.
			// It must be a compile-time error instead, matching AutoHotkey's load-time error for the same case.
			static string[] Lower(string src)
			{
				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
				_ = lowerer.Build(prog, "Test");
				return lowerer.Diagnostics.ToArray();
			}

			// Real Ks members still compile cleanly (the fix must not over-reject) — both a method (Cosh) and a
			// type exposed under a [UserDeclaredName] (Image, whose CLR type is KeysharpImage).
			Assert.IsEmpty(Lower("#import \"Ks\" { Cosh }\n"), "a valid built-in method import should not error");
			Assert.IsEmpty(Lower("#import KS { Image }\n"), "a valid built-in type import (UserDeclaredName) should not error");

			// A name the module does not expose is rejected, and the diagnostic names the offending member.
			var bad = Lower("#import \"Ks\" { NotARealKsMember123 }\n");
			Assert.IsTrue(System.Array.Exists(bad, d => d.Contains("has no exported member") && d.Contains("NotARealKsMember123")),
				"expected a 'has no exported member' diagnostic, got: " + string.Join("; ", bad));
		}

		[Test, Category("Module")]
		public void ScopedImport() => Assert.IsTrue(TestScript("module-scoped-import", false));

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
			static string[] Lower(string src)
			{
				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
				_ = lowerer.Build(prog, "Test");
				return lowerer.Diagnostics.ToArray();
			}

			// Assigning to an imported function/type inside a function is a load-time error (not a silent local).
			var wr = Lower("Foo() {\n#import KS { Cosh }\nCosh := 5\n}\n");
			Assert.IsTrue(System.Array.Exists(wr, d => d.Contains("cannot assign to imported name") && d.Contains("Cosh")),
				"expected a 'cannot assign to imported name' diagnostic, got: " + string.Join("; ", wr));

			// Built-in properties with public setters write through explicit imports; getter-only properties remain
			// protected just like imported functions and types.
			Assert.IsEmpty(Lower("Foo() {\n#import KS { A_PeekFrequency }\nA_PeekFrequency := 10\n}\n"),
				"a writable KS property import should accept assignment");
			Assert.IsEmpty(Lower("Foo() {\n#import AHK { A_DetectHiddenWindows }\nA_DetectHiddenWindows := true\n}\n"),
				"a writable AHK property import should accept assignment");
			var readOnly = Lower("Foo() {\n#import KS { A_KsVersion }\nA_KsVersion := \"invalid\"\n}\n");
			Assert.IsTrue(System.Array.Exists(readOnly, d => d.Contains("cannot assign to imported name") && d.Contains("A_KsVersion")),
				"expected a read-only built-in property assignment diagnostic, got: " + string.Join("; ", readOnly));

			// A bad member name is still rejected eagerly when the import sits inside a function body (nested position).
			var nested = Lower("Foo() {\n#import \"Ks\" { NotARealKsMember123 }\nreturn NotARealKsMember123\n}\n");
			Assert.IsTrue(System.Array.Exists(nested, d => d.Contains("has no exported member") && d.Contains("NotARealKsMember123")),
				"expected a nested-position 'has no exported member' diagnostic, got: " + string.Join("; ", nested));

			// A well-formed scoped import produces no diagnostics (and no false #Warn-unset baked into codegen).
			Assert.IsEmpty(Lower("Foo() {\n#import KS { Cosh }\nreturn Cosh(0)\n}\nFoo()\n"),
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
