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
			public int Index;
			public ModInfo Root;
			public readonly Dictionary<string, ModInfo> Names = new(StringComparer.OrdinalIgnoreCase);
		}

		private sealed record ModuleSource(ModInfo Script = null, string Builtin = null)
		{
			public string Name => Script?.Name ?? Builtin;
			public bool IsAhk => Script?.IsAhk ?? StringComparer.OrdinalIgnoreCase.Equals(Builtin, "AHK");
		}

		private ModuleGroup _mainGroup;
		private ModInfo _ahkModule;
		private readonly Dictionary<string, ModuleGroup> _moduleFiles = new(SourcePathComparer);
		private readonly Dictionary<ImportDirective, ModuleSource> _importModules = new();
		private long _lastSourceOrder;

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

		private ModuleSource ImportSource(ImportDirective directive) => _importModules.GetValueOrDefault(directive);
		private ModuleSource AhkSource => new(_ahkModule, _ahkModule == null ? "AHK" : null);

		private ModuleSource FindNamedModule(ModuleGroup group, string name)
		{
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "__Main")) return new(_mainGroup.Root);
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "__Init") || name.Length == 0) return new(group.Root);
			if (StringComparer.OrdinalIgnoreCase.Equals(name, "AHK")) return AhkSource;
			if (group.Names.TryGetValue(name, out var module)) return new(module);
			return Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(name, out var type)
				&& typeof(Module).IsAssignableFrom(type) ? new(Builtin: Script.GetUserDeclaredName(type) ?? type.Name) : null;
		}

		private List<ModInfo> PartitionModules(List<Stmt> body, ModuleGroup group)
		{
			var mods = new List<ModInfo> { group.Root };
			var current = group.Root;
			foreach (var statement in body)
			{
				if (statement is DirectiveStmt directive && StringComparer.OrdinalIgnoreCase.Equals(directive.Name, "Module"))
				{
					var name = (directive.Args ?? "").Trim();
					current = FindNamedModule(group, name)?.Script;
					if (current == null)
					{
						current = new ModInfo { Name = name, Group = group, Dir = System.IO.Path.GetDirectoryName(group.Path) ?? _includeDir,
							IsAhk = StringComparer.OrdinalIgnoreCase.Equals(name, "AHK"),
							CodeName = StringComparer.OrdinalIgnoreCase.Equals(name, "AHK") ? "AHK"
								: group == _mainGroup ? NameMangler.ModuleClass(name) : "__KSFile" + group.Index + "_" + NameMangler.ModuleClass(name) };
						group.Names.Add(name, current);
						if (StringComparer.OrdinalIgnoreCase.Equals(name, "AHK")) _ahkModule = current;
					}
					if (!mods.Contains(current)) mods.Add(current);
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
					var path = import.Module;
					string submodule = null;
					var colon = path.LastIndexOf(':');
					if (colon >= 0 && (colon == path.Length - 1 || IsModuleSuffix(path.AsSpan(colon + 1))))
					{
						submodule = path[(colon + 1)..];
						path = path[..colon];
					}
					var source = submodule == null ? FindNamedModule(module.Group, path) : null;
					if (source == null)
					{
						var group = module.Group;
						if (path.Length > 0)
						{
							var localDir = import.File == null ? module.Dir : System.IO.Path.GetDirectoryName(import.File);
							var file = ResolveModuleFile(path, localDir ?? _includeDir);
							if (file == null) { _importModules[import] = null; continue; }
							file = System.IO.Path.GetFullPath(file);
							if (!_moduleFiles.TryGetValue(file, out group))
							{
								group = new ModuleGroup { Path = file, Index = _moduleFiles.Count + 1 };
								group.Root = new ModInfo { Name = path, CodeName = "__KSFile" + group.Index + "___Init", Group = group,
									Dir = System.IO.Path.GetDirectoryName(file) };
								_moduleFiles.Add(file, group);
								try
								{
									var text = System.IO.File.ReadAllText(file);
									var (program, diagnostics) = Parser.ParseWithDiagnostics(text, group.Root.Dir, file, _defines, MainScriptPath);
									_errorStdOutActive |= program.ErrorStdOut;
									var fileName = System.IO.Path.GetFileName(file);
									foreach (var diagnostic in diagnostics)
										Diag(diagnostic.StartsWith(fileName + ":", System.StringComparison.Ordinal) ? diagnostic : fileName + ":" + diagnostic);
									if (diagnostics.Count > 0) continue;
									AppendSourceOrder(program);
									AddSourceTexts(file, text, program);
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
						if (source == null) source = submodule == null ? new(group.Root) : FindNamedModule(group, submodule);
					}
					_importModules[import] = source;
				}
		}

		private static bool IsModuleSuffix(System.ReadOnlySpan<char> name)
		{
			if (name.IsEmpty) return false;
			foreach (var character in name)
				if (character < 128 && !(char.IsAsciiLetterOrDigit(character) || character == '_')) return false;
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
