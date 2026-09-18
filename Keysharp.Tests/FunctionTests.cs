using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// Function tests don't need to also be wrapped in a function, so pass false to all.
	/// </summary>
	public class FunctionTests : TestRunner
	{
		[Test, Category("Function"), NonParallelizable]
		public void AllGlobalInFunc() => Assert.IsTrue(TestScript("func-all-global", false));

		[Test, Category("Function"), NonParallelizable]
		public void AllLocalInFunc() => Assert.IsTrue(TestScript("func-all-local", false));

		[Test, Category("Function"), NonParallelizable]
		public void BoundFunc() => Assert.IsTrue(TestScript("func-bound", false));

		[Test, Category("Function"), NonParallelizable]
		public void NamedArgs() => Assert.IsTrue(TestScript("func-named-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void FuncNames() => Assert.IsTrue(TestScript("func-names", false));

		[Test, Category("Function"), Category("Internal")]
		public void ThisFuncUsesLiteralFastPath()
		{
			const string source = "Probe() => A_ThisFunc\nclass C { static M() => A_ThisFunc\nP { get => A_ThisFunc }\nS => A_ThisFunc }\n"
								  + "F13::MsgBox A_ThisFunc\n";
			var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source);
			Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics));
			var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(program, "Test").ToFullString();
			Assert.IsFalse(generated.Contains("Keysharp.Builtins.Accessors.A_ThisFunc", StringComparison.Ordinal), generated);
			Assert.IsTrue(generated.Contains("\"Probe\"", StringComparison.Ordinal), generated);
			Assert.IsTrue(generated.Contains("\"C.M\"", StringComparison.Ordinal), generated);
			Assert.IsTrue(generated.Contains("\"C.Prototype.P.Get\"", StringComparison.Ordinal), generated);
			// A shorthand getter capitalises, and every hot callback is AutoHotkey's one `<Hotkey>` function.
			Assert.IsTrue(generated.Contains("\"C.Prototype.S.Get\"", StringComparison.Ordinal), generated);
			Assert.IsTrue(generated.Contains("\"<Hotkey>\"", StringComparison.Ordinal), generated);
		}

		// A nested function is a variable of the function declaring it, so any declaration of the same name conflicts with it.
		[Test, Category("Function")]
		public void NestedFunctionConflictsWithDeclaration()
		{
			foreach (var (source, line, existing) in new[]
			{
				("F() {\n\tstatic tick := 1\n\ttick() => 2\n}\n", 3, "static variable"),
				("F() {\n\tstatic tick := 1\n\tstatic tick() => 2\n}\n", 3, "static variable"),
				("F() {\n\tlocal tick := 1\n\ttick() => 2\n}\n", 3, "local variable"),
				("F() {\n\tglobal tick\n\ttick() => 2\n}\n", 3, "global variable"),
				("F(tick) {\n\tx := 1\n\ttick() => 2\n}\n", 3, "parameter"),
				("F() {\n\ttick() => 1\n\ttick() => 2\n}\n", 3, "Func"),
				("F() {\n\ttick() => 1\n\tf := tick() => 2\n}\n", 3, "Func"),
				("tick() => 1\nf := tick() => 2\n", 2, "Func"),
			})
			{
				var diagnostics = LoweringDiagnostics.Diagnostics(source);
				Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:") && d.EndsWith($"This function declaration conflicts with an existing {existing}: tick")),
					source.Replace("\n", "\\n") + ": " + string.Join("; ", diagnostics));
			}
		}

		[Test, Category("Function"), NonParallelizable]
		public void CombinedParamsInFunc() => Assert.IsTrue(TestScript("func-combined-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void DefParamsInFunc() => Assert.IsTrue(TestScript("func-def-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void DynVarsInFunc() => Assert.IsTrue(TestScript("func-dyn-vars", false));

		// Writing the module's own function or class is a load-time error; writing a local of the same name is not.
		[Test, Category("Function")]
		public void ConstantAssignment()
		{
			static string[] Diagnostics(string source) => LoweringDiagnostics.Diagnostics("OwnF() => 1\nclass OwnC {\n}\n" + source);

			const string output = "be used as an output variable", assigned = "be assigned a value", reference = "have its reference taken";

			foreach (var (source, line, message) in new[]
			{
				("ownf := 1\n", 4, $"This Func cannot {output}: OwnF"),
				("OwnC := 1\n", 4, $"This Class cannot {output}: OwnC"),
				("OwnF += 1\n", 4, $"This Func cannot {assigned}: OwnF"),
				("OwnF++\n", 4, $"This Func cannot {assigned}: OwnF"),
				("y := OwnF := 1\n", 4, $"This Func cannot {assigned}: OwnF"),
				("r := &OwnF\n", 4, $"This Func cannot {reference}: OwnF"),
				("for OwnF in [1]\n\tx := 1\n", 4, $"This Func cannot {output}: OwnF"),
				("F() {\n\tglobal\n\tOwnF := 1\n}\n", 6, $"This Func cannot {output}: OwnF"),
				("F() {\n\tglobal OwnF\n\tOwnF := 1\n}\n", 6, $"This Func cannot {output}: OwnF"),
				("F() {\n\tglobal OwnF := 1\n}\n", 5, $"This Func cannot {assigned}: OwnF"),
				("F() {\n\tglobal\n\tInner() {\n\t\tOwnC := 1\n\t}\n}\n", 7, $"This Class cannot {output}: OwnC"),
				("a_scriptdir := 1\n", 4, $"This built-in variable cannot {output}: a_scriptdir"),
				("F() {\n\tInner() => 1\n\tInner := 1\n}\n", 6, $"This Func cannot {output}: Inner"),
				("F() {\n\tinner() => 1\n\tr := &Inner\n}\n", 6, $"This Func cannot {reference}: inner"),
				("r := &A_ScriptDir\n", 4, $"This built-in variable cannot {reference}: A_ScriptDir"),
				("#Import Ks { Cosh }\ncosh := 1\n", 5, $"This Func cannot {output}: Cosh"),
				("#Import Ks as KsModule\nksmodule := 1\n", 5, $"This Module cannot {output}: KsModule"),
				("F() {\n\t#Import Ks { Cosh as Hyp }\n\thyp := 1\n}\n", 6, $"This Func cannot {output}: Hyp"),
				("#Import __Main\n__main := 1\n", 5, $"This Module cannot {output}: __main"),
				("F() {\n\tHelper() => 1\n\tG() {\n\t\tHelper := 5\n\t}\n}\n", 7, $"This Func cannot {output}: Helper"),
			})
			{
				var diagnostics = Diagnostics(source);
				Assert.IsTrue(System.Array.Exists(diagnostics, d => d.StartsWith($"{line}:") && d.EndsWith(message)),
					$"expected '{message}' at line {line} for {source.Replace("\n", "\\n")}, got: " + string.Join("; ", diagnostics));
			}

			foreach (var source in new[] { "F() {\n\tOwnF := 1\n\tOwnC++\n}\n", "F() {\n\tglobal OwnF\n}\n", "F(&OwnF) {\n\tOwnF := 1\n}\n", "A_Args := 1\nA_Args .= 2\nr := &A_Args\n", "OwnC ??= 1\nA_ScriptDir ??= 1\nF() {\n\tglobal OwnF\n\tOwnF ??= 1\n}\n", "F() {\n\tglobal\n\tlocal StrLen := 1\n\tlocal OwnF := 1\n}\n", "#Import Ks { * }\nKeysharpImage := 1\n" })
				Assert.IsEmpty(Diagnostics(source), source);
		}

		// A name which names no function adds no function object, so a probe such as IsSet(%name%) leaves nothing behind, and
		// a built-in function is one object however it is reached.
		[Test, Category("Function"), Category("Internal"), NonParallelizable]
		public void FunctionObjectsByMethod()
		{
			var data = s.FunctionData;
			var count = data.methodFunctions.Count;
			Assert.IsNull(Functions.GetKeysharpFuncByName("NoSuchFunctionAnywhere"));
			Assert.AreEqual(count, data.methodFunctions.Count);
			Assert.AreSame(Functions.MethodFunction(s.ReflectionsData.flatPublicStaticMethods["MsgBox"]), Functions.GetKeysharpFuncByName("MsgBox"));
		}

		[Test, Category("Function"), NonParallelizable]
		public void FatArrowFunc() => Assert.IsTrue(TestScript("func-fat-arrow", false));

		[Test, Category("Function"), NonParallelizable]
		public void GlobalLocalInFunc() => Assert.IsTrue(TestScript("func-global-local", false));

		[Test, Category("Function"), NonParallelizable]
		public void GlobalLocalStaticInFunc() => Assert.IsTrue(TestScript("func-global-local-static", false));

		[Test, Category("Function"), NonParallelizable]
		public void GlobalStaticInFunc() => Assert.IsTrue(TestScript("func-global-static", false));

		[Test, Category("Function"), NonParallelizable]
		public void LabelInFunc() => Assert.IsTrue(TestScript("func-label", true));

		[Test, Category("Function"), NonParallelizable]
		public void LocalStaticInFunc() => Assert.IsTrue(TestScript("func-local-static", false));

		[Test, Category("Function"), NonParallelizable]
		public void OptParamsInFunc() => Assert.IsTrue(TestScript("func-opt-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void ParamsInFunc() => Assert.IsTrue(TestScript("func-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void RefParamsInFunc() => Assert.IsTrue(TestScript("func-ref-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void ReturnFunc() => Assert.IsTrue(TestScript("func-return", false));

		[Test, Category("Function"), NonParallelizable]
		public void VarParamsInFunc() => Assert.IsTrue(TestScript("func-var-params", false));

        [Test, Category("Function"), NonParallelizable]
        public void FuncCallable() => Assert.IsTrue(TestScript("func-callable", false));

        [Test, Category("Function"), NonParallelizable]
        public void FuncClosure() => Assert.IsTrue(TestScript("func-closure", false));

		[Test, Category("Function"), NonParallelizable]
		public void FuncParamCount() => Assert.IsTrue(TestScript("func-param-count", false));

		// RequiresHook: registers a real global hotkey, which needs the keysharp-input hook and, on a desktop
		// with the daemon, prompts for input-access permission. Excluded from the non-interactive curated CI set.
		[Test, Category("Function"), Category("RequiresHook"), NonParallelizable]
		public void HotkeyLocalFunc() => Assert.IsTrue(TestScript("func-hotkey-local", false));

	}
}
