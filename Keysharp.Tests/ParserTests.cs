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
	public void LoopJumpTargets()
	{
		const string noLoop = "Break/Continue must be enclosed by a Loop.", invalid = "Parameter #1 invalid.";
		AssertCompileError("break\n", noLoop);
		AssertCompileError("F() {\ncontinue\n}\n", noLoop);
		AssertCompileError("switch 1 {\ncase 1: break\n}\n", noLoop);
		AssertCompileError("switch 1 {\ncase 1: continue\n}\n", noLoop);
		AssertCompileError("Loop {\nbreak 0\n}\n", invalid);
		AssertCompileError("Loop {\ncontinue 2\n}\n", invalid);
		AssertCompileError("Loop {\nbreak 99999999999\n}\n", invalid);
		AssertCompileError("Loop {\nbreak missing\n}\n", "Label not found in current scope: missing");
		AssertCompileError("notLoop:\nx := 1\nLoop {\nbreak notLoop\n}\n", invalid);
		AssertCompileError("F() {\nLoop {\nG() {\nbreak\n}\n}\n}\n", noLoop);
		AssertCompileError("F() {\nouter:\nLoop {\nG() {\nLoop {\nbreak outer\n}\n}\n}\n}\n", "Label not found in current scope: outer");
		AssertCompileError("F() {\nLoop {\nG() {\nLoop {\ncontinue 2\n}\n}\n}\n}\n", invalid);
		AssertCompileError("Loop {\ntry {\n} finally {\nbreak\n}\n}\n", noLoop);
		AssertCompileError("outer:\nLoop {\ntry {\n} finally {\nLoop {\nbreak outer\n}\n}\n}\n", invalid);
		AssertCompiles(
			"outer:\nLoop 2 {\nLoop 2 {\ncontinue OUTER\n}\n}\n",
			"Loop 2 {\nLoop 2 {\nbreak 2\n}\n}\n",
			"Loop {\ntry {\nthrow 1\n} catch {\nbreak\n}\n}\n",
			"Loop {\ntry {\n} finally {\nLoop {\nbreak\n}\n}\nbreak\n}\n",
			"F() {\nLoop {\nG() {\nLoop {\nbreak\n}\n}\nbreak\n}\n}\n",
			"first:\nsecond:\nLoop 2 {\nLoop 2 {\nbreak first\n}\n}\n",
			"outer:\nF() {\n}\nLoop 2 {\nLoop 2 {\nbreak outer\n}\n}\n",
			"outer:\n#SingleInstance Force\nLoop 2 {\nLoop 2 {\nbreak outer\n}\n}\n",
			"outer:\nglobal g\nLoop 2 {\nLoop 2 {\nbreak outer\n}\n}\n",
			"outer:\nF13::x := 1\n#SingleInstance Force\nglobal g\nLoop 2 {\nLoop 2 {\nbreak outer\n}\n}\n",
			"outer:\nLoop (() {\nLoop 1 {\nbreak\n}\nreturn 2\n})() {\nLoop 2 {\nbreak outer\n}\n}\n");
	}

	[Test, Category("Parser")]
	public void StaticGotoTargets()
	{
		AssertCompileError("goto(\"\")\n", "Label not found in current scope:");
		AssertCompileError("goto missing\n", "Label not found in current scope: missing");
		AssertCompileError("goto inner\nif 1 {\ninner:\nx := 1\n}\n", "A Goto must not jump into a block that doesn't enclose it.");
		AssertCompileError("if 1 {\ngoto other\n}\nif 1 {\nother:\nx := 1\n}\n", "A Goto must not jump into a block that doesn't enclose it.");
		AssertCompileError("Loop 2 {\ngoto inner\n}\nLoop 1 {\ninner:\nx := 1\n}\n", "A Goto must not jump into a block that doesn't enclose it.");
		AssertCompileError("outside:\nLoop (() {\ngoto outside\nreturn 1\n})() {\nbreak\n}\n", "Label not found in current scope: outside");
		AssertCompileError("outside:\nF() {\ngoto outside\n}\n", "Label not found in current scope: outside");
		AssertCompileError("F() {\nouter:\nG() {\ngoto outer\n}\n}\n", "Label not found in current scope: outer");
		AssertCompileError("try {\n} finally {\ngoto after\n}\nafter:\nx := 1\n", "Jumps cannot exit a FINALLY block.");
		AssertCompileError("try {\n} finally {\ntry {\n} finally {\ngoto outer\n}\nouter:\nx := 1\n}\n", "Jumps cannot exit a FINALLY block.");
		AssertCompileError("F() {\ntry {\n} finally {\nreturn 1\n}\n}\n", "Jumps cannot exit a FINALLY block.");
		AssertCompileError("try {\n} finally {\nreturn\n}\n", "Jumps cannot exit a FINALLY block.");
		AssertCompiles(
			"goto MIXED\nMixed:\nx := 1\n",
			"if 1 {\ngoto outside\n}\noutside:\nx := 1\n",
			"switch 1 {\ncase 1: goto NEXT\ncase 2:\nNext:\nx := 1\n}\n",
			"try {\ngoto after\n} finally {\n}\nafter:\nx := 1\n",
			"Loop {\ntry {\nthrow 1\n} catch {\ngoto after\n}\n}\nafter:\nx := 1\n",
			"F() {\nlocalLabel:\nx := 1\n}\ngoto outside\noutside:\nx := 2\n",
			"a:\nF() {\na:\nx := 1\n}\n",
			"try {\n} finally {\nF() {\ngoto OWN\nOwn:\nreturn 1\n}\ngoto HERE\nHere:\nx := 1\n}\ngoto after\nafter:\nx := 2\n");
	}

	[Test, Category("Parser"), Category("Internal")]
	public void CallableEndpoints()
	{
		string[] sources =
		[
			"return\nclass AfterReturn {\n}\n",
			"goto done\nclass Skipped {\n}\ndone:\nx := Skipped\n",
			"F(n) {\nif n\nreturn 1\nelse\nreturn 2\n}\n",
			"F() {\nthrow Error()\n}\n",
			"F() {\ntry {\nreturn 1\n} catch {\nreturn 2\n}\n}\n",
			"F() {\ntry {\nreturn 1\n} finally {\nx := 1\n}\n}\n",
			"return\n",
			"F() {\nx := 0\ntry {\nthrow Error()\n} catch {\nx := 1\n} else {\nx := 2\n}\nreturn x\n}\n",
			"F(n) {\nif n\nreturn 1\n}\n",
			"F(n) {\nswitch n {\ncase 1: return 1\ndefault: return 2\n}\n}\n",
			"F(n) {\nswitch n {\ncase 1: return 1\n}\n}\n",
			"F(n) {\nswitch n {\ncase 1: return 1\ndefault: n := 2\n}\n}\n",
			"F(n, mode) {\nswitch n, mode {\ncase 1: return 1\ndefault: return 2\n}\n}\n",
			"F(n) {\nswitch n {\ncase 1:\nswitch n {\ncase 1: return 1\ndefault: return 2\n}\ndefault: return 3\n}\n}\n",
			"F() {\ntry {\nreturn 1\n} catch {\n}\n}\n"
		];
		var helper = new CompilerHelper();
		foreach (var source in sources)
		{
			var name = "endpoints_" + Guid.NewGuid().ToString("N");
			var compilation = helper.CreateCompilationUnitFromFile("#ErrorStdOut\n#Warn All, StdOut\n" + source, name, sourceIsFile: false);
			Assert.IsFalse(compilation.Errors.HasErrors, source);
			var (diagnostics, error) = helper.DiagnoseFromTree(compilation, name, AppContext.BaseDirectory);
			Assert.IsNull(error, source);
			var failures = diagnostics.Where(diagnostic => diagnostic.Id == "CS0162" || diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
			Assert.That(failures, Is.Empty, source + "\n" + string.Join("\n", failures.Select(diagnostic => diagnostic.ToString())));
		}
	}

	[Test, Category("Parser"), Category("Internal")]
	public void GeneratedHidingKeepsInlineWarnings()
	{
		const string source = "#ErrorStdOut\n#Warn All, StdOut\n"
			+ "class HidingBase {\nMethod() => 1\n#CSharp\npublic static object Native(object @this) => 1L;\n#EndCSharp\n}\n"
			+ "class HidingDerived extends HidingBase {\nMethod() => 2\n#CSharp\npublic static object Native(object @this) => 2L;\n#EndCSharp\n}\n";
		var helper = new CompilerHelper();
		var compilation = helper.CreateCompilationUnitFromFile(source, "hiding_warnings", sourceIsFile: false);
		Assert.IsFalse(compilation.Errors.HasErrors);
		var (diagnostics, error) = helper.DiagnoseFromTree(compilation, "hiding_warnings", AppContext.BaseDirectory);
		Assert.IsNull(error);
		Assert.That(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
		var hiding = diagnostics.Where(d => d.Id == "CS0108").ToArray();
		Assert.That(hiding, Has.Length.EqualTo(1));
		Assert.That(hiding[0].Location.SourceTree?.FilePath, Does.Contain(".inline."));
	}

	// Equal compilation inputs must produce the same cache identity.
	[Test, Category("Parser")]
	public void DeterministicCompile()
	{
		const string source = "#ErrorStdOut\n#Warn All, StdOut\nx := (a) => a + 1\nF() {\n\ty := 1\n\tG() => y\n\treturn G\n}\nclass C {\n\tM() => 1\n}\nx := F()() C().M()\n";
		var (first, firstError, _) = new CompilerHelper().CompileCodeToByteArray(source, "deterministic_compile");
		var (second, secondError, _) = new CompilerHelper().CompileCodeToByteArray(source, "deterministic_compile");
		Assert.That(first, Is.Not.Null, firstError);
		Assert.That(second, Is.Not.Null, secondError);
		Assert.That(second, Is.EqualTo(first));

		var (changed, changedError, _) = new CompilerHelper().CompileCodeToByteArray(source + "x := 2\n", "deterministic_compile");
		Assert.That(changed, Is.Not.Null, changedError);
		Assert.That(Assembly.Load(changed).ManifestModule.ModuleVersionId,
			Is.Not.EqualTo(Assembly.Load(first).ManifestModule.ModuleVersionId));
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

	// A class may be declared in a block outside any function, but not inside one, as in AHK.
	[Test, Category("Parser")]
	public void ClassInFunction()
	{
		AssertCompileError("F() {\n\t{\n\t\tclass C {\n\t\t}\n\t}\n}\n", "Functions cannot contain classes.");
		AssertCompileError("^a:: {\n\tclass C {\n\t}\n}\n", "Functions cannot contain classes.");
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

	// As in AutoHotkey, a jump target is the text after the keyword as written, or a constant in a `(` written directly after
	// it. A level is an IsNumeric integer read with _ttoi, and break or continue cannot take a computed target.
	[Test, Category("Parser")]
	public void JumpTargetsAsWritten()
	{
		const string invalid = "Parameter #1 invalid.";
		AssertCompileError("outer:\nLoop {\n\tbreak \"outer\"\n}\n", "Label not found in current scope: \"outer\"");
		AssertCompileError("outer:\nLoop {\n\tbreak (outer)\n}\n", "Label not found in current scope: (outer)");
		AssertCompileError("Loop {\n\tbreak -1\n}\n", "Label not found in current scope: -1");
		AssertCompileError("Loop {\n\tbreak %x%\n}\n", "Label not found in current scope: %x%");
		AssertCompileError("outer:\nLoop {\n\tbreak(outer)\n}\n", invalid);
		AssertCompileError("Loop {\n\tLoop {\n\t\tbreak(1+1)\n\t}\n}\n", invalid);
		AssertCompileError("Loop {\n\tcontinue(2)\n}\n", invalid);
		AssertCompileError("Loop {\n\tLoop {\n\t\tbreak 0x2\n\t}\n}\n", invalid);
		AssertCompileError("Loop {\n\tbreak 99999999999\n}\n", invalid);
		AssertCompileError("goto \"lbl\"\nlbl:\nx := 1\n", "Label not found in current scope: \"lbl\"");
		AssertCompiles(
			"outer:\nLoop {\n\tbreak(\"outer\")\n}\n",
			"outer:\nLoop {\n\tbreak((\"outer\"))\n}\n",
			"Loop {\n\tLoop {\n\t\tbreak((2))\n\t}\n}\n",
			"Loop {\n\tLoop {\n\t\tbreak(0x2)\n\t}\n}\n",
			"Loop {\n\tLoop {\n\t\tcontinue(2)\n\t}\n}\n",
			"Loop {\n\tcontinue(1)\n}\n",
			"Loop {\n\tLoop {\n\t\tbreak +2\n\t}\n}\n",
			"goto(\"lbl\")\nlbl:\nx := 1\n",
			"v := \"lbl\"\ngoto(v)\nlbl:\nx := 1\n");
	}

	// As in AutoHotkey, a label may be declared once per function, whether a goto, a break or nothing names it.
	[Test, Category("Parser")]
	public void DuplicateLabel()
	{
		AssertCompileError("outer:\nLoop 2 {\n\tbreak outer\n}\nouter:\nLoop 2 {\n\tbreak outer\n}\n", "Duplicate label: outer");
		AssertCompileError("F() {\n\tif 1 {\n\t\tx:\n\t} else {\n\t\tX:\n\t}\n}\n", "Duplicate label: X");
	}

	// An Until after a loop body which cannot complete never runs, but its errors are reported all the same.
	[Test, Category("Parser")]
	public void UntilAfterUnfinishedBody() =>
		AssertCompileError("SomeFunc() {\n}\nLoop {\n\treturn\n}\nUntil SomeFunc := 0\n", "This Func cannot be assigned a value");

	[Test, Category("Parser")]
	public void DiagnosticLocation()
	{
		var (_, error) = Compile("x := (1 + 2\n");
		Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(error, @"\*:\s*Line\s+3\s+Col\s+1|3:1"), error);
	}

	[Test, Category("Parser")]
	public void ImportErrors()
	{
		foreach (var source in new[]
		{
			"Foo() {\n#Import Export KS { Cosh }\n}\n",
			"Foo() => {\n#Import Export KS { Cosh }\nValue: 1\n}\n",
			"class C {\nMethod() {\n#Import Export KS { Cosh }\n}\n}\n",
			"class C {\nMethod() => {\n#Import Export KS { Cosh }\nValue: 1\n}\n}\n",
			"class C {\n+(Other) {\n#Import Export KS { Cosh }\n}\n}\n",
			"class C {\n+(Other) => {\n#Import Export KS { Cosh }\nValue: 1\n}\n}\n",
			"class C {\nProperty {\nget {\n#Import Export KS { Cosh }\n}\n}\n}\n",
			"class C {\nProperty {\nget => {\n#Import Export KS { Cosh }\nValue: 1\n}\n}\n}\n",
			"class C {\nProperty {\nset {\n#Import Export KS { Cosh }\n}\n}\n}\n",
			"class C {\nProperty {\nset => {\n#Import Export KS { Cosh }\nValue: 1\n}\n}\n}\n",
			"class C {\nProperty => {\n#Import Export KS { Cosh }\nValue: 1\n}\n}\n",
			"Holder := () {\n#Import Export KS { Cosh }\n}\n",
			"Holder := () => {\n#Import Export KS { Cosh }\nValue: 1\n}\n",
			"^F12::{\n#Import Export KS { Cosh }\n}\n",
			"^F12:: Value := {\n#Import Export KS { Cosh }\nValue: 1\n}\n",
			":X:word::{\n#Import Export KS { Cosh }\n}\n",
			":X:word:: Value := {\n#Import Export KS { Cosh }\nValue: 1\n}\n",
		})
			AssertCompileError(source, "after #import");
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
		var previousPath = Environment.GetEnvironmentVariable("AhkLibPath");
		_ = Directory.CreateDirectory(first);
		_ = Directory.CreateDirectory(second);

		try
		{
			var module = Path.Combine(second, "SearchTarget.ahk");
			var main = Path.Combine(root, "main.ahk");
			File.WriteAllText(module, "Value := 1\n");
			File.WriteAllText(main, "#ErrorStdOut\n#Warn All, StdOut\n#Import SearchTarget { Value }\nanswer := Value\n");
			Environment.SetEnvironmentVariable("AhkLibPath", first + ";" + second);
			var (_, _, files) = CompileFile(main);
			Assert.That(files, Has.Member(module));
		}
		finally
		{
			Environment.SetEnvironmentVariable("AhkLibPath", previousPath);
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
		"switch 1, (0) {\n\tcase 1:\n\t\tx := 1\n}\n",                                        // switch case-sense arg
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

	// The first warning a compilation reports makes Roslyn compile its diagnostics machinery, a cost paid on every cold
	// compile, so lowered code compiles without one unless the script's own code is at fault. Each source holds only code
	// its author means to run.
	[Test, Category("Parser"), Category("Internal")]
	public void WarningFreeLowering()
	{
		string[] sources =
		[
			// #ErrorStdOut and #SuspendExempt with nothing to apply to, then with a hotkey and a hotstring.
			"""
			#SuspendExempt
			x := 1
			""",
			"""
			#SuspendExempt
			F1::x := 1
			#SuspendExempt false
			::btw::by the way
			""",
			// Loop bodies which never complete, a try body which throws before its else, and a switch in a loop whose cases
			// continue and break the loop by its label and go to a label in another case.
			"""
			First(list) {
				for item in list
					return item
				until true
				Loop
					break
				while true
					throw Error()
			}
			Fallback() {
				try
					throw Error()
				catch
					n := 1
				else
					n := 2
				return n
			}
			outer:
			Loop 3 {
				switch A_Index {
					case 1: continue outer
					case 2: goto Later
					case 3:
						Later:
						break outer
				}
			}
			x := First([1]) Fallback()
			""",
			// Instance and static initialization alongside inherited wrappers.
			"""
			class Base {
				Method() => 1
				Prop => 1
				class Inner {
				}
			}
			class Derived extends Base {
				value := 1
				static shared := 2
				Method() => 2
				Prop => 2
				class Inner {
					value := 3
				}
			}
			x := Derived().Method() Derived.shared
			""",
			// Static initializers, one static declared twice, and nested functions which capture nothing, alone, recursive,
			// mutual or inside a closure, next to closures, in functions, methods, field initializers and the auto-execute
			// section.
			"""
			Statics(n) {
				static items := [], first := n
				static items := Map()
				return first items.Count
			}
			Nesting(a) {
				Twice(x) => x * 2
				Fact(k) => k <= 1 ? 1 : k * Fact(k - 1)
				Even(k) => k = 0 ? 1 : Odd(k - 1)
				Odd(k) => k = 0 ? 0 : Even(k - 1)
				Late() => Capturing()
				Capturing() {
					Counted() {
						static made := []
						return made.Length
					}
					return a Counted()
				}
				return Twice(a) Fact(3) Even(4) Late() ((y) => y)(1)
			}
			class Holder {
				field := () => 1
				static shared := (*) => 2
				Method(p) {
					static cache := Map()
					Inner(q) => q
					return Inner(p) cache.Count
				}
			}
			top := (z) => z
			x := Statics(1) Nesting(2) top(3) Holder().Method(4)
			""",
			// Untils which never run, needing a temporary or an anonymous or named fat arrow, then a class and directives after
			// the auto-execute section's return.
			"""
			DeadUntils(a, b) {
				i := 0
				Loop
					return i
				until i++ > 3
				Loop
					return a
				until a && b
				Loop
					return 1
				until IsObject(() => 1)
				Loop
					return 2
				until (Named() => 3)
				return Named()
			}
			x := DeadUntils(1, 2)
			Loop
				break
			until x || IsObject(() => 1)
			return
			#UseHook
			#MaxThreadsPerHotkey 2
			class AfterReturn {
			}
			"""
		];
		var helper = new CompilerHelper();
		var failures = new List<string>();

		foreach (var source in sources)
		{
			var name = "lowering_" + Guid.NewGuid().ToString("N");
			var compilation = helper.CreateCompilationUnitFromFile("#ErrorStdOut\n#Warn All, StdOut\n" + source, name, sourceIsFile: false);
			Assert.That(compilation.Errors.HasErrors, Is.False, source);
			var (diagnostics, error) = helper.DiagnoseFromTree(compilation, name, AppContext.BaseDirectory);
			Assert.IsNull(error, source);
			var reported = diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).ToArray();

			if (reported.Length > 0)
				failures.Add(source + "\n" + string.Join("\n", reported.Select(d => d.ToString())));
		}

		Assert.That(failures, Is.Empty, string.Join("\n\n", failures));
	}
}
