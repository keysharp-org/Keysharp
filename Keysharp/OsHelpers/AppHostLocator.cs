using System.Xml;
using System.Xml.Linq;
using Semver;

namespace Keysharp.Main;

internal static class AppHostLocator
{
	internal static string Find()
	{
		var roots = new List<string>();
		var architecture = RuntimeInformation.ProcessArchitecture;

		roots.Add(Environment.GetEnvironmentVariable($"DOTNET_ROOT_{architecture.ToString().ToUpperInvariant()}"));
#if WINDOWS
		if (architecture == Architecture.X86)
			roots.Add(Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"));
#endif
		roots.Add(Environment.GetEnvironmentVariable("DOTNET_ROOT"));

		// shared/Microsoft.NETCore.App/<version>/ belongs to the runtime that loaded this process.
		roots.Add(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
#if WINDOWS
		var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

		if (architecture == Architecture.X64 && RuntimeInformation.OSArchitecture == Architecture.Arm64)
			roots.Add(Path.Combine(installed, "x64"));

		roots.Add(installed);
#elif OSX
		if (architecture == Architecture.X64 && RuntimeInformation.OSArchitecture == Architecture.Arm64)
			roots.Add("/usr/local/share/dotnet/x64");

		roots.Add("/usr/local/share/dotnet");
		roots.Add("/usr/share/dotnet");
#elif LINUX
		roots.Add("/usr/share/dotnet");
		roots.Add("/usr/lib/dotnet");
		roots.Add("/lib/dotnet");
#endif
		roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"));

		return Find(roots);
	}

	internal static string Find(IEnumerable<string> roots)
	{
		var rid = RuntimeInformation.RuntimeIdentifier;
#if WINDOWS
		const string hostName = "apphost.exe";
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
#else
		const string hostName = "apphost";
		var seen = new HashSet<string>(StringComparer.Ordinal);
#endif

		foreach (var root in roots)
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
				continue;

			var fullRoot = Path.GetFullPath(root);

			if (!seen.Add(fullRoot))
				continue;

			if (NewestHost(Path.Combine(fullRoot, "packs", $"Microsoft.NETCore.App.Host.{rid}"), Path.Combine("runtimes", rid, "native", hostName)) is { } packHost)
				return packHost;

			if (NewestHost(Path.Combine(fullRoot, "sdk"), Path.Combine("AppHostTemplate", hostName), sdk: true) is { } sdkHost)
				return sdkHost;
		}

		return null;
	}

	private static string NewestHost(string directory, string relativePath, bool sdk = false)
	{
		if (!Directory.Exists(directory))
			return null;

		try
		{
			var major = int.Parse(Script.dotNetMajorVersion);
			SemVersion newest = null;
			string result = null;

			foreach (var path in Directory.GetDirectories(directory))
			{
				if (!SemVersion.TryParse(Path.GetFileName(path), SemVersionStyles.Strict, out var version)
					|| version.Major != major || (newest != null && version.ComparePrecedenceTo(newest) <= 0))
					continue;

				var host = Path.Combine(path, relativePath);

				if (!File.Exists(host) || (sdk && !MatchesSdkRuntime(path)))
					continue;

				newest = version;
				result = host;
			}

			return result;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool MatchesSdkRuntime(string directory)
	{
		try
		{
			// SDK templates have no RID in their path. The SDK's portable RID also distinguishes glibc from musl.
			var properties = XDocument.Load(Path.Combine(directory, "Microsoft.NETCoreSdk.BundledVersions.props"));
			var rid = properties.Descendants("NETCoreSdkPortableRuntimeIdentifier").FirstOrDefault()?.Value.Trim();

			if (string.IsNullOrEmpty(rid))
				rid = properties.Descendants("NETCoreSdkRuntimeIdentifier").FirstOrDefault()?.Value.Trim();

			return rid == RuntimeInformation.RuntimeIdentifier;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
		{
			return false;
		}
	}
}
