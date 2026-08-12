using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Keysharp.Tests
{
	public class ParserTests : TestRunner
	{
		private static (byte[] Bytes, string Error) Compile(string source)
		{
			var name = "parser_" + Guid.NewGuid().ToString("N");
			var (bytes, error, _) = new CompilerHelper().CompileCodeToByteArray("#ErrorStdOut\n" + source, name);
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
