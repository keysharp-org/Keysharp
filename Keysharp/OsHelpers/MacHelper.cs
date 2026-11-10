#if OSX
using MonoMac.Foundation;

namespace Keysharp.Main;

internal class MacHelper
{
	/// <summary>
	/// Separates AppKit's launch-argument callbacks from later open-document requests.
	/// </summary>
	private static readonly DocumentOpenFilter macOsDocuments = new(AppContext.BaseDirectory);

	/// <summary>
	/// Stamping the script's path into the apphost invalidates the signature the template arrived with, and
	/// macOS on arm64 refuses to exec a binary whose signature does not verify: the kernel sends SIGKILL
	/// before any managed code runs, so the exe would die with exit 137 and no diagnostic. An ad-hoc
	/// signature carries no identity but satisfies that check. HostModel cannot do this itself - the public
	/// package is the .NET Core 3.1 one, which predates Mach-O signing entirely - so sign out of process.
	/// /usr/bin/codesign is part of the base system, not the Command Line Tools.
	/// </summary>
	internal static string AdHocSign(string path)
	{
		try
		{
			using var proc = Process.Start(new ProcessStartInfo("/usr/bin/codesign")
			{
				ArgumentList = { "--force", "--sign", "-", path },
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
			});

			if (proc == null)
				return $"Could not start /usr/bin/codesign to sign {path}.";

			var stderr = proc.StandardError.ReadToEnd();
			_ = proc.StandardOutput.ReadToEnd();
			proc.WaitForExit();
			return proc.ExitCode == 0 ? null : $"Signing {path} failed: codesign exited with {proc.ExitCode}. {stderr.Trim()}";
		}
		catch (Exception ex)
		{
			return $"Signing {path} failed: {ex.Message}";
		}
	}

	/// <summary>
	/// On macOS, double-clicking a .ks/.ahk file sends an Apple Event rather than a command-line
	/// argument. Receive it via Eto's AppDelegate before the normal arg-parsing pipeline.
	/// </summary>
	/// <param name="args">The original program command line argument strings.</param>
	/// <returns>The modified args</returns>
	internal static string[] HandleArgs(string[] args)
	{
		var launchedFromFinder = args.Length == 0;

		if (launchedFromFinder)
			args = MacHelper.WaitForMacOsDocumentOpen();

		return args;
	}

	/// <summary>
	/// Finder can route document opens to any running Keysharp instance. Each dispatches those requests to a
	/// separate process, whose normal single-instance policy decides whether to replace an earlier script.
	/// Utility commands never start the AppKit loop and do not subscribe.
	/// </summary>
	internal static void HandleCommand(CliCommand command)
	{
		if (command.Kind is CliCommandKind.RunSource or CliCommandKind.RunAssembly)
		{
			if (!string.IsNullOrEmpty(command.ScriptName))
				macOsDocuments.AddLaunchDocument(command.ScriptName);

			// The re-delivery is not limited to the script: any argv entry naming an existing file can come
			// back as a "document" — for a "dotnet Keysharp.dll <script>" launch that includes Keysharp.dll
			// itself, and re-spawning THAT is a scriptless launcher stuck on its error dialog forever.
			foreach (var arg in Environment.GetCommandLineArgs())
				if (!string.IsNullOrEmpty(arg) && File.Exists(arg))
					macOsDocuments.AddLaunchDocument(arg);

			Eto.Mac.AppDelegate.FileOpened += SpawnScriptInNewProcess;
		}
	}

	/// <summary>
	/// Start a minimal Eto Application, wait up to 1 s for macOS to deliver the "open file" Apple
	/// Event (via AppDelegate.OpenFile / OpenFiles), then stop the loop and return the first path.
	/// If no event arrives the method returns an empty array and the normal "no script" error follows.
	/// The Application instance is deliberately NOT disposed: EnsureEtoApplication() reuses it for
	/// GUI scripts that call RunMainWindow → app.Run() afterwards.
	/// </summary>
	internal static string[] WaitForMacOsDocumentOpen()
	{
		string openedPath = null;
		var app = Application.Instance ?? new Application();

		void OnFileOpened(string path)
		{
			Volatile.Write(ref openedPath, path);
			Eto.Mac.AppDelegate.FileOpened -= OnFileOpened;
			app.AsyncInvoke(app.Quit);
		}

		Eto.Mac.AppDelegate.FileOpened += OnFileOpened;

		var timeoutThread = new Thread(() =>
		{
			Thread.Sleep(1000);

			if (Volatile.Read(ref openedPath) == null)
				app.AsyncInvoke(app.Quit);
		})
		{ IsBackground = true };
		timeoutThread.Start();

		app.Run();

		Eto.Mac.AppDelegate.FileOpened -= OnFileOpened;
		var path = Volatile.Read(ref openedPath);
		return path != null ? [path] : [];
	}
	/// <summary>
	/// Canonicalize including symlinked path components (macOS's open-document event delivers the resolved
	/// path, e.g. /private/tmp/x, while a launch argument may be /tmp/x); Path.GetFullPath does not resolve
	/// symlinks, so use realpath.
	/// </summary>
	private static string CanonicalPath(string path)
	{
		try
		{
			var ptr = realpath(path, IntPtr.Zero);

			if (ptr != IntPtr.Zero)
			{
				try
				{ return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr) ?? Path.GetFullPath(path); }
				finally { free(ptr); }
			}
		}
		catch { }

		try
		{ return Path.GetFullPath(path); }
		catch { return path; }
	}

	internal sealed class DocumentOpenFilter(string runtimeDirectory)
	{
		private readonly HashSet<string> launchDocuments = new(StringComparer.OrdinalIgnoreCase);
		private readonly string runtimeDirectoryPath = CanonicalPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar);

		internal void AddLaunchDocument(string path) => _ = launchDocuments.Add(CanonicalPath(path));

		internal bool ShouldDispatch(string path, bool isOpenDocumentRequest)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return false;

			var canonicalPath = CanonicalPath(path);

			// AppKit synthesizes launch-file callbacks without an Apple event, sometimes more than once.
			// A real open-document request for the same path must still reach #SingleInstance.
			if (!isOpenDocumentRequest && launchDocuments.Contains(canonicalPath))
				return false;

			return !string.Equals(Path.GetDirectoryName(canonicalPath), runtimeDirectoryPath, StringComparison.OrdinalIgnoreCase);
		}
	}

	[System.Runtime.InteropServices.DllImport("libc")]
	private static extern void free(IntPtr ptr);

	[System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
	private static extern IntPtr realpath(string path, IntPtr resolved);

	/// <summary>
	/// Launch a script that macOS handed to this already-running instance as its own Keysharp process, so each
	/// script runs independently (like double-clicking one when nothing is running). Re-exec THIS apphost with
	/// the script path as a plain argument, which bypasses LaunchServices' single-instance document routing.
	/// </summary>
	private static void SpawnScriptInNewProcess(string path)
	{
		try
		{
			var appleEvent = NSAppleEventManager.SharedAppleEventManager.CurrentAppleEvent;
			var isOpenDocumentRequest = appleEvent is { EventClass: AEEventClass.AppleEvent, EventID: AEEventID.OpenDocuments };

			if (!macOsDocuments.ShouldDispatch(path, isOpenDocumentRequest))
				return;

			var exe = Environment.ProcessPath; // the Keysharp apphost inside this .app bundle

			if (!string.IsNullOrEmpty(exe))
			{
				var start = new System.Diagnostics.ProcessStartInfo
				{
					FileName = exe,
					UseShellExecute = false,
				};

				// Under the muxer ("dotnet Keysharp.dll …") the entry dll must be re-inserted; handing dotnet
				// just the script path would try to run the script itself as a managed app.
				if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
						&& Assembly.GetEntryAssembly()?.Location is { Length: > 0 } entryAsm)
					start.ArgumentList.Add(entryAsm);

				start.ArgumentList.Add(path);
				_ = System.Diagnostics.Process.Start(start);
			}
		}
		catch
		{
		}
	}
}
#endif
