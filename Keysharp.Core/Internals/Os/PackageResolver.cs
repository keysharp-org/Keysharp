using Keysharp.Builtins;
using KsDebug = Keysharp.Builtins.Debug;

namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Resolves package requests through locally installed providers. This class owns batching and per-request-set
	/// isolation; providers own package-system policy, configuration, network access and their persistent cache format.
	/// It never loads a package assembly and never invokes an MSBuild, SDK or package-manager executable.
	/// </summary>
	internal static class PackageResolver
	{
		private const int RestoreTimeoutMs = 180_000;
		internal static int RestoreCount;
		internal static int ResolveCount;

		private static readonly object[] resolutionLocks = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();

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
			var key = CacheKeyFor(packages, settingsDirectory);

			var stripe = resolutionLocks[(uint)StringComparer.Ordinal.GetHashCode(key) % resolutionLocks.Length];

			lock (stripe)
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

					if (!Add(managed, name, path, package, "managed assembly", out failure))
						return false;
				}

				foreach (var path in package.Resources)
					if (!Add(managed, NuGetPackageLoader.ManagedKeyFor(path), path, package, "resource assembly", out failure))
						return false;

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

		internal static bool IsValidId(string value) =>
			value?.Length is > 0 and < 128
			&& value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

		internal static bool TryValidateId(string providerName, string value, out string error)
		{
			error = null;

			if (!PackageProviderRegistry.TryGet(providerName, out var provider, out error))
				return false;

			if (provider.IsValidPackageId(value))
				return true;

			error = $"'{value}' is not a valid package name for provider '{providerName}'";
			return false;
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

		internal static bool IsValidVersion(string value) =>
			value?.Length < 64
			&& value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '*' or '[' or ']' or '(' or ')' or ',');

		private static bool IsFloatingVersion(string value) =>
			value == "*" || value.EndsWith(".*", StringComparison.Ordinal) && IsPlainVersion(value[..^2]);

		private static bool IsValidRange(string value)
		{
			if (!IsValidVersion(value) || value.Length < 3 || value[0] is not ('[' or '(') || value[^1] is not (']' or ')'))
				return false;

			var parts = value[1..^1].Split(',');

			if (parts.Length > 2 || parts.All(p => p.Length == 0))
				return false;

			if (parts.Length == 1 && (value[0] != '[' || value[^1] != ']'))
				return false;

			return parts.All(p => p.Length == 0 || IsPlainVersion(p));
		}

		/// <summary>Maps AHK/#Requires-style versions to NuGet ranges, including optional leading <c>v</c>.</summary>
		internal static bool TryTranslateVersion(string written, out string range, out string error)
		{
			range = Translate((written ?? "").Trim());
			error = range == null ? $"'{written}' is not a valid version" : null;
			return range != null;

			static string Translate(string value)
			{
				if (value.Length == 0)
					return "*";

				if (value[0] is '[' or '(')
					return IsValidRange(value) ? value : null;

				if (value.Contains('*'))
					return IsValidVersion(value) && IsFloatingVersion(value) ? value : null;

				var tokens = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

				if (tokens.Length == 1 && Operator(tokens[0]).Length == 0)
				{
					var only = StripV(tokens[0]);
					return !IsPlainVersion(only) ? null : only.Split('.').Length >= 3 ? $"[{only}]" : $"{only}.*";
				}

				string lower = null, upper = null, exact = null;
				bool lowerInclusive = false, upperInclusive = false;

				foreach (var raw in tokens)
				{
					var op = Operator(raw);
					var token = op.Length == 0 ? null : StripV(raw[op.Length..]);

					if (token == null || !IsPlainVersion(token))
						return null;

					switch (op)
					{
						case ">=": lower = token; lowerInclusive = true; break;
						case ">": lower = token; break;
						case "<=": upper = token; upperInclusive = true; break;
						case "<": upper = token; break;
						default: exact = token; break;
					}
				}

				if (exact != null)
					return lower == null && upper == null ? $"[{exact}]" : null;

				return $"{(lowerInclusive ? '[' : '(')}{lower},{upper}{(upperInclusive ? ']' : ')')}";
			}

			static string Operator(string token)
			{
				foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
					if (token.StartsWith(candidate, StringComparison.Ordinal))
						return candidate;

				return "";
			}

			static string StripV(string token) =>
				token.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? token[1..] : token;
		}

		private static bool IsPlainVersion(string value)
		{
			if (string.IsNullOrEmpty(value))
				return false;

			var plus = value.IndexOf('+');

			if (plus >= 0 && (!Identifiers(value[(plus + 1)..]) || value.IndexOf('+', plus + 1) >= 0))
				return false;

			var withoutMetadata = plus >= 0 ? value[..plus] : value;
			var dash = withoutMetadata.IndexOf('-');

			if (dash >= 0 && !Identifiers(withoutMetadata[(dash + 1)..]))
				return false;

			var core = dash >= 0 ? withoutMetadata[..dash] : withoutMetadata;
			var parts = core.Split('.');
			return parts.Length is >= 1 and <= 4 && parts.All(p => p.Length != 0 && p.All(char.IsAsciiDigit));

			static bool Identifiers(string candidate) => candidate.Length != 0
				&& candidate.Split('.').All(p => p.Length != 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
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
		}

		// Retained as a tolerant reader for existing caches and package-manifest tests.
		internal static bool RestoreSucceeded(string directory)
		{
			try
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "obj", "project.nuget.cache")));
				return doc.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
			}
			catch { return false; }
		}

		internal static List<ResolvedPackage> TryReadAssets(string assetsPath)
		{
			if (!File.Exists(assetsPath))
				return null;

			try
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
				var root = doc.RootElement;

				if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("libraries", out var libraries))
					return null;

				var folders = root.TryGetProperty("packageFolders", out var packageFolders)
					? packageFolders.EnumerateObject().Select(p => p.Name).ToList() : [];

				if (folders.Count == 0 || !TrySelectTarget(targets, out var target))
					return null;

				var result = new List<ResolvedPackage>();

				foreach (var entry in target.EnumerateObject())
				{
					var slash = entry.Name.LastIndexOf('/');

					if (slash <= 0 || !libraries.TryGetProperty(entry.Name, out var library)
						|| !library.TryGetProperty("type", out var type) || type.GetString() != "package"
						|| !library.TryGetProperty("path", out var relativeElement) || relativeElement.GetString() is not { } relative)
						continue;

					var baseDirectory = folders.Select(f => Path.Combine(f, relative.Replace('/', Path.DirectorySeparatorChar)))
						.FirstOrDefault(Directory.Exists);

					if (baseDirectory == null)
						return null;

					var package = new ResolvedPackage
					{
						Id = entry.Name[..slash], Version = entry.Name[(slash + 1)..], Root = baseDirectory
					};

					if (!CollectAssets(entry.Value, "runtime", baseDirectory, package.Managed)
						|| !CollectAssets(entry.Value, "native", baseDirectory, package.Native))
						return null;

					result.Add(package);
				}

				return result.Count == 0 ? null : result;
			}
			catch { return null; }
		}

		private static bool TrySelectTarget(JsonElement targets, out JsonElement target)
		{
			target = default;
			var found = false;

			foreach (var candidate in targets.EnumerateObject())
			{
				if (!candidate.Name.StartsWith(tfm, StringComparison.OrdinalIgnoreCase))
					continue;

				if (candidate.Name.EndsWith("/" + rid, StringComparison.OrdinalIgnoreCase))
				{
					target = candidate.Value;
					return true;
				}

				if (!found)
				{
					target = candidate.Value;
					found = true;
				}
			}

			return found;
		}

		private static bool CollectAssets(JsonElement package, string section, string baseDirectory, List<string> destination)
		{
			if (!package.TryGetProperty(section, out var assets))
				return true;

			foreach (var asset in assets.EnumerateObject())
			{
				if (asset.Name.EndsWith("_._", StringComparison.Ordinal))
					continue;

				var fullPath = Path.Combine(baseDirectory, asset.Name.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(fullPath))
					return false;

				destination.Add(fullPath);
			}

			return true;
		}
	}
}
