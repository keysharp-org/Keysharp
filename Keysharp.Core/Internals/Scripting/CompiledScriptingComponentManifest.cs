using System.Text.Json.Serialization;
using Keysharp.Components.Scripting;

namespace Keysharp.Internals.Scripting
{
	/// <summary>Optional first-party scripting-unit payloads carried by compiled scripts which need them at runtime.</summary>
	internal sealed class CompiledScriptingComponentManifest
	{
		internal const string ResourceName = "Keysharp.Components.Scripting.Manifest.json";
		internal const string AssetResourcePrefix = "Keysharp.Components.Scripting.Asset/";

		[JsonPropertyName("components")] public List<Entry> Components { get; set; } = [];

		internal sealed class Entry
		{
			[JsonPropertyName("name")] public string Name { get; set; }
			[JsonPropertyName("capabilities")] public ScriptingCapability Capabilities { get; set; }
			[JsonPropertyName("files")] public List<Asset> Files { get; set; } = [];
		}

		internal sealed class Asset
		{
			[JsonPropertyName("path")] public string Deployed { get; set; }
			[JsonPropertyName("sha256")] public string Hash { get; set; }
			[JsonIgnore] public string Source { get; set; }
		}

		internal IEnumerable<Asset> Assets => Components.SelectMany(component => component.Files);

		internal static bool TryBuild(IEnumerable<string> required, out CompiledScriptingComponentManifest manifest, out string failure)
		{
			manifest = new();
			failure = null;

			try
			{
				foreach (var name in (required ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
				{
					if (!ScriptingComponentRegistry.TryGetPayload(name, out var payload))
					{
						failure = $"Required scripting component '{name}' was not found at '{ScriptingComponentRegistry.DescriptorRelativePath(name)}'.";
						return false;
					}

					var entry = new Entry { Name = payload.Id.ToLowerInvariant(), Capabilities = payload.Capabilities };
					foreach (var source in payload.Files)
					{
						var relative = Path.GetRelativePath(payload.Root, source);
						if (!IsSafeRelative(relative))
						{
							failure = $"Scripting component '{name}' contains an invalid payload path '{source}'.";
							return false;
						}

						entry.Files.Add(new()
						{
							Source = Path.GetFullPath(source),
							Deployed = $"{ScriptingComponentRegistry.RelativeDirectory(entry.Name)}/{relative.Replace('\\', '/')}",
							Hash = HashFile(source)
						});
					}

					manifest.Components.Add(entry);
				}
			}
			catch (Exception e)
			{
				failure = $"Reading a scripting component payload failed: {e.Message}";
				return false;
			}

			return true;
		}

		internal string Write() => JsonSerializer.Serialize(this);

		internal static string AssetResourceName(Asset asset) => AssetResourcePrefix + asset.Deployed;

		internal string CopyTo(string destination)
		{
			if (string.IsNullOrWhiteSpace(destination))
				return null;

			try
			{
				foreach (var asset in Assets)
				{
					if (!File.Exists(asset.Source) || !MatchesHash(asset.Source, asset.Hash))
						return $"Scripting component asset changed or disappeared while compiling: {asset.Source}";

					var target = SafePath(destination, asset.Deployed);
					if (Path.GetFullPath(asset.Source).Equals(target, PathComparison))
						continue;

					_ = Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(asset.Source, target, true);
				}
			}
			catch (Exception e)
			{
				return $"Copying scripting components to {destination} failed: {e.Message}";
			}

			return null;
		}

		internal static bool HasCapability(Assembly assembly, ScriptingCapability capability) =>
			TryRead(assembly, out var manifest, out _, out _)
			&& manifest?.Components.Any(entry => (entry.Capabilities & capability) == capability) == true;

		internal static bool TryPrepare(Assembly assembly, ScriptingCapability capability, out string failure)
		{
			failure = null;
			if (!TryRead(assembly, out var manifest, out var present, out failure))
				return false;
			if (!present)
				return true;

			var entry = manifest.Components.FirstOrDefault(component =>
				(component.Capabilities & capability) == capability);
			if (entry == null)
				return true;

			var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrWhiteSpace(userRoot))
			{
				failure = $"Scripting component '{entry.Name}' cannot be extracted because this account has no local application-data directory.";
				return false;
			}

			var cacheRoot = Path.Combine(userRoot, "Keysharp", "embedded-components");
			var root = Path.Combine(cacheRoot, ContentKey(entry));
			var seen = new HashSet<string>(PathComparer);

			try
			{
				foreach (var asset in entry.Files)
				{
					var normalized = asset.Deployed?.Replace('\\', '/');
					var prefix = ScriptingComponentRegistry.RelativeDirectory(entry.Name) + "/";
					if (normalized == null || !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
							|| !IsHash(asset.Hash) || !seen.Add(normalized))
						throw new InvalidDataException("The embedded scripting-component manifest contains an invalid payload entry.");

					var target = SafePath(root, normalized);
					if (File.Exists(target) && MatchesHash(target, asset.Hash))
						continue;

					using var source = assembly.GetManifestResourceStream(AssetResourceName(asset));
					if (source == null)
						throw new InvalidDataException($"Embedded scripting-component asset '{normalized}' is missing.");

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
							throw new InvalidDataException($"Embedded scripting-component asset '{normalized}' failed its SHA-256 check.");
						File.Move(temporary, target, true);
					}
					finally
					{
						try { File.Delete(temporary); } catch { }
					}
				}

				ScriptingComponentRegistry.AddSearchRoot(root);
				TouchAndPruneCache(cacheRoot, root);
				return true;
			}
			catch (Exception e)
			{
				failure = $"Preparing embedded scripting component '{entry.Name}' failed: {e.Message}";
				return false;
			}
		}

		internal static string GetCacheDirectory(Assembly assembly, ScriptingCapability capability)
		{
			if (!TryRead(assembly, out var manifest, out var present, out _) || !present)
				return null;

			var entry = manifest.Components.FirstOrDefault(component =>
				(component.Capabilities & capability) == capability);
			var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return entry == null || string.IsNullOrWhiteSpace(userRoot) ? null : Path.Combine(userRoot,
				"Keysharp", "embedded-components", ContentKey(entry));
		}

		private static string ContentKey(Entry entry)
		{
			var text = new StringBuilder("scripting-component-v1\0")
				.Append(entry.Name?.ToLowerInvariant()).Append('\0')
				.Append((int)entry.Capabilities).Append('\0');
			foreach (var asset in entry.Files.OrderBy(asset => asset.Deployed, StringComparer.Ordinal))
				_ = text.Append(asset.Deployed?.Replace('\\', '/')).Append('\0')
					.Append(asset.Hash?.ToUpperInvariant()).Append('\0');
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
		}

		private static void TouchAndPruneCache(string cacheRoot, string currentRoot)
		{
			try { Directory.SetLastWriteTimeUtc(currentRoot, DateTime.UtcNow); } catch { }
			try
			{
				var cutoff = DateTime.UtcNow.AddDays(-30);
				foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
				{
					if (Path.GetFullPath(directory).Equals(Path.GetFullPath(currentRoot), PathComparison))
						continue;
					try
					{
						if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
							Directory.Delete(directory, true);
					}
					catch { }
				}
			}
			catch { }
		}

		private static bool TryRead(Assembly assembly, out CompiledScriptingComponentManifest manifest, out bool present, out string failure)
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
				manifest = JsonSerializer.Deserialize<CompiledScriptingComponentManifest>(stream)
					?? throw new InvalidDataException("The embedded scripting-component manifest is empty.");
				return true;
			}
			catch (Exception e)
			{
				failure = $"Reading the embedded scripting-component manifest failed: {e.Message}";
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
				throw new InvalidDataException("A scripting-component manifest contains an invalid deployment path.");

			var fullRoot = Path.GetFullPath(root);
			var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
			var prefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
			if (!full.StartsWith(prefix, PathComparison))
				throw new InvalidDataException("A scripting-component manifest contains an invalid deployment path.");
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
