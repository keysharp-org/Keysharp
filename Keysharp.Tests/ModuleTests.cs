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
		public void FileIdentity()
		{
			var files = new (string Name, string Source)[]
			{
				("main.ahk", $$"""
					#NoTrayIcon
					#ErrorStdOut
					#Warn All, StdOut
					#Include "{{Path.Combine(path, "Lib", "assert.ahk")}}"

					#Import "Left/Outer" as Left
					#Import "Left/./Outer.ahk" as Left
					#Import "Left/Outer.ahk" as LeftAgain
					#Import "Left/Outer.ahk:__Init" as LeftInit
					#Import "Left/Outer.ahk:" as LeftEmpty
					#Import "Left/Outer.ahk" { Value as SharedValue }
					#Import "Left/./Outer" { Value as SharedValue }
					#Import "Right/Outer.ahk" as Right
					#Import "Left/Outer.ahk:iNnEr" as LeftInner
					#Import "Right/Outer.ahk:Inner" as RightInner
					#Import "Left/Outer.ahk:__Main" as MainThroughFile
					#Import ":__Init" as MainInit
					#Import "" as MainEmpty
					#Import __Main as Main
					#Import "./main.ahk" as MainByPath
					#Import "main.ahk:Named" as NamedByPath
					#Import "Named" as SameFileNamed
					#Import "AHK:" as FileNamedAHK
					#Import AHK as BuiltinAHK
					#Include "Included/Importer.ahk"

					Assert(Left == LeftAgain && Left == LeftInit && Left == LeftEmpty, A_LineNumber)
					Assert(Main == MainInit && Main == MainThroughFile, A_LineNumber)
					Assert(Main == MainByPath && SameFileNamed == NamedByPath, A_LineNumber)
					Assert(Main == MainEmpty && ReadSelf() == Main, A_LineNumber)
					AssertEq(Left.Value, 1, A_LineNumber)
					AssertEq(Right.Value, 2, A_LineNumber)
					Left.Value := 9
					AssertEq(LeftAgain.Value, 9, A_LineNumber)
					AssertEq(SharedValue, 9, A_LineNumber)
					AssertEq(Right.Value, 2, A_LineNumber)
					AssertEq(LeftInner.Value, 101, A_LineNumber)
					AssertEq(RightInner.Value, 202, A_LineNumber)
					AssertEq(Left.InnerValue, 101, A_LineNumber)
					AssertEq(Right.InnerValue, 202, A_LineNumber)
					AssertEq(RelativeTarget.Value, 55, A_LineNumber)
					AssertEq(SameFileNamed.Value, 7, A_LineNumber)
					AssertEq(FileNamedAHK.Value, 505, A_LineNumber)
					Assert(FileNamedAHK != BuiltinAHK, A_LineNumber)
					AssertEq(FromLeftFile(), 303, A_LineNumber)
					AssertEq(Left.AfterReopen, 17, A_LineNumber)
					FileAppend "pass", "*"

					ReadSelf() {
					    #Import "" as Self
					    #Import "" { FromLeftFile as SelfFunction }
					    AssertEq(SelfFunction(), 303, A_LineNumber)
					    return Self
					}

					#Module Named
					Value := 7
					"""),
				("Left/Outer.ahk", """
					#Import Shared as Dependency
					#Import ":Inner" as LocalInner
					Value := Dependency.Value
					InnerValue := LocalInner.Value

					#Module Inner
					Value := 101

					#Module __Main
					FromLeftFile() => 303

					#Module __Init
					AfterReopen := 17
					"""),
				("Right/Outer.ahk", """
					#Import Shared as Dependency
					#Import ":Inner" as LocalInner
					Value := Dependency.Value
					InnerValue := LocalInner.Value

					#Module Inner
					Value := 202
					"""),
				("Left/Shared.ahk", "Value := 1\n"),
				("Right/Shared.ahk", "Value := 2\n"),
				("Named.ahk", "Value := 99\n"),
				("AHK.ahk", "Value := 505\n"),
				("Included/Importer.ahk", "#Import \"IncludedTarget\" as RelativeTarget\n"),
				("Included/IncludedTarget.ahk", "Value := 55\n"),
			};
			var root = Path.Combine(Path.GetTempPath(), "ks-module-files-" + Guid.NewGuid().ToString("N"));
			try
			{
				foreach (var (name, source) in files)
				{
					var file = Path.Combine(root, name);
					Directory.CreateDirectory(Path.GetDirectoryName(file));
					File.WriteAllText(file, source);
				}
				var output = RunScript(Path.Combine(root, "main.ahk"), "module_file_identity", true, false);
				Assert.AreEqual("pass", output.Trim(), output);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, Category("Module")]
		public void Export() => Assert.IsTrue(TestScript("module-export", false));

		[Test, Category("Module")]
		public void Order() => Assert.IsTrue(TestScript("module-order", false));

		[Test, Category("Module")]
		public void CompatibilityMode() => Assert.IsTrue(TestScript("module-compatibility-mode", false));

		[Test, Category("Module")]
		public void ImportUnknownMember()
		{
			// Real Ks members still compile cleanly (the fix must not over-reject) — both a method (Cosh) and a
			// type exposed under a [UserDeclaredName] (Image, whose CLR type is KeysharpImage).
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import \"Ks\" { Cosh }\n"), "a valid built-in method import should not error");
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import KS { Image }\n"), "a valid built-in type import (UserDeclaredName) should not error");

			// A name the module does not expose is rejected, and the diagnostic names the offending member.
			var bad = LoweringDiagnostics.Diagnostics("#import \"Ks\" { NotARealKsMember123 }\n");
			Assert.IsTrue(System.Array.Exists(bad, d => d.Contains("has no exported member") && d.Contains("NotARealKsMember123")),
				"expected a 'has no exported member' diagnostic, got: " + string.Join("; ", bad));
			var removed = LoweringDiagnostics.Diagnostics("#Import Ks { A_InputLevel }\n");
			Assert.IsTrue(System.Array.Exists(removed, d => d.Contains("has no exported member") && d.Contains("A_InputLevel")),
				"A_InputLevel must not be exported by Ks: " + string.Join("; ", removed));
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

		[Test, Category("Module")]
		public void ImportDiagnostics()
		{
			foreach (var source in new[]
			{
				"Foo() {\n#Import KS { Cosh as Shared }\n#Import KS { Sinh as Shared }\n}\n",
				"Foo() {\n#Import KS as Shared\n#Import AHK as Shared\n}\n",
				"Foo() {\nstatic Shared\n#Import KS { Cosh as Shared }\n}\n",
				"Foo() {\nglobal Shared\n#Import KS { Cosh as Shared }\n}\n",
				"#Import Source { Value as Shared }\nShared() => 1\n#Module Source\nValue := 1\n",
				"#Import Source { Value as Shared }\nHolder := Shared(x) => x\n#Module Source\nValue := 1\n",
				"#Import Source { Value as Shared }\nclass Shared {\n}\n#Module Source\nValue := 1\n",
				"#Import Source { Value as Shared }\nglobal Shared\n#Module Source\nValue := 1\n",
				"#Import Source { Value as Shared }\n#Import Relay { Alias as Shared }\n#Module Source\nValue := 1\n#Module Relay\n#Import Source { Value as Alias }\n",
			})
			{
				var conflicts = LoweringDiagnostics.Diagnostics(source);
				Assert.IsTrue(System.Array.Exists(conflicts, d => d.Contains("import declaration conflicts") && d.Contains("Shared")),
					"expected a conflicting import diagnostic, got: " + string.Join("; ", conflicts));
			}

			Assert.IsNotEmpty(LoweringDiagnostics.Diagnostics(
				"#Import Left\n#Module Left\n#Import Right { Y as X }\n#Module Right\n#Import Left { X as Y }\n"),
				"an alias cycle without a declaration must fail at load time");
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics(
				"#Import Left\n#Module Left\n#Import Right { Y as FromRight }\nX := 1\n#Module Right\n#Import Left { X as FromLeft }\nY := 2\n"),
				"a module dependency cycle with declared variables must remain valid");
			Assert.IsEmpty(LoweringDiagnostics.Diagnostics(
				"#Import Source { Value as Shared }\n#Import source { vAlUe as sHaReD }\n#Module Source\nValue := 1\n"),
				"repeating the same explicit binding must remain valid");

			// Writing an imported function is a load-time error (not a silent local), in a function and at module scope.
			foreach (var (source, message) in new[]
			{
				("Foo() {\n#import KS { Cosh }\nCosh := 5\n}\n", "This Func cannot be used as an output variable: Cosh"),
				("Foo() {\n#import KS { Cosh }\nCosh++\n}\n", "This Func cannot be assigned a value: Cosh"),
				("#import KS { Cosh }\nCosh := 5\n", "This Func cannot be used as an output variable: Cosh"),
				("#import KS { Cosh }\nr := &Cosh\n", "This Func cannot have its reference taken: Cosh"),
				("#Import Helper { Add }\nAdd := 1\n#Module Helper\nHolder := Add(x) => x+1\n", "This Func cannot be used as an output variable: Add"),
				("#Import Helper { Add }\nr := &Add\n#Module Helper\nHolder := Add(x) => x+1\n", "This Func cannot have its reference taken: Add"),
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
