using System.Collections.Generic;
using System.IO;
using Keysharp.Parsing.Lexing;

namespace Keysharp.Parsing.Syntax
{
	internal sealed partial class Parser
	{
		// Includes are expanded before parsing, so preserve the load-time mode at callable boundaries.
		private static bool BeginsDefinitionBlock(List<Token> tokens, int index)
		{
			var previous = index - 1;
			while (previous >= 0 && tokens[previous].Kind == TokenKind.Newline) previous--;
			if (previous < 0) return false;
			var end = tokens[previous];
			if (end.Kind == TokenKind.RParen)
			{
				var depth = 1;
				while (previous > 0 && depth > 0)
				{
					var kind = tokens[--previous].Kind;
					if (kind == TokenKind.RParen) depth++;
					else if (kind == TokenKind.LParen) depth--;
				}
			}
			while (previous > 0 && tokens[previous - 1].Kind is not (TokenKind.Newline or TokenKind.LBrace or TokenKind.RBrace)) previous--;
			var header = tokens[previous];
			if (header.IsKeyword("if") || header.IsKeyword("while") || header.IsKeyword("for")
				|| header.IsKeyword("switch") || header.IsKeyword("catch") || header.IsKeyword("loop")
				|| header.IsKeyword("else") || header.IsKeyword("try") || header.IsKeyword("finally")) return false;
			return header.IsKeyword("class") || header.IsKeyword("struct") || end.IsKeyword("get") || end.IsKeyword("set")
				|| end.Kind is TokenKind.RParen or TokenKind.HotkeyTrigger or TokenKind.HotstringTrigger;
		}

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
