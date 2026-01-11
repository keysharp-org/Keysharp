using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keysharp.Scripting
{
	internal class PrettyPrinter : CSharpSyntaxWalker
	{
		readonly StringBuilder _sb;
		int _indent;

		public PrettyPrinter(int initialIndent = 0)
			: base(SyntaxWalkerDepth.StructuredTrivia)
		{
			_sb = new StringBuilder();
			_indent = initialIndent;
		}

		public static string Print(SyntaxNode node, int indent = 0)
		{
			var printer = new PrettyPrinter(indent);
			printer.Visit(node);
			printer.TrimTrailingNewLine();
			return printer._sb.ToString();
		}

		// helper to emit current indent
		void WriteIndent()
		{
			_sb.Append(new string('\t', _indent));
		}

		// write a token (its text) and maybe a trailing space
		public override void VisitToken(SyntaxToken token)
		{
			var text = token.Text;
			if (string.IsNullOrWhiteSpace(text))
				return;

			_sb.Append(text);
			// add a single space if the next token on same line is an identifier/literal
			if (NeedsSpaceAfter(token) && !_sb.ToString().EndsWith(" "))
				_sb.Append(' ');
		}

		// decide when to force a space
		bool NeedsSpaceAfter(SyntaxToken token)
		{
			switch (token.Kind())
			{
				case SyntaxKind.PublicKeyword:
				case SyntaxKind.StaticKeyword:
				case SyntaxKind.UsingKeyword:
				case SyntaxKind.ClassKeyword:
				case SyntaxKind.IfKeyword:
				case SyntaxKind.ForKeyword:
				case SyntaxKind.WhileKeyword:
				case SyntaxKind.ReturnKeyword:
				case SyntaxKind.IdentifierToken:
				case SyntaxKind.NumericLiteralToken:
				case SyntaxKind.StringLiteralToken:
				case SyntaxKind.IsKeyword:
				case SyntaxKind.NewKeyword:
					return true;
				default:
					return false;
			}
		}

		public override void VisitCompilationUnit(CompilationUnitSyntax node)
		{
			// 1) using‐directives
			foreach (var u in node.Usings)
			{
				Visit(u);
			}

			// blank line between usings and namespace
			_sb.AppendLine();

			// 2) leading assembly‐attributes
			foreach (var al in node.AttributeLists)
			{
				Visit(al);
			}
				

			// 3) top‐level members (namespaces, classes, etc.)
			foreach (var m in node.Members)
			{
				Visit(m);
			}
		}

		public override void VisitAttributeList(AttributeListSyntax node)
		{
			bool inline =
				 node.Parent is ParameterSyntax         // parameter [Attr]
			  || node.Parent is TypeParameterSyntax    // generic‐type param [Attr]
			  || (node.Target?.Identifier.IsKind(SyntaxKind.ReturnKeyword) ?? false); // [return: Attr]
																			   // e.g. “[assembly: Foo(…)]”
			if (!inline)
				WriteIndent();

			_sb.Append("[");
			if (node.Target != null)
			{
				_sb.Append(node.Target.Identifier.Text).Append(": ");
			}
			bool first = true;
			foreach (var a in node.Attributes)
			{
				if (!first) _sb.Append(", ");
				first = false;
				Visit(a);
			}
			if (!inline)
				_sb.AppendLine("]");
			else
				_sb.Append("]");
		}

		public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			WriteIndent();
			_sb.Append("namespace ").Append(node.Name.ToString());
			_sb.AppendLine();
			WriteIndent();
			_sb.AppendLine("{");
			_indent++;

			foreach (var u in node.Usings)
			{
				Visit(u);
			}
			if (node.Usings.Count > 0)
			{
				_sb.AppendLine(); // blank line after the usings
			}

			foreach (var m in node.Members)
			{
				Visit(m);
				_sb.AppendLine();
			}
			TrimTrailingNewLine();
			_indent--;
			WriteIndent();
			_sb.AppendLine("}");
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			foreach (var attrs in node.AttributeLists)
			{
				Visit(attrs);
			}

			WriteIndent();
			// modifiers + “class”
			foreach (var mod in node.Modifiers)
			{
				_sb.Append(mod.Text).Append(" ");
			}
			_sb.Append("class ").Append(node.Identifier.Text);

			if (node.BaseList != null)
			{
				_sb.Append(" : ")
					.Append(string.Join(", ", node.BaseList.Types.Select(t => t.ToString())));
			}

			_sb.AppendLine();
			WriteIndent();
			_sb.AppendLine("{");
			_indent++;
			for (int i = 0; i < node.Members.Count; i++)
			{
				var m = node.Members[i];
				Visit(m);
				if (i < node.Members.Count - 1 && NeedsMemberSpacer(m))
					_sb.AppendLine();
			}
			//TrimTrailingNewLine();
			_indent--;
			WriteIndent();
			_sb.AppendLine("}");
		}

		static bool NeedsMemberSpacer(MemberDeclarationSyntax m) =>
			   m is not FieldDeclarationSyntax
			&& m is not PropertyDeclarationSyntax;

		public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			PrintFunctionHeader(
				node.ReturnType,
				node.Identifier.Text,
				node.TypeParameterList,
				node.Modifiers,
				node.AttributeLists,
				node.ParameterList
			);

			// Body (block or expression‐bodied)
			if (node.Body != null)
				Visit(node.Body);
			else if (node.ExpressionBody != null)
			{
				// e.g. “=> expr;”
				WriteIndent();
				_sb.Append("=> ");
				Visit(node.ExpressionBody.Expression);
				_sb.AppendLine(";");
			}
		}

		public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
		{
			PrintFunctionHeader(
				node.ReturnType,
				node.Identifier.Text,
				node.TypeParameterList,
				node.Modifiers,
				node.AttributeLists,
				node.ParameterList
			);

			Visit(node.Body);
		}


		public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
		{
			// 1) parameters
			PrintParameterList(node.ParameterList);
			_sb.Append(" =>");

			// 2a) block‐body
			if (node.Body is BlockSyntax)
				_sb.AppendLine();
			else
				_sb.Append(" ");
			
			Visit(node.Body);

			TrimTrailingNewLine();
		}

		// Simple lambda: x => x * 2
		public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
		{
			// 1) single identifier
			_sb.Append(node.Parameter.Identifier.Text);
			_sb.Append(" =>");

			// 2a) block‐body
			if (node.Body is BlockSyntax block)
				_sb.AppendLine();
			else
				_sb.Append(" ");

			Visit(node.Body);
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			WriteIndent();
			foreach (var mod in node.Modifiers)
				_sb.Append(mod.Text).Append(" ");
			Visit(node.Declaration);
			_sb.AppendLine(";");
		}
		public override void VisitReturnStatement(ReturnStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("return");
			if (node.Expression != null)
			{
				_sb.Append(" ");
				Visit(node.Expression);
			}
			_sb.AppendLine(";");
		}

		public override void VisitTryStatement(TryStatementSyntax node)
		{
			// print "try" at current indent
			WriteIndent();
			_sb.AppendLine("try");
			// body one level deeper
			VisitBlockIndented(node.Block);
			// each catch at same level as try
			foreach (var catchClause in node.Catches)
				VisitCatchClause(catchClause);
			if (node.Finally != null)
				VisitFinallyClause(node.Finally);
		}

		public override void VisitCatchFilterClause(CatchFilterClauseSyntax node)
		{
			// prints "when (cond)"
			_sb.Append("when "); //NormalizeWhitespace() does it without a leading space
			_sb.Append("(");
			Visit(node.FilterExpression);
			_sb.Append(")");
		}

		public override void VisitCatchClause(CatchClauseSyntax node)
		{
			// catch at the *same* indent as try
			WriteIndent();
			_sb.Append("catch");
			if (node.Declaration != null)
			{
				var decl = node.Declaration;       // CatchDeclarationSyntax
				_sb.Append(" (");
				Visit(decl.Type);                  // e.g. Exception
				if (decl.Identifier.Text != string.Empty)
				{
					_sb.Append(" ");
					_sb.Append(decl.Identifier.Text);  // e.g. e
				}
				_sb.Append(")");
			}

			if (node.Filter != null)
			{
				Visit(node.Filter);
			}

			_sb.AppendLine();

			// and now the block one level deeper
			VisitBlockIndented(node.Block);
		}

		public override void VisitFinallyClause(FinallyClauseSyntax node)
		{
			WriteIndent();
			_sb.AppendLine("finally");
			VisitBlockIndented(node.Block);
		}

		/// <summary>
		/// A helper that prints a BlockSyntax but
		/// *does not* change the caller’s _indent.
		/// It takes the current _indent, writes the open‐brace,
		/// bumps the indent internally for the body and
		/// restores it afterward, then writes the close‐brace.
		/// </summary>
		private void VisitBlockIndented(BlockSyntax block)
		{
			// print "{"
			WriteIndent();
			_sb.AppendLine("{");

			// body
			_indent++;
			var stmts = block.Statements;
			for (int i = 0; i < stmts.Count; i++)
			{
				var stmt = stmts[i];
				Visit(stmt);

				if (i < stmts.Count - 1)
				{
					// if this statement “ends with” its own block, and there is another one after it,
					// emit an extra blank line.
					if (EndsWithBlock(stmt))
						_sb.AppendLine();
				}
			}
			_indent--;

			// closing "}"
			WriteIndent();
			_sb.AppendLine("}");
		}

		public override void VisitUsingDirective(UsingDirectiveSyntax node)
		{
			WriteIndent();
			_sb.Append("using ");
			if (node.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
			{
				_sb.Append("static ");
			}
			if (node.Alias != null)
			{
				// node.Alias is a NameEqualsSyntax, e.g. "Foo ="
				_sb.Append(node.Alias.Name.Identifier.Text)
					.Append(" = ");
			}
			Visit(node.Name);
			_sb.AppendLine(";");
		}

		public override void VisitExternAliasDirective(ExternAliasDirectiveSyntax node)
		{
			WriteIndent();
			_sb.Append("extern alias ")
				.Append(node.Identifier.Text)
				.AppendLine(";");
		}

		public override void VisitBlock(BlockSyntax node)
		{
			VisitBlockIndented(node);
		}

		public override void VisitExpressionStatement(ExpressionStatementSyntax node)
		{
			WriteIndent();
			Visit(node.Expression);
			_sb.AppendLine(";");
		}

		public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
		{
			// e.g. “var (”
			Visit(node.Type);           // prints “var”

			// the designation can be a parenthesized tuple:
			if (node.Designation is ParenthesizedVariableDesignationSyntax pvd)
			{
				_sb.Append("(");
				_sb.Append(string.Join(", ",
					pvd.Variables.OfType<SingleVariableDesignationSyntax>()
						.Select(v => v.Identifier.Text)));
				_sb.Append(")");
			}
			else if (node.Designation is SingleVariableDesignationSyntax svd)
			{
				_sb.Append(" ");
				// just a single name:
				_sb.Append(svd.Identifier.Text);
			}
			else
			{
				_sb.Append(" ");
				// fallback
				_sb.Append(node.Designation.ToString());
			}
		}

		public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
		{
			WriteIndent();

			// --- 1) print the "local" keyword if you have it as a modifier ---
			//    (C# doesn't normally have "local", so we assume you stuck it
			//     in node.Modifiers or in the node.Type as a QualifiedName)
			if (node.Modifiers.Count > 0)
			{
				foreach (var mod in node.Modifiers)
					_sb.Append(mod.Text).Append(" ");
			}

			Visit(node.Declaration);
			_sb.AppendLine(";");
		}

		public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
		{
			Visit(node.Type);
			_sb.Append(' ');
			bool first = true;
			foreach (var v in node.Variables)
			{
				if (!first) _sb.Append(", ");
				first = false;
				Visit(v);
			}
		}

		public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
		{
			// ─── deconstruction case ───
			if (node.ArgumentList != null)
				PrintBracketedArgumentList(node.ArgumentList);
			// ─── normal case ───
			else
			{
				_sb.Append(node.Identifier.Text);
			}

			// ─── initializer (same for both) ───
			if (node.Initializer != null)
			{
				_sb.Append(" = ");
				Visit(node.Initializer.Value);
			}
		}

		public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			// “new Type”
			_sb.Append("new ");
			Visit(node.Type);

			// ( … arguments … )
			PrintArgumentList(node.ArgumentList);

			// optional { … } initializer
			if (node.Initializer != null)
			{
				Visit(node.Initializer);
			}
		}

		public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
		{
			// “new Type[...]”
			_sb.Append("new ");
			Visit(node.Type);

			// optional { … } initializer
			if (node.Initializer != null)
			{
				Visit(node.Initializer);
			}
		}

		public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
		{
			// “new[]”
			_sb.Append("new[]");

			if (node.Initializer != null)
			{
				Visit(node.Initializer);
			}
		}

		public override void VisitCollectionExpression(CollectionExpressionSyntax node)
		{
			_sb.Append("[");
				
			// comma-separated elements
			bool first = true;
			foreach (var expr in node.Elements)
			{
				if (!first) _sb.Append(", ");
				first = false;
				Visit(expr);
			}

			_sb.Append("]");
		}

		public override void VisitTupleExpression(TupleExpressionSyntax node)
		{
			_sb.Append("(");
			bool first = true;
			foreach (var arg in node.Arguments)
			{
				if (!first) _sb.Append(", ");
				first = false;

				// if it’s named like “x: expr”
				if (arg.NameColon != null)
				{
					_sb.Append(arg.NameColon.Name.Identifier.Text).Append(": ");
				}

				Visit(arg.Expression);
			}
			_sb.Append(")");
		}

		public override void VisitInitializerExpression(InitializerExpressionSyntax node)
		{
			if (node.IsKind(SyntaxKind.ArrayInitializerExpression) && 
				!(node.Parent?.Parent is EqualsValueClauseSyntax || 
					(node.Parent?.Parent is AssignmentExpressionSyntax assign
					 && assign.Parent is ExpressionStatementSyntax)))
			{
				_sb.Append(" {");
				bool first = true;
				foreach (var expr in node.Expressions)
				{
					if (!first) _sb.Append(",");
					first = false;
					_sb.Append(" ");
					Visit(expr);
				}
				_sb.Append(" }");
				return;
			}

			// Handles object initializers
			// print “ {” on its own line
			_sb.AppendLine();
			WriteIndent();
			_sb.AppendLine("{");

			// each element one indent deeper
			_indent++;
			var exprs = node.Expressions;
			for (int i = 0; i < exprs.Count; i++)
			{
				WriteIndent();
				Visit(exprs[i]);
				// comma except after last
				if (i < exprs.Count - 1) _sb.AppendLine(",");
				else _sb.AppendLine();
			}
			_indent--;

			// closing “}” aligned with its parent
			WriteIndent();
			_sb.Append("}");
		}

		public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
		{
			Visit(node.Condition);
			_sb.Append(" ? ");
			Visit(node.WhenTrue);
			_sb.Append(" : ");
			Visit(node.WhenFalse);
		}

		public override void VisitIfStatement(IfStatementSyntax node)
		{
			if (node.Parent is not ElseClauseSyntax)
				WriteIndent();

			_sb.Append("if (");
			Visit(node.Condition);
			_sb.AppendLine(")");
			if (node.Statement is BlockSyntax)
			{
				Visit(node.Statement);
			}
			else
			{
				_indent++;
				Visit(node.Statement);
				_indent--;
			}
			if (node.Else != null)
			{
				WriteIndent();
				if (node.Else.Statement is IfStatementSyntax)
				{
					_sb.Append("else ");
					Visit(node.Else.Statement);
				}
				else
				{
					_sb.AppendLine("else");
					if (node.Else.Statement is BlockSyntax)
					{
						Visit(node.Else.Statement);
					}
					else
					{
						_indent++;
						Visit(node.Else.Statement);
						_indent--;
					}
				}
			}
		}

		public override void VisitForStatement(ForStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("for (");
			// initializer
			if (node.Declaration != null)
				Visit(node.Declaration);
			_sb.Append("; ");
			// condition
			if (node.Condition != null)
				Visit(node.Condition);
			_sb.Append(node.Incrementors.Count == 0 ? ";" : "; ");
			// incrementors
			bool first = true;
			foreach (var inc in node.Incrementors)
			{
				if (!first) _sb.Append(", ");
				first = false;
				Visit(inc);
			}
			_sb.AppendLine(")");
			if (node.Statement is BlockSyntax)
				Visit(node.Statement);
			else
			{
				_sb.AppendLine();
				_indent++;
				WriteIndent();
				Visit(node.Statement);
				_indent--;
			}
		}

		public override void VisitWhileStatement(WhileStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("while (");
			Visit(node.Condition);
			_sb.AppendLine(")");

			if (node.Statement is BlockSyntax)
			{
				// block will indent itself
				Visit(node.Statement);
			}
			else
			{
				// single stmt: bump indent, visit, then newline
				_indent++;
				Visit(node.Statement);
				_indent--;
			}
		}

		public override void VisitDoStatement(DoStatementSyntax node)
		{
			WriteIndent();
			_sb.AppendLine("do");

			if (node.Statement is BlockSyntax)
			{
				Visit(node.Statement);
			}
			else
			{
				_indent++;
				Visit(node.Statement);
				_indent--;
			}

			WriteIndent();
			_sb.Append("while (");
			Visit(node.Condition);
			_sb.AppendLine(");");
		}

		public override void VisitForEachStatement(ForEachStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("foreach (")
				.Append(node.Type.ToString()).Append(" ")
				.Append(node.Identifier.Text)
				.Append(" in ");
			Visit(node.Expression);
			_sb.AppendLine(")");

			if (node.Statement is BlockSyntax)
			{
				Visit(node.Statement);
			}
			else
			{
				_indent++;
				Visit(node.Statement);
				_indent--;
			}
		}

		public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("foreach (var ")
				.Append(node.Variable.ToString())
				.Append(" in ");
			Visit(node.Expression);
			_sb.AppendLine(")");

			if (node.Statement is BlockSyntax)
			{
				Visit(node.Statement);
			}
			else
			{
				_indent++;
				Visit(node.Statement);
				_indent--;
			}
		}

		public override void VisitUsingStatement(UsingStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("using (");
			if (node.Declaration != null)
				Visit(node.Declaration);
			else
				Visit(node.Expression);
			_sb.AppendLine(")");

			if (node.Statement is BlockSyntax)
			{
				Visit(node.Statement);
			}
			else
			{
				_indent++;
				Visit(node.Statement);
				_indent--;
			}
		}

		public override void VisitBreakStatement(BreakStatementSyntax node)
		{
			WriteIndent();
			_sb.AppendLine("break;");
		}

		public override void VisitContinueStatement(ContinueStatementSyntax node)
		{
			WriteIndent();
			_sb.AppendLine("continue;");
		}

		public override void VisitThrowStatement(ThrowStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("throw");
			if (node.Expression != null)
			{
				_sb.Append(" ");
				Visit(node.Expression);
			}
			_sb.AppendLine(";");
		}

		public override void VisitGotoStatement(GotoStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("goto ");
			Visit(node.Expression);
			_sb.AppendLine(";");
		}

		public override void VisitLabeledStatement(LabeledStatementSyntax node)
		{
			WriteIndent();
			_sb.Append(node.Identifier.Text).AppendLine(":");

			_indent++;
			Visit(node.Statement);
			_indent--;
		}

		public override void VisitEmptyStatement(EmptyStatementSyntax node)
		{
			WriteIndent();
			_sb.AppendLine(";");
		}

		public override void VisitSwitchStatement(SwitchStatementSyntax node)
		{
			WriteIndent();
			_sb.Append("switch (");
			Visit(node.Expression);
			_sb.AppendLine(")");
			WriteIndent();
			_sb.AppendLine("{");
			_indent++;
			foreach (var section in node.Sections)
				Visit(section);
			_indent--;
			WriteIndent();
			_sb.AppendLine("}");
		}

		public override void VisitSwitchSection(SwitchSectionSyntax node)
		{
			foreach (var label in node.Labels)
			{
				WriteIndent();
				Visit(label);
			}
			_indent++;
			foreach (var stmt in node.Statements)
			{
				if (stmt is BlockSyntax)
				{
					_indent--;
					Visit(stmt);
					_indent++;
				} else
					Visit(stmt);

				if (EndsWithBlock(stmt))
					_sb.AppendLine();
			}
			_indent--;
		}

		public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
		{
			_sb.Append("case ");
			Visit(node.Value);
			_sb.AppendLine(":");
		}

		public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
		{
			// indent is handled by the container (VisitSwitchSection)
			_sb.Append("case ");

			// 1) the pattern (e.g. “string _ks_string_5”)
			Visit(node.Pattern);

			// 2) optional “when <expr>”
			if (node.WhenClause != null)
			{
				_sb.Append(" when ");
				Visit(node.WhenClause.Condition);
			}

			_sb.AppendLine(":");
		}

		public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
		{
			_sb.AppendLine("default:");
		}

		public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
		{
			// ex
			Visit(node.Expression);
			// “ is ”
			_sb.Append(" is ");
			// the pattern (e.g. “Keysharp.Core.Error kserr”)
			Visit(node.Pattern);
		}

		public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
		{
			Visit(node.Type);
			_sb.Append(" ").Append(node.Designation.ToString());
		}

		public override void VisitBinaryExpression(BinaryExpressionSyntax node)
		{
			// left
			Visit(node.Left);
			// operator
			_sb.Append(' ')
				.Append(node.OperatorToken.Text)
				.Append(' ');
			// right
			Visit(node.Right);
		}

		public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
		{
			// print “b”
			Visit(node.Left);

			// print “ = ”
			_sb.Append(" ")
				.Append(node.OperatorToken.Text)   // “=”, “+=”, “-=”, etc.
				.Append(" ");

			// print “2L”
			Visit(node.Right);
		}

		public override void VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			Visit(node.Expression);
			PrintArgumentList(node.ArgumentList);
		}

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			_sb.Append(node.Identifier.Text);
		}

		public override void VisitGenericName(GenericNameSyntax node)
		{
			_sb.Append(node.Identifier.Text);
			PrintTypeArgumentList(node.TypeArgumentList);
		}

		public override void VisitLiteralExpression(LiteralExpressionSyntax node)
		{
			_sb.Append(node.Token.Text);
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			// 1) any attributes on the ctor
			foreach (var attrs in node.AttributeLists)
			{
				WriteIndent();
				Visit(attrs);
			}

			// 2) signature: modifiers + name
			WriteIndent();
			foreach (var mod in node.Modifiers)
				_sb.Append(mod.Text).Append(" ");
			_sb.Append(node.Identifier.Text);

			// 3) parameter list
			PrintParameterList(node.ParameterList);

			// 4) optional : base(...) or : this(...)
			if (node.Initializer != null)
			{
				// the Initializer kind is either BaseConstructorInitializer or ThisConstructorInitializer
				var kind = node.Initializer.ThisOrBaseKeyword.Text; // "base" or "this"
				_sb.Append(" : ").Append(kind);
				PrintArgumentList(node.Initializer.ArgumentList);
			}

			// 5) body on next line
			_sb.AppendLine();
			Visit(node.Body);
		}

		public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
		{
			// 1) attributes (unlikely but possible)
			foreach (var attrs in node.AttributeLists)
			{
				WriteIndent();
				Visit(attrs);
			}

			// 2) signature: "~" + class name + "()"
			WriteIndent();
			_sb.Append("~").Append(node.Identifier.Text).Append("()");
			_sb.AppendLine();

			// 3) body
			Visit(node.Body);
		}

		public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			// 1) any attributes
			foreach (var attrs in node.AttributeLists)
			{
				WriteIndent();
				Visit(attrs);
			}

			// 2) signature: modifiers + type + name
			WriteIndent();
			foreach (var mod in node.Modifiers)
				_sb.Append(mod.Text).Append(" ");
			Visit(node.Type);
			_sb.Append(" ").Append(node.Identifier.Text);

			// 3a) normal get/set block
			if (node.AccessorList != null)
			{
				// detect “{ get; set; }” with no bodies
				var accessors = node.AccessorList.Accessors;
				if (accessors.All(a => a.Body == null && a.ExpressionBody == null))
				{
					_sb.Append(" {");
					foreach (var acc in accessors)
					{
						_sb.Append(" ");
						_sb.Append(acc.Keyword.Text).Append(";");
					}
					_sb.AppendLine(" }");
					return;
				}

				// otherwise fall back to multi‐line
				_sb.AppendLine();
				Visit(node.AccessorList);
			}
			// 3b) expression‐body: “=> expr;”
			else if (node.ExpressionBody != null)
			{
				Visit(node.ExpressionBody);
				_sb.AppendLine(";");
			}
		}

		public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
		{
			// we know there’s no indent here, property/body already printed
			_sb.Append(" => ");
			Visit(node.Expression);
		}

		// Fallback to default walk: walks into child nodes/tokens
		public override void DefaultVisit(SyntaxNode node)
		{
			foreach (var child in node.ChildNodesAndTokens())
			{
				if (child.IsNode) Visit(child.AsNode());
				else VisitToken(child.AsToken());
			}
		}

		// --- Helpers for printing a function‐like signature ---
		void PrintFunctionHeader(
			TypeSyntax returnType,
			string identifier,
			TypeParameterListSyntax typeParams,
			SyntaxTokenList modifiers,
			SyntaxList<AttributeListSyntax> attributes,
			ParameterListSyntax parameters)
		{
			// attributes
			foreach (var al in attributes)
			{
				Visit(al);
			}

			// indent + modifiers + return‐type + name
			WriteIndent();
			foreach (var mod in modifiers)
				_sb.Append(mod.Text).Append(" ");
			Visit(returnType);
			_sb.Append(" ").Append(identifier);
			if (typeParams != null)
				_sb.Append(typeParams.ToString());

			PrintParameterList(parameters);
			_sb.AppendLine();
		}

		private void PrintParameterList(ParameterListSyntax parameters)
		{
			_sb.Append("(");
			for (int i = 0; i < parameters.Parameters.Count; i++)
			{
				var p = parameters.Parameters[i];

				// attributes
				foreach (var al in p.AttributeLists)
				{
					Visit(al);
				}
				if (p.AttributeLists.Count > 0)
					_sb.Append(" ");

				// modifiers (ref/out/in/params)
				foreach (var pm in p.Modifiers)
					_sb.Append(pm.Text).Append(" ");

				// type + name
				if (p.Type != null)
				{
					Visit(p.Type);
					_sb.Append(" ");
				}
				_sb.Append(p.Identifier.Text);

				// default
				if (p.Default != null)
				{
					_sb.Append(" = ");
					Visit(p.Default.Value);
				}

				if (i < parameters.Parameters.Count - 1)
					_sb.Append(", ");
			}
			_sb.Append(")");
		}

		private void PrintArgumentList(ArgumentListSyntax argList, bool trimTrailingSpace = true)
		{
			if (argList == null) return;

			// if we just wrote a space (e.g. after a method name), drop it:
			if (trimTrailingSpace)
				TrimTrailingSpace();

			_sb.Append("(");

			bool first = true;
			foreach (var arg in argList.Arguments)
			{
				if (!first)
					_sb.Append(", ");
				first = false;

				if (arg.NameColon != null)
				{
					_sb.Append(arg.NameColon.Name.Identifier.Text)
					   .Append(": ");
				}

				if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
				{
					_sb.Append(arg.RefKindKeyword.Text)
					   .Append(" ");
				}

				// print the expression inside the ArgumentSyntax
				Visit(arg.Expression);
			}

			_sb.Append(")");
		}

		private void PrintBracketedArgumentList(BracketedArgumentListSyntax argList, bool trimTrailingSpace = true)
		{
			if (argList == null) return;

			// if we just wrote a space (e.g. after a method name), drop it:
			if (trimTrailingSpace)
				TrimTrailingSpace();

			_sb.Append("[");

			bool first = true;
			foreach (var arg in argList.Arguments)
			{
				if (!first)
					_sb.Append(", ");
				first = false;

				// print the expression inside the ArgumentSyntax
				Visit(arg.Expression);
			}

			_sb.Append("]");
		}

		private void PrintTypeArgumentList(TypeArgumentListSyntax typeArgs)
		{
			if (typeArgs == null) return;

			_sb.Append("<");
			bool first = true;
			foreach (var t in typeArgs.Arguments)
			{
				if (!first) _sb.Append(", ");
				first = false;
				Visit(t);
			}
			_sb.Append(">");
		}

		private bool EndsWithBlock(StatementSyntax stmt)
		{
			return stmt switch
			{
				// if … else { … }
				IfStatementSyntax ifs
					when ifs.Else?.Statement is StatementSyntax
					=> EndsWithBlock(ifs.Else.Statement),

				// if { … } (even without else)
				IfStatementSyntax ifs2
					when ifs2.Statement is StatementSyntax
					=> EndsWithBlock(ifs2.Statement),

				// try / catch / finally all printed as one TryStatementSyntax
				TryStatementSyntax _
					=> true,

				// loops
				ForStatementSyntax _
				or ForEachStatementSyntax _
				or WhileStatementSyntax _
				or DoStatementSyntax _
					=> true,

				// using / fixed / lock / unsafe with body
				UsingStatementSyntax uss
					when uss.Statement is BlockSyntax
					=> true,
				FixedStatementSyntax _
				or LockStatementSyntax _
				or UnsafeStatementSyntax _
				or SwitchStatementSyntax _
				or BlockSyntax _
					=> true,

				LocalFunctionStatementSyntax _ => true,

				_ => false
			};
		}

			void TrimTrailingNewLine()
		{
			var nl = Environment.NewLine;
			if (_sb.Length >= nl.Length &&
				_sb.ToString(_sb.Length - nl.Length, nl.Length) == nl)
			{
				_sb.Length -= nl.Length;
			}
		}

		void TrimTrailingSpace()
		{
			if (_sb.Length > 0 && _sb[^1] == ' ')
				_sb.Length--;
		}
	}
}
