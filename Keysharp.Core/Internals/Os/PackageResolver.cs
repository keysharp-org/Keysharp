using Keysharp.Builtins;
using KsDebug = Keysharp.Builtins.Debug;

namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Turns a set of <c>#Package</c> / <c>Clr.LoadPackage</c> requests into concrete assembly paths: it restores
	/// (writing only the SDK's own cache directories), reads the resulting <c>project.assets.json</c> and hands back
	/// what it found. It never loads an assembly and never installs a resolver hook. The only state it keeps is a memo of set -> result keyed by the
	/// set itself, so no request can change the answer given to a different one.
	/// <para>That is the whole point of the split. <see cref="NuGetPackageLoader"/> is a RUNTIME loader, and its
	/// union-everything behaviour is correct for one: resolution is whole-graph, so two independent resolutions in
	/// one process can pick different versions of a shared dependency and try to load both, which .NET cannot undo.
	/// A COMPILER has the opposite requirement — the daemon resolves for many unrelated scripts, and script A's
	/// `Foo 1.0` must not decide script B's `Foo 2.0`. One class could not be both, so this one resolves and that
	/// one loads.</para>
	/// See docs/design-nuget-packages.md for why the SDK does the actual work.
	/// </summary>
	internal static class PackageResolver
	{
		/// <summary>Restore is a network operation; bound it so a hung feed fails loudly instead of wedging startup.</summary>
		private const int RestoreTimeoutMs = 180_000;

		/// <summary>Restore subprocesses spawned. A warm set must spawn none, which is otherwise unobservable.</summary>
		internal static int RestoreCount;

		/// <summary>
		/// Calls to <see cref="TryResolve"/>, whether they read the assets file or were served from the memo. A batched
		/// request must resolve exactly once however many packages it carries; unlike <see cref="RestoreCount"/> this
		/// counts warm sets too.
		/// </summary>
		internal static int ResolveCount;

		internal static void ResetCounters()
		{
			RestoreCount = ResolveCount = 0;
			memo.Clear();       // without this, a test asserting a cold resolve would silently get the memoized answer
			while (memoOrder.TryDequeue(out _)) { }
		}

		/// <summary>
		/// Reports what a floating request actually resolved to, so `#Package Foo` has a discoverable answer the
		/// user can paste back as an explicit version. Only floating ones: an exact request already knows. Every
		/// caller passes TRANSLATED ranges (the lowerer canonicalizes directives at prescan, `Clr.LoadPackage` via
		/// TryTranslateVersion), so a versionless request arrives as `*`, never as an empty string.
		/// </summary>
		internal static void ReportResolved(List<PackageRef> wanted, List<ResolvedPackage> resolved, string label)
		{
			foreach (var w in wanted.Where(w => w.Version.Contains('*')))
				if (resolved.FirstOrDefault(r => r.Id.Equals(w.Id, StringComparison.OrdinalIgnoreCase)) is { } hit)
					_ = KsDebug.OutputDebug($"{label}: {hit.Id} {w.Version} resolved to {hit.Version}");
		}

		/// <summary>
		/// Resolves exactly this package set to concrete assembly paths. Nothing is loaded, and the result is memoized
		/// under the set's own identity only: two calls with DIFFERENT sets cannot influence each other, which is what
		/// lets the compile daemon resolve for unrelated scripts in one process.
		/// </summary>
		/// <param name="allowRestore">
		/// False means <b>offline</b>: satisfy the set from the existing cache or fail, never spawn <c>dotnet restore</c>.
		/// This is what `--validate` and Keyview use. Without it a syntax check could block for up to
		/// <see cref="RestoreTimeoutMs"/> ms on a network fetch — behind a keystroke-debounced recompile, no less.
		/// It is the same split as <c>dotnet build --no-restore</c>.
		/// </param>
		/// <param name="label">What to call the feature in failure text, so a `Clr.LoadPackage` caller is never told
		/// about a directive it does not contain.</param>
		internal static bool TryResolve(List<PackageRef> packages, bool allowRestore, string label,
										out List<ResolvedPackage> resolved, out string failure)
		{
			failure = null;
			resolved = null;
			_ = Interlocked.Increment(ref ResolveCount);
			var key = CacheKeyFor(packages);

			// Reading the answer back costs two JSON parses (project.assets.json runs to megabytes for a real graph)
			// plus a File.Exists per asset of every package in the closure. The compile daemon serves script after
			// script in one process and Keyview recompiles on a keystroke debounce, so the same set is resolved over
			// and over. CacheKeyFor is already an exact, order-independent identity for the set, framework and RID —
			// keying on it makes the warm path free. Memoizing ONE set by its own key is not the shared state this
			// class avoids: no set here can influence the answer for a different one.
			if (memo.TryGetValue(key, out var hit))
			{
				resolved = hit;
				return true;
			}

			// A bounded lock stripe keeps the SAME cold set single-flight without retaining a lock per unique set.
			// The concurrency is real — Ks.ParseScript runs on whatever thread a script calls it from, alongside a
			// host compile — and without the lock both would rewrite the generated csproj in one shared directory and
			// spawn `dotnet restore` into one shared obj/. (Two PROCESSES can still race there, benignly: the same key
			// writes byte-identical files, and the SDK serializes the package folder itself.) The old code got this
			// from the loader's single lock, which the compiler path no longer goes through.
			var stripe = resolutionLocks[(uint)StringComparer.Ordinal.GetHashCode(key) % resolutionLocks.Length];

			lock (stripe)
			{
				if (memo.TryGetValue(key, out hit))
				{
					resolved = hit;
					return true;
				}

				return ResolveUncached(packages, allowRestore, label, key, out resolved, out failure);
			}
		}

		private static bool ResolveUncached(List<PackageRef> packages, bool allowRestore, string label, string key,
											out List<ResolvedPackage> resolved, out string failure)
		{
			failure = null;
			var dir = Path.Combine(CacheRoot(), key);
			var assetsPath = Path.Combine(dir, "obj", "project.assets.json");
			// 1) Cache hit: no SDK, no network, no subprocess. This is the common case after first run. A restore that
			// FAILED still writes a complete, well-formed assets file for whichever packages did resolve, so the
			// assets file alone cannot be trusted — reading it would turn a hard error into a silently missing
			// package on every later run. RestoreSucceeded consults NuGet's own verdict alongside it.
			resolved = RestoreSucceeded(dir) ? TryReadAssets(assetsPath) : null;

			if (resolved != null)
				return Memoize(key, resolved);

			if (!allowRestore)
			{
				// Named as a distinct, actionable state rather than a generic failure: the script is not wrong, it
				// just has not been restored on this machine yet.
				failure = $"{label}: these packages have not been restored yet, and restoring is disabled here "
						  + $"({string.Join(", ", packages.Select(p => $"{p.Id} {p.Version}"))}). Run the script once, or compile it, to restore them.";
				return false;
			}

			// 2) Miss: let the SDK do the actual work.
			if (!TryRestore(dir, packages, label, out failure))
				return false;

			resolved = TryReadAssets(assetsPath);

			if (resolved == null)
			{
				failure = $"{label}: restore succeeded but no usable package list was produced in \"{assetsPath}\".";
				return false;
			}

			return Memoize(key, resolved);
		}

		private const int MaxMemoEntries = 64;

		/// <summary>Resolved sets keyed by <see cref="CacheKeyFor"/>. Bounded for long-lived compile daemons.</summary>
		private static readonly ConcurrentDictionary<string, List<ResolvedPackage>> memo = new(StringComparer.Ordinal);
		private static readonly ConcurrentQueue<string> memoOrder = new();

		/// <summary>Fixed stripes avoid retaining one lock forever for every package set a daemon has seen.</summary>
		private static readonly object[] resolutionLocks = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();

		private static bool Memoize(string key, List<ResolvedPackage> resolved)
		{
			// A key is enqueued exactly once, on FIRST add: the overwrite branch means the key is already in the
			// queue (whoever added it enqueued it), so enqueueing again would double-count it toward eviction. The
			// bound is deliberately FIFO by first-add, not LRU — a refreshed entry may still be evicted first, which
			// only costs a re-resolve from the on-disk cache.
			if (memo.TryAdd(key, resolved))
				memoOrder.Enqueue(key);
			else
				memo[key] = resolved;

			while (memo.Count > MaxMemoEntries && memoOrder.TryDequeue(out var oldest))
				_ = memo.TryRemove(oldest, out _);

			return true;
		}

		// ---- directive spec ----

		/// <summary>
		/// A package id and an optional version. Both are validated by the lowerer (so a malformed one is a compile
		/// error) and again here, which is also what makes writing them straight into the generated csproj safe.
		/// </summary>
		internal static bool IsValidId(string s) =>
			s.Length > 0 && s.Length < 128 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-');

		internal static bool IsValidVersion(string s) =>
			s.Length < 64 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '+' || c == '*'
										|| c == '[' || c == ']' || c == '(' || c == ')' || c == ',');

		/// <summary>A floating version: a plain version whose last component is the only <c>*</c> (<c>13.*</c>, <c>*</c>).</summary>
		private static bool IsFloatingVersion(string s) =>
			s == "*" || (s.EndsWith(".*", StringComparison.Ordinal) && IsPlainVersion(s[..^2]));

		/// <summary>
		/// A literal NuGet interval — a bracket, one or two comma-separated plain versions, a closing bracket. Either
		/// bound may be empty (an open end), and the single-version form <c>[1.0]</c> means exactly that version.
		/// </summary>
		private static bool IsValidRange(string s)
		{
			if (!IsValidVersion(s) || s.Length < 3 || (s[0] != '[' && s[0] != '(') || (s[^1] != ']' && s[^1] != ')'))
				return false;

			var parts = s[1..^1].Split(',');

			if (parts.Length > 2 || parts.All(p => p.Length == 0))
				return false;   // "[]" / "[,]" bounds nothing

			// A single version is only meaningful as the inclusive `[1.0]` form; `(1.0)` bounds nothing.
			if (parts.Length == 1 && (s[0] != '[' || s[^1] != ']'))
				return false;

			return parts.All(p => p.Length == 0 || IsPlainVersion(p));
		}

		/// <summary>
		/// Translates the version a script writes (omitted, partial, exact, comparison-bounded, or already a NuGet
		/// range) into the range NuGet understands, mirroring what <c>#Requires</c> accepts. Two rules are not
		/// self-evident from the code: a FULL version becomes exact (<c>13.0.3</c> → <c>[13.0.3]</c>, because NuGet
		/// reads a bare <c>13.0.3</c> as "or newer" and a script naming a full version wants reproducibility), and
		/// translation must happen at all because <c>&lt;</c>/<c>&gt;</c> are not legal in an XML attribute value and
		/// so can never reach the generated project file as typed. Runs at compile time, so a bad version is a
		/// compile error and the cache key is already canonical. VersionFormsTranslateToNuGetRanges pins every form.
		/// </summary>
		internal static bool TryTranslateVersion(string written, out string range, out string error)
		{
			range = Translate((written ?? "").Trim());
			error = range == null ? $"'{written}' is not a valid version" : null;
			return range != null;

			// Returns null for anything malformed; the single caller above turns that into the one error message.
			static string Translate(string s)
			{
				if (s.Length == 0)
					return "*";   // newest stable

				// A literal NuGet range or a floating version is already in the target language, but still has to be
				// well-formed: it is written verbatim into the csproj, where a malformed one would surface as a NuGet
				// error at run time instead of the compile error this method promises.
				if (s[0] == '[' || s[0] == '(')
					return IsValidRange(s) ? s : null;

				if (s.Contains('*'))
					return IsValidVersion(s) && IsFloatingVersion(s) ? s : null;

				var tokens = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

				// A bare version is exactly one token with no comparison, so a stray trailing word (`13.0.3 extra`)
				// is rejected rather than having only its first token honoured.
				if (tokens.Length == 1 && Operator(tokens[0]).Length == 0)
				{
					var only = StripV(tokens[0]);
					// A partial version floats within what was written; a full one is exact.
					return !IsPlainVersion(only) ? null
						 : only.Split('.').Length >= 3 ? $"[{only}]" : $"{only}.*";
				}

				string lo = null, hi = null, exact = null;
				bool loInclusive = false, hiInclusive = false;

				foreach (var raw in tokens)
				{
					var op = Operator(raw);
					var tok = op.Length == 0 ? null : StripV(raw.Substring(op.Length));

					if (tok == null || !IsPlainVersion(tok))
						return null;

					switch (op)
					{
						case ">=": lo = tok; loInclusive = true; break;
						case ">": lo = tok; break;
						case "<=": hi = tok; hiInclusive = true; break;
						case "<": hi = tok; break;
						default: exact = tok; break;   // "="
					}
				}

				// `=` pins the version, so a bound alongside it is contradictory rather than merely redundant. This is
				// checked after the loop, not on sight, so that the tokens following it are still validated.
				if (exact != null)
					return lo == null && hi == null ? $"[{exact}]" : null;

				// An absent bound is open, and an open bound is always exclusive in NuGet's syntax: `(,14)`, never `[,14)`.
				return $"{(loInclusive ? '[' : '(')}{lo},{hi}{(hiInclusive ? ']' : ')')}";
			}

			static string Operator(string t)
			{
				foreach (var c in new[] { ">=", "<=", ">", "<", "=" })
					if (t.StartsWith(c, StringComparison.Ordinal))
						return c;

				return "";
			}

			// `#Requires` allows an optional leading "v"; accept it here so the two read the same.
			static string StripV(string t) =>
				t.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? t.Substring(1) : t;
		}

		/// <summary>A version with no operator, range syntax or wildcard — digits, dots and a pre-release/metadata tail.</summary>
		private static bool IsPlainVersion(string s)
		{
			if (string.IsNullOrEmpty(s))
				return false;

			var plus = s.IndexOf('+');

			if (plus >= 0 && (!Identifiers(s[(plus + 1)..]) || s.IndexOf('+', plus + 1) >= 0))
				return false;

			var withoutMetadata = plus >= 0 ? s[..plus] : s;
			var dash = withoutMetadata.IndexOf('-');

			if (dash >= 0 && !Identifiers(withoutMetadata[(dash + 1)..]))
				return false;

			var core = dash >= 0 ? withoutMetadata[..dash] : withoutMetadata;
			var parts = core.Split('.');
			return parts.Length is >= 1 and <= 4 && parts.All(p => p.Length != 0 && p.All(char.IsAsciiDigit));

			static bool Identifiers(string value) => value.Length != 0
				&& value.Split('.').All(p => p.Length != 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
		}

		/// <summary>One `#Package` directive: its name, requested version (empty = newest stable) and `*i` flag.</summary>
		internal readonly record struct PackageRef(string Id, string Version, bool Optional);

		// ---- cache location ----

		// One directory per (package set, framework, RID) — CacheRoot() + CacheKeyFor(). The generated project and its
		// assets file live there; package CONTENT always lives in the standard global packages folder, written by the
		// SDK — Keysharp never writes there.

		/// <summary>
		/// The cache key for a package set: order-independent (so writing the same directives in a different order is
		/// still a cache hit) but sensitive to every id, version and to the framework/RID the assets were resolved for.
		/// The `*i` flag is deliberately excluded — it changes what happens on failure, not what gets resolved.
		/// </summary>
		internal static string CacheKeyFor(List<PackageRef> packages)
		{
			var key = string.Join(";", packages.Select(p => $"{p.Id.ToLowerInvariant()}|{p.Version.ToLowerInvariant()}")
											   .OrderBy(s => s, StringComparer.Ordinal));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tfm}\n{rid}\n{key}")))[..16].ToLowerInvariant();
		}

		private static string CacheRoot()
		{
#if WINDOWS
			var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#else
			var root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

			if (string.IsNullOrEmpty(root))
				root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

#endif
			return Path.Combine(root, "Keysharp", "packages");
		}

		/// <summary>
		/// The framework and RID a package must be compatible with, and that the assets file is read against — taken
		/// from the running runtime rather than hard-coded, so they track the target Keysharp itself is built for.
		/// </summary>
		internal static string TargetFramework => tfm;

		internal static string RuntimeId => rid;

		private static readonly string tfm =
#if WINDOWS
			$"net{Environment.Version.Major}.{Environment.Version.Minor}-windows";
#else
			$"net{Environment.Version.Major}.{Environment.Version.Minor}";
#endif

		private static readonly string rid = RuntimeInformation.RuntimeIdentifier;

		// ---- restore (step 2) ----

		private static string BuildProject(List<PackageRef> packages)
		{
			var sb = new StringBuilder();
			_ = sb.AppendLine("<!-- Generated by Keysharp for the #Package directive. Safe to delete; it will be recreated. -->");
			_ = sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
			_ = sb.AppendLine("  <PropertyGroup>");
			_ = sb.AppendLine($"    <TargetFramework>{tfm}</TargetFramework>");
			_ = sb.AppendLine($"    <RuntimeIdentifier>{rid}</RuntimeIdentifier>");
			_ = sb.AppendLine("    <SelfContained>false</SelfContained>");
			_ = sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
			// Advisories are the one security control this design gets for free; make sure they are on.
			_ = sb.AppendLine("    <NuGetAudit>true</NuGetAudit>");
			_ = sb.AppendLine("    <NuGetAuditMode>all</NuGetAuditMode>");
			_ = sb.AppendLine("  </PropertyGroup>");
			_ = sb.AppendLine("  <ItemGroup>");

			foreach (var (id, version, _) in packages)
				// Already a NuGet range: the lowerer translated whatever the script wrote (see TryTranslateVersion),
				// so nothing here can contain a character that is illegal in an XML attribute value. A floating range
				// stays pinned by this cache entry — the assets file is written once and reused, so a script does not
				// silently upgrade on later runs.
				_ = sb.AppendLine($"    <PackageReference Include=\"{id}\" Version=\"{version}\" />");

			_ = sb.AppendLine("  </ItemGroup>");
			_ = sb.AppendLine("</Project>");
			return sb.ToString();
		}

		private static bool TryRestore(string dir, List<PackageRef> packages, string label, out string failure)
		{
			failure = null;
			_ = Interlocked.Increment(ref RestoreCount);

			try
			{
				_ = Directory.CreateDirectory(dir);
				// MSBuild walks up from the project directory looking for these; an unrelated one above the cache
				// root would silently change how the generated project restores. Terminate the walk here.
				File.WriteAllText(Path.Combine(dir, "Directory.Build.props"), "<Project />");
				File.WriteAllText(Path.Combine(dir, "Directory.Build.targets"), "<Project />");
				File.WriteAllText(Path.Combine(dir, "Directory.Packages.props"), "<Project />");   // NuGet walks up for this one too
				File.WriteAllText(Path.Combine(dir, "keysharp-packages.csproj"), BuildProject(packages));
			}
			catch (Exception e)
			{
				failure = $"{label}: could not write the package project to \"{dir}\": {e.Message}";
				return false;
			}

			var psi = new ProcessStartInfo("dotnet", "restore --nologo")
			{
				WorkingDirectory = dir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			string output;

			try
			{
				using var proc = Process.Start(psi);

				if (proc == null)
					return Fail(dir, packages, "the 'dotnet' command could not be started", "", true, label, out failure);

				// Read both pipes before waiting, or a chatty restore can fill a pipe buffer and deadlock.
				var stdout = proc.StandardOutput.ReadToEndAsync();
				var stderr = proc.StandardError.ReadToEndAsync();

				if (!proc.WaitForExit(RestoreTimeoutMs))
				{
					try { proc.Kill(entireProcessTree: true); } catch { }

					return Fail(dir, packages, $"'dotnet restore' did not finish within {RestoreTimeoutMs / 1000} seconds", "", false, label, out failure);
				}

				output = (stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult()).Trim();

				if (proc.ExitCode != 0)
					return Fail(dir, packages, $"'dotnet restore' failed (exit code {proc.ExitCode})", output, false, label, out failure);
			}
			catch (System.ComponentModel.Win32Exception e)
			{
				// The process could not be started at all — "file not found" here means the .NET SDK is not installed
				// or not on PATH, which is the one case the install-the-SDK advice fits.
				return Fail(dir, packages, $"'dotnet restore' could not be run ({e.Message})", "", true, label, out failure);
			}
			catch (Exception e)
			{
				return Fail(dir, packages, $"'dotnet restore' failed ({e.Message})", "", false, label, out failure);
			}

			// NuGet audit findings (NU1901-NU1904) are advisory, not fatal: the user asked for these packages, and a
			// vulnerable transitive dependency is information they need rather than a reason to refuse to start.
			foreach (var line in output.Split('\n'))
				if (line.Contains("NU190", StringComparison.Ordinal))
					_ = KsDebug.OutputDebug($"{label}: {line.Trim()}");

			return true;
		}

		/// <summary>
		/// Builds the restore-failure message. The "install the SDK" paragraph appears only when the SDK is actually
		/// the problem: printing it for a plain NU1101 (package not found, SDK working fine) buries the real error
		/// under advice the user has already followed.
		/// </summary>
		private static bool Fail(string dir, List<PackageRef> packages, string reason, string output, bool sdkMissing, string label, out string failure)
		{
			var advice = sdkMissing
						 ? """
						   Keysharp does not download packages itself — it asks the .NET SDK to resolve them, so the
						   SDK must be installed and on PATH the first time a package set is used on this machine.
						   Install it from https://dotnet.microsoft.com/download, or restore manually with:

						   """
						 : "Reproduce and investigate with:";
			var log = output.Length == 0 ? "" : "\n\n" + (output.Length > 4000 ? output[..4000] + " …" : output);
			failure = $"""
					   {label}: {reason}.

					   {advice}
					       cd "{dir}"
					       dotnet restore

					   Requested: {string.Join(", ", packages.Select(p => $"{p.Id} {p.Version}"))}{log}
					   """;
			return false;
		}

		// ---- assets file (steps 1 and 3) ----

		internal sealed class ResolvedPackage
		{
			internal string Id;
			internal string Version;
			internal string Root;
			internal readonly List<string> Managed = [];
			internal readonly List<string> Native = [];
		}

		/// <summary>
		/// Whether the restore that produced this directory's assets file succeeded, per the `success` flag NuGet
		/// writes to project.nuget.cache beside it. Absent or unreadable counts as failure, so an interrupted restore
		/// re-runs rather than being trusted.
		/// </summary>
		internal static bool RestoreSucceeded(string dir)
		{
			try
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "obj", "project.nuget.cache")));
				return doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Reads the SDK's resolved package graph. Returns null when the file is absent, unreadable, or names a file
		/// that is missing — the last case matters because the global packages folder is shared and can be cleared
		/// behind us, and a stale cache entry must fall through to a fresh restore rather than half-load. Whether the
		/// restore that wrote it succeeded at all is a separate question, answered by RestoreSucceeded.
		/// </summary>
		internal static List<ResolvedPackage> TryReadAssets(string assetsPath)
		{
			if (!File.Exists(assetsPath))
				return null;

			try
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
				var root = doc.RootElement;

				if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("libraries", out var libraries))
					return null;

				var folders = root.TryGetProperty("packageFolders", out var pf)
							  ? pf.EnumerateObject().Select(p => p.Name).ToList()
							  : [];

				if (folders.Count == 0 || !TrySelectTarget(targets, out var target))
					return null;

				var result = new List<ResolvedPackage>();

				foreach (var entry in target.EnumerateObject())
				{
					// "Id/Version"; anything not of type "package" (i.e. a project reference) has no cached assets.
					var slash = entry.Name.LastIndexOf('/');

					if (slash <= 0
						|| !libraries.TryGetProperty(entry.Name, out var lib)
						|| !lib.TryGetProperty("type", out var type) || type.GetString() != "package"
						|| !lib.TryGetProperty("path", out var relElem) || relElem.GetString() is not { } rel)
						continue;

					var baseDir = folders.Select(f => Path.Combine(f, rel.Replace('/', Path.DirectorySeparatorChar)))
										 .FirstOrDefault(Directory.Exists);

					if (baseDir == null)
						return null;   // package content is gone — treat the whole cache entry as stale

					var pkg = new ResolvedPackage
					{
						Id = entry.Name.Substring(0, slash), Version = entry.Name.Substring(slash + 1), Root = baseDir
					};

					if (!CollectAssets(entry.Value, "runtime", baseDir, pkg.Managed)
						|| !CollectAssets(entry.Value, "native", baseDir, pkg.Native))
						return null;

					result.Add(pkg);
				}

				return result.Count == 0 ? null : result;
			}
			catch (Exception)
			{
				return null;   // unparseable/unreadable — fall through to a fresh restore
			}
		}

		/// <summary>Prefers the RID-qualified target (which is what carries native and RID-specific managed assets).</summary>
		private static bool TrySelectTarget(JsonElement targets, out JsonElement target)
		{
			target = default;
			var found = false;

			foreach (var t in targets.EnumerateObject())
			{
				if (!t.Name.StartsWith(tfm, StringComparison.OrdinalIgnoreCase))
					continue;

				if (t.Name.EndsWith("/" + rid, StringComparison.OrdinalIgnoreCase))
				{
					target = t.Value;
					return true;
				}

				if (!found)
				{
					target = t.Value;
					found = true;
				}
			}

			return found;
		}

		private static bool CollectAssets(JsonElement package, string section, string baseDir, List<string> into)
		{
			if (!package.TryGetProperty(section, out var assets))
				return true;

			foreach (var asset in assets.EnumerateObject())
			{
				// "_._" is NuGet's explicit placeholder for "this package deliberately contributes nothing here".
				if (asset.Name.EndsWith("_._", StringComparison.Ordinal))
					continue;

				var full = Path.Combine(baseDir, asset.Name.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(full))
					return false;

				into.Add(full);
			}

			return true;
		}
	}
}
