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
		public void CombinedParamsInFunc() => Assert.IsTrue(TestScript("func-combined-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void DefParamsInFunc() => Assert.IsTrue(TestScript("func-def-params", false));

		[Test, Category("Function"), NonParallelizable]
		public void DynVarsInFunc() => Assert.IsTrue(TestScript("func-dyn-vars", false));

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

		// RequiresHook: registers a real global hotkey, which needs the keysharp-inputd hook and, on a desktop
		// with the daemon, prompts for input-access permission. Excluded from the non-interactive curated CI set.
		[Test, Category("Function"), Category("RequiresHook"), NonParallelizable]
		public void HotkeyLocalFunc() => Assert.IsTrue(TestScript("func-hotkey-local", false));

		public object TestFunc(object a) => a;
		[Test, Category("Function"), NonParallelizable]
		public void FuncComparison()
		{
			var f1 = Keysharp.Builtins.Functions.Func(TestFunc);
			var f2 = Keysharp.Builtins.Functions.Func(TestFunc, f1);
			Assert.AreEqual(f1, f1);
			Assert.AreNotEqual(f1, f2);

			var bf1 = f1.Bind("1");
			var bf2 = f2.Bind("1");
			Assert.AreNotEqual(f1, bf2);
			Assert.AreNotEqual(bf1, bf2);
		}
	}
}
