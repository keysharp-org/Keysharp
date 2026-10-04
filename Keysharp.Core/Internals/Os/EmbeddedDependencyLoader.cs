using Keysharp.Builtins;
namespace Keysharp.Internals.Os
{
	internal static class EmbeddedDependencyLoader
	{
		internal static Dictionary<string, Assembly> assemblyResources = new(StringComparer.OrdinalIgnoreCase);
#if WINDOWS
		internal const string dllExt = ".dll";
#elif OSX
		internal const string dllExt = ".dylib";
#else
		internal const string dllExt = ".so";
#endif

		static EmbeddedDependencyLoader()
		{
			var asm = Assembly.GetExecutingAssembly();
			foreach (var name in asm.GetManifestResourceNames())
				if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
					assemblyResources[name] = asm;

			asm = Assembly.GetEntryAssembly();
			if (asm == null) return;
			foreach (var name in asm.GetManifestResourceNames())
				if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
					assemblyResources[name] = asm;
		}

#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
		[ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
		public static void Initialize()
		{
			NativeLibrary.SetDllImportResolver(
				typeof(EmbeddedDependencyLoader).Assembly, ResolveNativeFromResources);

			if (assemblyResources.Count > 0)
				AppDomain.CurrentDomain.AssemblyResolve += ResolveFromResources;
		}

		private static nint ResolveNativeFromResources(string libName, Assembly asm, DllImportSearchPath? path)
		{
			Assembly resourceAsm;
			var resourceName = $"{libName}{dllExt}";

			Stream rs = null;
			if (assemblyResources.TryGetValue(resourceName, out resourceAsm))
				rs = resourceAsm.GetManifestResourceStream(resourceName);
			else if (assemblyResources.TryGetValue("Deps." + resourceName, out resourceAsm))
				rs = resourceAsm.GetManifestResourceStream("Deps." + resourceName);

			if (rs == null)
			{
#if LINUX
				if (libName is "libkeysharp-input.so.1" or "libkeysharp-desktop.so.1")
				{
					//The loader's own search goes first so that LD_LIBRARY_PATH, or a copy beside the executable,
					//still wins; NixOS keeps its system libraries where that search never looks.
					if (NativeLibrary.TryLoad(libName, asm, path, out var searchedLibrary))
						return searchedLibrary;

					var systemProfilePath = Path.Combine(
						"/run/current-system/sw/lib", libName);
					if (NativeLibrary.TryLoad(systemProfilePath, out var systemProfileLibrary))
						return systemProfileLibrary;
				}
#endif
				return 0;
			}

			using (rs)
			{
				var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				if (string.IsNullOrEmpty(userRoot)) return 0;
				var cacheRoot = Path.Combine(userRoot, "Keysharp", "embedded-dependencies");
				var root = Path.Combine(cacheRoot, resourceAsm.ManifestModule.ModuleVersionId.ToString("N"));
				var extracted = EmbeddedExtraction.Extract(rs, root, resourceName);
				ExtractionCache.TouchAndPrune(cacheRoot, root);
				return NativeLibrary.Load(NativeLibraryResolver.NormalizeLoaderPath(extracted));
			}
		}

		// Keyed by simple name because Assembly.Load(byte[]) mints a new identity per call and does not satisfy
		// later binding, so resolving one dependency twice yields two mutually uncastable copies of its types.
		private static readonly Dictionary<string, Assembly> resolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);

		private static Assembly ResolveFromResources(object sender, ResolveEventArgs args)
		{
			var simpleName = new AssemblyName(args.Name).Name;

			// Re-entrant: loading an assembly can ask for its own dependencies on this thread.
			lock (resolvedAssemblies)
			{
				if (resolvedAssemblies.TryGetValue(simpleName, out var cached))
					return cached;

				// An assembly the runtime already has wins over a second copy from the resources.
				foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
					if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
						return resolvedAssemblies[simpleName] = loaded;

				var name = simpleName + ".dll";
				var resourceName = "Deps." + name; // match the <LogicalName> used in the manifest

				Stream stream = null;
				Assembly asm;
				if (assemblyResources.TryGetValue(name, out asm))
					stream = asm.GetManifestResourceStream(name);
				else if (assemblyResources.TryGetValue(resourceName, out asm))
					stream = asm.GetManifestResourceStream(resourceName);
				if (stream == null) return null;

				byte[] data = new byte[stream.Length];
				stream.ReadExactly(data);
				stream.Close();

				asm = Assembly.Load(data);
				resolvedAssemblies[simpleName] = asm;

				// Native P/Invoke resolver. Set exactly once per assembly — a second call throws.
				NativeLibrary.SetDllImportResolver(asm, ResolveNativeFromResources);

				return asm;
			}
		}
	}
}
