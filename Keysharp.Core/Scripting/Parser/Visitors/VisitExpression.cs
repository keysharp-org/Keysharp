using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using static MainParser;
using static Keysharp.Scripting.Parser;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.AccessControl;
using System.Configuration;
using System.Xml.Linq;

namespace Keysharp.Scripting
{
    internal partial class VisitMain : MainParserBaseVisitor<SyntaxNode>
    {
        // Converts identifiers such as a%b% to
        // Keysharp.Scripting.Script.Vars[string.Concat<object>(new object[] {"a", b)]
        public SyntaxNode CreateDynamicVariableString(ParserRuleContext context)
        {
            // Collect the parts of the dereference expression
            var parts = new List<ExpressionSyntax>();

            foreach (var child in context.children)
            {
                if (child is PropertyNameContext)
                    parts.Add(SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(child.GetText().Trim())
                    ));
                else
                    parts.Add((ExpressionSyntax)Visit(child));
            }

            // Determine if there is only a single part (no string concatenation needed)
            ExpressionSyntax combinedExpression;
            if (parts.Count == 1)
                combinedExpression = parts[0];
            else
            {
                // Create string.Concat<object>(new object[] { ... }) for multiple parts
                combinedExpression = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        CreateQualifiedName("System.String"), // Roslyn doesn't like lowercase "string"
                        SyntaxFactory.IdentifierName("Concat")
                    ),
					CreateArgumentList(
						SyntaxFactory.ArrayCreationExpression(
                            SyntaxFactory.ArrayType(
                                SyntaxFactory.PredefinedType(
                                    Parser.PredefinedKeywords.Object
                                ),
                                SyntaxFactory.SingletonList(
                                    SyntaxFactory.ArrayRankSpecifier(
                                        SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                            SyntaxFactory.OmittedArraySizeExpression()
                                        )
                                    )
                                )
                            ),
                            SyntaxFactory.InitializerExpression(
                                SyntaxKind.ArrayInitializerExpression,
                                SyntaxFactory.SeparatedList(parts)
                            )
                        )
                    )
                );
            }

            // Wrap the combined expression in Keysharp.Scripting.Script.Vars[...]
            return combinedExpression;
        }

        public override SyntaxNode VisitDereference([NotNull] DereferenceContext context)
        {
            return Visit(context.expression());
        }

        public override SyntaxNode VisitMemberIdentifier([NotNull] MemberIdentifierContext context)
        {
            if (context.dynamicIdentifier() == null)
            {
                var text = context.GetText();
                return SyntaxFactory.IdentifierName(text);
            }
            return base.VisitMemberIdentifier(context);
        }

        public override SyntaxNode VisitIdentifierExpression([NotNull] IdentifierExpressionContext context)
        {
            return Visit(context.identifier());
        }

        public override SyntaxNode VisitPropertyName([NotNull] PropertyNameContext context)
        {
            return SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(context.GetText().Trim('"', ' '))
                    );
        }

		public override SyntaxNode VisitAccessExpression([NotNull] AccessExpressionContext context)
		{
            var accessSuffix = context.accessSuffix();
			if (accessSuffix.memberIdentifier() != null)
            {
				return GenerateMemberDotAccess(context.primaryExpression(), accessSuffix.memberIdentifier(), accessSuffix.memberIndexArguments());
			} 
            else if (accessSuffix.memberIndexArguments() != null)
            {
				return GenerateMemberIndexAccess(context.primaryExpression(), accessSuffix.memberIndexArguments(), accessSuffix.modifier != null);
			} 
            else if (accessSuffix.modifier != null && accessSuffix.modifier.Type == MainLexer.QuestionMark)
            {
                return Visit(context.primaryExpression());
            }
            else
            {
                ExpressionSyntax targetExpression = null;
                // Visit the singleExpression (the method to be called)
                string methodName = context.primaryExpression().GetText();

                // In case it's a variable
                var normalized = parser.IsVarDeclaredLocally(parser.NormalizeIdentifier(methodName));
                if (normalized != null)
                    methodName = normalized;
                // I don't like that this complicated check is repeated in GenerateFunctionInvocation,
                // but see no good way around it.
                if (TheScript.ReflectionsData.flatPublicStaticMethods.TryGetValue(methodName, out var mi)
                    && mi != null
                    && !parser.UserFuncs.Contains(methodName)
                    && parser.IsVarDeclaredLocally(methodName) == null)
                    targetExpression = CreateQualifiedName($"{mi.DeclaringType}.{mi.Name}");
                else
                {
                    targetExpression = (ExpressionSyntax)Visit(context.primaryExpression());
                    methodName = ExtractMethodName(targetExpression) ?? methodName;
                }

                // Get the argument list
                ArgumentListSyntax argumentList;
                if (accessSuffix.arguments() != null)
                    argumentList = (ArgumentListSyntax)VisitArguments(accessSuffix.arguments());
                else
                    argumentList = SyntaxFactory.ArgumentList();

                if (methodName.Equals("IsSet", StringComparison.InvariantCultureIgnoreCase))
                {
                    var arg = argumentList.Arguments.First().Expression;
                    if (arg is IdentifierNameSyntax identifierName)
                    {
                        var addedName = parser.MaybeAddVariableDeclaration(identifierName.Identifier.Text);
                        if (addedName != null && addedName != identifierName.Identifier.Text)
                        {
                            identifierName = SyntaxFactory.IdentifierName(addedName);
                            argumentList = CreateArgumentList(identifierName);
                        }
                    }
                    else if (arg is InvocationExpressionSyntax ies && CheckInvocationExpressionName(ies, "GetPropertyValue"))
                    {
                        var valueExpr = parser.PushTempVar();

                        var origArgs = ies.ArgumentList.Arguments;

                        // Build the out-temp third argument: `out temp`
                        var outArg = SyntaxFactory.Argument(valueExpr)
                            .WithRefKindKeyword(
                                SyntaxFactory.Token(SyntaxKind.OutKeyword)
                            );
                        parser.PopTempVar();

                        return SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("TryGetPropertyValue"),
                            CreateArgumentList(
                                [outArg, ..origArgs]
                            )
                        );
                    }
                }
                else if (methodName.Equals("StrPtr", StringComparison.InvariantCultureIgnoreCase)
                    && argumentList.Arguments.First().Expression is ExpressionSyntax strVar
                    && strVar is not LiteralExpressionSyntax)
                {
                    argumentList = CreateArgumentList(parser.ConstructVarRef(strVar, false));
                }

				if (accessSuffix.modifier != null)
                {
                    if (targetExpression is not IdentifierNameSyntax)
                        throw new ArgumentException("Optional method and index calls are not supported");

					return SyntaxFactory.ConditionalExpression(
					    SyntaxFactory.BinaryExpression(
						    SyntaxKind.EqualsExpression,
							targetExpression,
						    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
					    ),
					    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
						parser.GenerateFunctionInvocation(targetExpression, argumentList, methodName)
					);
				}

                return parser.GenerateFunctionInvocation(targetExpression, argumentList, methodName);
			}
		}

        private ExpressionSyntax GenerateMemberIndexAccess(PrimaryExpressionContext singleExpression, MemberIndexArgumentsContext memberIndexArguments, bool isOptional = false)
        {
            // Visit the main expression (e.g., c in c[1])
            var targetExpression = (ExpressionSyntax)Visit(singleExpression);

            // Visit the expressionSequence to generate an ArgumentListSyntax
            var exprArgSeqContext = memberIndexArguments.arguments();
            var argumentList = exprArgSeqContext == null 
                ? SyntaxFactory.ArgumentList()
                : (ArgumentListSyntax)Visit(exprArgSeqContext);

            // Prepend the targetExpression as the first argument
            var fullArgumentList = argumentList.WithArguments(
                argumentList.Arguments.Insert(0, SyntaxFactory.Argument(targetExpression))
            );

            // Generate the invocation: Keysharp.Scripting.Script.Index(target, index)
            var indexInvocation = SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "Index"),
                fullArgumentList
            );

            // Handle optional chaining: if '? ' is present
            if (isOptional)
            {
                return SyntaxFactory.ConditionalExpression(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        targetExpression,
                        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                    ),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
                    indexInvocation
                );
            }

            return indexInvocation;
        }

        public override SyntaxNode VisitExpressionStatement([Antlr4.Runtime.Misc.NotNull] ExpressionStatementContext context)
        {
			var sequence = context.expressionSequence();
			if (parser.currentFunc.Name == Keywords.AutoExecSectionName && sequence.ChildCount == 1 && (sequence.expression(0) is FunctionExpressionContext || sequence.expression(0) is FatArrowExpressionContext))
			{
				return (MethodDeclarationSyntax)Visit(sequence.expression(0));
			}
			var sequenceArgList = (ArgumentListSyntax)Visit(sequence);
			ArgumentListSyntax argumentList = CreateArgumentList(sequenceArgList.Arguments.ToArray());

            ExpressionSyntax singleExpression = null;
            if (argumentList.Arguments.Count == 0)
                throw new Error("Expression count can't be 0");

            singleExpression = argumentList.Arguments[0].Expression;

            if (argumentList.Arguments.Count == 1)
            {
                // Validate and convert the expression if necessary
                singleExpression = EnsureValidStatementExpression(singleExpression);

                return SyntaxFactory.ExpressionStatement(singleExpression);
            }
            else
            {
                 // Wrap each expression if needed and create a block
                return SyntaxFactory.Block(argumentList.Arguments
                    .Select(arg =>
                    {
                        var expression = EnsureValidStatementExpression(arg.Expression);
                        return SyntaxFactory.ExpressionStatement(expression);
                    })
                    .ToList());
            }
        }

        private ExpressionSyntax EnsureValidStatementExpression(ExpressionSyntax expression)
        {
            // Check if the expression is valid as a statement
            if (expression is InvocationExpressionSyntax ||
                expression is AssignmentExpressionSyntax ||
                expression is PostfixUnaryExpressionSyntax ||
                expression is PrefixUnaryExpressionSyntax ||
                expression is AwaitExpressionSyntax ||
                expression is ObjectCreationExpressionSyntax)
            {
                return expression; // It's valid, return as-is
            }

            // If not valid, wrap it in a dummy call
            
            return ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                .WithArgumentList(
				    CreateArgumentList(expression)
                );
        }

        public override SyntaxNode VisitParenthesizedExpression([NotNull] ParenthesizedExpressionContext context)
        {
            ArgumentListSyntax argumentList = (ArgumentListSyntax)Visit(context.expressionSequence());
            if (argumentList.Arguments.Count == 1)
                return SyntaxFactory.ParenthesizedExpression(argumentList.Arguments[0].Expression);

            return ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                .WithArgumentList(argumentList);
        }

        public override SyntaxNode VisitExpressionSequence(ExpressionSequenceContext context)
        {
            var arguments = new List<ExpressionSyntax>();

            bool isComma;
            bool lastWasComma = true;
            for (var i = 0; i < context.ChildCount; i++)
            {
                var child = context.GetChild(i);
                if (child is ITerminalNode node)
                {
                    if (node.Symbol.Type == EOL || node.Symbol.Type == WS)
                        continue;
                    isComma = node.Symbol.Type == MainLexer.Comma;
                }
                else
                    isComma = false;

                if (isComma)
                {
                    if (lastWasComma)
                        arguments.Add(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));

                    goto ShouldVisitNextChild;
                }
                SyntaxNode expr = Visit((ExpressionContext)child);
				if (expr is MethodDeclarationSyntax)
					return expr;
				arguments.Add((ExpressionSyntax)expr);

                ShouldVisitNextChild:
                lastWasComma = isComma;
            }

            return CreateArgumentList(arguments);
        }

        /*
        public override SyntaxNode VisitConcatenateExpression([NotNull] ConcatenateExpressionContext context)
        {
            return ConcatenateExpressions(
                new List<ExpressionSyntax> {
                    (ExpressionSyntax)Visit(context.singleExpression()),
                    (ExpressionSyntax)Visit(context.singleExpressionConcatenation())
            });
        }
        */

		public ExpressionSyntax ConcatenateExpressions(List<ExpressionSyntax> expressions)
        {
            ExpressionSyntax finalExpression = expressions[0];
            if (expressions.Count == 1)
                return finalExpression;

            for (var i = 1; i < expressions.Count; i++)
            {
                finalExpression = CreateBinaryOperatorExpression(MainParser.Dot, finalExpression, expressions[i]);
            }
            return finalExpression;
        }

		public override SyntaxNode VisitPreIncrementDecrementExpression([NotNull] PreIncrementDecrementExpressionContext context)
		{
			var expression = (ExpressionSyntax)Visit(context.expression());
			return HandleCompoundAssignment(expression, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1L)), context.op.Type == MainLexer.PlusPlus ? "+=" : "-=");
		}

		public override SyntaxNode VisitPreIncrementDecrementExpressionDuplicate([NotNull] PreIncrementDecrementExpressionDuplicateContext context)
		{
			var expression = (ExpressionSyntax)Visit(context.singleExpression());
			return HandleCompoundAssignment(expression, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1L)), context.op.Type == MainLexer.PlusPlus ? "+=" : "-=");
		}

		public override SyntaxNode VisitPostIncrementDecrementExpression([NotNull] PostIncrementDecrementExpressionContext context)
		{
			var expression = (ExpressionSyntax)Visit(context.expression());
			return HandleCompoundAssignment(expression, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1L)), context.op.Type == MainLexer.PlusPlus ? "+=" : "-=", isPostFix: true);
		}

		public override SyntaxNode VisitPostIncrementDecrementExpressionDuplicate([NotNull] PostIncrementDecrementExpressionDuplicateContext context)
		{
			var expression = (ExpressionSyntax)Visit(context.singleExpression());
			return HandleCompoundAssignment(expression, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1L)), context.op.Type == MainLexer.PlusPlus ? "+=" : "-=", isPostFix: true);
		}

		public override SyntaxNode VisitPowerExpression([NotNull] PowerExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitPowerExpressionDuplicate([NotNull] PowerExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitUnaryExpression([NotNull] UnaryExpressionContext context)
		{
			return HandleUnaryExpressionVisit(context, context.op.Type);
		}

		public override SyntaxNode VisitUnaryExpressionDuplicate([NotNull] UnaryExpressionDuplicateContext context)
		{
			return HandleUnaryExpressionVisit(context, context.op.Type);
		}

		public SyntaxNode HandleBinaryExpressionVisit([NotNull] ParserRuleContext left, ParserRuleContext right, int op)
        {
            return CreateBinaryOperatorExpression(op, (ExpressionSyntax)Visit(left), (ExpressionSyntax)Visit(right));
		}

        public SyntaxNode HandlePureBinaryExpressionVisit([NotNull] ParserRuleContext context)
        {
            if (context.GetChild(1) is ITerminalNode node && node != null)
                return SyntaxFactory.BinaryExpression(
                    pureBinaryOperators[node.Symbol.Type],
                    (ExpressionSyntax)Visit(context.GetChild(0)),
                    (ExpressionSyntax)Visit(context.GetChild(2)));
            throw new ValueError("Invalid operand: " + context.GetChild(1).GetText());
        }

        public override SyntaxNode VisitMultiplicativeExpression([NotNull] MultiplicativeExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitMultiplicativeExpressionDuplicate([NotNull] MultiplicativeExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitAdditiveExpression([NotNull] AdditiveExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitAdditiveExpressionDuplicate([NotNull] AdditiveExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitBitShiftExpression([NotNull] BitShiftExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitBitShiftExpressionDuplicate([NotNull] BitShiftExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitBitAndExpression([NotNull] BitAndExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitBitAndExpressionDuplicate([NotNull] BitAndExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitBitXOrExpression([NotNull] BitXOrExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitBitXOrExpressionDuplicate([NotNull] BitXOrExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitBitOrExpression([NotNull] BitOrExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitBitOrExpressionDuplicate([NotNull] BitOrExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitConcatenateExpression([NotNull] ConcatenateExpressionContext context)
        {
            var arguments = new List<ExpressionSyntax> {
                (ExpressionSyntax)Visit(context.left),
                (ExpressionSyntax)Visit(context.right)
            };

            return ConcatenateExpressions(arguments);
        }

		public override SyntaxNode VisitConcatenateExpressionDuplicate([NotNull] ConcatenateExpressionDuplicateContext context)
		{
			var arguments = new List<ExpressionSyntax> {
				(ExpressionSyntax)Visit(context.left),
				(ExpressionSyntax)Visit(context.right)
			};

			return ConcatenateExpressions(arguments);
		}

		public override SyntaxNode VisitRegExMatchExpression([NotNull] RegExMatchExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitRegExMatchExpressionDuplicate([NotNull] RegExMatchExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitRelationalExpression([NotNull] RelationalExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitRelationalExpressionDuplicate([NotNull] RelationalExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitEqualityExpression([NotNull] EqualityExpressionContext context)
        {
            return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitEqualityExpressionDuplicate([NotNull] EqualityExpressionDuplicateContext context)
		{
			return HandleBinaryExpressionVisit(context.left, context.right, context.op.Type);
		}

		public override SyntaxNode VisitContainExpression([NotNull] ContainExpressionContext context)
        {
            return HandleContainExpressionVisit(context.left, context.right, context.op.Type);
        }

		public override SyntaxNode VisitContainExpressionDuplicate([NotNull] ContainExpressionDuplicateContext context)
		{
			return HandleContainExpressionVisit(context.left, context.right, context.op.Type);
		}

		private SyntaxNode HandleContainExpressionVisit(ParserRuleContext left, ParserRuleContext right, int type)
        {
			switch (type)
			{
				case MainLexer.Is:
					var leftExpression = (ExpressionSyntax)Visit(left);
					// Ensure the classExpression is treated as a string (e.g., "KeysharpObject")
					var classAsRawString = right.GetText().Trim();
					if (classAsRawString.IndexOfAny(Spaces) != -1)
						throw new Error("Is expression comparison word cannot contain spaces: " + classAsRawString);

					if (Keywords.TypeNameAliases.TryGetValue(classAsRawString, out var alias))
						classAsRawString = alias;

					if (classAsRawString == "unset" || classAsRawString == "null")
					{
						var nullComparison = SyntaxFactory.BinaryExpression(
							SyntaxKind.EqualsExpression,
							leftExpression,
							SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
						);
						return nullComparison;
					}

					var classAsString = SyntaxFactory.LiteralExpression(
						SyntaxKind.StringLiteralExpression,
						SyntaxFactory.Literal(classAsRawString) // Convert class to its string representation
					);

					return CreateBinaryOperatorExpression(
						MainLexer.Is,
						leftExpression,
						classAsString);
			}
			throw new NotImplementedException();
		}

        private SyntaxNode HandleLogicalAndExpression(ExpressionSyntax left, ExpressionSyntax right)
        {
            var leftIf = ((InvocationExpressionSyntax)InternalMethods.IfTest)
                .WithArgumentList(CreateArgumentList(left));

            var rightIf = ((InvocationExpressionSyntax)InternalMethods.IfTest)
                .WithArgumentList(CreateArgumentList(right));

            // Create If(left) && If(right)
            var andExpression = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                leftIf,
                rightIf
            );

            // Call .ParseObject()
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParenthesizedExpression(andExpression),
                    SyntaxFactory.IdentifierName("ParseObject")
                )
            );

			/*
            return SyntaxFactory.ConditionalExpression(
                SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Scripting.Script", "ForceBool"),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(left))
                    )
                ),
                right,  // If left is truthy, return right
                left    // If left is falsy, return left
            );
            */
		}
		private SyntaxNode HandleLogicalOrExpression(ExpressionSyntax left, ExpressionSyntax right)
        {
            var leftIf = ((InvocationExpressionSyntax)InternalMethods.IfTest)
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(left))));

            var rightIf = ((InvocationExpressionSyntax)InternalMethods.IfTest)
                .WithArgumentList(CreateArgumentList(right));

            // Create If(left) && If(right)
            var orExpression = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalOrExpression,
                leftIf,
                rightIf
            );

            // Call .ParseObject()
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParenthesizedExpression(orExpression),
                    SyntaxFactory.IdentifierName("ParseObject")
                )
            );
			/*
            return SyntaxFactory.ConditionalExpression(
                SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Scripting.Script", "ForceBool"),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(left))
                    )
                ),
                left,  // If left is truthy, return left
                right    // If left is falsy, return right
            );
            */
		}
		public override SyntaxNode VisitLogicalAndExpression([NotNull] LogicalAndExpressionContext context)
        {
            return HandleLogicalAndExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
        }
        public override SyntaxNode VisitLogicalAndExpressionDuplicate([NotNull] LogicalAndExpressionDuplicateContext context)
        {
            return HandleLogicalAndExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
        }

        public override SyntaxNode VisitLogicalOrExpression([NotNull] LogicalOrExpressionContext context)
        {
            return HandleLogicalOrExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
        }
        public override SyntaxNode VisitLogicalOrExpressionDuplicate([NotNull] LogicalOrExpressionDuplicateContext context)
        {
            return HandleLogicalOrExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
        }

        public SyntaxNode HandleCoalesceExpression(ExpressionSyntax left, ExpressionSyntax right)
        {
            if (right is AssignmentExpressionSyntax)
                right = SyntaxFactory.ParenthesizedExpression(right);
            if (left is InvocationExpressionSyntax ies && CheckInvocationExpressionName(ies, "GetPropertyValue"))
            {
				var valueExpr = parser.PushTempVar();

				var origArgs = ies.ArgumentList.Arguments;

				// Build the out-temp third argument: `out temp`
				var outArg = SyntaxFactory.Argument(valueExpr)
					.WithRefKindKeyword(
						SyntaxFactory.Token(SyntaxKind.OutKeyword)
					);

				// Now build: TryGetPropertyValue(obj, key, out temp)
				var tryCall = SyntaxFactory.InvocationExpression(
					SyntaxFactory.IdentifierName("TryGetPropertyValue"),
					CreateArgumentList(
				        [outArg,
                        ..origArgs]
					)
				);

				// Build the conditional: TryGetPropertyValue(...) ? temp0 : right
				var conditional = SyntaxFactory.ConditionalExpression(
					tryCall,
					valueExpr,
					right
				);

				parser.PopTempVar();

                return SyntaxFactory.ParenthesizedExpression(conditional);
			}
			return SyntaxFactory.BinaryExpression(
				SyntaxKind.CoalesceExpression,
				left,
				right
			);
		}

        public override SyntaxNode VisitCoalesceExpression([NotNull] CoalesceExpressionContext context)
        {
            return HandleCoalesceExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
        }
        public override SyntaxNode VisitCoalesceExpressionDuplicate([NotNull] CoalesceExpressionDuplicateContext context)
        {
			return HandleCoalesceExpression((ExpressionSyntax)Visit(context.left), (ExpressionSyntax)Visit(context.right));
		}

        public SyntaxNode HandleUnaryExpressionVisit([NotNull] ParserRuleContext context, int type)
        {
            var arguments = new List<ExpressionSyntax>() {
                CreateQualifiedName("Keysharp.Scripting.Script.Operator." + unaryOperators[type]),
                (ExpressionSyntax)Visit(context.GetChild(context.ChildCount - 1))
            };
            var argumentList = CreateArgumentList(arguments);
            return SyntaxFactory.InvocationExpression(ScriptOperateUnaryName, argumentList);
        }

		public override SyntaxNode VisitVerbalNotExpression([NotNull] VerbalNotExpressionContext context)
        {
            return HandleUnaryExpressionVisit(context, context.op.Type);
        }

		public override SyntaxNode VisitVerbalNotExpressionDuplicate([NotNull] VerbalNotExpressionDuplicateContext context)
		{
			return HandleUnaryExpressionVisit(context, context.op.Type);
		}

		public override SyntaxNode VisitArrayLiteralExpression([NotNull] ArrayLiteralExpressionContext context)
        {
            return base.VisitArrayLiteralExpression(context);
        }

        private SyntaxNode HandleAssignmentExpression(IParseTree left, IParseTree right, string assignmentOperator)
        {
            var leftExpression = (ExpressionSyntax)Visit(left);
            var rightExpression = (ExpressionSyntax)Visit(right);

            return HandleAssignment(leftExpression, rightExpression, assignmentOperator);
        }
        public override SyntaxNode VisitAssignmentExpression([NotNull] AssignmentExpressionContext context)
        {
            return HandleAssignmentExpression(context.left, context.right, context.assignmentOperator().GetText());
        }

        public override SyntaxNode VisitAssignmentExpressionDuplicate([NotNull] AssignmentExpressionDuplicateContext context)
        {
            return HandleAssignmentExpression(context.left, context.right, context.assignmentOperator().GetText());
        }

        private ExpressionSyntax HandleAssignment(ExpressionSyntax leftExpression, ExpressionSyntax rightExpression, string assignmentOperator)
        {
            
            // Handle static member assignment
            if (leftExpression is InvocationExpressionSyntax invocationExpression &&
                IsStaticMemberAccessInvocation(invocationExpression))
            {
                return HandleStaticMemberAssignment(invocationExpression, rightExpression, assignmentOperator);
            }

            // Handle ElementAccessExpression for array or indexed assignments
            else if (leftExpression is ElementAccessExpressionSyntax elementAccess)
            {
                return HandleElementAccessAssignment(elementAccess, rightExpression, assignmentOperator);
            }

            // Handle MemberAccessExpression for property or field assignments
            else if (leftExpression is MemberAccessExpressionSyntax memberAccess)
            {
                return HandleMemberAccessAssignment(memberAccess, rightExpression, assignmentOperator);
            }

            // Handle other cases (e.g., property assignments, index access)
            if (assignmentOperator == ":=" || assignmentOperator == "??=")
            {
                var assignmentKind = (assignmentOperator == ":=") ? SyntaxKind.SimpleAssignmentExpression : SyntaxKind.CoalesceAssignmentExpression;
				if ((leftExpression is ObjectCreationExpressionSyntax objectExpression)
                && objectExpression.Type is IdentifierNameSyntax objectName
                && objectName.Identifier.Text == "VarRef")
                {
                    var varRefExpression = leftExpression;
                    leftExpression = parser.PushTempVar();
                    var result = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                        .WithArgumentList(
							CreateArgumentList(
								SyntaxFactory.AssignmentExpression(
                                    assignmentKind,
                                    leftExpression,
									varRefExpression
                                ),
                                ((InvocationExpressionSyntax)InternalMethods.SetPropertyValue)
                                    .WithArgumentList(
										CreateArgumentList(
                                            leftExpression,
                                            SyntaxFactory.LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                SyntaxFactory.Literal("__Value")
                                            ),
                                            rightExpression
                                        )
                                    )
                                ,
								leftExpression
							)
                        );
                    parser.PopTempVar();
                    return result;
                }
                if (leftExpression is InvocationExpressionSyntax getPropertyInvocation &&
                    CheckInvocationExpressionName(getPropertyInvocation, "GetPropertyValue"))
                {
                    return HandlePropertyAssignment(getPropertyInvocation, rightExpression);
                }
                else if (leftExpression is InvocationExpressionSyntax indexAccessInvocation &&
					CheckInvocationExpressionName(indexAccessInvocation, "Index"))
                {
                    return HandleIndexAssignment(indexAccessInvocation, rightExpression);
                }
                else if (leftExpression is IdentifierNameSyntax identifierNameSyntax)
                {
                    var addedName = parser.MaybeAddVariableDeclaration(identifierNameSyntax.Identifier.Text);
                    if (addedName != null && addedName != identifierNameSyntax.Identifier.Text)
                        identifierNameSyntax = SyntaxFactory.IdentifierName(addedName);
                    leftExpression = identifierNameSyntax;
                    return SyntaxFactory.AssignmentExpression(
                        assignmentKind,
                        leftExpression,
						rightExpression
                    );
                }
                else
                {
                    throw new Error("Unknown left expression type");
                }
            }

            // Handle compound assignments
            return HandleCompoundAssignment(leftExpression, rightExpression, assignmentOperator);
        }

        private ExpressionSyntax HandleStaticMemberAssignment(
            InvocationExpressionSyntax staticMemberInvocation,
            ExpressionSyntax rightExpression,
            string assignmentOperator)
        {
            if (assignmentOperator == "??=")
            {
                var getStaticMemberValue = staticMemberInvocation;
                var setStaticMemberValue = CreateSetStaticMemberInvocation(getStaticMemberValue, rightExpression);

                // Return: left ?? (SetStaticMemberValueT<typeName>(member, right))
                return SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    getStaticMemberValue,
                    setStaticMemberValue
                );
            }

            if (assignmentOperator == ":=")
            {
                return CreateSetStaticMemberInvocation(staticMemberInvocation, rightExpression);
            }

            // Handle compound assignments (e.g., "+=", "-=")
            string binaryOperator = MapAssignmentOperatorToBinaryOperator(assignmentOperator);
            var binaryOperation = CreateBinaryOperatorExpression(
                GetOperatorToken(binaryOperator),
                staticMemberInvocation,
                rightExpression
            );

            return CreateSetStaticMemberInvocation(staticMemberInvocation, binaryOperation);
        }


        private ExpressionSyntax HandleElementAccessAssignment(
            ElementAccessExpressionSyntax elementAccess,
            ExpressionSyntax rightExpression,
            string assignmentOperator)
        {
            if (assignmentOperator == ":=")
            {
                return SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Scripting.Script", "SetObject"),
					CreateArgumentList(
                        elementAccess.Expression,
                        elementAccess.ArgumentList.Arguments,
						rightExpression
					)
                );
            }

            if (assignmentOperator == "??=")
            {
                var getIndexValue = CreateElementAccessGetterInvocation(elementAccess);
                var setIndexValue = (ExpressionSyntax)HandleElementAccessAssignment(elementAccess, rightExpression, ":=");

                // Return: left ?? (SetObject(base, index, right))
                return SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    getIndexValue,
                    setIndexValue
                );
            }

            // Handle compound assignments
            string binaryOperator = MapAssignmentOperatorToBinaryOperator(assignmentOperator);
            var binaryOperation = CreateBinaryOperatorExpression(
                GetOperatorToken(binaryOperator),
                CreateElementAccessGetterInvocation(elementAccess),
                rightExpression
            );

            return HandleElementAccessAssignment(elementAccess, binaryOperation, ":=");
        }

        private ExpressionSyntax HandleMemberAccessAssignment(
            MemberAccessExpressionSyntax memberAccess,
            ExpressionSyntax rightExpression,
            string assignmentOperator)
        {
            if (assignmentOperator == ":=")
            {
                return SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Scripting.Script", "SetPropertyValue"),
					CreateArgumentList(
                        memberAccess.Expression,
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(memberAccess.Name.Identifier.Text)
                        ),
                        rightExpression
                    )
                );
            }

            if (assignmentOperator == "??=")
            {
                var getPropertyValue = SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Scripting.Script", "GetPropertyValue"),
					CreateArgumentList(
                        memberAccess.Expression,
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(memberAccess.Name.Identifier.Text)
                        )
                    )
                );

                var setPropertyValue = (ExpressionSyntax)HandleMemberAccessAssignment(memberAccess, rightExpression, ":=");

                // Return: left ?? (SetPropertyValue(base, member, right))
                return SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    getPropertyValue,
                    setPropertyValue
                );
            }

            // Handle compound assignments
            string binaryOperator = MapAssignmentOperatorToBinaryOperator(assignmentOperator);
            var binaryOperation = CreateBinaryOperatorExpression(
                GetOperatorToken(binaryOperator),
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Scripting.Script", "GetPropertyValue"),
					CreateArgumentList(
                        memberAccess.Expression,
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(memberAccess.Name.Identifier.Text)
                        )
                    )
                ),
                rightExpression
            );

            return HandleMemberAccessAssignment(memberAccess, binaryOperation, ":=");
        }

        private ExpressionSyntax CreateElementAccessGetterInvocation(ElementAccessExpressionSyntax elementAccess)
        {
            var baseExpression = elementAccess.Expression;
            var indexExpression = elementAccess.ArgumentList.Arguments.First().Expression;

            return SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "Index"),
				CreateArgumentList(
                    baseExpression,
                    indexExpression
                )
            );
        }

        private ExpressionSyntax HandlePropertyAssignment(InvocationExpressionSyntax getPropertyInvocation, ExpressionSyntax rightExpression)
        {
            var args = getPropertyInvocation.ArgumentList.Arguments;
            if (args[^1].Expression is CollectionExpressionSyntax ces)
            {
				var collectionElements = new List<CollectionElementSyntax>();
				foreach (var node in ces.Elements)
					collectionElements.Add(node);
                collectionElements.Add(SyntaxFactory.ExpressionElement(rightExpression));
				var collectionExpression = SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList(collectionElements));

				return SyntaxFactory.InvocationExpression(
	                CreateMemberAccess("Keysharp.Scripting.Script", "SetPropertyValue"),
	                CreateArgumentList(
		                args.SkipLast(1),
						collectionExpression
					)
                );
			}

			return SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "SetPropertyValue"),
				CreateArgumentList(
					args,
                    rightExpression
                )
            );
        }

        private ExpressionSyntax HandleIndexAssignment(InvocationExpressionSyntax indexAccessInvocation, ExpressionSyntax rightExpression)
        {
            return SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "SetObject"),
				CreateArgumentList(
					indexAccessInvocation.ArgumentList.Arguments,
					rightExpression
				)
            );
        }

        private ExpressionSyntax HandleCompoundAssignment(ExpressionSyntax leftExpression, ExpressionSyntax rightExpression, string assignmentOperator, bool isPostFix = false)
        {
            string binaryOperator = MapAssignmentOperatorToBinaryOperator(assignmentOperator);
			var operatorToken = GetOperatorToken(binaryOperator);

			InvocationExpressionSyntax binaryOperation;
            InvocationExpressionSyntax result = null;

            while (leftExpression is ParenthesizedExpressionSyntax pes)
                leftExpression = pes.Expression;

			// If the left side is itself a simple assignment like "a = expr",
			// rewrite (a = expr) <op>= rhs  into  a = (expr <op> rhs)
			if (leftExpression is AssignmentExpressionSyntax simpleAssign && simpleAssign.Kind() == SyntaxKind.SimpleAssignmentExpression)
			{
				// decompose "a = expr"
				var finalTarget = simpleAssign.Left;        // "a"
				var initialValue = simpleAssign.Right;      // "expr" (1 in your example)

				// Build "expr <op> rightExpression"
				// i.e. CreateBinaryOperatorExpression(operatorToken, initialValue, rightExpression)
				var combinedOperation = CreateBinaryOperatorExpression(
					operatorToken,
					initialValue,
					rightExpression
				);

				if (isPostFix)
				{
					// Postfix: (a = expr) <op>= rhs
					//
					// Semantics should match:
					// temp = (a = expr)
					// a = (temp <op> rhs)
					// return temp

					var tempVar = parser.PushTempVar();

					// temp = expr
					var assignTemp = SyntaxFactory.AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						tempVar,
						PredefinedKeywords.EqualsToken,
						initialValue
					);

					// a = temp <op> rhs
					var tempOp = CreateBinaryOperatorExpression(
						operatorToken,
						tempVar,
						rightExpression
					);
					var assignBack = SyntaxFactory.AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						finalTarget,
						PredefinedKeywords.EqualsToken,
						tempOp
					);

					// MultiStatement(temp = expr, a = temp <op> rhs, temp)
					var multi = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
						.WithArgumentList(
							CreateArgumentList(
								assignTemp,
								assignBack,
								tempVar
							)
						);

					parser.PopTempVar();
					return multi;
				}

				// Non-postfix:
				// a = (initialValue <op> rightExpression)
				return SyntaxFactory.AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					finalTarget,
					PredefinedKeywords.EqualsToken,
					combinedOperation
				);
			}
			// In the case of member or index access, buffer the base and member and then get+set to avoid multiple evaluations
			if (!(leftExpression is IdentifierNameSyntax))
            {
                var baseTemp = parser.PushTempVar();
                var memberTemp = parser.PushTempVar();
                IdentifierNameSyntax resultTemp = null;
                IdentifierNameSyntax varRefTemp = null;
                ExpressionSyntax assignmentExpression = null;
                ExpressionSyntax baseExpression = null;
                ExpressionSyntax memberExpression = null;
                ExpressionSyntax varRefExpression = null;

                if ((leftExpression is ObjectCreationExpressionSyntax objectExpression)
                    && objectExpression.Type is IdentifierNameSyntax objectName
                    && objectName.Identifier.Text == "VarRef")
                {
                    varRefTemp = parser.PushTempVar();
                    varRefExpression = leftExpression;
                    leftExpression = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParenthesizedExpression(
                                SyntaxFactory.CastExpression(
                                    SyntaxFactory.IdentifierName("VarRef"),
                                    varRefTemp
                                )
                            ),
                            SyntaxFactory.IdentifierName("__Value")
                        );
                }
                if (leftExpression is InvocationExpressionSyntax getPropertyInvocation 
                    && CheckInvocationExpressionName(getPropertyInvocation, "GetPropertyValue"))
                {
                    baseExpression = getPropertyInvocation.ArgumentList.Arguments[0].Expression;
                    memberExpression = getPropertyInvocation.ArgumentList.Arguments[1].Expression;

                    var propValue = ((InvocationExpressionSyntax)InternalMethods.GetPropertyValue)
                        .WithArgumentList(
							CreateArgumentList(
                                baseTemp,
                                memberTemp
                            )
                        );

                    if (isPostFix)
                    {
                        resultTemp = parser.PushTempVar();
                        binaryOperation = CreateBinaryOperatorExpression(
							operatorToken,
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                resultTemp,
                                PredefinedKeywords.EqualsToken,
                                propValue
                            ),
                            rightExpression
                        );
                        parser.PopTempVar();
                    }
                    else
                    {
                        binaryOperation = CreateBinaryOperatorExpression(
							operatorToken,
                            propValue,
                            rightExpression
                        );
                    }

                    assignmentExpression = ((InvocationExpressionSyntax)InternalMethods.SetPropertyValue)
                        .WithArgumentList(
							CreateArgumentList(
                                baseTemp,
                                memberTemp,
                                binaryOperation
                            )
                    );
                }
                else if (leftExpression is InvocationExpressionSyntax indexAccessInvocation 
                    && CheckInvocationExpressionName(indexAccessInvocation, "Index"))
                {
                    baseExpression = indexAccessInvocation.ArgumentList.Arguments[0].Expression;
                    memberExpression = indexAccessInvocation.ArgumentList.Arguments[1].Expression;

                    var propValue = ((InvocationExpressionSyntax)InternalMethods.Index)
                        .WithArgumentList(
							CreateArgumentList(
								baseTemp,
                                memberTemp
                            )
                        );

                    if (isPostFix)
                    {
                        resultTemp = parser.PushTempVar();
                        binaryOperation = CreateBinaryOperatorExpression(
							operatorToken,
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                resultTemp,
                                PredefinedKeywords.EqualsToken,
                                propValue),
                            rightExpression
                        );
                        parser.PopTempVar();
                    } else
                    {
                        binaryOperation = CreateBinaryOperatorExpression(
                            operatorToken,
                            propValue,
                            rightExpression
                        );
                    }

                    assignmentExpression = ((InvocationExpressionSyntax)InternalMethods.SetObject)
                        .WithArgumentList(
							CreateArgumentList(
                                baseTemp,
                                memberTemp,
								binaryOperation
							)
                        );
                }
                else if (leftExpression is ElementAccessExpressionSyntax elementAccess)
                {
                    baseExpression = elementAccess.Expression;
                    var indices = elementAccess.ArgumentList.Arguments;

                    resultTemp = parser.PushTempVar();

                    // Assign each index to a temporary variable to avoid repeated evaluation
                    var indexTemps = new List<IdentifierNameSyntax>();
                    foreach (var _ in indices)
                        indexTemps.Add(parser.PushTempVar());
                    // Create assignment expressions for each index and tempIndex
                    var indexTempAssigns = indices.Zip(indexTemps, (indexArg, tempIndex) =>
                        SyntaxFactory.Argument(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                tempIndex, // The temporary variable
                                PredefinedKeywords.EqualsToken,
                                indexArg.Expression // The original index expression
                            )
                        )
                    ).ToList();

                    // Creates an access in the form Keysharp.Scripting.Script.Vars[_ks_temp2 = x]
                    // This will get assigned to baseTemp: _ks_temp1 = Keysharp.Scripting.Script.Vars[_ks_temp2 = x]
                    baseExpression = elementAccess
                        .WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(indexTempAssigns)));

                    // Keysharp.Scripting.Script.Vars[_ks_temp2]
                    memberExpression = elementAccess
                        .WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(indexTemps.Select(index => SyntaxFactory.Argument(index)).ToList())));

                    // Keysharp.Scripting.Script.Operate(Keysharp.Scripting.Script.Operator.Add, _ks_temp1, rightExpression)
                    binaryOperation = CreateBinaryOperatorExpression(
						operatorToken,
                        resultTemp,
                        rightExpression
                    );

                    assignmentExpression = SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        memberExpression,
                        PredefinedKeywords.EqualsToken,
                        binaryOperation
                    );

                    var argumentList = new List<ArgumentSyntax> {
                        SyntaxFactory.Argument(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                resultTemp,
                                PredefinedKeywords.EqualsToken,
                                baseExpression
                            )
                        ),
                        SyntaxFactory.Argument(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                memberExpression,
                                PredefinedKeywords.EqualsToken,
                                binaryOperation
                            )
                        )
                    };

                    result = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                    .WithArgumentList(
						CreateArgumentList(argumentList)
                    );

                    // Clean up pushed temporaries for indices
                    foreach (var temp in indexTemps)
                    {
                        parser.PopTempVar();
                    }

                    parser.PopTempVar(); // For the resultTemp variable
                }
                else
                    throw new Error("Unknown compound assignment left operand");

                parser.PopTempVar();
                parser.PopTempVar();

                if (result == null)
                result = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                    .WithArgumentList(
						CreateArgumentList(
							SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                baseTemp,
                                PredefinedKeywords.EqualsToken,
                                baseExpression
                            ),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                memberTemp,
                                PredefinedKeywords.EqualsToken,
                                memberExpression
                            ),
                            assignmentExpression
                        )
                    );

                if (varRefTemp != null)
                {
                    result = result.WithArgumentList(
						CreateArgumentList(
							SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                varRefTemp, // The temporary variable
                                varRefExpression // The VarRef expression
                            ),
                            result.ArgumentList.Arguments,
                            varRefTemp
                        )
                    );
                    parser.PopTempVar();
                }

                if (isPostFix && resultTemp != null)
                {
                    return result.WithArgumentList(
						CreateArgumentList(
                            result.ArgumentList.Arguments,
                            resultTemp
                        )
                    );
                }

                return result;
            }

            // If operator is .= then make sure the left-side operand is declared
            if (operatorToken == MainParser.Dot && leftExpression is IdentifierNameSyntax identifier)
            {
                parser.MaybeAddVariableDeclaration(identifier.Identifier.Text);
            }

			binaryOperation = CreateBinaryOperatorExpression(
				operatorToken,
                leftExpression,
                rightExpression
            );

            if (isPostFix)
            {
                var tempVar = parser.PushTempVar(); // Create a temporary variable

                // Create a MultiStatement:
                // temp = x, x = x + 1, temp
                result = ((InvocationExpressionSyntax)InternalMethods.MultiStatement)
                    .WithArgumentList(
						CreateArgumentList(
							SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                tempVar,
                                PredefinedKeywords.EqualsToken,
                                leftExpression
                            ),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                leftExpression,
                                PredefinedKeywords.EqualsToken,
                                binaryOperation
                            ),
                            tempVar
                        )
                    );
                parser.PopTempVar();
                return result;
            }

            return SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                leftExpression,
                PredefinedKeywords.EqualsToken,
                binaryOperation
            );
        }

        private bool IsStaticMemberAccessInvocation(InvocationExpressionSyntax invocation)
        {
            return CheckInvocationExpressionName(invocation, "GetStaticMemberValueT", false);
        }

        private InvocationExpressionSyntax CreateSetStaticMemberInvocation(
            InvocationExpressionSyntax getStaticMemberInvocation,
            ExpressionSyntax newValue)
        {
            if (getStaticMemberInvocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name is GenericNameSyntax genericName)
            {
                // Extract the type argument (e.g., `typeName` in `GetStaticMemberValueT<typeName>`)
                var typeArgument = genericName.TypeArgumentList.Arguments.First();

                // Extract the member argument (e.g., `member`)
                var memberArgument = getStaticMemberInvocation.ArgumentList.Arguments.First();

                // Create SetStaticMemberValueT<typeName>(member, newValue)
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.GenericName("SetStaticMemberValueT")
                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { typeArgument }))),
                    CreateArgumentList(
                        memberArgument,
                        newValue
                    )
                );
            }

            throw new InvalidOperationException("Invalid static member access invocation");
        }

        // Helper function to map assignment operators to binary operators
        private string MapAssignmentOperatorToBinaryOperator(string assignmentOperator)
        {
            return assignmentOperator switch
            {
                "+=" => "Add",
                "-=" => "Minus",
                "*=" => "Multiply",
                "/=" => "Divide",
                "%=" => "Modulus",
                "//=" => "FloorDivide",
                ".=" => "Concat",
                "|=" => "BitwiseOr",
                "&=" => "BitwiseAnd",
                "^=" => "BitwiseXor",
                "<<=" => "BitShiftLeft",
                ">>=" => "BitShiftRight",
                ">>>=" => "LogicalBitShiftRight",
                "**=" => "Power",
                "??=" => "Coalesce",
                _ => throw new InvalidOperationException($"Unsupported assignment operator: {assignmentOperator}")
            };
        }

        // Helper function to map binary operator names to tokens
        private int GetOperatorToken(string binaryOperator)
        {
            return binaryOperators.FirstOrDefault(kvp => kvp.Value == binaryOperator).Key;
        }

        private InvocationExpressionSyntax GenerateMemberDotAccess(PrimaryExpressionContext baseIdentifier, MemberIdentifierContext memberIdentifier, MemberIndexArgumentsContext propertyIndexArguments)
        {
            // Visit the base expression (e.g., `arr` in `arr.Length`)
            ExpressionSyntax baseExpression;
            baseExpression = (ExpressionSyntax)Visit(baseIdentifier);

            // Determine the property or method being accessed
            ExpressionSyntax memberExpression = (ExpressionSyntax)Visit(memberIdentifier);

            // Simple identifier should be converted to string literal
            if (memberExpression is IdentifierNameSyntax memberIdentifierName)
            {
                memberExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(memberIdentifierName.Identifier.Text));
            }
            // Keysharp.Scripting.Script.Vars[expression] should extract expression
            else if (memberExpression is ElementAccessExpressionSyntax memberElementAccess)
            {
                memberExpression = memberElementAccess.ArgumentList.Arguments.FirstOrDefault().Expression;
            }
            else
                throw new Error("Invalid member dot access expression member");

            if (baseExpression is IdentifierNameSyntax identifierName)
            {
                var name = parser.NormalizeIdentifier(identifierName.Identifier.Text);
                if ((Script.TheScript.ReflectionsData.stringToTypes.ContainsKey(name) || name.Equals(Keywords.MainClassName, StringComparison.OrdinalIgnoreCase))
                    && parser.IsVarDeclaredGlobally(name) == null && parser.IsVarDeclaredLocally(name) == null && parser.IsVarDeclaredInClass(parser.currentClass, name) == null)
                {
                    //name = Reflections.stringToTypes.SingleOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
                    // Generate Keysharp.Scripting.Script.GetStaticMemberValueT<baseExpression>(memberExpression)
                    if (name.Equals(Keywords.MainClassName, StringComparison.OrdinalIgnoreCase))
                        return SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                CreateQualifiedName("Keysharp.Scripting.Script"),
                                SyntaxFactory.GenericName("GetStaticMemberValueT")
                                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                                        SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.IdentifierName(Keywords.MainClassName))
                                    ))
                            ),
                                CreateArgumentList(
                                    memberExpression,
                                    SyntaxFactory.IdentifierName(name)
                                )
                            );
                    else
                        baseExpression = SyntaxFactory.IdentifierName(name);
                }
                else
                    parser.MaybeAddGlobalFuncObjVariable(identifierName.Identifier.Text);
            }

            ArgumentListSyntax memberIndexArgList = SyntaxFactory.ArgumentList();

			if (propertyIndexArguments != null)
            {
				// Visit the expressionSequence to generate an ArgumentListSyntax
                if (propertyIndexArguments.arguments() != null)
				    memberIndexArgList = (ArgumentListSyntax)Visit(propertyIndexArguments.arguments());
                else
                {
					return ((InvocationExpressionSyntax)InternalMethods.GetPropertyValue)
                    .WithArgumentList(
						CreateArgumentList(
							((InvocationExpressionSyntax)InternalMethods.GetPropertyValue)
						    .WithArgumentList(
								CreateArgumentList(
								    baseExpression,
								    memberExpression
							    )
						    ),
						    SyntaxFactory.LiteralExpression(
							    SyntaxKind.StringLiteralExpression,
							    SyntaxFactory.Literal("__Item")
						    )
						)
					);
				}
			}

            // Generate the call to Keysharp.Scripting.Script.GetPropertyValue(base, member)
            return SyntaxFactory.InvocationExpression(
                CreateMemberAccess("Keysharp.Scripting.Script", "GetPropertyValue"),
                CreateArgumentList(
                    baseExpression,
                    memberExpression,
                    memberIndexArgList.Arguments
                )
            );
        }

        public override SyntaxNode VisitObjectLiteralExpression([NotNull] ObjectLiteralExpressionContext context)
        {
            return base.VisitObjectLiteralExpression(context);
        }
    }
}
