using System.Text.Json.Serialization;

namespace Keysharp.Internals.Os
{
	/// <summary>Provider payloads carried only by compiled scripts which call <c>Clr.LoadPackage</c>.</summary>
	internal sealed class CompiledPackageProviderManifest
	{
		internal const string ResourceName = "Keysharp.Components.Packages.Providers.json";
		internal const string AssetResourcePrefix = "Keysharp.Components.Packages.Provider/";

		[JsonPropertyName("providers")] public List<Entry> Providers { get; set; } = [];

		internal sealed class Entry
		{
			[JsonPropertyName("name")] public string Name { get; set; }
			[JsonPropertyName("files")] public List<Asset> Files { get; set; } = [];
		}

		internal sealed class Asset
		{
			[JsonPropertyName("path")] public string Deployed { get; set; }
			[JsonPropertyName("sha256")] public string Hash { get; set; }
			[JsonIgnore] public string Source { get; set; }
		}

		internal IEnumerable<Asset> Assets => Providers.SelectMany(provider => provider.Files);

		internal static bool TryBuild(IEnumerable<string> required, out CompiledPackageProviderManifest manifest, out string failure)
		{
			manifest = new();
			failure = null;

			try
			{
				foreach (var name in (required ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
				{
					if (!PackageProviderRegistry.IsValidProviderName(name))
					{
						failure = $"Package provider name '{name}' is invalid.";
						return false;
					}

					if (!PackageProviderRegistry.TryGetPayload(name, out var payload))
					{
						failure = $"Required package provider '{name}' was not found at '{PackageProviderRegistry.DescriptorRelativePath(name)}'.";
						return false;
					}

					var entry = new Entry { Name = name.ToLowerInvariant() };

					foreach (var source in payload.Files)
					{
						var relative = Path.GetRelativePath(payload.Root, source);

						if (!IsSafeRelative(relative))
						{
							failure = $"Package provider '{name}' contains an invalid payload path '{source}'.";
							return false;
						}

						entry.Files.Add(new Asset
						{
							Source = Path.GetFullPath(source),
							Deployed = $"{PackageProviderRegistry.RelativeDirectory(entry.Name)}/{relative.Replace('\\', '/')}",
							Hash = HashFile(source)
						});
					}

					manifest.Providers.Add(entry);
				}
			}
			catch (Exception e)
			{
				failure = $"Reading a package provider payload failed: {e.Message}";
				return false;
			}

			return true;
		}

		internal string Write() => JsonSerializer.Serialize(this);

		internal static string AssetResourceName(Asset asset) => AssetResourcePrefix + asset.Deployed;

		internal static CompiledPackageProviderManifest FromAssembly(Assembly assembly)
		{
			if (!TryRead(assembly, out var manifest, out _, out _))
				return null;

			return manifest;
		}

		/// <summary>Copies the provider hierarchy beside a full standalone executable.</summary>
		internal string CopyTo(string destination)
		{
			if (string.IsNullOrWhiteSpace(destination))
				return null;

			try
			{
				foreach (var asset in Assets)
				{
					if (!File.Exists(asset.Source) || !MatchesHash(asset.Source, asset.Hash))
						return $"Package provider asset changed or disappeared while compiling: {asset.Source}";

					var target = SafePath(destination, asset.Deployed);

					if (Path.GetFullPath(asset.Source).Equals(target, PathComparison))
						continue;

					_ = Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(asset.Source, target, true);
				}
			}
			catch (Exception e)
			{
				return $"Copying package providers to {destination} failed: {e.Message}";
			}

			return null;
		}

		/// <summary>Extracts and registers an embedded provider before the first imperative package resolution.</summary>
		internal static bool TryPrepare(Assembly assembly, string providerName, out string failure)
		{
			failure = null;

			if (!TryRead(assembly, out var manifest, out var present, out failure))
				return false;

			if (!present)
				return true;

			var entry = manifest.Providers.FirstOrDefault(p =>
				p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));

			if (entry == null)
				return true;

			if (!PackageProviderRegistry.IsValidProviderName(entry.Name))
			{
				failure = "The embedded package-provider manifest contains an invalid provider name.";
				return false;
			}

			var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			if (string.IsNullOrWhiteSpace(userRoot))
			{
				failure = $"Package provider '{entry.Name}' cannot be extracted because this account has no local application-data directory.";
				return false;
			}

			var root = Path.Combine(userRoot, "Keysharp", "embedded-components",
				assembly.ManifestModule.ModuleVersionId.ToString("N"));
			var seen = new HashSet<string>(PathComparer);

			try
			{
				foreach (var asset in entry.Files)
				{
					var normalized = asset.Deployed?.Replace('\\', '/');
					var prefix = PackageProviderRegistry.RelativeDirectory(entry.Name) + "/";

					if (normalized == null || !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
							|| !IsHash(asset.Hash) || !seen.Add(normalized))
						throw new InvalidDataException("The embedded package-provider manifest contains an invalid payload entry.");

					var target = SafePath(root, normalized);

					if (File.Exists(target) && MatchesHash(target, asset.Hash))
						continue;

					using var source = assembly.GetManifestResourceStream(AssetResourceName(asset));

					if (source == null)
						throw new InvalidDataException($"Embedded package-provider asset '{normalized}' is missing.");

					_ = Directory.CreateDirectory(Path.GetDirectoryName(target));
					var temporary = target + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";

					try
					{
						using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
						{
							source.CopyTo(output);
							output.Flush(true);
						}

						if (!MatchesHash(temporary, asset.Hash))
							throw new InvalidDataException($"Embedded package-provider asset '{normalized}' failed its SHA-256 check.");

						File.Move(temporary, target, true);
					}
					finally
					{
						try { File.Delete(temporary); } catch { }
					}

					if (!MatchesHash(target, asset.Hash))
						throw new InvalidDataException($"Extracted package-provider asset '{normalized}' failed its SHA-256 check.");
				}

				PackageProviderRegistry.AddSearchRoot(root);
				return true;
			}
			catch (Exception e)
			{
				failure = $"Preparing embedded package provider '{entry.Name}' failed: {e.Message}";
				return false;
			}
		}

		private static bool TryRead(Assembly assembly, out CompiledPackageProviderManifest manifest, out bool present, out string failure)
		{
			manifest = null;
			present = false;
			failure = null;

			if (assembly == null)
				return true;

			try
			{
				using var stream = assembly.GetManifestResourceStream(ResourceName);

				if (stream == null)
					return true;

				present = true;
				manifest = JsonSerializer.Deserialize<CompiledPackageProviderManifest>(stream);

				if (manifest == null)
					throw new InvalidDataException("The embedded package-provider manifest is empty.");

				return true;
			}
			catch (Exception e)
			{
				failure = $"Reading the embedded package-provider manifest failed: {e.Message}";
				return false;
			}
		}

		private static string HashFile(string path)
		{
			using var stream = File.OpenRead(path);
			return Convert.ToHexString(SHA256.HashData(stream));
		}

		private static bool MatchesHash(string path, string expected)
		{
			try { return HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase); }
			catch { return false; }
		}

		private static bool IsHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

		private static bool IsSafeRelative(string relative) =>
			!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative) && relative != ".."
			&& !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
			&& !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

		private static string SafePath(string root, string relative)
		{
			if (!IsSafeRelative(relative))
				throw new InvalidDataException("A package-provider manifest contains an invalid deployment path.");

			var fullRoot = Path.GetFullPath(root);
			var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
			var prefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;

			if (!full.StartsWith(prefix, PathComparison))
				throw new InvalidDataException("A package-provider manifest contains an invalid deployment path.");

			return full;
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
