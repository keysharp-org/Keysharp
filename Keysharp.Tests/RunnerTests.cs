using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests;

public class RunnerTests : TestRunner
{
	[TestCase(true), TestCase(false), Category("Internal"), Category("Curated")]
	public void CompilationContextLifetime(bool execute)
	{
		Script compilationContext = null;

		void CaptureContext(object sender, AssemblyLoadEventArgs args)
		{
			if (args.LoadedAssembly.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}") != null)
				compilationContext = Script.TheScript;
		}

		AppDomain.CurrentDomain.AssemblyLoad += CaptureContext;

		try
		{
			var previousOutput = Console.Out;
			var output = RunScript("#ErrorStdOut\n#Warn All, StdOut\nFileAppend 'pass', '*'\n",
				"compilation-context-lifetime", execute, false);
			Assert.AreSame(previousOutput, Console.Out, "A script run must restore the test host's output writer.");
			Assert.IsNotNull(compilationContext);
			Assert.AreEqual(execute, compilationContext.IsDisposed,
				"An executing program must release its compilation context; a compile-only run still owns it.");
			Assert.AreEqual(!execute, ReferenceEquals(compilationContext, s));

			if (execute)
				Assert.IsTrue(HasPassed(output));
		}
		finally
		{
			AppDomain.CurrentDomain.AssemblyLoad -= CaptureContext;
			compilationContext?.Dispose();
		}
	}

	[Test, Category("Misc")]
	public void CommandLineSwitches()
	{
		var scriptPath = Path.GetTempFileName();
		var includePath = Path.GetTempFileName();

		try
		{
			var args = new[]
			{
				"/force",
				"/f",
				"/restart",
				"/r",
				"/ErrorStdOut=UTF-8",
				"/Debug",
				"/CP65001",
				"/Validate",
				"/iLib",
				"ignored.txt",
				"/include",
				includePath,
				scriptPath,
				"script-arg"
			};
			var command = Runner.Parse(args);

			Assert.AreEqual(CliCommandKind.RunSource, command.Kind);
			Assert.AreEqual(Path.GetFullPath(scriptPath), command.ScriptName);
			Assert.IsTrue(command.Validate);
			Assert.AreEqual(65001, command.CodePage);
			Assert.AreEqual(Path.GetFullPath(includePath), command.IncludeFile);
			Assert.AreEqual(args.Take(args.Length - 2).ToArray(), command.KeysharpArgs);
			Assert.AreEqual(new[] { "script-arg" }, command.ScriptArgs);

			s.KeysharpArgs = command.KeysharpArgs;
			Assert.AreEqual("/ErrorStdOut=UTF-8", Env.FindCommandLineArg("errorstdout"));
			Assert.AreEqual(includePath, Env.FindCommandLineArgVal("include"));
		}
		finally
		{
			File.Delete(scriptPath);
			File.Delete(includePath);
		}
	}

	[Test, Category("Misc")]
	public void ErrorStdOutRouting()
	{
		var previousError = Console.Error;
		var previousArgs = s.KeysharpArgs;
		using var output = new StringWriter();

		try
		{
			Console.SetError(output);
			s.KeysharpArgs = [];
			Assert.AreEqual(1, Runner.Message("source routing", true, errorStdOut: true));
			Assert.IsTrue(output.ToString().Contains("source routing", StringComparison.Ordinal));

			output.GetStringBuilder().Clear();
			s.KeysharpArgs = ["--errorstdout"];
			Assert.AreEqual(1, Runner.Message("command-line routing", true, errorStdOut: false));
			Assert.IsTrue(output.ToString().Contains("command-line routing", StringComparison.Ordinal));
		}
		finally
		{
			s.KeysharpArgs = previousArgs;
			Console.SetError(previousError);
		}
	}

	[Test, Category("Misc")]
	public void AbsoluteScriptPath()
	{
		var scriptPath = Path.GetTempFileName();

		try
		{
			var command = Runner.Parse([scriptPath]);

			Assert.AreEqual(Path.GetFullPath(scriptPath), command.ScriptName);
			Assert.IsEmpty(command.KeysharpArgs);
		}
		finally
		{
			File.Delete(scriptPath);
		}
	}

#if WINDOWS
	[Test, Category("Internal")]
	public void CompileDaemonRejectsUnregisteredServer()
	{
		var name = "keysharp-daemon-test-" + Guid.NewGuid().ToString("N");
		using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous,
			TokenImpersonationLevel.Identification);
		var waiting = server.WaitForConnectionAsync();
		client.Connect(2000);
		Assert.IsTrue(waiting.Wait(2000));
		Assert.IsFalse(CompileDaemonSecurity.IsExpectedServer(client));
	}

	[Category("Internal")]
	[TestCase("host --daemon", true)]
	[TestCase("host --Daemon", true)]
	[TestCase("\"C:\\Program Files\\Keysharp.exe\" --daemon", true)]
	[TestCase("host script.ahk", false)]
	[TestCase("host script.ahk --daemon", false)]
	[TestCase("host --daemon stop", false)]
	[TestCase("host --daemonx", false)]
	public void CompileDaemonArguments(string commandLine, bool expected) =>
		Assert.AreEqual(expected, CompileDaemonSecurity.HasDaemonArguments(commandLine, "Keysharp.exe", null));

	[Test, Category("Internal")]
	public void CompileDaemonCommandLine()
	{
		using var process = System.Diagnostics.Process.GetCurrentProcess();
		Assert.AreEqual(Marshal.PtrToStringUni(GetCommandLineW()), CompileDaemonSecurity.ReadCommandLine(process));
		Assert.IsTrue(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Keysharp.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"));
		Assert.IsFalse(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Other.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"));
	}

	[DllImport("kernel32.dll")]
	private static extern nint GetCommandLineW();
#endif
}
