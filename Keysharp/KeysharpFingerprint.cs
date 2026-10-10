namespace Keysharp.Main;

/// <summary>
/// Identifies the exact Keysharp build, used to key the compile-server pipe. A client must only ever
/// talk to a daemon running the SAME Keysharp.exe AND Keysharp.Core.dll, otherwise it could receive
/// assembly bytes compiled against different references/runtime. We fingerprint both modules by their
/// Module Version IDs (MVIDs): the compiler stamps a distinct MVID into every build of an assembly, so
/// any change to either binary changes the fingerprint and a mismatched client simply spawns its own
/// daemon instead of reusing an incompatible one. The compiler component the daemon loads, which a rebuild
/// or update can replace alone, counts too: its Keysharp assemblies by the MVID in their metadata, and its
/// other files by name and size.
/// </summary>
internal static class KeysharpFingerprint
{
	// Null when the compiler component is missing or could not be read, as while an update replaces it: no daemon is
	// used then, so a launch reports a missing compiler at once rather than after waiting for a daemon.
	internal static string Value { get; } = Compute();

	private static string Compute()
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> buf = stackalloc byte[16];

		// typeof(KeysharpFingerprint) lives in Keysharp.dll/exe; typeof(Script) lives in Keysharp.Core.dll.
		_ = typeof(KeysharpFingerprint).Module.ModuleVersionId.TryWriteBytes(buf);
		hash.AppendData(buf);
		_ = typeof(Script).Module.ModuleVersionId.TryWriteBytes(buf);
		hash.AppendData(buf);

		try
		{
			var directory = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, ScriptingComponentRegistry.RelativeDirectory(ScriptingComponentIds.Compiler)));

			foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(file => file.FullName, StringComparer.Ordinal))
			{
				var identity = file.Name.StartsWith("Keysharp.", StringComparison.OrdinalIgnoreCase) && file.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
							   ? ReadMvid(file).ToString() : file.Length.ToString();
				hash.AppendData(Encoding.UTF8.GetBytes($"{Path.GetRelativePath(directory.FullName, file.FullName)}|{identity}\n"));
			}
		}
		catch
		{
			return null;
		}

		return Convert.ToHexString(hash.GetHashAndReset().AsSpan(0, 8)); // 16 hex chars is plenty to avoid collisions.
	}

	private static Guid ReadMvid(FileInfo file)
	{
		using var pe = new PEReader(file.OpenRead());
		var metadata = pe.GetMetadataReader();
		return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
	}
}