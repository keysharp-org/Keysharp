#if OSX
using MonoMac.AppKit;

namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// The application an event is addressed to. Addressing by bundle identifier is preferred over a process id
	/// because it survives the application being quit and relaunched, which a long lived script object otherwise
	/// would not.
	/// </summary>
	internal sealed class AETarget
	{
		internal string BundleId;
		internal int Pid;
		internal string DisplayName;

		/// <summary>Identifies the application for the terminology cache.</summary>
		internal string CacheKey => BundleId ?? $"pid:{Pid}";

		/// <summary>
		/// A process id is only set when the script named one, and it then wins over the bundle identifier: asking
		/// for a particular process means that instance, not whichever one the identifier currently resolves to.
		/// The identifier is still resolved for a pid target, because terminology is per application.
		/// </summary>
		internal AEValue MakeAddress()
			=> Pid != 0
			   ? AE.FromBytes(AE.TypeKernelProcessID, BitConverter.GetBytes(Pid))
			   : AE.FromBytes(AE.TypeApplicationBundleID, Encoding.UTF8.GetBytes(BundleId));

		public override string ToString() => $"application \"{DisplayName ?? BundleId ?? Pid.ToString(CultureInfo.InvariantCulture)}\"";
	}

	/// <summary>
	/// Resolves what a script wrote into an addressable application, and launches one on demand. Two lookups here
	/// go through osascript rather than a framework call: mapping an application name to its bundle identifier and
	/// a bundle identifier to its path are both one-line AppleScript idioms, they run once per application and are
	/// cached, and the alternative is a deprecated LaunchServices surface.
	/// </summary>
	internal static class AETargets
	{
		private static readonly ConcurrentDictionary<string, string> bundleIdByName = new (StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, string> pathByBundleId = new (StringComparer.OrdinalIgnoreCase);

		/// <summary>How long to wait for an application asked to launch to become addressable.</summary>
		private const int LaunchTimeoutMs = 20_000;

		internal static AETarget Resolve(string target)
		{
			target = (target ?? "").Trim();

			if (target.Length == 0)
				throw new ArgumentException("ComObject needs an application to address: a bundle id, a name, a path, or \"pid:1234\".");

			if (target.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
			{
				if (!int.TryParse(target["pid:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
					throw new ArgumentException($"'{target}' does not name a process id.");

				return new AETarget { Pid = pid, BundleId = BundleIdOfPid(pid), DisplayName = target };
			}

			if (target.Contains('/', StringComparison.Ordinal))
			{
				var bundleId = BundleIdOfPath(target)
							   ?? throw new ArgumentException($"'{target}' is not an application bundle.");
				_ = pathByBundleId.TryAdd(bundleId, target);
				return new AETarget { BundleId = bundleId, DisplayName = Path.GetFileNameWithoutExtension(target) };
			}

			// A dot means it is already a bundle identifier; anything else is a name to look one up for.
			if (target.Contains('.', StringComparison.Ordinal))
				return new AETarget { BundleId = target, DisplayName = target };

			var resolved = Remember(bundleIdByName, target, LookUpBundleIdByName);

			if (string.IsNullOrEmpty(resolved))
				throw new ArgumentException($"No application named '{target}' could be found.");

			return new AETarget { BundleId = resolved, DisplayName = target };
		}

		/// <summary>The process id currently serving this target, or zero when it is not running.</summary>
		internal static int RunningPid(AETarget target)
		{
			// A named process is that process: a sibling instance of the same application being alive says nothing
			// about whether this one still is.
			if (target.Pid != 0)
				return IsPidAlive(target.Pid) ? target.Pid : 0;

			try
			{
				foreach (var app in NSWorkspace.SharedWorkspace.RunningApplications)
				{
					if (app == null)
						continue;

					if (string.Equals(app.BundleIdentifier, target.BundleId, StringComparison.OrdinalIgnoreCase))
						return app.ProcessIdentifier;
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Could not enumerate running applications: {ex.Message}");
			}

			return 0;
		}

		internal static bool IsRunning(AETarget target) => RunningPid(target) != 0;

		/// <summary>
		/// Starts the application if it is not already running, without bringing it to the front. Sending an event
		/// never launches anything by itself, so this is the step that stands in for activation on other platforms.
		/// </summary>
		internal static void Launch(AETarget target)
		{
			if (IsRunning(target))
				return;

			var args = target.BundleId != null
					   ? new[] { "-g", "-b", target.BundleId }
					   : throw new AEException(AE.ErrAEProcNotFound,
											   $"Process {target.Pid} is not running, and a process id cannot be launched.");

			try
			{
				using var process = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "/usr/bin/open",
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};

				foreach (var arg in args)
					process.StartInfo.ArgumentList.Add(arg);

				_ = process.Start();
				var stderr = process.StandardError.ReadToEndAsync();

				// ExitCode throws while the process is still running, so the timeout has to be handled first.
				if (!process.WaitForExit(LaunchTimeoutMs))
				{
					try { process.Kill(entireProcessTree: true); } catch { }

					throw new AEException(AE.ErrAEProcNotFound, $"Launching '{target.BundleId}' did not finish in time.");
				}

				if (process.ExitCode != 0)
				{
					var error = stderr.GetAwaiter().GetResult().Trim();
					throw new AEException(AE.ErrAEProcNotFound,
										  $"Could not launch '{target.BundleId}'{(error.Length > 0 ? ": " + error : ".")}");
				}
			}
			catch (AEException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new AEException(AE.ErrAEProcNotFound, $"Could not launch '{target.BundleId}': {ex.Message}");
			}

			WaitUntilRunning(target);
		}

		/// <summary>
		/// Waits for a launching application to register itself. The wait pumps the message loop so timers, hotkeys
		/// and the GUI keep running while an application starts up.
		/// </summary>
		private static void WaitUntilRunning(AETarget target)
		{
			var task = Task.Run(() =>
			{
				var deadline = Environment.TickCount64 + LaunchTimeoutMs;

				while (Environment.TickCount64 < deadline)
				{
					if (IsRunning(target))
						return;

					Thread.Sleep(50);
				}
			});

			if (!task.WaitInterruptible(LaunchTimeoutMs) || !IsRunning(target))
				throw new AEException(AE.ErrAEProcNotFound, $"'{target.BundleId}' did not finish launching in time.");
		}

		/// <summary>The application bundle's path, which is where its scripting definition is read from.</summary>
		internal static string PathOf(AETarget target)
		{
			if (target.BundleId == null)
				return null;

			var path = Remember(pathByBundleId, target.BundleId, LookUpPathByBundleId);
			return string.IsNullOrEmpty(path) ? null : path;
		}

		/// <summary>
		/// Caches a lookup, but only once it has succeeded. A failed lookup is usually transient — the application
		/// is still starting, or osascript was interrupted — and remembering the empty answer would keep the
		/// application unreachable for the rest of the run.
		/// </summary>
		private static string Remember(ConcurrentDictionary<string, string> cache, string key, Func<string, string> lookUp)
		{
			if (cache.TryGetValue(key, out var cached))
				return cached;

			var resolved = lookUp(key);

			if (!string.IsNullOrEmpty(resolved))
				cache[key] = resolved;

			return resolved;
		}

		private static string LookUpBundleIdByName(string name)
		{
			// Quoting matters: a name with a double quote in it would otherwise change the meaning of the script.
			var escaped = name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
			return RunAppleScript($"id of application \"{escaped}\"");
		}

		private static string LookUpPathByBundleId(string bundleId)
		{
			var escaped = bundleId.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
			return RunAppleScript($"POSIX path of (path to application id \"{escaped}\")");
		}

		private static string RunAppleScript(string script)
		{
			try
			{
				return script.AppleScript(out var output, wait: true) == 0 ? output.Trim() : "";
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Application lookup failed: {ex.Message}");
				return "";
			}
		}

		private static string BundleIdOfPath(string path)
		{
			try
			{
				var bundle = MonoMac.Foundation.NSBundle.FromPath(path);
				var id = bundle?.BundleIdentifier;
				return string.IsNullOrEmpty(id) ? null : id;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Could not read the bundle at '{path}': {ex.Message}");
				return null;
			}
		}

		private static string BundleIdOfPid(int pid)
		{
			try
			{
				var app = NSRunningApplication.GetRunningApplication(pid);
				var id = app?.BundleIdentifier;
				return string.IsNullOrEmpty(id) ? null : id;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Could not identify process {pid}: {ex.Message}");
				return null;
			}
		}

		private static bool IsPidAlive(int pid)
		{
			try
			{
				return NSRunningApplication.GetRunningApplication(pid) != null;
			}
			catch
			{
				return false;
			}
		}
	}
}
#endif
