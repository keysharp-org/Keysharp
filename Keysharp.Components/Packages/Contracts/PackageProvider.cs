using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Keysharp.Components.Packages;

/// <summary>A provider-neutral package request whose version has been normalized for its provider.</summary>
public sealed record PackageRequest(string Id, string Version);

/// <summary>Everything a provider needs to resolve a graph without consulting the Keysharp engine.</summary>
public sealed record PackageResolveContext(
	string CacheDirectory,
	string SettingsDirectory,
	string TargetFramework,
	string RuntimeIdentifier,
	bool AllowRestore,
	TimeSpan Timeout,
	string Label);

/// <summary>A package in the resolved closure and the selected assets it contributes.</summary>
public sealed class ResolvedPackage
{
	public string Id { get; init; }
	public string Version { get; init; }
	/// <summary>An exact provider-native constraint which reproduces <see cref="Version"/>.</summary>
	public string PinnedVersion { get; init; }
	public string Root { get; init; }
	public List<string> Compile { get; init; } = [];
	public List<string> Runtime { get; init; } = [];
	public List<string> Resources { get; init; } = [];
	public List<string> Native { get; init; } = [];
}

/// <summary>The provider's complete answer.</summary>
public sealed class PackageResolveResult
{
	public bool Success { get; init; }
	public bool RestoreAttempted { get; init; }
	public string Failure { get; init; }
	public List<string> Diagnostics { get; init; } = [];
	public List<ResolvedPackage> Packages { get; init; } = [];
}

/// <summary>Implemented by a package-system component loaded from <c>components/packages/&lt;name&gt;</c>.</summary>
public interface IPackageProvider
{
	string Name { get; }
	string Version { get; }
	bool IsValidPackageId(string id);
	bool TryNormalizeVersion(string written, out string normalized, out string error);
	Task<PackageResolveResult> ResolveAsync(PackageResolveContext context,
		IReadOnlyList<PackageRequest> packages, CancellationToken cancellationToken);
}
