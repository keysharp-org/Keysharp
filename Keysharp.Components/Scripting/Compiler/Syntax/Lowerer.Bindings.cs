using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Keysharp.Parsing.Syntax;
using Keysharp.Runtime;

namespace Keysharp.Compilation.Syntax
{
	internal sealed partial class Lowerer
	{
		private sealed record MemberBinding(string Owner, string Member, ModuleBindingKind Storage, ExportK Kind,
			string Name, string Constant = null, ModInfo Module = null, Stmt Declaration = null,
			System.Reflection.MemberInfo Builtin = null, bool Inline = false, bool Synthetic = false, ModuleSource ImportedModule = null,
			MemberDeclarationSyntax InlineDeclaration = null);

		private sealed record ModuleImport(ImportRef Bound, ImportDirective Directive);
		private ModInfo _boundModule;
		private bool _bindingsSealed;

		private static string ModuleOwner(ModInfo module) => "Program." + module.CodeName;
		private IEnumerable<string> ExportNames(ModuleSource source) => source.Script is { } module
			? source.IsAhk ? module.ExportNames.Concat(BuiltinExportKinds("AHK").Keys.Where(name => ExportsMember(module, name))) : module.ExportNames
			: BuiltinExportKinds(source.Builtin).Keys;
		private static bool ExportsMember(ModInfo module, string name) => module.ExportNames.Contains(name)
			|| module.IsAhk && !module.OwnBindings.ContainsKey(name) && !module.NamedBindings.ContainsKey(name)
				&& BuiltinMember("AHK", true, name) != null;
		private static ExpressionSyntax BindingKind(ModuleBindingKind kind) => Access("Keysharp.Runtime.ModuleBindingKind." + kind);
		private static ExpressionSyntax BindingOwner(MemberBinding target) => SyntaxFactory.TypeOfExpression(Ty(target.Owner));

		private ExpressionSyntax MemberRead(MemberBinding target) => target.Storage == ModuleBindingKind.Class ? TypeSingleton(target.Owner) : target.Inline
			? Inv(Access("Keysharp.Builtins.Misc.MemberGet"), BindingOwner(target), Str(target.Member), BindingKind(target.Storage))
			: target.Builtin != null ? BindMember(target.Builtin) : Access(target.Owner + "." + target.Member);

		private ExpressionSyntax MemberWrite(MemberBinding target, ExpressionSyntax value) => target.Inline
			? Inv(Access("Keysharp.Builtins.Misc.MemberSet"), BindingOwner(target), target.Member == null ? Null : Str(target.Member), BindingKind(target.Storage), value)
			: Assign(MemberRead(target), value);

		private ExpressionSyntax MemberReference(MemberBinding target, bool naked = false)
		{
			var reference = Inv(Access("Keysharp.Builtins.Misc.MemberRef"), BindingOwner(target), target.Member == null ? Null : Str(target.Member),
				BindingKind(target.Storage));
			return naked ? reference : Inv(Access("Keysharp.Builtins.Misc.MakeVarRef"), reference, Str(target.Name));
		}

		private void TrackBinding(MemberBinding target)
		{
			if (target.Builtin is Type type && type.IsDefined(typeof(ExperimentalAttribute), false))
				_experimentalTypes.Add(type);
			else if (target.Builtin is System.Reflection.MethodInfo method)
				TrackRuntimeComponent(method);
		}

		private static MemberBinding BuiltinBindingTarget(System.Reflection.MemberInfo member)
		{
			var kind = member switch
			{
				System.Reflection.PropertyInfo => ModuleBindingKind.BuiltinVariable,
				Type => ModuleBindingKind.Class,
				_ => ModuleBindingKind.Function
			};
			return new(member is Type type ? type.FullName.Replace('+', '.') : member.DeclaringType.FullName.Replace('+', '.'),
				member is Type ? null : member.Name, kind,
				member is Type ? ExportK.Type : member is System.Reflection.PropertyInfo ? ExportK.Variable : ExportK.Function,
				Script.GetUserDeclaredName(member) ?? member.Name, ConstantOf(member), Builtin: member);
		}

		private void ValidateModuleImports(List<ModInfo> modules)
		{
			foreach (var module in modules)
			foreach (var import in module.AllImports.OrderBy(item => item.SourceOrder))
			{
				if (import.Module.Length == 0 && !import.Quoted) continue;
				ActivateErrorStdOutBefore(module.Body, import.SourceOrder);
				var source = ImportSource(import);
				if (source == null)
					Diag($"{NodeAnchor(import)}#Import: module not found: {import.Module}");
				else if (source.Script == module && NamedImports(import.Named).Any(item => item.Name == "*"))
					Diag($"{NodeAnchor(import)}#Import: Invalid import: *");
				else if (source.Script == null && !source.IsAhk)
					foreach (var (name, _) in NamedImports(import.Named))
						if (name != "*" && BuiltinMember(source.Builtin, false, name) == null)
							Diag($"{NodeAnchor(import)}#Import: module '{import.Module}' has no exported member named '{name}'");
			}
		}

		private void BindModules(List<ModInfo> modules)
		{
			// Unknown named AHK imports introduce storage in that module, as ordinary script-module imports do.
			if (_ahkModule == null && modules.SelectMany(m => m.AllImports).Any(import =>
				ImportSource(import)?.IsAhk == true
				&& NamedImports(import.Named).Any(item => item.Name != "*" && BuiltinMember("AHK", true, item.Name) == null)))
			{
				_ahkModule = new ModInfo { Name = "AHK", CodeName = "AHK", Dir = _includeDir, Group = _mainGroup, IsAhk = true };
				modules.Add(_ahkModule);
				_mainGroup.Names["AHK"] = _ahkModule;
			}
			foreach (var import in _importModules.Keys.ToArray())
				if (_importModules[import]?.IsAhk == true) _importModules[import] = AhkSource;

			foreach (var module in modules)
			{
				var declarations = module.Body.Concat(NamedArrows(module.Body, null)).ToList();
				var locals = CollectLocalDeclNames(declarations);
				var globals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var statement in module.Body)
					VisitScope(statement, node =>
					{
						if (node is DeclStmt { Keyword: "global" } declaration)
							foreach (var item in declaration.Items)
								if (DeclItemName(item) is { } declared) globals.Add(declared);
					});
				foreach (var import in module.ModuleBindings.OrderBy(im => im.SourceOrder))
				{
					if (import.Module.Length == 0 && !import.Quoted) continue;
					void Add(string name, string member)
					{
						if (locals.Contains(name) || globals.Contains(name) || module.InlineMembers?.Contains(name) == true)
						{
							Diag($"{NodeAnchor(import)}This import declaration conflicts with an existing declaration: {name}");
							return;
						}
						var binding = new ModuleImport(new(ImportSource(import), member, null, name), import);
						if (module.NamedBindings.TryGetValue(name, out var existing))
						{
							if (!SameImport(existing.Bound, binding.Bound))
								Diag($"{NodeAnchor(import)}This import declaration conflicts with an existing import: {name}");
							else module.NamedBindings[name] = binding;
							return;
						}
						module.NamedBindings.Add(name, binding);
					}
					if (import.Alias != null) Add(import.Alias, null);
					else if (!import.Quoted) Add(ImportBindingName(import.Module), null);
					foreach (var (name, alias) in NamedImports(import.Named))
						if (name != "*") Add(alias, name);
				}
				foreach (var (name, kind) in module.Exports)
				{
					if (module.NamedBindings.ContainsKey(name)) continue;
					var declaration = declarations.FirstOrDefault(s => s is FunctionDecl f && StringComparer.OrdinalIgnoreCase.Equals(f.Name, name)
						|| s is ClassDecl c && StringComparer.OrdinalIgnoreCase.Equals(c.Name, name));
					module.OwnBindings[name] = new(ModuleOwner(module), NameMangler.Global(name),
						kind == ExportK.Type ? ModuleBindingKind.Property : ModuleBindingKind.Field, kind, name,
						ConstantOf(kind), module, declaration);
					module.ExportNames.Add(name);
				}
				GatherInlineBindings(module);
				foreach (var (name, binding) in module.NamedBindings)
					if (binding.Directive.ReExport) module.ExportNames.Add(name);
			}
			var declaredExports = modules.ToDictionary(module => module,
				module => new HashSet<string>(module.ExportNames, StringComparer.OrdinalIgnoreCase));

			bool CanExportWildcard(ModInfo module, string name)
			{
				if (name.StartsWith('_')) return false;
				if (module.NamedBindings.TryGetValue(name, out var imported)) return imported.Directive.ReExport;
				if (module.OwnBindings.TryGetValue(name, out var own)) return own.Synthetic || declaredExports[module].Contains(name);
				return !module.Bindings.ContainsKey(name) || declaredExports[module].Contains(name);
			}
			void CloseExports()
			{
				bool changed;
				do
				{
					changed = false;
					foreach (var module in modules)
					{
						// A wildcard export enables all wildcard lookup; a declared import keeps its own export flag.
						if (!module.ModuleBindings.Any(im => im.ReExport && NamedImports(im.Named).Any(item => item.Name == "*"))) continue;
						foreach (var import in module.ModuleBindings.Where(im => NamedImports(im.Named).Any(item => item.Name == "*")))
							foreach (var name in ExportNames(ImportSource(import)).ToArray())
								if (CanExportWildcard(module, name)) changed |= module.ExportNames.Add(name);
					}
				} while (changed);
			}
			// Collect every undeclared source name before resolving wildcard bindings.
			foreach (var module in modules)
				foreach (var import in module.AllImports)
					if (ImportSource(import)?.Script is { } source)
						foreach (var (name, _) in NamedImports(import.Named))
							if (name != "*" && !source.OwnBindings.ContainsKey(name) && !source.NamedBindings.ContainsKey(name)
								&& !(source.IsAhk && BuiltinMember("AHK", true, name) != null))
							{
								source.OwnBindings[name] = new(ModuleOwner(source), NameMangler.Global(name), ModuleBindingKind.Field,
									ExportK.Variable, name, Module: source, Synthetic: true);
								source.ExportNames.Add(name);
							}
			var candidates = modules.ToDictionary(module => module,
				module => module.OwnBindings.Where(item => item.Value.Synthetic).ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
			void RebuildExports()
			{
				foreach (var module in modules)
				{
					module.ExportNames.Clear();
					module.ExportNames.UnionWith(declaredExports[module]);
					foreach (var (name, _) in module.OwnBindings.Where(item => item.Value.Synthetic)) module.ExportNames.Add(name);
				}
				CloseExports();
			}
			RebuildExports();
			// An indirect import retains its target when a later import changes that target's export visibility.
			var anchors = new HashSet<MemberBinding>();
			foreach (var module in modules)
			{
				foreach (var (name, original) in candidates[module])
				{
					if (anchors.Contains(original)) continue;
					ImportDirective selected = null;
					var target = ResolveMemberBinding(new(module), name, selectedWildcard: import => selected = import);
					if (target == null || target == original) continue;
					if (target.Synthetic)
					{
						anchors.Add(target);
						target.Module.Bindings[target.Name] = target;
					}
					module.Bindings[name] = target;
					module.OwnBindings.Remove(name);
					if (selected?.ReExport == true) declaredExports[module].Add(name);
					RebuildExports();
				}
			}
			foreach (var module in modules)
				foreach (var import in module.AllImports)
					foreach (var (name, _) in NamedImports(import.Named))
						if (name != "*") ResolveMemberBinding(ImportSource(import), name, at: import);
			_bindingsSealed = true;

			foreach (var module in modules)
			{
				var names = new HashSet<string>(module.OwnBindings.Keys, StringComparer.OrdinalIgnoreCase);
				names.UnionWith(module.NamedBindings.Keys);
				foreach (var import in module.ModuleBindings.Where(im => NamedImports(im.Named).Any(item => item.Name == "*")))
					names.UnionWith(ExportNames(ImportSource(import)).Where(name => !name.StartsWith('_')));
				foreach (var name in names)
					if (ResolveMemberBinding(new(module), name) is { } binding)
					{
						module.Bindings[name] = binding;
						if (module.OwnBindings.TryGetValue(name, out var original) && original.Synthetic && original != binding)
							module.OwnBindings.Remove(name);
						else if (original?.Synthetic == true) module.DirectVariables.Add(name);
					}
				module.Exports.Clear();
				foreach (var name in module.ExportNames)
					if (module.Bindings.TryGetValue(name, out var binding)) module.Exports[name] = binding.Kind;
			}
		}

		private static bool SameImport(ImportRef left, ImportRef right) =>
			left.Source == right.Source && StringComparer.OrdinalIgnoreCase.Equals(left.Member, right.Member);

		private MemberBinding ResolveMemberBinding(ModuleSource source, string name, bool create = false, ImportDirective at = null,
			List<(ModInfo, string)> visiting = null, bool wildcard = false, Action<ImportDirective> selectedWildcard = null)
		{
			if (source == null) return null;
			if (source.Script is not { } module)
				return BuiltinMember(source.Builtin, source.IsAhk, name) is { } builtin
					? BuiltinBindingTarget(builtin) : null;
			if (module.Bindings.TryGetValue(name, out var cached)) return cached;
			if (module.OwnBindings.TryGetValue(name, out var own) && !own.Synthetic) return own;
			visiting ??= new();
			var key = (module, name);
			if (visiting.Any(item => item.Item1 == module && StringComparer.OrdinalIgnoreCase.Equals(item.Item2, name)))
			{
				if (!wildcard) Diag($"{NodeAnchor(at)}This import declaration conflicts with an existing import: {name}");
				return null;
			}
			visiting.Add(key);
			try
			{
				if (module.NamedBindings.TryGetValue(name, out var imported))
				{
					if (imported.Bound.Member == null)
						return Remember(new(ModuleOwner(module), NameMangler.Global(imported.Bound.Name), ModuleBindingKind.Field,
							ExportK.Module, imported.Bound.Name, "Module", module, ImportedModule: imported.Bound.Source));
					var target = ResolveMemberBinding(imported.Bound.Source, imported.Bound.Member, create: true,
						at: imported.Directive, visiting: visiting);
					if (target != null) Remember(target);
					return target;
				}
				if (!name.StartsWith('_') && !module.DirectVariables.Contains(name))
					foreach (var import in module.ModuleBindings.OrderByDescending(im => im.SourceOrder))
					{
						if (!NamedImports(import.Named).Any(item => item.Name == "*")) continue;
						var importedSource = ImportSource(import);
						if (importedSource.Script is { } script ? !ExportsMember(script, name)
							: !importedSource.IsAhk && !import.ReExport
								&& (!Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(importedSource.Builtin, out var type) || !BuiltinWildcardSupplies(type, name))) continue;
						if (ResolveMemberBinding(importedSource, name, visiting: visiting, wildcard: true) is { } target)
						{
							selectedWildcard?.Invoke(import);
							return Remember(target);
						}
					}
				if (source.IsAhk && BuiltinMember("AHK", true, name) is { } ahk)
					return BuiltinBindingTarget(ahk);
				if (own != null) return own;
				if (!create || !_bindingsSealed) return null;
				var field = new MemberBinding(ModuleOwner(module), NameMangler.Global(name), ModuleBindingKind.Field, ExportK.Variable, name, Module: module);
				module.OwnBindings[name] = field;
				module.ExportNames.Add(name);
				return field;
			}
			finally { visiting.Remove(key); }
			MemberBinding Remember(MemberBinding target)
			{
				if (_bindingsSealed) module.Bindings[name] = target;
				return target;
			}
		}

		private void GatherInlineBindings(ModInfo module)
		{
			foreach (var (name, path, directive) in _inlineBlocks ?? [])
			{
				if (name != module.CodeName || path != null) continue;
				foreach (var member in Parsed(directive).Members)
				{
					var facts = InlineFacts(member);
					if (!IsScriptVisible(member) || facts.Hidden) continue;
					if (member is TypeDeclarationSyntax type && facts.ScriptClass)
					{
						var visible = InlineDeclaredName(type, type.Identifier.ValueText);
						AddBinding(visible, new(ModuleOwner(module) + "." + type.Identifier.Text, null,
							ModuleBindingKind.Class, ExportK.Type, visible, "Class", module, Inline: true, InlineDeclaration: type));
						continue;
					}
					if (!member.Modifiers.Any(token => token.IsKind(SyntaxKind.StaticKeyword) || token.IsKind(SyntaxKind.ConstKeyword))) continue;
					if (member is MethodDeclarationSyntax method)
					{
						var visible = InlineDeclaredName(method, method.Identifier.ValueText);
						AddBinding(visible, new(ModuleOwner(module), method.Identifier.ValueText,
							ModuleBindingKind.Function, ExportK.Function, visible, "Func", module, Inline: true, InlineDeclaration: method));
					}
					else if (member is FieldDeclarationSyntax field)
						foreach (var variable in field.Declaration.Variables)
							Add(variable.Identifier.ValueText, ModuleBindingKind.Field,
								field.Modifiers.Any(token => token.IsKind(SyntaxKind.ReadOnlyKeyword) || token.IsKind(SyntaxKind.ConstKeyword)));
					else if (member is PropertyDeclarationSyntax property)
						Add(property.Identifier.ValueText, ModuleBindingKind.Property,
							property.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
								&& !accessor.Modifiers.Any(token => token.IsKind(SyntaxKind.PrivateKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword)
									|| token.IsKind(SyntaxKind.InternalKeyword))) != true);
					void Add(string physical, ModuleBindingKind kind, bool readOnly)
					{
						var visible = InlineDeclaredName(member, physical);
						AddBinding(visible, new(ModuleOwner(module), physical, kind, ExportK.Variable, visible,
							readOnly ? "built-in variable" : null, module, Inline: true));
					}
					void AddBinding(string visible, MemberBinding binding)
					{
						if (module.OwnBindings.TryGetValue(visible, out var existing) && existing.Inline)
							Diag($"{NodeAnchor(directive)}#CSharp: '{visible}' conflicts with another declaration in module '{module.Name}'");
						else if (existing?.Declaration is FunctionDecl && member is MethodDeclarationSyntax)
							Diag($"{NodeAnchor(directive)}'{visible}' is declared both as a script function and as a public method in a #CSharp block; rename one of them");
						else if (existing?.Declaration != null)
							Diag($"{NodeAnchor(directive)}#CSharp: '{visible}' collides with a variable or function the script declares; rename one of them");
						else module.OwnBindings[visible] = binding;
					}
				}
			}
		}

		private List<MemberDeclarationSyntax> EmitBoundImports(ModInfo module)
		{
			_boundModule = module;
			foreach (var import in module.ModuleBindings) RecordModuleImport(import);
			var bridges = new List<MemberDeclarationSyntax>();
			foreach (var (name, target) in module.Bindings)
			{
				if (module.OwnBindings.ContainsKey(name)) continue;
				var direct = module.NamedBindings.GetValueOrDefault(name);
				if (direct != null) TrackBinding(target);
				var bound = direct?.Bound ?? new ImportRef(target.Module == null ? AhkSource : new(target.Module), target.Name, target.Constant, name);
				bound = bound with { Constant = target.Constant, Name = direct?.Bound.Name ?? name };
				_importMembers[name] = bound;
				_fields[name] = NameMangler.Global(name);
				if (direct?.Bound.Member == null && direct != null)
				{
					var source = direct.Bound.Source;
					var value = ModuleObjectExpr(source);
					if (value != null) _fieldDecls.Add(Declared(ConstantField(NameMangler.Global(name), value), name));
					continue;
				}
				if (_inlineBlocks?.Any(block => block.Module == module.CodeName) == true)
				{
					var id = NameMangler.Global(name);
					var property = ObjArrowProp(id, MemberRead(target));
					if (target.Constant == null)
						property = property.WithExpressionBody(null).WithSemicolonToken(default)
							.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new[]
							{
								SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(MemberRead(target))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
								SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(MemberWrite(target, Id("value")))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
							})));
					bridges.Add(property.AddAttributeLists(Attr("Keysharp.Runtime.PublicHiddenFromUser")));
				}
			}
			return bridges;
		}

		private ImportBinding ResolvedImportBinding(ModuleSource module, string member, string alias, ImportDirective directive = null)
		{
			var target = ResolveMemberBinding(module, member, create: true, at: directive);
			if (target != null) TrackBinding(target);
			return target == null ? null : new ImportBinding
			{
				Read = () => MemberRead(target),
				Write = value => MemberWrite(target, value),
				Target = target,
				Bound = new(module, member, target.Constant, alias),
				Directive = directive
			};
		}

		private MemberBinding ImportedTarget(string name)
		{
			if (_boundModule?.Bindings.TryGetValue(name, out var target) == true) return target;
			if (_boundModule?.DirectVariables.Contains(name) == true || _userFuncByLower.ContainsKey(name) || _userClassDeclByLower.ContainsKey(name))
				return null;
			return _ahkModule?.Bindings.GetValueOrDefault(name);
		}

		private MemberBinding ResolvedReferenceTarget(string name, NameBinding binding) => binding.Kind == NameKind.ScopedImport
			? binding.Import.Target : binding.Kind == NameKind.BuiltinProperty ? BuiltinBindingTarget(binding.Builtin)
			: binding.Kind == NameKind.ModuleField || binding is { Kind: NameKind.ScopeVariable, Variable.Storage: VarStorage.Global }
				? ImportedTarget(name) : null;

		private ClassDeclarationSyntax AddBindingMetadata(ClassDeclarationSyntax declaration, ModInfo module)
		{
			if (module == null) return declaration;
			foreach (var (name, target) in module.Bindings)
				if ((!module.OwnBindings.ContainsKey(name) || target is { Inline: true, Storage: ModuleBindingKind.Function or ModuleBindingKind.Class })
					&& !(module.NamedBindings.TryGetValue(name, out var imported) && imported.Bound.Member == null))
					declaration = declaration.AddAttributeLists(Attr("Keysharp.Runtime.ModuleBinding", Str(name), BindingOwner(target),
						target.Member == null ? Null : Str(target.Member), BindingKind(target.Storage)));
			return declaration;
		}
	}
}
