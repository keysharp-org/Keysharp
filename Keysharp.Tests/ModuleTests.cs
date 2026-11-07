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
				#Import "{{drive}}Other.ahk:Sub" as DriveRelativeSub
				#Import "{{drive}}Other.ahk:Sub:Deep" as DriveRelativeDeep
				#Import "{{drive}}BareFile.ahk:" as DriveRelativeRoot
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
				Assert(Left.SameChild, A_LineNumber)
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
				#Import RootChild as BareChild
				#Import __Main:OwnRootChild as MainChild
				Value := Dependency.Value
				InnerValue := LocalInner.Value
				MainChildValue := MainChild.Value
				SameChild := Child == BareChild

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

		// A specifier ending in names is modules whatever files exist, so a drive-relative path needs its extension.
		if (drive.Length == 0) return;
		var workingDirectory = Environment.CurrentDirectory;
		try
		{
			_ = Directory.CreateDirectory(root);
			File.WriteAllText(Path.Combine(root, "Other.ahk"), "#Module Sub\nValue := 606\n");
			Environment.CurrentDirectory = root;
			AssertDiagnostic($"#Import \"{drive}Other:Sub\" as Sub\n", "module not found");
		}
		finally
		{
			Environment.CurrentDirectory = workingDirectory;
			try { Directory.Delete(root, true); }
			catch { }
		}
	}

	[Test, Category("Module")]
	public void Import() => Assert.IsTrue(TestScript("module-import", false));

	[Test, Category("Module")]
	public void ImportDirectiveOrder()
	{
		RunFiles(TempRoot("directive-order"), "module_import_directive_order",
		[
			("main.ahk", """
				#NoTrayIcon
				#ErrorStdOut
				#Warn All, StdOut
				#Hotstring C0
				#Import "RootFirst"
				Hotstring(":C:ksimportnested", , "Off")
				Hotstring(":C0:ksimporttail", , "Off")
				Hotstring(":C:ksimportroot", , "Off")
				FileAppend "pass", "*"
				ExitApp()
				#Module Later
				#Import "Branch"
				#Import "Tail"
				"""),
			("RootFirst.ahk", "::ksimportroot::x\n"),
			("Branch.ahk", "#Hotstring C\n#Import \"Leaf\"\n"),
			("Leaf.ahk", "::ksimportnested::x\n#Hotstring C0\n"),
			("Tail.ahk", "::ksimporttail::x\n#Hotstring C\n"),
		]);
	}

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
				; A #Requires in an #Include reaches what that file includes and ends with it.
				AssertEq(DefinedMode(Helpers.ThroughInclude), "v2.1", A_LineNumber)
				; A #Module takes the mode of the file holding it, and only a #Requires in that file sets the module's mode.
				AssertEq(Helpers.Startup, "v2.0", A_LineNumber)
				; As in AutoHotkey, this file's #Requires does not reach __Main's section of another file.
				AssertEq(DefinedMode(MainSectionInLib), "v2.0", A_LineNumber)
				; A file starts in v2.0 mode whatever imports it, and a #Module takes the mode of its own file's root.
				#Import "Plain" as Plain
				#Import "Plain:Sibling" as PlainSibling
				AssertEq(DefinedMode(Plain.F), "v2.0", A_LineNumber)
				AssertEq(Plain.Startup, "v2.0", A_LineNumber)
				AssertEq(DefinedMode(PlainSibling.F), "v2.0", A_LineNumber)
				AssertEq(PlainSibling.Startup, "v2.0", A_LineNumber)
				; A file's #Requires reaches a section of __Main after it, which leaves __Main's own mode alone.
				#Import "Carrying"
				AssertEq(DefinedMode(CarriedIntoMain), "v2.1", A_LineNumber)
				; Reopening a module keeps the load-time mode of the directive before it, in the file holding them.
				#Import "Reopening" as Reopening
				#Import "Reopening:Other" as ReopenedOther
				AssertEq(DefinedMode(Reopening.BeforeOther), "v2.1", A_LineNumber)
				AssertEq(DefinedMode(Reopening.AfterOther), "v2.0", A_LineNumber)
				AssertEq(Reopening.Startup, "v2.1", A_LineNumber)
				AssertEq(ReopenedOther.Startup, "v2.0", A_LineNumber)
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
			("Plain.ahk", $$"""
				F() {
				}
				{{StartupProbe}}
				#Module Sibling
				F() {
				}
				{{StartupProbe}}
				"""),
			("Carrying.ahk", """
				#Requires AutoHotkey v2.1-alpha
				#Module __Main
				CarriedIntoMain() {
				}
				"""),
			("Reopening.ahk", $$"""
				#Requires AutoHotkey v2.1-alpha
				BeforeOther() {
				}
				{{StartupProbe}}
				#Module Other
				#Requires AutoHotkey v2.0
				{{StartupProbe}}
				#Module __Init
				AfterOther() {
				}
				"""),
			("Reopener.ahk", "#Module __Main:Helpers\n#Requires AutoHotkey v2.1-alpha\n"),
			("Part.ahk", "#Requires AutoHotkey v2.1-alpha\n#Include \"Deep.ahk\"\n"),
			("Deep.ahk", "ThroughInclude() {\n}\n"),
		]);
	}

	// AutoHotkey reads an unquoted name by its identifier characters and converts escape sequences in a quoted path.
	[Test, Category("Module")]
	public void ImportNameForms()
	{
		RunFiles(TempRoot("import-forms"), "module_import_forms",
		[
			("main.ahk", $$"""
				#NoTrayIcon
				#ErrorStdOut
				#Warn All, StdOut
				#Include "{{Path.Combine(path, "Lib", "assert.ahk")}}"
				#Import 7zip as Seven
				#Import 7zip
				#Import Other as 1x
				#Import "Esc`;aped" as Escaped
				#Import Export as
				AssertEq(Seven.Value, 7, A_LineNumber)
				AssertEq(Escaped.Value, "escaped", A_LineNumber)
				AssertEq(%"as"%.Value, "as", A_LineNumber)
				FileAppend "pass", "*"
				#Module Other
				"""),
			("7zip.ahk", "Value := 7\n"),
			("Esc;aped.ahk", "Value := \"escaped\"\n"),
			("as.ahk", "Value := \"as\"\n"),
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

				#Module Spelled
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
			// `__Main:Spelled` and `Spelled` name one module in the main file, so a #Requires under either spelling is its mode.
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

	// What a script lowers to, the only place a cached field shows, as a run cannot tell one from a lookup. Whatever the
	// script, no function body builds a built-in's function or module object: only a field's initializer does.
	// Only compile takes the code through Roslyn, for a case where what C# accepts is the point, as a compile costs far more.
	private static string GeneratedCode(string source, string[] contains = null, string[] absent = null, bool compile = false)
	{
		var code = PrettyPrinter.Print(Lowered(source).Unit);
		if (compile)
		{
			var (bytes, text, _) = new CompilerHelper().CompileCodeToByteArray(source, "module_generated", null, false, true, ScriptCompilationOutput.InMemory, sourceIsFile: false);
			Assert.That(bytes, Is.Not.Null, text);
		}
		foreach (var line in code.Split('\n'))
			if (line.Contains("Functions.Func((System.Delegate)Keysharp.Builtins.") || line.Contains("Misc.ModuleObject("))
				Assert.That(line.Trim(), Does.Match(@"^(public|private) static (readonly )?object @?\w+ = "), source);
		foreach (var text in contains ?? [])
			Assert.That(code, Does.Contain(text), source);
		foreach (var text in absent ?? [])
			Assert.That(code, Does.Not.Contain(text), source);
		return code;
	}

	private static ScriptCompilationResult Lowered(string source)
	{
		var compilation = new CompilerHelper().CreateCompilationUnitFromFile(source, "module_generated", sourceIsFile: false);
		Assert.That(compilation.Errors.HasErrors, Is.False, () => source + CompilerHelper.GetCompilerErrors(compilation.Errors).Item1);
		return compilation;
	}

	private const string BuiltinFunc = "Keysharp.Builtins.Functions.Func((System.Delegate)Keysharp.Builtins.";
	private const string Invoke = "Keysharp.Runtime.Script.Invoke(";
	private const string InvokeOrNull = "Keysharp.Runtime.Script.InvokeOrNull(";
	private const string InvokeAs = "Keysharp.Runtime.Script.InvokeAs(";
	private const string MainModule = "Keysharp.CompiledMain.Program.__Main.";
	private const string OtherF = "Keysharp.CompiledMain.Program.Other.f";

	private static string CachedFunc(string field, string target) => $"private static readonly object {field} = {BuiltinFunc}{target});";

	private static string CachedModule(string field, string target) =>
		$"private static readonly object {field} = Keysharp.Builtins.Misc.ModuleObject(typeof(Keysharp.Builtins.{target}));";

	// A named import of a built-in holds the function in a field under its own name, as a global built-in does.
	[Test, Category("Module"), Category("Internal")]
	public void NamedImportField()
	{
		GeneratedCode("#import Ks:Dotnet { Format }\nMsgBox Format(\"{1}\", 1)\n",
			[CachedFunc("format", "Ks.DotNet.Format"), $"{InvokeOrNull}msgbox, null, {Invoke}format, null, \"{{1}}\", 1L));"], compile: true);
		GeneratedCode("#Import Ks { * }\nx := Cosh(1)\n", [CachedFunc("cosh", "Ks.Cosh"), $"{Invoke}cosh, null, 1L)"]);
		GeneratedCode("#Import Ks { Cosh as Hyperbolic }\nx := Hyperbolic(1)\n", [CachedFunc("hyperbolic", "Ks.Cosh"), $"{InvokeAs}hyperbolic, \"Hyperbolic\", 1L)"]);
		// Two names for one function share its field.
		GeneratedCode("#Import Ks { Cosh, Cosh as Hyperbolic }\nx := Hyperbolic(1)\ny := Cosh(2)\n",
			[CachedFunc("cosh", "Ks.Cosh"), $"{InvokeAs}cosh, \"Hyperbolic\", 1L)", $"{Invoke}cosh, null, 2L)"], ["hyperbolic ="]);
		GeneratedCode("#Import AHK { StrLen as Length }\nx := Length(\"a\")\n", [CachedFunc("length", "Strings.StrLen"), $"{InvokeAs}length, \"Length\", \"a\")"]);
		// A variable's function is known only at run time, so its call passes along the name written, and a statement needs no name.
		GeneratedCode("Foo(Callback) {\nx := Callback(1)\nCallback(2)\nreturn x\n}\n", [$"{InvokeAs}Callback, \"Callback\", 1L)", "InvokeOrNull(Callback, null, 2L)"]);
		// A name C# reserves is escaped as any module field's.
		GeneratedCode("#Import Ks { Cosh as event }\nx := event(1)\nFoo(event) {\nreturn Ks.Cosh(event)\n}\n#Import Ks\n",
			[CachedFunc("@event", "Ks.Cosh"), $"{InvokeAs}@event, \"event\", 1L)", $"return {Invoke}{MainModule}@event, null, @event);"], compile: true);
	}

	// A member call on a chain of constants invokes that function's field, naming no member.
	[Test, Category("Module"), Category("Internal")]
	public void MemberCallField()
	{
		GeneratedCode("#Import Ks\nx := Ks.Cosh(1)\n", [CachedFunc("KS_Ks_Cosh", "Ks.Cosh"), $"{Invoke}KS_Ks_Cosh, null, 1L)"]);
		// A statement discards the result, and no variable named like the member changes the target.
		GeneratedCode("#Import Ks\nCosh := 5\nKs.Cosh(1)\n", [$"{InvokeOrNull}KS_Ks_Cosh, null, 1L);"]);

		foreach (var source in new[]
		{
			"#Import Ks\nx := Ks.DotNet.Format(\"{1}\", 1)\n",
			"#Import Ks:DotNet\nx := DotNet.Format(\"{1}\", 1)\n",
			"#Import Ks { DotNet as N }\nx := (N).Format(\"{1}\", 1)\n",
			"#Import Ks { * }\nx := DotNet.Format(\"{1}\", 1)\n",
			"#Import Relay\nx := Relay.DotNet.Format(\"{1}\", 1)\n#Module Relay\n#Import Export Ks:DotNet\n",
		})
			GeneratedCode(source, [CachedFunc("KS_Ks_DotNet_Format", "Ks.DotNet.Format"), $"{Invoke}KS_Ks_DotNet_Format, null, \"{{1}}\", 1L)"], [", \"Format\", \"{1}\""]);

		// It reuses the field a module-level import names, whichever reference comes first.
		GeneratedCode("#Import Ks:DotNet { Format }\nx := DotNet.Format(\"{1}\", 1)\ny := Format(\"{1}\", 2)\n",
			[CachedFunc("format", "Ks.DotNet.Format"), $"{Invoke}format, null, \"{{1}}\", 1L)", $"{Invoke}format, null, \"{{1}}\", 2L)"], ["KS_Ks_DotNet_Format"]);
		// And the field a global built-in has, whichever reference comes first.
		GeneratedCode("#Import AHK\nx := StrLen(\"a\")\ny := AHK.StrLen(\"ab\")\n",
			[CachedFunc("strlen", "Strings.StrLen"), $"{Invoke}strlen, null, \"a\")", $"{Invoke}strlen, null, \"ab\")"], ["KS_Strings_StrLen"]);
		GeneratedCode("#Import AHK\nx := AHK.StrLen(\"a\")\ny := StrLen(\"ab\")\n",
			[CachedFunc("KS_Strings_StrLen", "Strings.StrLen"), $"{Invoke}KS_Strings_StrLen, null, \"a\")", $"{Invoke}KS_Strings_StrLen, null, \"ab\")"], ["object strlen ="]);

		// An exported function has the module's own field, reached however the module holds it.
		GeneratedCode("#Import Other\nx := Other.F()\ny := Other.Nested.F()\nOther.F()\n#Module Other\n#Import Other:Nested\nF() => 1\n#Module Other:Nested\nF() => 2\n",
			[$"{Invoke}{OtherF}, null)", $"{InvokeOrNull}{OtherF}, null);",
				$"{Invoke}Keysharp.CompiledMain.Program.Other.Nested.f, null)"], [", \"F\")", ", \"Nested\")"]);
		GeneratedCode("#Import Relay\nx := Relay.F()\ny := Relay.G()\nz := Relay.H()\n#Module Relay\n#Import Export Other { * }\n#Import Other { F as G }\n#Import Other { F as H }\n#Module Other\nF() => 1\n",
			[$"{Invoke}{OtherF}, null)"], ["relay, \"F\"", "relay, \"G\"", "relay, \"H\""]);
		GeneratedCode("#Import __Main\nx := __Main.F()\nF() => 1\n", [$"{Invoke}{MainModule}f, null)"], [", \"F\")"]);

		// Another spelling of its name takes the same field, and says how it was written only where a no-value error can name it.
		GeneratedCode("#Import Relay\nx := Relay.G()\nRelay.G()\ny := (Relay.G()?)\nz := Relay.G() ?? 1\nArrow() => Relay.G()\n#Module Relay\n#Import Other { F as G }\n#Module Other\nF() => 1\n",
			[$"\tx = {InvokeAs}{OtherF}, \"G\");", $"\t{InvokeOrNull}{OtherF}, null);", $"\ty = ({InvokeOrNull}{OtherF}, null));", $"{InvokeOrNull}{OtherF}, null)) ?? 1L", $"return {InvokeOrNull}{OtherF}, null);"], ["relay, \"G\""]);
		GeneratedCode("#Import Ks\nx := Ks.COSH(1)\n", [CachedFunc("KS_Ks_Cosh", "Ks.Cosh"), $"{InvokeAs}KS_Ks_Cosh, \"COSH\", 1L)"]);

		// The AHK module's functions are the global built-ins, which a script's own #Module AHK can replace.
		GeneratedCode("#Import AHK\nx := AHK.StrLen(\"a\")\n#Module AHK\nStrLen(s) => 1\n", [$"{Invoke}Keysharp.CompiledMain.Program.AHK.strlen, null, \"a\")"], [BuiltinFunc + "Strings.StrLen"]);

		// The arguments take the path a named import's do.
		GeneratedCode("#Import Ks:DotNet\nf := [1]\nx := DotNet.Format(f*)\ny := DotNet.RegExMatch(\"a\", NeedleRegEx: \"b\")\nz := DotNet.RegExMatch(\"a\", \"b\", &m)\n",
			[$"{Invoke}KS_Ks_DotNet_Format, null, ", "KS_Ks_DotNet_RegExMatch, null, \"a\", Keysharp.Runtime.Script.NamedArgs(\"NeedleRegEx\", \"b\"))"]);
	}

	// A nested module's class is declared inside its parent's, and a module's class has a name none of its own or its
	// parent's members can take.
	[Test, Category("Module"), Category("Internal")]
	public void NestedModuleClass()
	{
		GeneratedCode("#Import other:nested\nx := nested.F()\n#Module other:nested\nF() => 2\n", ["Program.Other.Nested.f, null)"]);
		GeneratedCode("#Import Other:Nested\nx := Nested.F()\n#Module Other\nclass Nested {\n}\n#Module Other:Nested\nF() => 2\n",
			["Program.Other.__KSPath_004E_0065_0073_0074_0065_0064.f, null)"], compile: true);
	}

	// A reference with no module-level name goes by its target, and one with a name reaches the field past a local or member.
	[Test, Category("Module"), Category("Internal")]
	public void ScopedImportField()
	{
		foreach (var source in new[] { "Foo() {\n#Import Ks { Cosh }\nreturn Cosh(1)\n}\n", "Foo() {\n#Import Ks { * }\nreturn Cosh(1)\n}\n" })
			GeneratedCode(source, [CachedFunc("KS_Ks_Cosh", "Ks.Cosh"), $"return {Invoke}KS_Ks_Cosh, null, 1L);"]);
		GeneratedCode("class C {\n#Import Ks { Sinh }\nstatic G(a) {\nreturn Sinh(a)\n}\n}\n", [CachedFunc("KS_Ks_Sinh", "Ks.Sinh"), $"return {Invoke}KS_Ks_Sinh, null, a);"]);
		GeneratedCode("Foo() {\n#Import Ks { Cosh as Shared }\nreturn Shared(1)\n}\nBar() {\n#Import Ks { Sinh as Shared }\nreturn Shared(1)\n}\n",
			[CachedFunc("KS_Ks_Cosh", "Ks.Cosh"), CachedFunc("KS_Ks_Sinh", "Ks.Sinh"), $"return {InvokeAs}KS_Ks_Cosh, \"Shared\", 1L);", $"return {InvokeAs}KS_Ks_Sinh, \"Shared\", 1L);"]);
		GeneratedCode("#Import Ks { Cosh }\nFoo() {\n#Import Ks { Cosh as Scoped }\nreturn Scoped(1)\n}\n", [CachedFunc("cosh", "Ks.Cosh"), $"return {InvokeAs}cosh, \"Scoped\", 1L);"], ["KS_Ks_Cosh"]);
		GeneratedCode("Foo() {\n#Import Other { F }\nreturn F()\n}\n#Module Other\nF() => 1\n", [$"return {Invoke}{OtherF}, null);"]);

		// A C# local of the field's name would capture a simple name, the enclosing function's too.
		GeneratedCode("#Import Ks\nFoo(KS_Ks_Cosh) {\nreturn Ks.Cosh(KS_Ks_Cosh)\n}\n", [$"return {Invoke}{MainModule}KS_Ks_Cosh, null, KS_Ks_Cosh);"]);
		GeneratedCode("#Import Ks\nFoo() {\nlocal KS_Ks_Cosh := 1\nNested() {\nreturn Ks.Cosh(1)\n}\nreturn Nested() KS_Ks_Cosh\n}\n", [$"return {Invoke}{MainModule}KS_Ks_Cosh, null, 1L);"]);
		// A class member of that name would too, and a method's name is no field's.
		GeneratedCode("#Import Ks { Cosh as op }\nclass C {\nstatic G() {\nreturn op(1)\n}\n}\n", [$"return {InvokeAs}{MainModule}op, \"op\", 1L);"]);
		GeneratedCode("#Import Ks:DotNet { Format }\n#Import Ks\nclass C {\nFormat(a) {\nreturn Format(a)\n}\nKs() {\nreturn Ks.DotNet.Format(1)\n}\n}\n",
			[$"return {Invoke}format, null, a);", $"return {Invoke}format, null, 1L);"]);
		// `this` is a method's receiver, whatever an import of that name binds.
		GeneratedCode("#Import Ks as this\nclass C {\nG() {\nreturn this.Name()\n}\n}\n", [$"return {Invoke}@this, \"Name\");"]);
		GeneratedCode("#Import Ks\n#Import Ks { Sinh as this }\nclass C {\nG() {\nreturn Ks.Sinh(1)\n}\n}\n", [$"return {Invoke}KS_Ks_Sinh, null, 1L);"], ["object @this ="]);
		// So a whole-module import of that name, which has the field, serves no other reference, in a method or an arrow in one.
		GeneratedCode("#Import Ks\n#Import Ks:DotNet as this\nclass C {\nG() {\nreturn Ks.DotNet\n}\nH() {\n#Import Ks:DotNet as Inner\nreturn Inner\n}\nI() {\nreturn () => Ks.DotNet\n}\n}\n",
			[CachedModule("KS_Ks_DotNet", "Ks.DotNet"), "return KS_Ks_DotNet;"], ["return @this;"], compile: true);
		// A module named like a field has a class of another name, which C# requires of a type and its member.
		GeneratedCode("#Import KS_Ks_Cosh\n#Module KS_Ks_Cosh\n#Import Ks\nx := Ks.Cosh(1)\n", [CachedFunc("KS_Ks_Cosh", "Ks.Cosh")], compile: true);
	}

	// A built-in module object is cached as a function is, and a whole-module import's field serves every reference to it.
	[Test, Category("Module"), Category("Internal")]
	public void ModuleObjectField()
	{
		GeneratedCode("#Import Ks { DotNet }\nx := DotNet\n", [CachedModule("dotnet", "Ks.DotNet"), "\tx = dotnet;"]);
		GeneratedCode("#Import Ks\nx := Ks.DotNet\n", [CachedModule("KS_Ks_DotNet", "Ks.DotNet"), "\tx = KS_Ks_DotNet;"]);
		foreach (var source in new[] { "Foo() {\n#Import Ks:DotNet\nreturn DotNet\n}\n", "Foo() {\n#Import Ks { DotNet as Scoped }\nreturn Scoped\n}\n" })
			GeneratedCode(source, [CachedModule("KS_Ks_DotNet", "Ks.DotNet"), "return KS_Ks_DotNet;"]);
		GeneratedCode("Foo() {\n#Import AHK\nreturn AHK\n}\n", ["private static readonly object KS_Keysharp_Runtime_Ahk = Keysharp.Builtins.Misc.ModuleObject(typeof(Keysharp.Runtime.Ahk));", "return KS_Keysharp_Runtime_Ahk;"]);
		GeneratedCode("#Import Ks\n#Import Ks { DotNet }\n#Import Ks:DotNet as Whole\nx := Ks.DotNet\ny := DotNet\nFoo() {\n#Import Ks:DotNet\nreturn DotNet\n}\n",
			["\tx = whole;", "\ty = whole;", "return whole;"], ["KS_Ks_DotNet", "object dotnet ="]);
		GeneratedCode("#Import Ks:DotNet\nx := DotNet\nFoo(DotNet) {\n#Import Ks { DotNet as Other }\nreturn Other\n}\n", ["\tx = " + MainModule + "dotnet;", $"return {MainModule}dotnet;"]);
	}

	// In a module with #CSharp blocks a bridge has the import's name, so the field has its target's, and nothing takes a field
	// until a script reference names it: a bridge reads the member without one, as a wildcard's dynamic name does.
	[Test, Category("Module"), Category("Internal")]
	public void InlineModuleField()
	{
		const string Inline = "#CSharp\npublic static object Through() => format;\n#EndCSharp\n";
		const string FormatBridge = "public static object format => Keysharp.Builtins.Misc.MemberGet(typeof(Keysharp.Builtins.Ks.DotNet), \"Format\", Keysharp.Runtime.ModuleBindingKind.Function);";
		GeneratedCode("#Import Ks:DotNet { Format }\nx := Format(\"{1}\", 1)\n" + Inline,
			[CachedFunc("KS_Ks_DotNet_Format", "Ks.DotNet.Format"), FormatBridge, $"{Invoke}KS_Ks_DotNet_Format, null, \"{{1}}\", 1L)"], ["object format = "], compile: true);
		GeneratedCode("#Import Ks:DotNet { Format }\n" + Inline, [FormatBridge], ["KS_Ks_DotNet_Format"]);
		const string MemberBridge = "public static object member => Keysharp.Builtins.Misc.MemberGet(typeof(Keysharp.Builtins.Ks.DotNet), null, Keysharp.Runtime.ModuleBindingKind.Module);";
		GeneratedCode("#Import Ks { DotNet as Member }\n#Import Ks:DotNet as Whole\n#CSharp\npublic static object Through() => member;\n#EndCSharp\n", [MemberBridge], ["KS_Ks_DotNet"]);
		// A whole-module import's field serves a script's reference here too.
		GeneratedCode("#Import Ks { DotNet as Member }\n#Import Ks:DotNet as Whole\nx := Member\n#CSharp\npublic static object Through() => member;\n#EndCSharp\n", [MemberBridge, "\tx = whole;"], ["KS_Ks_DotNet"]);
		GeneratedCode("#Import Ks { * }\n#CSharp\npublic static object Through() => cosh;\n#EndCSharp\n",
			["public static object cosh => Keysharp.Builtins.Misc.MemberGet(typeof(Keysharp.Builtins.Ks), \"Cosh\", Keysharp.Runtime.ModuleBindingKind.Function);"], ["KS_Ks_Cosh ="]);
		GeneratedCode("#Import Ks { * }\nx := Cosh(1)\n#CSharp\npublic static object Through() => cosh;\n#EndCSharp\n", [CachedFunc("KS_Ks_Cosh", "Ks.Cosh"), $"{Invoke}KS_Ks_Cosh, null, 1L)"], [BuiltinFunc + "Ks.Sinh"]);
		// A name of that form is a method's to take where no field has it.
		GeneratedCode("#Import Ks\n#CSharp\npublic static object KS_Ks_Cosh() => 1;\n#EndCSharp\n", compile: true);
	}

	// What the receiver answers at run time stays a lookup: what can change, what has no field, and what only it can resolve.
	[Test, Category("Module"), Category("Internal")]
	public void MemberCallStaysDynamic()
	{
		const string OtherObject = MainModule + "other";

		foreach (var (source, dynamicCall) in new[]
		{
			("#Import Ks\nFoo(Ks) {\nreturn Ks.Cosh(1)\n}\n", "Invoke(Ks, \"Cosh\", 1L)"),
			("#Import Ks\nFoo() {\nKs := 1\nNested() {\nreturn Ks.Cosh(1)\n}\nreturn Nested()\n}\n", "Invoke(Ks, \"Cosh\", 1L)"),
			("#Import Ks\nx := Ks.%\"Cosh\"%(1)\n", "Invoke(" + MainModule + "ks, \"Cosh\", 1L)"),
			("#Import Ks\nx := (Ks?).Cosh(1)\n", "\"Cosh\", 1L)"),
			("#Import Ks\nx := Ks.HasProp(\"a\")\n", "\"HasProp\", \"a\")"),
			("#Import Ks\nx := Ks.HashMap()\n", "\"HashMap\")"),
			("#Import Ks\nx := Ks.DotNet()\n", "\"DotNet\")"),
			("#Import Ks\nx := Ks.NoSuch()\n", "\"NoSuch\")"),
		})
			GeneratedCode(source, [dynamicCall], ["KS_Ks_Cosh ="]);

		foreach (var (source, member) in new[]
		{
			("#Import Other\nx := Other.V()\n#Module Other\nV := 1\n", "V"),
			("#Import Other\nx := Other.K()\n#Module Other\nclass K {\n}\n", "K"),
			("#Import Other\nx := Other.Cs()\n#Module Other\n#CSharp\npublic static object Cs() => 1;\n#EndCSharp\n", "Cs"),
			("#Import Other\nx := Other.Nested()\n#Module Other\n#Import Other:Nested\n#Module Other:Nested\n", "Nested"),
		})
			GeneratedCode(source, [$"{OtherObject}, \"{member}\""]);
	}

	// An import nothing names costs nothing at startup, and a dynamic name through a wildcard reaches its member without a field.
	[Test, Category("Module"), Category("Internal")]
	public void UnusedImportHasNoField()
	{
		foreach (var source in new[]
		{
			"#Import Ks { Cosh }\n",
			"#Import Ks { * }\n",
			"Foo() {\n#Import Ks { Cosh }\nreturn 1\n}\n",
			"class C {\n#Import Ks { Cosh }\n}\n",
			"#Import Ks { * }\nFoo(Name) => %Name%\n",
			"#Import Ks { Cosh, Sinh as Hyperbolic }\n#CSharp\npublic static object One() => 1;\n#EndCSharp\n",
			"#Import Ks { * }\n#CSharp\npublic static object One() => 1;\n#EndCSharp\n",
		})
			GeneratedCode(source, [], ["Functions.Func((System.Delegate)Keysharp.Builtins.Ks", "KS_Ks_"]);
		foreach (var source in new[] { "#Import Ks { DotNet }\n", "Foo() {\n#Import Ks:DotNet\nreturn 1\n}\n", "#Import Ks { DotNet as Net }\n#CSharp\npublic static object One() => 1;\n#EndCSharp\n" })
			GeneratedCode(source, [], ["Misc.ModuleObject(typeof(Keysharp.Builtins.Ks.DotNet))"]);
		// `#Import AHK { * }` reaches hundreds of functions, whose fields a dynamic name does not ask for.
		GeneratedCode("#Import AHK { * }\nFoo(Name) => %Name%\n", [], ["KS_Strings_StrLen", "object strlen ="]);
	}

	// A compiled program carries what any reference to a run-time compile needs, a field shared with another reference or not.
	[Test, Category("Module")]
	public void FieldsTrackComponents()
	{
		foreach (var source in new[]
		{
			"#Import Ks { RunScript }\n",
			"#Import Ks { * }\nRunScript(\"x := 1\")\n",
			"Foo() {\n#Import Ks { RunScript }\nreturn RunScript(\"x := 1\")\n}\n",
			"#Import Ks\nKs.RunScript(\"x := 1\")\n",
			"#Import Ks\nKs?.RunScript(\"x := 1\")\n",
			"#Import Ks\nf := Ks.RunScript\n",
			"#Import Relay\nRelay.RunScript(\"x := 1\")\n#Module Relay\n#Import Export Ks { RunScript }\n",
			"#Import Ks { * }\nFoo(Name) => %Name%\n",
			"#Import Ks { * }\n#CSharp\npublic static object Through() => runscript;\n#EndCSharp\n",
		})
			Assert.That(Lowered(source).RequiredComponents, Has.Member("compiler"), source);
	}

	// A field takes a method group, which an overload or a generic method could not be, so a member call depends on this.
	[Test, Category("Module"), Category("Internal")]
	public void BuiltinModuleFunctionsCompile()
	{
		var script = new StringBuilder("#Import Ks\n#Import Ks:DotNet\nFoo() {\n");
		foreach (var (receiver, type) in new[] { ("Ks", typeof(Keysharp.Builtins.Ks)), ("DotNet", typeof(Keysharp.Builtins.Ks.DotNet)) })
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName))
				_ = script.Append(receiver).Append('.').Append(Script.GetUserDeclaredName(method) ?? method.Name).Append("()\n");
		_ = GeneratedCode(script.Append("}\n").ToString(), compile: true);
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
