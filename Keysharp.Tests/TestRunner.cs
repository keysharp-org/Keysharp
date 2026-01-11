using Keysharp.Core.Common.Threading;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class TestRunner
	{
		protected string path = string.Format("..{0}..{0}..{0}Keysharp.Tests{0}Code{0}", Path.DirectorySeparatorChar);
		private const string ext = ".ahk";
		protected Script s;
		internal HotstringManager hsm;

		[SetUp]
		public void SetupBeforeEachTest()
		{
			s = new Script();
			hsm = s.HotstringManager;
		}

		protected bool HasPassed(string output)
		{
			if (string.IsNullOrEmpty(output))
				return false;

			const string pass = "pass";

			foreach (var remove in new[] { pass, " ", "\n" })
					output = output.Replace(remove, string.Empty);
			return output.Length == 0;
		}

		protected string RunScript(string source, string name, bool execute, bool wrapinfunction, bool exeout, int? exitCode = null) => RunScript(WrapInFunc(File.ReadAllText(source)), name, execute, exeout, exitCode);

		protected string RunScript(string source, string name, bool execute, bool exeout, int? exitCode = null)
		{
			SetupBeforeEachTest();
			s.SetName(name);
			_ = KeysharpEnhancements.OutputDebugLine(Environment.CurrentDirectory);
			var ch = new CompilerHelper();
			var (arr, code) = ch.CompileCodeToByteArray([source], name);

			if (arr == null)
			{
				_ = KeysharpEnhancements.OutputDebugLine(code);
				return string.Empty;
			}

			using (var sourceWriter = new StreamWriter("./" + name + ".cs"))
			{
				sourceWriter.WriteLine(code);
			}

			if (exeout)
			{
				File.WriteAllBytes("./" + name + ".exe", arr);
				File.WriteAllText("./" + name + ".runtimeconfig.json", CompilerHelper.GenerateRuntimeConfig());//Probably not needed for test exe outputs.
			}

			CompilerHelper.compiledasm = Assembly.Load(arr);
			var buffer = new StringBuilder();
			var output = string.Empty;

			if (execute)
			{
				using (var writer = new StringWriter(buffer))
				{
					try
					{
						Console.SetOut(writer);
						GC.Collect(); //Necessary to prevent testhost.exe throwing an error on long runs
						GC.WaitForPendingFinalizers();

						if (CompilerHelper.compiledasm == null)
							throw new Exception("Compilation failed.");

						//Environment.SetEnvironmentVariable("SCRIPT", script);
						var program = CompilerHelper.compiledasm.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}");
						var main = program.GetMethod("Main");
						var temp = new string[] { };
						var result = StaTask.RunSync(() => main.Invoke(null, [temp]));

						if (exitCode.HasValue)
						{
							if (result is int i && i == exitCode.Value)
								Console.Write("pass");
							else
								Console.Write("fail");
						}
						else if (result is int i && i != 0)//This is for when an exception is thrown in the compiled program, the catch blocks make it return 1.
							Console.Write("fail");
					}
					catch (Exception ex)
					{
						if (ex is TargetInvocationException)
							ex = ex.InnerException;

						var error = new StringBuilder();
						_ = error.AppendLine("Execution error:\n");
						_ = error.AppendLine($"{ex.GetType().Name}: {ex.Message}");
						_ = error.AppendLine();
						_ = error.AppendLine(ex.StackTrace);
						var msg = error.ToString();
						_ = KeysharpEnhancements.OutputDebugLine(msg);
						Console.Write("fail");
						Assert.IsTrue(false);
					}
					finally
					{
						writer.Flush();
						output = buffer.ToString();

						using (var console = Console.OpenStandardOutput())
						{
							var stdout = new StreamWriter(console);
							stdout.AutoFlush = true;
							Console.SetOut(stdout);
						}
					}
				}
			}

			//Make the Script object from within the script available to the calling code.
			//This is uesd in the HotstringParsing2() test.
			s = Script.TheScript;
			hsm = s.HotstringManager;
			return output;
		}

		protected void TestException(Action func)
		{
			var excthrown = false;

			try
			{
				func();
			}
			catch (Exception)
			{
				excthrown = true;
			}

			Assert.IsTrue(excthrown);
		}

		protected bool TestScript(string source, bool testfunc, bool exeout = false)
		{
			var scriptPath = string.Concat(path, source, ext);
			var b2 = true;
			var b1 = HasPassed(RunScript(scriptPath, source, true, exeout));

			if (testfunc && b1)
				b2 = HasPassed(RunScript(scriptPath, source + "_func", true, true, exeout));

			return b1 && b2;
		}

		protected string WrapInFunc(string source)
		{
			var sb = new StringBuilder();
			_ = sb.AppendLine("TestFunc()");//This must be named TestFunc because it's referenced in some of the tests.
			_ = sb.AppendLine("{");

			using (var sr = new StringReader(source))
			{
				string line;

				while ((line = sr.ReadLine()) != null)
					_ = sb.AppendLine("\t" + line);
			}

			_ = sb.AppendLine("}");
			_ = sb.AppendLine("testfunc()");//Deliberately change case to always make sure case insensitivity works.
			return sb.ToString();
		}
	}
}