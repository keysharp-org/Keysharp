using System.Collections.Generic;
using System.Linq;
using Keysharp.Parsing.Syntax;
using Keysharp.Runtime;

namespace Keysharp.Compilation.Syntax
{
	internal sealed partial class Lowerer
	{
		private sealed class ModuleGroup
		{
			public string Path;
			public ModInfo Root;
			public List<string> SearchPath;
			public readonly Dictionary<string, ModInfo> Names = new(StringComparer.OrdinalIgnoreCase);
		}

		private sealed record ModuleSource(ModInfo Script = null, Type BuiltinType = null)
		{
			public bool IsAhk => Script?.IsAhk ?? BuiltinType == typeof(Ahk);
		}

		private ModuleGroup _mainGroup;
		private ModInfo _ahkModule;
		private readonly Dictionary<string, ModuleGroup> _moduleFiles = new(SourcePathComparer);
		private readonly Dictionary<ImportDirective, ModuleSource> _importModules = new();
		private readonly List<int> _includeParents = [];   // by inclusion, see AppendInclusions
		private long _lastSourceOrder;

		// An imported file's initial module is named for its file, numbered only when another file has taken the name. A
		// file's group joins _moduleFiles once it is named.
		private string FileModuleClass(string file)
		{
			var name = NameMangler.FileModuleClass(System.IO.Path.GetFileNameWithoutExtension(file));
			var unique = name;
			for (var number = 2; _moduleFiles.Values.Any(group => group.Root.ClassName == unique); number++) unique = name + "_" + number;
			return unique;
		}

		private void AppendSourceOrder(ProgramNode program)
		{
			var seen = new HashSet<Node>();
			var pending = new Stack<Node>();
			var highest = 0L;
			pending.Push(program);
			while (pending.TryPop(out var node))
			{
				if (node == null || !seen.Add(node)) continue;
				if (node is Stmt statement && statement.SourceOrder > 0)
				{
					highest = System.Math.Max(highest, statement.SourceOrder);
					statement.SourceOrder += _lastSourceOrder;
				}
				foreach (var child in AstChildren.Of(node)) pending.Push(child);
			}
			_lastSourceOrder += highest;
		}

		// Each file numbers its #Include splices from zero but a module's body can span files, so the numbers are made unique
		// across the script. Returns the number of the file's own splice, which has no parent.
		private int AppendInclusions(ProgramNode program)
		{
			var root = _includeParents.Count;
			_includeParents.Add(-1);
			foreach (var parent in program.IncludeParents) _includeParents.Add(parent + root);
			foreach (var statement in program.Body) statement.Inclusion += root;
			return root;
		}

		private ModuleSource ImportSource(ImportDirective directive) => _importModules.GetValueOrDefault(directive);
		private ModuleSource AhkSource => new(_ahkModule, _ahkModule == null ? typeof(Ahk) : null);

		private ModuleSource FindNamedModule(ModuleGroup group, string name)
		{
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "__Main")) return new(_mainGroup.Root);
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "__Init") || name.Length == 0) return new(group.Root);
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "AHK")) return AhkSource;
			if (group.Names.TryGetValue(name, out var module)) return new(module);
			return Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(name, out var type)
				&& typeof(Module).IsAssignableFrom(type) ? new(BuiltinType: type) : null;
		}

		// The child a script declared with `#Module Parent:Name`, a file's module under the file's initial module, or a nested
		// type of a built-in module.
		private static ModuleSource NestedChild(ModuleSource parent, string name) => parent?.Script is { } script
			? script.Nested.TryGetValue(name, out var child) || script == script.Group.Root && script.Group.Names.TryGetValue(name, out child)
				? new(child) : null
			: parent?.BuiltinType is { } builtin && FindModuleMember(builtin, name) is Type nested && typeof(Module).IsAssignableFrom(nested) ? new(BuiltinType: nested) : null;

		// AutoHotkey's Var::ValidateName, for a module name or one segment of a nested one.
		private static string ModuleNameError(string name) =>
			char.IsAsciiDigit(name[0]) ? $"This module name starts with a number, which is not allowed: \"{name}\""
			: !IsIdentifierText(name) ? $"The following module name contains an illegal character: \"{name}\""
			: Parser.IsReservedWord(name) ? $"The following reserved word must not be used as a module name: \"{name}\""
			: null;

		// Whether a #Module directive in this body names `name` itself, which makes it the group's own module however late.
		private static bool DeclaresModule(List<Stmt> body, string name) => body.Any(statement => statement is DirectiveStmt directive
			&& StringComparer.OrdinalIgnoreCase.Equals(directive.Name, "Module")
			&& StringComparer.OrdinalIgnoreCase.Equals((directive.Args ?? "").Trim(), name));

		// The module a #Module directive names, reopened or created with any parent it lacks, or null when the name is
		// rejected. A nested module belongs to its parent's group but is not one of its names: only its parent finds it.
		private ModInfo DeclareModule(DirectiveStmt directive, List<Stmt> body, ModuleGroup group, List<ModInfo> mods)
		{
			var name = (directive.Args ?? "").Trim();
			var segments = name.Split(':');
			var error = name.Length == 0 ? "Parameter #1 required."
				: segments.Length > 1 && segments.Any(segment => segment.Length == 0 || segment[0] == ' ' || segment[^1] == ' ')
					? "a nested module name is written A:B, with no spaces around the colon"
				: segments.Select(ModuleNameError).FirstOrDefault(message => message != null);
			// A file's initial module holds the file's modules, so `__Init:Name` and `__Main:Name` are that file's module Name,
			// as "Path:Name" is from another file.
			while (error == null && segments.Length > 1 && FindNamedModule(group, segments[0])?.Script is { } root && root == root.Group.Root)
				(group, segments) = (root.Group, segments[1..]);
			var first = FindNamedModule(group, segments[0]);
			error ??= segments.Length > 1 && (first is { IsAhk: true } || first is { Script: null } && !DeclaresModule(body, segments[0]))
				? "a built-in module cannot be a parent: " + segments[0] : null;
			if (error != null)
			{
				ActivateErrorStdOutBefore(body, directive.SourceOrder);
				Diag(NodeAnchor(directive) + error);
				return null;
			}

			ModInfo Create(string segment, ModInfo parent)
			{
				var owner = parent?.Group ?? group;
				// The class is named once every module's members are known.
				var created = new ModInfo { Name = parent == null ? segment : parent.Name + ":" + segment,
					Enclosing = parent ?? (owner == _mainGroup ? null : owner.Root), Group = owner,
					Dir = System.IO.Path.GetDirectoryName(owner.Path) ?? _includeDir };
				mods.Add(created);
				return created;
			}

			var module = first?.Script;
			if (module == null)
			{
				module = Create(segments[0], null);
				group.Names.Add(segments[0], module);
				if (StringComparer.OrdinalIgnoreCase.Equals(segments[0], "AHK"))
				{
					module.IsAhk = true;
					module.Compat = Script.DefaultCompatibilityVersion;   // it exists already, so its directive only reopens it
					(module.ClassName, module.Enclosing) = ("AHK", null);
					_ahkModule = module;
				}
			}
			foreach (var segment in segments.Skip(1))
			{
				if (!module.Nested.TryGetValue(segment, out var child))
					module.Nested.Add(segment, child = Create(segment, module));
				module = child;
			}
			// A parent created for a child takes this from its own directive, which can be in another splice than the child's.
			if (module.MainInclusion < 0) module.MainInclusion = directive.Inclusion;
			if (!mods.Contains(module)) mods.Add(module);
			return module;
		}

		private List<ModInfo> PartitionModules(List<Stmt> body, ModuleGroup group)
		{
			var mods = new List<ModInfo> { group.Root };
			var current = group.Root;
			current.Compat = Script.DefaultCompatibilityVersion;
			// The mode a definition takes, as AutoHotkey's load-time mBackCompatMode: a file starts in v2.0 mode whatever imports
			// it, an #Include in the mode its includer had there, and the end of an #Include restores that. Only creating a module
			// changes it, not reopening one.
			var modes = new Dictionary<int, Semver.SemVersion>();
			Semver.SemVersion ModeOf(int inclusion) => modes.TryGetValue(inclusion, out var mode) ? mode
				: modes[inclusion] = _includeParents[inclusion] < 0 ? Script.DefaultCompatibilityVersion : ModeOf(_includeParents[inclusion]);

			foreach (var statement in body)
			{
				if (statement is DirectiveStmt directive && StringComparer.OrdinalIgnoreCase.Equals(directive.Name, "Module"))
				{
					var next = DeclareModule(directive, body, group, mods) ?? current;
					if (next != current)
					{
						current = next;
						// Preserve the boundary when reopened sections are grouped into one module body.
						current.Body.Add(new DirectiveStmt("HotIf", "")
						{
							Line = directive.Line, Column = directive.Column, File = directive.File,
							SourceOrder = directive.SourceOrder, Inclusion = directive.Inclusion
						});
					}
					if (current.Compat == null) modes[directive.Inclusion] = current.Compat = group.Root.Compat;
				}
				else if (statement is ImportDirective import)
				{
					current.ModuleBindings.Add(import);
					current.AllImports.Add(import);
				}
				else
				{
					current.Body.Add(statement);
					CollectNestedImports(statement, current.ModuleBindings);
					CollectStatements(statement, current.AllImports);
					if (RequiresMode(statement) is { } required)
					{
						modes[statement.Inclusion] = required;
						// Only the file declaring a module sets the module's own mode, as IsFileMain has it.
						if (!current.CompatSet && statement.Inclusion == current.MainInclusion) (current.Compat, current.CompatSet) = (required, true);
					}
					else if (statement is FunctionDecl or ClassDecl or HotkeyDef or HotstringDef) _definitionCompat[statement] = ModeOf(statement.Inclusion);
				}
			}
			return mods;
		}

		private void LoadFileModules(List<ModInfo> mods)
		{
			var queue = new Queue<ModInfo>(mods);
			while (queue.TryDequeue(out var module))
				foreach (var import in module.AllImports.OrderBy(item => item.SourceOrder).ToArray())
				{
					if (_importModules.ContainsKey(import)) continue;
					ActivateErrorStdOutBefore(module.Body, import.SourceOrder);
					var (path, segments) = SplitModuleSpecifier(import.Module);
					// A nested path resolves only where a nested child exists, which an AutoHotkey script cannot declare, so what
					// it writes reaches its file.
					var source = segments.Aggregate(FindNamedModule(module.Group, path), NestedChild);
					if (source == null)
					{
						var group = module.Group;
						if (path.Length > 0)
						{
							var localDir = (import.File == null ? module.Dir : System.IO.Path.GetDirectoryName(import.File)) ?? _includeDir;
							var file = ResolveModuleFile(path, localDir, module.Group);
							if (file == null) { _importModules[import] = null; continue; }
							file = System.IO.Path.GetFullPath(file);
							if (!_moduleFiles.TryGetValue(file, out group))
							{
								group = new ModuleGroup { Path = file };
								group.Root = new ModInfo { Name = path, ClassName = FileModuleClass(file), Group = group,
									Dir = System.IO.Path.GetDirectoryName(file) };
								_moduleFiles.Add(file, group);
								try
								{
									var text = System.IO.File.ReadAllText(file);
									var (program, diagnostics) = Parser.ParseWithDiagnostics(text, group.Root.Dir, file, _defines, MainScriptPath, scriptDir: _includeDir);
									_errorStdOutActive |= program.ErrorStdOut;
									var fileName = System.IO.Path.GetFileName(file);
									foreach (var diagnostic in diagnostics)
										Diag(diagnostic.StartsWith(fileName + ":", System.StringComparison.Ordinal) ? diagnostic : fileName + ":" + diagnostic);
									if (diagnostics.Count > 0) continue;
									AppendSourceOrder(program);
									AddSourceTexts(file, text, program);
									group.Root.MainInclusion = AppendInclusions(program);
									foreach (var discovered in PartitionModules(program.Body, group))
									{
										if (!mods.Contains(discovered)) mods.Add(discovered);
										PrescanManifestDirectives(discovered.Body);
										queue.Enqueue(discovered);
									}
								}
								catch (System.Exception error) { Diag($"#Import: failed to read module '{path}' from {System.IO.Path.GetFileName(file)}: {error.Message}"); }
							}
						}
						source = segments.Length == 0 ? new(group.Root)
							: segments.Skip(1).Aggregate(NestedChild(new(group.Root), segments[0]) ?? FindNamedModule(group, segments[0]), NestedChild);
					}
					_importModules[import] = source;
				}
		}

		// Splits a specifier into the head and the module names after it: colons are taken from the right while the text
		// after each is an identifier, or nothing after a trailing colon, as AutoHotkey takes its one. The rest is the head,
		// whatever files exist, so a path ending in `:name` is written with its extension ("C:Lib.ahk:Sub").
		private static (string Head, string[] Segments) SplitModuleSpecifier(string specifier)
		{
			var head = specifier.Length;
			for (var colon = specifier.LastIndexOf(':'); colon >= 0; colon = colon > 0 ? specifier.LastIndexOf(':', colon - 1) : -1)
			{
				var text = specifier.AsSpan(colon + 1, head - colon - 1);
				if (!IsIdentifierText(text) && !(text.IsEmpty && head == specifier.Length)) break;
				head = colon;
			}
			return head == specifier.Length ? (specifier, []) : (specifier[..head], specifier[(head + 1)..].Split(':'));
		}

		private static bool IsIdentifierText(System.ReadOnlySpan<char> name)
		{
			if (name.IsEmpty) return false;
			foreach (var character in name)
				if (!Keysharp.Parsing.Lexing.Lexer.IsIdentPart(character)) return false;
			return true;
		}

		private List<string> ComputeExecOrder(List<ModInfo> modules)
		{
			var order = new List<string>();
			var visited = new HashSet<ModInfo>();
			void Visit(ModInfo module)
			{
				if (!visited.Add(module)) return;
				foreach (var import in module.AllImports)
					if (ImportSource(import)?.Script is { } dependency) Visit(dependency);
				order.Add(module.CodeName);
			}
			for (var index = modules.Count - 1; index >= 0; index--) Visit(modules[index]);
			return order;
		}
	}
}
