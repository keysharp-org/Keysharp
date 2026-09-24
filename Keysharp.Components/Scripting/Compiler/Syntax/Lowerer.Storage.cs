using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Keysharp.Internals.ExtensionMethods;

namespace Keysharp.Compilation.Syntax
{
	internal sealed partial class Lowerer
	{
		private int _boxCounter;
		private static readonly TypeSyntax BoxType = SyntaxFactory.ParseTypeName("System.Runtime.CompilerServices.StrongBox<object>");
		private static ExpressionSyntax NewBox(ExpressionSyntax value) =>
			SyntaxFactory.ObjectCreationExpression(BoxType).WithArgumentList(
				SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(value))));

		private sealed class LocalStorage
		{
			public string Box;
			private ExpressionSyntax value;
			public ExpressionSyntax Read(Lowerer lowerer, string name) => value ??= lowerer.Unsettled(Id(NameMangler.Escape(name)),
				() => Box != null, _ => Member(Id(Box), "Value"));
		}

		private LocalStorage Storage(ScopeVar variable) => variable.Owner.Variables.GetOrAdd(variable.Key);
		private ExpressionSyntax LocalValue(ScopeVar variable) => Storage(variable).Read(this, variable.Key);
		private ScopeVar Receiver() => new("this", VarStorage.Local, _scope.Root);

		private ExpressionSyntax LocalBox(ScopeVar variable)
		{
			var storage = Storage(variable);
			storage.Box ??= "KS_box" + ++_boxCounter;
			return Id(storage.Box);
		}

		private StatementSyntax DeclareVariable(FunctionScope scope, string name, ExpressionSyntax value)
		{
			var declaration = LocalDecl(ObjType, NameMangler.Escape(name), value);
			var storage = Storage(scope.Find(name));
			return Unsettled(declaration, () => storage.Box != null, node => LocalDeclVar(storage.Box,
				NewBox(((LocalDeclarationStatementSyntax)node).Declaration.Variables[0].Initializer.Value)));
		}

		private static void InitializeParameters(List<StatementSyntax> body, IEnumerable<ScopeVar> parameters)
		{
			foreach (var parameter in parameters)
				if (parameter.Owner.Variables.TryGetValue(parameter.Key, out var storage) && storage.Box != null
					&& parameter.Owner.Params?.Any(p => p.Variadic && p.Name.Equals(parameter.Key, System.StringComparison.OrdinalIgnoreCase)) != true)
					body.Insert(0, LocalDeclVar(storage.Box, NewBox(Id(NameMangler.Escape(parameter.Key)))));
		}

		private static ExpressionSyntax LifetimeRoot(ScopeVar variable) =>
			Id(variable.Owner.Variables.GetValueOrDefault(variable.Key)?.Box ?? NameMangler.Escape(variable.Key));
	}
}
