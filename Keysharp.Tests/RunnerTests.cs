using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Keysharp.Tests;

public class RunnerTests : TestRunner
{
	[TestCase("net10.0"), TestCase("net10.0/osx-arm64"), TestCase("net10.0-windows/win-arm64")]
	[Category("Internal"), Category("Curated")]
	public void FixtureRoot(string outputDirectory)
	{
		var output = Path.Combine(RepositoryRoot, "bin", "Debug", outputDirectory);
		Assert.That(FindRepositoryRoot(output), Is.EqualTo(RepositoryRoot));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void FixturePathsOutsideWorkingDirectory()
	{
		var workingDirectory = Directory.GetCurrentDirectory();

		try
		{
			Directory.SetCurrentDirectory(Path.GetTempPath());
			Assert.IsTrue(TestScript("file-filegetsize", true));
		}
		finally
		{
			Directory.SetCurrentDirectory(workingDirectory);
		}
	}

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
			foreach (var startsWith in new[] { true, false })
			{
				Assert.That(Assert.Throws<ArgumentNullException>(() => Env.FindCommandLineArg(null, startsWith)).ParamName,
					Is.EqualTo("value"));
				Assert.That(Assert.Throws<ArgumentNullException>(() => Env.FindCommandLineArgVal(null, startsWith)).ParamName,
					Is.EqualTo("value"));
			}
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

	[TestCase(false), TestCase(true), Category("Internal")]
	public void AppHostLocatorVersions(bool sdk)
	{
		WithAppHostRoot(root =>
		{
			_ = WriteAppHostTemplate(root, sdk, "10.0.99");
			_ = WriteAppHostTemplate(root, sdk, "10.0.100-preview.9");
			var preview = WriteAppHostTemplate(root, sdk, "10.0.100-preview.10");
			var candidate = WriteAppHostTemplate(root, sdk, "10.0.100-rc.1");
			var release = WriteAppHostTemplate(root, sdk, "10.0.100");
			_ = WriteAppHostTemplate(root, sdk, "100.0.100");
			_ = WriteAppHostTemplate(root, sdk, "10.bad");
			var missing = WriteAppHostTemplate(root, sdk, "10.0.999");

			File.Delete(missing);
			Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(release));
			File.Delete(release);
			Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(candidate));
			File.Delete(candidate);
			Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(preview));
		});
	}

	[Test, Category("Internal")]
	public void AppHostLocatorSdkMetadata()
	{
		WithAppHostRoot(root =>
		{
			var fallback = WriteAppHostTemplate(root, true, "10.0.100");
			var preferred = WriteAppHostTemplate(root, true, "10.0.200");
			var metadata = Path.Combine(root, "sdk", "10.0.200", "Microsoft.NETCoreSdk.BundledVersions.props");
			var rid = RuntimeInformation.RuntimeIdentifier;
			var runtimeRid = $"<NETCoreSdkRuntimeIdentifier>{rid}</NETCoreSdkRuntimeIdentifier>";
			var portableRid = $"<NETCoreSdkPortableRuntimeIdentifier>{rid}</NETCoreSdkPortableRuntimeIdentifier>";

			File.Delete(metadata);
			Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(fallback));

			foreach (var contents in new[] { "<Project>", "<Project />",
				$"<Project><PropertyGroup><NETCoreSdkPortableRuntimeIdentifier>other-rid</NETCoreSdkPortableRuntimeIdentifier>{runtimeRid}</PropertyGroup></Project>" })
			{
				File.WriteAllText(metadata, contents);
				Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(fallback), contents);
			}

			foreach (var properties in new[] { runtimeRid,
				"<NETCoreSdkPortableRuntimeIdentifier />" + runtimeRid,
				portableRid + "<NETCoreSdkRuntimeIdentifier>other-rid</NETCoreSdkRuntimeIdentifier>" })
			{
				File.WriteAllText(metadata, $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>");
				Assert.That(Keysharp.Main.AppHostLocator.Find([root]), Is.EqualTo(preferred), properties);
			}
		});
	}

	[Test, Category("Internal")]
	public void AppHostLocatorRoots()
	{
		WithAppHostRoot(root =>
		{
			var first = Path.Combine(root, "first");
			var second = Path.Combine(root, "second");
			_ = WriteAppHostTemplate(first, false, "9.0.100");
			var fallback = WriteAppHostTemplate(second, true, "10.0.200");
			Assert.That(Keysharp.Main.AppHostLocator.Find([null, "\0", first, second]), Is.EqualTo(fallback));
			Assert.That(Keysharp.Main.AppHostLocator.Find([first]), Is.Null);
			_ = WriteAppHostTemplate(first, true, "10.0.300");
			var preferred = WriteAppHostTemplate(first, false, "10.0.100");
			Assert.That(Keysharp.Main.AppHostLocator.Find([first, second]), Is.EqualTo(preferred));

			var variable = $"DOTNET_ROOT_{RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}";
			var previous = Environment.GetEnvironmentVariable(variable);
			var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

			try
			{
				Environment.SetEnvironmentVariable(variable, first);
				Environment.SetEnvironmentVariable("DOTNET_ROOT", second);
				Assert.That(Keysharp.Main.AppHostLocator.Find(), Is.EqualTo(preferred));
			}
			finally
			{
				Environment.SetEnvironmentVariable(variable, previous);
				Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
			}
		});
	}

	private static string WriteAppHostTemplate(string root, bool sdk, string version)
	{
		var rid = RuntimeInformation.RuntimeIdentifier;
#if WINDOWS
		var name = "apphost.exe";
#else
		var name = "apphost";
#endif
		var target = sdk ? Path.Combine(root, "sdk", version, "AppHostTemplate", name)
			: Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{rid}", version, "runtimes", rid, "native", name);

		Directory.CreateDirectory(Path.GetDirectoryName(target));
		File.WriteAllText(target, "apphost");

		if (sdk)
			File.WriteAllText(Path.Combine(root, "sdk", version, "Microsoft.NETCoreSdk.BundledVersions.props"),
				$"<Project><PropertyGroup><NETCoreSdkPortableRuntimeIdentifier>{rid}</NETCoreSdkPortableRuntimeIdentifier></PropertyGroup></Project>");

		return target;
	}

	private static void WithAppHostRoot(Action<string> action)
	{
		var root = Directory.CreateTempSubdirectory("keysharp-apphost-").FullName;

		Assert.That(Path.GetDirectoryName(root), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))));

		try
		{
			action(root);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test, Category("Internal")]
	public void FingerprintMvid()
	{
		Assert.That(CompileDaemonFingerprint.Hash("hello"u8), Is.EqualTo(0xA430D84680AABD0BUL));
		foreach (var assembly in new[] { typeof(Script).Assembly, typeof(Keysharp.Main.Program).Assembly, typeof(RunnerTests).Assembly,
			typeof(object).Assembly, typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly })
			Assert.That(CompileDaemonFingerprint.ReadMvid(assembly.Location), Is.EqualTo(assembly.ManifestModule.ModuleVersionId), assembly.GetName().Name);
	}

	[Test, Category("Internal")]
	public void FingerprintUncompressedMetadata()
	{
		var image = File.ReadAllBytes(typeof(Keysharp.Main.Program).Assembly.Location);
		using var pe = new PEReader(new MemoryStream(image));
		var root = pe.PEHeaders.MetadataStartOffset;
		var name = image.AsSpan(root, pe.PEHeaders.MetadataSize).IndexOf("#~\0"u8);
		Assert.That(name, Is.GreaterThanOrEqualTo(0));
		image[root + name + 1] = (byte)'-';
		using var uncompressed = new PEReader(new MemoryStream(image));
		var metadata = uncompressed.GetMetadataReader();
		var expected = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
		var path = Path.GetTempFileName();

		try
		{
			File.WriteAllBytes(path, image);
			Assert.That(CompileDaemonFingerprint.ReadMvid(path), Is.EqualTo(expected));
		}
		finally { File.Delete(path); }
	}

	[Test, Category("Internal")]
	public void FingerprintInvalidImage()
	{
		var original = File.ReadAllBytes(typeof(Keysharp.Main.Program).Assembly.Location);
		using var pe = new PEReader(new MemoryStream(original));
		var path = Path.GetTempFileName();

		try
		{
			foreach (var offset in new[] { 0, BitConverter.ToInt32(original, 0x3C), pe.PEHeaders.MetadataStartOffset })
			{
				var image = (byte[])original.Clone();
				image[offset] = 0;
				File.WriteAllBytes(path, image);
				Assert.Throws<BadImageFormatException>(() => CompileDaemonFingerprint.ReadMvid(path), $"Signature at {offset}");
			}
		}
		finally { File.Delete(path); }
	}

	[Test, Category("Internal")]
	public void DaemonFallback()
	{
		var retryFailure = false;
#if WINDOWS
		retryFailure = CompileDaemonSecurity.IsElevated;
#endif
		Assert.That(CompileDaemonClient.ShouldRetryLocally(null, false), Is.True);
		Assert.That(CompileDaemonClient.ShouldRetryLocally(DaemonReply.Unavailable, false), Is.True);
		Assert.That(CompileDaemonClient.ShouldRetryLocally(new(CompileDaemonStatus.Compiled), true), Is.False);
		Assert.That(CompileDaemonClient.ShouldRetryLocally(new(CompileDaemonStatus.PackageRestoreNeeded), true), Is.True);
		Assert.That(CompileDaemonClient.ShouldRetryLocally(new(CompileDaemonStatus.PackageRestoreNeeded), false), Is.EqualTo(retryFailure));
		Assert.That(CompileDaemonClient.ShouldRetryLocally(new(CompileDaemonStatus.CompileFailed), true), Is.EqualTo(retryFailure));
	}

#if WINDOWS

	// The host accepts the replacement's request without exiting, as a busy or vetoing predecessor would.
	[Test, Category("Internal")]
	public void ReloadWaitTimeout()
	{
		var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

		if (!File.Exists(launcher))
			Assert.Ignore($"launcher not built at {launcher}");

		var script = Path.Combine(Path.GetTempPath(), "keysharp-reload-" + Guid.NewGuid().ToString("N") + ".ahk");

		try
		{
			File.WriteAllText(script, "#ErrorStdOut\n#Warn All, StdOut\n#NoTrayIcon\nFileAppend 'replacement-started', '*'\n");
			using var requests = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
			using var cancellation = new CancellationTokenSource();
			var start = new ProcessStartInfo(launcher)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				ArgumentList = { script }
			};
			start.Environment["KEYSHARP_DAEMON"] = "0";
			start.Environment["KEYSHARP_FORCE_HEADLESS"] = "0";
			start.Environment[ReloadHandshake.HandshakeVar] = $"{Environment.ProcessId},{requests.GetClientHandleAsString()}";
			using var process = Process.Start(start);
			requests.DisposeLocalCopyOfClientHandle();
			var stdout = process.StandardOutput.ReadToEndAsync();
			var stderr = process.StandardError.ReadToEndAsync();

			try
			{
				var request = new byte[1];
				var asked = requests.ReadAsync(request, 0, 1, cancellation.Token);
				Assert.That(asked.Wait(30000) && asked.Result == 1 && request[0] == 1, Is.True, "The replacement asks once it is compiled and about to run.");
				var elapsed = Stopwatch.StartNew();

				Assert.That(process.WaitForExit(35000), Is.True, "The replacement must time out without opening a retry dialog.");
				Assert.That(process.ExitCode, Is.EqualTo(2), "A replacement which did not run exits as a critical error, as in AutoHotkey.");
				// The child starts waiting before the host observes its request; allow scheduling delay at this boundary.
				Assert.That(elapsed.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(14)));
				Assert.That(stdout.GetAwaiter().GetResult(), Is.Empty, "The replacement must not execute while the predecessor is alive.");
				Assert.That(stderr.GetAwaiter().GetResult(), Does.Contain("did not exit within 15000 ms."));
			}
			finally
			{
				cancellation.Cancel();
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
					_ = process.WaitForExit(5000);
				}
			}
		}
		finally { File.Delete(script); }
	}

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
		Assert.That(CompileDaemonSecurity.IsExpectedServer(client, name, Environment.ProcessPath,
			typeof(Keysharp.Main.Program).Assembly.Location), Is.False);
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
		Assert.That(CompileDaemonSecurity.ReadCommandLine(process.Handle), Is.EqualTo(Marshal.PtrToStringUni(GetCommandLineW())));
		Assert.IsTrue(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Keysharp.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"));
		Assert.That(CompileDaemonSecurity.HasDaemonArguments("dotnet \"C:\\Other.dll\" --daemon", "dotnet.exe", "C:\\Keysharp.dll"), Is.False);
		var image = Processes.GetProcessImage((uint)process.Id, false);
		Assert.That(image, Is.Not.Empty);
		Assert.That(CompileDaemonSecurity.HasImage(process.Handle, image), Is.True);
		Assert.That(CompileDaemonSecurity.HasImage(process.Handle, image.ToUpperInvariant()), Is.True);
		Assert.That(CompileDaemonSecurity.HasImage(process.Handle, image + ".other"), Is.False);
		Assert.That(CompileDaemonSecurity.HasImage(process.Handle, "x"), Is.False);
		Assert.That(CompileDaemonSecurity.HasImage(0, Environment.ProcessPath), Is.False);
		Assert.That(CompileDaemonSecurity.ReadCommandLine(0), Is.Null);
	}

	[DllImport("kernel32.dll")]
	private static extern nint GetCommandLineW();

	[Test, Category("Internal")]
	public void CompileDaemonRejectsPayloadAliases()
	{
		var directory = Directory.CreateTempSubdirectory("keysharp-daemon-paths-").FullName;
		try
		{
			var original = Path.Combine(directory, "Keysharp.dll");
			var alias = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "Other")).FullName, "Keysharp.dll");
			File.WriteAllText(original, "payload");
			Assert.That(CreateHardLinkW(alias, original, 0), Is.True);
			Assert.That(CompileDaemonSecurity.HasDaemonArguments($"dotnet \"{alias}\" --daemon", "dotnet.exe", original), Is.False);
		}
		finally { Directory.Delete(directory, true); }
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateHardLinkW(string link, string existing, nint securityAttributes);

#endif
}
