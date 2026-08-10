using Keysharp.Internals.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class TestRunner
	{
		static TestRunner()
		{
#if LINUX
			var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
				|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

			if (isWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
				Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
			else if (!isWayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
				Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
#endif
		}

		protected sealed class QueuedSynchronizationContext : SynchronizationContext
		{
			private readonly Queue<(SendOrPostCallback callback, object state)> posted = new();

			internal int PendingCount => posted.Count;

			public override void Post(SendOrPostCallback d, object state) => posted.Enqueue((d, state));

			public override void Send(SendOrPostCallback d, object state) => d(state);

			internal void DrainAll()
			{
				while (posted.Count != 0)
				{
					var (callback, state) = posted.Dequeue();
					callback(state);
				}
			}
		}

		protected string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Keysharp.Tests", "Code")) + Path.DirectorySeparatorChar;
		private const string ext = ".ahk";
		protected Script s;
		internal HotstringManager hsm;

		[SetUp]
		public void SetupBeforeEachTest() => ResetScriptState();

		[TearDown]
		public void CleanupAfterEachTest()
		{
#if !WINDOWS
			if (!Script.IsUiInitializationBlocked && Application.Instance is { } app)
			{
				void CloseWindows()
				{
					foreach (var window in app.Windows.ToArray())
						window.Close();

					app.MainForm = null;
				}

				if (app.IsUIThread)
					CloseWindows();
				else
					app.Invoke(CloseWindows);
			}
#endif
			s?.Dispose();
			s = null;
			hsm = null;
		}

		private void ResetScriptState()
		{
			s?.Dispose();
			s = null;
			hsm = null;

#if !WINDOWS
			if (!Script.IsUiInitializationBlocked)
				_ = Application.Instance ?? new Application();
#endif
			s = new Script();
			hsm = s.HotstringManager;
		}

		protected static void SkipIfUiInitializationBlocked(string reason)
		{
			if (Script.IsUiInitializationBlocked)
				Assert.Ignore(reason);
		}

		protected QueuedSynchronizationContext UseQueuedMainContext()
		{
			var context = new QueuedSynchronizationContext();
			s.UIThreadContext = context;
			return context;
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
			ResetScriptState();
			s.SetName(name);
			var ch = new CompilerHelper();
			var (arr, code, _) = ch.CompileCodeToByteArray(source, name);

			if (arr == null)
			{
				_ = Ks.OutputDebugLine(code);
				return code;
			}

			// Avoid writing generated script sources into the repo tree, which can pollute subsequent test builds.

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
						Environment.ExitCode = 0;
#if WINDOWS
						var result = StaTask.RunSync(() => main.Invoke(null, [temp]));
#else
						object result = null;
						try
						{
							result = main.Invoke(null, [temp]);
						}
						catch (Keysharp.Builtins.Flow.UserRequestedExitException)
						{
						}
#endif

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
						_ = Ks.OutputDebugLine(msg);
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

		// Keep module-member blocks outside the function wrapper.
		private static (string Blocks, string Script) LiftCSharpBlocks(string source)
		{
			var blocks = new StringBuilder();
			var rest = new StringBuilder();

			using var sr = new StringReader(source);
			string line;

			while ((line = sr.ReadLine()) != null)
			{
				var trimmed = line.TrimStart(Keywords.Spaces);

				// Reuse the lexer rule so file forms and comments classify identically.
				if (!Keysharp.Parsing.Lexing.Lexer.IsCSharpBlockOpener(trimmed))
				{
					_ = rest.AppendLine(line);
					continue;
				}

				_ = blocks.AppendLine(line);

				while ((line = sr.ReadLine()) != null)
				{
					_ = blocks.AppendLine(line);

					if (Keysharp.Parsing.Lexing.Lexer.IsCSharpBlockTerminator(line))
						break;
				}
			}

			return (blocks.ToString(), rest.ToString());
		}

		protected string WrapInFunc(string source)
		{
			var sb = new StringBuilder();
			var (csharpBlocks, body) = LiftCSharpBlocks(source);
			source = body;

			using (var sr = new StringReader(source))
			{
				string line;

				while ((line = sr.ReadLine()) != null)
				{
					var trimmed = line.TrimStart(Keywords.Spaces);

					if (trimmed.Length == 0
						|| trimmed.StartsWith(';')
						|| (trimmed.StartsWith('#') && !trimmed.StartsWith("#if ")))
					{
						_ = sb.AppendLine(line);
						line = null;
					}
					else
						break;
				}

				// Inline members belong at module scope.
				if (csharpBlocks.Length != 0)
					_ = sb.Append(csharpBlocks);

				_ = sb.AppendLine("TestFunc()");//This must be named TestFunc because it's referenced in some of the tests.
				_ = sb.AppendLine("{");

				if (line != null)
					_ = sb.AppendLine("\t" + line);

				while ((line = sr.ReadLine()) != null)
					_ = sb.AppendLine("\t" + line);
			}

			_ = sb.AppendLine("}");
			_ = sb.AppendLine("testfunc()");//Deliberately change case to always make sure case insensitivity works.
			return sb.ToString();
		}
	}
}
