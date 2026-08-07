using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class DirectivesTests : TestRunner
	{
		[Test, Category("Directives")]
		public void AsmInfo()
		{
			var scriptpath = string.Concat(path, "directive-asminfo", ".ahk");
			var exepath = "./directive-asminfo.exe";
			_ = RunScript(scriptpath, "directive-asminfo", false, true);
			Assert.IsTrue(File.Exists(exepath));
			var asm = Assembly.LoadFrom(exepath);
			var title = asm.GetCustomAttribute<AssemblyTitleAttribute>();
			Assert.IsNotNull(title);
			Assert.AreEqual(title.Title, "This is a title!");
			//
			var desc = asm.GetCustomAttribute<AssemblyDescriptionAttribute>();
			Assert.IsNotNull(desc);
			Assert.AreEqual(desc.Description, "This is a description!");
			//
			var config = asm.GetCustomAttribute<AssemblyConfigurationAttribute>();
			Assert.IsNotNull(config);
			Assert.AreEqual(config.Configuration, "This is a config!");
			//
			var comp = asm.GetCustomAttribute<AssemblyCompanyAttribute>();
			Assert.IsNotNull(comp);
			Assert.AreEqual(comp.Company, "This is a company!");
			//
			var prod = asm.GetCustomAttribute<AssemblyProductAttribute>();
			Assert.IsNotNull(prod);
			Assert.AreEqual(prod.Product, "This is a product!");
			//
			var copy = asm.GetCustomAttribute<AssemblyCopyrightAttribute>();
			Assert.IsNotNull(copy);
			Assert.AreEqual(copy.Copyright, "This is a copyright!");
			//
			var tm = asm.GetCustomAttribute<AssemblyTrademarkAttribute>();
			Assert.IsNotNull(tm);
			Assert.AreEqual(tm.Trademark, "This is a trademark!");
			//
			var ver = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
			Assert.IsNotNull(ver);
			Assert.AreEqual(ver.Version, "9.8.7.6");
			//
			// #AssemblyName sets the assembly's identity rather than an attribute, so it is read from the name and
			// not via GetCustomAttribute. It overrides the name derived from the script file.
			Assert.AreEqual("ThisIsAnAsmName", asm.GetName().Name);
			//
			Assert.IsTrue(TestScript("directive-asminfo", false));
		}

		[Test, Category("Directives")]
		public void IncludeAsmInfo() => Assert.IsTrue(TestScript("directive-include-asminfo", false));

		[Test, Category("Directives")]
		public void Include() => Assert.IsTrue(TestScript("directive-include", false));

		[Test, Category("Directives")]
		public void Defines() => Assert.IsTrue(TestScript("directive-defines", true));

		[Test, Category("Directives")]
		public void Misc() => Assert.IsTrue(TestScript("directive-misc", false));

		[Test, Category("Directives")]
		public void ConditionalDirectiveErrors()
		{
			// Every case below used to be accepted silently, and each one silently changed which code compiled:
			// a condition the grammar does not cover was dropped (taking the wrong branch), an unmatched #if
			// swallowed the rest of the file, and a stray/duplicate #else..#endif was ignored outright.
			static string Diag(string src) => string.Join("; ", Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src).diagnostics);

			static void Rejects(string src, string expect)
			{
				var d = Diag(src);
				Assert.IsTrue(d.Contains(expect, StringComparison.OrdinalIgnoreCase),
					$"expected '{expect}' for {src.Replace("\n", "\\n")} but got: {(d.Length == 0 ? "no diagnostic at all" : d)}");
			}

			// Conditions outside the supported grammar. defined() and == are the dangerous ones: they look like C
			// but evaluated to a silent false/true, so the wrong branch compiled with nothing to notice it by.
			Rejects("#if defined(WINDOWS)\nx:=1\n#endif\n", "unexpected '('");
			Rejects("#if WINDOWS == 0\nx:=1\n#endif\n", "unexpected '=='");
			Rejects("#if\nx:=1\n#endif\n", "requires a condition");
			Rejects("#if (WINDOWS\nx:=1\n#endif\n", "missing ')'");
			Rejects("#if !\nx:=1\n#endif\n", "incomplete");
			// A dead branch's condition is validated too, so a typo is caught on every platform and not just on
			// the one that happens to take the branch.
			Rejects("#if WINDOWS\nx:=1\n#elif defined(LINUX)\nx:=2\n#endif\n", "unexpected '('");

			// #Else/#EndIf take no condition. Swallowing one silently compiled the block on every platform: `#else OSX`
			// reads as "the OSX case" but is just #else.
			Rejects("#if LINUX\nx:=1\n#else OSX\nx:=2\n#endif\n", "#else takes no condition");
			Rejects("#if WINDOWS\nx:=1\n#endif WINDOWS == 1\n", "#endif takes no condition");

			// Conditions are evaluated even in dead branches, so an unbounded recursion there used to kill the process
			// with an uncatchable StackOverflowException instead of reporting anything.
			Rejects($"#if LINUX\n#if {new string('(', 5000)}WINDOWS{new string(')', 5000)}\nx:=1\n#endif\n#endif\n", "nested too deeply");

			// Unbalanced blocks.
			Rejects("#if WINDOWS\nx:=1\n", "without a matching #EndIf");
			Rejects("#if LINUX\nx:=1\n", "without a matching #EndIf");
			Rejects("#endif\n", "#EndIf without a matching #If");
			Rejects("#else\n#endif\n", "#Else without a matching #If");
			Rejects("#elif WINDOWS\n#endif\n", "#ElIf without a matching #If");
			Rejects("#if LINUX\n#else\n#else\n#endif\n", "duplicate #Else");
			Rejects("#if LINUX\n#else\n#elif WINDOWS\n#endif\n", "#ElIf after #Else");

			// Valid forms must still compile: the whole point is to reject only what was already broken.
			foreach (var ok in new[]
			{
				"#if WINDOWS\nx:=1\n#elif LINUX\nx:=2\n#else\nx:=3\n#endif\n",
				"#if (((WINDOWS || LINUX || OSX) && 1))\nx:=1\n#endif\n",
				"#if !NOT_DEFINED_ANYWHERE\nx:=1\n#endif\n",
				"#if WINDOWS and not LINUX\nx:=1\n#endif\n",
				"x := (\n#if WINDOWS\n1\n#else\n2\n#endif\n)\n",       // a block may split one statement
				"#if WINDOWS\n#if LINUX\nx:=1\n#else\nx:=2\n#endif\n#endif\n",
			})
				Assert.IsEmpty(Diag(ok), "valid conditional rejected: " + ok.Replace("\n", "\\n"));
		}

		[Test, Category("Directives")]
		public void WarningDirectiveIsNonFatal()
		{
			// #Warning is #Error's non-fatal sibling: reported at compile time, but the build still produces a unit.
			static (object unit, string[] diags, string[] warns) Lower(string src)
			{
				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new Keysharp.Parsing.Syntax.Lowerer();
				var unit = lowerer.Build(prog, "Test");
				return (unit, lowerer.Diagnostics.ToArray(), lowerer.CompileWarnings.ToArray());
			}

			var w = Lower("#Warning untested on macOS\nx := 1\n");
			Assert.IsNotNull(w.unit, "#Warning must not abort the compile");
			Assert.IsEmpty(w.diags, "#Warning must not produce an error");
			Assert.AreEqual(1, w.warns.Length, "expected exactly one warning");
			// Positioned like any other diagnostic, so it points at the directive rather than the top of the script.
			Assert.AreEqual("1:1: untested on macOS", w.warns[0]);
			Assert.AreEqual("3:1: on line three", Lower("x := 1\ny := 2\n#Warning on line three\n").warns[0]);

			// The message is free-form English text, captured verbatim: commas do not separate arguments, and an
			// apostrophe or a brace is literal. Lexed as code instead, "don't" is an unterminated string and a lone
			// "{" swallows every following line — silently, since #Warning does not fail the build.
			Assert.AreEqual("1:1: a, b, c", Lower("#Warning a, b, c\n").warns[0]);
			Assert.AreEqual("1:1: don't use this", Lower("#Warning don't use this\nx := 1\n").warns[0]);

			var brace = Lower("#Warning fix the { in ParseFoo\nx := 1\ny := 2\n");
			Assert.AreEqual("1:1: fix the { in ParseFoo", brace.warns[0]);
			Assert.IsEmpty(brace.diags, "a brace in the message must not consume the rest of the script");

			// A bare #Warning still says something rather than emitting an empty line.
			Assert.AreEqual("1:1: #Warning directive", Lower("#Warning\n").warns[0]);

			// Conditional compilation applies: a warning in a dead branch never fires.
			Assert.IsEmpty(Lower("#if LINUX\n#Warning dead\n#endif\nx := 1\n").warns,
				"a #Warning inside an excluded branch must not be reported");

			// Control: #Error remains fatal, and does not land in Warnings.
			var e = Lower("#Error nope\nx := 1\n");
			Assert.IsNull(e.unit, "#Error must abort the compile");
			Assert.IsNotEmpty(e.diags, "#Error must produce an error");
			Assert.IsEmpty(e.warns);
		}

		[Test, Category("Directives")]
		public void DefineSwitch()
		{
			// Symbols belong to a compilation, not to the process: they are passed down to the parse rather than read
			// from ambient state, so a nested compile (Ks.RunScript) can choose its own.
			var script = Path.GetTempFileName();

			try
			{
				File.WriteAllText(script, "x := 1\n");

				static bool BranchTaken(string symbol, params string[] defines)
				{
					// The #if body is a syntax error, so it compiles cleanly only when the branch is excluded.
					var (_, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics($"#if {symbol}\n}} }} }}\n#endif\n", null, null, defines);
					return diags.Count > 0;
				}

				Assert.IsFalse(BranchTaken("FEATURE_X"), "an undefined symbol should be false");
				Assert.IsTrue(BranchTaken("FEATURE_X", "FEATURE_X"), "a supplied symbol should be true");
				Assert.IsTrue(BranchTaken("feature_x", "FEATURE_X"), "symbols are case-insensitive, like #Define");
				Assert.IsFalse(BranchTaken("FEATURE_Y", "FEATURE_X"), "only the supplied symbol should be defined");

				// The switch itself: accepted forms, comma lists, and rejection of a value-looking argument. The
				// parsed symbols ride on the command, like every other switch — nothing is published process-wide.
				// "/" is a switch prefix on Windows only: elsewhere a leading "/" begins an absolute path, so just the
				// AutoHotkey-compatible run options (/force, /restart, ...) are matched there, and --define is a
				// Keysharp addition rather than one of those.
				var forms = new List<string> { "--define:A_SYM", "-define:A_SYM" };
#if WINDOWS
				forms.Add("/define:A_SYM");
#endif

				foreach (var form in forms)
				{
					var cmd = Keysharp.Internals.Scripting.Runner.Parse([form, script]);
					Assert.IsNull(cmd.ErrorText, $"{form} should parse; got: {cmd.ErrorText}");
					Assert.Contains("A_SYM", cmd.Defines, $"{form} should define A_SYM");
				}

#if !WINDOWS
				// The other half of that rule: the slash spelling must NOT be swallowed as a switch where it is a
				// legitimate path, otherwise a script named like a switch could never be run.
				Assert.IsEmpty(Keysharp.Internals.Scripting.Runner.Parse(["/define:A_SYM", script]).Defines,
					"/define: must not be treated as a switch on a platform where it is a valid path");
#endif

				// A comma list and a repeated switch both accumulate.
				var multi = Keysharp.Internals.Scripting.Runner.Parse(["--define:P,Q", "--define:R", script]);
				foreach (var sym in new[] { "P", "Q", "R" })
					Assert.Contains(sym, multi.Defines, $"{sym} should be defined");

				// One command line's symbols cannot reach another.
				Assert.IsEmpty(Keysharp.Internals.Scripting.Runner.Parse([script]).Defines,
					"a command line without --define should carry no symbols");

				// A precompiled assembly had its conditionals resolved when it was built, so it carries none.
				Assert.IsEmpty(Keysharp.Internals.Scripting.Runner.Parse(["--define:X", "--asm", script]).Defines,
					"a RunAssembly command should carry no symbols");

				// Symbols carry no value, so a name that could never be written in an #if is rejected outright
				// rather than defining something unusable. A colon-less --define is a typo, not a script name.
				foreach (var bad in new[] { "--define:FOO=1", "--define:9BAD", "--define:has space", "--define:a-b", "--define" })
				{
					var cmd = Keysharp.Internals.Scripting.Runner.Parse([bad, script]);
					Assert.IsNotNull(cmd.ErrorText, $"{bad} should be rejected");
					Assert.IsTrue(cmd.ErrorText.Contains("--define", StringComparison.Ordinal), $"{bad} should name the switch; got: {cmd.ErrorText}");
				}
			}
			finally
			{
				try { File.Delete(script); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void DefinesArePerCompilation()
		{
			// Symbols are an argument to a compilation, not process state, so two compiles in the same process can
			// disagree — which is what lets Ks.RunScript compile a nested script with its own set. Asserted on the
			// generated C# (emitCode: true) so nothing has to run.
			static string Compile(string src, params string[] defines)
			{
				var (arr, code) = new CompilerHelper().CompileCodeToByteArray(src, "definetest", null, false, true, false, defines);
				Assert.IsNotNull(arr, code);
				return code;
			}

			const string src = "#if FEATURE_X\nx := \"on-marker\"\n#else\nx := \"off-marker\"\n#endif\n";

			Assert.IsTrue(Compile(src, "FEATURE_X").Contains("on-marker"), "the supplied symbol should select its branch");
			Assert.IsTrue(Compile(src).Contains("off-marker"), "no symbols should select the #else branch");
			// Back-to-back in one process, in both directions: neither compile can leak into the other.
			Assert.IsTrue(Compile(src, "FEATURE_X").Contains("on-marker"), "a later compile must not inherit the previous one's absence of symbols");
			Assert.IsTrue(Compile(src).Contains("off-marker"), "a later compile must not inherit the previous one's symbols");
		}

		[Test, Category("Directives")]
		public void RunScriptOptionsSplitCompileTimeFromRuntime()
		{
			// Ks.RunScript takes a command line, not a bespoke defines list. It compiles in-process and pipes only the
			// resulting bytes to the launcher, so --define has to be applied HERE and everything else forwarded there.
			static (string[] defines, string[] rest, string error) Split(params string[] args)
			{
				var err = Keysharp.Internals.Scripting.Runner.SplitDefines(args, out var d, out var r);
				return (d.ToArray(), r.ToArray(), err);
			}

			// SplitDefines has to agree with Runner.Parse about what is a switch at all, so the slash form is
			// extracted only where Parse would also have accepted it — on Windows. Elsewhere "/define:A,B" is an
			// ordinary path-shaped argument and must be forwarded verbatim rather than silently eaten.
			var mixed = Split("--define:FEATURE_X", "--force", "/define:A,B", "--errorstdout");
			Assert.IsNull(mixed.error);
#if WINDOWS
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "FEATURE_X", "A", "B" }, mixed.defines, "every --define form should be extracted");
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--force", "--errorstdout" }, mixed.rest, "other switches must be forwarded untouched");
#else
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "FEATURE_X" }, mixed.defines, "only the dash forms are switches here");
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--force", "/define:A,B", "--errorstdout" }, mixed.rest, "a path-shaped argument must be forwarded untouched");
#endif

			// Nothing to extract: the whole command line is forwarded.
			var none = Split("--force", "--restart");
			Assert.IsEmpty(none.defines);
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--force", "--restart" }, none.rest);

			// A bad symbol is reported rather than forwarded as if it were an ordinary switch.
			Assert.IsNotNull(Split("--define:FOO=1").error, "an invalid symbol name should be rejected");

			// The string form of RunScript's Options: double quotes group and are removed, so a switch value containing
			// spaces survives as ONE argument. Split on whitespace alone, `--include "My include.ahk"` becomes
			// `--include`, `"My`, `include.ahk"` — three broken arguments carrying literal quotes.
			var split = typeof(Keysharp.Builtins.Ks).GetMethod("SplitCommandLine", BindingFlags.NonPublic | BindingFlags.Static);
			List<string> Args(object o) => (List<string>)split.Invoke(null, [o]);

			NUnit.Framework.Legacy.CollectionAssert.AreEqual(
				new[] { "--define:FEATURE_X", "--include", "My include.ahk", "--force" },
				Args(@"--define:FEATURE_X --include ""My include.ahk"" --force"),
				"a quoted argument containing spaces must stay a single argument");

			// Quotes group anywhere in an argument, not just around the whole of it.
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--include=My include.ahk" }, Args(@"--include=""My include.ahk"""));
			// A deliberate empty argument survives, so the arguments after it keep their positions.
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--a", "", "--b" }, Args(@"--a """" --b"));
			// Tabs and runs of spaces separate exactly like single spaces.
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--a", "--b" }, Args("  --a \t\t --b  "));
			// An unterminated quote takes the rest of the line rather than dropping it.
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--include", "My include.ahk" }, Args(@"--include ""My include.ahk"));
			// An Array element is already one argument, so it needs no quoting even with spaces in it.
			NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "--include", "My include.ahk" },
				Args(new Keysharp.Builtins.Array(["--include", "My include.ahk"])));
		}

		[Test, Category("Directives")]
		public void DefinesReachImportedModules()
		{
			// A module file is parsed separately from the script that imports it, and is compiled once and shared by
			// every importer — so an importer's own #define cannot reach it, and the supplied symbols must.
			var dir = Path.Combine(Path.GetTempPath(), "ks_moddef_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				File.WriteAllText(Path.Combine(dir, "DefMod.ahk"),
								  "#if FEATURE_X\nexport Which() => \"module-on-marker\"\n#else\nexport Which() => \"module-off-marker\"\n#endif\n");
				var main = Path.Combine(dir, "defmain.ahk");
				// The main script also #defines the symbol, to prove that is NOT what reaches the module.
				File.WriteAllText(main, "#define FEATURE_X\n#import \"DefMod\" { Which }\nx := Which()\n");

				string Compile(params string[] defines)
				{
					var (arr, code) = new CompilerHelper().CompileCodeToByteArray(main, "defmain", null, false, true, false, defines);
					Assert.IsNotNull(arr, code);
					return code;
				}

				Assert.IsTrue(Compile().Contains("module-off-marker"),
					"an importer's own #define must not reach the module it imports");
				Assert.IsTrue(Compile("FEATURE_X").Contains("module-on-marker"),
					"the compilation's symbols must reach the module it imports");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void RequiresCapability()
		{
			// `#Requires capability <names>` must lower to a RequireCapabilities(...) call in the auto-exec
			// section. Unlike the runtime RequestCapabilities builtin, denial of a declared hard requirement
			// terminates startup instead of allowing the script to continue without the required permission.
			// A bare version requirement (`#Requires AutoHotkey v2.0`) must NOT emit one. Assert on the
			// generated C# (emitCode: true) so the check never contacts the permission daemon.
			var ch = new CompilerHelper();

			var (arr, code) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\n#Requires capability ScreenCapture, InputMonitoring\nx := 1\n",
				"reqcap-emit", null, false, true);
			Assert.IsNotNull(arr, code);
			Assert.IsTrue(code.Contains("RequireCapabilities(\"ScreenCapture, InputMonitoring\")"),
				"the capability directive should emit a RequireCapabilities call; generated:\n" + code);

			// The plural alias also works.
			var (arrPl, codePl) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\n#Requires capabilities InputMonitoring\nx := 1\n",
				"reqcap-plural", null, false, true);
			Assert.IsNotNull(arrPl, codePl);
			Assert.IsTrue(codePl.Contains("RequireCapabilities(\"InputMonitoring\")"),
				"the plural `#Requires capabilities` alias should emit a RequireCapabilities call");

			// Control: a version-only #Requires must NOT emit a capability request.
			var (arrNone, codeNone) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\nx := 1\n", "reqcap-none", null, false, true);
			Assert.IsNotNull(arrNone, codeNone);
			Assert.IsFalse(codeNone.Contains("RequireCapabilities"),
				"a version-only #Requires must not emit RequireCapabilities");
		}

		[Test, Category("Directives")]
		public void WarnQuotesTheFileTheLineCameFrom()
		{
			// A #Warn line number counts from the file the offending line is IN, but the dialog excerpt was always
			// read from the MAIN script — so a warning raised in an #included file quoted whatever unrelated text the
			// main script happened to have at that line number, and named no file. Both the VarUnset and the
			// Unreachable warning are checked; assert on the generated C# (emitCode: true) so nothing has to run.
			var dir = Path.Combine(Path.GetTempPath(), "ks_warnfile_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				// Line 3 of the include is the unset read; line 4 is unreachable. The main script's own lines 3/4 are
				// deliberately different text, so quoting the wrong file is unmistakable.
				var incPath = Path.Combine(dir, "warn-inc.ks");
				File.WriteAllText(incPath, "IncWarnHelper() {\n\treturn 1\n\tzzUnsetInInclude := zzUnsetInInclude + 1\n\tzzNeverRuns := 2\n}\n");
				var mainPath = Path.Combine(dir, "warn-main.ks");
				File.WriteAllText(mainPath, $"#Warn All, MsgBox\n#include \"{incPath}\"\nMainWarnHelper() {{\n\treturn zzUnsetInMain\n}}\n");

				var (arr, code) = new CompilerHelper().CompileCodeToByteArray(mainPath, "warn-main", null, false, true);
				Assert.IsNotNull(arr, code);

				// The included file's warnings quote ITS text at ITS line numbers, and name it.
				Assert.IsTrue(code.Contains("In warn-inc.ks:"), "an included file's warning should name the file; generated:\n" + code);
				Assert.IsTrue(code.Contains("3: zzUnsetInInclude := zzUnsetInInclude + 1"),
					"the VarUnset excerpt should quote the include's own line 3; generated:\n" + code);
				Assert.IsTrue(code.Contains("4: zzNeverRuns := 2"),
					"the Unreachable excerpt should quote the include's own line 4; generated:\n" + code);
				// The regression: the main script's line 3 must never be quoted for a warning raised in the include.
				Assert.IsFalse(code.Contains("3: MainWarnHelper"),
					"an included file's warning must not quote the main script at the same line number; generated:\n" + code);

				// A main-script warning is unchanged: its own text, no file header.
				Assert.IsTrue(code.Contains("4: return zzUnsetInMain"),
					"a main-script warning should still quote the main script; generated:\n" + code);
				Assert.IsFalse(code.Contains("In warn-main.ks:"),
					"a main-script warning should not be prefixed with a file name; generated:\n" + code);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}
	}
}
