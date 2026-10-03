using Keysharp.Builtins;
using KsDebug = Keysharp.Builtins.Debug;

namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Resolves package requests through locally installed providers. This class owns batching; providers own
	/// package-system policy, isolation, configuration, network access and their persistent cache format.
	/// It never loads a package assembly and never invokes an MSBuild, SDK or package-manager executable.
	/// </summary>
	internal static class PackageResolver
	{
		private const int RestoreTimeoutMs = 180_000;
		internal static int RestoreCount;
		internal static int ResolveCount;

		internal static void ResetCounters()
		{
			RestoreCount = ResolveCount = 0;
		}

		internal static void ReportResolved(List<PackageRef> wanted, List<ResolvedPackage> resolved, string label)
		{
			foreach (var request in wanted.Where(w => w.Version.Contains('*')))
				if (resolved.FirstOrDefault(r => r.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase)
					&& r.Id.Equals(request.Id, StringComparison.OrdinalIgnoreCase)) is { } hit)
					_ = KsDebug.OutputDebug($"{label}: {hit.Id} {request.Version} resolved to {hit.Version}");
		}

		internal static bool TryResolve(List<PackageRef> packages, bool allowRestore, string label,
			out List<ResolvedPackage> resolved, out string failure, string settingsDirectory = null)
		{
			failure = null;
			resolved = null;
			_ = Interlocked.Increment(ref ResolveCount);
			settingsDirectory ??= Keysharp.Builtins.Accessors.A_ScriptDir as string ?? Environment.CurrentDirectory;
			settingsDirectory = Path.GetFullPath(settingsDirectory);
			return ResolveUncached(packages, allowRestore, label, settingsDirectory, out resolved, out failure);
		}

		private static bool ResolveUncached(List<PackageRef> packages, bool allowRestore, string label, string settingsDirectory,
			out List<ResolvedPackage> resolved, out string failure)
		{
			failure = null;
			resolved = [];

			foreach (var providerGroup in packages.GroupBy(p => p.Provider, StringComparer.OrdinalIgnoreCase))
			{
				var providerName = providerGroup.Key;

				if (!PackageProviderRegistry.TryGet(providerName, out var provider, out var providerFailure))
				{
					failure = $"{label}: {providerFailure}";
					return false;
				}

				var group = providerGroup.ToList();
				var invalid = group.FirstOrDefault(p => !provider.IsValidPackageId(p.Id));

				if (!invalid.Equals(default(PackageRef)))
				{
					failure = $"{label}: '{invalid.Id}' is not a valid package name for provider '{providerName}'";
					return false;
				}

				var providerKey = CacheKeyFor(group, settingsDirectory);
				var directory = Path.Combine(CacheRoot(), providerName.ToLowerInvariant(), providerKey);
				var context = new Keysharp.Components.Packages.PackageResolveContext(directory, settingsDirectory, tfm, rid, allowRestore,
					TimeSpan.FromMilliseconds(RestoreTimeoutMs), label);
				var requests = group.Select(p => new Keysharp.Components.Packages.PackageRequest(p.Id, p.Version)).ToArray();
				Keysharp.Components.Packages.PackageResolveResult result;

				try
				{
					using var timeout = new CancellationTokenSource(RestoreTimeoutMs);
					// AHK exposes package loading synchronously, often from the WinForms UI thread. NuGet's restore
					// pipeline contains awaits which may capture the current SynchronizationContext, so blocking that
					// same context here would deadlock. Providers execute on a context-free worker boundary instead.
					result = Task.Run(() => provider.ResolveAsync(context, requests, timeout.Token), timeout.Token)
						.GetAwaiter().GetResult();
				}
				catch (OperationCanceledException)
				{
					failure = $"{label}: {providerName} restore did not finish within {RestoreTimeoutMs / 1000} seconds";
					return false;
				}
				catch (Exception e)
				{
					failure = $"{label}: {providerName} provider failed: {e.GetBaseException().Message}";
					return false;
				}

				if (result?.RestoreAttempted == true)
					_ = Interlocked.Increment(ref RestoreCount);

				if (result == null || !result.Success)
				{
					failure = result?.Failure ?? $"{label}: {providerName} provider returned no result";
					return false;
				}

				foreach (var diagnostic in result.Diagnostics)
					_ = KsDebug.OutputDebug($"{label}: {diagnostic}");

				foreach (var package in result.Packages)
				{
					if (string.IsNullOrWhiteSpace(package.PinnedVersion))
					{
						failure = $"{label}: {providerName} provider did not supply an exact constraint for '{package.Id} {package.Version}'";
						return false;
					}

					var adapted = new ResolvedPackage
					{
						Provider = providerName,
						Id = package.Id,
						Version = package.Version,
						PinnedVersion = package.PinnedVersion,
						Root = package.Root
					};
					adapted.Compile.AddRange(package.Compile);
					adapted.Managed.AddRange(package.Runtime);
					adapted.Resources.AddRange(package.Resources);
					adapted.Native.AddRange(package.Native);
					resolved.Add(adapted);
				}
			}

			return resolved.Count != 0 && ValidateAssetNames(resolved, label, out failure);
		}

		private static bool ValidateAssetNames(List<ResolvedPackage> packages, string label, out string failure)
		{
			failure = null;
			var managed = new Dictionary<string, (string Path, ResolvedPackage Package)>(StringComparer.OrdinalIgnoreCase);
			var native = new Dictionary<string, (string Path, ResolvedPackage Package)>(StringComparer.OrdinalIgnoreCase);

			foreach (var package in packages)
			{
				foreach (var path in package.Managed)
				{
					var name = NuGetPackageLoader.ManagedKeyFor(path);
					package.ManagedKeys[path] = name;

					if (!Add(managed, name, path, package, "managed assembly", out failure))
						return false;
				}

				foreach (var path in package.Resources)
				{
					var name = NuGetPackageLoader.ManagedKeyFor(path);
					package.ManagedKeys[path] = name;
					if (!Add(managed, name, path, package, "resource assembly", out failure))
						return false;
				}

				foreach (var path in package.Native)
					foreach (var name in NuGetPackageLoader.NativeAliasesFor(path))
						if (!Add(native, name, path, package, "native library", out failure))
							return false;
			}

			return true;

			bool Add(Dictionary<string, (string Path, ResolvedPackage Package)> names, string name, string path,
				ResolvedPackage package, string kind, out string error)
			{
				error = null;

				if (!names.TryGetValue(name, out var existing))
				{
					names[name] = (path, package);
					return true;
				}

				if (Path.GetFullPath(existing.Path).Equals(Path.GetFullPath(path), PathComparison))
					return true;

				error = $"{label}: {kind} name '{name.Replace('\0', '/')}' is supplied by both "
					+ $"'{existing.Package.Provider}:{existing.Package.Id}' and '{package.Provider}:{package.Id}'";
				return false;
			}
		}

		internal static bool TryNormalizeVersion(string providerName, string written, out string normalized, out string error)
		{
			if (!PackageProviderRegistry.TryGet(providerName, out var provider, out error))
			{
				normalized = null;
				return false;
			}

			return provider.TryNormalizeVersion(written, out normalized, out error);
		}

		internal readonly record struct PackageRef(string Id, string Version, bool Optional, string Provider = "nuget");

		internal static string CacheKeyFor(List<PackageRef> packages, string settingsDirectory = null)
		{
			var identity = string.Join(";", packages
				.Select(p => $"{PackageProviderRegistry.Identity(p.Provider)}|{p.Id.ToLowerInvariant()}|{p.Version.ToLowerInvariant()}")
				.OrderBy(s => s, StringComparer.Ordinal));
			settingsDirectory = string.IsNullOrWhiteSpace(settingsDirectory) ? "" : Path.GetFullPath(settingsDirectory);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tfm}\n{rid}\n{settingsDirectory}\n{identity}")))[..16].ToLowerInvariant();
		}

		private static string CacheRoot()
		{
#if WINDOWS
			var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#else
			var root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (string.IsNullOrEmpty(root))
				root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
#endif
			return Path.Combine(root, "Keysharp", "packages");
		}

		internal static string TargetFramework => tfm;
		internal static string RuntimeId => rid;

		private static readonly string tfm =
#if WINDOWS
			$"net{Environment.Version.Major}.{Environment.Version.Minor}-windows7.0";
#else
			$"net{Environment.Version.Major}.{Environment.Version.Minor}";
#endif
		private static readonly string rid = RuntimeInformation.RuntimeIdentifier;
		private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		internal sealed class ResolvedPackage
		{
			internal string Provider = "nuget";
			internal string Id;
			internal string Version;
			internal string PinnedVersion;
			internal string Root;
			internal readonly List<string> Compile = [];
			internal readonly List<string> Managed = [];
			internal readonly List<string> Resources = [];
			internal readonly List<string> Native = [];
			internal readonly Dictionary<string, string> ManagedKeys = new();
		}

	}
}
