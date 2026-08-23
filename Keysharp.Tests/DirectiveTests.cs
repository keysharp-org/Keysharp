using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class DirectiveTests : TestRunner
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
		public void ConditionalErrors()
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
		public void WarningNonFatal()
		{
			// #Warning is #Error's non-fatal sibling: reported at compile time, but the build still produces a unit.
			static (object unit, string[] diags, string[] warns) Lower(string src)
			{
				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(src);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
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

			// Conditional compilation applies: a warning in a dead branch never fires. The symbol has to be one no
			// host ever defines — a platform symbol is live when the suite runs on that platform, so the branch is
			// only dead on the other ones. KEYSHARP is the control: always defined, so that branch must still warn.
			Assert.IsEmpty(Lower("#if KEYSHARP_NO_SUCH_SYMBOL\n#Warning dead\n#endif\nx := 1\n").warns,
				"a #Warning inside an excluded branch must not be reported");
			Assert.AreEqual("2:1: live", Lower("#if KEYSHARP\n#Warning live\n#endif\nx := 1\n").warns[0]);

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
		public void HotIfCriterionIsAnExpression()
		{
			// A `#HotIf` criterion is an expression. At statement level a trailing primary chain is a zero-arg
			// call, which would invoke `Probe.Running` and `MyFlag` instead of reading them, while a criterion
			// that really is a call must still be called. Asserted on the generated C# (emitCode: true) because
			// firing a hotkey needs the hook and focus.
			static string Compile(string src)
			{
				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(src, "hotiftest", null, false, true, false);
				Assert.IsNotNull(arr, code);
				return code;
			}

			var code = Compile("class Probe {\n\tstatic Running := false\n}\n"
							   + "global MyFlag := false\n"
							   + "#HotIf Probe.Running\n*F13::return\n#HotIf\n"
							   + "#HotIf MyFlag\n*F14::return\n#HotIf\n"
							   + "#HotIf IsOn()\n*F15::return\n#HotIf\n"
							   + "IsOn() => 1\n");

			// A class static is read, not invoked.
			Assert.IsTrue(code.Contains("GetPropertyValue(probe, \"Running\")"),
						  "a `Class.Prop` criterion must read the property; generated:\n" + code);
			Assert.IsFalse(code.Contains("Invoke(probe, \"Running\")"),
						   "a `Class.Prop` criterion must not be invoked as a method; generated:\n" + code);
			// A plain variable is its own value, not something to call.
			Assert.IsFalse(code.Contains("Invoke(myflag, \"Call\")"),
						   "a bare-variable criterion must not be called; generated:\n" + code);
			// A criterion that really is a call still is one.
			Assert.IsTrue(code.Contains("Invoke(ison, \"Call\")"),
						  "a `Func()` criterion must still be called; generated:\n" + code);
		}

		[Test, Category("Directives")]
		public void CompilationDefines()
		{
			// Symbols are an argument to a compilation, not process state, so two compiles in the same process can
			// disagree — which is what lets Ks.RunScript compile a nested script with its own set. Asserted on the
			// generated C# (emitCode: true) so nothing has to run.
			static string Compile(string src, params string[] defines)
			{
				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(src, "definetest", null, false, true, false, defines);
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
		public void RunScriptOptions()
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
		public void ImportedDefines()
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
					var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(main, "defmain", null, false, true, false, defines);
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

			var (arr, code, _) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\n#Requires capability ScreenCapture, InputMonitoring\nx := 1\n",
				"reqcap-emit", null, false, true);
			Assert.IsNotNull(arr, code);
			Assert.IsTrue(code.Contains("RequireCapabilities(\"ScreenCapture, InputMonitoring\")"),
				"the capability directive should emit a RequireCapabilities call; generated:\n" + code);

			// The plural alias also works.
			var (arrPl, codePl, _) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\n#Requires capabilities InputMonitoring\nx := 1\n",
				"reqcap-plural", null, false, true);
			Assert.IsNotNull(arrPl, codePl);
			Assert.IsTrue(codePl.Contains("RequireCapabilities(\"InputMonitoring\")"),
				"the plural `#Requires capabilities` alias should emit a RequireCapabilities call");

			// Control: a version-only #Requires must NOT emit a capability request.
			var (arrNone, codeNone, _) = ch.CompileCodeToByteArray(
				"#Requires AutoHotkey v2.0\nx := 1\n", "reqcap-none", null, false, true);
			Assert.IsNotNull(arrNone, codeNone);
			Assert.IsFalse(codeNone.Contains("RequireCapabilities"),
				"a version-only #Requires must not emit RequireCapabilities");
		}

		[Test, Category("Directives")]
		public void WarnFile()
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

				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(mainPath, "warn-main", null, false, true);
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

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpBlock() => Assert.IsTrue(TestScript("directive-csharp", true));

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpSymbolOrder() => Assert.IsTrue(TestScript("directive-csharp-symbols", false));

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpExceptions() => Assert.IsTrue(TestScript("directive-csharp-errors", true));

		[Test, Category("Directives")]
		public void CSharpClassScope() => Assert.IsTrue(TestScript("directive-csharp-class", false));

		[Test, Category("Directives")]
		public void CSharpClrBoundary() => Assert.IsTrue(TestScript("directive-csharp-clr", false));

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpPreprocessor()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csif_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				string Inline(string body, string name, IEnumerable<string> defines = null)
				{
					var p = Path.Combine(dir, name + ".ks");
					File.WriteAllText(p, "#NoTrayIcon\n#CSharp\n" + body + "\n#EndCSharp\nx := Pick()\n");
					var ch = new CompilerHelper();
					var (arr, code, compilation) = ch.CompileCodeToByteArray(p, name, defines: defines);
					Assert.IsNotNull(arr, "compile failed:\n" + code);
					return compilation.InlineCode;
				}

				var plat = Inline("#if WINDOWS\npublic static long Pick() => 1;\n#else\npublic static long Pick() => 2;\n#endif", "plat");
#if WINDOWS
				Assert.IsTrue(plat.Contains("=> 1"), plat);
				Assert.IsFalse(plat.Contains("=> 2"), plat);
#else
				Assert.IsTrue(plat.Contains("=> 2"), plat);
				Assert.IsFalse(plat.Contains("=> 1"), plat);
#endif
				const string branch = "public static long Pick() => 1;\n#else\npublic static long Pick() => 2;\n#endif";
				var ks = Inline("#if KEYSHARP\n" + branch, "ks");
				Assert.IsTrue(ks.Contains("=> 1"), ks);
				var undef = Inline("#if NOT_DEFINED_ANYWHERE\n" + branch, "undef");
				Assert.IsTrue(undef.Contains("=> 2"), undef);
				var sup = Inline("#if MYSYM\n" + branch, "sup", ["MYSYM"]);
				Assert.IsTrue(sup.Contains("=> 1"), sup);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpGlobalUsings()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csusing_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "usings.ks");
				File.WriteAllText(script, """
					#NoTrayIcon
					#CSharp
					global using TextBuilder = System.Text.StringBuilder;
					#if WINDOWS
					using PlatformType = System.Windows.Forms.Form;
					#else
					using PlatformType = System.IO.FileInfo;
					#endif
					public static object PlatformName() => typeof(PlatformType).Name;
					#EndCSharp
					class C {
					#CSharp
					public object Build() => new TextBuilder().Append("ok").ToString();
					#EndCSharp
					}
					x := PlatformName()
					y := C().Build()
					""");
				var ch = new CompilerHelper();
				var (arr, code, compilation) = ch.CompileCodeToByteArray(script, "usings");
				Assert.IsNotNull(arr, "conditional and global using directives must compile:\n" + code);
				Assert.IsTrue(compilation.InlineCode.Contains("using TextBuilder"), compilation.InlineCode);
#if WINDOWS
				Assert.IsTrue(compilation.InlineCode.Contains("System.Windows.Forms.Form"), compilation.InlineCode);
				Assert.IsFalse(compilation.InlineCode.Contains("System.IO.FileInfo"), compilation.InlineCode);
#else
				Assert.IsTrue(compilation.InlineCode.Contains("System.IO.FileInfo"), compilation.InlineCode);
				Assert.IsFalse(compilation.InlineCode.Contains("System.Windows.Forms.Form"), compilation.InlineCode);
#endif
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpScope()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csplace_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				(byte[] Arr, string Code, string Inline) Compile(string src, string name)
				{
					var p = Path.Combine(dir, name + ".ks");
					File.WriteAllText(p, src);
					var ch = new CompilerHelper();
					var (arr, code, compilation) = ch.CompileCodeToByteArray(p, name);
					return (arr, code, compilation.InlineCode);
				}

				// A block after an in-file `#Module` belongs to THAT module's class, not to __Main.
				var mod = Compile("#NoTrayIcon\nx := 1\n#Module Helper\n#CSharp\npublic static long Only() => 7;\n#EndCSharp\n", "mod");
				Assert.IsNotNull(mod.Arr, "a #CSharp block inside a #Module must compile:\n" + mod.Code);
				var helperClass = mod.Inline.IndexOf("class Helper", StringComparison.Ordinal);
				var onlyDecl = mod.Inline.IndexOf("Only()", StringComparison.Ordinal);
				Assert.Greater(helperClass, -1, "the module's partial class must be emitted:\n" + mod.Inline);
				Assert.Greater(onlyDecl, helperClass, "the member must land inside the module that declared it:\n" + mod.Inline);

				// A block in an `export class` reaches that class rather than disappearing.
				var exp = Compile("#NoTrayIcon\nexport class E {\n#CSharp\npublic static object Who(object @this) => \"e\";\n#EndCSharp\n}\n", "exp");
				Assert.IsNotNull(exp.Arr, "a #CSharp block in an export class must compile:\n" + exp.Code);
				Assert.IsTrue(exp.Inline.Contains("Who"), "its members must not silently vanish:\n" + exp.Inline);

				// Below the top level of a script/module/class there is no type for members to belong to.
				var fn = Compile("#NoTrayIcon\nf() {\n#CSharp\npublic static long N() => 1;\n#EndCSharp\n}\nf()\n", "fn");
				Assert.IsNull(fn.Arr, "a #CSharp block inside a function must be rejected; generated:\n" + fn.Code);
				Assert.IsTrue(fn.Code.Contains("must appear at the top level"), "the diagnostic should say where it may go; got:\n" + fn.Code);

				// An unterminated block is reported, rather than swallowing the rest of the file.
				var unterm = Compile("#NoTrayIcon\n#CSharp\npublic static long N() => 1;\nx := 1\n", "unterm");
				Assert.IsNull(unterm.Arr, "a #CSharp block with no #EndCSharp must be rejected; generated:\n" + unterm.Code);
				Assert.IsTrue(unterm.Code.Contains("EndCSharp"), "the diagnostic should name the missing terminator; got:\n" + unterm.Code);

				// A hoisted `using` is anchored like a member, so a typo in one points at the author's own line (4).
				var badUsing = Compile("#NoTrayIcon\n#CSharp\nusing Systm.Text;\npublic static long N() => 1;\n#EndCSharp\nx := N()\n", "badusing");
				Assert.IsNull(badUsing.Arr, "an unresolvable using must fail the compile; generated:\n" + badUsing.Code);
				Assert.IsTrue(badUsing.Code.Contains("badusing.ks"),
							  "the error must point at the script the using was written in, not the generated file; got:\n" + badUsing.Code);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpModuleErrors()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csdiag_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				string Compile(string src, string name)
				{
					var p = Path.Combine(dir, name + ".ks");
					File.WriteAllText(p, src);
					var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(p, name);
					Assert.IsNull(arr, $"{name} must be rejected; it compiled");
					return code;
				}

				void Rejects(string src, string name, string expect, int line = 0)
				{
					var code = Compile(src, name);
					Assert.IsTrue(code.Contains(expect), $"{name}: expected '{expect}'; got:\n{code}");

					if (line > 0)
						Assert.IsTrue(code.Contains($"{name}.ks {line}:"),
									  $"{name}: expected the diagnostic anchored at line {line}; got:\n{code}");
				}

				Rejects("#NoTrayIcon\nglobal Hits := 0\n#CSharp\npublic static long hits;\n#EndCSharp\nx := 1\n",
						"globalcol", "collides with a variable or function the script declares", line: 4);
				Rejects("#NoTrayIcon\n#CSharp\nstatic long AutoExecSection;\n#EndCSharp\nx := 1\n",
						"reserved", "is a name Keysharp generates");
				Rejects("#NoTrayIcon\nf() => 1\n#CSharp\npublic static long F() => 2;\n#EndCSharp\nx := 1\n",
						"funccol", "declared both as a script function");
				Rejects("#NoTrayIcon\n#CSharp\npublic static long R(ref long a) => a;\n#EndCSharp\nx := 1\n",
						"refparam", "cannot be passed from a script", line: 3);
				Rejects("#NoTrayIcon\n#CSharp\npublic static unsafe long* P() => null;\n#EndCSharp\nx := 1\n",
						"ptrret", "return type that cannot be handed");
				Rejects("#NoTrayIcon\n#CSharp\npublic static decimal D(decimal value) => value;\n#EndCSharp\nx := 1\n",
						"decimalparam", "cannot be passed from a script");
				Rejects("#NoTrayIcon\n#CSharp\npublic static char Ch() => 'x';\n#EndCSharp\nx := 1\n",
						"charret", "return type that cannot be handed");
				Rejects("#NoTrayIcon\n#CSharp\npublic static long Sum(params long[] values) => 0;\n#EndCSharp\nx := 1\n",
						"typedparams", "params object[]");
				Rejects("#NoTrayIcon\n#CSharp\nstatic long value;\npublic static ref long RefRet() => ref value;\n#EndCSharp\nx := 1\n",
						"refreturn", "return type that cannot be handed", line: 4);
				Rejects("#NoTrayIcon\n#CSharp\npublic static T Id<T>(T x) => x;\n#EndCSharp\nx := 1\n",
						"generic", "is generic", line: 3);
				Rejects("#NoTrayIcon\n#CSharp\npublic long M() => 1;\n#EndCSharp\nx := 1\n",
						"nonstatic", "must be 'static' to be callable", line: 3);
				Rejects("#NoTrayIcon\nx := 1\n#Module M\n#CSharp\npublic static long M() => 1;\n#EndCSharp\n",
						"modname", "its module's exact name", line: 5);
				Rejects("#NoTrayIcon\n#CSharp\npublic static long tally() => 1;\n#EndCSharp\nx := 1\n",
						"lowerexport", "cannot be all-lowercase", line: 3);
				// The spelled-out forms get the keyword verdicts — the boundary check is by WRITTEN name, so
				// `System.Char` must not slip past what `char` is rejected for.
				Rejects("#NoTrayIcon\n#CSharp\npublic static System.Char Ch() => 'x';\n#EndCSharp\nx := 1\n",
						"charspelled", "return type", line: 3);
				Rejects("#NoTrayIcon\n#CSharp\npublic static long N(System.Nullable<long> v) => 0;\n#EndCSharp\nx := 1\n",
						"nullspelled", "cannot be passed from a script", line: 3);
				// [Export] misuse is diagnosed on EVERY lowering path: a library validated standalone
				// (single-module, no importer in sight) must get the same verdict its importers will.
				Rejects("#NoTrayIcon\n#CSharp\n[Export]\nprivate static long Hidden() => 1;\n#EndCSharp\nx := 1\n",
						"exportprivate", "[Export] is supported only on public static module methods", line: 3);
				Rejects("#NoTrayIcon\n#CSharp\n[Export]\npublic static long Slot = 3;\n#EndCSharp\nx := 1\n",
						"exportfield", "[Export] is supported only on public static module methods", line: 3);
				Rejects("#NoTrayIcon\n#CSharp unsafe\npublic static long N() => 1;\n#EndCSharp\nx := 1\n",
						"options", "takes no options");
				Rejects("#NoTrayIcon\nclass C\n{\n#CSharp\n[Export]\npublic static long M() => 1;\n#EndCSharp\n}\n",
						"classexport", "[Export] is valid only at module scope");
				Rejects("#NoTrayIcon\n#CSharp \"a.cs\" \"b.cs\"\nx := 1\n",
						"paths", "accepts exactly one quoted .cs file path");
				Rejects("#NoTrayIcon\n#EndCSharp\nx := 1\n", "stray", "#EndCSharp without a matching #CSharp");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void TranspileInlineCSharp()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks_cstrans_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "t.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#CSharp\nusing System.Text;\npublic static object Marker() => new StringBuilder(\"m\").ToString();\n#EndCSharp\nx := Marker()\n");
				using var proc = Process.Start(new ProcessStartInfo(launcher, $"--errorstdout --transpile \"{script}\"")
				{
					RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
				});
				proc.WaitForExit(120000);
				Assert.AreEqual(0, proc.ExitCode, proc.StandardError.ReadToEnd());
				Assert.IsTrue(File.Exists(Path.Combine(dir, "t.cs")), "the lowered tree's .cs must be written");
				var inline = Path.Combine(dir, "t.inline.cs");
				Assert.IsTrue(File.Exists(inline), "the inline C# must be written to its own .inline.cs");
				var text = File.ReadAllText(inline);
				Assert.IsTrue(text.Contains("Marker"), "the user's member must be in the inline file:\n" + text);
				// Members are indented to their scope's depth (module members sit three levels deep), so the
				// generated file reads like code rather than a paste at column zero.
				Assert.IsTrue(text.Contains("\t\t\tpublic static object Marker"),
							  "the member must be indented to its scope depth:\n" + text);
				Assert.IsFalse(File.ReadAllText(Path.Combine(dir, "t.cs")).Contains("Marker()"),
							   "the user's C# must not leak into the lowered tree's file");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void CompiledCSharp()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks_csexe_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "csart.ks");
				File.WriteAllText(script,
								  "#NoTrayIcon\n#ErrorStdOut\n"
								  + "#CSharp\npublic static object Marker() => \"mod\";\n#EndCSharp\n"
								  + "class C {\n#CSharp\n"
								  + "[Keysharp.Runtime.Static] public static object Tag(object @this) => \"cls\";\n"
								  + "[Keysharp.Runtime.Static] public static object Boom(object @this) { throw new System.IndexOutOfRangeException(\"x\"); }\n"
								  + "#EndCSharp\n}\n"
								  + "caught := \"\"\ntry\n\tC.Boom()\ncatch\n\tcaught := \"caught\"\n"
								  + "FileAppend(Marker() \"-\" C.Tag() \"-\" caught, \"*\")\nExitApp()\n");
				var (compileExit, _, compileErr) = Run(launcher, $"--errorstdout --compile exe \"{script}\"");
				Assert.AreEqual(0, compileExit, "compile failed: " + compileErr);
				var exe = Path.ChangeExtension(script, ".exe");
				Assert.IsTrue(File.Exists(exe), $"compile produced no exe at {exe}");
				var (runExit, stdout, stderr) = Run(exe, "");
				Assert.AreEqual("mod-cls-caught", stdout.Trim(),
								$"exit {runExit}, stderr: {stderr}");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void IncludedCSharp()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csinc_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var inc = Path.Combine(dir, "inc.ahk");
				var main = Path.Combine(dir, "main.ks");
				File.WriteAllText(main, "#NoTrayIcon\n#Include \"inc.ahk\"\nx := IncFn()\n");

				File.WriteAllText(inc, "#CSharp\npublic static object IncFn() => \"inc\";\n#EndCSharp\n");
				var (arr, code, compilation) = new CompilerHelper().CompileCodeToByteArray(main, "incmain");
				Assert.IsNotNull(arr, "a block in an included file must compile:\n" + code);
				Assert.IsTrue(compilation.InlineCode.Contains("IncFn"), "the included block's member must be emitted:\n" + compilation.InlineCode);
				Assert.IsFalse(compilation.InlineCode.Contains("#line"),
							   "the inline unit is pretty-printed with no source mapping:\n" + compilation.InlineCode);

				File.WriteAllText(inc, "#CSharp\nusing Systm.Text;\npublic static object IncFn() => \"inc\";\n#EndCSharp\n");
				var (arr2, code2, _) = new CompilerHelper().CompileCodeToByteArray(main, "incmain");
				Assert.IsNull(arr2, "a broken using in an included block must fail the compile; generated:\n" + code2);
				Assert.IsTrue(code2.Contains("Systm"),
							  "the diagnostic must name the unknown namespace; got:\n" + code2);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void ValidateCSharp()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks_csval_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var bad = Path.Combine(dir, "bad.ks");
				File.WriteAllText(bad, "#NoTrayIcon\n#CSharp\npublic static object F() => new NoSuchType();\n#EndCSharp\nF()\n");
				var (badExit, badOut, badErr) = Run(launcher, $"--errorstdout --validate \"{bad}\"");
				Assert.AreNotEqual(0, badExit, "an unknown type in a #CSharp block must fail --validate");
				Assert.IsTrue((badOut + badErr).Contains("NoSuchType"),
							  $"the failure should name the offending code; stdout:\n{badOut}\nstderr:\n{badErr}");

				var good = Path.Combine(dir, "good.ks");
				File.WriteAllText(good, "#NoTrayIcon\n#CSharp\npublic static object F() => 42L;\n#EndCSharp\nF()\n");
				var (goodExit, goodOut, goodErr) = Run(launcher, $"--errorstdout --validate \"{good}\"");
				Assert.AreEqual(0, goodExit, $"a valid script must pass --validate; stdout:\n{goodOut}\nstderr:\n{goodErr}");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>Runs a process to completion with captured output, killing it rather than orphaning it on timeout.</summary>
		private static (int ExitCode, string StdOut, string StdErr) Run(string exe, string args)
		{
			using var proc = Process.Start(new ProcessStartInfo(exe, args)
			{
				RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
			});
			var so = proc.StandardOutput.ReadToEndAsync();
			var se = proc.StandardError.ReadToEndAsync();

			if (!proc.WaitForExit(240000))
			{
				try { proc.Kill(true); } catch { }

				Assert.Fail($"'{exe} {args}' did not exit within 240s");
			}

			return (proc.ExitCode, so.Result, se.Result);
		}

		[Test, Category("Directives")]
		public void CSharpErrors()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csharpsyn_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "broken.ks");
				File.WriteAllText(script,
								  "#NoTrayIcon\n#CSharp\nusing System.Text\npublic static object Test() { return \"\"; }\n#EndCSharp\n\nTest()\n");
				var ch = new CompilerHelper();
				var (arr, code, _) = ch.CompileCodeToByteArray(script, "broken");
				Assert.IsNull(arr, "a #CSharp block that does not parse must not produce an assembly; generated:\n" + code);
				Assert.IsTrue(code.Contains("#CSharp:"), "the failure should be reported as a #CSharp diagnostic; got:\n" + code);
				Assert.IsTrue(code.Contains("broken.ks 3:"),
							  "the diagnostic should point at line 3, the `using` with no semicolon; got:\n" + code);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void CSharpMemberReceiver()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_csharprecv_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var ch = new CompilerHelper();

				(byte[] Arr, string Code) Compile(string member, string name)
				{
					var script = Path.Combine(dir, name + ".ks");
					File.WriteAllText(script, "#NoTrayIcon\nclass C {\n#CSharp\n" + member + "\n#EndCSharp\n}\n");
					var (a, c, _) = ch.CompileCodeToByteArray(script, name);
					return (a, c);
				}

				foreach (var (member, why) in new[]
			{
				("[Keysharp.Runtime.Static] public static long Solo(long a) => a;", "[Static], no receiver"),
					("public static long staticSolo(long a) => a;", "static name prefix, no receiver"),
					("public static long Inst(long a) => a;", "instance member, no receiver"),
					("[Keysharp.Runtime.Static] public static long Var(params object[] args) => args.Length;", "variadic, receiver would be absorbed"),
					("public static long Mistyped(long a, long b) => a;", "leading parameter cannot hold a receiver"),
				})
				{
					var (arr, code) = Compile(member, "recv");
					Assert.IsNull(arr, $"a class member that cannot receive its receiver must not compile ({why}); generated:\n{code}");
					Assert.IsTrue(code.Contains("must take the receiver as its first parameter"),
								  $"the diagnostic should name the fix ({why}); got:\n{code}");
				}

				// A property has no parameter list, so a static one can never receive the class.
				var (parr, pcode) = Compile("[Keysharp.Runtime.Static] public static long Answer => 42;", "recvprop");
				Assert.IsNull(parr, "a static property in a class block must not compile; generated:\n" + pcode);
				Assert.IsTrue(pcode.Contains("cannot be static"), "the diagnostic should name the fix; got:\n" + pcode);

				// Class members obey the SAME boundary-signature rule as module functions. The regression: these
				// checks ran only at module scope, so every one of these compiled and then failed at the CALL — a
				// raw CLR error from delegate compilation, outside the [InlineCSharp] boundary.
				foreach (var (member, expect, why) in new[]
			{
				("public static long ByRef(object @this, ref long x) => x;", "cannot be passed from a script", "ref parameter"),
					("public object SpanArg(System.Span<long> s) => s.Length;", "cannot be passed from a script", "Span parameter on an instance member"),
					("public static unsafe long* Ptr(object @this) => null;", "return type", "pointer return"),
					("public static T Gen<T>(object @this, T t) => t;", "is generic", "generic member"),
					("public System.Span<byte> View => default;", "property", "Span-typed property"),
				})
				{
					var (arr, code) = Compile(member, "clssig");
					Assert.IsNull(arr, $"a class member the dispatcher cannot marshal must not compile ({why}); generated:\n{code}");
					Assert.IsTrue(code.Contains(expect), $"the diagnostic should name the problem ({why}); got:\n{code}");
				}

				// The shapes that CAN receive one all compile: a declared receiver, a receiver typed more precisely
				// than `object`, and a C# instance member -- which needs none, C# having bound it already.
				foreach (var ok in new[]
			{
				"[Keysharp.Runtime.Static] public static long Fine(object @this, long a) => a;",
					"public static long Named(object self, long a) => a;",
					"public static object Typed(Keysharp.Builtins.KeysharpObject @this, long a) => a;",
					"public object InstMethod(long a) => a;",
					"public long InstProp => 42;",
					"static long Hidden(long a) => a;",
				})
				{
					var (arr, code) = Compile(ok, "recvok");
					Assert.IsNotNull(arr, $"this member can receive its receiver and must compile: {ok}\n{code}");
				}
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void CSharpModuleExports() => Assert.IsTrue(TestScript("directive-csharp-modules", false));

		[Test, Category("Directives")]
		public void CSharpModuleUsingScopes() => Assert.IsTrue(TestScript("directive-csharp-module-scopes", false));

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpFileScopes()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks_csfilescope_" + Guid.NewGuid().ToString("N"));
			var mods = Path.Combine(root, "mods");
			_ = Directory.CreateDirectory(mods);
			var savedPath = Environment.GetEnvironmentVariable("AhkImportPath");

			try
			{
				File.WriteAllText(Path.Combine(root, "vec.cs"),
								  "[Keysharp.Runtime.Static] public static object Tag(object @this) => \"cls-file\";");
				var s1 = Path.Combine(root, "s1.ks");
				File.WriteAllText(s1, "#NoTrayIcon\nclass C {\n#CSharp \"vec.cs\"\n}\nx := C.Tag()\n");
				var (arr1, code1, compilation1) = new CompilerHelper().CompileCodeToByteArray(s1, "s1");
				Assert.IsNotNull(arr1, "a class-body file form must compile:\n" + code1);
				Assert.IsTrue(compilation1.InlineCode.Contains("cls-file"), "the file's member must be emitted:\n" + compilation1.InlineCode);

				File.WriteAllText(Path.Combine(mods, "Fast.ahk"),
					"#define MODULE_CSHARP\n#CSharp\n#if MODULE_CSHARP\npublic static object BlockSymbol() => \"module-symbol\";\n#endif\n#EndCSharp\n#CSharp \"impl.cs\"\n");
				File.WriteAllText(Path.Combine(mods, "impl.cs"), "public static object Crunch() => \"file-mod\";");
				var main = Path.Combine(root, "main.ks");
				File.WriteAllText(main, "#NoTrayIcon\n#import \"Fast\" { Crunch, BlockSymbol }\nx := Crunch()\ny := BlockSymbol()\n");
				Environment.SetEnvironmentVariable("AhkImportPath", mods);
				var (arr2, code2, compilation2) = new CompilerHelper().CompileCodeToByteArray(main, "main");
				Assert.IsNotNull(arr2, "a module file's file form must resolve beside the module file:\n" + code2);
				Assert.IsTrue(compilation2.InlineCode.Contains("file-mod"), "the module file's member must be emitted:\n" + compilation2.InlineCode);
				Assert.IsTrue(compilation2.InlineCode.Contains("module-symbol"), "module-local symbols must reach inline C#:\n" + compilation2.InlineCode);
			}
			finally
			{
				Environment.SetEnvironmentVariable("AhkImportPath", savedPath);

				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, Category("Directives"), NonParallelizable]
		public void CSharpSearchOrder()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks_csfile_" + Guid.NewGuid().ToString("N"));
			var local = Path.Combine(root, "local");
			var shared = Path.Combine(root, "shared");
			_ = Directory.CreateDirectory(local);
			_ = Directory.CreateDirectory(shared);
			var savedPath = Environment.GetEnvironmentVariable("AhkImportPath");
			var savedCwd = Directory.GetCurrentDirectory();

			try
			{
				var script = Path.Combine(local, "s.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#CSharp \"helper.cs\"\nHelp()\n");
				Environment.SetEnvironmentVariable("AhkImportPath", shared);

				File.WriteAllText(Path.Combine(shared, "helper.cs"), "public static object Help() => \"shared\";");
				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(script, "s");
				Assert.IsNotNull(arr, "a file on the module search path must be found:\n" + code);

				File.WriteAllText(Path.Combine(local, "helper.cs"), "public static object Help() => \"local\";");
				var (arr2, code2, compilation2) = new CompilerHelper().CompileCodeToByteArray(script, "s");
				Assert.IsNotNull(arr2, code2);
				Assert.IsTrue(compilation2.InlineCode.Contains("\"local\""), "the directive's own directory must win; got:\n" + compilation2.InlineCode);

				File.Delete(Path.Combine(local, "helper.cs"));
				File.Delete(Path.Combine(shared, "helper.cs"));
				var cwd = Path.Combine(root, "cwd");
				_ = Directory.CreateDirectory(cwd);
				File.WriteAllText(Path.Combine(cwd, "helper.cs"), "public static object Help() => \"cwd\";");
				Directory.SetCurrentDirectory(cwd);
				Environment.SetEnvironmentVariable("AhkImportPath", "");
				var (arr3, code3, _) = new CompilerHelper().CompileCodeToByteArray(script, "s");
				Assert.IsNull(arr3, "the working directory must not be searched; generated:\n" + code3);
				Assert.IsTrue(code3.Contains("cannot find") && code3.Contains("Looked in:"),
							  "a miss should say where it looked; got:\n" + code3);
			}
			finally
			{
				Directory.SetCurrentDirectory(savedCwd);
				Environment.SetEnvironmentVariable("AhkImportPath", savedPath);

				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void ParseScriptCSharp()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_parsecs_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				string Parse(string body, string name)
				{
					var p = Path.Combine(dir, name + ".ks");
					File.WriteAllText(p, body);
					return Ks.ParseScript(p) as string ?? "";
				}

				var bad = Parse("#NoTrayIcon\n#CSharp\npublic static object F() => new NoSuchType();\n#EndCSharp\nF()\n", "bad");
				Assert.IsNotEmpty(bad, "a #CSharp block naming an unknown type must not be reported as valid");
				Assert.IsTrue(bad.Contains("NoSuchType"), "the error should name the offending code; got:\n" + bad);

				var ambig = Parse("#NoTrayIcon\n#CSharp\nusing Keysharp.Builtins;\npublic static object F() => Array.Empty<long>().Length;\n#EndCSharp\nF()\n", "ambig");
				Assert.IsNotEmpty(ambig, "an ambiguous reference in a #CSharp block must not be reported as valid");

				Assert.IsEmpty(Parse("#NoTrayIcon\n#CSharp\npublic static long F() => 42;\n#EndCSharp\nF()\n", "good") ?? "");
				Assert.IsEmpty(Parse("#NoTrayIcon\nx := 1\n", "plain") ?? "");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}
	}
}
