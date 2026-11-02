namespace Keysharp.Tests;

public partial class ModuleTests : TestRunner
{
	[Test, Category("Module")]
	public void Basic() => Assert.IsTrue(TestScript("module-basic", false));

	[Test, Category("Module")]
	public void CompatibilityMode() => Assert.IsTrue(TestScript("module-compatibility-mode", false));

	[Test, Category("Module")]
	public void DynamicNames() => Assert.IsTrue(TestScript("module-dynamic-names", false));

	[Test, Category("Module")]
	public void Export() => Assert.IsTrue(TestScript("module-export", false));

	[Test, Category("Module")]
	public void Extends() => Assert.IsTrue(TestScript("module-extends", false));

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
			// A module is not a class, however it is named.
			("#Import Ks\nclass X extends Ks.DotNet {\n}\n", "Ks.DotNet"),
		})
			AssertDiagnostic(src, "Invalid base class", name);

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

	// That lowering the source reports one diagnostic holding every text.
	private static void AssertDiagnostic(string source, params string[] texts)
	{
		var diagnostics = LoweringDiagnostics.Diagnostics(source);
		Assert.IsTrue(System.Array.Exists(diagnostics, d => texts.All(text => d.Contains(text))),
			$"expected '{string.Join("', '", texts)}' for {source}, got: " + string.Join("; ", diagnostics));
	}

	// The same of parsing, for a diagnostic that stops the parse, which LoweringDiagnostics asserts against.
	private static void AssertParseDiagnostic(string source, string text)
	{
		var (_, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
		Assert.IsTrue(diagnostics.Any(d => d.Contains(text)), $"expected '{text}' for {source}, got: " + string.Join("; ", diagnostics));
	}

	private static string TempRoot(string tag) => Path.Combine(Path.GetTempPath(), "ks-module-" + tag + "-" + Guid.NewGuid().ToString("N"));

	// Writes a file set below a new temporary folder and runs its main.ahk with that folder as the working directory.
	private void RunFiles(string root, string name, (string Name, string Source)[] files)
	{
		var workingDirectory = Environment.CurrentDirectory;
		try
		{
			foreach (var (fileName, source) in files)
			{
				var file = Path.Combine(root, fileName);
				_ = Directory.CreateDirectory(Path.GetDirectoryName(file));
				File.WriteAllText(file, source);
			}
			Environment.CurrentDirectory = root;
			var output = RunScript(Path.Combine(root, "main.ahk"), name, true, false);
			Assert.That(output.Trim(), Is.EqualTo("pass"), output);
		}
		finally
		{
			Environment.CurrentDirectory = workingDirectory;
			try { Directory.Delete(root, true); }
			catch { }
		}
	}

	[Test, Category("Module")]
	public void FileIdentity()
	{
		var root = TempRoot("files");
		// Empty where paths have no drive, which leaves the same imports without a drive-relative path to test.
		var drive = Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar);
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
				#Import "Left/Outer.ahk:Inner:Deep" as LeftDeep
				#Import "Right/Outer.ahk:Inner:Deep" as RightDeep
				#Import "Left/Outer.ahk:RootChild" as LeftRootChild
				#Import "Left/Outer.ahk:RootChild:Grandchild" as LeftGrandchild
				#Import Left/Outer:Inner:Deep
				#Import "Left/Outer.ahk:Other:Nested" as LeftNested
				#Import "Right/Outer.ahk:Other:Nested" as RightNested
				#Import "{{Path.Combine(root, "Left", "Outer.ahk")}}:Inner:Deep" as RootedDeep
				#Import "Other:Sub" as OtherFileSub
				#Import "{{drive}}Other:Sub" as DriveRelativeSub
				#Import "{{drive}}Other:Sub:Deep" as DriveRelativeDeep
				#Import "{{drive}}BareFile:" as DriveRelativeRoot
				#Import Other:Nested as OwnNested
				#Import ":OwnRootChild" as OwnRootChild
				#Import __Main:OwnRootChild as SameOwnRootChild
				#Import "main.ahk:OwnRootChild" as OwnRootChildByPath
				#Import BareFile
				#Import __Main:FromFile
				#Import DotNet as UserDotNet
				#Import Ks as BuiltinKs
				#Import Ks:DotNet as BuiltinDotNet
				#Import "Ks:DotNet" as QuotedDotNet
				#Import "Ks.ahk:DotNet" as FileDotNet
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
				AssertEq(LeftDeep.Value, 111, A_LineNumber)
				AssertEq(RightDeep.Value, 222, A_LineNumber)
				AssertEq(Deep.Value, 111, A_LineNumber)
				AssertEq(LeftRootChild.Value, 121, A_LineNumber)
				AssertEq(LeftGrandchild.Value, 122, A_LineNumber)
				AssertEq(Left.Child.Value, 121, A_LineNumber)
				AssertEq(LeftNested.Value, 131, A_LineNumber)
				AssertEq(RightNested.Value, 232, A_LineNumber)
				Assert(LeftNested != RightNested && LeftNested != OwnNested, A_LineNumber)
				Assert(RootedDeep == LeftDeep, A_LineNumber)
				AssertEq(OtherFileSub.Value, 606, A_LineNumber)
				Assert(DriveRelativeSub == OtherFileSub, A_LineNumber)
				AssertEq(DriveRelativeDeep.Value, 616, A_LineNumber)
				AssertEq(DriveRelativeRoot.Value, 909, A_LineNumber)
				AssertEq(OwnNested.Value, 707, A_LineNumber)
				AssertEq(OwnRootChild.Value, 808, A_LineNumber)
				Assert(OwnRootChild == SameOwnRootChild && OwnRootChild == OwnRootChildByPath, A_LineNumber)
				AssertEq(BareFile.Value, 909, A_LineNumber)
				AssertEq(FromFile.Value, 404, A_LineNumber)
				AssertEq(Left.MainChildValue, 808, A_LineNumber)
				; A nested built-in is no bare name, so a user's own file is found by one, and the built-in wins over Ks.ahk.
				AssertEq(UserDotNet.Value, 1001, A_LineNumber)
				AssertEq(BuiltinDotNet.Format("{1,3}", 7), "  7", A_LineNumber)
				Assert(BuiltinDotNet == BuiltinKs.DotNet && BuiltinDotNet == QuotedDotNet, A_LineNumber)
				AssertEq(FileDotNet.Value, 1002, A_LineNumber)
				FileAppend "pass", "*"

				ReadSelf() {
				    #Import "" as Self
				    #Import "" { FromLeftFile as SelfFunction }
				    AssertEq(SelfFunction(), 303, A_LineNumber)
				    return Self
				}

				#Module Named
				Value := 7

				#Module Other
				Value := 700

				#Module Other:Nested
				Value := 707

				#Module Holder:BareFile
				Value := -909

				#Module __Init:OwnRootChild
				Value := 808
				"""),
			("Left/Outer.ahk", """
				#Import Shared as Dependency
				#Import ":Inner" as LocalInner
				#Import __Init:RootChild as Child
				#Import __Main:OwnRootChild as MainChild
				Value := Dependency.Value
				InnerValue := LocalInner.Value
				MainChildValue := MainChild.Value

				#Module Inner
				Value := 101

				#Module Inner:Deep
				Value := 111

				#Module __Init:RootChild
				Value := 121

				#Module __Init:RootChild:Grandchild
				Value := 122

				#Module Other:Nested
				Value := 131

				#Module __Main:FromFile
				Value := 404

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

				#Module Inner:Deep
				Value := 222

				#Module Other:Nested
				Value := 232
				"""),
			("Left/Shared.ahk", "Value := 1\n"),
			("Right/Shared.ahk", "Value := 2\n"),
			("Named.ahk", "Value := 99\n"),
			("AHK.ahk", "Value := 505\n"),
			("Other.ahk", "#Module Sub\nValue := 606\n#Module Sub:Deep\nValue := 616\n"),
			("BareFile.ahk", "Value := 909\n"),
			("DotNet.ahk", "Value := 1001\n"),
			("Ks.ahk", "#Module DotNet\nValue := 1002\n"),
			("Included/Importer.ahk", "#Import \"IncludedTarget\" as RelativeTarget\n"),
			("Included/IncludedTarget.ahk", "Value := 55\n"),
		};
		RunFiles(root, "module_file_identity", files);
	}

	[Test, Category("Module")]
	public void Import() => Assert.IsTrue(TestScript("module-import", false));

	// A module's body can come from several files, and each file's definitions take their mode through its own #Includes.
	[Test, Category("Module")]
	public void ModesAcrossFiles()
	{
		const string StartupProbe = """
			try {
			    Map().Get("absent")
			    Startup := "v2.1"
			} catch
			    Startup := "v2.0"
			""";
		RunFiles(TempRoot("modes"), "module_modes_across_files",
		[
			("main.ahk", $$"""
				#NoTrayIcon
				#ErrorStdOut
				#Warn All, StdOut
				#Requires AutoHotkey v2.1-alpha
				#Include "{{Path.Combine(path, "Lib", "assert.ahk")}}"
				#Import "Lib"
				#Import "Reopener"
				#Import __Main:Helpers
				DefinedMode(NoReturn) {
				    try {
				        returned := NoReturn()
				        return "v2.0"
				    } catch UnsetError
				        return "v2.1"
				}
				AssertEq(DefinedMode(Helpers.ThroughInclude), "v2.0", A_LineNumber)
				; Only a #Requires in the file that declares a module sets the module's mode.
				AssertEq(Helpers.Startup, "v2.1", A_LineNumber)
				; As in AutoHotkey, this file's #Requires does not reach __Main's section of another file.
				AssertEq(DefinedMode(MainSectionInLib), "v2.0", A_LineNumber)
				FileAppend "pass", "*"
				"""),
			("Lib.ahk", $$"""
				#Module __Main:Helpers
				#Include "Part.ahk"
				{{StartupProbe}}

				#Module __Main
				MainSectionInLib() {
				}
				"""),
			("Reopener.ahk", "#Module __Main:Helpers\n#Requires AutoHotkey v2.0\n"),
			("Part.ahk", "#Requires AutoHotkey v2.0\n#Include \"Deep.ahk\"\n"),
			("Deep.ahk", "ThroughInclude() {\n}\n"),
		]);
	}

	[Test, Category("Module")]
	public void SearchPaths()
	{
		var root = TempRoot("search");
		var previousPath = Environment.GetEnvironmentVariable("AhkLibPath");
		var files = new (string Name, string Source)[]
		{
			("main.ahk", $$"""
				#NoTrayIcon
				#ErrorStdOut
				#Warn All, StdOut
				#Include "{{Path.Combine(path, "Lib", "assert.ahk")}}"
				#Import "Package/Outer" as Outer
				#Import "Package/Outer:Named" as Named
				#Import EnvOnly as MainEnv
				#Import "Exact.raw" as Exact
				#Import Folder
				#Import Modes:Modern
				#Import Modes:Legacy
				#Import __Main:Spelled
				AssertEq(Outer.FromMain, 11, A_LineNumber)
				AssertEq(Outer.FromEnv, 22, A_LineNumber)
				AssertEq(Outer.FromLine, 33, A_LineNumber)
				AssertEq(Outer.FromIncluded, 44, A_LineNumber)
				AssertEq(Outer.FromLibrary, 55, A_LineNumber)
				AssertEq(Named.Value, 22, A_LineNumber)
				AssertEq(MainEnv.Value, 66, A_LineNumber)
				AssertEq(Exact.Value, 77, A_LineNumber)
				AssertEq(Folder.Value, 88, A_LineNumber)
				AssertEq(Modern.Probe, "local", A_LineNumber)
				AssertEq(Legacy.Probe, "library", A_LineNumber)
				AssertEq(Spelled.Probe, "local", A_LineNumber)
				FileAppend "pass", "*"

				#Module Modes:Modern
				#Requires AutoHotkey v2.1-alpha.35
				#Include <ModeProbe>

				#Module Modes:Legacy
				#Include <ModeProbe>

				#Module __Main:Spelled
				#Requires AutoHotkey v2.1-alpha.35

				#Module __Init:Spelled
				#Include <SpelledProbe>
				"""),
			("Package/Outer.ahk", """
				#Include "included/Importer.ahk"
				#Include <ModuleLibrary>
				#Import MainOnly
				#Import EnvOnly
				FromMain := MainOnly.Value
				FromEnv := EnvOnly.Value
				FromLine := LineOnly.Value
				FromIncluded := IncludedOnly.Value
				FromLibrary := LibraryValue
				#Module Named
				#Import EnvOnly
				Value := EnvOnly.Value
				"""),
			("Package/included/Importer.ahk", "#Import IncludedOnly\n#Import LineOnly\n"),
			("Package/included/IncludedOnly.ahk", "Value := 44\n"),
			("Package/IncludedOnly.ahk", "Value := -44\n"),
			("MainOnly.ahk", "Value := 11\n"),
			("Package/Libraries/MainOnly.ahk", "Value := -11\n"),
			("Libraries/EnvOnly.ahk", "Value := 66\n"),
			("Package/Libraries/EnvOnly.ahk", "Value := 22\n"),
			("Package/LineLibraries/LineOnly.ahk", "Value := 33\n"),
			("Package/included/LineLibraries/LineOnly.ahk", "Value := -33\n"),
			("Package/Lib/ModuleLibrary.ahk", "LibraryValue := 55\n"),
			("Lib/ModuleLibrary.ahk", "LibraryValue := -55\n"),
			("Exact.raw", "Value := 77\n"),
			("Exact.raw.ahk", "Value := -77\n"),
			("Folder/__Init.ahk", "Value := 88\n"),
			("Folder.ahk", "Value := -88\n"),
			// A sibling's #Requires leaves Modes:Legacy in v2.0 mode, where <ModeProbe> skips the local file.
			("ModeProbe", "Probe := \"local\"\n"),
			("Lib/ModeProbe.ahk", "Probe := \"library\"\n"),
			// __Main and __Init name one module in the main file, so a #Requires under either spelling is its mode.
			("SpelledProbe", "Probe := \"local\"\n"),
			("Lib/SpelledProbe.ahk", "Probe := \"library\"\n"),
		};
		try
		{
			foreach (var (name, source) in files)
			{
				var file = Path.Combine(root, name);
				_ = Directory.CreateDirectory(Path.GetDirectoryName(file));
				File.WriteAllText(file, "#ErrorStdOut\n#Warn All, StdOut\n" + source);
			}
			Environment.SetEnvironmentVariable("AhkLibPath", "Missing;Libraries;MissingAgain;Libraries;%A_LineFile%\\..\\LineLibraries");
			var output = RunScript(Path.Combine(root, "main.ahk"), "module_search_paths", true, false);
			Assert.That(output.Trim(), Is.EqualTo("pass"), output);
		}
		finally
		{
			Environment.SetEnvironmentVariable("AhkLibPath", previousPath);
			try { Directory.Delete(root, true); }
			catch { }
		}
	}

	[Test, Category("Module")]
	public void ImportDiagnostics()
	{
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("Foo() {\n#Import Export { Value }\n#Import Export as Source\nreturn Value\n}\n#Module Export\nValue := 1\n"),
			"Export remains a valid module name inside a function");

		foreach (var source in new[]
		{
			"Foo() {\n#Import KS { Cosh as Shared }\n#Import KS { Sinh as Shared }\n}\n",
			"Foo() {\n#Import KS as Shared\n#Import AHK as Shared\n}\n",
			"Foo() {\nstatic Shared\n#Import KS { Cosh as Shared }\n}\n",
			"Foo() {\nglobal Shared\n#Import KS { Cosh as Shared }\n}\n",
			"Foo() {\nlocal Shared\n#Import KS { Cosh as Shared }\n}\n",
			"Foo() {\n#Import KS { Cosh as Shared }\nlocal sHaReD := 1\n}\n",
			"Foo() {\n#Import KS as Shared\nif true {\nlocal Shared\n}\n}\n",
			"Foo(Shared) {\n#Import KS { Cosh as Shared }\n}\n",
			"Foo(&Shared) {\n#Import KS as Shared\n}\n",
			"Foo(Shared*) {\n#Import KS { Cosh as Shared }\n}\n",
			"Foo() {\n#Import KS { Cosh as Shared }\nShared() => 1\n}\n",
			"Foo() {\n#Import KS { Cosh as Shared }\nHolder := Shared(x) => x\n}\n",
			"#Import Source { Value as Shared }\nShared() => 1\n#Module Source\nValue := 1\n",
			"#Import Source { Value as Shared }\nHolder := Shared(x) => x\n#Module Source\nValue := 1\n",
			"#Import Source { Value as Shared }\nclass Shared {\n}\n#Module Source\nValue := 1\n",
			"#Import Source { Value as Shared }\nglobal Shared\n#Module Source\nValue := 1\n",
			"#Import Source { Value as Shared }\n#Import Relay { Alias as Shared }\n#Module Source\nValue := 1\n#Module Relay\n#Import Source { Value as Alias }\n",
			"#Import Ks:DotNet as Shared\n#Import Ks as Shared\n",
			"#Import Ks:DotNet { Format as Shared }\n#Import Ks { DotNet as Shared }\n",
			"#Import Other { Nested as Shared }\n#Import Other:Nested as Shared\n#Module Other\n#Import Other:Nested\n#Module Other:Nested\n",
			// A global declaration at any depth declares the module's variable, which the import then conflicts with.
			"#Import Source { Value as Shared }\nFoo() {\nglobal Shared\n}\n#Module Source\nValue := 1\n",
			"#Import Source { Value as Shared }\nclass Holder {\nstatic Foo() {\nglobal Shared\n}\n}\n#Module Source\nValue := 1\n",
			"#Import Ks:DotNet as Shared\nFoo() {\nglobal Shared\nreturn Shared\n}\n",
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
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Import Ks:DotNet { Format, RegExMatch }\n#Import Ks { DotNet }\n#Import Ks:DotNet\nDotNet.Format(\"{1}\", 1)\n"),
			"a path and a member of the parent bind one nested module");
		// A global declaration of a built-in variable names the built-in, so it declares nothing for an import to meet.
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Import Source { Value as A_WorkingDir }\nFoo() {\nglobal A_WorkingDir\n}\n#Module Source\nValue := 1\n"),
			"a global declaration of a built-in variable must not conflict with an import");

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

		// A module path names an existing module, and a nested module is no name of its group.
		foreach (var (source, specifier) in new[]
		{
			("#Import Nested\n#Module Other:Nested\n", "Nested"),
			("#Import Other:Nope\n#Module Other:Nested\n", "Other:Nope"),
			("#Import Other:Nested:Nope\n#Module Other:Nested\n", "Other:Nested:Nope"),
			("#Import Ks:Nope\n", "Ks:Nope"),
			("#Import \"Ks:Nope\"\n", "Ks:Nope"),
			("Foo() {\n#Import Other:Nope\n}\n#Module Other\n", "Other:Nope"),
			// A child of a module is not found through a trailing colon or from the group's root.
			("#Import \"Other:Nested:\"\n#Module Other:Nested\n", "Other:Nested:"),
			("#Import \":Nested\"\n#Module Other:Nested\n", ":Nested"),
			// A built-in's nested module has only its path, and only the module declaring it is its parent.
			("#Import DotNet\n", "DotNet"),
			("#Import AHK:DotNet\n", "AHK:DotNet"),
			("#Import Ks:DotNet:Nope\n", "Ks:DotNet:Nope"),
			// A script's own Ks takes the name from the built-in, so only its own children are found through it.
			("#Import Ks:DotNet\n#Module Ks\n", "Ks:DotNet"),
		})
			AssertDiagnostic(source, "#Import: module not found: " + specifier);

		// An unquoted nested name takes no spaces, and an empty segment is no name. Each stops the parse.
		foreach (var source in new[] { "#Import Other : Nested\n", "#Import Other: Nested\n", "#Import Other :Nested\n", "#Import Other\n:Nested\n",
			"#Import Other:\n", "#Import Other::Nested\n", "#Import Other:1\n" })
			AssertParseDiagnostic(source, "a nested module name is written A:B");

		// A #Module name follows AutoHotkey's rule for each segment, and only a script's own module can be a parent.
		foreach (var (source, message) in new[]
		{
			("#Module :X\n", "a nested module name is written A:B"),
			("#Module X:\n", "a nested module name is written A:B"),
			("#Module X : Y\n", "a nested module name is written A:B"),
			("#Module X::Y\n", "a nested module name is written A:B"),
			("#Module\n", "Parameter #1 required"),
			("#Module AHK:X\n", "a built-in module cannot be a parent: AHK"),
			("#Module AHK\n#Module AHK:X\n", "a built-in module cannot be a parent: AHK"),
			("#Module Ks:X\n", "a built-in module cannot be a parent: Ks"),
			("#Module Ks:DotNet\n", "a built-in module cannot be a parent: Ks"),
			("#Module 1a\n", "This module name starts with a number, which is not allowed: \"1a\""),
			("#Module X:1a\n", "This module name starts with a number, which is not allowed: \"1a\""),
			("#Module a-b\n", "The following module name contains an illegal character: \"a-b\""),
			("#Module X:a-b\n", "The following module name contains an illegal character: \"a-b\""),
			("#Module If\n", "The following reserved word must not be used as a module name: \"If\""),
			("#Module X:unset\n", "The following reserved word must not be used as a module name: \"unset\""),
		})
			AssertDiagnostic(source, message);

		// A script's own module is an ordinary parent although a built-in module has its name, whichever directive comes first.
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Module Ks\n#Module Ks:X\n"), "a script's own #Module Ks can be a parent");
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Module Ks:X\n#Module Ks\n"), "a script's own #Module Ks can come after its child");
		Assert.IsNotEmpty(LoweringDiagnostics.Diagnostics("#Module AHK:X\n#Module AHK\n"), "AHK is no parent in either order");

		// A rejected name reaches stderr under an #ErrorStdOut in any earlier module, not a dialog.
		var (errorStdOutProgram, _) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("#ErrorStdOut\n#Module A\n#Module Ks:X\n");
		var errorStdOutLowerer = new Keysharp.Compilation.Syntax.Lowerer();
		_ = errorStdOutLowerer.Build(errorStdOutProgram, "Test");
		Assert.IsNotEmpty(errorStdOutLowerer.Diagnostics);
		Assert.IsTrue(errorStdOutLowerer.ErrorStdOut, "a rejected #Module name must honor #ErrorStdOut");

		// An imported module name is a constant, and the import exposing a nested module conflicts as any import does.
		foreach (var (source, message) in new[]
		{
			("#Import Ks\nKs := 1\n", "This Module cannot be used as an output variable: Ks"),
			("#Import Ks\nKs++\n", "This Module cannot be assigned a value: Ks"),
			("#Import Ks\nr := &Ks\n", "This Module cannot have its reference taken: Ks"),
			("#Import Ks\nfor Ks in [1]\nx := 1\n", "This Module cannot be used as an output variable: Ks"),
			("#Import Ks\ntry\nx := 1\ncatch as Ks\nx := 2\n", "This Module cannot be used as an output variable: Ks"),
			("#Import Ks\nFoo() {\nglobal\nKs := 1\n}\n", "This Module cannot be used as an output variable: Ks"),
			("#Import Other:Nested\nNested := 1\n#Module Other:Nested\n", "This Module cannot be used as an output variable: Nested"),
			("#Module Other\n#Import Other:Nested\nNested() => 1\n#Module Other:Nested\n", "This import declaration conflicts with an existing declaration: Nested"),
			("#Import Ks:DotNet\nDotNet := 1\n", "This Module cannot be used as an output variable: DotNet"),
			("#Import Ks { DotNet }\nDotNet := 1\n", "This Module cannot be used as an output variable: DotNet"),
			("#Import Ks:DotNet { Format }\nFormat := 1\n", "This Func cannot be used as an output variable: Format"),
			("#Import Ks:DotNet { Format }\nFormat(x) => x\n", "This import declaration conflicts with an existing declaration: Format"),
			("#Import Ks:DotNet\nx := DotNet.Format(\"{1}\", 1)\nFoo() {\nglobal DotNet\nreturn DotNet\n}\n", "This import declaration conflicts with an existing global variable: DotNet"),
		})
			AssertDiagnostic(source, message);
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Module Other\nNested() => 1\n#Module Other:Nested\n"),
			"declaring a nested module takes no name from its parent");
	}

	[Test, Category("Module")]
	public void ImportFile() => Assert.IsTrue(TestScript("module-import-file", false));

	[Test, Category("Module")]
	public void ImportUnknownMember()
	{
		// Real Ks members still compile cleanly (the fix must not over-reject) — both a method (Cosh) and a
		// type exposed under a [UserDeclaredName] (Image, whose CLR type is KeysharpImage).
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import \"Ks\" { Cosh }\n"), "a valid built-in method import should not error");
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#import KS { Image }\n"), "a valid built-in type import (UserDeclaredName) should not error");

		// A name the module does not expose is rejected, and the diagnostic names the offending member.
		AssertDiagnostic("#import \"Ks\" { NotARealKsMember123 }\n", "has no exported member", "NotARealKsMember123");
		AssertDiagnostic("#Import Ks { A_InputLevel }\n", "has no exported member", "A_InputLevel");

		// The members of a nested module are checked as those of any built-in module, and Ks has no member of these names.
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Import Ks:DotNet { Format, RegExMatch, RegExReplace }\n"), "the members of Ks:DotNet should import");
		Assert.IsEmpty(LoweringDiagnostics.Diagnostics("#Import Ks { DotNet }\n"), "a nested module is a member of its parent");
		foreach (var (source, member) in new[]
		{
			("#Import Ks:DotNet { Nope }\n", "Nope"),
			("#Import Ks { FormatCs }\n", "FormatCs"),
		})
			AssertDiagnostic(source, "has no exported member", member);
	}

	// A script sees only what a global name finds, and Keyview's highlighter reads these tables. No class sits in a nested
	// built-in module yet, so no script reaches the container check either.
	[Test, Category("Module"), Category("Internal")]
	public void NestedBuiltinTables()
	{
		var dotNet = typeof(Keysharp.Builtins.Ks.DotNet);
		var tables = s.ReflectionsData;
		Assert.IsFalse(tables.flatPublicStaticMethods.Values.Any(method => method.DeclaringType == dotNet));
		Assert.IsFalse(tables.stringToTypes.Values.Contains(dotNet));
		Assert.IsTrue(Script.IsModuleContainer(dotNet, s));

		// A named import replaces the global function, so a call naming its arguments has to mean the same through either.
		foreach (var (name, owner) in new[] { ("Format", typeof(Keysharp.Builtins.Strings)), ("RegExMatch", typeof(Keysharp.Builtins.RegEx)), ("RegExReplace", typeof(Keysharp.Builtins.RegEx)) })
		{
			Assert.That(tables.flatPublicStaticMethods[name].DeclaringType, Is.EqualTo(owner));
			Assert.That(dotNet.GetMethod(name).GetParameters().Select(parameter => parameter.Name),
				Is.EqualTo(tables.flatPublicStaticMethods[name].GetParameters().Select(parameter => parameter.Name)), name);
		}
	}

	// A script's own Ks is an ordinary module, so a path through it finds its own child, whichever directive comes first.
	[TestCase("#Module Ks\nValue := \"parent\"\n#Module Ks:DotNet\nValue := \"child\"\n")]
	[TestCase("#Module Ks:DotNet\nValue := \"child\"\n#Module Ks\nValue := \"parent\"\n")]
	[Category("Module")]
	public void OwnKsModule(string modules)
	{
		var output = RunScript("#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Include <assert>\n#Import Ks\n#Import Ks:DotNet\n"
			+ "AssertEq(Ks.Value, \"parent\", A_LineNumber)\nAssertEq(DotNet.Value, \"child\", A_LineNumber)\n"
			+ "Throws(() => Ks.Cosh, A_LineNumber, PropertyError)\nFileAppend \"pass\", \"*\"\n" + modules, "module_own_ks", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
	}

	// ListVars opens a window, so the text it shows is read directly. A module is listed by its qualified name.
	[Test, Category("Module"), Category("Internal")]
	public void ListVarsNamesModules()
	{
		var output = RunScript("#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import AHK\n#Import Ks:DotNet\n#Import Other:Nested\nFileAppend \"pass\", \"*\"\n#Module Other:Nested\n",
			"module_listvars", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
		var listed = Keysharp.Builtins.Debug.GetVars(false);
		Assert.That(listed, Does.Contain("DotNet: Ks:DotNet (Module)"));
		Assert.That(listed, Does.Contain("Nested: Other:Nested (Module)"));
		Assert.That(listed, Does.Contain("AHK: AHK (Module)"));
	}

	[Test, Category("Module")]
	public void ModuleBelowTopLevel()
	{
		// `#Module` is only meaningful at the top level; anywhere else it is a parse error, not a silent drop.
		foreach (var source in new[] { "class C {\n#Module Nope\n}\n", "F() {\n#Module Nope\n}\n", "if 1 {\n#Module Nope\n}\n", "{\n#Module Nope\n}\n" })
			AssertParseDiagnostic(source, "#Module is only allowed at the top level");
	}

	[Test, Category("Module")]
	public void ObjectMembers() => Assert.IsTrue(TestScript("module-object-members", false));

	[Test, Category("Module")]
	public void Order() => Assert.IsTrue(TestScript("module-order", false));

	[Test, Category("Module")]
	public void ScopedImport() => Assert.IsTrue(TestScript("module-scoped-import", false));

	/// <summary>
	/// A script without #Module is one module, and a wildcard import supplies no name it assigns or declares, as in
	/// AutoHotkey: each is the module's own variable, so assigning one leaves the imported module's alone, and one
	/// only declared is unset.
	/// </summary>
	[Test, Category("Module")]
	public void WildcardSkipsOwnVariables() => Assert.IsTrue(TestScript("module-wildcard-own-vars", false));
}
