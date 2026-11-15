using System.Collections.Generic;
using System.IO;
using Keysharp.Parsing.Lexing;

namespace Keysharp.Parsing.Syntax
{
	internal sealed partial class Parser
	{
		internal static bool? RequiresBackCompat(string args)
		{
			var words = (args ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 2) return null;
			if (words[0].Equals("Keysharp", StringComparison.OrdinalIgnoreCase)) return false;
			return words[0].Equals("AutoHotkey", StringComparison.OrdinalIgnoreCase)
				? Keysharp.Runtime.CompatibilityVersions.MeetsRequirement("2.0.x", words.AsSpan(1)) : null;
		}

		internal static List<string> DirectiveSearchPath(bool include, string scriptDir, string moduleFile, string mainScriptPath)
		{
			if (moduleFile == "*") moduleFile = null;
			var moduleDir = Path.GetDirectoryName(moduleFile);
			if (string.IsNullOrEmpty(moduleDir)) moduleDir = scriptDir ?? Directory.GetCurrentDirectory();
			var dirs = new List<string>();
			void Add(string path)
			{
				try
				{
					path = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(moduleDir, path));
					if (!path.IsNamedPipe() && Directory.Exists(path) && !dirs.Contains(path, SourcePathComparer)) dirs.Add(path);
				}
				catch { }
			}
			Add(include ? Path.Combine(moduleDir, "Lib") : moduleDir);
			if (!string.IsNullOrEmpty(scriptDir)) Add(include ? Path.Combine(scriptDir, "Lib") : scriptDir);
			var spec = Environment.GetEnvironmentVariable("AhkLibPath");
			if (string.IsNullOrEmpty(spec))
				foreach (var library in SharedLibraryDirs()) Add(library.Dir);
			else
				foreach (var path in ExpandPathVars(spec, scriptDir, moduleFile, mainScriptPath).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
					Add(NormalizeDirectiveSeparators(path));
			return dirs;
		}

		// Exact names precede suffixes within each directory. Only v2.0 includes truncate underscore names.
		internal static string FindLibraryFile(string name, string scriptDir, IReadOnlyList<string> extensions,
			string localDir = null, string moduleFile = null, string mainScriptPath = null, bool compatible = true)
		{
			if (string.IsNullOrEmpty(name)) return null;
			name = NormalizeDirectiveSeparators(name);
			var dirs = DirectiveSearchPath(true, scriptDir, moduleFile, mainScriptPath);
			if (!compatible && !string.IsNullOrEmpty(localDir)) dirs.Insert(0, localDir);
			while (true)
			{
				foreach (var dir in dirs)
				{
					string path;
					try { path = Path.GetFullPath(Path.Combine(dir, name)); }
					catch { continue; }
					if (path.IsNamedPipe()) continue;
					if (!compatible && File.Exists(path)) return path;
					foreach (var extension in extensions)
						if (File.Exists(path + extension)) return path + extension;
				}
				var underscore = name.LastIndexOf('_');
				if (!compatible || underscore < 0) return null;
				name = name[..underscore];
			}
		}
	}
}
