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
#elif OSX
			// AppKit's event loop cannot run inside testhost, but Eto's drawing handlers still work without one.
			_ = Eto.Platform.Detect;
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
		private int wrapOffset;
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

		// A script is silent while it succeeds and writes a single "pass" once it reaches its end, so anything else
		// in the output — a "fail <tag>" from Assert, an error trace, or nothing at all because the run died
		// partway — is a failure. Matching only "pass"es would score a script that stopped halfway as passing.
		protected static bool HasPassed(string output) => output?.Trim() == "pass";

		protected string RunScript(string source, string name, bool execute, bool wrapinfunction, bool exeout, int? exitCode = null) => RunScript(WrapInFunc(File.ReadAllText(source)), name, execute, exeout, exitCode);

		protected string RunScript(string source, string name, bool execute, bool exeout, int? exitCode = null)
		{
			ResetScriptState();
			s.SetName(name);
			var ch = new CompilerHelper();
			//Source handed over as text has no directory of its own, so `#Include <assert>` and friends resolve
			//against Code/ the same way they do when the file itself is compiled.
			var (arr, code, _) = ch.CompileCodeToByteArray(source, name, includeDirOverride: path);

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

			ScriptExecutionState.Assembly = Assembly.Load(arr);
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

						if (ScriptExecutionState.Assembly == null)
							throw new Exception("Compilation failed.");

						//Environment.SetEnvironmentVariable("SCRIPT", script);
						var program = ScriptExecutionState.Assembly.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}");
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

						//Silent on success, like the script's own assertions: the run's only "pass" is the one the script writes.
						if (exitCode.HasValue)
						{
							if (!(result is int i && i == exitCode.Value))
								Console.Write($"fail exit {result} (expected {exitCode.Value})");
						}
						else if (result is int i2 && i2 != 0)//This is for when an exception is thrown in the compiled program, the catch blocks make it return 1.
							Console.Write($"fail exit {i2}");
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
						Assert.Fail(msg);
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

			//The compiled program builds its own Script; take it over so the test can read back what the run registered.
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
			Verify(scriptPath, RunScript(scriptPath, source, true, exeout), 0);

			if (testfunc)
				Verify(scriptPath, RunScript(scriptPath, source + "_func", true, true, exeout), wrapOffset);

			return true;
		}

		// Fails with what the script actually wrote, so the tag Assert emitted names the line instead of the test
		// reporting "expected True". The wrapped run compiles two extra lines ahead of the body, shifting its tags.
		private static void Verify(string scriptPath, string output, int lineOffset)
		{
			if (HasPassed(output))
				return;

			var shift = lineOffset != 0 ? $" (wrapped in TestFunc: subtract {lineOffset} from each line)" : "";
			Assert.Fail($"{scriptPath}{shift}\n{(string.IsNullOrWhiteSpace(output) ? "<no output: the run ended before the final pass>" : output.Trim())}");
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

				// Inline members belong at module scope. Lifting them out of the body moves the lines around it, so
				// the body's A_LineNumber shift is only knowable when there are none.
				wrapOffset = csharpBlocks.Length == 0 ? 2 : 0;

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
