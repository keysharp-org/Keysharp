using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using KP = Keysharp.Parsing.Syntax;

namespace Keysharp.Tests
{
	/// <summary>
	/// Covers the `#Package` directive. The compile-time half (validation and, crucially, batching) is tested here
	/// against the lowerer directly; tests which may need a package-feed connection are kept in their own category
	/// and out of the curated CI set.
	/// </summary>
	[Category("Internal")]
	public class PackageDirectiveTests : TestRunner
	{
		/// <summary>
		/// The loader accumulates every package requested in the process (deliberately — see its `requested` field),
		/// so without this the tests below would be order-coupled: one leaving a bad id behind sends every later one
		/// through the optional-drop path and an extra restore.
		/// </summary>
		[SetUp]
		public void ResetPackageState() => Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();

		private static (string Code, List<string> Diags) Lower(string src)
		{
			var (prog, parseDiags) = KP.Parser.ParseWithDiagnostics(src);
			Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
			var lowerer = new KP.Lowerer();
			var unit = lowerer.Build(prog, "nugettest");
			lastPackages = lowerer.Packages;
			return (unit?.ToFullString() ?? "", lowerer.Diagnostics);
		}

		/// <summary>
		/// The package set the last <see cref="Lower"/> collected, rendered in the shape the lowerer USED to emit
		/// into the script. `#Package` now carries its data in a build-time manifest rather than in the generated
		/// call (see PackageManifest), so these assertions read it from here — what they pin is the version
		/// translation, the batching and the order, none of which moved.
		/// </summary>
		private static IReadOnlyList<Keysharp.Internals.Os.PackageResolver.PackageRef> lastPackages;

		private static string Packages() =>
			"LoadPackages(" + string.Join(", ", lastPackages.Select(p => $"(\"{p.Id}\", \"{p.Version}\", {(p.Optional ? "true" : "false")})")) + ")";

		/// <summary>
		/// Asserts the lowered tree contains <paramref name="expected"/>. A synthesized syntax tree carries no
		/// incidental whitespace, so both sides are compared with whitespace removed and the expectation stays
		/// readable rather than being written out unspaced.
		/// </summary>
		private static void AssertEmits(string code, string expected)
		{
			static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "");
			StringAssert.Contains(Strip(expected), Strip(code));
		}

		private static int CountCalls(string code) =>
			System.Text.RegularExpressions.Regex.Matches(code, @"LoadPackages\(").Count;

		[Test, Category("Directives")]
		public void RuntimeProviderMetadata()
		{
			static IReadOnlyCollection<string> Required(string source)
			{
				var (prog, parseDiags) = KP.Parser.ParseWithDiagnostics(source);
				Assert.IsEmpty(parseDiags, "unexpected parse diagnostics: " + string.Join("; ", parseDiags));
				var lowerer = new KP.Lowerer();
				_ = lowerer.Build(prog, "providertest");
				return lowerer.RequiredProviders;
			}

			Assert.IsEmpty(Required("#Package Newtonsoft.Json 13.0.3\n"),
				"declarative packages are resolved at compile time and must not deploy the resolver");
			CollectionAssert.Contains(Required("#import Ks { Clr }\nClr.LoadPackage(\"A\",, true)\n"), "nuget");
			CollectionAssert.Contains(Required("Ks.Clr.LoadPackage(\"A\",, true)\n"), "nuget");
			CollectionAssert.Contains(Required("loader := Clr.LoadPackage\nloader(\"A\")\n"), "nuget",
				"a statically visible member reference must carry its provider even when invoked through an alias");
			CollectionAssert.Contains(Required("Clr.LoadPackage(\"aris:dev/name\")\n"), "aris",
				"a literal provider-qualified id makes the deployment metadata provider-specific");
			Assert.IsFalse(Required("Clr.LoadPackage(\"aris:dev/name\")\n").Contains("nuget"),
				"a known non-NuGet call should not carry an unrelated provider");
			CollectionAssert.Contains(Required("id := \"aris:dev/name\"\nClr.LoadPackage(id)\n"), "nuget",
				"computed package ids use the conservative default provider");
		}

		[Test, Category("Directives")]
		public void MissingId()
		{
			var (_, diags) = Lower("#Package\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("expected a package name", diags[0]);
		}

		[Test, Category("Directives")]
		public void InvalidId()
		{
			var (_, diags) = Lower("#Package Some/Bad;Id 1.0.0\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("is not a valid package name", diags[0]);
		}

		[Test, Category("Directives")]
		public void ProviderQualification()
		{
			var (_, diags) = Lower("#Package nuget:Newtonsoft.Json v13.0\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, lastPackages.Count);
			Assert.AreEqual("nuget", lastPackages[0].Provider);
			Assert.AreEqual("Newtonsoft.Json", lastPackages[0].Id);
			Assert.AreEqual("13.0.*", lastPackages[0].Version);

			(_, diags) = Lower("#Package 9aris:dev/name\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("not a valid package provider name", diags[0]);
		}

		[Test, Category("Directives")]
		public void MissingOptionalProviderIsOptional()
		{
			var (_, diags) = Lower("#Package *i providernotinstalled:dev/name v1\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, lastPackages.Count);
			Assert.AreEqual("providernotinstalled", lastPackages[0].Provider);
			Assert.AreEqual("dev/name", lastPackages[0].Id);
			Assert.AreEqual("v1", lastPackages[0].Version,
				"an absent provider owns the grammar, so its optional request must remain opaque until it is skipped");

			var assemblies = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne(
				"providernotinstalled:dev/name", "v1", true, out var error);
			Assert.IsNull(assemblies);
			Assert.IsNull(error, "Optional runtime packages include an optional provider installation.");
		}

		/// <summary>
		/// Versions may be omitted, partial, exact, or bounded — the shapes `#Requires` accepts — and are translated
		/// to NuGet ranges at compile time. The translation is provider input, not a formatting convenience: every
		/// equivalent AHK spelling must describe the same dependency constraint to the in-process restore engine.
		/// </summary>
		[Test, Category("Directives")]
		public void VersionRanges()
		{
			void Check(string written, string expected)
			{
				var ok = Keysharp.Internals.Os.PackageResolver.TryTranslateVersion(written, out var range, out var err);
				Assert.IsTrue(ok, $"'{written}' -> {err}");
				Assert.AreEqual(expected, range, $"'{written}'");
			}

			Check("", "*");                       // omitted -> newest stable
			Check("13", "13.*");                  // partial -> newest within it
			Check("13.0", "13.0.*");
			Check("13.0.3", "[13.0.3]");          // full -> exact
			Check("1.2.3.4", "[1.2.3.4]");
			Check("1.0.0-beta.1", "[1.0.0-beta.1]");
			Check("v13.0", "13.0.*");             // #Requires-style "v" prefix
			Check("=13.0.3", "[13.0.3]");
			Check(">=13.0", "[13.0,)");
			Check(">13.0", "(13.0,)");
			Check("<14", "(,14)");
			Check("<=14", "(,14]");
			Check(">=13.0 <14", "[13.0,14)");
			Check("13.*", "13.*");                // already floating
			Check("[13.0,14)", "[13.0,14)");      // literal NuGet range

			// Nothing may emit a character that is illegal inside Version="…".
			foreach (var v in new[] { "", "13", "13.0.3", ">=13.0 <14", "<14", "v2" })
			{
				_ = Keysharp.Internals.Os.PackageResolver.TryTranslateVersion(v, out var r, out _);
				Assert.IsFalse(r.Contains('<') || r.Contains('>') || r.Contains('"') || r.Contains('&'), $"'{v}' -> '{r}'");
			}

			Check("[13.0.3]", "[13.0.3]");        // literal exact
			Check("(13.0,)", "(13.0,)");          // literal open upper bound
			Check("*", "*");

			foreach (var bad in new[] { "abc", "1.0 2.0", ">=", "1.0 >=2.0", ">=x", "1..2", "1.",
											 "1.2.3.4.5", "1.0-", "1.0+", "1.0-beta..1" })
				Assert.IsFalse(Keysharp.Internals.Os.PackageResolver.TryTranslateVersion(bad, out _, out _), bad);

			// A literal range is passed through to the provider, so a malformed one has to be caught here rather
			// than surfacing during graph resolution — this method's whole contract is compile-time rejection.
			foreach (var bad in new[] { "[[[", "***", "[]", "[,]", "(1.0)", "[1.0,2.0,3.0]", "[abc,)", "1.*.3", "*.1" })
				Assert.IsFalse(Keysharp.Internals.Os.PackageResolver.TryTranslateVersion(bad, out _, out _), bad);
		}

		[Test, Category("Directives")]
		public void ProviderVersionNormalization()
		{
			void Check(string written, string expected)
			{
				Assert.IsTrue(Keysharp.Internals.Os.PackageResolver.TryNormalizeVersion("NuGet", written,
					out var normalized, out var error), error);
				Assert.AreEqual(expected, normalized);
			}

			Check("v1", "1.*");
			Check("v1.2", "1.2.*");
			Check("v1.2.3", "[1.2.3]");
			Check("v1.2.3-beta.1", "[1.2.3-beta.1]");
			Check(">=v1.2 <v2", "[1.2,2)");
		}

		[Test, Category("Directives")]
		public void ManifestPinsFloatingRequest()
		{
			var manifest = new Keysharp.Internals.Os.PackageManifest();
			manifest.Add(new Keysharp.Internals.Os.PackageResolver.ResolvedPackage
			{
				Provider = "nuget",
				Id = "Example.Package",
				Version = "1.9.4",
				PinnedVersion = "[1.9.4]"
			}, "1.*", false, true);

			var roundTrip = Keysharp.Internals.Os.PackageManifest.Read(manifest.Write());
			Assert.AreEqual("1.*", roundTrip.Packages[0].Requested);
			Assert.AreEqual("[1.9.4]", roundTrip.Direct[0].Version);
			Assert.IsFalse(roundTrip.Write().Contains("\"compile\"", StringComparison.Ordinal),
				"reference assets are build-only and must not be deployed in runtime manifests");
		}

		[Test, Category("Directives")]
		public void ProviderSpecificIdsCannotCollideInDeploymentPaths()
		{
			string DeployedFor(string id)
			{
				var root = Path.Combine(Path.GetTempPath(), "keysharp-provider-path-test");
				var source = Path.Combine(root, "lib", "Example.dll");
				var package = new Keysharp.Internals.Os.PackageResolver.ResolvedPackage
				{
					Provider = "aris", Id = id, Version = "1.0.0", PinnedVersion = "1.0.0", Root = root
				};
				package.Managed.Add(source);
				var manifest = new Keysharp.Internals.Os.PackageManifest();
				manifest.Add(package, "1.0.0", false, true);
				return manifest.Packages.Single().Managed.Single().Deployed;
			}

			Assert.AreNotEqual(DeployedFor("dev/name"), DeployedFor("dev_name"),
				"opaque provider ids must not collide when mapped to filesystem-safe artifact paths");
			Assert.AreNotEqual(DeployedFor("Dev/name"), DeployedFor("dev/name"),
				"Core must not assume an external provider's identifier is case-insensitive");
		}

		[Test, Category("NuGet")]
		public async Task ProviderAudit()
		{
			Assert.IsTrue(Keysharp.Internals.Os.PackageProviderRegistry.TryGet("NuGet", out var provider, out var failure), failure);
			var cache = Path.Combine(Path.GetTempPath(), "keysharp-provider-audit-" + Guid.NewGuid().ToString("N"));

			try
			{
				var context = new Keysharp.Components.Packages.PackageResolveContext(cache, Environment.CurrentDirectory,
					Keysharp.Internals.Os.PackageResolver.TargetFramework, Keysharp.Internals.Os.PackageResolver.RuntimeId,
					true, TimeSpan.FromMinutes(3), "audit test");
				var result = await provider.ResolveAsync(context,
					[new Keysharp.Components.Packages.PackageRequest("Newtonsoft.Json", "[12.0.1]")], CancellationToken.None);
				Assert.IsTrue(result.Success, result.Failure);
				Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("NU190", StringComparison.Ordinal)),
					"NuGetAudit was configured but emitted no NU190x advisory: " + string.Join("; ", result.Diagnostics));
			}
			finally
			{
				try { Directory.Delete(cache, true); } catch { }
			}
		}

		[Test, Category("NuGet")]
		public async Task ProviderWarmCacheValidation()
		{
			Assert.IsTrue(Keysharp.Internals.Os.PackageProviderRegistry.TryGet("nuget", out var provider, out var failure), failure);
			var root = Path.Combine(Path.GetTempPath(), "keysharp-provider-cache-" + Guid.NewGuid().ToString("N"));
			var cache = Path.Combine(root, "cache");
			Directory.CreateDirectory(root);
			var config = Path.Combine(root, "NuGet.Config");
			File.WriteAllText(config, "<configuration><packageSources><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>");

			try
			{
				var context = new Keysharp.Components.Packages.PackageResolveContext(cache, root,
					Keysharp.Internals.Os.PackageResolver.TargetFramework, Keysharp.Internals.Os.PackageResolver.RuntimeId,
					true, TimeSpan.FromMinutes(3), "cache test");
				var request = new[] { new Keysharp.Components.Packages.PackageRequest("Newtonsoft.Json", "[13.0.3]") };
				var cold = await provider.ResolveAsync(context, request, CancellationToken.None);
				Assert.IsTrue(cold.Success, cold.Failure);
				Assert.IsTrue(cold.RestoreAttempted);

				var warm = await provider.ResolveAsync(context, request, CancellationToken.None);
				Assert.IsTrue(warm.Success, warm.Failure);
				Assert.IsFalse(warm.RestoreAttempted, "a complete, matching lock graph should be served before RestoreRunner");

				var assetsPath = Directory.GetFiles(cache, "project.assets.json", SearchOption.AllDirectories).Single();
				var assets = File.ReadAllText(assetsPath);
				StringAssert.Contains("Newtonsoft.Json.dll", assets);
				File.WriteAllText(assetsPath, assets.Replace("Newtonsoft.Json.dll", "Newtonsoft.Json.missing.dll", StringComparison.Ordinal));
				var repaired = await provider.ResolveAsync(context, request, CancellationToken.None);
				Assert.IsTrue(repaired.Success, repaired.Failure);
				Assert.IsTrue(repaired.RestoreAttempted, "a lock graph selecting a missing asset must be restored, not accepted as warm");

				File.AppendAllText(config, "<!-- fingerprint change -->");
				var reconfigured = await provider.ResolveAsync(context, request, CancellationToken.None);
				Assert.IsTrue(reconfigured.Success, reconfigured.Failure);
				Assert.IsTrue(reconfigured.RestoreAttempted, "NuGet.Config content changes must invalidate the provider cache stamp");
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, Category("NuGet")]
		public void ProviderRestoreLockHonorsCancellation()
		{
			Assert.IsTrue(Keysharp.Internals.Os.PackageProviderRegistry.TryGet("nuget", out var provider, out var failure), failure);
			var root = Path.Combine(Path.GetTempPath(), "keysharp-provider-lock-" + Guid.NewGuid().ToString("N"));
			var cache = Path.Combine(root, "cache");
			Directory.CreateDirectory(cache);

			try
			{
				using var held = new FileStream(Path.Combine(cache, "restore.lock"), FileMode.OpenOrCreate,
					FileAccess.ReadWrite, FileShare.None);
				using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
				var context = new Keysharp.Components.Packages.PackageResolveContext(cache, root,
					Keysharp.Internals.Os.PackageResolver.TargetFramework, Keysharp.Internals.Os.PackageResolver.RuntimeId,
					true, TimeSpan.FromMinutes(3), "lock test");

				Assert.CatchAsync<OperationCanceledException>(async () => await provider.ResolveAsync(context,
					[new Keysharp.Components.Packages.PackageRequest("Newtonsoft.Json", "[13.0.3]")], timeout.Token));
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, Category("Directives")]
		public void MalformedVersion()
		{
			var (_, diags) = Lower("#Package Newtonsoft.Json 13.0.3 extra\n");
			Assert.AreEqual(1, diags.Count, string.Join("; ", diags));
			StringAssert.Contains("is not a valid version", diags[0]);
		}

		[Test, Category("Directives")]
		public void VersionConflict()
		{
			var (_, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package Newtonsoft.Json 12.0.0\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("requested twice with different versions", diags[0]);
		}

		[Test, Category("Directives")]
		public void DuplicatePackage()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package nuget:Newtonsoft.Json 13.0.3\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, lastPackages.Count, "default and explicit NuGet spellings are one provider-qualified identity");
			AssertEmits(Packages(), "LoadPackages((\"Newtonsoft.Json\", \"[13.0.3]\", false))");
		}

		/// <summary>
		/// Having its own directive is what keeps package names out of `#Requires`'s first-token space, where
		/// `MapRequires` matches the version form with StartsWith — a package literally named `Keysharp.Extensions`
		/// would otherwise have been read as a compatibility-version declaration.
		/// </summary>
		[Test, Category("Directives")]
		public void RequiresSeparation()
		{
			var (code, diags) = Lower("#Package Keysharp.Extensions 1.2.3\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			AssertEmits(Packages(), "LoadPackages((\"Keysharp.Extensions\", \"[1.2.3]\", false))");

			// The #Requires forms still lower to their own things, and never to a package load.
			foreach (var other in new[] { "#Requires AutoHotkey v2.0\n", "#Requires Keysharp v2.1\n", "#Requires capability ScreenCapture\n" })
			{
				var (c, d) = Lower(other);
				Assert.IsEmpty(d, other + " -> " + string.Join("; ", d));
				Assert.AreEqual(0, CountCalls(c), other + " must not load a package");
			}
		}

		/// <summary>
		/// A package requirement is program-wide, so one buried in a function body cannot be honoured at the point it
		/// appears. It must be reported rather than silently vanishing.
		/// </summary>
		[Test, Category("Directives")]
		public void NestedPackage()
		{
			var (_, diags) = Lower("f() {\n#Package Newtonsoft.Json 13.0.3\n}\nf()\n");
			Assert.AreEqual(1, diags.Count, string.Join("; ", diags));
			StringAssert.Contains("top level", diags[0]);
		}

		/// <summary>
		/// The same requirement written both at the top level and inside a function. The nested one must still be
		/// reported: if misplacement were judged by the directive's TEXT rather than by which node the prescan
		/// visited, the nested copy would pass for its top-level twin and the whole batch would be emitted from
		/// inside the function body — i.e. only when that function is called, and on its thread.
		/// </summary>
		[Test, Category("Directives")]
		public void NestedDuplicate()
		{
			var (_, diags) = Lower("f() {\n#Package Newtonsoft.Json 13.0.3\n}\n#Package Newtonsoft.Json 13.0.3\nf()\n");
			Assert.AreEqual(1, diags.Count, string.Join("; ", diags));
			StringAssert.Contains("top level", diags[0]);
		}

		/// <summary>
		/// Packages are program-wide, so they load from ONE fixed position: generated Main, before RunMainWindow
		/// starts the auto-exec — not at the position of the directive that happened to declare them (modules execute
		/// in dependency order, so an imported module's top-level code could run against packages not loaded yet),
		/// and not at the top of the outer auto-exec either: JITting that method resolves the module classes it calls
		/// into, and a `static` field of a package STRUCT type in a `#CSharp` block forces the package assembly to
		/// resolve before the method's own first statement has run.
		/// </summary>
		[Test, Category("Directives")]
		public void PackageLoadOrder()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\nx := 1\n#Module Helper\ny := 2\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, CountCalls(code));
			var load = code.IndexOf("LoadPackages(", StringComparison.Ordinal);
			// Before the call that starts (and therefore JITs) the auto-exec…
			var runWindow = code.IndexOf("RunMainWindow", StringComparison.Ordinal);
			Assert.IsTrue(runWindow >= 0, "expected Main to start the script via RunMainWindow; generated:\n" + code);
			Assert.Less(load, runWindow, "packages must load before RunMainWindow JITs the auto-exec; generated:\n" + code);
			// …and so before any module's own auto-exec statements.
			var firstAutoExec = System.Text.RegularExpressions.Regex.Match(code, @"Program\.\w+\.AutoExecSection\(\)");
			Assert.IsTrue(firstAutoExec.Success, "expected the outer auto-exec to drive each module; generated:\n" + code);
			Assert.Less(load, firstAutoExec.Index,
						"packages must load before the first module auto-exec; generated:\n" + code);
		}

		/// <summary>
		/// A NuGet restore that FAILS can still write a complete, well-formed project.assets.json describing whichever
		/// packages did resolve. Trusting that file would turn a hard "package not found" error into a silently
		/// missing package on every subsequent run — loud once, then never again. NuGet's own verdict lives in
		/// project.nuget.cache beside it, and that is what gates the cache hit.
		/// </summary>
		[Test, Category("Directives")]
		public void FailedRestoreCache()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks-restore-verdict", Guid.NewGuid().ToString("N"));
			var obj = Path.Combine(dir, "obj");
			_ = Directory.CreateDirectory(obj);
			var cache = Path.Combine(obj, "project.nuget.cache");

			// No project.nuget.cache at all: an interrupted restore, which must re-run rather than be trusted.
			Assert.IsFalse(Keysharp.Internals.Os.PackageResolver.RestoreSucceeded(dir));

			File.WriteAllText(cache, "{\"version\":2,\"success\":false,\"expectedPackageFiles\":[],\"logs\":[]}");
			Assert.IsFalse(Keysharp.Internals.Os.PackageResolver.RestoreSucceeded(dir),
						   "a restore NuGet itself recorded as failed must not count as a cache hit");

			File.WriteAllText(cache, "{\"version\":2,\"success\":true,\"expectedPackageFiles\":[],\"logs\":[]}");
			Assert.IsTrue(Keysharp.Internals.Os.PackageResolver.RestoreSucceeded(dir));

			File.WriteAllText(cache, "{ not json");
			Assert.IsFalse(Keysharp.Internals.Os.PackageResolver.RestoreSucceeded(dir));

			try { Directory.Delete(dir, true); } catch { }
		}

		/// <summary>
		/// A directive nobody handles is rejected rather than dropped: dropping it runs the script with the setting
		/// silently absent, so the failure surfaces somewhere unrelated. Deprecated AutoHotkey v1 directives are in
		/// that set deliberately — Keysharp targets v2, and ignoring a v1 leftover hides a porting bug.
		/// </summary>
		[Test, Category("Directives")]
		public void UnknownDirectives()
		{
			foreach (var bad in new[]
			{
				"#Packge Newtonsoft.Json 1.0\n", "#NuGet Newtonsoft.Json 1.0\n", "#TotallyMadeUp x\n",
				// AutoHotkey v1 directives, deliberately not carried over.
				"#InstallMouseHook\n", "#InstallKeybdHook\n", "#NoEnv\n", "#LTrim\n", "#MaxMem 64\n",
				"#KeyHistory 0\n", "#HotkeyInterval 2000\n",
			})
			{
				var (_, diags) = Lower(bad);
				Assert.AreEqual(1, diags.Count, bad + " -> " + string.Join("; ", diags));
				StringAssert.Contains("Unrecognized directive", diags[0]);
			}
		}

		/// <summary>
		/// Every directive reaches the runtime as its own `(id, version, optional)` tuple, in source order. The
		/// pieces are never packed into a string, so this pins the whole lowerer-to-loader contract — there is no
		/// separate format for the two ends to disagree about.
		/// </summary>
		[Test, Category("Directives")]
		public void Specs()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package *i Serilog >=4.0 <5\n#Package A.B\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, CountCalls(code), "expected exactly one batched call");
			AssertEmits(Packages(), "LoadPackages((\"Newtonsoft.Json\", \"[13.0.3]\", false), (\"Serilog\", \"[4.0,5)\", true), (\"A.B\", \"*\", false))");
		}

		/// <summary>
		/// Reading a NuGet assets file is retained for cache compatibility and package-manifest fixtures. A
		/// live restore is otherwise the only way to exercise. A fixture pins the shapes that matter: RID-suffixed
		/// target selection, the `_._` placeholder, and native assets landing separately from managed ones.
		/// </summary>
		[Test, Category("Directives")]
		public void AssetsFile()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-assets-fixture", Guid.NewGuid().ToString("N"));
			var pkgDir = Path.Combine(root, "packages", "demo.pkg", "1.0.0");
			_ = Directory.CreateDirectory(Path.Combine(pkgDir, "lib", "net6.0"));
			_ = Directory.CreateDirectory(Path.Combine(pkgDir, "runtimes", "win-x64", "native"));
			File.WriteAllText(Path.Combine(pkgDir, "lib", "net6.0", "Demo.dll"), "");
			File.WriteAllText(Path.Combine(pkgDir, "lib", "net6.0", "_._"), "");
			File.WriteAllText(Path.Combine(pkgDir, "runtimes", "win-x64", "native", "demo_native.dll"), "");
			var tfm = Keysharp.Internals.Os.PackageResolver.TargetFramework;
			var rid = Keysharp.Internals.Os.PackageResolver.RuntimeId;
			var assets = Path.Combine(root, "obj", "project.assets.json");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(assets));
			File.WriteAllText(assets, $$"""
			{
			  "targets": {
			    "{{tfm}}": { "demo.pkg/1.0.0": { "runtime": { "lib/net6.0/Demo.dll": {} } } },
			    "{{tfm}}/{{rid}}": { "demo.pkg/1.0.0": {
			        "runtime": { "lib/net6.0/Demo.dll": {}, "lib/net6.0/_._": {} },
			        "native":  { "runtimes/win-x64/native/demo_native.dll": {} } } }
			  },
			  "libraries": { "demo.pkg/1.0.0": { "type": "package", "path": "demo.pkg/1.0.0" } },
			  "packageFolders": { "{{Path.Combine(root, "packages").Replace("\\", "\\\\")}}": {} }
			}
			""");
			var read = Keysharp.Internals.Os.PackageResolver.TryReadAssets(assets);
			Assert.IsNotNull(read, "fixture should parse");
			Assert.AreEqual(1, read.Count);
			// The RID-qualified target wins, `_._` is dropped, and native assets do not land among the managed ones.
			Assert.AreEqual(1, read[0].Managed.Count, "managed: " + string.Join(", ", read[0].Managed));
			StringAssert.EndsWith("Demo.dll", read[0].Managed[0]);
			Assert.AreEqual(1, read[0].Native.Count);
			StringAssert.EndsWith("demo_native.dll", read[0].Native[0]);
			var manifest = new Keysharp.Internals.Os.PackageManifest();
			manifest.Add(read[0], "[1.0.0]", false, true);
			StringAssert.EndsWith(Path.Combine("managed", "lib", "net6.0", "Demo.dll"), manifest.Packages[0].Managed[0].Deployed);
			StringAssert.EndsWith(Path.Combine("native", "runtimes", "win-x64", "native", "demo_native.dll"),
				manifest.Packages[0].Native[0].Deployed);

			// A file the assets list names but which is gone means the shared package folder was cleared: the whole
			// entry is stale and must force a fresh restore rather than half-load.
			File.Delete(Path.Combine(pkgDir, "lib", "net6.0", "Demo.dll"));
			Assert.IsNull(Keysharp.Internals.Os.PackageResolver.TryReadAssets(assets));
			try { Directory.Delete(root, true); } catch { }
		}

		/// <summary>A P/Invoke names a native library in several ways; all of them have to find the one file.</summary>
		[Test, Category("Directives")]
		public void NativeAliases()
		{
			// Composed with Path.Combine rather than written as a literal Windows path: a native asset path always
			// reaches this function in the host platform's own separator style, and '\' is not a separator on Unix —
			// a hard-coded "C:\...\e_sqlite3.dll" makes GetFileName there return the whole string, not the file name.
			var dll = Keysharp.Internals.Os.NuGetPackageLoader.NativeAliasesFor(
				Path.Combine("p", "runtimes", "win-x64", "native", "e_sqlite3.dll"));
			CollectionAssert.AreEquivalent(new[] { "e_sqlite3.dll", "e_sqlite3" }, dll);

			// On Unix the "lib" prefix and the version suffix are both conventionally omitted in DllImport.
			var so = Keysharp.Internals.Os.NuGetPackageLoader.NativeAliasesFor("/p/runtimes/linux-x64/native/libfoo.so.1");
			CollectionAssert.AreEquivalent(new[] { "libfoo.so.1", "libfoo.so", "libfoo", "foo" }, so);
		}

		[Test, Category("Directives")]
		public void DuplicateBasenames()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-package-assets", Guid.NewGuid().ToString("N"));
			var first = Path.Combine(root, "source-a", "Shared.dll");
			var second = Path.Combine(root, "source-b", "Shared.dll");
			var output = Path.Combine(root, "output");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(first));
			_ = Directory.CreateDirectory(Path.GetDirectoryName(second));
			File.WriteAllText(first, "first");
			File.WriteAllText(second, "second");

			try
			{
				var manifest = new Keysharp.Internals.Os.PackageManifest();
				var a = new Keysharp.Internals.Os.PackageResolver.ResolvedPackage { Id = "Package.A", Version = "1.0.0" };
				var b = new Keysharp.Internals.Os.PackageResolver.ResolvedPackage { Id = "Package.B", Version = "2.0.0" };
				a.Managed.Add(first);
				b.Managed.Add(second);
				manifest.Add(a, "[1.0.0]", false, true);
				manifest.Add(b, "[2.0.0]", false, true);

				Assert.IsNull(manifest.CopyTo(output));
				var deployed = Directory.GetFiles(output, "Shared.dll", SearchOption.AllDirectories);
				Assert.AreEqual(2, deployed.Length, "same-named assets from different packages must not overwrite each other");
				CollectionAssert.AreEquivalent(new[] { "first", "second" }, deployed.Select(File.ReadAllText).ToArray());
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		/// <summary>The cache key must not depend on the order packages were written in, or every reorder re-restores.</summary>
		[Test, Category("Directives")]
		public void CacheKey()
		{
			static string Key(params (string, string)[] p) =>
				Keysharp.Internals.Os.PackageResolver.CacheKeyFor(
					p.Select(x => new Keysharp.Internals.Os.PackageResolver.PackageRef(x.Item1, x.Item2, false)).ToList());

			Assert.AreEqual(Key(("A", "[1.0]"), ("B", "[2.0]")), Key(("B", "[2.0]"), ("A", "[1.0]")));
			Assert.AreNotEqual(Key(("A", "[1.0]")), Key(("A", "[1.1]")));
			Assert.AreNotEqual(Key(("A", "[1.0]")), Key(("A", "[1.0]"), ("B", "[2.0]")));
		}

		/// <summary>Win-modifier hotkeys start with '#' too; they must never be mistaken for directives.</summary>
		[Test, Category("Directives")]
		public void WinHotkeys()
		{
			var (_, diags) = Lower("#c::MsgBox 'win-c'\n#z::MsgBox 'win-z'\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
		}

		private static Assembly Loaded(string simpleName) =>
			AppDomain.CurrentDomain.GetAssemblies()
					 .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// The runtime form. Its accumulate-then-re-resolve behaviour is the mitigation for the hazard that makes the
		/// directive preferable: two independent resolutions could each pick a different version of a shared
		/// dependency, so every call resolves the union of everything requested so far.
		/// </summary>
		[Test, Category("NuGet")]
		public void LoadPackage()
		{
			var pkg = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "13.0.3", false, out var error);
			Assert.IsNull(error, error);
			Assert.IsNotNull(pkg);
			Assert.IsTrue(pkg.Any(a => string.Equals(a.GetName().Name, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase)));

			// Same package/version again is a no-op, not a second resolution.
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "13.0.3", false, out var again);
			Assert.IsNull(again, again);

			// A different version for an already-requested package is reported rather than silently loading a second copy.
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "12.0.3", false, out var conflict);
			Assert.IsNotNull(conflict);
			StringAssert.Contains("already requested", conflict);

			// An unavailable optional package yields no assemblies and no error.
			var opt = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Keysharp.No.Such.Package.XYZ", "1.0.0", true, out var optErr);
			Assert.IsNull(opt);
			Assert.IsNull(optErr, optErr);
		}

		/// <summary>
		/// A REQUIRED package that does not exist must be reported and must name itself. The provider is already
		/// installed locally, so a plain "package not found" must not turn into advice about unrelated tools. The failed
		/// request must also roll back, so a later good call is not poisoned by a set that is known to fail.
		/// </summary>
		[Test, Category("NuGet")]
		public void RequiredPackageFailure()
		{
			var missing = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Keysharp.No.Such.Package.XYZ", "1.0.0", false, out var error);
			Assert.IsNull(missing);
			Assert.IsNotNull(error, "a required package that cannot be resolved must be reported");
			StringAssert.Contains("Keysharp.No.Such.Package.XYZ", error);
			StringAssert.Contains("Clr.LoadPackage", error);   // not "#Package": this script contains no directive
			Assert.IsFalse(error.Contains("dotnet.microsoft.com"),
						   "a package that does not exist is not an SDK problem; error was:\n" + error);

			// Rolled back, so the next request resolves on its own merits rather than dragging the bad id along.
			var good = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "13.0.3", false, out var goodErr);
			Assert.IsNull(goodErr, goodErr);
			Assert.IsNotNull(good);
		}

		/// <summary>
		/// However many packages a script declares, the COMPILER resolves them as ONE graph, exactly once — the
		/// batching now happens where the resolution moved. Resolving them one at a time would let a later resolution
		/// pick a version of a shared dependency that differs from an earlier one's, which .NET cannot unload.
		/// Counting resolutions rather than restores is deliberate: it holds whether the cache is warm or cold.
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void BatchedRestore()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgbatch_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);
			// Counters accumulate per process, so zero them here rather than trusting this test to run first.
			Keysharp.Internals.Os.PackageResolver.ResetCounters();

			try
			{
				var script = Path.Combine(dir, "batch.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#Package Newtonsoft.Json 13.0.3\n#Package Serilog 4.0.0\nx := 1\n");
				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(script, "batch");
				Assert.IsNotNull(arr, "compile failed:\n" + code);
				Assert.AreEqual(1, Keysharp.Internals.Os.PackageResolver.ResolveCount,
								"two directives in one script must resolve as one graph, not once each");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}

			// The contrast: the imperative runtime form cannot batch, so each call resolves again — over the UNION,
			// which is what keeps it correct despite not being able to.
			Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "13.0.3", false, out _);
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Serilog", "4.0.0", false, out _);
			Assert.AreEqual(2, Keysharp.Internals.Os.PackageResolver.ResolveCount);
		}

		/// <summary>
		/// The feature's headline promise: a feed is needed only the FIRST time a package set is used on a machine.
		/// Every later run reads the graph the in-process provider already wrote, spawning no subprocess.
		/// Nothing else observes that — a regression would just make startup quietly slow.
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void WarmCache()
		{
			var pkgs = new List<Keysharp.Internals.Os.PackageResolver.PackageRef> { new("Newtonsoft.Json", "[13.0.3]", false) };
			// Warm the set (this may or may not restore, depending on what earlier tests left in the on-disk cache).
			Assert.IsTrue(Keysharp.Internals.Os.PackageResolver.TryResolve(pkgs, true, "#Package", out _, out var err), err);
			// Zeroes the counters AND the in-process memo, so the second call must go back to DISK — which is the
			// cache this test exists to pin. Only the network/restore step may be skipped.
			Keysharp.Internals.Os.PackageResolver.ResetCounters();
			Assert.IsTrue(Keysharp.Internals.Os.PackageResolver.TryResolve(pkgs, true, "#Package", out _, out err), err);
			Assert.AreEqual(0, Keysharp.Internals.Os.PackageResolver.RestoreCount,
							"a package set already resolved on this machine must not invoke the provider restore again");
		}

		[Test, Category("Directives")]
		public void LoadPackageValidation()
		{
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Some/Bad;Id", "", false, out var e1);
			StringAssert.Contains("not a valid package name", e1);

			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "not a version", false, out var e2);
			StringAssert.Contains("not a valid version", e2);
		}

		/// <summary>
		/// End-to-end: actually resolves and loads a package. It may need the network on the first run for a given
		/// package set, so it is deliberately outside the curated CI categories.
		/// </summary>
		[Test, Category("NuGet")]
		public void PackageRestore()
		{
			// The canonical spelling the lowerer emits. It matters that every test in this fixture agrees on it: the
			// requested-package set is process-wide by design (loaded assemblies cannot be unloaded), so a different
			// spelling here would be reported as a version conflict with whatever ran first.
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "[13.0.3]", false, out _);
			var loaded = Loaded("Newtonsoft.Json");
			Assert.IsNotNull(loaded, "Newtonsoft.Json was not loaded");
			Assert.AreEqual(new Version(13, 0, 0, 0), loaded.GetName().Version);
			// The point of loading is that Clr can reach the types, which goes through TypeResolver's assembly index.
			Assert.IsNotNull(loaded.GetType("Newtonsoft.Json.JsonConvert"));
		}

		/// <summary>
		/// Both executable forms run from the graph pinned on the build machine. The full form deploys every package
		/// asset under its package/version-scoped path; the minimal form embeds and extracts the same paths.
		/// </summary>
		[TestCase("exe"), TestCase("exe-min"), Category("NuGet")]
		public void CompiledPackages(string mode)
		{
			// Driven through the real launcher rather than TestRunner's exeout: that path emits a bare assembly with
			// no runtimeconfig.json (fine for the reflection AsmInfo does, not runnable), and running it is the whole
			// point here.
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			// Emptied first and never the build output directory, so no stale deployment can satisfy the assertions.
			var dir = Path.Combine(Path.GetTempPath(), "ks-package-compile", mode, Guid.NewGuid().ToString("N"));

			if (Directory.Exists(dir))
				Directory.Delete(dir, true);

			_ = Directory.CreateDirectory(dir);
			var script = Path.Combine(dir, "compiled.ks");
			File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\n#Package Newtonsoft.Json 13.0.3\n#import \"Ks\" { Clr }\n"
									  + "FileAppend(Clr.Newtonsoft.Json.JsonConvert.SerializeObject([1, 2, 3]), \"*\")\nExitApp()\n");
			Assert.AreEqual(0, Run(launcher, $"--errorstdout --compile {mode} \"{script}\"", out _, out var cerr), "compile failed: " + cerr);
			var exe = Path.ChangeExtension(script, ".exe");
			Assert.IsTrue(File.Exists(exe), $"compile produced no exe at {exe}");

			// Each mode must work for its own reason: embedded resources versus private deployed assets.
			var siblings = Directory.GetFiles(dir, "*.dll")
									.Select(Path.GetFileName)
									.Where(f => !f.Equals("Keysharp.Core.dll", StringComparison.OrdinalIgnoreCase)
												&& !f.Equals(Path.GetFileName(Path.ChangeExtension(script, ".dll")), StringComparison.OrdinalIgnoreCase))
									.ToArray();

			var scriptAssembly = Assembly.Load(File.ReadAllBytes(Path.ChangeExtension(script, ".dll")));
			var manifest = Keysharp.Internals.Os.PackageManifest.FromAssembly(scriptAssembly);
			Assert.IsNotNull(manifest, "the generated assembly must carry its pinned package manifest");
			var embeddedPackages = scriptAssembly.GetManifestResourceNames()
										 .Where(n => n.StartsWith(Keysharp.Internals.Os.PackageManifest.AssetResourcePrefix, StringComparison.Ordinal))
										 .ToArray();
			var deployedRoot = Path.Combine(dir, ".keysharp", "packages");

			if (mode == "exe-min")
			{
				Assert.IsEmpty(siblings, "exe-min should embed its dependencies, but these were copied alongside: " + string.Join(", ", siblings));
				Assert.IsNotEmpty(embeddedPackages, "exe-min must embed its resolved package assets");
				Assert.IsFalse(Directory.Exists(deployedRoot), "exe-min must not need a package sidecar directory");
				Assert.IsTrue(manifest.TryLocate(scriptAssembly, out var located, out var missing), missing);
				// The cache lives under the PER-USER profile, never the world-writable temp directory: TryExtract's
				// pre-existing-file fast path trusts what it finds, which in a shared directory another local user
				// could have planted.
				var extractRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
											   "Keysharp", "embedded-packages");
				Assert.IsTrue(located.SelectMany(p => p.Managed.Concat(p.Native)).All(path =>
					path.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase)),
					"exe-min must resolve every package asset from its per-user embedded cache");
			}
			else
			{
				Assert.IsNotEmpty(siblings, "exe should copy its dependencies alongside the executable");
				Assert.IsEmpty(embeddedPackages, "the full executable should deploy package assets rather than duplicate them as resources");
				Assert.IsTrue(Directory.Exists(deployedRoot)
								&& Directory.GetFiles(deployedRoot, "*.dll", SearchOption.AllDirectories).Length != 0,
							  "the full executable must carry package assets in its private hierarchy");
				Assert.IsTrue(manifest.Assets.All(asset => File.Exists(Path.Combine(dir, asset.Deployed))),
					"every manifest asset must be present at its collision-free deployed path");
			}

			_ = Run(exe, "", out var stdout, out var stderr);
			Assert.AreEqual("[1,2,3]", stdout.Trim(), "stderr: " + stderr);
			try { Directory.Delete(dir, true); } catch { }
		}

		/// <summary>
		/// Imperative package loading is the one compiled-script path which still needs a provider at runtime. A full
		/// executable carries the provider as a loose subtree; exe-min authenticates an embedded copy and extracts it
		/// lazily before the call. Neither form puts NuGet implementation assemblies in the application root.
		/// </summary>
		[TestCase("exe"), TestCase("exe-min"), Category("NuGet"), NonParallelizable]
		public void CompiledLoadPackageCarriesProvider(string mode)
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks-runtime-provider", mode, Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "runtime-provider.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\n#Import Ks { Clr }\n"
					+ "Clr.LoadPackage(\"Newtonsoft.Json\", \"v13.0.3\")\n"
					+ "FileAppend(Clr.Newtonsoft.Json.JsonConvert.SerializeObject([4, 5]), \"*\")\nExitApp()\n");
				Assert.AreEqual(0, Run(launcher, $"--errorstdout --compile {mode} \"{script}\"", out _, out var compileError),
					"compile failed: " + compileError);

				var exe = Path.ChangeExtension(script, ".exe");
				var assembly = Assembly.Load(File.ReadAllBytes(Path.ChangeExtension(script, ".dll")));
				var resources = assembly.GetManifestResourceNames();
				Assert.IsFalse(Directory.GetFiles(dir, "NuGet*.dll", SearchOption.TopDirectoryOnly).Any(),
					"NuGet implementation assemblies belong under components/packages/nuget, never beside the application");

				if (mode == "exe")
				{
					var providerRoot = Path.Combine(dir, "components", "packages", "nuget");
					Assert.IsTrue(File.Exists(Path.Combine(providerRoot, "provider.json")));
					Assert.IsTrue(File.Exists(Path.Combine(providerRoot, "NuGet.Commands.dll")));
					Assert.IsFalse(resources.Contains(Keysharp.Internals.Os.CompiledPackageProviderManifest.ResourceName),
						"the full form deploys its provider and must not duplicate it in the script assembly");
				}
				else
				{
					Assert.IsFalse(Directory.Exists(Path.Combine(dir, "components", "packages")),
						"exe-min must not require a provider sidecar");
					Assert.IsTrue(resources.Contains(Keysharp.Internals.Os.CompiledPackageProviderManifest.ResourceName));
					Assert.IsTrue(resources.Any(name => name.StartsWith(
						Keysharp.Internals.Os.CompiledPackageProviderManifest.AssetResourcePrefix, StringComparison.Ordinal)));
				}

				Assert.IsFalse(Directory.Exists(Path.Combine(dir, "providers")),
					"compiled artifacts must not recreate the legacy providers subtree");

				Assert.AreEqual(0, Run(exe, "", out var stdout, out var stderr), stderr);
				Assert.AreEqual("[4,5]", stdout.Trim(), "stderr: " + stderr);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>
		/// `--compile asm` (a `.cks` beside the script, run later by the installed launcher) deploys package assets
		/// exactly as the full exe does: the launcher runs the `.cks` from the script's directory, so its packages
		/// must sit in the private `.keysharp/packages` hierarchy beside it (Runner.CopyPackageAssemblies).
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void CompiledCksPackages()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks-package-cks", Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "sidecar.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#Package Newtonsoft.Json 13.0.3\nx := 1\nExitApp()\n");
				Assert.AreEqual(0, Run(launcher, $"--errorstdout --compile asm \"{script}\"", out _, out var cerr), "compile failed: " + cerr);
				Assert.IsTrue(File.Exists(Path.ChangeExtension(script, ".cks")), "no .cks was produced");
				var deployed = Path.Combine(dir, ".keysharp", "packages");
				Assert.IsTrue(Directory.Exists(deployed)
							  && Directory.GetFiles(deployed, "*.dll", SearchOption.AllDirectories).Length != 0,
							  "the .cks must carry its package assets in the private hierarchy beside it");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		[Test, Category("NuGet"), NonParallelizable]
		public void CompiledCksUsesLauncherProviderWithoutSidecarCopy()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var dir = Path.Combine(Path.GetTempPath(), "ks-provider-cks", Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "provider-sidecar.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\n#Import Ks { Clr }\n"
					+ "Clr.LoadPackage(\"nuget:Newtonsoft.Json\", \"v13.0.3\")\n"
					+ "FileAppend(Clr.Newtonsoft.Json.JsonConvert.SerializeObject([6, 7]), \"*\")\nExitApp()\n");
				Assert.AreEqual(0, Run(launcher, $"--errorstdout --compile asm \"{script}\"", out _, out var error), error);
				var compiled = Path.ChangeExtension(script, ".cks");
				Assert.IsTrue(File.Exists(compiled));
				Assert.IsFalse(Directory.Exists(Path.Combine(dir, "components", "packages")),
					"a .cks already requires the Keysharp launcher and must not duplicate its provider payload");
				Assert.IsFalse(Directory.Exists(Path.Combine(dir, "providers")),
					"a .cks must not recreate the legacy providers subtree");
				Assert.AreEqual(0, Run(launcher, $"--errorstdout \"{compiled}\"", out var stdout, out error), error);
				Assert.AreEqual("[6,7]", stdout.Trim(), "stderr: " + error);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		private static int Run(string exe, string args, out string stdout, out string stderr)
		{
			using var proc = Process.Start(new ProcessStartInfo(exe, args)
			{
				RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
			});
			var o = proc.StandardOutput.ReadToEndAsync();
			var e = proc.StandardError.ReadToEndAsync();

			if (!proc.WaitForExit(240000))
			{
				try { proc.Kill(true); } catch { }

				Assert.Fail($"{Path.GetFileName(exe)} did not exit within 240s");
			}

			stdout = o.GetAwaiter().GetResult();
			stderr = e.GetAwaiter().GetResult();
			return proc.ExitCode;
		}

		/// <summary>
		/// A dependency the script never named must be (a) resolvable by type name and (b) not loaded until it is.
		/// Both halves matter: (a) is what the resolving hook alone cannot do â€” it fires on an assembly-name miss, and a
		/// lookup by TYPE name never gets that far â€” and (b) is the whole point of deferring at all. SQLitePCLRaw.core is
	/// a transitive dependency of the bundle package.
		/// </summary>
		[Test, Category("NuGet")]
		public void LazyDependency()
		{
			Assert.IsNull(Loaded("SQLitePCLRaw.core"), "test precondition: the dependency must not be loaded yet");
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("SQLitePCLRaw.bundle_e_sqlite3", "2.1.10", false, out _);

			// The requested package is loaded; its dependency is only registered by name.
			Assert.IsNotNull(Loaded("SQLitePCLRaw.batteries_v2"), "the requested package should be loaded");
			Assert.IsNull(Loaded("SQLitePCLRaw.core"), "a dependency must not be loaded before it is used");

			// Naming a type from it is what materializes it.
			var t = Keysharp.Builtins.TypeResolver.Resolve("SQLitePCL.raw");
			Assert.IsNotNull(t, "a dependency's type must still be resolvable by name");
			Assert.IsNotNull(Loaded("SQLitePCLRaw.core"), "resolving the type should have loaded its assembly");

			// The bundle's initialization reaches SQLitePCLRaw.provider.e_sqlite3 and then P/Invokes the selected
			// RID-specific e_sqlite3 asset. This proves that preserving a runtimes/ folder is not enough: the provider
			// selected the correct native file and the runtime loader registered its logical import name.
			var batteries = Loaded("SQLitePCLRaw.batteries_v2")?.GetType("SQLitePCL.Batteries_V2");
			Assert.IsNotNull(batteries);
			Assert.DoesNotThrow(() => batteries.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null));
		}

		/// <summary>
		/// The point of resolving at build time: inline C# can name a type from a package the script declared.
		/// <para>It could not before, and the reason was structural rather than a missing lookup — `#Package` was
		/// lowered into a RUNTIME call, so the assemblies did not exist until the script was already running and the
		/// compiler was never told they would. `using Newtonsoft.Json;` was a CS0246 no matter what the script
		/// declared.</para>
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void InlineCSharpPackage()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgcs_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "p.ks");
				File.WriteAllText(script,
								  "#NoTrayIcon\n#Package Newtonsoft.Json 13.0.3\n#CSharp\nusing Newtonsoft.Json;\n"
								  + "public static object Ser(object o) => JsonConvert.SerializeObject(42);\n#EndCSharp\nx := Ser(1)\n");
				var ch = new CompilerHelper();
				var (arr, code, compilation) = ch.CompileCodeToByteArray(script, "p");
				Assert.IsNotNull(arr, "inline C# must be able to bind a declared package's types:\n" + code);

				// The manifest is the artifact that makes the build reproducible: it records what the constraint
				// RESOLVED to, not what the script asked for.
				Assert.IsNotNull(compilation.Packages, "a script with #Package must carry a resolved manifest");
				var entry = compilation.Packages.Packages.FirstOrDefault(p => p.Id.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
				Assert.IsNotNull(entry, "the declared package must be in the manifest");
				Assert.AreEqual("13.0.3", entry.Resolved, "the manifest records the resolved version");
				Assert.IsNotEmpty(entry.Managed, "the manifest must record the assemblies the script was compiled against");
				Assert.IsTrue(File.Exists(entry.Managed[0].Source), "recorded assembly paths must exist: " + entry.Managed[0].Source);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>
		/// The manifest is what a built script actually loads from, so it has to be exercised as a manifest — not via
		/// the resolve-and-load helper the other runtime tests use.
		/// <para>Every bug in the first version of this feature lived here and none was visible to a green suite: the
		/// manifest could not be found in a precompiled script at all; an all-optional set produced no manifest and
		/// the script refused to start; and the whole dependency closure was recorded as directly requested, which
		/// both defeated lazy registration and made an unloadable transitive dependency fatal.</para>
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void PackageManifest()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgman_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				// Serilog.Sinks.Console pulls Serilog in transitively, so the closure is strictly bigger than the ask.
				var script = Path.Combine(dir, "m.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#Package Serilog.Sinks.Console 6.0.0\nx := 1\n");
				var ch = new CompilerHelper();
				var (arr, code, compilation) = ch.CompileCodeToByteArray(script, "m");
				Assert.IsNotNull(arr, "compile failed:\n" + code);

				var direct = compilation.Packages.Packages.Where(p => p.Direct).ToList();
				var transitive = compilation.Packages.Packages.Where(p => !p.Direct).ToList();
				Assert.AreEqual(1, direct.Count, "only the package the script named is direct");
				Assert.AreEqual("Serilog.Sinks.Console", direct[0].Id);
				Assert.IsNotEmpty(transitive, "the closure must be recorded too, so its types are still resolvable");
				Assert.IsTrue(transitive.Any(p => p.Id.Equals("Serilog", StringComparison.OrdinalIgnoreCase)),
							  "a transitive dependency belongs in the manifest, flagged not-direct");

				// A round trip through the embedded form: this is exactly what the runtime reads back.
				var round = Keysharp.Internals.Os.PackageManifest.Read(compilation.Packages.Write());
				Assert.AreEqual(1, round.Direct.Count, "only direct packages are unioned into the loader's request list");
				// The CONSTRAINT survives, in its translated form — a full version means exactly that version, so the
				// lowerer turned `6.0.0` into the NuGet range `[6.0.0]`. What must NOT appear here is a raw resolved
				// version, which is what a transitive entry carries and what used to collide with Clr.LoadPackage.
				Assert.AreEqual("[6.0.0]", round.Direct[0].Version, "the constraint the script wrote survives the round trip");
				Assert.IsTrue(round.TryLocate(null, out var located, out var missing), missing);
				Assert.AreEqual(compilation.Packages.Packages.Count, located.Count, "every recorded package must be locatable");

				// And it is embedded in the assembly the script will actually ask, rather than only living in memory.
				Assert.IsNotNull(Keysharp.Internals.Os.PackageManifest.FromAssembly(Assembly.Load(arr)),
								 "the manifest must be readable from the compiled assembly itself");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>
		/// `#Package *i` on a package that cannot be resolved must leave a script that still RUNS. It compiles either
		/// way; the regression was that it then died at startup with "carries no package manifest", because the
		/// compiler produced no manifest while the lowerer still emitted the call that looks for one.
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void OptionalManifest()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgopt_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "o.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#Package *i Keysharp.NoSuchPackage.ForTests 9.9.9\nx := 1\n");
				var ch = new CompilerHelper();
				var (arr, code, compilation) = ch.CompileCodeToByteArray(script, "o");
				Assert.IsNotNull(arr, "an unavailable *i package must not fail the build:\n" + code);
				Assert.IsNotNull(compilation.Packages, "a script that declares any package must carry a manifest, even an empty one");
				Assert.IsEmpty(compilation.Packages.Packages, "nothing resolved, so the manifest is empty rather than absent");
				Assert.IsNotNull(Keysharp.Internals.Os.PackageManifest.FromAssembly(Assembly.Load(arr)),
								 "the empty manifest must still be embedded, or the script cannot start");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>
		/// The copy step that makes a compiled artifact portable: the manifest's assemblies land beside the output,
		/// a stale copy from an earlier version IS overwritten (skipping it silently reintroduced the version drift
		/// the manifest exists to prevent), and Keysharp's own files are never clobbered.
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void PackageDeployment()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgcopy_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "c.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#Package Newtonsoft.Json 13.0.3\nx := 1\n");
				var ch = new CompilerHelper();
				var (arr, code, compilation) = ch.CompileCodeToByteArray(script, "c");
				Assert.IsNotNull(arr, code);

				var outDir = Path.Combine(dir, "out");
				Assert.IsNull(Keysharp.Internals.Scripting.Runner.CopyPackageAssemblies(compilation, outDir));
				// Assets deploy under .keysharp/packages/<id>/, so they can never collide with the host's own
				// files beside the artifact — and repeating the copy after a version change overwrites in place.
				var deployed = Directory.GetFiles(Path.Combine(outDir, ".keysharp", "packages"), "Newtonsoft.Json.dll", SearchOption.AllDirectories);
				Assert.IsNotEmpty(deployed, "the package assembly must deploy under .keysharp/packages");
				File.WriteAllText(deployed[0], "stale");
				Assert.IsNull(Keysharp.Internals.Scripting.Runner.CopyPackageAssemblies(compilation, outDir));
				Assert.Greater(new FileInfo(deployed[0]).Length, 100, "a re-copy must replace a stale file, not keep it");
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}

		/// <summary>
		/// `--validate` and <c>Ks.ParseScript</c> are checks, not builds, so an unrestored package set must be
		/// REPORTED rather than fetched. Without this a syntax check could block for up to three minutes on a
		/// network restore the user never asked for — and Keyview runs one on a keystroke debounce.
		/// </summary>
		[Test, Category("NuGet"), NonParallelizable]
		public void OfflineRestore()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks_pkgoff_" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(dir);

			try
			{
				var script = Path.Combine(dir, "off.ks");
				// A package id that cannot be in any cache, so the set is guaranteed cold.
				File.WriteAllText(script, "#NoTrayIcon\n#Package Keysharp.NoSuchPackage.ForTests 9.9.9\nx := 1\n");
				Keysharp.Internals.Os.PackageResolver.ResetCounters();
				// Passed per call: there is no mode to save, restore, or leak into a concurrent compile.
				var (arr, code, _) = new CompilerHelper().CompileCodeToByteArray(script, "off", allowPackageRestore: false);
				Assert.IsNull(arr, "an unrestored package set must fail an offline compile rather than resolve");
				Assert.AreEqual(0, Keysharp.Internals.Os.PackageResolver.RestoreCount,
								"an offline compile must not invoke the provider restore");
				Assert.IsTrue(code.Contains("not been restored"), "the report should name the actual situation; got:\n" + code);
			}
			finally
			{
				try { Directory.Delete(dir, true); } catch { }
			}
		}
	}
}
