using System.Runtime.Loader;
using System.Text.Json.Serialization;
using Keysharp.Components.Scripting;

namespace Keysharp.Internals.Scripting
{
	/// <summary>Discovers fixed optional first-party scripting units without statically referencing their implementations.</summary>
	internal static class ScriptingComponentRegistry
	{
		internal const int SupportedDescriptorSchema = 1;
		internal const int SupportedContractVersion = 1;
		private const string DescriptorName = "component.json";
		internal const string RelativeRoot = "components/scripting";
		private static readonly ConcurrentDictionary<string, ComponentHandle> components = new(PathComparer);
		private static readonly List<string> additionalRoots = [];
		private static readonly Lock sync = new();
		private static readonly IReadOnlyDictionary<string, Assembly> sharedAssemblies = new[]
		{
			typeof(ScriptingComponentRegistry).Assembly,
			typeof(IScriptingComponent).Assembly,
			typeof(Keysharp.Components.Packages.IPackageProvider).Assembly,
			typeof(Semver.SemVersion).Assembly,
		}.ToDictionary(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase);
		private static string[] testSearchRoots;

		internal sealed class Descriptor
		{
			[JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
			[JsonPropertyName("contractVersion")] public int ContractVersion { get; set; }
			[JsonPropertyName("id")] public string Id { get; set; }
			[JsonPropertyName("version")] public string Version { get; set; }
			[JsonPropertyName("assembly")] public string Assembly { get; set; }
			[JsonPropertyName("type")] public string Type { get; set; }
			[JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];
			[JsonPropertyName("files")] public List<string> Files { get; set; } = [];
		}

		internal sealed record Payload(string Id, string Root, string DescriptorPath,
			ScriptingCapability Capabilities, IReadOnlyList<string> Files);

		internal static bool TryGetSyntaxValidator(out IScriptSyntaxValidator validator, out string failure)
		{
			validator = null;
			if (!TryGet(ScriptingCapability.SyntaxValidation, out var component, out failure))
				return false;

			validator = component as IScriptSyntaxValidator;
			if (validator != null)
				return true;

			failure = $"Scripting component '{component.Id}' declares syntax-validation support but does not implement {nameof(IScriptSyntaxValidator)}.";
			return false;
		}

		internal static bool TryGetCompiler(out IScriptCompiler compiler, out string failure)
		{
			compiler = null;
			if (!TryGet(ScriptingCapability.Compilation, out var component, out failure))
				return false;

			compiler = component as IScriptCompiler;
			if (compiler != null)
				return true;

			failure = $"Scripting component '{component.Id}' declares compilation support but does not implement {nameof(IScriptCompiler)}.";
			return false;
		}

		internal static bool IsAvailable(ScriptingCapability capability)
		{
			return TryGet(capability, out _, out _);
		}

		internal static void AddSearchRoot(string root)
		{
			if (string.IsNullOrWhiteSpace(root))
				return;

			root = Path.GetFullPath(root);
			lock (sync)
				if (!additionalRoots.Contains(root, PathComparer))
					additionalRoots.Add(root);
		}

		internal static bool TryGetPayload(string name, out Payload payload)
		{
			payload = null;
			if (!IsKnownId(name))
				return false;

			foreach (var path in FindDescriptors(name))
			{
				try
				{
					_ = components.GetOrAdd(path, Load);
					var descriptor = ReadDescriptor(path);
					var root = Path.GetDirectoryName(path);
					var files = descriptor.Files.Select(file => SafePath(root, file)).Prepend(path)
						.Distinct(PathComparer).OrderBy(p => p, PathComparer).ToArray();
					payload = new(descriptor.Id, root, path, ParseCapabilities(descriptor.Capabilities), files);
					return true;
				}
				catch { }
			}

			return false;
		}

		internal static string RelativeDirectory(string name) => $"{RelativeRoot}/{name.ToLowerInvariant()}";

		internal static string DescriptorRelativePath(string name) => $"{RelativeDirectory(name)}/{DescriptorName}";

		internal static void ResetForTests()
		{
			components.Clear();
			lock (sync)
			{
				additionalRoots.Clear();
				testSearchRoots = null;
			}
		}

		internal static void SetSearchRootsForTests(params string[] roots)
		{
			components.Clear();
			lock (sync)
				testSearchRoots = roots?.Where(root => !string.IsNullOrWhiteSpace(root))
					.Select(Path.GetFullPath).Distinct(PathComparer).ToArray();
		}

		private static bool TryGet(ScriptingCapability capability, out IScriptingComponent component, out string failure)
		{
			component = null;
			failure = null;

			try
			{
				var embeddedAssembly = Script.TheScript?.ProgramType?.Assembly ?? Assembly.GetEntryAssembly();
				if (!CompiledScriptingComponentManifest.TryPrepare(embeddedAssembly, capability, out failure))
					return false;

				var descriptorPaths = FindDescriptors(capability).ToArray();
				if (descriptorPaths.Length == 0)
				{
					var id = ComponentId(capability);
					failure = $"No installed '{id}' scripting component provides '{capability}'. Expected it below '{RelativeRoot}/{id}'.";
					return false;
				}

				var failures = new List<string>();
				foreach (var descriptorPath in descriptorPaths)
				{
					try
					{
						var handle = components.GetOrAdd(descriptorPath, Load);
						component = handle.Component;
						return true;
					}
					catch (Exception e)
					{
						failures.Add($"'{descriptorPath}': {Unwrap(e).Message}");
					}
				}

				failure = $"Scripting component could not be loaded: {string.Join("; ", failures)}.";
				return false;
			}
			catch (Exception e)
			{
				failure = $"Scripting component could not be loaded: {Unwrap(e).Message}.";
				return false;
			}
		}

		private static ComponentHandle Load(string descriptorPath)
		{
			var descriptor = ReadDescriptor(descriptorPath);
			var root = Path.GetDirectoryName(descriptorPath);

			foreach (var file in descriptor.Files)
				_ = SafePath(root, file);

			var assemblyPath = SafePath(root, descriptor.Assembly);
			var loadContext = new ComponentLoadContext(assemblyPath);
			var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
			var type = assembly.GetType(descriptor.Type, throwOnError: true);
			var component = Activator.CreateInstance(type) as IScriptingComponent
				?? throw new InvalidCastException($"{descriptor.Type} does not implement {nameof(IScriptingComponent)}");
			var declared = ParseCapabilities(descriptor.Capabilities);

			if (!descriptor.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase)
					|| declared != component.Capabilities)
				throw new InvalidDataException($"Scripting component '{descriptor.Type}' does not match its descriptor.");

			return new(component, loadContext);
		}

		private static IEnumerable<string> FindDescriptors(ScriptingCapability capability)
		{
			var requiredName = ComponentId(capability);
			return DescriptorPaths().Select(path => (path, descriptor: TryReadDescriptor(path)))
				.Where(item => item.descriptor != null
					&& item.descriptor.Id.Equals(requiredName, StringComparison.OrdinalIgnoreCase)
					&& (ParseCapabilities(item.descriptor.Capabilities) & capability) == capability)
				.Select(item => item.path);
		}

		private static string ComponentId(ScriptingCapability capability) =>
			capability == ScriptingCapability.Compilation ? ScriptingComponentIds.Compiler : ScriptingComponentIds.Parser;

		private static IEnumerable<string> FindDescriptors(string id) => DescriptorPaths().Where(path =>
		{
			var descriptor = TryReadDescriptor(path);
			return descriptor != null && descriptor.Id.Equals(id, StringComparison.OrdinalIgnoreCase);
		});

		private static IEnumerable<string> DescriptorPaths()
		{
			foreach (var root in SearchRoots())
			{
				var componentRoot = Path.Combine(root, RelativeRoot.Replace('/', Path.DirectorySeparatorChar));
				if (!Directory.Exists(componentRoot))
					continue;

				foreach (var directory in Directory.EnumerateDirectories(componentRoot).OrderBy(path => path, PathComparer))
				{
					var descriptor = Path.Combine(directory, DescriptorName);
					if (File.Exists(descriptor))
						yield return Path.GetFullPath(descriptor);
				}
			}
		}

		private static IEnumerable<string> SearchRoots()
		{
			string[] added;
			lock (sync)
			{
				if (testSearchRoots is { Length: > 0 })
					return testSearchRoots;

				added = [.. additionalRoots];
			}

			return added.Concat(new[]
			{
				RunningArtifactDirectory(),
				AppContext.BaseDirectory,
				Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? ""),
				Path.GetDirectoryName(typeof(ScriptingComponentRegistry).Assembly.Location)
			}).Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(PathComparer);
		}

		private static string RunningArtifactDirectory()
		{
			var path = ScriptExecutionState.SourcePath;
			return !string.IsNullOrWhiteSpace(path) && path != "*" && File.Exists(path)
				? Path.GetDirectoryName(Path.GetFullPath(path))
				: null;
		}

		private static Descriptor TryReadDescriptor(string path)
		{
			try { return ReadDescriptor(path); }
			catch { return null; }
		}

		private static Descriptor ReadDescriptor(string path)
		{
			var descriptor = JsonSerializer.Deserialize<Descriptor>(File.ReadAllText(path))
				?? throw new InvalidDataException($"'{path}' is empty or invalid");

			if (descriptor.SchemaVersion != SupportedDescriptorSchema)
				throw new InvalidDataException($"'{path}' uses unsupported scripting-component schema version {descriptor.SchemaVersion}.");
			if (descriptor.ContractVersion != SupportedContractVersion)
				throw new InvalidDataException($"'{path}' requires unsupported scripting-component contract version {descriptor.ContractVersion}.");

			var capabilities = ParseCapabilities(descriptor.Capabilities);
			var expectedCapabilities = descriptor.Id?.Equals(ScriptingComponentIds.Parser, StringComparison.OrdinalIgnoreCase) == true
				? ScriptingCapability.SyntaxValidation
				: ScriptingCapability.Compilation;

			if (!IsKnownId(descriptor.Id) || !Version.TryParse(descriptor.Version, out _)
					|| string.IsNullOrWhiteSpace(descriptor.Assembly) || string.IsNullOrWhiteSpace(descriptor.Type)
					|| descriptor.Files.Count == 0 || capabilities != expectedCapabilities
					|| descriptor.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != descriptor.Files.Count
					|| !descriptor.Files.Contains(descriptor.Assembly, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException($"'{path}' does not completely describe a scripting component.");

			return descriptor;
		}

		private static ScriptingCapability ParseCapabilities(IEnumerable<string> values)
		{
			var result = ScriptingCapability.None;
			foreach (var value in values ?? [])
			{
				if (!Enum.TryParse<ScriptingCapability>(value, true, out var parsed) || parsed == ScriptingCapability.None)
					throw new InvalidDataException($"Unknown scripting capability '{value}'.");
				result |= parsed;
			}
			return result;
		}

		private static bool IsKnownId(string id) => id != null
			&& (id.Equals(ScriptingComponentIds.Parser, StringComparison.OrdinalIgnoreCase)
				|| id.Equals(ScriptingComponentIds.Compiler, StringComparison.OrdinalIgnoreCase));

		private static string SafePath(string root, string relative)
		{
			var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
			var full = Path.GetFullPath(Path.Combine(root, relative));
			if (!full.StartsWith(fullRoot, PathComparison) || !File.Exists(full))
				throw new InvalidDataException($"Scripting component payload path '{relative}' is missing or escapes its directory.");
			return full;
		}

		private static Exception Unwrap(Exception e) =>
			e is TypeInitializationException or TargetInvocationException && e.InnerException != null ? Unwrap(e.InnerException) : e;

		private sealed record ComponentHandle(IScriptingComponent Component, ComponentLoadContext LoadContext);

		private sealed class ComponentLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: false)
		{
			private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

			protected override Assembly Load(AssemblyName assemblyName)
			{
				// Core and the contracts may themselves have been loaded from embedded bytes, in which case
				// asking the default context for the same name creates a second, type-incompatible identity.
				if (sharedAssemblies.TryGetValue(assemblyName.Name, out var contractAssembly)
						&& AssemblyName.ReferenceMatchesDefinition(contractAssembly.GetName(), assemblyName))
					return contractAssembly;

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
