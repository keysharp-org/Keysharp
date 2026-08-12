using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keysharp.Internals.Os
{
	/// <summary>The exact package graph and assets a compiled script was built against.</summary>
	internal sealed class PackageManifest
	{
		internal const string ResourceName = "Keysharp.Packages.json";
		internal const string AssetResourcePrefix = "Keysharp.Package/";

		[JsonPropertyName("packages")] public List<Entry> Packages { get; set; } = [];

		internal sealed class Asset
		{
			/// <summary>The build machine's exact resolved asset, retained as the source-script fallback.</summary>
			[JsonPropertyName("source")] public string Source { get; set; }

			/// <summary>A package/version-scoped path used both beside full artifacts and inside minimal ones.</summary>
			[JsonPropertyName("deployed")] public string Deployed { get; set; }
		}

		internal sealed class Entry
		{
			[JsonPropertyName("provider")] public string Provider { get; set; } = "nuget";
			[JsonPropertyName("id")] public string Id { get; set; }
			[JsonPropertyName("requested")] public string Requested { get; set; }
			[JsonPropertyName("resolved")] public string Resolved { get; set; }
			[JsonPropertyName("pinned")] public string Pinned { get; set; }
			[JsonPropertyName("optional")] public bool Optional { get; set; }
			[JsonPropertyName("direct")] public bool Direct { get; set; }
			[JsonIgnore] public List<Asset> Compile { get; set; } = [];
			[JsonPropertyName("managed")] public List<Asset> Managed { get; set; } = [];
			[JsonPropertyName("resources")] public List<Asset> Resources { get; set; } = [];
			[JsonPropertyName("native")] public List<Asset> Native { get; set; } = [];
		}

		internal IEnumerable<Asset> Assets => Packages.SelectMany(p => p.Managed.Concat(p.Resources).Concat(p.Native)).DistinctBy(a => a.Deployed);

		internal List<PackageResolver.PackageRef> Direct =>
			[.. Packages.Where(p => p.Direct).Select(p => new PackageResolver.PackageRef(p.Id,
				p.Pinned ?? (p.Provider.Equals("nuget", StringComparison.OrdinalIgnoreCase) ? $"[{p.Resolved}]" : p.Requested),
				p.Optional, p.Provider))];

		internal void Add(PackageResolver.ResolvedPackage package, string requested, bool optional, bool direct)
		{
			Packages.Add(new Entry
			{
				Provider = package.Provider,
				Id = package.Id,
				Requested = requested,
				Resolved = package.Version,
				Pinned = package.PinnedVersion,
				Optional = optional,
				Direct = direct,
				Compile = MakeAssets(package, package.Compile, "compile"),
				Managed = MakeAssets(package, package.Managed, "managed"),
				Resources = MakeAssets(package, package.Resources, "resources"),
				Native = MakeAssets(package, package.Native, "native")
			});
		}

		private static List<Asset> MakeAssets(PackageResolver.ResolvedPackage package, IEnumerable<string> sources, string kind)
		{
			var result = new List<Asset>();

			foreach (var source in sources)
			{
				var relative = RelativeAssetPath(package, source);
				result.Add(new Asset
				{
					Source = source,
					Deployed = Path.Combine(".keysharp", "packages", package.Provider.ToLowerInvariant(),
						IdentitySegment(package.Id), IdentitySegment(package.Version), kind, relative)
				});
			}

			return result;
		}

		private static string RelativeAssetPath(PackageResolver.ResolvedPackage package, string source)
		{
			if (!string.IsNullOrEmpty(package.Root))
			{
				var relative = Path.GetRelativePath(package.Root, source);

				if (!Path.IsPathRooted(relative) && relative != ".."
						&& !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
					return relative;
			}

			// Only synthetic/custom resolvers lack a package root. Keep even those collision-free without reading
			// the asset contents: the source path is already the resolver's unique identity for this build.
			var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source)))).ToLowerInvariant();
			return Path.Combine("_external", key, Path.GetFileName(source));
		}

		private static string IdentitySegment(string value)
		{
			value ??= "";
			var readable = new string(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_')
				.Take(32).ToArray()).Trim('.', '_');
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32]
				.ToLowerInvariant();
			return $"{(readable.Length == 0 ? "package" : readable)}-{hash}";
		}

		internal static string AssetResourceName(Asset asset) =>
			AssetResourcePrefix + asset.Deployed.Replace('\\', '/');

		internal static PackageManifest Read(string json)
		{
			try { return JsonSerializer.Deserialize<PackageManifest>(json); }
			catch (Exception) { return null; }
		}

		internal string Write() => JsonSerializer.Serialize(this);

		internal static PackageManifest FromAssembly(Assembly asm)
		{
			if (asm == null)
				return null;

			try
			{
				using var stream = asm.GetManifestResourceStream(ResourceName);

				if (stream == null)
					return null;

				using var reader = new StreamReader(stream);
				return Read(reader.ReadToEnd());
			}
			catch (Exception) { return null; }
		}

		/// <summary>Copies package assets without flattening their names or touching host dependencies.</summary>
		internal string CopyTo(string destination)
		{
			if (string.IsNullOrEmpty(destination))
				return null;

			try
			{
				foreach (var asset in Assets)
				{
					if (!File.Exists(asset.Source))
						return $"Package asset was not found: {asset.Source}";

					var target = SafePath(destination, asset.Deployed);
					_ = Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(asset.Source, target, true);
				}
			}
			catch (Exception e)
			{
				return $"Copying package assemblies to {destination} failed: {e.Message}";
			}

			return null;
		}

		/// <summary>Locates deployed/embedded assets first and build-cache assets only as a development fallback.</summary>
		internal bool TryLocate(Assembly scriptAssembly, out List<PackageResolver.ResolvedPackage> resolved, out string missing)
		{
			resolved = [];
			missing = null;
			var besides = new[]
			{
				Path.GetDirectoryName(scriptAssembly?.Location ?? ""),
				Keysharp.Builtins.Accessors.A_ScriptDir as string,
				Path.GetDirectoryName(Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location ?? "")
			}.Where(p => !string.IsNullOrEmpty(p)).Distinct(PathComparer).ToArray();

			foreach (var entry in Packages)
			{
				var package = new PackageResolver.ResolvedPackage { Provider = entry.Provider, Id = entry.Id, Version = entry.Resolved };
				var ok = LocateAll(scriptAssembly, besides, entry.Managed, package.Managed)
						 && LocateAll(scriptAssembly, besides, entry.Resources, package.Resources)
						 && LocateAll(scriptAssembly, besides, entry.Native, package.Native);

				if (ok)
				{
					resolved.Add(package);
					continue;
				}

				if (entry.Optional)
					continue;

				missing = $"package '{entry.Id} {entry.Resolved}' is incomplete. Rebuild the artifact, or restore it by running the source script.";
				return false;
			}

			return true;
		}

		private static bool LocateAll(Assembly scriptAssembly, string[] besides, IEnumerable<Asset> assets, List<string> into)
		{
			foreach (var asset in assets)
			{
				if (TryExtract(scriptAssembly, asset, out var path)
						|| TryFindDeployed(besides, asset, out path)
						|| File.Exists(asset.Source) && Set(asset.Source, out path))
					into.Add(path);
				else
					return false;
			}

			return true;
		}

		private static bool TryFindDeployed(IEnumerable<string> roots, Asset asset, out string path)
		{
			foreach (var root in roots)
			{
				try
				{
					var candidate = SafePath(root, asset.Deployed);

					if (File.Exists(candidate))
						return Set(candidate, out path);
				}
				catch { }
			}

			path = null;
			return false;
		}

		private static bool TryExtract(Assembly assembly, Asset asset, out string path)
		{
			path = null;

			if (assembly == null)
				return false;

			try
			{
				using var source = assembly.GetManifestResourceStream(AssetResourceName(asset));

				if (source == null)
					return false;

				// Per-user, NOT Path.GetTempPath(): the fast path below trusts whatever already sits at the target,
				// and /tmp is world-writable -- any local user could pre-create this path (the MVID is readable from
				// the artifact) and have their DLL loaded instead of the embedded one. A profile-less account gets no
				// extraction rather than a shared directory; the deployed/source lookups after this still apply.
				var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

				if (string.IsNullOrEmpty(userRoot))
					return false;

				var root = Path.Combine(userRoot, "Keysharp", "embedded-packages",
									assembly.ManifestModule.ModuleVersionId.ToString("N"));
				path = SafePath(root, asset.Deployed);

				if (File.Exists(path))
					return true;

				_ = Directory.CreateDirectory(Path.GetDirectoryName(path));
				var temporary = path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";

				try
				{
					using (var target = File.Create(temporary))
						source.CopyTo(target);

					try { File.Move(temporary, path, true); }
					catch (IOException) when (File.Exists(path)) { }   // another process extracted this asset first
				}
				finally
				{
					try { File.Delete(temporary); } catch { }
				}

				return true;
			}
			catch
			{
				path = null;
				return false;
			}
		}

		private static string SafePath(string root, string relative)
		{
			var fullRoot = Path.GetFullPath(root);
			var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
			var prefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;

			if (!full.StartsWith(prefix, PathComparison))
				throw new InvalidDataException("A package manifest contains an invalid deployment path.");

			return full;
		}

		private static bool Set(string value, out string result)
		{
			result = value;
			return true;
		}

#if WINDOWS
		private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
		private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
#else
		private const StringComparison PathComparison = StringComparison.Ordinal;
		private static readonly StringComparer PathComparer = StringComparer.Ordinal;
#endif
	}
}
