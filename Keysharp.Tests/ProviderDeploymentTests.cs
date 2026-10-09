namespace Keysharp.Tests;

[Category("Internal")]
public class ProviderDeploymentTests : TestRunner
{
	[SetUp]
	public void ResetProviders() => Keysharp.Internals.Os.PackageProviderRegistry.ResetForTests();

	[TearDown]
	public void ClearProviders() => Keysharp.Internals.Os.PackageProviderRegistry.ResetForTests();

	[Test]
	public void FullArtifactCopiesOnlyRequiredProviders()
	{
		var root = NewProviderRoot(out var providerRoot);
		var destination = Path.Combine(Path.GetTempPath(), "ks-provider-copy-" + Guid.NewGuid().ToString("N"));

		try
		{
			Keysharp.Internals.Os.PackageProviderRegistry.AddSearchRoot(root);
			Assert.IsTrue(Keysharp.Internals.Os.CompiledPackageProviderManifest.TryBuild(["fake"], out var manifest, out var buildError), buildError);
			Assert.IsNull(manifest.CopyTo(destination));
			var deployedRoot = Path.Combine(destination, "components", "packages", "fake");
			Assert.AreEqual("provider-binary", File.ReadAllText(Path.Combine(deployedRoot, "fake.dll")));
			Assert.AreEqual("nested-payload", File.ReadAllText(Path.Combine(deployedRoot, "data", "payload.bin")));
			Assert.AreEqual(File.ReadAllText(Path.Combine(providerRoot, "provider.json")),
				File.ReadAllText(Path.Combine(deployedRoot, "provider.json")));
			Assert.IsFalse(Directory.Exists(Path.Combine(destination, "providers")),
				"component deployment must not recreate the legacy providers subtree");

			var declarativeOnly = Path.Combine(destination, "declarative-only");
			Assert.IsFalse(Directory.Exists(Path.Combine(declarativeOnly, "components", "packages")),
				"a compilation without imperative provider metadata must not deploy a provider");
			Assert.IsFalse(Directory.Exists(Path.Combine(declarativeOnly, "providers")),
				"a compilation without imperative provider metadata must not deploy a legacy provider subtree");
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
			try { Directory.Delete(destination, true); } catch { }
		}
	}

	[Test]
	public void MinimalArtifactAuthenticatesAndRepairsEmbeddedProvider()
	{
		var root = NewProviderRoot(out _);
		var work = Path.Combine(Path.GetTempPath(), "ks-provider-embed-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(work);
		var script = Path.Combine(work, "embed.ks");
		File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\nClr.LoadPackage(\"fake:demo\",, true)\n");
		string extractedRoot = null;

		try
		{
			Keysharp.Internals.Os.PackageProviderRegistry.AddSearchRoot(root);
			var (bytes, error, compilation) = new CompilerHelper().CompileCodeToByteArray(script, "providerembed", minimalexeout: true);
			Assert.IsNotNull(bytes, error);
			CollectionAssert.Contains(compilation.RequiredProviders, "fake");

			var assembly = Assembly.Load(bytes);
			var manifest = Keysharp.Internals.Os.CompiledPackageProviderManifest.FromAssembly(assembly);
			Assert.IsNotNull(manifest, "a minimal artifact with Clr.LoadPackage must carry its provider manifest");
			Assert.AreEqual(3, manifest.Assets.Count(), "descriptor, provider assembly, and nested payload must all be embedded");
			Assert.IsTrue(manifest.Assets.All(asset =>
				assembly.GetManifestResourceNames().Contains(Keysharp.Internals.Os.CompiledPackageProviderManifest.AssetResourceName(asset))));

			using (var stream = assembly.GetManifestResourceStream(Keysharp.Internals.Os.CompiledPackageProviderManifest.ResourceName))
			using (var reader = new StreamReader(stream))
				Assert.IsFalse(reader.ReadToEnd().Contains(root, StringComparison.OrdinalIgnoreCase),
					"the build machine's provider path must not be serialized into the artifact");

			Keysharp.Internals.Os.PackageProviderRegistry.ResetForTests();
			Assert.IsTrue(Keysharp.Internals.Os.CompiledPackageProviderManifest.TryPrepare(assembly, "fake", out var failure), failure);
			Assert.IsTrue(Keysharp.Internals.Os.PackageProviderRegistry.TryGetPayload("fake", out var payload));
			extractedRoot = Keysharp.Internals.Os.CompiledPackageProviderManifest.GetCacheDirectory(assembly, "fake");
			var expectedProviderRoot = Path.Combine(extractedRoot, "components", "packages", "fake");
			Assert.IsTrue(payload.Root.Equals(expectedProviderRoot, StringComparison.OrdinalIgnoreCase),
				"embedded providers must retain the components/packages/<name> hierarchy in the per-user component cache");
			Assert.IsFalse(Directory.Exists(Path.Combine(extractedRoot, "providers")),
				"embedded extraction must not recreate the legacy providers subtree");

			var nested = Path.Combine(payload.Root, "data", "payload.bin");
			var descriptor = Path.Combine(payload.Root, "provider.json");
			File.WriteAllText(nested, "tampered");
			File.WriteAllText(descriptor, "tampered");
			Assert.IsTrue(Keysharp.Internals.Os.CompiledPackageProviderManifest.TryPrepare(assembly, "fake", out failure), failure);
			Assert.AreEqual("nested-payload", File.ReadAllText(nested), "an existing cache file must be hash-checked and repaired");
			Assert.IsTrue(File.ReadAllText(descriptor).Contains("\"name\":\"fake\""),
				"provider.json is part of the authenticated payload, not trusted as ambient cache state");
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
			try { Directory.Delete(work, true); } catch { }
			try { if (extractedRoot != null) Directory.Delete(extractedRoot, true); } catch { }
		}
	}

	[Test]
	public void BundledNuGetProviderIsAnIsolatedExplicitPayload()
	{
		Assert.IsTrue(Keysharp.Internals.Os.PackageProviderRegistry.TryGetPayload("nuget", out var payload),
			"the test host must ship the NuGet provider under components/packages/nuget");
		Assert.AreEqual(16, payload.Files.Count,
			"provider.json plus its explicit 15-file payload should be the complete provider directory");
		Assert.IsTrue(payload.Files.Any(path => Path.GetFileName(path).Equals("NuGet.Commands.dll", StringComparison.OrdinalIgnoreCase)));
		Assert.IsTrue(payload.Files.Any(path => Path.GetFileName(path).Equals("NuGet.Credentials.dll", StringComparison.OrdinalIgnoreCase)));
		Assert.IsTrue(payload.Files.Any(path => Path.GetFileName(path).Equals("Keysharp.Components.Packages.NuGet.deps.json", StringComparison.OrdinalIgnoreCase)));

		var forbidden = new[] { "Keysharp.Core", "Microsoft.CodeAnalysis", "PCRE", "BitFaster", "Semver", "System.Management" };
		Assert.IsFalse(payload.Files.Any(path => forbidden.Any(name =>
			Path.GetFileName(path).Contains(name, StringComparison.OrdinalIgnoreCase))),
			"the provider payload must not absorb Core's runtime/compiler dependency graph");
		Assert.IsFalse(Directory.GetFiles(AppContext.BaseDirectory, "NuGet*.dll", SearchOption.TopDirectoryOnly).Any(),
			"NuGet implementation DLLs must remain under components/packages/nuget rather than entering the host root");
	}

	[Test, Category("Directives")]
	public void InlineNamesFromPackageConstants()
	{
		var root = NewProviderRoot(out var providerRoot);
		Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();
		Keysharp.Internals.Os.PackageResolver.ResetCounters();
		try
		{
			var source = """
				using System;
				using System.Collections.Generic;
				using System.IO;
				using System.Threading;
				using System.Threading.Tasks;
				using Keysharp.Components.Packages;
				namespace Fake;
				public static class Names { public const string Field = "Renamed"; }
				public sealed class Provider : IPackageProvider
				{
				    public string Name => "fake";
				    public string Version => "1.0";
				    public bool IsValidPackageId(string id) => id == "Names";
				    public bool TryNormalizeVersion(string written, out string normalized, out string error)
				    { normalized = written; error = null; return true; }
				    public Task<PackageResolveResult> ResolveAsync(PackageResolveContext context,
				        IReadOnlyList<PackageRequest> packages, CancellationToken cancellationToken)
				    {
				        var file = typeof(Provider).Assembly.Location;
				        return Task.FromResult(new PackageResolveResult { Success = true, Packages = new()
				        {
				            new() { Id = "Names", Version = "1.0.0", PinnedVersion = "1.0.0",
				                Root = Path.GetDirectoryName(file), Compile = new() { file }, Runtime = new() { file } }
				        }});
				    }
				}
				""";
			var providerCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("InlineNames_" + Guid.NewGuid().ToString("N"),
				[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)], CompilerHelper.CompilationReferences(root, true),
				new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
			using (var file = File.Create(Path.Combine(providerRoot, "fake.dll")))
			{
				var emitted = providerCompilation.Emit(file);
				Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
			}
			Keysharp.Internals.Os.PackageProviderRegistry.AddSearchRoot(root);
			var script = Path.Combine(root, "names.ks");
			File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Package fake:Names 1.0.0\n"
				+ "#Import Typed { Renamed as Alias }\nAlias := 7\nif Typed.ReadRaw() != 7\n    throw Error(\"alias did not reach the original field\")\n"
				+ "FileAppend \"pass\", \"*\"\n#Module Typed\n#CSharp\n"
				+ "[UserDeclaredName(Fake.Names.Field)] public static long RawField = 1;\n"
				+ "public static long ReadRaw() => RawField;\n#EndCSharp\n");
			Assert.AreEqual("pass", RunScript(script, "package_names", true, false).Trim());
			Assert.AreEqual(1, Keysharp.Internals.Os.PackageResolver.ResolveCount,
				"declaration binding and final compilation must share the one resolved package manifest");
		}
		finally
		{
			Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();
			try { Directory.Delete(root, true); } catch { }
		}
	}

	private static string NewProviderRoot(out string providerRoot)
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-provider-source-" + Guid.NewGuid().ToString("N"));
		providerRoot = Path.Combine(root, "components", "packages", "fake");
		_ = Directory.CreateDirectory(Path.Combine(providerRoot, "data"));
		File.WriteAllText(Path.Combine(providerRoot, "fake.dll"), "provider-binary");
		File.WriteAllText(Path.Combine(providerRoot, "data", "payload.bin"), "nested-payload");
		File.WriteAllText(Path.Combine(providerRoot, "provider.json"),
			"{\"name\":\"fake\",\"version\":\"1.0\",\"assembly\":\"fake.dll\",\"type\":\"Fake.Provider\","
			+ "\"files\":[\"fake.dll\",\"data/payload.bin\"]}");
		return root;
	}
}
