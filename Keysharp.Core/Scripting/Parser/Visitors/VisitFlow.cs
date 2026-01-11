using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Antlr4.Runtime.Misc;
using static Keysharp.Scripting.Parser;
using static MainParser;
using Microsoft.CodeAnalysis;
using System.Drawing.Imaging;
using Antlr4.Runtime;
using System.IO;
using System.Collections;
using System.Xml.Linq;
using System.Data.Common;
using System.Reflection;
using Antlr4.Runtime.Tree;

namespace Keysharp.Scripting
{
    internal partial class VisitMain : MainParserBaseVisitor<SyntaxNode>
    {
        private SyntaxNode VisitLoopGeneric(
            ExpressionSyntax loopExpression,
            SyntaxNode loopBodyNode,
            ExpressionSyntax untilCondition,
            SyntaxNode elseNode,
            string loopType,
            string loopEnumeratorName,
            string enumeratorType,
            string enumeratorMethodName,
            List<string> backupVariableNames,
            params ExpressionSyntax[] enumeratorArguments)
        {
            // Generate the enumerator initialization
            var loopFunction = SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Core.Loops", enumeratorMethodName),
				CreateArgumentList(enumeratorArguments)
            );
            var enumeratorVariable = SyntaxFactory.IdentifierName(loopEnumeratorName);
            var enumeratorDeclaration = SyntaxFactory.VariableDeclaration(
                CreateQualifiedName(enumeratorType),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        enumeratorVariable.Identifier,
                        null,
                        SyntaxFactory.EqualsValueClause(
							PredefinedKeywords.EqualsToken,
							enumeratorMethodName == "MakeEnumerator" ? loopFunction :
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    loopFunction, 
                                    SyntaxFactory.IdentifierName("GetEnumerator")
                                )
                            )
                        )
                    )
                )
            );

            // Generate the Push statement with the loopType
            var pushStatement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Loops", "Push"),
                    loopType == null ? SyntaxFactory.ArgumentList() :
					CreateArgumentList(
                        CreateMemberAccess("Keysharp.Core.LoopType", loopType)
                    )
                )
            );

            // Generate the loop condition
            var loopCondition = ((InvocationExpressionSyntax)InternalMethods.IsTrueAndRunning)
                .WithArgumentList(
				    CreateArgumentList(
					    SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                enumeratorVariable,
                                SyntaxFactory.IdentifierName("MoveNext")
                            )
                        )
                    )
                );

            // Ensure the loop body is a block
            BlockSyntax loopBody = EnsureBlockSyntax(loopBodyNode);

            // Add the `Until` condition, if provided
            StatementSyntax untilStatement = untilCondition != null
                ? SyntaxFactory.IfStatement(
                    ((InvocationExpressionSyntax)InternalMethods.IfTest)
                    .WithArgumentList(
						CreateArgumentList(untilCondition)
                    ),
                    SyntaxFactory.Block(SyntaxFactory.BreakStatement())
                )
                : SyntaxFactory.EmptyStatement();

            // Add the loop continuation label `_ks_eX_next:`
            loopBody = loopBody.AddStatements(
                SyntaxFactory.LabeledStatement(
                    SyntaxFactory.Identifier(loopEnumeratorName + "_next"), 
                    SyntaxFactory.Token(SyntaxKind.ColonToken),
                    untilStatement)
            );

            // Generate the `for` loop
            var forLoop = SyntaxFactory.ForStatement(
                SyntaxFactory.Token(SyntaxKind.ForKeyword),
				SyntaxFactory.Token(SyntaxKind.OpenParenToken),
                default,
				SyntaxFactory.SeparatedList<ExpressionSyntax>(), // No initializer
				SyntaxFactory.Token(SyntaxKind.SemicolonToken),
                loopCondition,
				SyntaxFactory.Token(SyntaxKind.SemicolonToken),
                SyntaxFactory.SeparatedList<ExpressionSyntax>(), // No incrementor
				SyntaxFactory.Token(SyntaxKind.CloseParenToken),
                loopBody
            );

            // Generate the `_ks_eX_end:` label
            var endLabel = SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(loopEnumeratorName + "_end"),
                SyntaxFactory.Token(SyntaxKind.ColonToken),
                SyntaxFactory.EmptyStatement()
            );

            // Generate the Pop() call
            var popStatement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Loops", "Pop")
                )
            );

            // Handle the `Else` clause
            StatementSyntax elseClause = null;
            if (elseNode != null)
            {
                BlockSyntax elseBody = EnsureBlockSyntax(elseNode);

                // Wrap the else body in an if-check
                elseClause = SyntaxFactory.IfStatement(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.InvocationExpression(
								CreateMemberAccess("Keysharp.Core.Loops", "Pop")
                            ),
                            SyntaxFactory.IdentifierName("index")
                        ),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0L))
                    ),
                    elseBody
                );
            }
            else
                elseClause = popStatement;

            LocalDeclarationStatementSyntax backupDeclaration = null;
            ExpressionStatementSyntax restoreBackupVariables = null;

            if (backupVariableNames != null)
            {
                var uniqueVariableNames = backupVariableNames.Distinct().ToList();
                var backupIdentifier = SyntaxFactory.IdentifierName(loopEnumeratorName + "_backup");

                backupDeclaration = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ArrayType(
                            SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // object[]
                            SyntaxFactory.SingletonList(
                                SyntaxFactory.ArrayRankSpecifier(
                                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                        SyntaxFactory.OmittedArraySizeExpression() // Allow dynamic array initialization
                                    )
                                )
                            )
                        )
                    ).WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(backupIdentifier.Identifier)
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
									PredefinedKeywords.EqualsToken,
									SyntaxFactory.ArrayCreationExpression(
                                        SyntaxFactory.ArrayType(
                                            SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object),
                                            SyntaxFactory.SingletonList(
                                                SyntaxFactory.ArrayRankSpecifier(
                                                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(uniqueVariableNames.Count))
                                                    )
                                                )
                                            )
                                        )
                                    ).WithInitializer(
                                        SyntaxFactory.InitializerExpression(
                                            SyntaxKind.ArrayInitializerExpression,
                                            SyntaxFactory.SeparatedList<ExpressionSyntax>(
                                                uniqueVariableNames.Select(name => SyntaxFactory.IdentifierName(name))
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                );

                if (uniqueVariableNames.Count == 1)
                    restoreBackupVariables = SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(uniqueVariableNames[0]),
							PredefinedKeywords.EqualsToken,
							SyntaxFactory.ElementAccessExpression(backupIdentifier)
                                .WithArgumentList(
                                    SyntaxFactory.BracketedArgumentList(
                                        SyntaxFactory.SingletonSeparatedList(
                                            SyntaxFactory.Argument(
                                                SyntaxFactory.LiteralExpression(
                                                    SyntaxKind.NumericLiteralExpression,
                                                    SyntaxFactory.Literal(0)
                                                )
                                            )
                                        )
                                    )
                                )
                            )
                        );
                else
                    restoreBackupVariables = SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.TupleExpression(
                                SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                    uniqueVariableNames.Select(name =>
                                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name))
                                    )
                                )
                            ),
							PredefinedKeywords.EqualsToken,
							SyntaxFactory.TupleExpression(
                                SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                    uniqueVariableNames.Select((_, index) =>
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.ElementAccessExpression(backupIdentifier)
                                            .WithArgumentList(
                                                SyntaxFactory.BracketedArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Argument(
                                                            SyntaxFactory.LiteralExpression(
                                                                SyntaxKind.NumericLiteralExpression,
                                                                SyntaxFactory.Literal(index)
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
                );
            }

            // Create the finally block with Pop() and end label
            var finallyBlock = backupVariableNames == null ? SyntaxFactory.Block(elseClause) : SyntaxFactory.Block(restoreBackupVariables, elseClause);

            // Wrap the loop in a try-finally statement
            var tryFinallyStatement = SyntaxFactory.TryStatement(
                SyntaxFactory.Block(forLoop), // Try block containing the loop
                SyntaxFactory.List<CatchClauseSyntax>(), // No catch clauses
                SyntaxFactory.FinallyClause(
                    SyntaxFactory.Token(SyntaxKind.FinallyKeyword),
                    finallyBlock
                ) // Finally block
            );

            List<StatementSyntax> blockElements = new()
            {
				SyntaxFactory.LocalDeclarationStatement(enumeratorDeclaration),
				pushStatement,
				tryFinallyStatement,
				endLabel
			};
            if (backupDeclaration != null)
                blockElements.Insert(1, backupDeclaration);

            return SyntaxFactory.Block(
                blockElements
            );
        }

        public override SyntaxNode VisitElseProduction([NotNull] ElseProductionContext context)
        {
            return context.Else() != null
                ? EnsureBlockSyntax(Visit(context.statement()))
                     //Assume it was increased before visiting
				: null;
        }

        public override SyntaxNode VisitUntilProduction([NotNull] UntilProductionContext context)
        {
            return context.Until() != null
                ? Visit(context.singleExpression())
                : null;
        }

        public override SyntaxNode VisitFlowBlock([NotNull] FlowBlockContext context)
        {
            BlockSyntax result;
            if (context.block() != null)
                result = (BlockSyntax)Visit(context.block());
            else
                result = EnsureBlockSyntax(Visit(context.statement()));

            return result;
        }

        public override SyntaxNode VisitLoopStatement([NotNull] LoopStatementContext context)
        {
            var loopEnumeratorName = LoopEnumeratorBaseName + parser.LoopStack.Peek().Label;
            // Determine the loop expression (or -1 for infinite loops)
            ExpressionSyntax loopExpression = context.singleExpression() != null
                ? (ExpressionSyntax)Visit(context.singleExpression())
                : SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1));

            // Determine the `Until` condition, if present
            ExpressionSyntax untilCondition = context.untilProduction() != null ? (ExpressionSyntax)VisitUntilProduction(context.untilProduction()) : null;

            // Visit the loop body
            SyntaxNode loopBodyNode = Visit(context.flowBlock());

            // Visit the `Else` clause, if present
            SyntaxNode elseNode = context.elseProduction() != null ? Visit(context.elseProduction()) : null;
            // Invoke the generic loop handler
            return VisitLoopGeneric(
                loopExpression,
                loopBodyNode,
                untilCondition,
                elseNode,
                "Normal",
                loopEnumeratorName,
                "System.Collections.IEnumerator",
                "Loop",
                null,
                loopExpression
            );
        }

        public override SyntaxNode VisitSpecializedLoopStatement([NotNull] SpecializedLoopStatementContext context)
        {
			string loopType = null;
            string enumeratorMethodName = null;
			var loopEnumeratorName = LoopEnumeratorBaseName + parser.LoopStack.Peek().Label;
			var singleExprCount = context.singleExpression().Length;
			SyntaxNode loopBodyNode = Visit(context.flowBlock());
			ExpressionSyntax untilCondition = context.untilProduction() != null ? (ExpressionSyntax)Visit(context.untilProduction()) : null;
			SyntaxNode elseNode = context.elseProduction() != null ? Visit(context.elseProduction()) : null;
            ExpressionSyntax[] enumeratorArguments = null;

			switch (context.type.Type)
            {
                case MainLexer.Parse:
					// Visit the string literals
					enumeratorArguments = [(ExpressionSyntax)Visit(context.singleExpression(0)) //String
					    , singleExprCount > 1 //DelimiterChars
						    ? (ExpressionSyntax)Visit(context.singleExpression(1))
						    : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
					    , singleExprCount > 2 //OmitChars
							? (ExpressionSyntax)Visit(context.singleExpression(2))
						    : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)];
                    loopType = "Parse";
                    enumeratorMethodName = "LoopParse";
                    break;
                case MainLexer.Files:
					enumeratorArguments = [(ExpressionSyntax)Visit(context.singleExpression(0)) //FilePattern
                        , singleExprCount != 1 //Mode
						    ? (ExpressionSyntax)Visit(context.singleExpression(1))
						    : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)];

                    loopType = "Directory";
                    enumeratorMethodName = "LoopFile";
                    break;
                case MainLexer.Read:
                    enumeratorArguments = [(ExpressionSyntax)Visit(context.singleExpression(0)) //InputFile
                        , singleExprCount > 1 //OutputFile
                            ? (ExpressionSyntax)Visit(context.singleExpression(1))
                            : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)];
                    loopType = "File";
                    enumeratorMethodName = "LoopRead";
                    break;
				case MainLexer.Reg:
					enumeratorArguments = [(ExpressionSyntax)Visit(context.singleExpression(0)) //KeyName
					    , singleExprCount > 1 //Mode
						    ? (ExpressionSyntax)Visit(context.singleExpression(1))
						    : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)];
                    loopType = "Registry";
                    enumeratorMethodName = "LoopRegistry";
                    break;
                default:
                    throw new NotImplementedException();
			}

			return VisitLoopGeneric(
				null,
				loopBodyNode,
				untilCondition,
				elseNode,
				loopType,
				loopEnumeratorName,
				"System.Collections.IEnumerator",
				enumeratorMethodName,
				null,
				enumeratorArguments
			);
		}

        public override SyntaxNode VisitForInStatement([NotNull] ForInStatementContext context)
        {
            var loopEnumeratorName = LoopEnumeratorBaseName + parser.LoopStack.Peek().Label;

            // Get the loop expression (e.g., `arr` in `for x in arr`)
            var loopExpression = (ExpressionSyntax)Visit(context.forInParameters().singleExpression());

            // Collect loop variable names (e.g., `x`, `y` in `for x, y in arr`)
            var parameters = context.forInParameters();
            List<string> variableNames = new();
            var lastParamType = MainLexer.Comma;
            foreach (var parameter in parameters.children)
            {
                var paramType = MainLexer.Identifier;
                if (parameter is ITerminalNode node)
                    paramType = node.Symbol.Type;

                if (paramType == MainLexer.OpenParen || paramType == MainLexer.CloseParen || paramType == MainLexer.EOL || paramType == MainLexer.WS)
                    continue;
                if (paramType == MainLexer.In)
                {
                    if (lastParamType == MainLexer.Comma)
                        variableNames.Add("_");
                    break;
                }
                if (paramType == MainLexer.Comma)
                {
                    if (lastParamType == MainLexer.Comma)
                        variableNames.Add("_");
                }
                else
                {
                    var paramText = parameter.GetText();
                    var normalizedName = parser.NormalizeFunctionIdentifier(paramText);
                    var variableName = parser.MaybeAddVariableDeclaration(normalizedName);
                    variableNames.Add(variableName);
                }
                lastParamType = paramType;
            }
            var variableNameCount = variableNames.Count;

            if (variableNames.Any(name => name == "_"))
                parser.MaybeAddVariableDeclaration("_");

            // Ensure the loop body is a block
            BlockSyntax loopBody = (BlockSyntax)Visit(context.flowBlock());

            var incStatement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Loops", "Inc")
                )
            );

            // Add the `Current` assignment to the loop body
            loopBody = loopBody.WithStatements(
                SyntaxFactory.List(
                    new StatementSyntax[] { 
                        incStatement
                    }
                    .Concat(loopBody.Statements))
            );

            ExpressionSyntax untilCondition = context.untilProduction() != null ? (ExpressionSyntax)Visit(context.untilProduction()) : null;

            // Handle the `Else` clause, if present
            SyntaxNode elseNode = context.elseProduction() != null ? Visit(context.elseProduction()) : null;

            List<ExpressionSyntax> argsList = new List<ExpressionSyntax>();
            argsList.Add(loopExpression);

            foreach (string variableName in variableNames)
            {
                ExpressionSyntax varRefExpr = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Misc"),
                        SyntaxFactory.IdentifierName("MakeVarRef")
                    ),
					CreateArgumentList(
						// Getter lambda: () => variableName
						SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(),
                            SyntaxFactory.IdentifierName(variableName)
                        ),
                        // Setter lambda: (Val) => variableName = Val
                        SyntaxFactory.ParenthesizedLambdaExpression(
                            SyntaxFactory.ParameterList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Parameter(SyntaxFactory.Identifier("Val"))
                                )
                            ),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(variableName),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.IdentifierName("Val")
                            )
                        )
                    )
                );

                argsList.Add(varRefExpr);
            }

            // Generate the final loop structure using `VisitLoopGeneric`
            BlockSyntax loopSyntax = (BlockSyntax)VisitLoopGeneric(
                null,
                loopBody,
                untilCondition,
                elseNode,
                null,
                loopEnumeratorName,
                "var",
                "MakeEnumerable",
                variableNames,
                argsList.ToArray()
            );

            return loopSyntax;
        }


        public override SyntaxNode VisitWhileStatement([NotNull] WhileStatementContext context)
        {
            var loopEnumeratorName = LoopEnumeratorBaseName + parser.LoopStack.Peek().Label;

            // Visit the singleExpression (loop condition)
            var conditionExpression = (ExpressionSyntax)Visit(context.singleExpression());

            // Wrap the condition in IfTest
            var conditionWrapped = SyntaxFactory.InvocationExpression(
				CreateMemberAccess("Keysharp.Scripting.Script", "IfTest"),
				CreateArgumentList(conditionExpression)
            );

            // Generate the loop condition: IsTrueAndRunning(IfTest(...))
            var loopCondition = ((InvocationExpressionSyntax)InternalMethods.IsTrueAndRunning)
                .WithArgumentList(CreateArgumentList(conditionWrapped));

            // Generate the loop body
            BlockSyntax loopBody = (BlockSyntax)Visit(context.flowBlock());

            var incStatement = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        CreateMemberAccess("Keysharp.Core.Loops", "Inc")
                    )
                );

            // Add Keysharp.Core.Loops.Inc() at the start of the loop body
            if (loopBody.Statements.Count > 0)
                loopBody = loopBody.InsertNodesBefore(
                    loopBody.Statements.FirstOrDefault(), new[] { incStatement }
                );
            else
                loopBody = SyntaxFactory.Block(incStatement);

            StatementSyntax untilStatement;
            ExpressionSyntax untilCondition = context.untilProduction() != null ? (ExpressionSyntax)Visit(context.untilProduction()) : null;
            if (untilCondition != null)
            {
                untilStatement = SyntaxFactory.IfStatement(
                    SyntaxFactory.InvocationExpression(
						CreateMemberAccess("Keysharp.Scripting.Script", "IfTest"),
						CreateArgumentList(untilCondition)
                    ),
                    SyntaxFactory.Block(
                        SyntaxFactory.BreakStatement()
                    )
                );
            }
            else
                untilStatement = SyntaxFactory.EmptyStatement();

            // Add the loop continuation label `_ks_e1_next:`
            loopBody = loopBody.AddStatements(
                SyntaxFactory.LabeledStatement(
                    loopEnumeratorName + "_next",
                    untilStatement
                )
            );

            // Generate the `for` loop structure
            var forLoop = SyntaxFactory.ForStatement(
                null, // No initializer
                SyntaxFactory.SeparatedList<ExpressionSyntax>(), // No initializer
                loopCondition,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(), // No incrementor
                loopBody
            );

            // Generate `_ks_e1_end:` label and Pop statement
            var endLabel = SyntaxFactory.LabeledStatement(
                loopEnumeratorName + "_end",
                SyntaxFactory.EmptyStatement()
            );

            // Generate the Pop() call
            var popStatement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Loops", "Pop")
				)
            );

            // Handle the `Else` clause if present
            StatementSyntax elseClause = null;
            BlockSyntax elseBody = context.elseProduction() != null ? (BlockSyntax)Visit(context.elseProduction()) : null;
            if (elseBody != null)
            {
                // Wrap the else body in an if statement checking Pop().index == 0L
                elseClause = SyntaxFactory.IfStatement(
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.InvocationExpression(
								CreateMemberAccess("Keysharp.Core.Loops", "Pop")
                            ),
                            SyntaxFactory.IdentifierName("index")
                        ),
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            SyntaxFactory.Literal(0L)
                        )
                    ),
                    elseBody
                );
            }

            var startLoopStatement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Loops", "Push")
                )
            );

            // Create the finally block
            var finallyBlock = elseClause == null
                ? SyntaxFactory.Block(popStatement)
                : SyntaxFactory.Block(elseClause);

            // Wrap the for loop in a try-finally statement
            var tryFinallyStatement = SyntaxFactory.TryStatement(
                SyntaxFactory.Block(forLoop), // Try block containing the loop
                SyntaxFactory.List<CatchClauseSyntax>(), // No catch clauses
                SyntaxFactory.FinallyClause(finallyBlock) // Finally block
            );

            // Wrap everything in a block and return
            return SyntaxFactory.Block(startLoopStatement, tryFinallyStatement, endLabel);
        }


        public override SyntaxNode VisitContinueStatement([NotNull] ContinueStatementContext context)
        {
            var targetLabel = parser.LoopStack.Peek().Label;
            if (context.propertyName() != null)
                targetLabel = context.propertyName().GetText().Trim('"');
            targetLabel = LoopEnumeratorBaseName + targetLabel + "_next";

            // Generate the goto statement
            return SyntaxFactory.GotoStatement(
                SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(targetLabel)
            );
        }

        public override SyntaxNode VisitBreakStatement([NotNull] BreakStatementContext context)
        {
            var targetLabel = parser.loopDepth.ToString();
            if (context.propertyName() != null)
            {
                targetLabel = context.propertyName().GetText().Trim('"');
                if (int.TryParse(targetLabel, out int result) && result <= parser.loopDepth && result > 0)
                {
                    targetLabel = (parser.loopDepth + 1 - result).ToString();
                }
            }
            targetLabel = LoopEnumeratorBaseName + targetLabel + "_end";

            // Generate the goto statement
            return SyntaxFactory.GotoStatement(
                SyntaxKind.GotoStatement,
                SyntaxFactory.IdentifierName(targetLabel)
			);
        }

        private string exceptionIdentifierName;

        public override SyntaxNode VisitTryStatement([NotNull] TryStatementContext context)
        {
            parser.tryDepth++;
            var prevExceptionIdentifierName = exceptionIdentifierName;
            var elseClaudeIdentifier = InternalPrefix + "trythrew_" + parser.tryDepth.ToString();
            // Generate the try block
            BlockSyntax tryBlock = EnsureBlockSyntax(Visit(context.statement()));

            // Generate the Else block (if present)
            StatementSyntax elseCondition = null;
            LocalDeclarationStatementSyntax exceptionVariableDeclaration = null;
            if (context.elseProduction()?.Else() != null)
            {
                BlockSyntax elseBlock = ((BlockSyntax)Visit(context.elseProduction()));

				// Declare the `exception` variable
				exceptionVariableDeclaration = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(elseClaudeIdentifier)
                                .WithInitializer(
                                    SyntaxFactory.EqualsValueClause(
										PredefinedKeywords.EqualsToken, 
                                        SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))
                                )
                        )
                    )
                );

                // Add `exception = false;` at the end of the try block
                tryBlock = tryBlock.AddStatements(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(elseClaudeIdentifier),
							PredefinedKeywords.EqualsToken,
							SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                        )
                    )
                );

                // Create `if (!exception) { ... }` for the Else block
                elseCondition = SyntaxFactory.IfStatement(
                    SyntaxFactory.PrefixUnaryExpression(
                        SyntaxKind.LogicalNotExpression,
                        SyntaxFactory.IdentifierName(elseClaudeIdentifier)
                    ),
                    elseBlock
                );
            }

            // Generate Catch clauses
            
            var catchClauses = new List<CatchClauseSyntax>();
            uint i = 0;
            foreach (var catchProduction in context.catchProduction())
            {
                i++;
                exceptionIdentifierName = InternalPrefix + "ex_" + parser.tryDepth.ToString() + "_" + i.ToString();
                catchClauses.Add((CatchClauseSyntax)VisitCatchProduction(catchProduction));
            }

            // Ensure a catch clause for `Keysharp.Core.Error` exists
            if (catchClauses.Count == 0)
            {
                var keysharpErrorCatch = SyntaxFactory.CatchClause(
					SyntaxFactory.Token(SyntaxKind.CatchKeyword),
					SyntaxFactory.CatchDeclaration(
                        SyntaxFactory.ParseTypeName("Keysharp.Core.Error"),
                        SyntaxFactory.Identifier(InternalPrefix + "ex_" + parser.tryDepth.ToString() + "_0")
                    ),
                    default,
                    SyntaxFactory.Block()
                );

                catchClauses.Add(keysharpErrorCatch);
            }

            catchClauses.Sort(
                (c1, c2) =>
                  {
                      var t1 = Type.GetType(c1.Declaration?.Type.ToString() ?? "", false, true);
                      var t2 = Type.GetType(c2.Declaration?.Type.ToString() ?? "", false, true);

                      if (t1 == t2)
                          return 0;

                      var d1 = Keysharp.Scripting.Parser.TypeDistance(t1, typeof(KeysharpException));
                      var d2 = Keysharp.Scripting.Parser.TypeDistance(t2, typeof(KeysharpException));

                      if (d1 == d2)
                          return 0;
                      else if (d1 < d2)
                          return 1;
                      else
                          return -1;
                  });

			// Gather every TypeSyntax we need to PushTry
			var handledTypeSyntaxes = new List<TypeSyntax>();
			for (int j = 0; j < catchClauses.Count; j++)
			{
                var catchClause = catchClauses[j];
				// if there's a `when`‐filter, extract all the TypePattern nodes underneath it
				if (catchClause.Filter is CatchFilterClauseSyntax filterClause)
				{
					var patterns = filterClause
						.FilterExpression
						.DescendantNodesAndSelf()
						.OfType<TypePatternSyntax>();

					foreach (var tp in patterns)
						handledTypeSyntaxes.Add(tp.Type);
				}
				// otherwise just use the declared exception type
				else if (catchClause.Declaration?.Type != null)
				{
					handledTypeSyntaxes.Add(catchClause.Declaration.Type);
				}
			}

			// Generate Finally block
			FinallyClauseSyntax finallyClause = null;
			if (context.finallyProduction()?.Finally() != null || elseCondition != null)
			{
				var finallyStatements = new List<StatementSyntax>();
				if (elseCondition != null)
				{
					finallyStatements.Add(elseCondition);
				}

				if (context.finallyProduction().Finally() != null)
				{
					finallyStatements.AddRange(((BlockSyntax)VisitFinallyProduction(context.finallyProduction())).Statements);
				}

				finallyClause = SyntaxFactory.FinallyClause(
					SyntaxFactory.Token(SyntaxKind.FinallyKeyword),
					SyntaxFactory.Block(finallyStatements)
						
				);
			}

			var pushTryStmt = SyntaxFactory.ExpressionStatement(
                ((InvocationExpressionSyntax)InternalMethods.PushTry)
	            .WithArgumentList(
					CreateArgumentList(
						handledTypeSyntaxes
				        .Select(ts => SyntaxFactory.Argument(
					        SyntaxFactory.TypeOfExpression(ts)))
		            )
	            )
            );

			var popTryStmt = SyntaxFactory.ExpressionStatement((InvocationExpressionSyntax)InternalMethods.PopTry);

            var innerTry = SyntaxFactory.TryStatement(
				SyntaxFactory.Token(SyntaxKind.TryKeyword),
                tryBlock,
                default,
                SyntaxFactory.FinallyClause(
                    SyntaxFactory.Token(SyntaxKind.FinallyKeyword),
                    SyntaxFactory.Block(popTryStmt)
                )
            );
           

			// Construct the TryStatementSyntax
			var tryStatement = SyntaxFactory.TryStatement(
				SyntaxFactory.Token(SyntaxKind.TryKeyword),
				SyntaxFactory.Block(
                    pushTryStmt, 
                    innerTry),
				SyntaxFactory.List(catchClauses),
				finallyClause != null ? finallyClause : default);

            parser.tryDepth--;
            exceptionIdentifierName = prevExceptionIdentifierName;

            if (exceptionVariableDeclaration != null)
            {
                return SyntaxFactory.Block(new SyntaxList<StatementSyntax> { exceptionVariableDeclaration, tryStatement });
            }

            return tryStatement;
        }

        public override SyntaxNode VisitCatchProduction([NotNull] CatchProductionContext context)
        {
            // Visit the Catch block
            var block = (BlockSyntax)(Visit(context.flowBlock()));

            var catchAssignable = context.catchAssignable();
            if (catchAssignable != null)
            {
                // Handle optional `As` and `identifier`
                if (catchAssignable.identifier() != null)
                {
                    var catchVarName = parser.NormalizeFunctionIdentifier(catchAssignable.identifier().GetText());
                    if (parser.IsVarDeclaredLocally(catchVarName) != null)
                    {
                        var assignmentStatement = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(catchVarName),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.IdentifierName(exceptionIdentifierName)
                            )
                        );
                        block = SyntaxFactory.Block(
                            block.Statements.Insert(0, assignmentStatement)
                        );
                    } else
                        exceptionIdentifierName = catchVarName;
                }
                
                SyntaxToken exceptionIdentifier = SyntaxFactory.Identifier(exceptionIdentifierName);

                TypeSyntax exceptionType = SyntaxFactory.ParseTypeName("Keysharp.Core.Error");
                var typeConditions = new List<ExpressionSyntax>();

                if (catchAssignable.catchClasses() != null)
                {
                    string catchClassText = null;
                    foreach (var catchClass in catchAssignable.catchClasses().identifier())
                    {
                        catchClassText = catchClass.GetText();
                        if (catchClassText.Equals("As", StringComparison.OrdinalIgnoreCase))
                            continue;
					    else if (catchClassText.Equals("Any", StringComparison.OrdinalIgnoreCase))
                            catchClassText = "Error";
                        if (Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(catchClassText, out var t))
                            catchClassText = t.FullName;
                        else
                            catchClassText = "Keysharp.Core." + catchClassText;

                        // Create condition: `ex is IndexError`
                        typeConditions.Add(
                            SyntaxFactory.IsPatternExpression(
								SyntaxFactory.IdentifierName(exceptionIdentifierName),
								PredefinedKeywords.IsToken,
								SyntaxFactory.TypePattern(SyntaxFactory.ParseTypeName(catchClassText))
                            )
                        );
                    }
                    if (typeConditions.Count == 1)
                        exceptionType = SyntaxFactory.ParseTypeName(catchClassText);
                } 
                
                if (typeConditions.Count == 0)
                {
                    typeConditions.Add(
                        SyntaxFactory.IsPatternExpression(
                            SyntaxFactory.IdentifierName(exceptionIdentifierName),
							PredefinedKeywords.IsToken,
							SyntaxFactory.TypePattern(SyntaxFactory.ParseTypeName("Keysharp.Core.Error"))
                        )
                    );
                }

                ExpressionSyntax conditionExpression = typeConditions[0];

                for (int i = 1; i < typeConditions.Count; i++)
                {
                    conditionExpression = SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalOrExpression,
                        conditionExpression,
                        typeConditions[i]
                    );
                }

                CatchFilterClauseSyntax whenClause = typeConditions.Count > 1
                    ? SyntaxFactory.CatchFilterClause(conditionExpression)
                    : null;

                return SyntaxFactory.CatchClause(
                    SyntaxFactory.Token(SyntaxKind.CatchKeyword),
					SyntaxFactory.CatchDeclaration(exceptionType, exceptionIdentifier),
                    whenClause,
                    block
				);
            }

            // Catch-all clause
            return SyntaxFactory.CatchClause(
				SyntaxFactory.Token(SyntaxKind.CatchKeyword),
				SyntaxFactory.CatchDeclaration(
					SyntaxFactory.ParseTypeName("Keysharp.Core.Error"),
					SyntaxFactory.Identifier(exceptionIdentifierName)
				),
                default,
                block
			);
        }

        public override SyntaxNode VisitFinallyProduction([NotNull] FinallyProductionContext context)
        {
            return EnsureBlockSyntax(Visit(context.statement()));
        }

        private bool switchCaseSense = true;
        private uint caseClauseCount = 0;
        private bool switchValueExists = true;
        public override SyntaxNode VisitSwitchStatement([NotNull] SwitchStatementContext context)
        {
            // Extract the switch value (SwitchValue)
            bool prevSwitchValue = switchValueExists;
            bool prevSwitchCaseSense = switchCaseSense;

			switchValueExists = context.singleExpression() != null;
            var switchValue = switchValueExists
                ? (ExpressionSyntax)Visit(context.singleExpression())
                : SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);

            // Extract case sensitivity (CaseSense)
            LiteralExpressionSyntax caseSense = context.literal() != null
                ? (LiteralExpressionSyntax)Visit(context.literal())
                : SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1)); // Default: case-sensitive
            switchCaseSense = (caseSense.Token.Text == "1L" || caseSense.Token.Text == "1" || caseSense.Token.Text.Equals("on", StringComparison.InvariantCultureIgnoreCase));

            // Visit the case block
            var caseBlock = (SwitchStatementSyntax)VisitCaseBlock(context.caseBlock());

            ExpressionSyntax switchValueToString;
            if (switchValueExists)
            {
                switchValueToString = ((InvocationExpressionSyntax)InternalMethods.ForceString)
                .WithArgumentList(
					CreateArgumentList(switchValue)
                );
                switchValueToString = !switchCaseSense
                    ? SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            switchValueToString,
                            SyntaxFactory.IdentifierName("ToLower")
                        )
                    )
                    : switchValueToString;
            }
            else
                switchValueToString = switchValue;

            var switchInvocation = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier("caseIndex"),
                            null,
                            SyntaxFactory.EqualsValueClause(PredefinedKeywords.EqualsToken, switchValueToString)
                        )
                    )
                )
            );

            switchValueExists = prevSwitchValue;
			switchCaseSense = prevSwitchCaseSense;

			// Combine the SwitchHelper invocation and the case block
			return caseBlock.WithExpression(switchValueToString);
        }

        public override SyntaxNode VisitCaseBlock([NotNull] CaseBlockContext context)
        {
            var sections = new List<SwitchSectionSyntax>();
            var caseExpressions = new List<ExpressionSyntax>();
            SwitchSectionSyntax defaultClause = null;

            if (context.caseClause().Length > 0)
            {
				foreach (var caseClause in context.caseClause())
				{
                    SwitchSectionSyntax fullSection = (SwitchSectionSyntax)VisitCaseClause(caseClause);
					sections.Add(fullSection);
				}
			}

            if (defaultClause != null)
                sections.Add(defaultClause);

			// Return the switch statement
			return SyntaxFactory.SwitchStatement(
                SyntaxFactory.Token(SyntaxKind.SwitchKeyword),
				SyntaxFactory.Token(SyntaxKind.OpenParenToken),
				SyntaxFactory.IdentifierName("caseIndex"),
				SyntaxFactory.Token(SyntaxKind.CloseParenToken),
				SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
				SyntaxFactory.List(sections),
				SyntaxFactory.Token(SyntaxKind.CloseBraceToken)

			);
        }

        public override SyntaxNode VisitCaseClause([NotNull] CaseClauseContext context)
        {
			// Visit the statement list, if present
			var statements = context.statementList() != null
				? (StatementSyntax)Visit(context.statementList())
				: SyntaxFactory.Block();

			// Ensure a break statement is appended
			statements = EnsureBreakStatement(statements);

            SyntaxList<SwitchLabelSyntax> switchLabels;

            if (context.Default() != null)
            {
                switchLabels = SyntaxFactory.SingletonList<SwitchLabelSyntax>(
                        SyntaxFactory.DefaultSwitchLabel()
                    );
            }
            else
            {
                // Visit the case expressions and generate case labels
                var caseLabels = context.expressionSequence()
                    .expression()
                    .Select(expr =>
                    {
                        ++caseClauseCount;
                        var exprSyntax = (ExpressionSyntax)Visit(expr);

                        // Convert the case expression to string
                        var toStringExpr = switchValueExists
                            ? ((InvocationExpressionSyntax)InternalMethods.ForceString)
                                .WithArgumentList(
									CreateArgumentList(exprSyntax)
                                )
                            : exprSyntax;

                        // Handle case-sensitivity by applying ToLower if switchCaseSense is false
                        var comparisonExpr = !switchCaseSense
                            ? SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    toStringExpr,
                                    SyntaxFactory.IdentifierName("ToLower")
                                )
                            )
                            : toStringExpr;

                        // Generate either `case true when ...` or `case ...` based on `switchValueExists`
                        if (!switchValueExists)
                        {
                            // Create a `case true when ...` label
                            return SyntaxFactory.CasePatternSwitchLabel(
								SyntaxFactory.Token(SyntaxKind.CaseKeyword), // case keyword
                                SyntaxFactory.ConstantPattern(
                                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                                ), // case true
                                SyntaxFactory.WhenClause(
                                    SyntaxFactory.Token(SyntaxKind.WhenKeyword),
                                    SyntaxFactory.CastExpression(
                                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                                        ((InvocationExpressionSyntax)InternalMethods.ForceBool)
                                        .WithArgumentList(
											CreateArgumentList(comparisonExpr)
                                        )
                                    )
                                ),
								SyntaxFactory.Token(SyntaxKind.ColonToken) // colon
							);
                        }
                        else
                        {
                            // Create a condition for the `when` clause
                            var whenClause = SyntaxFactory.WhenClause(
								SyntaxFactory.Token(SyntaxKind.WhenKeyword),
								SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(InternalPrefix + "string_" + caseClauseCount),
                                        SyntaxFactory.IdentifierName("Equals")
                                    ),
									CreateArgumentList(comparisonExpr)
                                )
                            );

                            // Create a `case string s when ...` label
                            return SyntaxFactory.CasePatternSwitchLabel(
								SyntaxFactory.Token(SyntaxKind.CaseKeyword), // case keyword
                                SyntaxFactory.DeclarationPattern(
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                                    SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(InternalPrefix + "string_" + caseClauseCount))
                                ),
                                whenClause, // when condition
								SyntaxFactory.Token(SyntaxKind.ColonToken) // colon
							);
                        }
                    })
                    .ToArray();

                switchLabels = SyntaxFactory.List<SwitchLabelSyntax>(caseLabels);
			}

            // Return a switch section for this case
            return SyntaxFactory.SwitchSection(switchLabels, 
                SyntaxFactory.List<StatementSyntax>(
                    statements is BlockSyntax blockSyntax
                        ? blockSyntax.Statements // Unwrap block statements
                        : new[] { statements }   // Wrap single statement in an array
                )
            );
        }

        public override SyntaxNode VisitLabelledStatement([NotNull] LabelledStatementContext context)
        {
            // Get the label identifier
            var labelName = parser.ToValidIdentifier(context.identifier().GetText().Trim('"'));

            // Return a labeled statement with an empty statement as the body
            return SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(labelName),
                SyntaxFactory.EmptyStatement()
            );
        }

		internal IReadOnlyList<LabelInfo> GetValidTargetsForGoto(MainParser.GotoStatementContext gotoCtx)
		{
            if (!parser.functionParserData.TryGetValue(parser.currentFunc.RootContext, out var functionData))
                return new List<LabelInfo>();
            
			if (!functionData.Index.GotoSitePaths.TryGetValue(gotoCtx, out var sitePath))
				sitePath = System.Array.Empty<int>(); // fallback; should not happen if pre-pass saw it

			// Only labels whose region path is a prefix of the goto site’s path.
			return functionData.Index.Labels.Where(l => IsPrefix(l.Path, sitePath)).ToList();

		    bool IsPrefix(int[] prefix, int[] full)
		    {
			    if (prefix.Length > full.Length) return false;
			    for (int i = 0; i < prefix.Length; i++)
				    if (prefix[i] != full[i]) return false;
			    return true;
		    }
		}

		public override SyntaxNode VisitGotoStatement([NotNull] GotoStatementContext context)
        {
            if (context.propertyName() != null)
            {
                // Get the target label
                var labelName = parser.ToValidIdentifier(context.propertyName().GetText().Trim('"'));

				if (labelName == null)
                    throw new ArgumentException("Goto target label is missing.");

				// Return the Goto statement
				return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(labelName));
			}

			var labels = GetValidTargetsForGoto(context);
			var expr = Visit(context.singleExpression());
            var exprString = ((InvocationExpressionSyntax)InternalMethods.ForceString)
                .WithArgumentList(CreateArgumentList(expr));

			var tempVar = parser.PushTempVar();

			// switch (ForceString(expr).ToLowerInvariant()) { case "foo": goto foo; ... default: throw; }
			ExpressionSyntax selector = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, tempVar, SyntaxFactory.InvocationExpression(
				SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
					exprString,
					SyntaxFactory.IdentifierName("ToLowerInvariant"))));

			var sections = new List<SwitchSectionSyntax>(labels.Count + 1);
			foreach (var li in labels)
			{
				sections.Add(
					SyntaxFactory.SwitchSection(
						SyntaxFactory.SingletonList<SwitchLabelSyntax>(
							SyntaxFactory.CaseSwitchLabel(
								SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
									SyntaxFactory.Literal(li.Raw.ToLowerInvariant())))),
						SyntaxFactory.SingletonList<StatementSyntax>(
							SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(parser.ToValidIdentifier(li.Raw))))));
			}

			sections.Add(
	            SyntaxFactory.SwitchSection(
		            SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.DefaultSwitchLabel()),
		            SyntaxFactory.SingletonList<StatementSyntax>(
			            SyntaxFactory.ThrowStatement(CreateErrorWihMessage("Invalid goto target: {0}", tempVar)))));

            parser.PopTempVar();

			return SyntaxFactory.SwitchStatement(selector, SyntaxFactory.List(sections));
        }

		private static ObjectCreationExpressionSyntax CreateErrorWihMessage(string fmt, ExpressionSyntax arg) =>
	    SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("Error"))
	    .WithArgumentList(
		    SyntaxFactory.ArgumentList(
			    SyntaxFactory.SingletonSeparatedList(
				    SyntaxFactory.Argument(
					    SyntaxFactory.InvocationExpression(CreateQualifiedName("System.String.Format"))
					    .WithArgumentList(
						    SyntaxFactory.ArgumentList(
							    SyntaxFactory.SeparatedList(new[]
							    {
								    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
									    SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(fmt))),
								    SyntaxFactory.Argument(arg)
							    })))))));


	}
}