using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Keysharp.Parsing.Syntax;

namespace Keysharp.Compilation.Syntax
{
	internal sealed partial class Lowerer
	{
		private readonly record struct InlineMemberFacts(string Name = null, bool Exported = false, bool Hidden = false, bool ScriptClass = false, bool StructClass = false);
		private Dictionary<SyntaxNode, InlineMemberFacts> _inlineFacts;
		private IReadOnlyList<MetadataReference> _inlineReferences;
		private IReadOnlyList<ModInfo> _inlineModules;
		internal Func<IReadOnlyList<Keysharp.Internals.Os.PackageResolver.PackageRef>, IReadOnlyList<MetadataReference>> InlineReferenceProvider { get; init; }

		private bool PrepareInlineDeclarations(IReadOnlyList<ModInfo> modules)
		{
			if (_inlineBlocks == null) return true;
			_inlineModules = modules;
			foreach (var (_, _, directive) in _inlineBlocks)
				ReportInlineSyntaxErrors(Parsed(directive), directive.CodeLine - 1, directive.CodeFile ?? _scriptPath ?? "*");
			if (Diagnostics.Count > 0) return false;
			_inlineReferences = InlineReferenceProvider?.Invoke(_packages);
			return InlineReferenceProvider == null || _inlineReferences != null;
		}

		private InlineMemberFacts InlineFacts(SyntaxNode member)
		{
			if (member is MemberDeclarationSyntax { AttributeLists.Count: 0 } and not TypeDeclarationSyntax
				|| member is ParameterSyntax { AttributeLists.Count: 0 })
				return default;
			if (_inlineFacts == null) BindInlineDeclarationFacts();
			return _inlineFacts.GetValueOrDefault(member);
		}

		private string InlineDeclaredName(SyntaxNode member, string physicalName) => InlineFacts(member).Name ?? physicalName;
		private bool InlineExported(MemberDeclarationSyntax member) => InlineFacts(member).Exported;

		private void CheckInlineClassMember(MemberDeclarationSyntax member, string at, bool generatedClass)
		{
			if (InlineExported(member))
				Diag($"{at}#CSharp: [Export] is valid only at module scope");
			CheckInlineClassMemberReceiver(member, at);
			if (member is MethodDeclarationSyntax method && IsScriptVisible(method))
				_ = CheckInlineBoundarySignature(method, at);
			if (generatedClass)
				foreach (var name in DeclaredNames(member))
					if (Keywords.InlineReservedClassNames.Contains(name))
						Diag($"{at}#CSharp: '{name}' is a name Keysharp generates into this class; rename it");
			CheckInlineBoundaryProperty(member, at);
		}

		private void CheckInlineBoundaryProperty(MemberDeclarationSyntax member, string at)
		{
			if (member is PropertyDeclarationSyntax property && IsScriptVisible(property) && IsUnsupportedBoundaryReturn(property.Type))
				Diag($"{at}#CSharp: public property '{property.Identifier.Text}' has a type that cannot be handed to a script. "
					+ "Use a supported scalar/reference type or make it non-public.");
		}

		private void CheckInlineScriptTypeMembers(MemberDeclarationSyntax member, string file, int lineOffset, int wrapperOffset)
		{
			if (member is not TypeDeclarationSyntax type || !IsScriptVisible(type) || InlineFacts(type) is not { ScriptClass: true, Hidden: false })
				return;
			foreach (var child in type.Members)
			{
				var line = lineOffset + child.GetLocation().GetLineSpan().StartLinePosition.Line - wrapperOffset + 1;
				CheckInlineClassMember(child, $"{System.IO.Path.GetFileName(file)}:{line}:1: ", false);
				CheckInlineScriptTypeMembers(child, file, lineOffset, wrapperOffset);
			}
		}

		private MemberDeclarationSyntax MarkInlineBoundary(MemberDeclarationSyntax member, bool moduleScope)
		{
			var emitted = member is TypeDeclarationSyntax type && InlineFacts(type).ScriptClass
				? type.WithMembers(SyntaxFactory.List(type.Members.Select(child => MarkInlineBoundary(child, false)))) : member;
			if (!IsScriptVisible(member) || member is not (MethodDeclarationSyntax or PropertyDeclarationSyntax)
				&& !(moduleScope && member is FieldDeclarationSyntax))
				return emitted;
			// Leading directives must precede the injected attribute and retain their line boundary.
			var leading = emitted.GetLeadingTrivia();
			emitted = emitted.WithoutLeadingTrivia();
			return emitted.WithAttributeLists(emitted.AttributeLists.Insert(0, Attr("Keysharp.Runtime.InlineCSharp")))
				.WithLeadingTrivia(leading);
		}

		// Script types provide their identity and Any ancestry here; the final compilation validates their actual bases.
		private static ClassDeclarationSyntax InlineScriptClassSkeleton(ClassDecl declaration) =>
			SyntaxFactory.ClassDeclaration(NameMangler.ClassType(declaration.Name)).AddModifiers(PublicTok, PartialTok)
				.WithBaseList(BaseList(declaration.IsStruct ? "Keysharp.Builtins.Struct" : "Keysharp.Builtins.Any"))
				.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(declaration.Nested.Select(InlineScriptClassSkeleton)));

		// Bind attributes in their real C# scope so aliases, constants and nameof have the same meaning as in the final compilation.
		private void BindInlineDeclarationFacts()
		{
			_inlineFacts = new(ReferenceEqualityComparer.Instance);
			var declarations = new List<SyntaxNode>();
			var trees = new List<SyntaxTree>();
			foreach (var module in _inlineModules)
			{
				var members = new List<MemberDeclarationSyntax>
				{
					SyntaxFactory.ClassDeclaration(module.CodeName).AddModifiers(PublicTok, PartialTok)
						.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(module.Body.OfType<ClassDecl>().Select(InlineScriptClassSkeleton)))
				};
				var usings = new HashSet<string>(StringComparer.Ordinal)
				{
					"using System;", "using System.Collections.Generic;", "using System.Runtime.CompilerServices;",
					"using System.Runtime.InteropServices;", "using Keysharp.Runtime;"
				};
				foreach (var (_, path, directive) in _inlineBlocks.Where(block => block.Module == module.CodeName).OrderBy(block => block.Dir.SourceOrder))
				{
					var parsed = Parsed(directive);
					foreach (var use in parsed.Usings) usings.Add(use);
					var blockMembers = new List<MemberDeclarationSyntax>();
					foreach (var declaration in parsed.Members)
					{
						var annotated = declaration.ReplaceNodes(declaration.DescendantNodesAndSelf()
							.Where(node => node is TypeDeclarationSyntax or MemberDeclarationSyntax { AttributeLists.Count: > 0 }
								or ParameterSyntax { AttributeLists.Count: > 0 }), (original, rewritten) =>
							{
								var annotation = new SyntaxAnnotation("InlineDeclaration", declarations.Count.ToString(CultureInfo.InvariantCulture));
								declarations.Add(original);
								return rewritten.WithAdditionalAnnotations(annotation);
							});
						blockMembers.Add(FreezeConditionals(annotated));
					}
					var owner = SyntaxFactory.ClassDeclaration(module.CodeName)
						.AddModifiers(PublicTok, PartialTok);
					var nested = blockMembers;
					foreach (var part in (path?.Split('.') ?? []).Reverse())
						nested = [SyntaxFactory.ClassDeclaration(part).AddModifiers(PublicTok, PartialTok).WithMembers(SyntaxFactory.List(nested))];
					members.Add(owner.WithMembers(SyntaxFactory.List(nested)));
				}
				var program = SyntaxFactory.ClassDeclaration("Program").AddModifiers(PublicTok, PartialTok)
					.WithMembers(SyntaxFactory.List(members));
				var scope = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("Keysharp.CompiledMain"))
					.WithUsings(SyntaxFactory.List(usings.Select(text => SyntaxFactory.ParseCompilationUnit(text).Usings[0])))
					.AddMembers(program);
				trees.Add(CSharpSyntaxTree.Create(SyntaxFactory.CompilationUnit().AddMembers(scope)));
			}
			var references = _inlineReferences ?? CompilerHelper.CompilationReferences(_includeDir ?? Directory.GetCurrentDirectory(), true);
			var compilation = CSharpCompilation.Create("InlineDeclarations", trees, references,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
			var nameAttribute = compilation.GetTypeByMetadataName(typeof(UserDeclaredNameAttribute).FullName);
			var exportAttribute = compilation.GetTypeByMetadataName(typeof(Export).FullName);
			var hiddenAttribute = compilation.GetTypeByMetadataName(typeof(PublicHiddenFromUser).FullName);
			var anyType = compilation.GetTypeByMetadataName(typeof(Keysharp.Builtins.Any).FullName);
			var structType = compilation.GetTypeByMetadataName(typeof(Keysharp.Builtins.Struct).FullName);
			foreach (var tree in trees)
			{
				var model = compilation.GetSemanticModel(tree);
				foreach (var node in tree.GetRoot().GetAnnotatedNodes("InlineDeclaration"))
				{
					var original = declarations[int.Parse(node.GetAnnotations("InlineDeclaration").Single().Data, CultureInfo.InvariantCulture)];
					var symbol = model.GetDeclaredSymbol(node is FieldDeclarationSyntax field ? field.Declaration.Variables.First() : node);
					var attributes = symbol?.GetAttributes() ?? [];
					bool Has(INamedTypeSymbol attribute) => attribute != null && attributes.Any(item => SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute));
					var name = attributes.FirstOrDefault(item => nameAttribute != null && SymbolEqualityComparer.Default.Equals(item.AttributeClass, nameAttribute));
					var visible = name is { ConstructorArguments.Length: > 0 } ? name.ConstructorArguments[0].Value as string : null;
					var scriptClass = false;
					var structClass = false;
					for (var type = (symbol as INamedTypeSymbol)?.BaseType; type != null; type = type.BaseType)
					{
						if (SymbolEqualityComparer.Default.Equals(type, structType)) structClass = true;
						if (SymbolEqualityComparer.Default.Equals(type, anyType)) { scriptClass = true; break; }
					}
					_inlineFacts[original] = new(visible, Has(exportAttribute), Has(hiddenAttribute), scriptClass, structClass);
				}
			}
		}
	}
}
