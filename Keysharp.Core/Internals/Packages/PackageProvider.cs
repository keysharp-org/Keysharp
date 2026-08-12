using System.Runtime.Loader;
using System.Text.Json.Serialization;

namespace Keysharp.Internals.Os
{
	/// <summary>Locates and loads package providers without a static reference from Core to an implementation.</summary>
	internal static class PackageProviderRegistry
	{
		private const string DescriptorName = "provider.json";
		internal const string RelativeRoot = "components/packages";
		private static readonly ConcurrentDictionary<string, ProviderHandle> providers = new(StringComparer.OrdinalIgnoreCase);
		private static readonly List<string> additionalRoots = [];
		private static readonly Lock sync = new();

		internal sealed class Descriptor
		{
			[JsonPropertyName("name")] public string Name { get; set; }
			[JsonPropertyName("version")] public string Version { get; set; }
			[JsonPropertyName("assembly")] public string Assembly { get; set; }
			[JsonPropertyName("type")] public string Type { get; set; }
			[JsonPropertyName("files")] public List<string> Files { get; set; } = [];
		}

		internal sealed record Payload(string Root, string DescriptorPath, IReadOnlyList<string> Files);

		internal static void AddSearchRoot(string root)
		{
			if (string.IsNullOrWhiteSpace(root))
				return;

			root = Path.GetFullPath(root);

			lock (sync)
				if (!additionalRoots.Contains(root, PathComparer))
					additionalRoots.Add(root);
		}

		internal static bool TryGet(string name, out Keysharp.Components.Packages.IPackageProvider provider, out string failure)
		{
			provider = null;
			failure = null;

			if (!IsValidProviderName(name))
			{
				failure = $"package provider name '{name}' is invalid";
				return false;
			}

			try
			{
				var handle = providers.GetOrAdd(name, Load);
				provider = handle.Provider;
				return true;
			}
			catch (Exception e)
			{
				failure = $"package provider '{name}' could not be loaded: {Unwrap(e).Message}. "
						  + $"Expected a local provider at '{DescriptorRelativePath(name)}'.";
				return false;
			}
		}

		internal static bool TryGetPayload(string name, out Payload payload)
		{
			payload = null;
			var descriptorPath = FindDescriptor(name);

			if (descriptorPath == null)
				return false;

			var root = Path.GetDirectoryName(descriptorPath);
			var descriptor = ReadDescriptor(descriptorPath, name);
			var files = descriptor.Files.Select(file => SafePath(root, file)).Prepend(descriptorPath)
				.Distinct(PathComparer).OrderBy(p => p, PathComparer).ToArray();
			payload = new Payload(root, descriptorPath, files);
			return true;
		}

		internal static string Identity(string name)
		{
			var path = FindDescriptor(name);

			if (path == null)
				return name.ToLowerInvariant() + "@missing";

			try
			{
				var descriptor = ReadDescriptor(path, name);
				return name.ToLowerInvariant() + "@" + descriptor.Version;
			}
			catch { return name.ToLowerInvariant() + "@invalid"; }
		}

		internal static bool IsValidProviderName(string name) =>
			!string.IsNullOrEmpty(name) && name.Length < 32
			&& char.IsAsciiLetter(name[0])
			&& name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

		internal static string RelativeDirectory(string name) => $"{RelativeRoot}/{name.ToLowerInvariant()}";

		internal static string DescriptorRelativePath(string name) => $"{RelativeDirectory(name)}/{DescriptorName}";

		internal static void ResetForTests()
		{
			providers.Clear();
			lock (sync)
				additionalRoots.Clear();
		}

		private static ProviderHandle Load(string name)
		{
			var descriptorPath = FindDescriptor(name)
				?? throw new FileNotFoundException($"No {DescriptorName} was found for provider '{name}'");
			var descriptor = ReadDescriptor(descriptorPath, name);

			var root = Path.GetDirectoryName(descriptorPath);

			foreach (var file in descriptor.Files)
				_ = SafePath(root, file);

			var assemblyPath = SafePath(root, descriptor.Assembly);
			var loadContext = new ProviderLoadContext(assemblyPath);
			var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
			var type = assembly.GetType(descriptor.Type, throwOnError: true);
			var provider = Activator.CreateInstance(type) as Keysharp.Components.Packages.IPackageProvider
				?? throw new InvalidCastException($"{descriptor.Type} does not implement {nameof(Keysharp.Components.Packages.IPackageProvider)}");

			if (!name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"provider '{descriptor.Type}' identifies itself as '{provider.Name}'");

			if (!descriptor.Version.Equals(provider.Version, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"provider '{descriptor.Type}' version '{provider.Version}' does not match descriptor '{descriptor.Version}'");

			return new ProviderHandle(provider, loadContext);
		}

		private static string FindDescriptor(string name)
		{
			if (!IsValidProviderName(name))
				return null;

			foreach (var root in SearchRoots())
			{
				var path = Path.Combine(root, DescriptorRelativePath(name).Replace('/', Path.DirectorySeparatorChar));

				if (File.Exists(path))
					return Path.GetFullPath(path);
			}

			return null;
		}

		private static Descriptor ReadDescriptor(string path, string expectedName)
		{
			var descriptor = JsonSerializer.Deserialize<Descriptor>(File.ReadAllText(path))
				?? throw new InvalidDataException($"'{path}' is empty or invalid");

			if (!expectedName.Equals(descriptor.Name, StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(descriptor.Version)
				|| string.IsNullOrWhiteSpace(descriptor.Assembly) || string.IsNullOrWhiteSpace(descriptor.Type)
				|| descriptor.Files.Count == 0)
				throw new InvalidDataException($"'{path}' does not completely describe provider '{expectedName}'");

			return descriptor;
		}

		private static IEnumerable<string> SearchRoots()
		{
			string[] added;
			lock (sync)
				added = [.. additionalRoots];

			return added.Concat(new[]
			{
				AppContext.BaseDirectory,
				Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? ""),
				Path.GetDirectoryName(typeof(PackageProviderRegistry).Assembly.Location)
			}).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(PathComparer);
		}

		private static string SafePath(string root, string relative)
		{
			var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
			var full = Path.GetFullPath(Path.Combine(root, relative));

			if (!full.StartsWith(fullRoot, PathComparison) || !File.Exists(full))
				throw new InvalidDataException($"provider payload path '{relative}' is missing or escapes its directory");

			return full;
		}

		private static Exception Unwrap(Exception e) =>
			e is TypeInitializationException or TargetInvocationException && e.InnerException != null ? Unwrap(e.InnerException) : e;

		private sealed record ProviderHandle(Keysharp.Components.Packages.IPackageProvider Provider, ProviderLoadContext LoadContext);

		private sealed class ProviderLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: false)
		{
			private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

			protected override Assembly Load(AssemblyName assemblyName)
			{
				var core = typeof(Keysharp.Components.Packages.IPackageProvider).Assembly;

				if (AssemblyName.ReferenceMatchesDefinition(core.GetName(), assemblyName))
					return core;

				if (resolver.ResolveAssemblyToPath(assemblyName) is { } path)
					return LoadFromAssemblyPath(path);

				var beside = Path.Combine(Path.GetDirectoryName(mainAssemblyPath), assemblyName.Name + ".dll");
				return File.Exists(beside) ? LoadFromAssemblyPath(beside) : null;
			}

			protected override nint LoadUnmanagedDll(string unmanagedDllName) =>
				resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : 0;
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
