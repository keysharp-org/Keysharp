using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Keysharp.Tests
{
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
			StringAssert.Contains(expected, error);
		}

		private static void AssertCompiles(params string[] sources)
		{
			foreach (var source in sources)
			{
				var (bytes, error) = Compile(source);
				Assert.IsNotNull(bytes, error);
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
		private static (byte[] Bytes, string Text) EmitWithInclude(string source, string includeDir = null)
		{
			var (bytes, text, _) = new CompilerHelper().CompileCodeToByteArray(source, "inc_" + Guid.NewGuid().ToString("N"),
									   emitCode: true, includeDirOverride: includeDir, sourceIsFile: false);
			return (bytes, text);
		}

		private static void AssertIncluded((byte[] Bytes, string Text) emitted)
		{
			Assert.IsNotNull(emitted.Bytes, emitted.Text);
			StringAssert.Contains("class Included", emitted.Text);
		}

		private static void AssertIncludeNotFound((byte[] Bytes, string Text) emitted)
		{
			Assert.IsNull(emitted.Bytes, "A missing #Include must fail loudly, not be skipped.");
			StringAssert.Contains("#Include file not found", emitted.Text);
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
				var emitted = EmitWithInclude("x := 1\n#include \"Cont.ks\"\n+ 3\n", dir);
				Assert.IsNotNull(emitted.Bytes, emitted.Text);
				StringAssert.Contains("x = 1L;", emitted.Text);
				StringAssert.Contains("y = 10L;", emitted.Text);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
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
			Assert.IsNotNull(bytes, error);
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
}
