#if !WINDOWS
namespace Keysharp.Main;

internal class UnixHelper
{
	// Locates the newest matching-major apphost template, trying every plausible dotnet install root:
	// the running runtime's own root covers however this process was launched, DOTNET_ROOT covers CI
	// (GitHub runners use /usr/share/dotnet on Linux and ~/.dotnet on macOS), and the rest are the
	// distro/pkg defaults (apt's /usr/lib/dotnet is also visible as /lib/dotnet).
	internal static string FindAppHostTemplate()
	{
		var roots = new List<string>();

		void AddRoot(string root)
		{
			if (!root.IsNullOrEmpty() && Directory.Exists(root) && !roots.Contains(root))
				roots.Add(Path.GetFullPath(root));
		}

		try
		{
			// shared/Microsoft.NETCore.App/<ver>/ -> three levels up is the root.
			AddRoot(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
		}
		catch { }

		AddRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
#if LINUX
		AddRoot("/usr/share/dotnet");
		AddRoot("/usr/lib/dotnet");
		AddRoot("/lib/dotnet");
#else
		AddRoot("/usr/local/share/dotnet");
		AddRoot("/usr/share/dotnet");
		AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"));
#endif
		var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
		var rid = $"{(OperatingSystem.IsMacOS() ? "osx" : "linux")}-{arch}";

		foreach (var root in roots)
		{
			// A full SDK carries the template directly; a runtime with the host pack still has one under packs.
			if (NewestHost(Path.Combine(root, "sdk"), Path.Combine("AppHostTemplate", "apphost")) is { } sdkHost)
				return sdkHost;

			if (NewestHost(Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{rid}"), Path.Combine("runtimes", rid, "native", "apphost")) is { } packHost)
				return packHost;
		}

		return null;

		static string NewestHost(string versionsDir, string hostRelativePath)
		{
			return !Directory.Exists(versionsDir)
				? null
				: Directory.GetDirectories(versionsDir)
				.Where(dir => Path.GetFileName(dir).StartsWith(Script.dotNetMajorVersion))
				.OrderByDescending(dir => ParseVersion(Path.GetFileName(dir)))
				.Select(dir => Path.Combine(dir, hostRelativePath))
				.FirstOrDefault(File.Exists);
		}

		static Version ParseVersion(string name)
		{
			var dash = name.IndexOf('-');   // 10.0.100-rc.1.25451.107 -> 10.0.100

			if (dash > 0)
				name = name[..dash];

			return Version.TryParse(name, out var version) ? version : new Version(0, 0);
		}
	}
}
#endif