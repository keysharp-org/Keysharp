#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// Fetches and caches an application's scripting definition. This is the type library of the Apple Events
	/// world: it supplies the four-character codes that drive marshalling and the member tables that make late
	/// binding work. A definition only changes when the application itself is replaced, so the cache is keyed by
	/// the bundle's write time and needs no invalidation while a script runs.
	/// </summary>
	internal static partial class AETerminology
	{
		private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

		// Keyed by application only. An application cannot be meaningfully replaced underneath a script that is
		// driving it, and re-checking the bundle's timestamp would put a filesystem call on every member lookup;
		// an application updated mid-run keeps its existing terminology until the script restarts.
		private static readonly ConcurrentDictionary<string, AESdefDictionary> cache = new (StringComparer.Ordinal);

		/// <summary>
		/// Applications known not to be scriptable at all. Only this settled answer is remembered: a definition
		/// that could not be read this time (the application is still starting, the tool timed out) is retried,
		/// because caching that would leave the application unusable for the rest of the run.
		/// </summary>
		private static readonly ConcurrentDictionary<string, string> notScriptable = new (StringComparer.Ordinal);

		[LibraryImport(Carbon)]
		private static partial int OSACopyScriptingDefinitionFromURL(nint url, int modeFlags, out nint sdef);

		internal static AESdefDictionary Get(AETarget target)
		{
			if (cache.TryGetValue(target.CacheKey, out var cached))
				return cached;

			if (notScriptable.TryGetValue(target.CacheKey, out var why))
				throw new AEException(AE.ErrAEEventNotHandled, why);

			AESdefDictionary dict;

			try
			{
				dict = Load(target, AETargets.PathOf(target));
			}
			catch (Exception ex)
			{
				throw new AEException(AE.ErrAEEventNotHandled,
									  $"Could not read the scripting definition of {target}: {ex.Message}");
			}

			if (dict.Suites.Count == 0)
			{
				var message = $"{target} publishes no scripting terminology, so it cannot be automated this way.";
				_ = notScriptable.TryAdd(target.CacheKey, message);
				throw new AEException(AE.ErrAEEventNotHandled, message);
			}

			_ = cache.TryAdd(target.CacheKey, dict);
			return dict;
		}

		private static AESdefDictionary Load(AETarget target, string path)
		{
			var xml = path != null ? CopyDefinition(path) : null;

			// The framework call needs an application bundle it can find; the command line tool covers the cases
			// it refuses, including applications whose terminology is still in the older aete resource.
			if (string.IsNullOrWhiteSpace(xml))
				xml = RunSdefTool(path);

			if (string.IsNullOrWhiteSpace(xml))
				throw new InvalidOperationException("no scripting definition was returned");

			return AESdef.Parse(xml, href => ResolveInclude(href, path));
		}

		private static string CopyDefinition(string appPath)
		{
			var url = CF.CreateFileUrl(appPath, isDirectory: true);

			if (url == 0)
				return null;

			try
			{
				var status = OSACopyScriptingDefinitionFromURL(url, 0, out var data);

				if (status != 0 || data == 0)
					return null;

				try
				{
					return Encoding.UTF8.GetString(CF.ReadData(data));
				}
				finally
				{
					CF.CFRelease(data);
				}
			}
			finally
			{
				CF.CFRelease(url);
			}
		}

		private static string RunSdefTool(string appPath)
		{
			if (string.IsNullOrEmpty(appPath))
				return null;

			try
			{
				using var process = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "/usr/bin/sdef",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};
				process.StartInfo.ArgumentList.Add(appPath);
				_ = process.Start();
				// Both pipes have to drain concurrently: reading one to the end first lets the other fill and
				// block the child, which would deadlock rather than time out.
				var stdout = process.StandardOutput.ReadToEndAsync();
				var stderr = process.StandardError.ReadToEndAsync();

				if (!process.WaitForExit(15_000))
				{
					try { process.Kill(entireProcessTree: true); } catch { }

					Diagnostics.Debug.WriteLine($"sdef did not finish for '{appPath}'.");
					return null;
				}

				var xml = stdout.GetAwaiter().GetResult();
				_ = stderr.GetAwaiter().GetResult();
				return process.ExitCode == 0 ? xml : null;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"sdef failed for '{appPath}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Reads an xi:include. Almost every application includes the standard suite this way, so skipping one
		/// would cost the common commands (get, set, count) that most scripts start from.
		/// </summary>
		private static string ResolveInclude(string href, string appPath)
		{
			try
			{
				string path;

				if (href.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
				{
					if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
						return null;

					path = uri.LocalPath;
				}
				else if (href.StartsWith('/'))
					path = href;
				else if (appPath != null)
					path = Path.Combine(appPath, "Contents", "Resources", href);
				else
					return null;

				return File.Exists(path) ? File.ReadAllText(path) : null;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Could not read the included definition '{href}': {ex.Message}");
				return null;
			}
		}

	}
}
#endif
