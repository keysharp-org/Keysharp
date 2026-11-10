using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
#if WINDOWS
using Keysharp.Internals.Os.Windows;
#endif

namespace Keysharp.Tests;

public class RunnerTests : TestRunner
{
	[Test, Category("Internal"), Category("Curated")]
	public void RestartUsesEffectiveArguments()
	{
		var previousArgs = s.LaunchArgs;
		var args = new[] { "--define:RESTART_TEST", Path.Combine(Path.GetTempPath(), "Finder script.ks"), "argument with spaces", "é" };

		try
		{
			s.LaunchArgs = args;
			var start = Runner.CreateRestartStartInfo();
			Assert.That(start.ArgumentList.TakeLast(args.Length), Is.EqualTo(args));
			var explicitStart = Runner.CreateRestartStartInfo(["--version"]);
			Assert.That(explicitStart.ArgumentList.Last(), Is.EqualTo("--version"));
			Assert.That(explicitStart.ArgumentList.Count, Is.EqualTo(start.ArgumentList.Count - args.Length + 1));
		}
		finally { s.LaunchArgs = previousArgs; }
	}

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

	[Test, Category("Internal"), Category("Curated")]
	public async Task ScriptEndpointStartup()
	{
		var pid = Environment.ProcessId;
		var path = ScriptEndpoint.PathFor(pid);
		var processStartTicks = ScriptEndpoint.CurrentProcessStartTicks;
		_ = Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.Delete(path);

		try
		{
			Assert.That(processStartTicks, Is.GreaterThan(0));
			Assert.That(ScriptEndpoint.ProcessStartTicks(pid), Is.EqualTo(processStartTicks));
			using var timeout = new CancellationTokenSource(5000);

			using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
			{
				socket.Bind(new UnixDomainSocketEndPoint(path));
				Assert.That(await ScriptEndpoint.QueryAsync(pid), Is.Null);
				Assert.That(File.Exists(path), Is.True, "Discovery must preserve a live process's socket before Listen.");
				var request = ScriptEndpoint.RequestAsync(pid, processStartTicks, [ScriptEndpoint.Version, ScriptEndpoint.Info], timeout.Token);
				Assert.That(request.IsCompleted, Is.False, "A refused startup connection must wait for the listener.");
				socket.Listen();
				using var client = await socket.AcceptAsync(timeout.Token);
				using var stream = new NetworkStream(client, ownsSocket: false);
				await stream.WriteAsync(new byte[] { 3, 0, 0, 0, 7, 8, 9 }, timeout.Token);
				Assert.That(await request, Is.EqualTo(new byte[] { 7, 8, 9 }));
			}

			File.Delete(path);
			var foreignPath = ScriptEndpoint.PathFor(int.MaxValue);

			try
			{
				using var foreign = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				foreign.Bind(new UnixDomainSocketEndPoint(foreignPath));
				Assert.That(await ScriptEndpoint.QueryAsync(int.MaxValue), Is.Null);
				Assert.That(File.Exists(foreignPath), Is.True, "Discovery does not own another process's endpoint path.");
			}
			finally { File.Delete(foreignPath); }

			using var endpoint = ScriptEndpoint.Start(s);
			Assert.That(endpoint, Is.Not.Null);
			Assert.That(await ScriptEndpoint.QueryAsync(0), Is.Null);
			var info = await ScriptEndpoint.RequestAsync(pid, processStartTicks, [ScriptEndpoint.Version, ScriptEndpoint.Info], timeout.Token);
			var peer = ScriptEndpoint.DecodeInfo(pid, info);
			Assert.That(peer.ScriptPath, Is.EqualTo(s.scriptPath ?? ""));
			var retired = new KeysharpScript(pid, "*", processStartTicks + 1);
			Assert.That(retired.IsExited, Is.True, "A different process lifetime must not become the old script.");
			var rejected = (KeysharpTask)retired.Post("wrong lifetime");
			Assert.That(rejected.Wait(2000), Is.EqualTo(true));
			Assert.That(rejected.Error, Is.TypeOf<TargetError>());
			_ = await Assert.CatchAsync<IOException>(async () =>
				await ScriptEndpoint.RequestAsync(pid, processStartTicks,
					ScriptEndpoint.ExitRequest(Keysharp.Builtins.Flow.ExitReasons.Close, processStartTicks + 1), timeout.Token));
			Assert.That(s.hasExited, Is.False, "A stale script's exit request must leave the current process alone.");

			using var olderReload = new ReloadHandshake(s);
			using var currentReload = new ReloadHandshake(s);
			s.FlowData.pendingReload = currentReload;

			try
			{
				foreach (var token in new[] { 0, olderReload.RequestToken })
				{
					var oldRequest = ScriptEndpoint.ExitRequest(Keysharp.Builtins.Flow.ExitReasons.Reload, processStartTicks, token);
					var declined = await ScriptEndpoint.RequestAsync(pid, processStartTicks, oldRequest, timeout.Token);
					Assert.That(declined, Is.EqualTo(new byte[] { ScriptEndpoint.Empty }), "A reload request belongs to one attempt.");
				}

				var valid = ScriptEndpoint.ExitRequest(Keysharp.Builtins.Flow.ExitReasons.Reload, processStartTicks, currentReload.RequestToken);
				var accepted = await ScriptEndpoint.RequestAsync(pid, processStartTicks, valid, timeout.Token);
				Assert.That(accepted, Is.EqualTo(new byte[] { ScriptEndpoint.Accepted }));
			}
			finally { s.FlowData.pendingReload = null; }
		}
		finally { File.Delete(path); }
	}

	[TestCase("Ignore"), TestCase("Force"), TestCase("Off"), TestCase("Prompt"), TestCase("Default"), Category("Curated")]
	public void HeadlessSingleInstance(string mode) => CheckSingleInstance(mode, true);

#if WINDOWS
	[Test, Category("Curated")]
	public void WindowSingleInstance() => CheckSingleInstance("Force", false);
#endif

	private static void CheckSingleInstance(string mode, bool headless)
	{
		var launcher = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Keysharp.exe" : "Keysharp");

		if (!File.Exists(launcher))
			Assert.Ignore($"launcher not built at {launcher}");

		var script = Path.Combine(Path.GetTempPath(), "keysharp-instance-" + Guid.NewGuid().ToString("N") + ".ahk");
		var closeMonitor = headless ? "" : "OnMessage(0x10, (WParam, LParam, Msg, Hwnd) => (FileAppend(\"WM_CLOSE:\", \"*\"), \"\"))";
		Process previous = null, next = null;

		try
		{
			File.WriteAllText(script, $$"""
				#ErrorStdOut
				#Warn All, StdOut
				#NoTrayIcon
				{{(mode == "Default" ? "" : "#SingleInstance " + mode)}}
				if (A_Args[1] = "old") {
					Persistent
					OnExit((Reason, Code) => FileAppend(Reason, "*"))
					SetTimer(() => ExitApp(88), -45000)
					{{closeMonitor}}
					FileAppend "ready`n", "*"
				} else {
					if (A_Args[3] = "Force" && ProcessExist(A_Args[2]))
						throw Error("The previous instance is still running.")
					FileAppend "new", "*"
				}
				""");
			previous = Process.Start(Start("old"));
			var ready = previous.StandardOutput.ReadLineAsync();
			var oldError = previous.StandardError.ReadToEndAsync();
			Assert.That(ready.Wait(30000) && ready.Result == "ready", Is.True, "The previous instance must be ready to receive the retirement request.");
			var oldOutput = previous.StandardOutput.ReadToEndAsync();
			next = Process.Start(Start("new"));
			var newOutput = next.StandardOutput.ReadToEndAsync();
			var newError = next.StandardError.ReadToEndAsync();
			Assert.That(next.WaitForExit(30000), Is.True, "Forced replacement and headless launch policies must not open a dialog.");
			Assert.That(next.ExitCode, Is.Zero, newError.GetAwaiter().GetResult());
			Assert.That(newError.GetAwaiter().GetResult(), Is.Empty);
			Assert.That(newOutput.GetAwaiter().GetResult(), Is.EqualTo(mode == "Ignore" ? "" : "new"));

			if (mode == "Force")
			{
				Assert.That(previous.HasExited, Is.True, "The replacement must wait for the previous process to end.");
				Assert.That(oldOutput.GetAwaiter().GetResult(), Is.EqualTo(headless ? "Single" : "WM_CLOSE:Single"));
				Assert.That(oldError.GetAwaiter().GetResult(), Is.Empty);
			}
			else
				Assert.That(previous.HasExited, Is.False, "Ignore and Off must leave the previous instance running.");
		}
		finally
		{
			foreach (var child in new[] { next, previous })
				if (child != null)
				{
					if (!child.HasExited)
					{
						child.Kill(entireProcessTree: true);
						_ = child.WaitForExit(5000);
					}

					child.Dispose();
				}

			File.Delete(script);
		}

		ProcessStartInfo Start(string role)
		{
			var start = new ProcessStartInfo(launcher)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				ArgumentList = { script, role, (previous?.Id ?? 0).ToString(CultureInfo.InvariantCulture), mode }
			};
			start.Environment["KEYSHARP_DAEMON"] = "0";
			start.Environment["KEYSHARP_FORCE_HEADLESS"] = headless ? "1" : "0";
			return start;
		}
	}

#if WINDOWS

	// The host accepts the replacement's request without exiting, as a busy or vetoing predecessor would.
	[Test, Category("Internal")]
	public void InstanceReplacementWaitTimeout()
	{
		var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

		if (!File.Exists(launcher))
			Assert.Ignore($"launcher not built at {launcher}");

		var script = Path.Combine(Path.GetTempPath(), "keysharp-reload-" + Guid.NewGuid().ToString("N") + ".ahk");

		using var requested = new CountdownEvent(2);
		var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
		var children = new List<(Process Process, Task<string> Output, Task<string> Error)>();
		var thread = new Thread(() =>
		{
			try
			{
				using var window = new InstanceReplacementWaitWindow(requested, script);
				ready.SetResult(window.Handle);
				Application.Run();
			}
			catch (Exception ex) { ready.TrySetException(ex); }
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		nint hwnd = 0;

		try
		{
			File.WriteAllText(script, "#ErrorStdOut\n#Warn All, StdOut\n#NoTrayIcon\n#SingleInstance Force\nFileAppend 'replacement-started', '*'\n");
			Assert.That(ready.Task.Wait(10000), Is.True);
			hwnd = ready.Task.Result;
			var elapsed = Stopwatch.StartNew();

			// Both waits run together so SingleInstance and Reload exercise the same refusal without doubling the delay.
			foreach (var reload in new[] { true, false })
			{
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
				if (reload)
					start.Environment[ReloadHandshake.HandshakeVar] = FormattableString.Invariant($"{Environment.ProcessId},{hwnd},{ScriptEndpoint.CurrentProcessStartTicks},1");

				var process = Process.Start(start);
				children.Add((process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
			}

			Assert.That(requested.Wait(30000), Is.True, "Both replacements ask once they are compiled and about to run.");

			foreach (var child in children)
			{
				Assert.That(child.Process.WaitForExit(35000), Is.True, "#ErrorStdOut must prevent a retry dialog, even with a desktop.");
				Assert.That(child.Process.ExitCode, Is.EqualTo(2), "A replacement which did not run exits as a critical error, as in AutoHotkey.");
				Assert.That(child.Output.GetAwaiter().GetResult(), Is.Empty, "The replacement must not execute while the predecessor is alive.");
				Assert.That(child.Error.GetAwaiter().GetResult(), Does.Contain("did not exit within 15000 ms."));
			}

			Assert.That(elapsed.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(14)));
		}
		finally
		{
			foreach (var child in children)
			{
				if (!child.Process.HasExited)
				{
					child.Process.Kill(entireProcessTree: true);
					_ = child.Process.WaitForExit(5000);
				}

				child.Process.Dispose();
			}

			if (hwnd != 0)
				_ = WindowsAPI.PostMessage(hwnd, InstanceReplacementWaitWindow.StopMessage, (nint)0, (nint)0);

			_ = thread.Join(5000);
			File.Delete(script);
		}
	}

	private sealed class InstanceReplacementWaitWindow : NativeWindow, IDisposable
	{
		internal const int StopMessage = 0x8000;
		private readonly CountdownEvent requested;

		internal InstanceReplacementWaitWindow(CountdownEvent requested, string script)
		{
			this.requested = requested;
			CreateHandle(new CreateParams { Caption = script + " - Keysharp v" + typeof(Script).Assembly.GetName().Version });
			_ = WindowsAPI.SetProp(Handle, ScriptEndpoint.MainWindowProperty, 1);
		}

		protected override void WndProc(ref Message message)
		{
			if (message.Msg == WindowsAPI.WM_CLOSE)
			{
				if (!requested.IsSet)
					requested.Signal();
			}
			else if (message.Msg == StopMessage)
				Application.ExitThread();
			else
				base.WndProc(ref message);
		}

		public void Dispose() => DestroyHandle();
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
