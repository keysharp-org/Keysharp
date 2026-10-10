namespace Keysharp.Main;

#if WINDOWS
internal class WindowsHelper
{
	internal const uint AttachParentProcess = 0xFFFFFFFF;

	internal static string WindowsHostPackRoot => @$"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.{WindowsHostRid}\";

	// The apphost stamped onto a compiled exe has to match the architecture of the Keysharp that produced
	// it, since that exe loads the same Keysharp.Core and native dependencies sitting next to it.
	internal static string WindowsHostRid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	internal static extern bool AttachConsole(uint processId);

	// Closes every process belonging to THIS install — scripts launched through Keysharp.exe, the compile
	// daemon, and the Keyview editor — so a locked Keysharp.exe / Keysharp.Core.dll can be replaced or
	// deleted. Windows refuses to overwrite a running image or a loaded DLL, so an upgrade or uninstall
	// performed while Keysharp is running otherwise fails or defers files to a reboot, which can leave a
	// stale-version compile daemon serving against the new binaries. This is a manual command; the MSI does
	// its own version-independent close before InstallValidate (see Keysharp.Install/package-windows.ps1).
	// Best-effort: a kill failure never blocks; only an explicit "No" at the optional prompt returns nonzero.
	internal static int CloseRunningInstances(string exeDir, string[] args)
	{
		// Stop the compile daemon through its coordinator first: it may run as a different user, and
		// StopOwner kills by the recorded PID regardless. The scan below mops up anything still holding files.
		try
		{ DaemonCoordinator.StopOwner(); }
		catch { }

		var dir = Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var selfId = Environment.ProcessId;

		var targets = Process.GetProcessesByName("Keysharp")
					  .Concat(Process.GetProcessesByName("Keyview"))
					  .Where(p =>
					  {
						  if (p.Id == selfId)
							  return false;

						  try
						  {
							  var moduleDir = Path.GetDirectoryName(Path.GetFullPath(p.MainModule.FileName));
							  return string.Equals(moduleDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), dir, StringComparison.OrdinalIgnoreCase);
						  }
						  catch
						  {
							  return false; // Exited, or a process we cannot inspect (different elevation/user).
						  }
					  }).ToArray();

		if (targets.Length == 0)
			return 0;

		// Confirm before closing when a UILevel >= 5 is passed. Uses MessageBox.Show, matching how the rest
		// of Keysharp reports to the user (see Runner.Message).
		if (args.Length > 0 && int.TryParse(args[0], out var uiLevel) && uiLevel >= 5)
		{
			var prompt = $"Keysharp is currently running ({targets.Length} process(es)).\n\n"
						 + "It must be closed to continue. Close it now?\n\n"
						 + "Choose No to cancel.";

			if (MessageBox.Show(prompt, "Keysharp", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
				return 1; // Caller asked to confirm and the user declined.
		}

		foreach (var p in targets)
		{
			try
			{
				// Ask GUI scripts / Keyview to close gracefully (runs OnExit, lets them save), then force-kill
				// anything that ignores it or has no window (background scripts, the console daemon).
				if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000))
					continue;

				if (!p.HasExited)
				{
					p.Kill();
					_ = p.WaitForExit(5000);
				}
			}
			catch { /* already gone, or cannot be killed; best-effort */ }
			finally { p.Dispose(); }
		}

		return 0;
	}

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	internal static extern bool FreeConsole();

	internal static string GetLatestDotNetVersion()
			=> Directory.GetDirectories(WindowsHostPackRoot)
		.Select(Path.GetFileName)
		.Where(x => x.StartsWith(Script.dotNetMajorVersion))
		.OrderByDescending(x => new Version(x.Contains("-rc", StringComparison.OrdinalIgnoreCase) ? x[..x.IndexOf("-rc", StringComparison.OrdinalIgnoreCase)] : x))
		.FirstOrDefault();

	internal static int InstallToPath(string path, string[] args)
	{
		if (!TryResolveScope(args, out var scope, out var scopeError))
		{
			Console.Error.WriteLine(scopeError);
			return 1;
		}

		try
		{
			var (root, keyName, valueName) = PathLocationFor(scope);
			using var key = root.CreateSubKey(keyName);
			var oldPath = (string)key.GetValue(valueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames);

			if (!oldPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(s => SamePathEntry(s, path)))
				key.SetValue(valueName, oldPath.Length == 0 ? path : oldPath + (oldPath.EndsWith(';') ? path : $";{path}"), RegistryValueKind.ExpandString);

			RegisterShellIntegration(path, RootFor(scope));
			Console.WriteLine($"Registered Keysharp at '{path}' for {(scope == InstallScope.Machine ? "all users" : "the current user")}.");
			return 0;
		}
		catch (UnauthorizedAccessException)
		{
			Console.Error.WriteLine("Access denied writing the registry. Run from an elevated prompt for a machine-wide install, or pass \"user\" to install for the current user only.");
			return 1;
		}
	}

	internal static int RemoveFromPath(string path, string[] args)
	{
		if (!TryResolveScope(args, out var scope, out var scopeError))
		{
			Console.Error.WriteLine(scopeError);
			return 1;
		}

		DaemonCoordinator.StopOwner();

		// An administrator may have registered either way round, so clean both hives; an ordinary user can
		// only have written their own, and the machine attempt is skipped rather than failed. Removing what
		// is not there is a no-op, so this cannot damage the other scope's registration.
		var scopes = scope == InstallScope.Machine
					 ? new[] { InstallScope.Machine, InstallScope.User }
					 : new[] { InstallScope.User };

		foreach (var target in scopes)
		{
			try
			{
				var (root, keyName, valueName) = PathLocationFor(target);
				using var key = root.CreateSubKey(keyName);
				var oldPath = (string)key.GetValue(valueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
				var newPath = string.Join(';', oldPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !SamePathEntry(s, path)));

				if (!string.Equals(newPath, oldPath, StringComparison.Ordinal))
					key.SetValue(valueName, newPath, RegistryValueKind.ExpandString);

				UnregisterShellIntegration(RootFor(target));
			}
			catch (UnauthorizedAccessException)
			{
				// Only reachable for the machine hive, and only if rights were lost between the check and
				// here; the per-user half still completed.
			}
		}

		Console.WriteLine($"Unregistered Keysharp for {(scope == InstallScope.Machine ? "all users and the current user" : "the current user")}.");
		return 0;
	}

	private static bool IsElevated()
	{
		try
		{
			using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	// Machine PATH lives under Session Manager and is spelled PATH; the per-user one is a top-level
	// Environment key and is conventionally spelled Path. Both are written back as REG_EXPAND_SZ so
	// entries such as %SystemRoot% in the existing value keep working.
	private static (RegistryKey Root, string Key, string Name) PathLocationFor(InstallScope scope) =>
		scope == InstallScope.Machine
		? (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "PATH")
		: (Registry.CurrentUser, "Environment", "Path");

	private static void RegisterCompileVerb(string extension, string command, string exe, RegistryKey root)
	{
		using var shell = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{extension}\shell\KeysharpCompile");
		shell.SetValue("", "Compile");
		shell.SetValue("Icon", $"\"{exe}\",0");

		using var commandKey = shell.CreateSubKey("command");
		commandKey.SetValue("", command);
	}

	private static void RegisterEditVerb(string extension, string command, string exe, RegistryKey root)
	{
		using var shell = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{extension}\shell\KeyviewEdit");
		shell.SetValue("", "Edit with Keyview");
		shell.SetValue("Icon", $"\"{exe}\",0");

		using var commandKey = shell.CreateSubKey("command");
		commandKey.SetValue("", command);
	}

	// Registers Keyview as an application Windows offers under "Open with" for script files, WITHOUT making
	// it the default handler: .ks keeps its "Keysharp" ProgID (set above), so double-clicking still runs the
	// script through Keysharp.exe. SupportedTypes scopes Keyview to the recommended list for these extensions,
	// and the per-extension OpenWithList entries make it show deterministically.
	private static void RegisterKeyviewOpenWith(string keyviewExe, RegistryKey root)
	{
		using (var app = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe"))
			app.SetValue("FriendlyAppName", "Keyview");

		using (var icon = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\DefaultIcon"))
			icon.SetValue("", $"\"{keyviewExe}\",0");

		using (var open = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\shell\open\command"))
			open.SetValue("", $"\"{keyviewExe}\" \"%1\"");

		using (var supported = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\SupportedTypes"))
		{
			supported.SetValue(".ahk", "");
			supported.SetValue(".ks", "");
		}

		using (root.CreateSubKey(@"Software\Classes\.ahk\OpenWithList\Keyview.exe"))
		{ }

		using (root.CreateSubKey(@"Software\Classes\.ks\OpenWithList\Keyview.exe"))
		{ }
	}

	private static void RegisterShellIntegration(string path, RegistryKey root)
	{
		var exe = Path.Combine(path, "Keysharp.exe");
		var keyviewExe = Path.Combine(path, "Keyview.exe");
		var command = $"\"{exe}\" \"%1\"";
		var compileCommand = $"\"{exe}\" --compile \"%1\"";
		var editCommand = $"\"{keyviewExe}\" \"%1\"";

		using (var ext = root.CreateSubKey(@"Software\Classes\.ks"))
			ext.SetValue("", "Keysharp");

		// Explorer's New > Keysharp script entry, seeded from the same template the Dash writes and the
		// MSI/MSIX register. Skipped rather than dangling if the file is missing from this layout.
		var templatePath = Path.Combine(path, "Scripts", "Template.ks");

		if (File.Exists(templatePath))
			using (var shellNew = root.CreateSubKey(@"Software\Classes\.ks\ShellNew"))
				shellNew.SetValue("FileName", templatePath);

		using (var type = root.CreateSubKey(@"Software\Classes\Keysharp"))
			type.SetValue("", "Keysharp script");

		using (var icon = root.CreateSubKey(@"Software\Classes\Keysharp\DefaultIcon"))
			icon.SetValue("", $"\"{exe}\",0");

		using (var open = root.CreateSubKey(@"Software\Classes\Keysharp\shell\open\command"))
			open.SetValue("", command);

		using (var ext = root.CreateSubKey(@"Software\Classes\.cks"))
			ext.SetValue("", "Keysharp.CompiledScript");

		using (var type = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript"))
			type.SetValue("", "Compiled Keysharp script");

		using (var icon = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript\DefaultIcon"))
			icon.SetValue("", $"\"{exe}\",0");

		using (var open = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript\shell\open\command"))
			open.SetValue("", command);

		// Older installers registered this iconless verb under the .ks ProgID. Remove it so only the
		// SystemFileAssociations verb below remains.
		root.DeleteSubKeyTree(@"Software\Classes\Keysharp\shell\compile", false);
		RegisterCompileVerb(".ahk", compileCommand, exe, root);
		RegisterCompileVerb(".ks", compileCommand, exe, root);

		if (File.Exists(keyviewExe))
		{
			RegisterEditVerb(".ahk", editCommand, keyviewExe, root);
			RegisterEditVerb(".ks", editCommand, keyviewExe, root);
			RegisterKeyviewOpenWith(keyviewExe, root);
		}
	}

	// The two hives the scope selects between. HKCU\Software\Classes is a genuine per-user class store -
	// the same place the MSI's HKMU-rooted rows land when a package installs per-user - so the key paths
	// below are identical in both cases and only the root differs.
	private static RegistryKey RootFor(InstallScope scope) => scope == InstallScope.Machine ? Registry.LocalMachine : Registry.CurrentUser;

	// Trailing separators are ignored when matching, so an entry written by the MSI - which resolves
	// [INSTALLFOLDER] with a trailing backslash - is still recognised as the same directory here.
	private static bool SamePathEntry(string a, string b) =>
		string.Equals(a?.TrimEnd('\\', '/'), b?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Reads an explicit "user" / "machine" argument, falling back to what this process can actually do.
	/// Defaulting by elevation keeps the old behaviour for an administrator while letting an ordinary
	/// user run the same command instead of failing with an access-denied exception part-way through.
	/// </summary>
	private static bool TryResolveScope(string[] args, out InstallScope scope, out string error)
	{
		error = null;
		var requested = args?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))?.Trim();

		if (string.IsNullOrEmpty(requested))
		{
			scope = IsElevated() ? InstallScope.Machine : InstallScope.User;
			return true;
		}

		if (string.Equals(requested, "machine", StringComparison.OrdinalIgnoreCase))
		{
			scope = InstallScope.Machine;

			if (!IsElevated())
			{
				error = "A machine-wide install needs administrator rights. Run this from an elevated prompt, or omit \"machine\" to install for the current user only.";
				return false;
			}

			return true;
		}

		if (string.Equals(requested, "user", StringComparison.OrdinalIgnoreCase))
		{
			scope = InstallScope.User;
			return true;
		}

		scope = InstallScope.User;
		error = $"Unrecognized scope '{requested}'. Use \"user\" or \"machine\", or pass nothing to choose automatically.";
		return false;
	}

	private static void UnregisterShellIntegration(RegistryKey root)
	{
		root.DeleteSubKeyTree(@"Software\Classes\.cks", false);
		root.DeleteSubKeyTree(@"Software\Classes\.ks", false);
		root.DeleteSubKeyTree(@"Software\Classes\Keysharp", false);
		root.DeleteSubKeyTree(@"Software\Classes\Keysharp.CompiledScript", false);
		root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ahk\shell\KeysharpCompile", false);
		root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ks\shell\KeysharpCompile", false);
		root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ahk\shell\KeyviewEdit", false);
		root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ks\shell\KeyviewEdit", false);
		// Keyview "Open with" registration. The .ks OpenWithList entry is removed with the .ks tree above; the
		// .ahk entry and the shared Applications\Keyview.exe registration must be removed explicitly (.ahk keeps
		// its own default ProgID, so we never delete the whole .ahk key).
		root.DeleteSubKeyTree(@"Software\Classes\Applications\Keyview.exe", false);
		root.DeleteSubKeyTree(@"Software\Classes\.ahk\OpenWithList\Keyview.exe", false);
	}

	/// <summary>
	/// Where a manual install writes. Per-machine needs administrator rights; per-user needs none, which
	/// is what makes the portable zip usable without them - the MSI is per-machine only, because WiX v5
	/// cannot build a package that is both (see docs/design-wix-installer-migration.md, D1).
	/// </summary>
	private enum InstallScope
	{
		Machine,
		User
	}
}

internal sealed class ShutdownWindow : System.Windows.Forms.NativeWindow
{
	private const int WM_CLOSE = 0x0010;
	private const int WM_ENDSESSION = 0x0016;
	private const int WM_QUERYENDSESSION = 0x0011;

	/// <summary>
	/// Roots the shutdown window for the life of the process. NativeWindow destroys its handle from its
	/// finalizer, and the local in <see cref="StartShutdownListener"/> is dead the moment CreateHandle
	/// returns - Application.Run does not reference it - so without this the window is collected and
	/// silently unregistered. The warmup compile allocates enough to make that a certainty rather than
	/// a race, and the symptom is invisible: the handle is logged, the message loop keeps running, and
	/// the window is simply gone from EnumWindows.
	/// </summary>
	/// 
	private static ShutdownWindow shutdownWindow;

	/// <summary>
	/// Lets Windows shut the daemon down cleanly instead of having it killed.
	///
	/// The daemon holds Keysharp.exe and Keysharp.Core.dll open, so an installer replacing or removing
	/// them has to close it first. The Restart Manager, which every modern MSI uses, does that by
	/// *asking* a process to exit: it enumerates the process's top-level windows and sends
	/// WM_QUERYENDSESSION / WM_ENDSESSION. A daemon with no window has nothing to ask, so the Restart
	/// Manager reports it as blocking the operation and then leaves it running - which is exactly how
	/// an uninstall came to fail until the daemon was killed by hand.
	///
	/// Two details matter, and both are easy to get wrong:
	///
	///  * the window must be a genuine TOP-LEVEL window. A message-only window (HWND_MESSAGE parent) is
	///    not returned by EnumWindows and never receives session messages, so it would look right and
	///    silently do nothing. Default CreateParams gives a top-level window; it is never shown, so
	///    without WS_VISIBLE it stays out of the taskbar and Alt-Tab anyway.
	///  * session messages are SENT to windows, not posted to the thread queue, so a bare message loop
	///    on a window-less thread would never see them either.
	///
	/// The window lives on its own thread because Listen() blocks on the pipe from the warm parse-context
	/// STA thread and must keep doing so - compilation depends on running there.
	///
	/// This also covers WM_CLOSE, which is what the installer's util:CloseApplication sends before it
	/// resorts to terminating the process, so an up-to-date daemon now exits on its own during an
	/// upgrade or uninstall and never reaches the force-kill path.
	/// </summary>
	internal static void StartShutdownListener()
	{
		// Nothing downstream reads the window, so there is nothing to wait for: the pump thread is started
		// and the caller goes straight on to warm up. An earlier version blocked here for up to five
		// seconds, which at best only ordered the log lines and at worst spent half the client's patience
		// before the daemon had begun its warmup.
		var thread = new Thread(() =>
		{
			try
			{
				shutdownWindow = new ShutdownWindow();
				shutdownWindow.CreateHandle(new System.Windows.Forms.CreateParams());
				CompileServer.Log($"shutdown window ready (hwnd 0x{shutdownWindow.Handle:X}).");
				System.Windows.Forms.Application.Run(); // Pumps until the process exits.
				CompileServer.Log("shutdown window message loop returned unexpectedly.");
			}
			catch (Exception ex)
			{
				// Best effort: a daemon without the window still works, it just has to be terminated
				// rather than asked to leave, which is what every build before this one did.
				CompileServer.Log($"could not create the shutdown window ({ex.Message}); the daemon will have to be terminated to close it.");
			}
		})
		{ IsBackground = true, Name = "Keysharp compile daemon shutdown listener" };
		thread.SetApartmentState(ApartmentState.STA);

		try
		{
			thread.Start();
		}
		catch (Exception ex)
		{
			CompileServer.Log($"could not start the shutdown listener ({ex.Message}); the daemon will have to be terminated to close it.");
		}
	}

	protected override void WndProc(ref System.Windows.Forms.Message m)
	{
		switch (m.Msg)
		{
			case WM_QUERYENDSESSION:
				// Non-zero means "nothing here needs saving, go ahead". Returning without calling
				// base keeps DefWindowProc from answering for us.
				m.Result = 1;
				return;

			case WM_ENDSESSION:
			case WM_CLOSE:
				CompileServer.Log("shutdown requested; exiting.");
				// The daemon holds nothing that needs unwinding - it is respawned on demand, and a lock
				// file left behind by a hard kill is already recognised as stale. Releasing ownership is
				// therefore a courtesy, and it is given a deliberately tiny slice of the shutdown budget:
				// the default five-second wait is exactly Windows' HungAppTimeout, so a contended mutex
				// here would get the daemon reported as hung and listed on the shutdown-blocking screen
				// as a captionless entry.
				try
				{ DaemonCoordinator.ReleaseOwnership(TimeSpan.FromMilliseconds(250)); }
				catch { }

				Environment.Exit(0);
				return;
		}

		base.WndProc(ref m);
	}
}
#endif