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

				var withoutInclude = helper.CreateCompilationUnitFromFile(source, "include_none");
				Assert.IsFalse(withoutInclude.Unit?.ToFullString().Contains("class Included") == true);
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

		// A leading comma continues the previous line. parser.ahk covers what the joined line MEANS; these are the
		// shapes that used to fail to parse at all, chiefly a command-style call whose arguments continue.
		[Test, Category("Parser")]
		public void LeadingCommaContinuation() => AssertCompiles(
			"Loop 20\n\tMouseMove 10, 10, 20\n\t, Sleep 20\n",   // the reported case: braceless loop body
			"MsgBox \"a\", \"b\"\n, \"c\"\n, \"d\"\n",           // several continuation lines in a row
			"MsgBox \"a\"\n, , \"c\"\n",                         // an omitted argument across the continuation
			"MsgBox \"a\"\n\n, \"b\"\n",                         // a blank line between the two
			"f() {\n\tMsgBox \"a\"\n\t, \"b\"\n}\n",             // last statement of a block, `}` after
			"ExitApp\n, 1\n");                                   // zero-arg call statement + continuation

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

		// A continuation section outside a quoted string is merged as TEXT before it is parsed, so its content lines
		// become one logical line: a string can open on one and close on a later one, the Join text between them is
		// real syntax, and a name or operator can be split across them. See Code/string-continuation.ahk for what
		// each of these produces.
		[Test, Category("Parser")]
		public void ContinuationSections()
		{
			AssertCompiles(
				"Var :=\n(\n\"Quote marks are not escaped here.\nSpecify variables as follows: \" Var \"\nA line of text.\"\n)\n",  // docs example #2
				"a := Array(\n(Join,\n1\n2\n3\n)\n)\n",                    // the Join builds the argument list
				"MyVar := 1\nn :=\n(Join\nMyV\nar\n)\n",                   // one name split across the content lines
				"MyVar := 1\nt :=\n(Join\nMy\n)Var\n",                     // …and across the ')', whose trailing text joins on
				"n :=\n(JoinrL\nSt\nen(\"abcde\")\n)\n",                   // …with the Join string itself part of the name
				"v := Array(\n(Join,\na\nb\n)\n(Join,\nc\nd\n)\n)\n",      // a second section re-applies the name-character space
				"op :\n(Join\n= 5\n)\n",                                   // an operator split at the opening line
				"c :=\n(Join\n1 >\n= 1\n)\n",                              // …and between two content lines
				"v := 12\nr := v\n(\n34\n)\n",                             // a name character above the section gets a space after it
				"o := {p: 1}\nq := o\n(\n.p\n)\n",                         // …but an operator does not, so this stays member access
				"x :=\n(RTrim0 LTrim\n    \"abc\n    def\"\n)\n",          // trimming options apply inside the string too
				"y :=\n(Comments\n\"abc   ; stripped\ndef\"\n)\n",         // as does comment stripping
				"z := 1\n(\n  + 2\n)\n");                                  // the plain form is unchanged
			// A section opening the file merges onto nothing above it, so it has to bypass Compile's prefix.
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
