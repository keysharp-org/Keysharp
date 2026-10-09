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
			Assert.That(Console.Out, Is.SameAs(previousOutput), "A script run must restore the test host's output writer.");
			Assert.That(compilationContext, Is.Not.Null);
			Assert.That(compilationContext.IsDisposed, Is.EqualTo(execute),
				"An executing program must release its compilation context; a compile-only run still owns it.");
			Assert.That(ReferenceEquals(compilationContext, s), Is.EqualTo(!execute));

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

			Assert.That(command.Kind, Is.EqualTo(CliCommandKind.RunSource));
			Assert.That(command.ScriptName, Is.EqualTo(Path.GetFullPath(scriptPath)));
			Assert.IsTrue(command.Validate);
			Assert.That(command.CodePage, Is.EqualTo(65001));
			Assert.That(command.IncludeFile, Is.EqualTo(Path.GetFullPath(includePath)));
			Assert.That(command.KeysharpArgs, Is.EqualTo(args.Take(args.Length - 2).ToArray()));
			Assert.That(command.ScriptArgs, Is.EqualTo(["script-arg"]));

			s.KeysharpArgs = command.KeysharpArgs;
			Assert.That(Env.FindCommandLineArg("errorstdout"), Is.EqualTo("/ErrorStdOut=UTF-8"));
			Assert.That(Env.FindCommandLineArgVal("include"), Is.EqualTo(includePath));
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
			Assert.That(Runner.Message("source routing", true, errorStdOut: true), Is.EqualTo(1));
			Assert.IsTrue(output.ToString().Contains("source routing", StringComparison.Ordinal));

			_ = output.GetStringBuilder().Clear();
			s.KeysharpArgs = ["--errorstdout"];
			Assert.That(Runner.Message("command-line routing", true, errorStdOut: false), Is.EqualTo(1));
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

			Assert.That(command.ScriptName, Is.EqualTo(Path.GetFullPath(scriptPath)));
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
		Assert.That(CompileDaemonSecurity.IsExpectedServer(client), Is.False);
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
		Assert.That(CompileDaemonSecurity.HasDaemonArguments(commandLine, "Keysharp.exe", null), Is.EqualTo(expected));

	[Test, Category("Internal")]
	public void CompileDaemonCommandLine()
	{
		using var process = System.Diagnostics.Process.GetCurrentProcess();
		Assert.That(CompileDaemonSecurity.ReadCommandLine(process), Is.EqualTo(Marshal.PtrToStringUni(GetCommandLineW())));
		Assert.IsTrue(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Keysharp.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"));
		Assert.That(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Other.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"), Is.False);
	}

	[DllImport("kernel32.dll")]
	private static extern nint GetCommandLineW();
#endif
}
