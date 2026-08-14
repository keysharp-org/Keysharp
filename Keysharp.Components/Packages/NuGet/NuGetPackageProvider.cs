using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Keysharp.Components.Packages.NuGet;

public sealed class NuGetPackageProvider : IPackageProvider
{
	public string Name => "nuget";
	public string Version => ProviderInfo.Version;

	public bool IsValidPackageId(string value) => value?.Length is > 0 and < 128
		&& value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

	public bool TryNormalizeVersion(string written, out string normalized, out string error)
	{
		normalized = Translate((written ?? "").Trim());
		error = normalized == null ? $"'{written}' is not a valid version" : null;
		return normalized != null;
	}

	public async Task<PackageResolveResult> ResolveAsync(PackageResolveContext context,
		IReadOnlyList<PackageRequest> packages, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (context.Timeout > TimeSpan.Zero && context.Timeout != Timeout.InfiniteTimeSpan)
			timeout.CancelAfter(context.Timeout);

		var resolveToken = timeout.Token;
		Directory.CreateDirectory(context.CacheDirectory);
		var logger = new CollectingLogger();

		try
		{
			DefaultCredentialServiceUtility.SetupDefaultCredentialService(logger, nonInteractive: true);
			var settings = Settings.LoadDefaultSettings(context.SettingsDirectory);
			var packageSourceProvider = new PackageSourceProvider(settings);
			var sources = packageSourceProvider.LoadPackageSources().Where(source => source.IsEnabled).ToList();
			var packagesPath = SettingsUtility.GetGlobalPackagesFolder(settings);
			var fallbackFolders = SettingsUtility.GetFallbackPackageFolders(settings).ToList();
			var configFiles = settings.GetConfigFilePaths().ToList();
			var fingerprint = Fingerprint(context, packages, sources, packagesPath, fallbackFolders, configFiles);
			var graphDirectory = Path.Combine(context.CacheDirectory, fingerprint[..24]);
			Directory.CreateDirectory(graphDirectory);
			var assetsPath = Path.Combine(graphDirectory, "project.assets.json");
			var cachePath = Path.Combine(graphDirectory, "project.nuget.cache");
			var stampPath = Path.Combine(graphDirectory, "keysharp.restore.sha256");

			await using var restoreLock = await AcquireLockAsync(Path.Combine(context.CacheDirectory, "restore.lock"), resolveToken)
				.ConfigureAwait(false);
			var warm = TryReadWarm(assetsPath, cachePath, stampPath, fingerprint);

			if (warm != null && BuildResult(warm, context.RuntimeIdentifier, restoreAttempted: false) is { Success: true } warmResult)
				return warmResult;

			if (!context.AllowRestore)
				return Failure(context, packages, "these packages have not been restored yet, and restoring is disabled here");

			Invalidate(assetsPath, cachePath, stampPath);
			var spec = CreateSpec(context with { CacheDirectory = graphDirectory }, packages, sources, packagesPath,
				fallbackFolders, configFiles, assetsPath, cachePath);
			var graph = new DependencyGraphSpec();
			graph.AddProject(spec);
			graph.AddRestore(spec.RestoreMetadata.ProjectUniqueName);

			using var cacheContext = new SourceCacheContext();
			var args = new RestoreArgs
			{
				AllowNoOp = false,
				CacheContext = cacheContext,
				CachingSourceProvider = new CachingSourceProvider(packageSourceProvider),
				Log = logger,
				DisableParallel = false
			};
			args.PreLoadedRequestProviders.Add(new DependencyGraphSpecRequestProvider(
				new RestoreCommandProvidersCache(), graph, settings));
			var summaries = await RestoreRunner.RunAsync(args, resolveToken).ConfigureAwait(false);
			var summary = summaries.SingleOrDefault();

			if (summary?.Success != true)
				return Failure(context, packages, string.Join(Environment.NewLine, ImportantMessages(logger, LogLevel.Error)));

			var lockFile = ReadLockFile(assetsPath);

			if (lockFile == null || !RestoreCacheSucceeded(cachePath))
				return Failure(context, packages, $"restore succeeded but no usable package graph was produced in '{assetsPath}'");

			var result = BuildResult(lockFile, context.RuntimeIdentifier, restoreAttempted: true);

			if (!result.Success)
				return result;

			WriteStamp(stampPath, fingerprint);
			result.Diagnostics.AddRange(ImportantMessages(logger, LogLevel.Warning));
			return result;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception exception)
		{
			return Failure(context, packages, exception.GetBaseException().Message);
		}
	}

	private static PackageSpec CreateSpec(PackageResolveContext context, IReadOnlyList<PackageRequest> packages,
		IList<PackageSource> sources, string packagesPath, IList<string> fallbackFolders, IList<string> configFiles,
		string assetsPath, string cachePath)
	{
		var framework = NuGetFramework.ParseFolder(context.TargetFramework);

		if (framework.IsUnsupported)
			throw new InvalidDataException($"'{context.TargetFramework}' is not a valid NuGet target framework");
		var dependencies = packages.Select(package => new LibraryDependency
		{
			LibraryRange = new LibraryRange(package.Id, VersionRange.Parse(package.Version), LibraryDependencyTarget.Package),
			IncludeType = LibraryIncludeFlags.All,
			SuppressParent = LibraryIncludeFlags.None
		}).ToImmutableArray();
		var runtimeGraph = CreateRuntimeGraph(context.RuntimeIdentifier);
		var target = new TargetFrameworkInformation
		{
			FrameworkName = framework,
			TargetAlias = context.TargetFramework,
			Dependencies = dependencies,
			RuntimeIdentifierGraphPath = WriteRuntimeGraph(context, runtimeGraph)
		};
		var projectPath = Path.Combine(context.CacheDirectory, "keysharp-packages.csproj");
		var metadata = new ProjectRestoreMetadata
		{
			ProjectStyle = ProjectStyle.PackageReference,
			ProjectPath = projectPath,
			ProjectName = "keysharp-packages",
			ProjectUniqueName = projectPath,
			OutputPath = context.CacheDirectory,
			PackagesPath = packagesPath,
			CacheFilePath = cachePath,
			Sources = sources,
			FallbackFolders = fallbackFolders,
			ConfigFilePaths = configFiles,
			OriginalTargetFrameworks = [context.TargetFramework],
			TargetFrameworks = [new ProjectRestoreMetadataFrameworkInfo(framework) { TargetAlias = context.TargetFramework }],
			ValidateRuntimeAssets = true,
			SkipContentFileWrite = true
		};
		metadata.RestoreAuditProperties = new RestoreAuditProperties
		{
			EnableAudit = "true",
			AuditLevel = "low",
			AuditMode = "all",
			SuppressedAdvisories = []
		};

		return new PackageSpec([target])
		{
			Name = "keysharp-packages",
			FilePath = projectPath,
			RestoreMetadata = metadata,
			RuntimeGraph = runtimeGraph
		};
	}

	/// <summary>
	/// Writes the RID fallback chain where restore looks for it, and answers with that path.
	/// <para>RID-specific assets are selected from the graph named here, not from the spec's own RuntimeGraph,
	/// which only decides which RIDs get a target. Hosts report a distro RID (ubuntu.24.04-x64) while packages
	/// publish portable ones (runtimes/linux-x64/native/…), so without the chain a native asset is never
	/// selected — restore still succeeds and only the first P/Invoke fails.</para>
	/// <para>The SDK points this at its own PortableRuntimeIdentifierGraph.json, which a runtime-only host does
	/// not have; the shared framework's chain for the running RID is the same data, narrowed to what can be
	/// resolved here.</para>
	/// </summary>
	private static string WriteRuntimeGraph(PackageResolveContext context, global::NuGet.RuntimeModel.RuntimeGraph graph)
	{
		// No RID means no RID-specific target to select assets for, so there is nothing for a chain to say.
		if (string.IsNullOrWhiteSpace(context.RuntimeIdentifier))
			return null;

		var path = Path.Combine(context.CacheDirectory, "runtime-graph.json");
		global::NuGet.RuntimeModel.JsonRuntimeFormat.WriteRuntimeGraph(path, graph);
		return path;
	}

	private static global::NuGet.RuntimeModel.RuntimeGraph CreateRuntimeGraph(string rid)
	{
		if (string.IsNullOrWhiteSpace(rid))
			return global::NuGet.RuntimeModel.RuntimeGraph.Empty;

		try
		{
			var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
			var deps = AppContext.GetData("FX_DEPS_FILE") as string;

			if (string.IsNullOrWhiteSpace(deps) || !File.Exists(deps))
				deps = Path.Combine(runtimeDirectory ?? "", "Microsoft.NETCore.App.deps.json");

			using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(deps));
			var descriptions = new List<global::NuGet.RuntimeModel.RuntimeDescription>();

			foreach (var entry in document.RootElement.GetProperty("runtimes").EnumerateObject())
				descriptions.Add(new global::NuGet.RuntimeModel.RuntimeDescription(entry.Name,
					entry.Value.EnumerateArray().Select(value => value.GetString()).Where(value => value != null)));

			return new global::NuGet.RuntimeModel.RuntimeGraph(descriptions);
		}
		catch (Exception exception)
		{
			throw new InvalidDataException($"Could not read the installed .NET runtime identifier graph for '{rid}'", exception);
		}
	}

	private static LockFile ReadLockFile(string path)
	{
		try { return File.Exists(path) ? new LockFileFormat().Read(path) : null; }
		catch { return null; }
	}

	private static LockFile TryReadWarm(string assetsPath, string cachePath, string stampPath, string fingerprint)
	{
		if (!RestoreCacheSucceeded(cachePath) || !File.Exists(stampPath)
			|| !File.ReadAllText(stampPath).Trim().Equals(fingerprint, StringComparison.Ordinal))
			return null;

		return ReadLockFile(assetsPath);
	}

	private static bool RestoreCacheSucceeded(string path)
	{
		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(path));
			return document.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
		}
		catch { return false; }
	}

	private static void Invalidate(params string[] paths)
	{
		foreach (var path in paths)
			try { File.Delete(path); } catch { }
	}

	private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous); }
			catch (IOException) { await Task.Delay(75, cancellationToken).ConfigureAwait(false); }
		}
	}

	private static string Fingerprint(PackageResolveContext context, IReadOnlyList<PackageRequest> packages,
		IEnumerable<PackageSource> sources, string packagesPath, IEnumerable<string> fallbackFolders, IEnumerable<string> configFiles)
	{
		var text = new StringBuilder()
			.AppendLine("keysharp-nuget-provider/" + ProviderInfo.Version)
			.AppendLine(Path.GetFullPath(context.SettingsDirectory))
			.AppendLine(context.TargetFramework).AppendLine(context.RuntimeIdentifier)
			.AppendLine(Path.GetFullPath(packagesPath));

		foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
			_ = text.AppendLine($"p|{package.Id.ToLowerInvariant()}|{package.Version}");

		foreach (var source in sources.OrderBy(s => s.Source, StringComparer.OrdinalIgnoreCase))
			_ = text.AppendLine($"s|{source.Name}|{source.Source}|{source.ProtocolVersion}|{source.IsEnabled}");

		foreach (var folder in fallbackFolders.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
			_ = text.AppendLine("f|" + Path.GetFullPath(folder));

		foreach (var file in configFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
		{
			_ = text.Append("c|").Append(Path.GetFullPath(file)).Append('|');
			_ = text.AppendLine(File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : "missing");
		}

		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
	}

	private static void WriteStamp(string path, string fingerprint)
	{
		var temporary = path + "." + Environment.ProcessId + ".tmp";
		File.WriteAllText(temporary, fingerprint + Environment.NewLine);
		File.Move(temporary, path, true);
	}

	private static PackageResolveResult BuildResult(LockFile lockFile, string runtimeIdentifier, bool restoreAttempted)
	{
		var result = new PackageResolveResult { Success = true, RestoreAttempted = restoreAttempted };
		var target = lockFile.Targets.FirstOrDefault(target => string.Equals(target.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase))
			?? lockFile.Targets.FirstOrDefault();

		if (target == null)
			return new PackageResolveResult { Success = false, RestoreAttempted = restoreAttempted, Failure = "the resolved graph has no target" };

		var folders = lockFile.PackageFolders.Select(folder => folder.Path).ToArray();

		foreach (var library in target.Libraries.Where(library => library.Type == "package"))
		{
			var lockLibrary = lockFile.Libraries.FirstOrDefault(candidate =>
				candidate.Name.Equals(library.Name, StringComparison.OrdinalIgnoreCase) && candidate.Version == library.Version);

			if (lockLibrary == null)
				return Invalid($"package '{library.Name} {library.Version}' has no library entry");

			var root = folders.Select(folder => Path.Combine(folder, lockLibrary.Path.Replace('/', Path.DirectorySeparatorChar)))
				.FirstOrDefault(Directory.Exists);

			if (root == null)
				return Invalid($"package '{library.Name} {library.Version}' is missing from the global packages folder");

			var version = library.Version.ToNormalizedString();
			var resolved = new ResolvedPackage
			{
				Id = library.Name,
				Version = version,
				PinnedVersion = $"[{version}]",
				Root = root
			};

			if (!Collect(root, library.CompileTimeAssemblies, resolved.Compile)
				|| !Collect(root, library.RuntimeAssemblies, resolved.Runtime)
				|| !Collect(root, library.ResourceAssemblies, resolved.Resources)
				|| !Collect(root, library.NativeLibraries, resolved.Native))
				return Invalid($"package '{library.Name} {library.Version}' is missing a selected asset");

			result.Packages.Add(resolved);
		}

		return result;

		PackageResolveResult Invalid(string reason) => new() { Success = false, RestoreAttempted = restoreAttempted, Failure = reason };

		static bool Collect(string root, IEnumerable<LockFileItem> items, List<string> destination)
		{
			foreach (var item in items.Where(item => !item.Path.EndsWith("_._", StringComparison.Ordinal)))
			{
				var path = Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(path))
					return false;

				destination.Add(path);
			}

			return true;
		}
	}

	private static PackageResolveResult Failure(PackageResolveContext context, IReadOnlyList<PackageRequest> packages, string reason) =>
		new()
		{
			Success = false,
			RestoreAttempted = context.AllowRestore,
			Failure = $"{context.Label}: {reason}. Requested: {string.Join(", ", packages.Select(p => $"{p.Id} {p.Version}"))}"
		};

	private static IEnumerable<string> ImportantMessages(CollectingLogger logger, LogLevel minimum) => logger.Messages
		.Where(message => message.Level >= minimum)
		.Select(message => message.FormatWithCode()).Distinct();

	private static string Translate(string value)
	{
		if (value.Length == 0)
			return "*";

		if (value[0] is '[' or '(' || value.Contains('*'))
			return VersionRange.TryParse(value, out _) ? value : null;

		var tokens = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

		if (tokens.Length == 1 && Operator(tokens[0]).Length == 0)
		{
			var only = StripV(tokens[0]);

			if (!NuGetVersion.TryParse(only, out _))
				return null;

			var core = only.Split(['-', '+'], 2)[0];
			return core.Split('.').Length >= 3 || !core.Equals(only, StringComparison.Ordinal)
				? $"[{only}]" : $"{only}.*";
		}

		string lower = null, upper = null, exact = null;
		bool lowerInclusive = false, upperInclusive = false;

		foreach (var raw in tokens)
		{
			var op = Operator(raw);
			var token = op.Length == 0 ? null : StripV(raw[op.Length..]);

			if (token == null || !NuGetVersion.TryParse(token, out _))
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

	private static string Operator(string token)
	{
		foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
			if (token.StartsWith(candidate, StringComparison.Ordinal))
				return candidate;

		return "";
	}

	private static string StripV(string token) => token.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? token[1..] : token;

	private sealed class CollectingLogger : LoggerBase
	{
		internal readonly List<ILogMessage> Messages = [];
		public override void Log(ILogMessage message) { lock (Messages) Messages.Add(message); }
		public override Task LogAsync(ILogMessage message) { Log(message); return Task.CompletedTask; }
	}
}
