using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class BuiltInVarsTests : TestRunner
	{
		[Test, Category("BuiltInVars")]
		public void PropsDateTime() => Assert.IsTrue(TestScript("props-date-time", true));

		[Test, Category("BuiltInVars"), NonParallelizable]
		public void PropsScriptProperties() => Assert.IsTrue(TestScript("props-script-properties", false));

		[Test, Category("BuiltInVars"), NonParallelizable]
		public void PropsLineFile()
		{
			Assert.IsTrue(TestScript("props-linefile", false));
		}

		[Test, Category("BuiltInVars"), NonParallelizable]
		public void PropsScriptName()
		{
			var scriptPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "script-name-path.ahk");
			File.WriteAllText(scriptPath, @"FileAppend(A_ScriptName . ""`n"" . A_ScriptFullPath, ""*"")");

			var output = RunScript(scriptPath, "not-the-script-name", true, false);
			var lines = output.Split(["\r\n", "\n"], StringSplitOptions.None);

			Assert.AreEqual(Path.GetFileName(scriptPath), lines[0]);
			Assert.AreEqual(Path.GetFullPath(scriptPath), lines[1]);

			output = RunScript(@"FileAppend(A_ScriptName . ""`n"" . A_ScriptFullPath, ""*"")", "CustomScript", true, false);
			lines = output.Split(["\r\n", "\n"], StringSplitOptions.None);

			Assert.AreEqual("CustomScript", lines[0]);
			Assert.AreEqual("*", lines[1]);
		}

		[Test, Category("BuiltInVars"), NonParallelizable]
		public void PropsScriptSettings()
		{
			Assert.IsTrue(TestScript("props-script-settings", false));
		}

		[Test, Category("BuiltInVars"), NonParallelizable]
		public void PropsTrayMenuWithoutIcon()
		{
			SkipIfUiInitializationBlocked("Building a tray menu needs a usable UI toolkit.");
			Assert.IsTrue(TestScript("props-tray-menu", false));
		}

		[Test, Category("BuiltInVars")]
		public void PropsSpecialChars() => Assert.IsTrue(TestScript("props-special-chars", true));
	}
}
