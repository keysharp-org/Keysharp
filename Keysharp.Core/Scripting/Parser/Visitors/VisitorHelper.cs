using Antlr4.Runtime.Tree;
using Antlr4.Runtime;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static MainParser;
using System.Data.Common;
using System.Linq.Expressions;
using Antlr4.Runtime.Misc;

namespace Keysharp.Scripting
{
    internal partial class Parser
    {
		internal Dictionary<ParserRuleContext, VisitFunction> functionParserData = new();
        internal bool hasVisitedIdentifiers = true;

		internal static ExpressionSyntax VarsNameSyntax = CreateMemberAccess(Keywords.MainScriptVariableName, "Vars");

		internal static Dictionary<int, SyntaxKind> pureBinaryOperators = new Dictionary<int, SyntaxKind>()
        {
            {MainParser.Plus, SyntaxKind.AddExpression},
            {MainParser.Minus, SyntaxKind.SubtractExpression},
            {MainParser.Multiply, SyntaxKind.MultiplyExpression},
            {MainParser.Divide, SyntaxKind.DivideExpression},
            {MainParser.Modulus, SyntaxKind.ModuloExpression},
            {MainParser.LeftShiftArithmetic, SyntaxKind.LeftShiftExpression},
            {MainParser.RightShiftArithmetic, SyntaxKind.RightShiftExpression},
            {MainParser.RightShiftLogical, SyntaxKind.UnsignedRightShiftExpression},
            {MainParser.BitAnd, SyntaxKind.BitwiseAndExpression},
            {MainParser.BitOr, SyntaxKind.BitwiseOrExpression},
            {MainParser.BitXOr, SyntaxKind.ExclusiveOrExpression},
            {MainParser.LessThan, SyntaxKind.LessThanExpression},
            {MainParser.MoreThan, SyntaxKind.GreaterThanExpression},
            {MainParser.IdentityEquals, SyntaxKind.EqualsEqualsToken},
            {MainParser.NotEquals, SyntaxKind.NotEqualsExpression},
            {MainParser.And, SyntaxKind.LogicalAndExpression},
            {MainParser.VerbalAnd, SyntaxKind.LogicalAndExpression},
            {MainParser.Or, SyntaxKind.LogicalOrExpression},
            {MainParser.VerbalOr, SyntaxKind.LogicalOrExpression}
        };

        internal static Dictionary<int, string> binaryOperators = new Dictionary<int, string>()
        {
            {MainParser.Plus, "Add"},
            {MainParser.Minus, "Minus"},
            {MainParser.Multiply, "Multiply"},
            {MainParser.Divide, "Divide"},
            {MainParser.IntegerDivide, "FloorDivide"},
            {MainParser.Modulus, "Modulus"},
            {MainParser.LeftShiftArithmetic, "BitShiftLeft"},
            {MainParser.RightShiftArithmetic, "BitShiftRight"},
            {MainParser.RightShiftLogical, "LogicalBitShiftRight"},
            {MainParser.BitAnd, "BitwiseAnd"},
            {MainParser.BitOr, "BitwiseOr"},
            {MainParser.BitXOr, "BitwiseXor"},
            {MainParser.LessThan, "LessThan"},
            {MainParser.LessThanEquals, "LessThanOrEqual"},
            {MainParser.MoreThan, "GreaterThan"},
            {MainParser.GreaterThanEquals, "GreaterThanOrEqual"},
            {MainParser.IdentityEquals, "IdentityEquality"},
            {MainParser.IdentityNotEquals, "IdentityInequality"},
            {MainParser.NotEquals, "ValueInequality"},
            {MainParser.Equals_, "ValueEquality"},
            {MainParser.And, "BooleanAnd"},
            {MainParser.VerbalAnd, "BooleanAnd"},
            {MainParser.Or, "BooleanOr"},
            {MainParser.VerbalOr, "BooleanOr"},
            {MainParser.Dot, "Concat"},
            {MainParser.RegExMatch, "RegEx"},
            {MainParser.Power, "Power"},
            {MainParser.Is, "Is"}
        };

        internal static Dictionary<int, string> unaryOperators = new Dictionary<int, string>()
        {
            {MainParser.Plus, "Add" },
            {MainParser.Minus, "Minus"},
            {MainParser.Not, "LogicalNot"},
            {MainParser.VerbalNot, "LogicalNot"},
            {MainParser.BitNot, "BitwiseNot"}
        };
        internal void AddAssembly(string assemblyName, string value)
        {
            assemblies = assemblies.Add(SyntaxFactory.Attribute(
                CreateQualifiedName(assemblyName))
                .AddArgumentListArguments(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(value)))));
            return;
        }

        // Used to create temporary variables (eg for-loop vars) which can also be reused later
        internal IdentifierNameSyntax PushTempVar()
        {
            tempVarCount++;
            var tempVarName = Keywords.TempVariablePrefix + tempVarCount.ToString();
            var lastScope = currentFunc.Scope;
            currentFunc.Scope = eScope.Local;
            MaybeAddVariableDeclaration(tempVarName);
            currentFunc.Scope = lastScope;
            return SyntaxFactory.IdentifierName(tempVarName);
        }

        internal void PopTempVar() => tempVarCount--;

        private static List<ClassDeclarationContext> _classDeclarations = new();
        internal static List<ClassDeclarationContext> GetClassDeclarationsRecursive(ProgramContext program)
        {
			var result = new List<ClassDeclarationContext>();

			foreach (var se in program.sourceElements()?.sourceElement() ?? [])
			{
				var topClass = se.classDeclaration();
				if (topClass != null)
					GatherClassDeclarations(topClass);
			}

			void GatherClassDeclarations(ClassDeclarationContext cls)
			{
				result.Add(cls);

				var tail = cls.classTail();
				if (tail == null) return;

				foreach (var elem in tail.classElement())
				{
					if (elem is NestedClassDeclarationContext nested && nested != null)
						GatherClassDeclarations(nested.classDeclaration());
				}
			}

            return result;
        }

        public struct FunctionInfo
        {
            public string Name;
            public bool Static;
        }
		public enum RegionKind
		{
			Block,          // {...}
			FlowBody,       // if/loop body when not a {...} block
			Switch,         // whole switch
			SwitchSection,  // each 'case ...:' section
			Try, Catch, Finally
		}

		public readonly struct Region
		{
			public readonly RegionKind Kind;
			public readonly int Id;           // unique per region instance
			public Region(RegionKind kind, int id) { Kind = kind; Id = id; }
		}
		public readonly struct LabelInfo
		{
			public readonly string Raw;       // Label text
			public readonly int[] Path;       // region path at the label (ancestor->descendant)
			public readonly MainParser.LabelledStatementContext Node;
			public LabelInfo(string raw, int[] path, MainParser.LabelledStatementContext node)
			{ Raw = raw; Path = path; Node = node; }
		}
		/// Per-function index built by the pre-pass.
		public sealed class LabelIndex
		{
			public readonly List<LabelInfo> Labels = new();
			public readonly Dictionary<MainParser.GotoStatementContext, int[]> GotoSitePaths = new();
		}
		internal class VisitFunction : MainParserBaseVisitor<object>
        {
            internal List<FunctionInfo> functionInfos = new();
            private VisitMain mainVisitor;
            private Parser parser;
			private Function currentFunc;

			private readonly Stack<Region> stack = new();
			private int nextRegionId = 1;
			private readonly HashSet<string> seenLabels = new(StringComparer.OrdinalIgnoreCase);

			internal readonly LabelIndex Index = new();

			public VisitFunction(VisitMain visitor)
            {
                mainVisitor = visitor;
                parser = mainVisitor.parser;
				currentFunc = parser.currentFunc;
			}

			private IDisposable PushRegion(RegionKind kind)
			{
				var r = new Region(kind, nextRegionId++);
				stack.Push(r);
				return new RegionPopper(() => stack.Pop());
			}
			private sealed class RegionPopper : IDisposable
			{
				private readonly Action a; public RegionPopper(Action a) => this.a = a;
				public void Dispose() => a();
			}
			private int[] SnapshotPath()
			{
				// Path ancestor->descendant for simple prefix checks.
				return stack.Reverse().Select(r => r.Id).ToArray();
			}

            // Skip some rules
            public override object VisitClassDeclaration([NotNull] ClassDeclarationContext context) => null;
            public override object VisitLiteral([NotNull] LiteralContext context) => null;
			public override object VisitIdentifier([NotNull] IdentifierContext context)
			{
				return base.VisitIdentifier(context);
			}

            // Gather all function declarations/expressions, but don't recurse into them
			public override object VisitFunctionDeclaration([NotNull] FunctionDeclarationContext context)
			{
				AddFunction(context.functionHead());
				return null;
			}

			public override object VisitFatArrowExpression([NotNull] FatArrowExpressionContext context)
			{
				var head = context
					.fatArrowExpressionHead()
					.functionExpressionHead()?
					.functionHead();
				AddFunction(head);
				return null;
			}

			public override object VisitFunctionExpression([NotNull] FunctionExpressionContext context)
			{
				var head = context.functionExpressionHead().functionHead();
				AddFunction(head);
				return null;
			}

			private void AddFunction(FunctionHeadContext head)
			{
                if (head == null)
                    return;

				var info = new FunctionInfo
				{
					Name = head.identifierName().GetText(),
					Static = head.functionHeadPrefix()?.Static() != null,
				};
				functionInfos.Add(info);
			}

			// Gather all variable declarations
			public override object VisitVariableStatement([NotNull] VariableStatementContext context)
			{
				var prevScope = currentFunc.Scope;

				switch (context.GetChild(0).GetText().ToUpper())
				{
					case "LOCAL":
						currentFunc.Scope = eScope.Local;
						break;
					case "GLOBAL":
						currentFunc.Scope = eScope.Global;
						break;
					case "STATIC":
						currentFunc.Scope = eScope.Static;
						break;
				}

				if (context.variableDeclarationList() != null && context.variableDeclarationList().ChildCount > 0)
				{
					foreach (var varDecl in context.variableDeclarationList().variableDeclaration())
					{
						SyntaxNode node = mainVisitor.Visit(varDecl.assignable());
						if (!(node is IdentifierNameSyntax))
						{
							throw new Error();
						}
						string name = ((IdentifierNameSyntax)node).Identifier.Text;

						if (currentFunc.Scope == eScope.Static)
						{
							currentFunc.Statics.Add(name);
						}

						if (currentFunc.Scope == eScope.Global)
						{
							parser.MaybeAddVariableDeclaration(name);
							currentFunc.Locals.Remove(name);
							currentFunc.Globals.Add(name);
						}
						else
						{
							parser.AddVariableDeclaration(name);
							if (currentFunc.Scope == eScope.Local)
							{
								currentFunc.Globals.Remove(name);
								// AddVariableDeclaration added the Locals entry
							}
						}

                        if (varDecl.expression() != null)
                            Visit(varDecl);
					}

					currentFunc.Scope = prevScope;
				}

                return null;
			}

			public override object VisitVarRefExpression([NotNull] VarRefExpressionContext context)
			{
				MaybeAddExpressionDeclaration(context.primaryExpression());
                return base.VisitVarRefExpression(context);
			}

			public override object VisitAssignmentExpression([NotNull] AssignmentExpressionContext context)
			{
                MaybeAddExpressionDeclaration(context.left);
				return base.VisitAssignmentExpression(context);
			}

			public override object VisitCoalesceExpression([NotNull] CoalesceExpressionContext context)
			{
				MaybeAddExpressionDeclaration(context.left);
				return base.VisitCoalesceExpression(context);
			}
			public override object VisitCoalesceExpressionDuplicate([NotNull] CoalesceExpressionDuplicateContext context)
			{
				MaybeAddExpressionDeclaration(context.left);
				return base.VisitCoalesceExpressionDuplicate(context);
			}

            private void MaybeAddExpressionDeclaration(ParserRuleContext context)
            {
                IdentifierExpressionContext iec = null;
                while (context != null && context is not ITerminalNode) {
                    if (context is IdentifierExpressionContext iec2)
                    {
                        iec = iec2;
                        break;
                    }
                    if (context.ChildCount != 1) break;
                    context = context.GetChild(0) as ParserRuleContext;
                }

				if (iec != null)
				{
					SyntaxNode result = mainVisitor.Visit(iec);
					if (result is IdentifierNameSyntax identifierNameSyntax)
						_ = parser.MaybeAddVariableDeclaration(identifierNameSyntax.Identifier.Text);
				}
			}

			public override object VisitAssignmentExpressionDuplicate([NotNull] AssignmentExpressionDuplicateContext context)
			{
				if (context.left is IdentifierExpressionContext iec)
                {
					SyntaxNode result = mainVisitor.Visit(iec);
					if (result is IdentifierNameSyntax identifierNameSyntax)
						_ = parser.MaybeAddVariableDeclaration(identifierNameSyntax.Identifier.Text);
				}
				return base.VisitAssignmentExpressionDuplicate(context);
			}

			// ---------- label & goto capture ----------
			public override object VisitLabelledStatement([NotNull] MainParser.LabelledStatementContext ctx)
			{
				var raw = ctx.identifier().GetText().Trim('"');
				if (!seenLabels.Add(raw))
					throw new Exception($"Duplicate label: {raw}");

				Index.Labels.Add(new LabelInfo(raw, SnapshotPath(), ctx));
				return null;
			}

			public override object VisitGotoStatement([NotNull] MainParser.GotoStatementContext ctx)
			{
				Index.GotoSitePaths[ctx] = SnapshotPath();
				return base.VisitGotoStatement(ctx); // keep walking to find more labels/sites
			}

			// ---------- regions ----------
			public override object VisitBlock([NotNull] MainParser.BlockContext ctx)
			{
				using (PushRegion(RegionKind.Block))
					return base.VisitBlock(ctx);
			}

			public override object VisitSwitchStatement([NotNull] MainParser.SwitchStatementContext ctx)
			{
				using (PushRegion(RegionKind.Switch))
					return base.VisitSwitchStatement(ctx);
			}

			public override object VisitCaseClause([NotNull] MainParser.CaseClauseContext ctx)
			{
				using (PushRegion(RegionKind.SwitchSection))
					return base.VisitCaseClause(ctx);
			}

			public override object VisitTryStatement([NotNull] MainParser.TryStatementContext ctx)
			{
				// try part:
				using (PushRegion(RegionKind.Try))
					Visit(ctx.statement());

				// zero or more catches:
				foreach (var c in ctx.catchProduction())
					Visit(c);

				// finally part:
				var fin = ctx.finallyProduction();
				if (fin != null)
					Visit(fin);

				return null;
			}

			public override object VisitCatchProduction([NotNull] MainParser.CatchProductionContext ctx)
			{
				using (PushRegion(RegionKind.Catch))
					return base.VisitCatchProduction(ctx); // visits flowBlock inside
			}

			public override object VisitFinallyProduction([NotNull] MainParser.FinallyProductionContext ctx)
			{
				using (PushRegion(RegionKind.Finally))
					return base.VisitFinallyProduction(ctx);
			}

			// Any “flow body” (if/loop) that isn’t a block still creates a boundary we shouldn’t jump into.
			public override object VisitFlowBlock([NotNull] MainParser.FlowBlockContext ctx)
			{
				using (PushRegion(RegionKind.FlowBody))
					return base.VisitFlowBlock(ctx);
			}
		}

		internal List<FunctionInfo> GetScopeFunctions(ParserRuleContext context, VisitMain mainVisitor)
        {
            currentFunc.RootContext = context;
            hasVisitedIdentifiers = false;
			var visitor = functionParserData.GetOrAdd(context, () => {
                var v = new VisitFunction(mainVisitor);
                v.Visit(context);
                return v;
            });
            hasVisitedIdentifiers = true;

			return visitor.functionInfos;
        }

        private static Dictionary<string, NameSyntax> _qualifiedNameCache;
        // Creating an identifier for something like "Keysharp.Program" results in a single identifier,
        // not a namespace/class access. This function splits the name by "." and then joins it one-by-one.
        internal static NameSyntax CreateQualifiedName(string qualifiedString)
        {
            if (_qualifiedNameCache == null)
                _qualifiedNameCache = new ();

            if (_qualifiedNameCache.TryGetValue(qualifiedString, out var result))
                return result;

            NameSyntax qualifiedName = null;

            foreach (var identifier in qualifiedString.Split('.'))
            {
                var name = SyntaxFactory.IdentifierName(identifier);

                if (qualifiedName != null)
                {
                    qualifiedName = SyntaxFactory.QualifiedName(qualifiedName, name);
                }
                else
                {
                    qualifiedName = name;
                }
            }

            _qualifiedNameCache[qualifiedString] = qualifiedName;

			return qualifiedName;
        }

		/// <summary>
		/// Creates a field declaration like
		///     [modifiers] [type] [name] = [initializer];
		/// purely via SyntaxFactory calls.
		/// </summary>
		/// <param name="modifierKinds">
		/// e.g. new[]{ SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword }
		/// </param>
		/// <param name="type">the TypeSyntax for the field’s type</param>
		/// <param name="name">the field’s identifier</param>
		/// <param name="initializer">an ExpressionSyntax for the RHS</param>
		internal static FieldDeclarationSyntax CreateFieldDeclaration(
			IEnumerable<SyntaxKind> modifierKinds,
			TypeSyntax type,
			string name,
			ExpressionSyntax initializer)
		{
			// 1) var identifier = initializer
			var variable = SyntaxFactory.VariableDeclarator(name)
				.WithInitializer(
					SyntaxFactory.EqualsValueClause(initializer)
				);

			// 2) Type variableList
			var declaration = SyntaxFactory.VariableDeclaration(type)
				.WithVariables(
					SyntaxFactory.SingletonSeparatedList(variable)
				);

			// 3) Modifiers
			var tokens = modifierKinds
				.Select(k => SyntaxFactory.Token(k))
				.ToArray();
			var modifiers = SyntaxFactory.TokenList(tokens);

			// 4) FieldDeclaration
			return SyntaxFactory.FieldDeclaration(declaration)
				.WithModifiers(modifiers);
		}

		internal static MemberAccessExpressionSyntax CreateMemberAccess(string baseName, string targetName)
        {
            return SyntaxFactory.MemberAccessExpression(
	            SyntaxKind.SimpleMemberAccessExpression,
	            CreateQualifiedName(baseName),
	            SyntaxFactory.IdentifierName(targetName)
            );
		}

		/// <summary>
		/// Creates a SyntaxList of UsingDirectiveSyntax from lines like
		///     using static Foo.Bar;
		///     using Alias = Baz.Qux;
		/// </summary>
		public static SyntaxList<UsingDirectiveSyntax> BuildUsingDirectiveSyntaxList(string text)
		{
			var list = new List<UsingDirectiveSyntax>();
			var lines = text
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (!line.StartsWith("using", StringComparison.Ordinal))
					continue;

				// drop the "using" prefix and the trailing ";"
				var body = line.Substring(5).Trim();
				if (body.EndsWith(";"))
					body = body.Substring(0, body.Length - 1).Trim();

				// detect "static"
				bool isStatic = false;
				if (body.StartsWith("static ", StringComparison.Ordinal))
				{
					isStatic = true;
					body = body.Substring(6).Trim();
				}

				// detect alias ("Name = Qualified.Name")
				NameEqualsSyntax alias = null;
				string nameText;
				var eqIndex = body.IndexOf('=');
				if (eqIndex >= 0)
				{
					var aliasName = body.Substring(0, eqIndex).Trim();
					nameText = body.Substring(eqIndex + 1).Trim();
					alias = SyntaxFactory.NameEquals(
						SyntaxFactory.IdentifierName(aliasName)
					);
				}
				else
				{
					nameText = body;
				}

				// build the NameSyntax (could be a dotted name)
				NameSyntax nameSyntax = CreateQualifiedName(nameText);

				// create the directive
				var u = SyntaxFactory.UsingDirective(nameSyntax)
									 .WithAlias(alias)
									 .WithStaticKeyword(
										 isStatic
										   ? SyntaxFactory.Token(SyntaxKind.StaticKeyword)
										   : default
									  );

				list.Add(u);
			}

			return SyntaxFactory.List(list);
		}

		internal UsingDirectiveSyntax CreateUsingDirective(string usingName)
        {
            string alias = null;

            if (usingName.Contains("="))
            {
                // Handle alias import
                var parts = usingName.Split('=');
                alias = parts[0].Trim();
                usingName = parts[1].Trim();
            }

            var qualifiedName = CreateQualifiedName(usingName);

            if (alias != null)
                return SyntaxFactory.UsingDirective(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias)), qualifiedName);

            return SyntaxFactory.UsingDirective(qualifiedName);
        }

        internal static LiteralExpressionSyntax NumericLiteralExpression(string value)
        {
            if (value.Contains("."))
            {
                double.TryParse(value, CultureInfo.InvariantCulture, out double result);
				value = value.EndsWith("d", StringComparison.OrdinalIgnoreCase)
	                ? value
	                : value + "d";
				return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value, result));
            }
            else if (value.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
            {
                long.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long result);
                return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(result));
            }
            else if (value.StartsWith("0b", StringComparison.InvariantCultureIgnoreCase))
            {
                long.TryParse(value.Substring(2), NumberStyles.AllowBinarySpecifier, CultureInfo.InvariantCulture, out long result);
                return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(result));
            }
            else
            {
                long.TryParse(value, out long result);
                return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(result));
            }
        }

        internal static InvocationExpressionSyntax CreateBinaryOperatorExpression(int op, ExpressionSyntax exprL, ExpressionSyntax exprR)
        {
            return SyntaxFactory.InvocationExpression(
                ScriptOperateName,
				CreateArgumentList(
				    CreateMemberAccess("Keysharp.Scripting.Script.Operator", binaryOperators[op]),
                    exprL,
                    exprR
                )
            );
        }

        // Checks whether a local or static variable is in the current function or any parent functions.
        // See also: IsVarDeclaredLocally
        internal string IsLocalVar(string name, bool caseSense = true)
        {
            if (caseSense)
            {
                if (currentFunc.Locals.ContainsKey(name) || currentFunc.Statics.Contains(name))
                    return name;
            }
            else
            {
                string match = currentFunc.Locals.Keys.FirstOrDefault(v => v.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                if (match != null)
                    return match;
                match = currentFunc.Statics.FirstOrDefault(v => v.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                if (match != null)
                    return match;
            }

			if (currentFunc.Params != null)
			{
				var parameterMatch = currentFunc.Params.FirstOrDefault(param =>
					caseSense ? param.Identifier.Text == name
							  : param.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
				if (parameterMatch != null)
					return parameterMatch.Identifier.Text;
			}

			if (!currentFunc.Static)
            {
                // Skip the UserMainFunction function in the stack and check the rest
                foreach (var (func, _) in FunctionStack.SkipLast(1))
                {
					if (caseSense)
                    {
                        if (func.Locals.ContainsKey(name) || func.Statics.Contains(name))
                            return name;
                    }
                    else
                    {
                        string match = func.Locals.Keys.FirstOrDefault(v => v.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                        if (match != null)
                            return match;
                        match = func.Statics.FirstOrDefault(v => v.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                        if (match != null)
                            return match;
                    }

					if (func.Params != null)
					{
						var parameterMatch = func.Params.FirstOrDefault(param =>
							caseSense ? param.Identifier.Text == name
									  : param.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
						if (parameterMatch != null)
							return parameterMatch.Identifier.Text;
					}

                    if (func.Static)
                        break;
				}
            }

            return null;
        }

        // Whether the variable was a by-reference parameter
        internal string IsVarRef(string name)
        {
            string match = null;
            if (currentFunc.VarRefs.TryGetValue(name, out match))
                return match;

            foreach (var (func, _) in FunctionStack.SkipLast(1))
            {
                if (func.VarRefs.TryGetValue(name, out match))
                    return match;
            }
            return null;
        }

        internal string IsStaticVar(string name)
        {
            if (currentFunc.Statics.TryGetValue(name, out var match))
                return match;
            return null;
        }

        // Checks whether a local or static variable has been declared in the C# function body
        internal string IsVarDeclaredLocally(string name, bool caseSense = true)
        {
            if (currentFunc.Body == null)
                return null;

            if (currentFunc.Params != null)
            {
                var parameterMatch = currentFunc.Params.FirstOrDefault(param =>
                    caseSense ? param.Identifier.Text == name
                              : param.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (parameterMatch != null)
                    return parameterMatch.Identifier.Text;
            }

            var localMatch = IsLocalVar(name, caseSense);
            if (localMatch != null) return localMatch;

            var variableDeclarations = currentFunc.Body.OfType<LocalDeclarationStatementSyntax>();
            if (variableDeclarations != null)
            {
                foreach (var declaration in variableDeclarations)
                {
                    VariableDeclaratorSyntax match;
                    if (caseSense)
                        match = declaration.Declaration.Variables.FirstOrDefault(v => v.Identifier.Text == name);
                    else
                        match = declaration.Declaration.Variables.FirstOrDefault(v => v.Identifier.Text.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                    if (match != null)
                        return match.Identifier.Text;
                }
            }

            if (IsStaticDefinedInThisOrParent(name) is string staticName && staticName != null)
                return staticName;

            return null;
        }

        internal string IsStaticDefinedInThisOrParent(string name)
        {
            if (currentFunc == null) return null;
            name = name.ToLowerInvariant();
            string staticName = CreateStaticIdentifier(name);

            if (currentFunc.Statics.Contains(staticName)) return staticName;
            if (currentFunc.Locals.ContainsKey(name)) return null;

            foreach (var (f, _) in FunctionStack)
            {
                if (f.Name == Keywords.AutoExecSectionName)
                    break;

                staticName = f.Name.ToUpper() + "_" + name.TrimStart('@');
                if (f.Statics.Contains(staticName)) return staticName;
                if (f.Locals.ContainsKey(name)) return null;
            }
            return null;
        }

        // Checks whether the variable has been declared as global 
        internal string IsGlobalVar(string name, bool caseSense = true)
        {
            if (caseSense)
            {
                if (currentFunc.Globals.Contains(name))
                    return name;
            }
            else
            {
                string match = currentFunc.Globals.FirstOrDefault(v => v.Equals(name, StringComparison.InvariantCultureIgnoreCase));
                if (match != null)
                    return match;
            }
            return null;
        }

        // Checks for a field declaration in a specific class
        internal string IsVarDeclaredInClass(Class cls, string name, bool caseSense = true)
        {
            var body = cls.Body;
			var currentCount = body.Count;

			if (cls.lastCheckedBodyCount < currentCount)
            {
                for (int i = cls.lastCheckedBodyCount; i < currentCount; i++)
                {
					FieldDeclarationSyntax fds = body[i] as FieldDeclarationSyntax;
					if (fds == null)
						continue;

					foreach (var varSyntax in fds.Declaration.Variables)
                    {
                        var identifier = varSyntax.Identifier.Text;
                        cls.cachedFieldNames.Add(identifier);
					}
				}
                cls.lastCheckedBodyCount = currentCount;

			}
            if (caseSense)
            {
                if (cls.cachedFieldNames.Contains(name))
                    return name;
            } 
            else
            {
                var match = cls.cachedFieldNames.FirstOrDefault(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return null;
		}

        internal string IsVarDeclaredGlobally(string name, bool caseSense = true)
        {
            return IsVarDeclaredInClass(mainClass, name, caseSense);
        }

        internal string IsBuiltIn(string name, bool caseSense = false)
        {
            var builtin = IsBuiltInMethod(name, caseSense);
            if (builtin != null) return builtin;

            builtin = IsBuiltInProperty(name, caseSense, true);
            if (builtin != null) return builtin;

            if (Script.TheScript.ReflectionsData.stringToTypes.ContainsKey(name))
                return caseSense ? ((Script.TheScript.ReflectionsData.stringToTypes.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key) == name ? name : null) : name.ToLower();

            return null;
        }

        internal string IsBuiltInProperty(string name, bool caseSense = false, bool ignoreExtensionClass = false)
        {
            var script = Script.TheScript;
			KeyValuePair<string, PropertyInfo> match;
            if (caseSense && script.ReflectionsData.flatPublicStaticProperties.ContainsKey(name))
                return name;
            else
                match = script.ReflectionsData.flatPublicStaticProperties.FirstOrDefault(v => v.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (!ignoreExtensionClass
                && match.Key == null
                && currentClass != null
                && script.ReflectionsData.stringToTypeProperties.ContainsKey(name))
            {
                var baseName = currentClass.Base;
                while (UserTypes.ContainsKey(baseName))
                {
                    if (baseName == UserTypes[baseName])
                        break;
                    baseName = UserTypes[baseName];
                }

                if (script.ReflectionsData.stringToTypeProperties[name].Keys.Any(item => item.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                    return script.ReflectionsData.stringToTypeProperties.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
            }
            return match.Key;
        }

		internal string IsBuiltInMethod(string name, bool caseSense = false)
		{
			var rd = Script.TheScript.ReflectionsData;

			// Fast O(1): this is filled in Initialize() from Keysharp.* static classes.
			if (rd.flatPublicStaticMethods.TryGetValue(name, out var mi))
			{
				// If exact case is required, confirm it (dictionary is OrdinalIgnoreCase).
				if (!caseSense || mi.Name.Equals(name, StringComparison.Ordinal))
					return mi.Name; // canonical casing
			}
			return null;
		}

		// Either adds a new global variable declaration, or returns a previously declared ones name
		internal string MaybeAddGlobalVariableDeclaration(string name, bool caseSense = true)
        {
            string match = IsVarDeclaredGlobally(name, caseSense);
            if (match != null)
                return match;

            if (!caseSense)
                name = name.ToLowerInvariant();

            var variableDeclaration = SyntaxFactory.VariableDeclaration(PredefinedKeywords.ObjectType)
                .AddVariables(SyntaxFactory.VariableDeclarator(name));

            var fieldDeclaration = SyntaxFactory.FieldDeclaration(variableDeclaration)
                .AddModifiers(
                    Parser.PredefinedKeywords.PublicToken,
                    Parser.PredefinedKeywords.StaticToken);

            mainClass.Body.Add(fieldDeclaration);

            return name;
        }

        internal string IsVariableDeclared(string name, bool caseSense = true)
        {
			string match;
			if (currentFunc.Scope == eScope.Static)
			{
				match = IsVarDeclaredInClass(currentClass, name);
				if (match != null) return match;
			}
			if (currentFunc.Scope == eScope.Local || currentFunc.Scope == eScope.Static)
			{
				// If the variable is supposed to be global then don't add a local declaration
				match = IsGlobalVar(name, caseSense);
				if (match != null) return match;

				// Otherwise if a local declaration is present then return it
				match = IsVarDeclaredLocally(name, caseSense);
				if (match != null) return match;
			}
			else if (currentFunc.Scope == eScope.Global)
			{
				// If the variable is supposed to be local then return it
				match = IsLocalVar(name);
				if (match != null)
					return match;

				match = IsVarDeclaredGlobally(name, caseSense);
				if (match != null) return match;
			}

			if (IsBuiltIn(name, caseSense) is string builtin && builtin != null)
				return builtin;

            return null;
		}

        // Either adds a new local, static, or global variable declaration (depending on scope),
        // or returns a previously declared variable name
        internal string MaybeAddVariableDeclaration(string name, bool caseSense = true)
        {
            string match = IsVariableDeclared(name, caseSense);
            if (match != null)
                return match;

            return AddVariableDeclaration(name, caseSense);
        }

        // Unconditionally adds a static, local, or global variable declaration
        internal string AddVariableDeclaration(string name, bool caseSense = true)
        {
            if (!caseSense)
                name = name.ToLowerInvariant();

            if (currentFunc.Scope == eScope.Static && !currentFunc.Statics.Contains(name))
            {
                currentFunc.Statics.Add(name);
            }

            var variableDeclaration = SyntaxFactory.VariableDeclaration(
            Parser.PredefinedKeywords.ObjectType)
            .AddVariables(
                SyntaxFactory.VariableDeclarator(name)
                .WithInitializer(
                    SyntaxFactory.EqualsValueClause(
						PredefinedKeywords.EqualsToken,
						SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                    )
                )
            );

            if (currentFunc.Scope == eScope.Local)
            {
                var localDeclaration = SyntaxFactory.LocalDeclarationStatement(variableDeclaration);
                currentFunc.Locals[name] = (StatementSyntax)localDeclaration;
                return name;
            }

            var fieldDeclaration = SyntaxFactory.FieldDeclaration(variableDeclaration)
                .AddModifiers(
            Parser.PredefinedKeywords.PublicToken,
            Parser.PredefinedKeywords.StaticToken);

            if (currentFunc.Statics.Contains(name) || currentFunc.Scope == eScope.Static)
            {
                currentClass.Body.Add(fieldDeclaration);
            }
            else
                mainClass.Body.Add(fieldDeclaration);

            return name;
        }

        // // Adds `public static object myclass = Myclass.__Static;` as a global variable (the class static instance declaration)
        internal string MaybeAddClassStaticVariable(string className, bool caseSense = false)
        {
            string name = IsVarDeclaredGlobally(className, caseSense);
            if (name != null) return name;

            var nonescapedClassName = className.TrimStart('@');

            string alias = Keywords.TypeNameAliases.SingleOrDefault(kv => kv.Value.Equals(nonescapedClassName, StringComparison.OrdinalIgnoreCase)).Key;
            if (alias != null)
                nonescapedClassName = alias;

			// Only add built-in classes, because user-defined classes are handled in the constructor
			if (!Parser.BuiltinTopLevelTypes.ContainsKey(nonescapedClassName))
                return null;
            string casedName = Parser.BuiltinTopLevelTypes.SingleOrDefault(kv => kv.Key.Equals(nonescapedClassName, StringComparison.OrdinalIgnoreCase)).Key;

            // Add `internal static object myclass = Myclass.__Static;` global field
            var fieldDeclaration = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    Parser.PredefinedKeywords.ObjectType,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(className)
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
									PredefinedKeywords.EqualsToken,
									SyntaxFactory.ElementAccessExpression(
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
											VarsNameSyntax,
                                            SyntaxFactory.IdentifierName("Statics")
                                        ),
                                        SyntaxFactory.BracketedArgumentList(
                                            SyntaxFactory.SingletonSeparatedList(
                                                SyntaxFactory.Argument(
                                                    SyntaxFactory.TypeOfExpression(
                                                        SyntaxFactory.IdentifierName(casedName)
                                                    )
                                                )
                                            )
                                        )
                                    )
                                )
                            )
                    )
                )
            )
            .AddModifiers(
                Parser.PredefinedKeywords.PublicToken,
                Parser.PredefinedKeywords.StaticToken
            );

            mainClass.Body.Add(fieldDeclaration);

            return className.ToLower();
        }

        // Adds a global FuncObj variable if one is not already present,
        // eg `public static object msgbox = FuncObj(MsgBox);`
        internal string MaybeAddGlobalFuncObjVariable(string functionName, bool caseSense = true)
        {
            functionName = functionName.TrimStart('@');
            string name = IsVarDeclaredGlobally(ToValidIdentifier(functionName), caseSense);
            if (name != null) return name;

            // Only add built-in functions, because user-defined functions are handled in the constructor
            name = IsBuiltInMethod(functionName, caseSense);
            if (name == null) return caseSense ? functionName : functionName.ToLowerInvariant();

            functionName = name;

            return AddGlobalFuncObjVariable(functionName, caseSense);
        }

        internal string AddGlobalFuncObjVariable(string functionName, bool caseSense = false)
        {
            if (!caseSense)
                functionName = functionName.ToLowerInvariant();

            var funcObjVariable = SyntaxFactory.FieldDeclaration(
                CreateFuncObjVariable(functionName)
            )
            .WithModifiers(
                SyntaxFactory.TokenList(
                    Parser.PredefinedKeywords.PublicToken,
                    Parser.PredefinedKeywords.StaticToken
                )
            );

            string identifier = ToValidIdentifier(functionName);

			for (int i = 0; i < mainClass.Body.Count; i++)
            {
                var mds = mainClass.Body[i];
                if (mds is FieldDeclarationSyntax fds && fds.Declaration.Variables.First().Identifier.Text == identifier)
                {
                    mainClass.Body[i] = fds;
                    return identifier;
                }
            }

            mainClass.Body.Add(funcObjVariable);
            return identifier;
        }

        // Used to create function object variables for closures. The closure is cast to a Delegate
        // and then the FuncObj is assigned to a lower-cased variable of the closure name.
        internal static VariableDeclarationSyntax CreateFuncObjDelegateVariable(string functionName)
        {
            return SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(functionName.ToLowerInvariant())
                        )
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
								PredefinedKeywords.EqualsToken,
								CreateFuncObj(
                                    SyntaxFactory.CastExpression(
										CreateQualifiedName("System.Delegate"),
                                        SyntaxFactory.IdentifierName(functionName)
                                    )
                                )
                            )
                        )
                    )
                );
        }

        internal static InvocationExpressionSyntax CreateFuncObj(ExpressionSyntax funcArg, bool asClosure = false)
        {
            return (asClosure ? (InvocationExpressionSyntax)InternalMethods.Closure : (InvocationExpressionSyntax)InternalMethods.Func)
                .WithArgumentList(
                    CreateArgumentList(funcArg)
                );
        }

        internal VariableDeclarationSyntax CreateFuncObjVariable(string functionName)
        {
            return SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(ToValidIdentifier(functionName))
                        )
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                PredefinedKeywords.EqualsToken,
                                CreateFuncObj(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(functionName)
                                    )
                                )
                            )
                        )
                    )
                );
        }

        // Creates a VariableDeclarationSyntax for `object var = null;`
        internal static VariableDeclarationSyntax CreateNullObjectVariable(string variableName)
        {
            return SyntaxFactory.VariableDeclaration(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(variableName))
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                            )
                        )
                    )
                );
        }

        internal static ExpressionSyntax CreateSimpleAssignment(string variableName, ExpressionSyntax value)
        {
            return SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(variableName),
                PredefinedKeywords.EqualsToken,
                value
            );
        }

		/*
        Converts an ExpressionSyntax to a function InvocationExpressionSyntax. 
        1. Built-in functions get called with the fully qualified name, eg MsgBox -> Keysharp.Core.Dialogs.MsgBox
        EXCEPT if the argument list contains variadic arguments, in which case Invoke is used.
        2. User-declared classes are converted to Invoke(targetExpression, "Call", arguments)
        3. Property access such as `obj.propName()` is converted to Invoke(obj, propName, args)
        4. GetStaticMemberValueT gets converted to Invoke(GetStaticMethodT<typeName>(methodName, -1))
        5. Anything else is converted to Invoke(targetExpression, "Call", arguments)
        */
		public ExpressionSyntax GenerateFunctionInvocation(
            ExpressionSyntax targetExpression,
            ArgumentListSyntax argumentList,
            string methodName)
        {
            // 1. Built-in functions: Directly invoke the built-in method
            // except in the case of a variadic function call
            if (!string.IsNullOrEmpty(methodName)
				&& TheScript.ReflectionsData.flatPublicStaticMethods.TryGetValue(methodName, out var mi)
				&& !UserFuncs.Contains(methodName)
                && IsVarDeclaredLocally(methodName) == null
                )
            {
                if (argumentList.Arguments.Count > 0 && argumentList.Arguments.First().Expression is CollectionExpressionSyntax)
                {
                    targetExpression = ((InvocationExpressionSyntax)InternalMethods.Func)
                    .WithArgumentList(
						CreateArgumentList(targetExpression)
                    );
                } else
                    // Fully qualified method invocation
                    return SyntaxFactory.InvocationExpression(
						CreateMemberAccess(mi.DeclaringType.ToString(), mi.Name),
                        argumentList
                    );
            }

            // 2. Handle UserTypes
            if (targetExpression is IdentifierNameSyntax identifierName &&
                UserTypes.ContainsKey(identifierName.Identifier.Text))
            {
                // Convert to Invoke(targetExpression, "Call", arguments)
				return ((InvocationExpressionSyntax)InternalMethods.Invoke)
                    .WithArgumentList(
					CreateArgumentList(
						targetExpression,
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal("Call")
                        ),
						argumentList.Arguments // Include additional arguments
                    )
                );
            }

            // 3. Handle GetPropertyValue invocation
            if (targetExpression is InvocationExpressionSyntax invocationExpression &&
                CheckInvocationExpressionName(invocationExpression, "GetPropertyValue"))
            {
                // Extract arguments of GetPropertyValue
                var propertyArguments = invocationExpression.ArgumentList.Arguments;
                if (propertyArguments.Count == 2)
                {
                    // Extract base expression and property name
                    var baseExpression = propertyArguments[0].Expression;
                    var propertyNameExpression = propertyArguments[1].Expression;

                    // Generate Script.Invoke(obj, prop, args)
                    return ((InvocationExpressionSyntax)InternalMethods.Invoke)
                        .WithArgumentList(
                            CreateArgumentList(
                                baseExpression,
                                propertyNameExpression,
								argumentList.Arguments // Pass additional arguments (args)
                            )
                        );
                }
            }


            // 4. Handle GetStaticMemberValueT invocation
            if (targetExpression is InvocationExpressionSyntax staticMemberInvocation &&
                staticMemberInvocation.Expression is MemberAccessExpressionSyntax staticMemberAccess &&
                staticMemberAccess.Name.Identifier.Text.Equals("GetStaticMemberValueT", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the generic type and argument
                if (staticMemberAccess.Name is GenericNameSyntax genericName &&
                    staticMemberInvocation.ArgumentList.Arguments.Count == 1)
                {
                    var typeArguments = genericName.TypeArgumentList;
                    var memberNameExpression = staticMemberInvocation.ArgumentList.Arguments[0].Expression;

                    // Transform into Script.Invoke(Script.GetStaticMethodT<typeName>(methodName, -1))
                    var getStaticMethodInvocation = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            CreateQualifiedName("Keysharp.Scripting.Script"),
                            SyntaxFactory.GenericName("GetStaticMethodT")
                                .WithTypeArgumentList(typeArguments)
                        ),
                        CreateArgumentList(
                            memberNameExpression,
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(-1)
                            )
                        )
                    );

                    // Wrap in Script.Invoke
                    return ((InvocationExpressionSyntax)InternalMethods.Invoke)
                        .WithArgumentList(
                        CreateArgumentList(
                            getStaticMethodInvocation,
                            argumentList.Arguments
                        )
                    );
                }
            }

            // 5. Default behavior: Treat as callable object and invoke .Call
            return ((InvocationExpressionSyntax)InternalMethods.Invoke)
            .WithArgumentList(
                CreateArgumentList(
					targetExpression,
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("Call")
                    ),
					argumentList.Arguments // Include additional arguments
                )
           );
        }


        internal static string ExtractMethodName(ExpressionSyntax expression)
        {
            if (expression is IdentifierNameSyntax identifierName)
            {
                return identifierName.Identifier.Text;
            }
            else if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.Text;
            }
            else if (expression is QualifiedNameSyntax qes)
            {
                return qes.Right.Identifier.Text;
            }
            return null;
        }

        internal string NormalizeClassIdentifier(string name, eNameCase nameCase = eNameCase.Title)
        {
            // Define the reserved keywords that should retain their original case

            if (nameCase == eNameCase.Title && ClassReservedKeywords.Contains(name))
                return ClassReservedKeywords.First(keyword => string.Equals(keyword, name, StringComparison.InvariantCultureIgnoreCase));

            var builtinProp = PropertyExistsInBuiltinBase(name);
            if (builtinProp != null) return builtinProp;

            return NormalizeIdentifier(name, nameCase);
        }

        // A static variable is named "UPPERCASEFUNCTIONNAME_lowercasevariablename"
        internal string CreateStaticIdentifier(string name)
        {
            if (currentFunc == null) return null;
            return currentFunc.Name.ToUpper() + "_" + name.ToLowerInvariant().TrimStart('@');
        }

        internal ExpressionStatementSyntax CreateStaticVariableInitializer(IdentifierNameSyntax identifier, ExpressionSyntax initializerValue)
        {
			var invocationExpression = SyntaxFactory.InvocationExpression(
                CreateMemberAccess("Keysharp.Scripting.Script", "InitStaticVariable"),
                CreateArgumentList(
                    // Argument: ref identifier
                    SyntaxFactory.Argument(identifier)
						.WithRefOrOutKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)),

                    // Argument: identifier as a string literal
					SyntaxFactory.LiteralExpression(
						SyntaxKind.StringLiteralExpression,
						SyntaxFactory.Literal(currentClass.Name + "_" + identifier.Identifier.Text)
					),

                    // Argument: () => initializerValue
					SyntaxFactory.ParenthesizedLambdaExpression(initializerValue)
		        )
            );
			return SyntaxFactory.ExpressionStatement(invocationExpression);
		}

        internal string NormalizeFunctionIdentifier(string name, eNameCase nameCase = eNameCase.Lower)
        {
            name = name.Trim('"', '\'');
            var normalizedName = NormalizeIdentifier(name, nameCase);

            // First check whether it's a static variable or should be a static variable,
            // then check if it's a built-in method, property, or a type

            if (IsVarDeclaredLocally(normalizedName, true) is string localName && localName != null)
                return localName;

            // This should be handled by IsVarDeclaredLocally
            //if (IsStaticDefinedInThisOrParent(normalizedName) is string staticName && staticName != null)
            //    return staticName;

            if (IsVarDeclaredGlobally(normalizedName, true) is string globalName && globalName != null)
                return globalName;

            if (UserTypes.ContainsKey(normalizedName))
                return normalizedName;

            var builtin = IsBuiltInProperty(name, false, true);
            if (builtin != null) return builtin;

            // Normalize before checking these, because method and type identifiers will all be lower-case
            builtin = IsBuiltInMethod(normalizedName);
            if (builtin != null) return normalizedName;

            if (Script.TheScript.ReflectionsData.stringToTypes.ContainsKey(normalizedName))
                return normalizedName;

            if (currentFunc.Scope == eScope.Static)
                return CreateStaticIdentifier(normalizedName);

            return normalizedName;
        }

        internal string NormalizeIdentifier(string name, eNameCase nameCase = eNameCase.Lower)
        {
            name = name.Trim('"', '\'');

            //if (Parser.TypeNameAliases.ContainsKey(name) && Reflections.stringToTypes.ContainsKey(Parser.TypeNameAliases[name]))
            //    name = Parser.TypeNameAliases[name];

            if (nameCase == eNameCase.Lower)
                return ToValidIdentifier(name.ToLowerInvariant());
            else if (nameCase == eNameCase.Title)
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
            else
                return name.ToUpperInvariant();
        }

        // Escapes keyword identifiers with @
        internal string ToValidIdentifier(string text)
        {
            if (SyntaxFacts.IsKeywordKind(SyntaxFactory.ParseToken(text).Kind()))
            {
				if (hasVisitedIdentifiers && text.Equals("null", StringComparison.OrdinalIgnoreCase) && IsVariableDeclared("@null", false) == null)
				{
					return "null";
				}
				return "@" + text;
            }
            return text;
        }

        internal static void UserTypeNameToKeysharp(ref string text)
        {
            var buf = text;
			string alias = Keywords.TypeNameAliases.SingleOrDefault(kv => kv.Value.Equals(buf, StringComparison.OrdinalIgnoreCase)).Key;
			if (alias != null)
				text = alias;
		}

        internal string PropertyExistsInBuiltinBase(string name)
        {
            if (Script.TheScript.ReflectionsData.stringToTypeProperties.TryGetValue(name, out var dttp))
            {
                string className = currentClass.Name;
                while (AllTypes.TryGetValue(className, out string classBase))
                {
                    className = classBase;
                    if (dttp.Any(t => t.Key.Name == className))
                        return Script.TheScript.ReflectionsData.stringToTypeProperties.Keys
                            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
                }
            }
            return null;
        }

        internal static BlockSyntax EnsureBlockSyntax(SyntaxNode node)
        {
            if (node is BlockSyntax block)
                return block;
            else if (node is StatementSyntax statement)
                return SyntaxFactory.Block(statement);
            else if (node is ExpressionSyntax expression)
                return SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(expression));
            else
                throw new InvalidOperationException("Unsupported SyntaxNode type for loop body.");
        }

        internal static StatementSyntax EnsureStatementSyntax(SyntaxNode node)
        {
            if (node is StatementSyntax statement)
                return statement;
            else if (node is ExpressionSyntax expression)
                return SyntaxFactory.ExpressionStatement(expression);
            else
                throw new InvalidOperationException("Unsupported node type in statement list.");
        }

        internal static StatementSyntax EnsureBreakStatement(StatementSyntax statements, int indent = 0)
        {
            if (statements is BlockSyntax blockSyntax)
            {
                var lastStatement = blockSyntax.Statements.LastOrDefault();
                if (lastStatement == null || !(lastStatement is BreakStatementSyntax))
                {
                    // Add a break statement to the block if not present
                    return blockSyntax.AddStatements(SyntaxFactory.BreakStatement());
                }
                return blockSyntax; // Block already has a break
            }
            else if (!(statements is BreakStatementSyntax))
            {
                // Wrap single statement in a block and add a break
                return SyntaxFactory.Block(
                    new StatementSyntax[] { statements, SyntaxFactory.BreakStatement() }
                );
            }

            // Already a break statement
            return statements;
        }


        internal static FieldDeclarationSyntax CreatePublicConstant(string name, Type type, object value)
        {
            return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ParseTypeName(type.FullName),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(
                                SyntaxFactory.Identifier(name))
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
                                    PredefinedKeywords.EqualsToken,
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(value.ToString())
                                    )
                                )
                            )
                        )
                    )
                )
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        Parser.PredefinedKeywords.PublicToken,
                        SyntaxFactory.Token(SyntaxKind.ConstKeyword)
                    )
                );
        }

		public static ArgumentListSyntax CreateArgumentList(params object[] items)
		{
			if (items == null) throw new ArgumentNullException(nameof(items));

			var args = new List<ArgumentSyntax>();
			foreach (var it in items)
			{
				switch (it)
				{
					case ArgumentSyntax a:
						args.Add(a);
						break;
					case CollectionExpressionSyntax e:
						args.Add(SyntaxFactory.Argument(e));
						break;
					case ExpressionSyntax e:
						args.Add(SyntaxFactory.Argument(e));
						break;
					case IEnumerable<ArgumentSyntax> iea:
						args.AddRange(iea);
						break;
					case IEnumerable<SyntaxNode> ies:
                        foreach (var sn in ies)
                        {
                            if (sn is ExpressionSyntax es)
                                args.Add(SyntaxFactory.Argument(es));
                            else if (sn is ArgumentSyntax a)
                                args.Add(a);
                            else
                                throw new ArgumentException(
                                    "Arguments must be either ArgumentSyntax or ExpressionSyntax",
                                    nameof(items));
						}
						break;
					default:
						throw new ArgumentException(
							"Arguments must be either ArgumentSyntax or ExpressionSyntax",
							nameof(items));
				}
			}

            if (args.Count == 1)
                return SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(args[0]));

			return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args));
		}

		public BlockSyntax RemoveLocalVariable(BlockSyntax block, string variableName)
        {
            var variableDeclaration = block.Statements
                .OfType<LocalDeclarationStatementSyntax>()
                .FirstOrDefault(declaration =>
                    declaration.Declaration.Variables.Any(v => v.Identifier.Text == variableName));

            if (variableDeclaration != null)
                return block.RemoveNode(variableDeclaration, SyntaxRemoveOptions.KeepNoTrivia);

            return block;
        }

        public bool RemoveGlobalVariable(string variableName, bool local)
        {
            var fieldDeclaration = mainClass.Body
                .OfType<FieldDeclarationSyntax>()
                .FirstOrDefault(declaration =>
                    declaration.Declaration.Variables.Any(v => v.Identifier.Text == variableName));

            if (fieldDeclaration != null)
            {
                mainClass.Body.Remove(fieldDeclaration);
                return true;
            }

            return false;
        }

        public static ExpressionSyntax ConstructVarRefFromIdentifier(string identifier)
        {
            var identifierName = SyntaxFactory.IdentifierName(identifier);
            return SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.IdentifierName("VarRef"),
                CreateArgumentList(
                    // Getter lambda: () => identifier
                    SyntaxFactory.ParenthesizedLambdaExpression(
                        SyntaxFactory.ParameterList(),
                        identifierName
                    ),
                    // Setter lambda: value => identifier = value
                    SyntaxFactory.ParenthesizedLambdaExpression(
                        SyntaxFactory.ParameterList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                            )
                        ),
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            identifierName,
						    PredefinedKeywords.EqualsToken,
						    SyntaxFactory.IdentifierName("value")
                        )
                    )
                ),
                null
            );
        }

        // Creates `new VarRef(() => identifier, value => identifier = value)`
        // In the case of index or property access, it uses the appropriate get/set methods.
        public ExpressionSyntax ConstructVarRef(ExpressionSyntax targetExpression, bool throwIfInvalid = true)
        {
            // Handle the different cases of singleExpression
            if (targetExpression is IdentifierNameSyntax identifierName)
            {
                // Case: Variable identifier
                var addedName = MaybeAddVariableDeclaration(identifierName.Identifier.Text);
                if (addedName != null && addedName != identifierName.Identifier.Text)
                    targetExpression = identifierName = SyntaxFactory.IdentifierName(addedName);
                return ConstructVarRefFromIdentifier(identifierName.Identifier.Text);
            }
            else if (targetExpression is MemberAccessExpressionSyntax memberAccess)
            {
                // Case: MemberDotExpression
                return SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("VarRef"),
                    CreateArgumentList(
                        // Getter lambda: () => obj.property
                        SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(),
                            memberAccess
                        ),
                        // Setter lambda: value => obj.property = value
                        SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                                )
                            ),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                memberAccess,
							    PredefinedKeywords.EqualsToken,
							    SyntaxFactory.IdentifierName("value")
                            )
                        )
                    ),
                    null
                );
            }
            else if (targetExpression is ElementAccessExpressionSyntax elementAccess)
            {
                // Case: MemberIndexExpression
                var baseExpression = elementAccess.Expression;
                var indexExpression = elementAccess.ArgumentList.Arguments.First().Expression;

                return SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("VarRef"),
                        CreateArgumentList(
                            // Getter lambda: () => obj[index]
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(),
                                SyntaxFactory.ElementAccessExpression(baseExpression)
                                    .WithArgumentList(
                                        SyntaxFactory.BracketedArgumentList(
                                            SyntaxFactory.SingletonSeparatedList(
                                                SyntaxFactory.Argument(indexExpression)
                                            )
                                        )
                                    )
                            ),
                            // Setter lambda: value => obj[index] = value
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                                    )
                                ),
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.ElementAccessExpression(baseExpression)
                                        .WithArgumentList(
                                            SyntaxFactory.BracketedArgumentList(
                                                SyntaxFactory.SingletonSeparatedList(
                                                    SyntaxFactory.Argument(indexExpression)
                                                )
                                            )
                                        ),
                                    PredefinedKeywords.EqualsToken,
                                    SyntaxFactory.IdentifierName("value")
                                )
                            )
                    ),
                    null
                );
            }
            else if (targetExpression is InvocationExpressionSyntax invocationExpression)
            {
                // Handle Keysharp.Scripting.Script.Index(varname, index)
                if (CheckInvocationExpressionName(invocationExpression, "Index", true))
                {
                    return SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("VarRef"),
                        CreateArgumentList(
                        // Getter lambda: () => Keysharp.Scripting.Script.Index(varname, index)
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(),
                                invocationExpression
                            ),
                        // Setter lambda: value => Keysharp.Scripting.Script.SetObject(value, varname, index)
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                                    )
                                ),
                                ((InvocationExpressionSyntax)InternalMethods.SetObject)
                                .WithArgumentList(
                                    CreateArgumentList(
										invocationExpression.ArgumentList.Arguments,
										SyntaxFactory.IdentifierName("value")
									)
                                )
                            )
                        ),
                        null
                    );
                }
                // Handle Keysharp.Scripting.Script.GetPropertyValue(obj, field)
                else if (CheckInvocationExpressionName(invocationExpression, "GetPropertyValue", true))
                {
                    return SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("VarRef"),
                        CreateArgumentList(
                        // Getter lambda: () => Keysharp.Scripting.Script.GetPropertyValue(obj, field)
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(),
								invocationExpression
							),
                        // Setter lambda: value => Keysharp.Scripting.Script.SetPropertyValue(obj, field, value)
                            SyntaxFactory.ParenthesizedLambdaExpression(
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                                    )
                                ),
                                ((InvocationExpressionSyntax)InternalMethods.SetPropertyValue)
                                .WithArgumentList(
                                    CreateArgumentList(
                                        invocationExpression.ArgumentList.Arguments, 
                                        SyntaxFactory.IdentifierName("value")
                                    )
                                )
                            )
                        ),
                        null
                    );
                }
                else if (invocationExpression.ArgumentList.Arguments.Count == 3 &&
                         invocationExpression.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax argumentName &&
                         argumentName.Token.Text == "\"__Value\"")
                {
                    return invocationExpression;
                }
            } else if (targetExpression is ObjectCreationExpressionSyntax oces
                && oces.Type.GetLastToken().ValueText == "VarRef")
            {
                return oces;
            }

            if (!throwIfInvalid)
            {
                var temp = PushTempVar();
                var varRef = ConstructVarRef(temp);
				var multiStatement = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
	            .WithArgumentList(
		            CreateArgumentList(
                        SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, temp, targetExpression),
                        varRef
                    )
	            );
				PopTempVar();

                return multiStatement;

			}

            throw new InvalidOperationException("Unsupported singleExpression type for VarRefExpression.");
        }

        internal static bool IsLiteralOrConstant(ExpressionSyntax expression)
        {
            return expression is LiteralExpressionSyntax ||
                   expression is TypeOfExpressionSyntax ||
                   expression is DefaultExpressionSyntax ||
                   expression is IdentifierNameSyntax identifier &&
                   char.IsUpper(identifier.Identifier.Text[0]); // Assume constants are upper-case
        }

        internal static bool CheckInvocationExpressionName(InvocationExpressionSyntax ies, string target, bool caseSense = false)
        {
            StringComparison comparison = caseSense ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            return (ies.Expression is MemberAccessExpressionSyntax maes && maes.Name.Identifier.Text.Equals(target, comparison))
                || (ies.Expression is QualifiedNameSyntax qes && qes.Right.Identifier.Text.Equals(target, comparison));
        }

        internal ExpressionSyntax CreateSuperTuple()
        {
            return SyntaxFactory.CastExpression(
                SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object),
                    SyntaxFactory.TupleExpression(
                        SyntaxFactory.SeparatedList<ArgumentSyntax>(
                            new ArgumentSyntax[]
                            {
                                SyntaxFactory.Argument(
                                    GenerateItemAccess(
										VarsNameSyntax,
										SyntaxFactory.IdentifierName(currentFunc.UserStatic ? "Statics" : "Prototypes"),
                                        SyntaxFactory.TypeOfExpression(
                                            CreateQualifiedName(currentClass.Base) // typeof(MyType)
                                        )
                                    )
                                ),
                                SyntaxFactory.Argument(PredefinedKeywords.This)
                            }
                        )
                    )
            );
        }

        internal static ExpressionSyntax GenerateItemAccess(ExpressionSyntax baseName, SimpleNameSyntax memberName, ExpressionSyntax item)
        {
            return SyntaxFactory.ElementAccessExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    baseName,
                    memberName
                ),
                SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            item
                        )
                    )
                )
            );
		}

        internal static ExpressionSyntax GenerateGetPropertyValue(ExpressionSyntax baseExpression, ExpressionSyntax memberExpression)
        {
			return SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "GetPropertyValue"),
                CreateArgumentList(baseExpression, memberExpression)
			);
		}

        // Non-constant optional parameter values get added to the top of the function body 
        // as `paramName ??= value` and the default value is set to null. This allows using
        // arbitrary expressions as default values.
        internal ParameterSyntax AddOptionalParamValue(ParameterSyntax parameter, ExpressionSyntax value)
        {
            // Extract the parameter name
            var parameterName = parameter.Identifier.Text;

            // Determine if the value is a constant
            bool isConstant = value is LiteralExpressionSyntax;

            // Check if the parameter type is VarRef
            var isVarRefType = parameter.AttributeLists
				.SelectMany(al => al.Attributes)
				.Any(attr => attr.Name.ToString().Equals("ByRef"));

            StatementSyntax initializationStatement = null;

            if (isVarRefType)
            {
                // Add the initialization statement to the function body
                initializationStatement = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.CoalesceAssignmentExpression,
                        SyntaxFactory.IdentifierName(parameterName),
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.IdentifierName("VarRef"),
                            CreateArgumentList(value),
                            null
                        )
                    )
                );

                // Set the default value to null
                value = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
            else if (!isConstant)
            {
                // Add the coalesce assignment to the function body if the value is non-constant
                initializationStatement = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.CoalesceAssignmentExpression,
                        SyntaxFactory.IdentifierName(parameterName),
                        value
                    )
                );

                // Set the default value to null
                value = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }

            if (initializationStatement != null)
            {
                currentFunc.Body.Add(initializationStatement);
            }

            // Add the attributes for Optional and DefaultParameterValue
            return parameter.WithAttributeLists(
				parameter.AttributeLists.Add(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SeparatedList(new[]
                        {
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Optional")),
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("DefaultParameterValue"),
                                SyntaxFactory.AttributeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.AttributeArgument(value)
                                    )
                                )
                            )
                        })
                    )
                )
            );
        }
	}
}
