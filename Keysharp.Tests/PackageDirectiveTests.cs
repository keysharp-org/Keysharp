using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using KP = Keysharp.Parsing.Syntax;

namespace Keysharp.Tests
{
	/// <summary>
	/// Covers the `#Package` directive. The compile-time half (validation and, crucially, batching) is tested here
	/// against the lowerer directly; the resolve-and-load half needs the network and the .NET SDK, so it is kept in
	/// its own category and out of the curated CI set.
	/// </summary>
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
			return (unit?.ToFullString() ?? "", lowerer.Diagnostics);
		}

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
		public void SpecValidation()
		{
			foreach (var ok in new[] { "Newtonsoft.Json", "SQLitePCLRaw.bundle_e_sqlite3", "a", "A-B_c.1" })
				Assert.IsTrue(Keysharp.Internals.Os.NuGetPackageLoader.IsValidId(ok), ok);

			// Anything outside the allowlist would be written verbatim into the generated project file, so these
			// have to be rejected rather than escaped.
			foreach (var bad in new[] { "", "a/b", "a;b", "a b", "a\"b", "<a>", "a&b", "../x" })
				Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.IsValidId(bad), bad);

			foreach (var ok in new[] { "", "13.0.3", "2.1.10", "1.0.0-beta.1", "13.*", "1.2.3.4", "1.0.0+meta" })
				Assert.IsTrue(Keysharp.Internals.Os.NuGetPackageLoader.IsValidVersion(ok), ok);

			foreach (var bad in new[] { "1.0;x", "1.0 2.0", "<1.0>", "1.0\"" })
				Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.IsValidVersion(bad), bad);
		}

		[Test, Category("Directives")]
		public void MissingIdIsACompileError()
		{
			var (_, diags) = Lower("#Package\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("expected a package name", diags[0]);
		}

		[Test, Category("Directives")]
		public void InvalidIdIsACompileError()
		{
			var (_, diags) = Lower("#Package Some/Bad;Id 1.0.0\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("is not a valid package name", diags[0]);
		}

		/// <summary>
		/// Versions may be omitted, partial, exact, or bounded — the shapes `#Requires` accepts — and are translated
		/// to NuGet ranges at compile time. The translation is not cosmetic: `&lt;` and `&gt;` are illegal in an XML
		/// attribute value, so a comparison must never reach the generated project file as the script wrote it.
		/// </summary>
		[Test, Category("Directives")]
		public void VersionFormsTranslateToNuGetRanges()
		{
			void Check(string written, string expected)
			{
				var ok = Keysharp.Internals.Os.NuGetPackageLoader.TryTranslateVersion(written, out var range, out var err);
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
				_ = Keysharp.Internals.Os.NuGetPackageLoader.TryTranslateVersion(v, out var r, out _);
				Assert.IsFalse(r.Contains('<') || r.Contains('>') || r.Contains('"') || r.Contains('&'), $"'{v}' -> '{r}'");
			}

			Check("[13.0.3]", "[13.0.3]");        // literal exact
			Check("(13.0,)", "(13.0,)");          // literal open upper bound
			Check("*", "*");

			foreach (var bad in new[] { "abc", "1.0 2.0", ">=", "1.0 >=2.0", ">=x" })
				Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.TryTranslateVersion(bad, out _, out _), bad);

			// A literal range is written verbatim into the csproj, so a malformed one has to be caught here rather
			// than surfacing as a NuGet error at run time — this method's whole contract is compile-time rejection.
			foreach (var bad in new[] { "[[[", "***", "[]", "[,]", "(1.0)", "[1.0,2.0,3.0]", "[abc,)", "1.*.3", "*.1" })
				Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.TryTranslateVersion(bad, out _, out _), bad);
		}

		[Test, Category("Directives")]
		public void BoundedVersionLowersIntoTheSpec()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json >=13.0 <14\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			AssertEmits(code, "LoadPackages((\"Newtonsoft.Json\", \"[13.0,14)\", false))");
		}

		[Test, Category("Directives")]
		public void MalformedVersionIsACompileError()
		{
			var (_, diags) = Lower("#Package Newtonsoft.Json 13.0.3 extra\n");
			Assert.AreEqual(1, diags.Count, string.Join("; ", diags));
			StringAssert.Contains("is not a valid version", diags[0]);
		}

		[Test, Category("Directives")]
		public void ConflictingVersionsForOnePackageIsACompileError()
		{
			var (_, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package Newtonsoft.Json 12.0.0\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("requested twice with different versions", diags[0]);
		}

		[Test, Category("Directives")]
		public void IdenticalDuplicateIsAccepted()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package Newtonsoft.Json 13.0.3\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			AssertEmits(code, "LoadPackages((\"Newtonsoft.Json\", \"[13.0.3]\", false))");
		}

		/// <summary>
		/// The load-bearing structural invariant: however many `#Package` lines a script has, they must reach the
		/// runtime as ONE call carrying the whole set. NuGet resolution is a whole-graph operation — resolving each
		/// directive separately could unify a shared dependency to two different versions and load both.
		/// </summary>
		[Test, Category("Directives")]
		public void MultiplePackagesLowerToASingleBatchedCall()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package Serilog 4.0.0\nx := 1\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, CountCalls(code), "expected exactly one batched LoadPackages call");
			AssertEmits(code, "LoadPackages((\"Newtonsoft.Json\", \"[13.0.3]\", false), (\"Serilog\", \"[4.0.0]\", false))");
		}

		[Test, Category("Directives")]
		public void VersionIsOptional()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			// The empty version becomes Version="*" in the generated project: newest stable, then pinned by the cache.
			AssertEmits(code, "LoadPackages((\"Newtonsoft.Json\", \"*\", false))");
		}

		/// <summary>
		/// Having its own directive is what keeps package names out of `#Requires`'s first-token space, where
		/// `MapRequires` matches the version form with StartsWith — a package literally named `Keysharp.Extensions`
		/// would otherwise have been read as a compatibility-version declaration.
		/// </summary>
		[Test, Category("Directives")]
		public void PackageNamesDoNotCollideWithRequiresForms()
		{
			var (code, diags) = Lower("#Package Keysharp.Extensions 1.2.3\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			AssertEmits(code, "LoadPackages((\"Keysharp.Extensions\", \"[1.2.3]\", false))");

			// The #Requires forms still lower to their own things, and never to a package load.
			foreach (var other in new[] { "#Requires AutoHotkey v2.0\n", "#Requires Keysharp v2.1\n", "#Requires capability ScreenCapture\n" })
			{
				var (c, d) = Lower(other);
				Assert.IsEmpty(d, other + " -> " + string.Join("; ", d));
				Assert.AreEqual(0, CountCalls(c), other + " must not load a package");
			}
		}

		/// <summary>
		/// `*i` marks a package optional. It has to survive into the emitted spec, because whether an unavailable
		/// package stops the script is decided at runtime — resolution is whole-graph, so the loader can only drop
		/// the optional ones and retry once it knows the full set failed.
		/// </summary>
		[Test, Category("Directives")]
		public void OptionalPackagesCarryTheIgnoreFlag()
		{
			var (code, diags) = Lower("#Package *i Serilog 4.0.0\n#Package Newtonsoft.Json 13.0.3\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			AssertEmits(code, "LoadPackages((\"Serilog\", \"[4.0.0]\", true), (\"Newtonsoft.Json\", \"[13.0.3]\", false))");
		}

		[Test, Category("Directives")]
		public void OptionalFlagStillRequiresAName()
		{
			var (_, diags) = Lower("#Package *i\n");
			Assert.AreEqual(1, diags.Count);
			StringAssert.Contains("expected a package name", diags[0]);
		}

		/// <summary>
		/// A package requirement is program-wide, so one buried in a function body cannot be honoured at the point it
		/// appears. It must be reported rather than silently vanishing.
		/// </summary>
		[Test, Category("Directives")]
		public void NestedRequirementIsReported()
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
		public void NestedRequirementIsReportedEvenWhenItMatchesATopLevelOne()
		{
			var (_, diags) = Lower("f() {\n#Package Newtonsoft.Json 13.0.3\n}\n#Package Newtonsoft.Json 13.0.3\nf()\n");
			Assert.AreEqual(1, diags.Count, string.Join("; ", diags));
			StringAssert.Contains("top level", diags[0]);
		}

		/// <summary>
		/// Packages are program-wide, so they must load before ANY module's auto-exec runs — not at the position of
		/// the directive that happened to declare them. Modules execute in dependency order, which need not match the
		/// order they lower in, so emitting at the directive's position lets an imported module's top-level code run
		/// against packages that are not loaded yet.
		/// </summary>
		[Test, Category("Directives")]
		public void PackagesLoadBeforeEveryModuleAutoExec()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\nx := 1\n#Module Helper\ny := 2\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, CountCalls(code));
			var load = code.IndexOf("LoadPackages(", StringComparison.Ordinal);
			var firstAutoExec = System.Text.RegularExpressions.Regex.Match(code, @"Program\.\w+\.AutoExecSection\(\)");
			Assert.IsTrue(firstAutoExec.Success, "expected the outer auto-exec to drive each module; generated:\n" + code);
			Assert.Less(load, firstAutoExec.Index,
						"packages must load before the first module auto-exec; generated:\n" + code);
		}

		/// <summary>
		/// A `dotnet restore` that FAILS still writes a complete, well-formed project.assets.json describing whichever
		/// packages did resolve. Trusting that file would turn a hard "package not found" error into a silently
		/// missing package on every subsequent run — loud once, then never again. NuGet's own verdict lives in
		/// project.nuget.cache beside it, and that is what gates the cache hit.
		/// </summary>
		[Test, Category("Directives")]
		public void AFailedRestoreIsNotTrustedAsACacheHit()
		{
			var dir = Path.Combine(Path.GetTempPath(), "ks-restore-verdict", Guid.NewGuid().ToString("N"));
			var obj = Path.Combine(dir, "obj");
			_ = Directory.CreateDirectory(obj);
			var cache = Path.Combine(obj, "project.nuget.cache");

			// No project.nuget.cache at all: an interrupted restore, which must re-run rather than be trusted.
			Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.RestoreSucceeded(dir));

			File.WriteAllText(cache, "{\"version\":2,\"success\":false,\"expectedPackageFiles\":[],\"logs\":[]}");
			Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.RestoreSucceeded(dir),
						   "a restore NuGet itself recorded as failed must not count as a cache hit");

			File.WriteAllText(cache, "{\"version\":2,\"success\":true,\"expectedPackageFiles\":[],\"logs\":[]}");
			Assert.IsTrue(Keysharp.Internals.Os.NuGetPackageLoader.RestoreSucceeded(dir));

			File.WriteAllText(cache, "{ not json");
			Assert.IsFalse(Keysharp.Internals.Os.NuGetPackageLoader.RestoreSucceeded(dir));

			try { Directory.Delete(dir, true); } catch { }
		}

		[Test, Category("Directives")]
		public void NoDirectiveEmitsNoCall()
		{
			var (code, diags) = Lower("x := 1\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(0, CountCalls(code));
		}

		/// <summary>
		/// A directive nobody handles is rejected rather than dropped: dropping it runs the script with the setting
		/// silently absent, so the failure surfaces somewhere unrelated. Deprecated AutoHotkey v1 directives are in
		/// that set deliberately — Keysharp targets v2, and ignoring a v1 leftover hides a porting bug.
		/// </summary>
		[Test, Category("Directives")]
		public void UnknownAndDeprecatedDirectivesAreRejected()
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
		/// The counterpart, and the regression the check above can most easily cause: everything Keysharp genuinely
		/// supports must still compile clean, including the directives consumed earlier in the pipeline that reach the
		/// lowerer's default arm as legitimate no-ops.
		/// </summary>
		[Test, Category("Directives")]
		public void SupportedDirectivesAreNotReported()
		{
			foreach (var ok in new[]
			{
				// Handled by the lowerer's switch.
				"#Requires AutoHotkey v2.0\n", "#Warn VarUnset, Off\n", "#NoTrayIcon\n", "#SingleInstance Force\n",
				"#Persistent\n", "#ErrorStdOut\n", "#MaxThreads 5\n", "#MaxThreadsBuffer 1\n",
				"#MaxThreadsPerHotkey 2\n", "#ClipboardTimeout 500\n", "#HotIfTimeout 100\n", "#InputLevel 1\n",
				"#SuspendExempt\n", "#UseHook\n", "#WinActivateForce\n", "#DllLoad *i user32\n",
				"#HookMutexName foo\n", "#MenuMaskKey vk11\n", "#NoMainWindow\n", "#StructPack 4\n",
				"#AssemblyTitle t\n", "#AssemblyVersion 1.2.3.4\n", "#Region\n#EndRegion\n", "#Nullable\n",
				// Consumed earlier: parser splices these, DHHR takes #HotIf/#Hotstring at top level.
				"#HotIf 1\nx::y\n#HotIf\n", "#Hotstring EndChars -,.?!\n", "#import \"Ks\" { Clr }\n",
				"#Define FOO 1\n#If FOO\ny := 1\n#EndIf\n",
			})
			{
				var (_, diags) = Lower(ok);
				Assert.IsEmpty(diags, ok + " -> " + string.Join("; ", diags));
			}
		}

		/// <summary>
		/// Every directive reaches the runtime as its own `(id, version, optional)` tuple, in source order. The
		/// pieces are never packed into a string, so this pins the whole lowerer-to-loader contract — there is no
		/// separate format for the two ends to disagree about.
		/// </summary>
		[Test, Category("Directives")]
		public void EachPackageLowersToItsOwnTuple()
		{
			var (code, diags) = Lower("#Package Newtonsoft.Json 13.0.3\n#Package *i Serilog >=4.0 <5\n#Package A.B\n");
			Assert.IsEmpty(diags, string.Join("; ", diags));
			Assert.AreEqual(1, CountCalls(code), "expected exactly one batched call");
			AssertEmits(code, "LoadPackages((\"Newtonsoft.Json\", \"[13.0.3]\", false), (\"Serilog\", \"[4.0,5)\", true), (\"A.B\", \"*\", false))");
		}

		/// <summary>
		/// Reading the SDK's assets file is the most assumption-laden code in the feature and the only part that a
		/// live restore is otherwise the only way to exercise. A fixture pins the shapes that matter: RID-suffixed
		/// target selection, the `_._` placeholder, and native assets landing separately from managed ones.
		/// </summary>
		[Test, Category("Directives")]
		public void AssetsFileIsReadPerNuGetsShapes()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-assets-fixture", Guid.NewGuid().ToString("N"));
			var pkgDir = Path.Combine(root, "packages", "demo.pkg", "1.0.0");
			_ = Directory.CreateDirectory(Path.Combine(pkgDir, "lib", "net6.0"));
			_ = Directory.CreateDirectory(Path.Combine(pkgDir, "runtimes", "win-x64", "native"));
			File.WriteAllText(Path.Combine(pkgDir, "lib", "net6.0", "Demo.dll"), "");
			File.WriteAllText(Path.Combine(pkgDir, "lib", "net6.0", "_._"), "");
			File.WriteAllText(Path.Combine(pkgDir, "runtimes", "win-x64", "native", "demo_native.dll"), "");
			var tfm = Keysharp.Internals.Os.NuGetPackageLoader.TargetFramework;
			var rid = Keysharp.Internals.Os.NuGetPackageLoader.RuntimeId;
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
			var read = Keysharp.Internals.Os.NuGetPackageLoader.TryReadAssets(assets);
			Assert.IsNotNull(read, "fixture should parse");
			Assert.AreEqual(1, read.Count);
			// The RID-qualified target wins, `_._` is dropped, and native assets do not land among the managed ones.
			Assert.AreEqual(1, read[0].Managed.Count, "managed: " + string.Join(", ", read[0].Managed));
			StringAssert.EndsWith("Demo.dll", read[0].Managed[0]);
			Assert.AreEqual(1, read[0].Native.Count);
			StringAssert.EndsWith("demo_native.dll", read[0].Native[0]);

			// A file the assets list names but which is gone means the shared package folder was cleared: the whole
			// entry is stale and must force a fresh restore rather than half-load.
			File.Delete(Path.Combine(pkgDir, "lib", "net6.0", "Demo.dll"));
			Assert.IsNull(Keysharp.Internals.Os.NuGetPackageLoader.TryReadAssets(assets));
			try { Directory.Delete(root, true); } catch { }
		}

		/// <summary>A P/Invoke names a native library in several ways; all of them have to find the one file.</summary>
		[Test, Category("Directives")]
		public void NativeLibraryAliasesCoverTheSpellingsPInvokeUses()
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

		/// <summary>The cache key must not depend on the order packages were written in, or every reorder re-restores.</summary>
		[Test, Category("Directives")]
		public void CacheKeyIsOrderIndependentButVersionSensitive()
		{
			static string Key(params (string, string)[] p) =>
				Keysharp.Internals.Os.NuGetPackageLoader.CacheKeyFor(
					p.Select(x => new Keysharp.Internals.Os.NuGetPackageLoader.PackageRef(x.Item1, x.Item2, false)).ToList());

			Assert.AreEqual(Key(("A", "[1.0]"), ("B", "[2.0]")), Key(("B", "[2.0]"), ("A", "[1.0]")));
			Assert.AreNotEqual(Key(("A", "[1.0]")), Key(("A", "[1.1]")));
			Assert.AreNotEqual(Key(("A", "[1.0]")), Key(("A", "[1.0]"), ("B", "[2.0]")));
		}

		/// <summary>Win-modifier hotkeys start with '#' too; they must never be mistaken for directives.</summary>
		[Test, Category("Directives")]
		public void WinModifierHotkeysAreNotDirectives()
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
		public void LoadPackageResolvesAndDetectsConflicts()
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
		/// A REQUIRED package that does not exist must be reported, must name itself, and must not blame the SDK —
		/// the install-the-SDK paragraph belongs only to a machine that actually lacks it, and printing it for a
		/// plain "package not found" buries the real error under advice the user has already followed. The failed
		/// request must also roll back, so a later good call is not poisoned by a set that is known to fail.
		/// </summary>
		[Test, Category("NuGet")]
		public void RequiredPackageThatDoesNotExistIsReportedAndRolledBack()
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
		/// The directive's whole reason for batching, at the runtime end: however many packages one `#Package` set
		/// carries, they must be resolved as ONE graph. Resolving them one at a time loads each package's closure
		/// before the next is resolved, and the later resolution can then pick a version of a shared dependency that
		/// differs from the one already in the process — which .NET cannot unload. Counting resolutions rather than
		/// restores is deliberate: it holds whether the cache is warm or cold.
		/// </summary>
		[Test, Category("NuGet")]
		public void ABatchedRequestResolvesTheGraphExactlyOnce()
		{
			Script.TheScript.LoadPackages(("Newtonsoft.Json", "[13.0.3]", false), ("Serilog", "[4.0.0]", false));
			Assert.AreEqual(1, Keysharp.Internals.Os.NuGetPackageLoader.ResolveCount,
							"two packages in one directive set must resolve as one graph, not once each");

			// The contrast: the imperative form cannot batch, so each call resolves again — over the UNION, which is
			// what keeps it correct despite not being able to.
			Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "13.0.3", false, out _);
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Serilog", "4.0.0", false, out _);
			Assert.AreEqual(2, Keysharp.Internals.Os.NuGetPackageLoader.ResolveCount);
		}

		/// <summary>
		/// The feature's headline promise: the SDK and the network are needed only the FIRST time a package set is
		/// used on a machine. Every later run reads the assets file the SDK already wrote, spawning no subprocess.
		/// Nothing else observes that — a regression would just make startup quietly slow.
		/// </summary>
		[Test, Category("NuGet")]
		public void AWarmPackageSetSpawnsNoRestore()
		{
			// Warm the set (this may or may not restore, depending on what earlier tests left in the cache).
			Script.TheScript.LoadPackages(("Newtonsoft.Json", "[13.0.3]", false));
			Keysharp.Internals.Os.NuGetPackageLoader.ResetForTests();   // zeroes the counter; the on-disk cache stays
			Script.TheScript.LoadPackages(("Newtonsoft.Json", "[13.0.3]", false));
			Assert.AreEqual(0, Keysharp.Internals.Os.NuGetPackageLoader.RestoreCount,
							"a package set already resolved on this machine must not spawn 'dotnet restore' again");
		}

		[Test, Category("Directives")]
		public void LoadPackageRejectsBadNamesAndVersionsWithoutResolving()
		{
			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Some/Bad;Id", "", false, out var e1);
			StringAssert.Contains("not a valid package name", e1);

			_ = Keysharp.Internals.Os.NuGetPackageLoader.LoadOne("Newtonsoft.Json", "not a version", false, out var e2);
			StringAssert.Contains("not a valid version", e2);
		}

		/// <summary>
		/// End-to-end: actually resolves and loads a package. Needs the network and the .NET SDK on the first run for
		/// a given package set, so it is deliberately outside the curated CI categories.
		/// </summary>
		[Test, Category("NuGet")]
		public void ResolvesAndLoadsARealPackage()
		{
			// The canonical spelling the lowerer emits. It matters that every test in this fixture agrees on it: the
			// requested-package set is process-wide by design (loaded assemblies cannot be unloaded), so a different
			// spelling here would be reported as a version conflict with whatever ran first.
			Script.TheScript.LoadPackages(("Newtonsoft.Json", "[13.0.3]", false));
			var loaded = Loaded("Newtonsoft.Json");
			Assert.IsNotNull(loaded, "Newtonsoft.Json was not loaded");
			Assert.AreEqual(new Version(13, 0, 0, 0), loaded.GetName().Version);
			// The point of loading is that Clr can reach the types, which goes through TypeResolver's assembly index.
			Assert.IsNotNull(loaded.GetType("Newtonsoft.Json.JsonConvert"));
		}

		/// <summary>
		/// A compiled script has no Keysharp launcher around it, so the generated call has to reach the loader on its
		/// own — and it resolves on the machine it runs on, not the one it was built on. Both `--compile exe` and
		/// `exe-min` share this path; the difference between them is which dependencies are embedded, and
		/// Keysharp.Core (which carries the loader) is excluded from embedding in both.
		/// </summary>
		[TestCase("exe"), TestCase("exe-min"), Category("NuGet")]
		public void CompiledScriptResolvesPackagesAtRunTime(string mode)
		{
			// Driven through the real launcher rather than TestRunner's exeout: that path emits a bare assembly with
			// no runtimeconfig.json (fine for the reflection AsmInfo does, not runnable), and running it is the whole
			// point here.
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			// Emptied first, and never the build output directory. If the dependencies happen to be lying next to the
			// exe already, `exe-min` resolves them from disk and passes without its embedded copies ever being
			// exercised — the two modes become indistinguishable and the test proves nothing.
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

			// Each mode must work for its own reason, or the pair of cases is really one case run twice. `exe-min`
			// embeds the managed dependencies, so nothing but Keysharp.Core (deliberately excluded from embedding,
			// since it is what loads the rest) may sit beside it; `exe` copies them instead.
			var siblings = Directory.GetFiles(dir, "*.dll")
									.Select(Path.GetFileName)
									.Where(f => !f.Equals("Keysharp.Core.dll", StringComparison.OrdinalIgnoreCase)
												&& !f.Equals(Path.GetFileName(Path.ChangeExtension(script, ".dll")), StringComparison.OrdinalIgnoreCase))
									.ToArray();

			if (mode == "exe-min")
				Assert.IsEmpty(siblings, "exe-min should embed its dependencies, but these were copied alongside: " + string.Join(", ", siblings));
			else
				Assert.IsNotEmpty(siblings, "exe should copy its dependencies alongside the executable");

			_ = Run(exe, "", out var stdout, out var stderr);
			Assert.AreEqual("[1,2,3]", stdout.Trim(), "stderr: " + stderr);
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
		public void DependencyIsResolvableButNotLoadedUntilUsed()
		{
			Assert.IsNull(Loaded("SQLitePCLRaw.core"), "test precondition: the dependency must not be loaded yet");
			Script.TheScript.LoadPackages(("SQLitePCLRaw.bundle_e_sqlite3", "2.1.10", false));

			// The requested package is loaded; its dependency is only registered by name.
			Assert.IsNotNull(Loaded("SQLitePCLRaw.batteries_v2"), "the requested package should be loaded");
			Assert.IsNull(Loaded("SQLitePCLRaw.core"), "a dependency must not be loaded before it is used");

			// Naming a type from it is what materializes it.
			var t = Keysharp.Builtins.TypeResolver.Resolve("SQLitePCL.raw");
			Assert.IsNotNull(t, "a dependency's type must still be resolvable by name");
			Assert.IsNotNull(Loaded("SQLitePCLRaw.core"), "resolving the type should have loaded its assembly");
		}
	}
}
