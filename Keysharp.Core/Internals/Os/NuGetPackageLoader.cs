using Keysharp.Builtins;
using KsDebug = Keysharp.Builtins.Debug;
// PEReader.GetMetadataReader() is an extension method declared in this namespace, so it must be imported by name.
using System.Reflection.Metadata;

namespace Keysharp.Internals.Os
{
	/// <summary>
	/// The RUNTIME half of packages: for <c>#Package</c> it reads the manifest the compiler embedded and applies the
	/// pinned closure (resolution and any restore happened at BUILD time, in <c>PackageResolver</c>); only
	/// <c>Clr.LoadPackage</c> may still resolve — and restore — in this process, because an imperative call at run
	/// time is the one place where fetching a missing package is what the user asked for.
	/// See docs/design-nuget-packages.md for why.
	/// </summary>
	internal static class NuGetPackageLoader
	{
		private static readonly Lock sync = new();

		// The two maps below are read by the AssemblyLoadContext hooks, which fire on any thread and at any time —
		// including while this class holds `sync` for the duration of a restore (up to RestoreTimeoutMs). They are
		// therefore concurrent rather than guarded by `sync`, so a resolution on another thread is never blocked
		// behind a network operation.
		/// <summary>Assembly name/culture -> path, for the whole resolved closure. Dependencies are resolvable, not surfaced.</summary>
		private static readonly ConcurrentDictionary<string, string> managedByName = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Native library name (several spellings per file — see <see cref="AddNativeAliases"/>) -> path.</summary>
		private static readonly ConcurrentDictionary<string, string> nativeByName = new(StringComparer.OrdinalIgnoreCase);

		private static bool hooksInstalled;

		/// <summary>
		/// What to call this feature in messages, so a script that only used <c>Clr.LoadPackage</c> is never told a
		/// directive it does not contain failed. Set by both entry points, under <see cref="sync"/>.
		/// </summary>
		private static string label = "#Package";

		/// <summary>
		/// Every package requested so far in this process, from <c>#Package</c> and <c>Clr.LoadPackage</c> alike. A
		/// later request resolves the UNION of this and itself rather than its own island: resolution is whole-graph,
		/// so two independent resolutions can pick different versions of a shared dependency and try to load both.
		/// The directive avoids this by construction (one batched call); the runtime API cannot, so it accumulates.
		/// </summary>
		private static readonly List<PackageResolver.PackageRef> requested = [];

		/// <summary>Package id -> the managed assembly paths that package itself contributed, for LoadPackage's return.</summary>
		private static readonly Dictionary<string, List<string>> assembliesByPackage = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Package id -> whether it was applied as a directly requested package (loaded) rather than a dependency
		/// (registered only). Re-resolving after a later call replays the same closure; this is what stops it
		/// re-loading and re-reading metadata for work already done.
		/// </summary>
		private static readonly Dictionary<string, bool> applied = new(StringComparer.OrdinalIgnoreCase);
		/// <summary>
		/// Resets bookkeeping so a test starts from a known state. Package content and already-loaded assemblies are
		/// untouched — .NET cannot unload them.
		/// </summary>
		internal static void ResetForTests()
		{
			lock (sync)
			{
				requested.Clear();
				applied.Clear();
				assembliesByPackage.Clear();
				PackageResolver.ResetCounters();
			}
		}

		/// <summary>
		/// Backs the <c>#Package</c> directive, from the manifest the compiler embedded in the running assembly.
		/// An unavailable package not marked <c>*i</c> stops the script.
		/// <para>Nothing is resolved and nothing is restored here: the compiler already did that, and the manifest
		/// records the EXACT versions it type-checked against. That is what makes a built script reproducible — a
		/// floating <c>#Package Foo</c> resolved once, at build time, instead of resolving again on every machine
		/// that runs it and drifting into a <c>MissingMethodException</c> at JIT time.</para>
		/// </summary>
		/// <param name="scriptAssembly">The generated script's own assembly, which is where the manifest lives.</param>
		internal static void Load(Assembly scriptAssembly)
		{
			lock (sync)
			{
				label = "#Package";
				// The SCRIPT's assembly, which is the one the manifest was embedded in, with the entry assembly as a
				// fallback for a compiled script whose entry point IS the script.
				//
				// The runtime must use the assembly it was handed rather than querying the optional compiler component:
				// compiled scripts which only consume packages do not need the parser, compiler, or Roslyn installed.
				var manifest = PackageManifest.FromAssembly(scriptAssembly)
							   ?? PackageManifest.FromAssembly(Assembly.GetEntryAssembly());

				if (manifest == null)
				{
					_ = Errors.ErrorOccurred($"{label}: this script declares packages but carries no package manifest "
											 + "(this is a Keysharp bug, not a script error)", null, Keyword_ExitApp);
					return;
				}

				if (!manifest.TryLocate(scriptAssembly, out var resolved, out var missing))
				{
					_ = Errors.ErrorOccurred($"{label}: {missing}", null, Keyword_ExitApp);
					return;
				}

				// Only the packages the SCRIPT named. The transitive closure is deliberately excluded from both:
				// from `requested`, because a dependency's exact resolved version is not a constraint the script
				// wrote and would collide with a later Clr.LoadPackage asking for the same id; and from Apply's
				// `wanted`, because that is what marks a package "direct" — marking the whole closure direct would
				// load every dependency eagerly and turn a platform-irrelevant dependency into a fatal startup error.
				var direct = manifest.Direct;
				requested.AddRange(direct);
				Apply(resolved, direct);
			}
		}

		/// <summary>
		/// Backs <c>Clr.LoadPackage</c>. Returns the named package's own managed assemblies, null when an optional
		/// package could not be made available, and sets <paramref name="error"/> when a required one could not.
		/// </summary>
		internal static Assembly[] LoadOne(string id, string version, bool optional, out string error)
		{
			lock (sync)
			{
				label = "Clr.LoadPackage";
				var providerName = "nuget";
				var colon = id?.IndexOf(':') ?? -1;

				if (colon > 0)
				{
					providerName = id[..colon];
					id = id[(colon + 1)..];
				}

				if (!PackageProviderRegistry.IsValidProviderName(providerName))
				{
					error = $"{label}: '{providerName}' is not a valid package provider name";
					return null;
				}

				var scriptAssembly = Script.TheScript?.ProgramType?.Assembly ?? Assembly.GetEntryAssembly();

				if (!CompiledPackageProviderManifest.TryPrepare(scriptAssembly, providerName, out var prepareFailure))
				{
					error = $"{label}: {prepareFailure}";
					return null;
				}

				if (!PackageProviderRegistry.TryGet(providerName, out var provider, out var providerFailure))
				{
					if (optional)
					{
						error = null;
						_ = KsDebug.OutputDebug($"{label}: optional package provider '{providerName}' is not available, continuing without '{id}'");
						return null;
					}

					error = $"{label}: {providerFailure}";
					return null;
				}

				if (!provider.IsValidPackageId(id))
				{
					error = $"{label}: '{id}' is not a valid package name for provider '{providerName}'";
					return null;
				}

				if (!provider.TryNormalizeVersion(version, out var range, out var verr))
				{
					error = $"{label}: {verr} for package '{id}'";
					return null;
				}

				if (!Add([new PackageResolver.PackageRef(id, range, optional, providerName)], out error))
					return null;

				// Absent, or present but empty (a package whose only asset for this framework is the `_._` placeholder):
				// either way there is nothing to hand back.
				if (!assembliesByPackage.TryGetValue(PackageKey(providerName, id), out var paths) || paths.Count == 0)
				{
					if (!optional)
						error = $"{label}: '{id}' resolved but contributed no assemblies for this framework.";

					return null;
				}

				var loaded = new List<Assembly>(paths.Count);

				foreach (var path in paths)
				{
					// Apply already loaded these; re-resolving by path is how the Assembly objects are recovered. An
					// identity the shared framework also ships throws here, exactly as it does in Apply, and is benign.
					try { loaded.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(path)); }
					catch (FileLoadException) { }
				}

				return loaded.Count == 0 ? null : loaded.ToArray();
			}
		}

		/// <summary>
		/// Folds <paramref name="incoming"/> into <see cref="requested"/> and resolves the union. Returns false with a
		/// reason when a required package could not be made available; the additions are rolled back in that case, so a
		/// caught error does not poison every later call with a set that is known to fail. Caller holds <see cref="sync"/>.
		/// </summary>
		private static bool Add(List<PackageResolver.PackageRef> incoming, out string error)
		{
			error = null;

			if (incoming.Count == 0)
				return true;

			var restore = new List<PackageResolver.PackageRef>(requested);

			foreach (var p in incoming)
			{
				var i = requested.FindIndex(r => r.Provider.Equals(p.Provider, StringComparison.OrdinalIgnoreCase)
					&& r.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase));

				if (i < 0)
					requested.Add(p);
				else if (!requested[i].Version.Equals(p.Version, StringComparison.OrdinalIgnoreCase))
				{
					error = $"{label}: '{p.Id}' was already requested as version '{requested[i].Version}' and is now requested as '{p.Version}'";
					return Rollback(restore);
				}
				else if (!p.Optional)
					requested[i] = p;   // asking again without `*i` promotes it: the stricter requirement wins
			}

			if (TryLoadSet(requested, out error))
				return true;

			// A `*i` package that cannot be resolved must not stop the script — but resolution is whole-graph (see
			// `requested`), so one unavailable optional package fails the restore for the required ones too. Drop the
			// optional packages and resolve the remainder as its own set (its own cache entry, since the set differs).
			var required = requested.Where(p => !p.Optional).ToList();

			if (required.Count != requested.Count && (required.Count == 0 || TryLoadSet(required, out error)))
			{
				// `error` still holds the failure from the full-set attempt above, and an all-optional set reaches here
				// without overwriting it. Clearing it is what keeps an unavailable optional package non-fatal.
				error = null;
				_ = KsDebug.OutputDebug($"{label}: optional package(s) not available, continuing without: "
													   + string.Join(", ", requested.Where(p => p.Optional).Select(p => p.Id)));
				return true;
			}

			return Rollback(restore);

			static bool Rollback(List<PackageResolver.PackageRef> to)
			{
				requested.Clear();
				requested.AddRange(to);
				return false;
			}
		}

		/// <summary>
		/// Resolves and loads exactly this package set, or reports why it could not. Never fatal — the caller decides,
		/// because an unavailable <c>*i</c> package is recoverable and a required one is not.
		/// </summary>
		private static bool TryLoadSet(List<PackageResolver.PackageRef> packages, out string failure)
		{
			// Restore is allowed here: this is the running script's own process, which is the one place where
			// fetching a missing package is what the user asked for. The compiler resolves offline (see
			// PackageResolver.TryResolve) precisely because a syntax check is not.
			if (!PackageResolver.TryResolve(packages, allowRestore: true, label, out var resolved, out failure))
				return false;

			// `#Package` itself reports at compile time (the compiler component calls the same method), but
			// its Direct refs do sit in `requested`, so a later Clr.LoadPackage re-resolves the union INCLUDING them,
			// and a floating directive version can be reported here at whatever the union resolves to. Apply still
			// skips reloading anything already applied.
			PackageResolver.ReportResolved(packages, resolved, label);
			Apply(resolved, packages);
			return true;
		}

		// ---- loading ----

		/// <summary>
		/// Makes a resolved closure usable. Packages the script named itself are loaded now; everything they drag in
		/// is only *registered*, by reading its type and namespace names out of PE metadata — a dependency is then
		/// loaded on the first lookup that resolves into it and not before, the same laziness a compiled C# program
		/// gets from its assembly references (see <c>TypeResolver.RegisterDeferredAssembly</c>).
		///
		/// Re-resolving after a later <c>Clr.LoadPackage</c> replays the same closure, so packages already applied are
		/// skipped: reloading is idempotent but re-reading every dependency's metadata is not free.
		/// </summary>
		private static void Apply(List<PackageResolver.ResolvedPackage> resolved, List<PackageResolver.PackageRef> wanted)
		{
			InstallHooks();

			foreach (var pkg in resolved)
			{
				var packageKey = PackageKey(pkg.Provider, pkg.Id);
				var direct = wanted.Any(r => r.Provider.Equals(pkg.Provider, StringComparison.OrdinalIgnoreCase)
					&& r.Id.Equals(pkg.Id, StringComparison.OrdinalIgnoreCase));

				// Skip what is already done, but a package first seen as a dependency and later named directly still
				// has to be loaded rather than left deferred, so only a same-or-weaker repeat is skipped.
				if (applied.TryGetValue(packageKey, out var wasDirect) && (wasDirect || !direct))
					continue;

				foreach (var path in pkg.Managed.Concat(pkg.Resources))
					managedByName[ManagedKeyFor(path)] = path;

				foreach (var path in pkg.Native)
					AddNativeAliases(path);

				// What this package itself contributed, so Clr.LoadPackage can hand back exactly those assemblies
				// rather than the whole closure.
				assembliesByPackage[packageKey] = pkg.Managed;

				foreach (var path in pkg.Managed)
				{
					if (!direct && TryRegisterDeferred(path))
						continue;

					try
					{
						_ = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
					}
					catch (Exception e)
					{
						// An assembly whose identity is already loaded from elsewhere (commonly one the shared
						// framework also ships) is not an error — the existing one is used. Anything else is only
						// fatal for a package the script named itself; an unloadable dependency may simply be one
						// this platform never needs, and it stays reachable through the resolving hook if it is.
						if (e is FileLoadException || !direct)
							continue;

						_ = Errors.ErrorOccurred($"{label}: failed to load \"{path}\" from package {pkg.Id} {pkg.Version}: {e.Message}",
												 null, Keyword_ExitApp);
					}
				}

				// Recorded only once the package's assemblies are in hand: an OnError handler can let the error above
				// through, and marking it applied any earlier would let a later LoadPackage hand back the partial set
				// as if it had succeeded.
				applied[packageKey] = direct;
			}
		}

		private static string PackageKey(string provider, string id) => provider + "\0" + id;

		/// <summary>
		/// Registers an assembly's public top-level type names with the resolver without loading it. Reading the
		/// metadata tables directly is what makes deferral worth having: it yields strings out of the metadata heap
		/// and allocates no <see cref="Type"/> objects, whereas loading would pull the assembly's entire type set into
		/// the resolver's index (via <c>GetTypes()</c>) for a dependency the script may never touch.
		///
		/// Nested types are intentionally skipped. `Clr` walks dotted names, which never match the <c>Outer+Inner</c>
		/// spelling anyway, and reaching a nested type requires naming its declaring type first — which materializes
		/// the assembly and re-indexes it properly.
		///
		/// Returns false if the file has no managed metadata or cannot be read, in which case the caller falls back
		/// to loading it.
		/// </summary>
		private static bool TryRegisterDeferred(string path)
		{
			try
			{
				using var fs = File.OpenRead(path);
				using var pe = new System.Reflection.PortableExecutable.PEReader(fs);

				if (!pe.HasMetadata)
					return false;

				var mr = pe.GetMetadataReader();
				var names = new List<(string, string)>(mr.TypeDefinitions.Count);

				foreach (var handle in mr.TypeDefinitions)
				{
					var td = mr.GetTypeDefinition(handle);

					if ((td.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
						continue;

					names.Add((mr.GetString(td.Namespace), mr.GetString(td.Name)));
				}

				// Type forwarders: names this assembly publicly answers to even though the type lives elsewhere.
				// Loading it is still the right response, since the forward is what redirects the lookup.
				foreach (var handle in mr.ExportedTypes)
				{
					var et = mr.GetExportedType(handle);

					if (et.IsForwarder)
						names.Add((mr.GetString(et.Namespace), mr.GetString(et.Name)));
				}

				if (names.Count == 0)
					return false;

				TypeResolver.RegisterDeferredAssembly(path, names);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Registers the spellings a P/Invoke might use for one native file: DllImport("e_sqlite3") and
		/// DllImport("libfoo.so.1") both have to find their file, and on Unix the "lib" prefix is conventionally
		/// dropped in source.
		/// </summary>
		private static void AddNativeAliases(string path)
		{
			foreach (var alias in NativeAliasesFor(path))
				nativeByName[alias] = path;
		}

		/// <summary>The spellings a DllImport might use for one native file. Pure, so it is unit-testable.</summary>
		internal static List<string> NativeAliasesFor(string path)
		{
			var file = Path.GetFileName(path);
			var stem = Path.GetFileNameWithoutExtension(file);
			var aliases = new List<string> { file, stem };
			var soIdx = file.IndexOf(".so.", StringComparison.Ordinal);

			if (soIdx > 0)
			{
				stem = file.Substring(0, soIdx);
				aliases.Add(stem + ".so");
				aliases.Add(stem);
			}

			if (stem.StartsWith("lib", StringComparison.Ordinal) && stem.Length > 3)
				aliases.Add(stem.Substring(3));

			return aliases.Distinct().ToList();
		}

		internal static string ManagedKeyFor(string path)
		{
			try { return ManagedKey(AssemblyName.GetAssemblyName(path)); }
			catch
			{
				var culture = new DirectoryInfo(Path.GetDirectoryName(path) ?? "").Name;
				return Path.GetFileNameWithoutExtension(path) + "\0" + culture;
			}
		}

		private static string ManagedKey(AssemblyName name) =>
			(name.Name ?? "") + "\0" + (name.CultureName ?? "");

		private static void InstallHooks()
		{
			if (hooksInstalled)
				return;

			hooksInstalled = true;

			AssemblyLoadContext.Default.Resolving += (ctx, name) =>
				managedByName.TryGetValue(ManagedKey(name), out var path) && File.Exists(path)
				? ctx.LoadFromAssemblyPath(path)
				: null;
			AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
				// The Windows module loader rejects '/' separators; route through the same chokepoint DllCall uses.
				nativeByName.TryGetValue(name ?? "", out var path) && File.Exists(path)
				? NativeLibrary.Load(NativeLibraryResolver.NormalizeLoaderPath(path))
				: 0;
		}

	}
}
