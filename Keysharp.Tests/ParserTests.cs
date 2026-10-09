namespace Keysharp.Tests;

public class ParserTests : TestRunner
{
	private static (byte[] Bytes, string Error) Compile(string source) => CompileRaw("#ErrorStdOut\n" + source);

	// Compiles the source exactly as written, with no leading directive. Only a case whose subject is the FIRST
	// line of the file needs this — a continuation section there merges onto whatever Compile prepends.
	private static (byte[] Bytes, string Error) CompileRaw(string source)
	{
		var name = "parser_" + Guid.NewGuid().ToString("N");
		var (bytes, error, _) = new CompilerHelper().CompileCodeToByteArray(source, name);
		return (bytes, error);
	}

	private static void AssertCompileError(string source, string expected)
	{
		var (bytes, error) = Compile(source);
		Assert.IsNull(bytes, "Expected compilation to fail.");
		Assert.That(error, Does.Contain(expected));
	}

	private static void AssertCompiles(params string[] sources)
	{
		foreach (var source in sources)
		{
			var (bytes, error) = Compile(source);
			Assert.That(bytes, Is.Not.Null, error);
		}
	}

	[Test, Category("Parser")]
	public void Parser() => Assert.IsTrue(TestScript("parser", false));

	[Test, Category("Parser")]
	public void OperatorSignatures()
	{
		AssertCompileError("class Invalid {\n *(Left, Right) => 1\n}", "Operator '*' requires one parameter");
		AssertCompileError("class Invalid {\n +(&Right) => Right\n}", "parameters cannot be optional, ByRef, variadic, or have defaults");
		AssertCompileError("class Invalid {\n +(Right*) => Right\n}", "parameters cannot be optional, ByRef, variadic, or have defaults");
		AssertCompileError("class Invalid {\n +(Right := 1) => Right\n}", "parameters cannot be optional, ByRef, variadic, or have defaults");
		AssertCompileError("class Invalid {\n +(Right) => Right\n +(Right) => Right\n}", "Duplicate operator");
		AssertCompileError("class Invalid {\n -() => 1\n -() => 2\n}", "Duplicate operator");
		AssertCompileError("class Invalid {\n !(Right) => Right\n}", "Operator '!' requires no parameters");
		AssertCompileError("class Invalid {\n *() => 1\n}", "Operator '*' requires one parameter");
		AssertCompileError("class Invalid {\n ?(Right) => true\n}", "Operator '?' requires no parameters");
		AssertCompileError("class Invalid {\n ?() => true\n ?() => false\n}", "Duplicate operator");
		AssertCompileError("class Invalid {\n ++(Right) => Right\n}", "Operator '++' requires no parameters");
		AssertCompileError("class Invalid {\n --(Right) => Right\n}", "Operator '--' requires no parameters");
		AssertCompileError("class Invalid {\n --() => 1\n --() => 2\n}", "Duplicate operator");
		AssertCompiles("class Valid {\n -() => 1\n -(Right) => Right\n +() => 2\n +(Right) => Right\n}");
		AssertCompiles("class Valid {\n ++() => 1\n}", "class Valid {\n --() => 1\n}");
		AssertCompiles("class Valid {\n static +(Right) => Right\n +(Right) => Right\n static +() => 1\n +() => 2\n static ?() => true\n static ++() => 1\n}");
		AssertCompileError("class Invalid {\n static +(Right) => Right\n static +(Right) => Right\n}", "Duplicate static operator");
		AssertCompileError("class Invalid {\n static ?(Right) => Right\n}", "Static operator '?' requires no parameters");
		AssertCompileError("class Invalid {\n static +(Right := 1) => Right\n}", "Static operator '+' parameters cannot be optional, ByRef, variadic, or have defaults");
	}

	[TestCase("=", "!="), TestCase("!=", "="), TestCase("==", "!=="), TestCase("!==", "=="), Category("Parser")]
	public void OperatorEqualityPairs(string symbol, string partner)
	{
		var expected = $"Operator '{symbol}' requires operator '{partner}' in the same class";
		AssertCompileError($"class Invalid {{\n {symbol}(Right) => true\n}}", expected);
		var parent = $"class Parent {{\n {symbol}(Right) => true\n {partner}(Right) => false\n}}\n";
		AssertCompileError(parent + $"class Child extends Parent {{\n {symbol}(Right) => false\n}}", expected);
		AssertCompiles(parent + "class Child extends Parent {\n}",
			parent + $"class Child extends Parent {{\n {symbol}(Right) => false\n {partner}(Right) => true\n}}");
	}

	[TestCase("=", "!="), TestCase("!=", "="), TestCase("==", "!=="), TestCase("!==", "=="), Category("Parser")]
	public void StaticOperatorEqualityPairs(string symbol, string partner)
	{
		var expected = $"Static operator '{symbol}' requires static operator '{partner}' in the same class";
		AssertCompileError($"class Invalid {{\n static {symbol}(Right) => true\n {partner}(Right) => false\n}}", expected);
		var parent = $"class Parent {{\n static {symbol}(Right) => true\n static {partner}(Right) => false\n}}\n";
		AssertCompileError(parent + $"class Child extends Parent {{\n static {symbol}(Right) => false\n}}", expected);
		AssertCompiles(parent + "class Child extends Parent {\n}",
			parent + $"class Child extends Parent {{\n static {symbol}(Right) => false\n static {partner}(Right) => true\n}}");
		AssertCompileError($"class Invalid {{\n {symbol}(Right) => true\n static {partner}(Right) => false\n}}",
			$"Operator '{symbol}' requires operator '{partner}' in the same class");
	}

	// The included content lands in the emitted code, not in the returned Unit, so only the generated text can
	// tell a resolved #Include from a skipped one. On failure Bytes is null and Text carries the diagnostics.
	private static (byte[] Bytes, string Text) EmitWithInclude(string source, string includeDir = null, string includeFile = null)
	{
		var (bytes, text, _) = new CompilerHelper().CompileCodeToByteArray(source, "inc_" + Guid.NewGuid().ToString("N"),
								   emitCode: true, includeDirOverride: includeDir, sourceIsFile: false, includeFile: includeFile);
		return (bytes, text);
	}

	private static void AssertIncluded((byte[] Bytes, string Text) emitted)
	{
		Assert.That(emitted.Bytes, Is.Not.Null, emitted.Text);
		Assert.That(emitted.Text, Does.Contain("class Included"));
	}

	private static void AssertIncludeNotFound((byte[] Bytes, string Text) emitted)
	{
		Assert.IsNull(emitted.Bytes, "A missing #Include must fail loudly, not be skipped.");
		Assert.That(emitted.Text, Does.Contain("#Include file not found"));
	}

	[Test, Category("Parser"), Category("Internal")]
	public void IncludeFromMemory()
	{
		var dir = Path.Combine(Path.GetTempPath(), "ks_include_" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(dir);

		try
		{
			var include = Path.Combine(dir, "Included.ks");
			File.WriteAllText(include, "class Included {\n}\n");
			var source = $"#include \"{include}\"\nx := Included()\n";
			var helper = new CompilerHelper();
			var withInclude = helper.CreateCompilationUnitFromFile(source, "include_memory", includeDirOverride: dir);
			var (result, stream, exception) = helper.Compile(withInclude, "include_memory", dir);

			using (stream)
			{
				Assert.IsNull(exception);
				Assert.IsTrue(result?.Success == true, string.Join(Environment.NewLine, result?.Diagnostics ?? []));
			}

			// A relative include is what the base directory actually decides: it resolves against the override,
			// and without one it is looked for in the working directory, where it is not.
			var relative = "#include \"Included.ks\"\nx := Included()\n";
			AssertIncluded(EmitWithInclude(relative, dir));
			AssertIncludeNotFound(EmitWithInclude(relative));

			// --include splices its file before the script's first line, as a plain #Include does, so the script's
			// own #Include of it is skipped rather than declaring the class twice.
			AssertIncluded(EmitWithInclude("x := Included()\n", includeFile: include));
			AssertIncluded(EmitWithInclude(source, includeFile: include));
			var (Bytes, Text) = EmitWithInclude("x := 1\n", includeFile: Path.Combine(dir, "Missing.ks"));
			Assert.IsNull(Bytes);
			Assert.That(Text, Does.Contain("--include file not found"));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	// As in AutoHotkey, an unquoted #Include path is the rest of the line as written, and a file is already
	// included while it is being parsed, so a script which names itself is not spliced in twice.
	[Test, Category("Parser"), Category("Internal")]
	public void IncludePathAsWritten()
	{
		var dir = Path.Combine(Path.GetTempPath(), "ks_include_" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(dir);

		try
		{
			// A ';' is written escaped when nothing separates it from the text before it.
			foreach (var (name, written) in new[] { ("O'Brien.ks", "O'Brien.ks"), ("My  Lib.ks", "My  Lib.ks"), ("My;Lib.ks", "My`;Lib.ks") })
			{
				File.WriteAllText(Path.Combine(dir, name), "class Included {\n}\n");
				AssertIncluded(EmitWithInclude($"#Include {written} ; comment\nx := Included()\n", dir));
				File.Delete(Path.Combine(dir, name));
			}

			var main = Path.Combine(dir, "main.ks");
			File.WriteAllText(main, "#Include %A_ScriptName%\nclass Included {\n}\nx := Included()\n");
			var (bytes, text, _) = new CompilerHelper().CompileCodeToByteArray(main, "include_self");
			Assert.That(bytes, Is.Not.Null, text);
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	// An in-memory script (stdin, or a host handing over text) has no file of its own to resolve #Include
	// against, so the base is the working directory — what A_ScriptDir reports for it. With no base at all the
	// preprocessor skipped every #Include and the lowerer took the leftover directive for one handled
	// elsewhere, so includes vanished in silence and even a missing file raised nothing.
	[Test, Category("Parser"), Category("Internal")]
	public void IncludeFromMemoryWithoutIncludeDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), "ks_include_" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(dir);

		try
		{
			var include = Path.Combine(dir, "Included.ks");
			File.WriteAllText(include, "class Included {\n}\n");
			var source = $"#include \"{include}\"\nx := Included()\n";
			AssertIncluded(EmitWithInclude(source));
			// A BOM survives a pipe where File.ReadAllText would have eaten it; unstripped it lexes as an
			// identifier and swallows the first line, which is exactly where an #Include sits.
			AssertIncluded(EmitWithInclude("\uFEFF" + source));
			AssertIncludeNotFound(EmitWithInclude($"#include \"{Path.Combine(dir, "NoSuchFile.ks")}\"\n"));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Test, Category("Parser")]
	public void RemapsCompile() => AssertCompiles(
		"a::b\n",
		"^x::^c\n",
		"Esc::CapsLock\n",
		"'::;\n",
		"\"::;\n",
		"+'::;\n",
		"*\"::a\n");

	// A comma glued to the word that starts a statement is the v1 command syntax, which AutoHotkey rejects
	// (script.cpp: the word must end at whitespace, '(' or end of line to be a call). Without this the line
	// silently reads the name as a variable and calls nothing.
	[Test, Category("Parser")]
	public void CommandCommaErrors()
	{
		const string expected = "use a comma only between parameters";
		AssertCompileError("MsgBox, \"a\"\n", expected);
		AssertCompileError("MsgBox,\"a\"\n", expected);
		AssertCompileError("x, y := 2\n", expected);
		AssertCompileError("obj := {}\nobj.Method, 1\n", expected);
		AssertCompileError("and, x := 2\n", expected);
		AssertCompileError("Goto, lbl\n", expected);
		AssertCompileError("for, k, v in Map()\n{\n}\n", expected);
	}

	// The same rule's exemptions: whitespace before the comma omits the first argument, and a chain that indexes
	// or calls ends its leading word at the '[' or '(', leaving an ordinary comma sequence.
	[Test, Category("Parser")]
	public void CommandCommaExemptionsCompile() => AssertCompiles(
		"MsgBox , \"a\"\n",
		"MsgBox   ,   \"a\"\n",
		"x := 1, y := 2\n",
		"arr := [1]\narr[1], x := 4\n",
		"arr := [1]\narr[1].b, x := 4\n",
		"foo() {\n}\nfoo(), x := 4\n",
		"obj := {}\nobj.Method , 1\n",
		"Loop Parse, \"a,b\", \",\"\n{\n}\n");

	[Test, Category("Parser")]
	public void DelimiterErrors()
	{
		AssertCompileError("x := \"hello\n", "unterminated string");
		AssertCompileError("MsgBox(\"oops)\n", "unterminated string");
		AssertCompileError("if (x) {\n\ty := 1\n", "expected '}'");
		AssertCompileError("x := (1 + 2\n", "expected ')'");
		AssertCompileError("x := [1, 2\n", "expected ']'");
	}

	[Test, Category("Parser")]
	public void DiagnosticLocation()
	{
		var (_, error) = Compile("x := (1 + 2\n");
		Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(error, @"\*:\s*Line\s+3\s+Col\s+1|3:1"), error);
	}

	[Test, Category("Parser")]
	public void ImportErrors()
	{
		AssertCompileError(
			"EncodeDecodeURI(str){\n#import Ks { Clr } static Web := Clr.Load(\"x\") return Web.Url(str)\n}\n",
			"after #import");
		AssertCompileError("#import Ks return 1\n", "after #import");
		AssertCompileError("#import \"A\", \"B\"\n", "after #import");
		AssertCompileError("#import \"D\" { x } as Y\n", "after #import");
		AssertCompileError("#import M { a aſ b }\n#Module M\na := 1\n", "in #import member list");
		AssertCompileError("#import Ks { Clr\n", "expected '}'");
		AssertCompileError("#import M { * as X }\n#Module M\n", "in #import member list");
		AssertCompileError("#Module Self\n#Import Export Self { * }\n", "Invalid import: *");
		AssertCompiles("#import M { a aS b, 1,, }\n#Module M\na := 1\n", "#import M {}\n#Module M\n");
	}

	[Test, Category("Parser")]
	public void DirectiveArgs()
	{
		AssertCompileError("#HotIf 1, 2\n", "accepts a single argument");
		AssertCompileError("#Warn VarUnset, Off, Extra\n", "accepts at most 2 arguments");
		AssertCompileError("#MaxThreads 4, 8\n", "accepts a single argument");
		AssertCompiles("#HotIf (1, 2)\nx::y\n#HotIf\n", "#Warn VarUnset, Off\n", "#Hotstring EndChars -,.?!\n");
	}

	[Test, Category("Parser")]
	public void InputLevel()
	{
		var parser = new ParserComponent();
		foreach (var value in new[] { "-1", "101", "0x65", "1.5", "1e2", "true", "false", "nonsense", "1tail", "1 + 2", "\"1\"", "999999999999999999999999" })
		{
			var result = parser.ValidateSyntax(new ScriptSyntaxValidationRequest
			{
				SourceText = $"#ErrorStdOut\n#Warn All, StdOut\n#InputLevel {value}\n"
			});
			Assert.That(result.Success, Is.False, value);
			Assert.That(result.Diagnostics[0].Message, Does.Contain("#InputLevel must be an integer from 0 through 100"));
			Assert.That(result.Diagnostics[0].Line, Is.EqualTo(3));
			Assert.That(result.Diagnostics[0].Column, Is.EqualTo(1));
		}

		var valid = string.Join("\n", new[] { "", "0", "100", "+1", "-0", "0x64", "+0X01", "-0x0", "50 ; comment" }
			.Select(value => $"#iNpUtLeVeL {value}")) + "\n";
		Assert.IsTrue(parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = valid }).Success);
		AssertCompiles(valid);
	}

	[Test, Category("Parser")]
	public void BooleanDirectiveArgs()
	{
		var directives = new[] { "UseHook", "SuspendExempt", "MaxThreadsBuffer", "Persistent" };
		AssertCompiles(string.Join("\n", directives.SelectMany(directive => new[]
		{
			$"#{directive}", $"#{directive} true", $"#{directive} false", $"#{directive} 1", $"#{directive} 0"
		})) + "\n");

		foreach (var directive in directives)
			AssertCompileError($"#{directive} maybe\n", "parameter must be true or false");

		AssertCompileError("#UseHook 2\n", "parameter must be true or false");
	}

	[Test, Category("Parser")]
	public void ReservedNames()
	{
		foreach (var source in new[]
		{
			"Web := Clr.Load(\"x\") return Web.Url(str)\n",
			"x := return\n",
			"static y := 1 if z\n",
			"case() {\n}\n",
			"class while {\n}\n",
			"f(return) {\n}\n",
			"cb := loop => loop\n"
		})
			AssertCompileError(source, "reserved word");
	}

	// A line starting with an operator continues the previous line. parser.ahk covers what the joined line MEANS;
	// these are the shapes that used to fail to parse at all, chiefly a statement form that ended at the break.
	[Test, Category("Parser")]
	public void LeadingOperatorContinuation() => AssertCompiles(
		"Loop 20\n\tMouseMove 10, 10, 20\n\t, Sleep 20\n",   // braceless loop body
		"MsgBox \"a\", \"b\"\n, \"c\"\n, \"d\"\n",           // several continuation lines in a row
		"MsgBox \"a\"\n, , \"c\"\n",                         // an omitted argument across the continuation
		"MsgBox \"a\"\n\n, \"b\"\n",                         // a blank line between the two
		"f() {\n\tMsgBox \"a\"\n\t, \"b\"\n}\n",             // last statement of a block, `}` after
		"ExitApp\n, 1\n",                                    // zero-arg call statement + continuation
		"a := 1\na\n?? MsgBox()\n",                          // a name alone on its line is not a call when continued
		"true\n\t? MsgBox()\n\t: \"\"\n",
		"(()\n\t=> MsgBox())()\n",
		"MsgBox\n.Call(\"x\")\n",
		"#HotIf WinActive(\"a\")\n\tand WinActive(\"b\")\nx::y\n#HotIf\n",
		"lbl:\n-1\n");                                       // a label ends its line

	// Lines are merged after #Include has spliced its file in, and a line is never continued by one from another
	// file: here merging would give x = 3 and y = 13.
	[Test, Category("Parser"), Category("Internal")]
	public void ContinuationStopsAtFileBoundary()
	{
		var dir = Path.Combine(Path.GetTempPath(), "ks_include_" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(dir);

		try
		{
			File.WriteAllText(Path.Combine(dir, "Cont.ks"), "+ 2\ny := 10\n");
			var (Bytes, Text) = EmitWithInclude("x := 1\n#include \"Cont.ks\"\n+ 3\n", dir);
			Assert.That(Bytes, Is.Not.Null, Text);
			Assert.That(Text, Does.Contain("x = 1L;"));
			Assert.That(Text, Does.Contain("y = 10L;"));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	// Compiles a script file with its includes, returning the image and the source files its locations index.
	private static (byte[] Bytes, Assembly Assembly, string[] Files) CompileFile(string main, ScriptCompilationOutput output = ScriptCompilationOutput.InMemory, string includeDir = null)
	{
		var (bytes, error, _) = new CompilerHelper().CompileCodeToByteArray(main, "sources_" + Guid.NewGuid().ToString("N"),
			output: output, includeDirOverride: includeDir, sourceIsFile: true);
		Assert.That(bytes, Is.Not.Null, error);
		var assembly = Assembly.Load(bytes);
		return (bytes, assembly, assembly.GetType(MainNamespaceName + ".Program").GetCustomAttribute<SourceFilesAttribute>().Files);
	}

	// Executable output names source files without full paths or source text, and A_LineFile on an included line
	// reports the executable. A source run carries every file's text for its indexed locations.
	[Test, Category("Parser")]
	public void CompiledSources()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks_sources_" + Guid.NewGuid().ToString("N"));
		var app = Path.Combine(root, "app");
		_ = Directory.CreateDirectory(Path.Combine(app, "Lib"));
		_ = Directory.CreateDirectory(Path.Combine(root, "shared"));
		_ = Directory.CreateDirectory(Path.Combine(root, "other"));

		try
		{
			File.WriteAllText(Path.Combine(app, "Lib", "InLib.ahk"), "InLib() => A_LineFile\n");
			File.WriteAllText(Path.Combine(root, "shared", "Helper.ahk"), "Helper() => A_LineFile\n");
			File.WriteAllText(Path.Combine(root, "other", "Helper.ahk"), "OtherHelper() => A_LineFile\n");
			var main = Path.Combine(app, "main.ahk");
			File.WriteAllText(main, "#ErrorStdOut\n#Warn All, StdOut\n#Include Lib\\InLib.ahk\n#Include ..\\shared\\Helper.ahk\n#Include ..\\other\\Helper.ahk\nx := InLib() Helper() OtherHelper()\nExplicitWhat() => Error(\"x\", -1)\n");
			var (bytes, compiled, compiledFiles) = CompileFile(main, output: ScriptCompilationOutput.Executable, includeDir: app);
			Assert.That(compiledFiles, Is.EqualTo(["./main.ahk", "./Lib/InLib.ahk", "<External>/Helper.ahk", "<External>/Helper (2).ahk"]).AsCollection);

			// String literals are UTF-16 in the assembly and attribute arguments UTF-8.
			foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
				Assert.That(bytes.AsSpan().IndexOf(encoding.GetBytes(root)), Is.EqualTo(-1), encoding.EncodingName);

			var program = compiled.GetType(MainNamespaceName + ".Program");
			s.Dispose();
			s = null;
			System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(program.TypeHandle);
			s = Script.TheScript;
			Assert.That(s.ProgramType, Is.EqualTo(program));
			Assert.IsEmpty(s.SourceLines);
			s.SetName(main);
			var module = compiled.GetType(MainNamespaceName + ".Program+__Main");
			object Call(string name) => Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(module.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Single(method => method.GetCustomAttribute<UserDeclaredNameAttribute>()?.Name == name)).CallFunc(null, []);

			foreach (var name in new[] { "InLib", "Helper", "OtherHelper" })
				Assert.That(Call(name), Is.EqualTo(A_ScriptFullPath), name);

			var error = (Error)Call("ExplicitWhat");
			Assert.That(error.Stack, Does.Contain("[ExplicitWhat]"));
			Assert.That(error.What, Is.EqualTo("-1"));

			var (_, running, files) = CompileFile(main, includeDir: app);
			Assert.That(files.Length, Is.EqualTo(4));
			Assert.That(SourceText.Lines(running), Is.EqualTo(files.Select(file => File.ReadAllText(file).Split('\n')).ToArray()));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test, Category("Parser"), Category("Internal")]
	public void AssemblyIncludedLineFileLocatesAssets()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks_assembly_sources_" + Guid.NewGuid().ToString("N"));
		var app = Path.Combine(root, "app");
		var lib = Path.Combine(root, "lib");
		var output = Path.Combine(root, "out", "nested");
		_ = Directory.CreateDirectory(app);
		_ = Directory.CreateDirectory(lib);
		_ = Directory.CreateDirectory(output);

		try
		{
			var included = Path.Combine(lib, "Theme.ahk");
			File.WriteAllText(included, "class IncludedAsset {\n static SourceFile := A_LineFile\n static Read() {\n  SplitPath(IncludedAsset.SourceFile, , &dir)\n  return FileRead(dir \"/base.css\", \"UTF-8\")\n }\n}\nAssetPath() => IncludedAsset.SourceFile\nAssetText() => IncludedAsset.Read()\n");
			File.WriteAllText(Path.Combine(lib, "base.css"), "body { color: red; }");
			var main = Path.Combine(app, "Showcase.ahk");
			File.WriteAllText(main, "#ErrorStdOut\n#Warn All, StdOut\n#Include ..\\lib\\Theme.ahk\n");
			var result = new CompilerComponent().Compile(new ScriptCompileRequest
			{
				ScriptPath = main,
				CompilationName = "showcase_" + Guid.NewGuid().ToString("N"),
				OutputDirectory = output,
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(result.Success, result.ErrorText);
			foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
				Assert.That(result.AssemblyBytes.AsSpan().IndexOf(encoding.GetBytes(root)), Is.EqualTo(-1), encoding.EncodingName);

			var artifact = Path.Combine(output, "Showcase.cks");
			File.WriteAllBytes(artifact, result.AssemblyBytes);
			var assembly = Assembly.Load(result.AssemblyBytes);
			var program = assembly.GetType(MainNamespaceName + ".Program");
			s.Dispose();
			s = null;
			System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(program.TypeHandle);
			s = Script.TheScript;
			s.SetName(artifact);
			var module = assembly.GetType(MainNamespaceName + ".Program+__Main");
			object Call(string name) => Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(module.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Single(method => method.GetCustomAttribute<UserDeclaredNameAttribute>()?.Name == name)).CallFunc(null, []);

			Assert.That(Call("AssetPath"), Is.EqualTo(included));
			Assert.That(Call("AssetText"), Is.EqualTo("body { color: red; }"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test, Category("Parser")]
	public void DistinctCaseModuleSearchDirectories()
	{
#if LINUX
		if (!OperatingSystem.IsLinux())
			Assert.Ignore("This test needs a case-sensitive filesystem.");

		var root = Path.Combine(Path.GetTempPath(), "ks_case_modules_" + Guid.NewGuid().ToString("N"));
		var first = Path.Combine(root, "Modules");
		var second = Path.Combine(root, "modules");
		var previousPath = Environment.GetEnvironmentVariable("AhkImportPath");
		_ = Directory.CreateDirectory(first);
		_ = Directory.CreateDirectory(second);

		try
		{
			var module = Path.Combine(second, "SearchTarget.ahk");
			var main = Path.Combine(root, "main.ahk");
			File.WriteAllText(module, "Value := 1\n");
			File.WriteAllText(main, "#ErrorStdOut\n#Warn All, StdOut\n#Import SearchTarget { Value }\nanswer := Value\n");
			Environment.SetEnvironmentVariable("AhkImportPath", first + ";" + second);
			var (_, _, files) = CompileFile(main);
			Assert.That(files, Has.Member(module));
		}
		finally
		{
			Environment.SetEnvironmentVariable("AhkImportPath", previousPath);
			Directory.Delete(root, true);
		}
#endif
	}

	[Test, Category("Parser")]
	public void DistinctCaseSourceFilesOnLinux()
	{
#if LINUX
		if (!OperatingSystem.IsLinux())
			Assert.Ignore("This test needs a case-sensitive filesystem.");

		var root = Path.Combine(Path.GetTempPath(), "ks_case_sources_" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(root);

		try
		{
			var main = Path.Combine(root, "Foo.ahk");
			var included = Path.Combine(root, "foo.ahk");
			File.WriteAllText(main, "#Include foo.ahk\nvalue := GetValue()\n");
			File.WriteAllText(included, "GetValue() => 42\n");
			var (_, assembly, files) = CompileFile(main);
			Assert.That(files, Is.EqualTo([main, included]).AsCollection);
			var lines = SourceText.Lines(assembly);
			Assert.That(lines[0][0], Is.EqualTo("#Include foo.ahk"));
			Assert.That(lines[1][0], Is.EqualTo("GetValue() => 42"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
#endif
	}

	// A header expression that ends in `(…)` right before the body's `{` is the header, not an anonymous
	// block-bodied function `(params) { … }`. `if`/`while`/`for` already parsed their header that way; the
	// specialized-loop arguments and `switch`'s case-sense argument did not.
	[Test, Category("Parser")]
	public void FlowHeaderEndingInParens() => AssertCompiles(
		"Loop Files \"*.txt\", \"DF\" (InStr(\"a\", \"b\") ? \"R\" : \"\") {\n\tbreak\n}\n",   // the reported case
		"Loop Parse \"a,b\", (\"\" \",\") {\n\tbreak\n}\n",                                   // trailing paren is the whole arg
		"Loop Read \"f.txt\", (\"o.txt\") {\n\tbreak\n}\n",
		"Loop Files \"*.txt\", (\"D\")\n\tbreak\n",                                           // braceless body still fine
		"switch 1, (0) {\n\tcase 1:\n\t\tbreak\n}\n",                                         // switch case-sense arg
		"Loop Files \"*.txt\", \"D\" {\n\tbreak\n}\n");                                       // unchanged plain form

	// Code/string-continuation.ahk covers what a merged continuation section produces. A section opening the file
	// merges onto nothing above it, which a script with directives on its first lines cannot show.
	[Test, Category("Parser")]
	public void ContinuationSections()
	{
		var (bytes, error) = CompileRaw("(Joinpp\nFileA\nend \"x\", \"*\"\n)\n");
		Assert.That(bytes, Is.Not.Null, error);
	}

	[Test, Category("Parser")]
	public void ContinuationOptions()
	{
		foreach (var source in new[] { "value := \"\n(%\none\n)\"\n", "value :=\n(%\n\"one\"\n)\n", "value := \"\n(Join% %\none\ntwo\n)\"\n" })
		{
			var (bytes, error) = Compile(source);
			Assert.IsNull(bytes, source);
			Assert.IsNotEmpty(error, source);
		}
	}

	[Test, Category("Parser")]
	public void MalformedInput()
	{
		foreach (var source in new[] { "\"", "(((('", "}}}", "class", "if (", "for x", "switch {", "x := [" })
			Assert.DoesNotThrow(() => Compile(source), source);
	}

	[Test, Category("Parser")]
	public void NamedArgErrors()
	{
		foreach (var source in new[]
		{
			"f(x: 1, 2)",
			"f(x: 1, , y: 2)",
			"f(x: 1, x: 2)",
			"f(x: 1, a*)",
			"f(x: a*)",
			"a[x: 1]",
			"f(\"x\": 1)",
			"f(1: 2)",
			"f(%x%: 1, 2)",
			"f(%x%: 1, , y: 2)",
			"f(%x%: 1, a*)",
			"f(%x%: a*)",
			"a[%x%: 1]"
		})
		{
			var (bytes, error) = Compile(source + "\n");
			Assert.IsNull(bytes, source + " should fail compilation.");
			Assert.IsNotEmpty(error);
		}
	}
}
