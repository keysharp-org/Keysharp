using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Keysharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Keysharp.Scripting.Parser;
using static MainParser;

namespace Keysharp.Scripting
{
    internal partial class VisitMain : MainParserBaseVisitor<SyntaxNode>
    {
        internal Keysharp.Scripting.Parser parser;
        public VisitMain(Keysharp.Scripting.Parser _parser) : base()
        {
            parser = _parser;
        }

		public override SyntaxNode VisitProgram([NotNull] ProgramContext context)
        {
            var usingDirectives = BuildUsingDirectiveSyntaxList(CompilerHelper.GlobalUsingStr);

			parser.AddAssembly("Keysharp.Scripting.AssemblyBuildVersionAttribute", Accessors.A_AhkVersion);

            parser.compilationUnit = SyntaxFactory.CompilationUnit()
                .AddUsings(usingDirectives.ToArray());

            // Create using directives
            var usings = BuildUsingDirectiveSyntaxList(CompilerHelper.NamespaceUsingStr);

			parser.namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(CreateQualifiedName(Keywords.MainNamespaceName))
                .AddUsings(usings.ToArray());

            parser.currentClass = new Parser.Class(Keywords.MainClassName, null);
            parser.mainClass = parser.currentClass;

            var mainFunc = new Function("Main", SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)));

            var mainFuncParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("args"))
                .WithType(PredefinedKeywords.StringArrayType);

            var staThreadAttribute = SyntaxFactory.Attribute(
                CreateQualifiedName("System.STAThreadAttribute"))
                .WithArgumentList(SyntaxFactory.AttributeArgumentList());

            mainFunc.Attributes.Add(staThreadAttribute);

            mainFunc.Params.Add(mainFuncParam);

            var modifierList = new List<SyntaxKind>() { SyntaxKind.PrivateKeyword, SyntaxKind.StaticKeyword };

			var hsManagerDeclaration = Parser.CreateFieldDeclaration(
				modifierList, 
                CreateQualifiedName("Keysharp.Core.Common.Keyboard.HotstringManager"), 
                "MainHotstringManager",
				SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("MainScript"),
                    SyntaxFactory.IdentifierName("HotstringManager")
                )
            );
            var mainClassType = SyntaxFactory.TypeOfExpression(SyntaxFactory.IdentifierName(Keywords.MainClassName));
            object[] mainScriptVarDeclarationArgs = parser.hookMutexName == "" ? [mainClassType] : [mainClassType, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(parser.hookMutexName))];

			var mainScriptVarDeclaration = Parser.CreateFieldDeclaration(
				modifierList,
                CreateQualifiedName("Keysharp.Scripting.Script"),
				MainScriptVariableName,
				SyntaxFactory.ObjectCreationExpression(
	                CreateQualifiedName("Keysharp.Scripting.Script"),    // the type to construct
	                CreateArgumentList(mainScriptVarDeclarationArgs),          // the argument list
	                null           // no initializer
                )
			);
			parser.mainClass.Body.Add(mainScriptVarDeclaration);
			parser.mainClass.Body.Add(hsManagerDeclaration);

            parser.mainFuncInitial.Add(
                SyntaxFactory.ExpressionStatement(
			        SyntaxFactory.InvocationExpression(
                        CreateMemberAccess(MainScriptVariableName, "SetName"),
				        Parser.CreateArgumentList(
					        SyntaxFactory.LiteralExpression(
						        SyntaxKind.StringLiteralExpression,
						        SyntaxFactory.Literal(parser.fileName == "*" ? "*" : Path.GetFullPath(parser.fileName))
					        )
                        )
                    )
			    )
		    );

			foreach (var (p, s) in parser.reader.PreloadedDlls)
            {
                parser.mainFuncInitial.Add(
				    SyntaxFactory.ExpressionStatement(
				        SyntaxFactory.InvocationExpression(
                            CreateMemberAccess(MainScriptVariableName, "LoadDll"),
					        CreateArgumentList(
						        SyntaxFactory.LiteralExpression(
							        SyntaxKind.StringLiteralExpression,
							        SyntaxFactory.Literal(p)
						        ),
						        SyntaxFactory.LiteralExpression(s ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)
					        )
                        )
				    )
			    );
            }

			var mainBodyBlock = CreateMainMethod(Keywords.MainScriptVariableName, Keywords.AutoExecSectionName, System.Enum.GetName(typeof(eScriptInstance), parser.reader.SingleInstance), parser.mainFuncInitial);
			mainFunc.Body = mainBodyBlock.Statements.ToList();

            parser.autoExecFunc = new Function(Keywords.AutoExecSectionName, SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object));
            parser.currentFunc = parser.autoExecFunc;
            parser.autoExecFunc.Scope = eScope.Global;
            parser.autoExecFunc.Method = parser.autoExecFunc.Method
                .AddModifiers(Parser.PredefinedKeywords.PublicToken, Parser.PredefinedKeywords.StaticToken);

            // Map out all user class types and also any built-in types they derive from
            var allClassDeclarations = GetClassDeclarationsRecursive(context);
            foreach (var classDeclaration in allClassDeclarations)
            {
                var className = classDeclaration.identifier().GetText();
                var classBase = classDeclaration.classExtensionName()?.GetText() ?? "KeysharpObject";
                parser.UserTypes[className] = classBase;
                parser.AllTypes[className] = classBase;
                while (Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(classBase, out Type baseType))
                {
                    classBase = parser.AllTypes[baseType.Name] = baseType.BaseType.Name;
                }
            }

            // Create global FuncObj variables for all functions here, because otherwise during parsing
            // we might not know how to case the name.
            var scopeFunctionDeclarations = parser.GetScopeFunctions(context, this);
			var mainClassBody = parser.mainClass.Body;
			foreach (var funcName in scopeFunctionDeclarations)
            {
                if (funcName.Name == "") continue;
                parser.UserFuncs.Add(funcName.Name);

				var funcObjVariable = SyntaxFactory.FieldDeclaration(
	                CreateFuncObjDelegateVariable(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(funcName.Name))
                )
                .WithModifiers(
	                SyntaxFactory.TokenList(
		                Parser.PredefinedKeywords.PublicToken,
		                Parser.PredefinedKeywords.StaticToken
	                )
                );
				string funcObjName = funcObjVariable.Declaration.Variables.First().Identifier.Text;

				for (int i = 0; i < mainClassBody.Count; i++)
				{
					if (mainClassBody[i] is FieldDeclarationSyntax fds && fds.Declaration.Variables.First().Identifier.Text == funcObjName)
					{
                        mainClassBody[i] = funcObjVariable;
                        funcObjVariable = null;
						break;
					}
				}

                if (funcObjVariable != null)
				    parser.mainClass.Body.Add(funcObjVariable);
            }

            if (context.sourceElements() != null)
                VisitSourceElements(context.sourceElements());

            GenerateGeneralDirectiveStatements();

            if (parser.persistent)
				parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
	                SyntaxFactory.InvocationExpression(
                        CreateMemberAccess("Keysharp.Core.Flow", "Persistent")
	                )
                ));

			// Merge positional directives, hotkeys, hotstrings to the beginning of the auto-execute section
			parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Core.Common.Keyboard.HotkeyDefinition", "ManifestAllHotkeysHotstringsHooks")
                )
            ));

            parser.autoExecFunc.Body = 
                parser.generalDirectiveStatements.Concat(parser.DHHR)
                .Concat(parser.autoExecFunc.Body)
                .ToList();

			ClassDeclarationSyntax userClasses =
	            SyntaxFactory.ClassDeclaration(UserDeclaredClassesContainerName)
		            .WithModifiers(
						SyntaxFactory.TokenList(
				            SyntaxFactory.Token(SyntaxKind.InternalKeyword), 
                            SyntaxFactory.Token(SyntaxKind.StaticKeyword)
			            )
		            )
		            .AddMembers(parser.declaredTopLevelClasses.ToArray());

            parser.mainClass.Body.Insert(0, userClasses);

			// Return "" by default
			parser.autoExecFunc.Body.Add(PredefinedKeywords.DefaultReturnStatement);
            parser.autoExecFunc.Method = parser.autoExecFunc.Assemble();

			mainFunc.Method = mainFunc.Assemble();
            // Add the Main function to the beginning, and AutoExecSection to the end. Keyview requires Main to be at the beginning
            parser.mainClass.Body.Insert(0, mainFunc.Method);
            parser.mainClass.Body.Add(parser.autoExecFunc.Method);
            parser.namespaceDeclaration = parser.namespaceDeclaration.AddMembers(parser.mainClass.Assemble());

            var attributeList = SyntaxFactory.AttributeList(parser.assemblies)
                .WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.AssemblyKeyword)));

            // Using tabs as indentation rather than spaces seems to be more performant.
            // Not normalizing whitespaces is even faster though, but breaks code compilation.
            parser.compilationUnit = parser.compilationUnit
                .AddMembers(parser.namespaceDeclaration)
                .AddAttributeLists(attributeList);
            //.NormalizeWhitespace("\t", Environment.NewLine);

			return parser.compilationUnit;
        }

		internal static BlockSyntax CreateMainMethod(
			string mainScriptVarName,            // e.g. "MainScript"
			string autoExecName,                 // e.g. "AutoExecSection"
			string singleInstanceMemberName,      // e.g. Enum.GetName(typeof(eScriptInstance), parser.reader.SingleInstance)
			List<StatementSyntax> tryStatements = null
		)
		{
			// ---- local helpers (closures) ----
			static NameSyntax Q(string dotted) => Keysharp.Scripting.Parser.CreateQualifiedName(dotted); // VisitorHelper
			static MemberAccessExpressionSyntax MA(ExpressionSyntax left, string right) =>
				SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, left, SyntaxFactory.IdentifierName(right));
			static MemberAccessExpressionSyntax SMA(string leftQ, string right) =>
				SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, Q(leftQ), SyntaxFactory.IdentifierName(right));
			static InvocationExpressionSyntax Call(ExpressionSyntax target, params ExpressionSyntax[] args) =>
				SyntaxFactory.InvocationExpression(target, Keysharp.Scripting.Parser.CreateArgumentList(args)); // VisitorHelper
			static LiteralExpressionSyntax S(string s) =>
				SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(s));
			static LiteralExpressionSyntax N(int i) =>
				SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i));
			static ExpressionSyntax Not(ExpressionSyntax e) =>
				SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, e);
			static BinaryExpressionSyntax AndAlso(ExpressionSyntax a, ExpressionSyntax b) =>
				SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, a, b);
			static ExpressionSyntax Plus(ExpressionSyntax a, ExpressionSyntax b) =>
				SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, a, b);

			// common symbols
			var mainScriptId = SyntaxFactory.IdentifierName(mainScriptVarName);                       // MainScript
			var autoExecId = SyntaxFactory.IdentifierName(autoExecName);                            // AutoExecSection
			var aScriptName = SMA("Keysharp.Core.Accessors", "A_ScriptName");                         // Accessors.A_ScriptName
			var suppressDialog = MA(mainScriptId, "SuppressErrorOccurredDialog");                       // MainScript.SuppressErrorOccurredDialog

			// ---- try { ... } ----
			tryStatements ??= new List<StatementSyntax>();

			// if (Script.HandleSingleInstance(A_ScriptName, eScriptInstance.<member>)) { return 0; }
			var singleInstanceGate = SyntaxFactory.IfStatement(
				Call(
					SMA("Keysharp.Scripting.Script", "HandleSingleInstance"),
					aScriptName,
					SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						Q("Keysharp.Scripting.eScriptInstance"),
						SyntaxFactory.IdentifierName(singleInstanceMemberName)
					)
				),
				SyntaxFactory.ReturnStatement(N(0))
			);
			tryStatements.Add(singleInstanceGate);

			// Keysharp.Core.Env.HandleCommandLineParams(args);
			tryStatements.Add(
				SyntaxFactory.ExpressionStatement(
					Call(SMA("Keysharp.Core.Env", "HandleCommandLineParams"), SyntaxFactory.IdentifierName("args"))
				)
			);

			// MainScript.CreateTrayMenu();
			tryStatements.Add(
				SyntaxFactory.ExpressionStatement(
					Call(MA(mainScriptId, "CreateTrayMenu"))
				)
			);

			// MainScript.RunMainWindow(A_ScriptName, AutoExecSection, false);
			tryStatements.Add(
				SyntaxFactory.ExpressionStatement(
					Call(MA(mainScriptId, "RunMainWindow"),
						 aScriptName,
						 autoExecId,
						 SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
				)
			);

			// MainScript.WaitThreads();
			tryStatements.Add(
				SyntaxFactory.ExpressionStatement(
					Call(MA(mainScriptId, "WaitThreads"))
				)
			);

			var tryBlock = SyntaxFactory.Block(tryStatements);

			// ---- catch (Keysharp.Core.Flow.UserRequestedExitException) { } ----
			var catchUserRequestedExit =
				SyntaxFactory.CatchClause()
					.WithDeclaration(SyntaxFactory.CatchDeclaration(Q("Keysharp.Core.Flow.UserRequestedExitException")))
					.WithBlock(SyntaxFactory.Block());

			// ---- catch (Keysharp.Core.Error kserr) { ... } ----
			var kserrId = SyntaxFactory.Identifier("kserr");

			// if (!kserr.Processed) Keysharp.Core.Errors.ErrorOccurred(kserr);
			var processedExpr = MA(SyntaxFactory.IdentifierName(kserrId), "Processed");
			var handledExpr = MA(SyntaxFactory.IdentifierName(kserrId), "Handled");

			var errorOccurredCall = Call(SMA("Keysharp.Core.Errors", "ErrorOccurred"), SyntaxFactory.IdentifierName(kserrId));
			var errorOccurredStmt = SyntaxFactory.ExpressionStatement(errorOccurredCall);

			var ifNotProcessed = SyntaxFactory.IfStatement(Not(processedExpr), errorOccurredStmt);

			// if (!kserr.Handled && !MainScript.SuppressErrorOccurredDialog) MsgBox("Uncaught Keysharp exception:\r\n" + kserr, A_ScriptName + ": Unhandled exception", "iconx");
			var showKeysharpMsgCond = AndAlso(Not(handledExpr), Not(suppressDialog));
			var keysharpMsg =
				Plus(S("Uncaught Keysharp exception:\r\n"), SyntaxFactory.IdentifierName(kserrId));
			var keysharpTitle =
				Plus(aScriptName, S(": Unhandled exception"));
			var msgBoxKeysharp = SyntaxFactory.ExpressionStatement(
				Call(SMA("Keysharp.Core.Dialogs", "MsgBox"), keysharpMsg, keysharpTitle, S("iconx"))
			);
			var ifShowKeysharpMsg = SyntaxFactory.IfStatement(showKeysharpMsgCond, msgBoxKeysharp);

			// ExitApp(1);
			var exitApp1 = SyntaxFactory.ExpressionStatement(
				Call(SMA("Keysharp.Core.Flow", "ExitApp"), N(1))
			);

			var catchKserr =
				SyntaxFactory.CatchClause()
					.WithDeclaration(SyntaxFactory.CatchDeclaration(Q("Keysharp.Core.Error"), kserrId))
					.WithBlock(SyntaxFactory.Block(ifNotProcessed, ifShowKeysharpMsg, exitApp1));

			// ---- catch (System.Exception mainex) { ... } ----
			var mainexId = SyntaxFactory.Identifier("mainex");

			// var ex = mainex.InnerException ?? mainex;
			var exDecl = SyntaxFactory.LocalDeclarationStatement(
				SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
					.WithVariables(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("ex"))
								.WithInitializer(
									SyntaxFactory.EqualsValueClause(
										SyntaxFactory.BinaryExpression(
											SyntaxKind.CoalesceExpression,
											MA(SyntaxFactory.IdentifierName(mainexId), "InnerException"),
											SyntaxFactory.IdentifierName(mainexId)
										)
									)
								)
						)
					)
			);

			// if (ex is Keysharp.Core.Error kserr) { ... } else if (!MainScript.SuppressErrorOccurredDialog) { MsgBox(...); }
			var isKsErrorPattern = SyntaxFactory.IsPatternExpression(
				SyntaxFactory.IdentifierName("ex"),
				SyntaxFactory.DeclarationPattern(
					Q("Keysharp.Core.Error"),
					SyntaxFactory.SingleVariableDesignation(kserrId)
				)
			);

			var ifIsKeysharpError = SyntaxFactory.IfStatement(
				isKsErrorPattern,
				SyntaxFactory.Block(ifNotProcessed, ifShowKeysharpMsg)
			);

			// else if (!MainScript.SuppressErrorOccurredDialog) { MsgBox("Uncaught exception:\r\n" + "Message: " + ex.Message + "\r\nStack: " + ex.StackTrace, A_ScriptName + ": Unhandled exception", "iconx"); }
			var genericMsg =
				Plus(S("Uncaught exception:\r\n"),
					Plus(S("Message: "),
						Plus(MA(SyntaxFactory.IdentifierName("ex"), "Message"),
							Plus(S("\r\nStack: "),
								MA(SyntaxFactory.IdentifierName("ex"), "StackTrace")))));

			var msgBoxElse = SyntaxFactory.ExpressionStatement(
				Call(SMA("Keysharp.Core.Dialogs", "MsgBox"), genericMsg, keysharpTitle, S("iconx"))
			);

			var elseIfShowGeneric = SyntaxFactory.ElseClause(
				SyntaxFactory.IfStatement(Not(suppressDialog), msgBoxElse)
			);

			var ifIsKsErrorWithElse = ifIsKeysharpError.WithElse(elseIfShowGeneric);

			var exitApp2 = SyntaxFactory.ExpressionStatement(Call(SMA("Keysharp.Core.Flow", "ExitApp"), N(1)));

			var catchMainEx =
				SyntaxFactory.CatchClause()
					.WithDeclaration(SyntaxFactory.CatchDeclaration(Q("System.Exception"), mainexId))
					.WithBlock(SyntaxFactory.Block(exDecl, ifIsKsErrorWithElse, exitApp2));

			// try/catches
			var tryStatement = SyntaxFactory.TryStatement(tryBlock, SyntaxFactory.List(new[] { catchUserRequestedExit, catchKserr, catchMainEx }), default);

			// return System.Environment.ExitCode;
			var returnExitCode = SyntaxFactory.ReturnStatement(MA(MA(Q("System"), "Environment"), "ExitCode"));

			return SyntaxFactory.Block(
				tryStatement,
				returnExitCode);
		}

		public override SyntaxNode VisitSourceElements([NotNull] SourceElementsContext context)
        {
            parser.autoExecFunc.Body.AddRange(HandleSourceElements(context.sourceElement()));
            return parser.mainClass.Declaration;
        }

        public override SyntaxNode VisitStatementList(StatementListContext context)
        {
			return SyntaxFactory.Block(new SyntaxList<StatementSyntax>(HandleSourceElements(context.sourceElement())));
        }

        private List<StatementSyntax> HandleSourceElements(SourceElementContext[] sourceElements)
        {
			// Collect all visited statements
			var statements = new List<StatementSyntax>();
			StatementContext stmt;

            for (int i = 0; i < sourceElements.Length; i++)
			{
				var child = sourceElements[i];
				SyntaxNode visited;
				stmt = child.statement();

				if (statements.Count > 0
					&& stmt != null
					&& stmt.iterationStatement() != null
					&& statements[^1] is LabeledStatementSyntax lss)
				{
					parser.LoopStack.Push(new Parser.Loop()
					{
						Label = lss.Identifier.Text,
						IsInitialized = false,
					});
					BlockSyntax result = (BlockSyntax)Visit(stmt);
					visited = result.WithStatements(result.Statements.Insert(0, lss));
					statements.RemoveAt(statements.Count - 1);
				}
				else
					visited = Visit(child);

				if (visited == null)
				{
					if (stmt != null && stmt.variableStatement() != null)
						continue;

                    if (child.positionalDirective() == null
                        && child.remap() == null
                        && child.hotkey() == null
                        && child.hotstring() == null)
                        throw new NoNullAllowedException();
                    else
                    {
                        if (parser.functionDepth > 0 || (parser.currentClass != null && parser.currentClass != parser.mainClass))
                            throw new ParseException("Directives, remaps, hotkeys, and hotstrings cannot be declared inside functions and classes", child);
                        continue;
                    }
				}
				if (visited is BlockSyntax block)
				{
					if (block.GetAnnotatedNodes("MergeStart").FirstOrDefault() != null)
						statements = block.WithoutAnnotations("MergeStart").Statements.Concat(statements).ToList();
					else if (block.GetAnnotatedNodes("MergeEnd").FirstOrDefault() != null)
						statements.AddRange(block.WithoutAnnotations("MergeEnd").Statements);
					else
						statements.Add(EnsureStatementSyntax(visited));
				}
				else if (visited is MemberDeclarationSyntax memberDeclarationSyntax)
				{
					// This shouldn't happen anywhere else but the auto-execute section
					// when the function declaration is inside a block for example
					parser.mainClass.Body.Add(memberDeclarationSyntax);
				}
				else if (visited is ClassDeclarationSyntax classDeclarationSyntax)
				{
					// In case a class is declared inside a function (such as some unit tests)
					parser.mainClass.Body.Add(classDeclarationSyntax);
				}
				else
					statements.Add(EnsureStatementSyntax(visited));
			}

			// Return the statements as a BlockSyntax
			return statements;
		}

        public override SyntaxNode VisitSourceElement([NotNull] SourceElementContext context)
        {
            return base.VisitSourceElement(context);
        }

        public override SyntaxNode VisitStatement([NotNull] MainParser.StatementContext context)
        {
            if (context.iterationStatement() != null)
            {
                Parser.Loop loop;
				parser.loopDepth++;
                if (parser.LoopStack.Count > 0 && !(loop = parser.LoopStack.Peek()).IsInitialized)
                {
                    loop.IsInitialized = true;
                } else {
                    loop = new Parser.Loop()
                    {
                        Label = parser.loopDepth.ToString(),
                        IsInitialized = true,
                    };
                    parser.LoopStack.Push(loop);
                }

                BlockSyntax result = (BlockSyntax)Visit(context.iterationStatement());

                parser.loopDepth--;
                _ = parser.LoopStack.Pop();
                return result;
            }

			return Visit(context.GetChild(0));
		}

        // This should always return the identifier in the exact case needed
        // Built-in properties: a_scriptdir -> A_ScriptDir
        // Variables are turned lowercase: HellO -> hello
        // StaticToken variables get the function name added in upper-case: a -> FUNCNAME_a
        // Special keywords do not get @ added here
        public override SyntaxNode VisitIdentifier([NotNull] IdentifierContext context)
        {
            var text = context.GetText();

            switch (text.ToLowerInvariant())
            {
                case "this":
                    return PredefinedKeywords.This;
                case "base":
                    return parser.currentClass.Name == Keywords.MainClassName ? SyntaxFactory.IdentifierName("@base") : SyntaxFactory.BaseExpression();
                case "super":
                    return parser.CreateSuperTuple();
                case "null":
					if (parser.hasVisitedIdentifiers && parser.IsVariableDeclared("@null", false) == null)
					{
                        return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
					}
                    break;
			}

            // Handle special built-ins
            if (parser.IsVarDeclaredGlobally(text) == null)
            {
                switch (text.ToLowerInvariant())
                {
                    case "a_linenumber":
                        var contextLineNumber = context.Start.Line;
                        return SyntaxFactory.CastExpression(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword)),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(contextLineNumber))
                        );
                    case "a_linefile":
                        string file = context.Start.InputStream.SourceName;
						return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(File.Exists(file) ? Path.GetFullPath(file) : file));
                }
            }

            return HandleIdentifierName(text);
        }

        private SyntaxNode HandleIdentifierName(string text)
        {
            text = parser.NormalizeFunctionIdentifier(text);

            parser.MaybeAddClassStaticVariable(text, false); // This needs to be before FuncObj one, because "object" can be both
            parser.MaybeAddGlobalFuncObjVariable(text, false);

            var vr = parser.IsVarRef(text);
            if (vr != null)
            {
                var debug = parser.currentFunc;
                // If it's a VarRef, access the __Value member
                return ((InvocationExpressionSyntax)InternalMethods.GetPropertyValue)
                .WithArgumentList(
					CreateArgumentList(
					    SyntaxFactory.IdentifierName(vr),
                        SyntaxFactory.LiteralExpression(
							SyntaxKind.StringLiteralExpression,
							SyntaxFactory.Literal("__Value")
						)
                    )
                );
            }

            return SyntaxFactory.IdentifierName(text);
        }

        public override SyntaxNode VisitReservedWord([NotNull] ReservedWordContext context)
        {
            return SyntaxFactory.IdentifierName(context.GetText().ToLowerInvariant());
        }

        public override SyntaxNode VisitKeyword([NotNull] KeywordContext context)
        {
            return HandleIdentifierName(context.GetText().ToLower());
        }

        public override SyntaxNode VisitIdentifierName([NotNull] IdentifierNameContext context)
        {
            if (context.identifier() != null)
                return VisitIdentifier(context.identifier());
            return SyntaxFactory.IdentifierName(context.GetText().ToLower());
        }

        public override SyntaxNode VisitVarRefExpression([NotNull] VarRefExpressionContext context)
        {
            // Visit the singleExpression to determine its type
            var targetExpression = Visit(context.primaryExpression()) as ExpressionSyntax;

            if (targetExpression == null)
                throw new InvalidOperationException("Unsupported singleExpression type for VarRefExpression.");

            return parser.ConstructVarRef(targetExpression);
        }


        public override SyntaxNode VisitDynamicIdentifierExpression([NotNull] DynamicIdentifierExpressionContext context)
        {
            return Visit(context.dynamicIdentifier());
        }

        public override SyntaxNode VisitDynamicIdentifier([NotNull] DynamicIdentifierContext context)
        {
            var dynVar = (ExpressionSyntax)CreateDynamicVariableString(context);

            parser.currentFunc.HasDerefs = true;

            // In this case we have a identifier composed of identifier parts and dereference expressions
            // such as a%b%. CreateDynamicVariableAccessor will return string.Concat<object>(new object[] {"a", b), so
            // to turn it into an identifier we need to wrap it in Keysharp.Scripting.Script.Vars[]
            return SyntaxFactory.ElementAccessExpression(
                parser.currentFunc.Name == Keywords.AutoExecSectionName 
                    ? SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName(Keywords.MainScriptVariableName),
                        SyntaxFactory.IdentifierName("Vars")
                      )
                    : SyntaxFactory.IdentifierName(InternalPrefix + "Derefs")
                ,
                SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(dynVar)
                    )
                )
            );
        }

        public override SyntaxNode VisitVariableStatement([NotNull] VariableStatementContext context)
        {
            var prevScope = parser.currentFunc.Scope;
            
            switch (context.GetChild(0).GetText().ToLower())
            {
                case "local":
                    parser.currentFunc.Scope = eScope.Local;
                    break;
                case "global":
                    parser.currentFunc.Scope = eScope.Global;
                    break;
                case "static":
                    parser.currentFunc.Scope = eScope.Static;
                    break;
            }

            if (context.variableDeclarationList() != null && context.variableDeclarationList().ChildCount > 0) {
                var result = VisitVariableDeclarationList(context.variableDeclarationList());
                parser.currentFunc.Scope = prevScope;
                return result;
            }

            if (prevScope == eScope.Global && prevScope != parser.currentFunc.Scope)
                throw new Error("Multiple differing scope declarations are not allowed");

            // Do nothing, but don't return null
            return SyntaxFactory.Block().WithAdditionalAnnotations(new SyntaxAnnotation("MergeEnd"));
        }

        public override SyntaxNode VisitVariableDeclarationList([NotNull] VariableDeclarationListContext context)
        {
            var declarations = new List<StatementSyntax>();

            foreach (var variableDeclaration in context.variableDeclaration())
            {
                var declaration = (ExpressionStatementSyntax)Visit(variableDeclaration);
                if (declaration != null)
                    declarations.Add(declaration);
            }

            return SyntaxFactory.Block(declarations).WithAdditionalAnnotations(new SyntaxAnnotation("MergeEnd"));
        }

        public override SyntaxNode VisitVariableDeclaration([NotNull] VariableDeclarationContext context)
        {
            SyntaxNode node = Visit(context.assignable());
            if (!(node is IdentifierNameSyntax))
            {
                throw new Error();
            }
            IdentifierNameSyntax identifier = (IdentifierNameSyntax)node;

            var name = identifier.Identifier.Text;

            parser.MaybeAddVariableDeclaration(name);

            if (parser.currentFunc.Scope == eScope.Global)
            {
                parser.currentFunc.Locals.Remove(name);
                parser.currentFunc.Globals.Add(name);
                if (context.assignmentOperator() != null)
                {
                    var initializerValue = (ExpressionSyntax)Visit(context.expression());

                    return SyntaxFactory.ExpressionStatement(HandleAssignment(
                        identifier,
                        initializerValue,
                        context.assignmentOperator().GetText()));
                }
            } else if (parser.currentFunc.Scope == eScope.Local) {
                parser.currentFunc.Globals.Remove(name);
                // MaybeAddVariableDeclaration added the Locals entry
            }

            // Check if there is an initializer (e.g., ':= singleExpression')
            if (context.assignmentOperator() != null)
            {
                var initializerValue = (ExpressionSyntax)Visit(context.expression());

                if (parser.currentFunc.Scope == eScope.Static)
                {
                    return parser.CreateStaticVariableInitializer(identifier, initializerValue);
                }

                return SyntaxFactory.ExpressionStatement(HandleAssignment(
                    identifier,
                    initializerValue,
                    context.assignmentOperator().GetText()));
            }
            else if (context.op != null)
            {
                return SyntaxFactory.ExpressionStatement(HandleCompoundAssignment(identifier, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1L)), context.op.Text == "++" ? "+=" : "-=", isPostFix: true));
            }

            // Return null if no assignment is needed
            return null;
        }


        public override SyntaxNode VisitArguments([NotNull] ArgumentsContext context)
        {

            var arguments = new List<SyntaxNode>();
            bool lastIsComma = true;
            bool containsSpread = false;
            int lastDefinedElement = 0;
            for (var i = 0; i < context.ChildCount; i++)
            {
                var child = context.GetChild(i);
                bool isComma = false;
                if (child is ITerminalNode node)
                {
                    if (node.Symbol.Type == MainParser.EOL || node.Symbol.Type == MainParser.WS)
                        continue;
                    else if (node.Symbol.Type == MainParser.Comma)
                        isComma = true;
                }

                if (isComma)
                {
                    if (lastIsComma)
                        arguments.Add(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));

                    goto ShouldVisitNextChild;
                }
                SyntaxNode arg = VisitArgument((ArgumentContext)child);
                if (arg != null)
                {
                    if (arg is ExpressionSyntax)
                        arguments.Add(arg);
                    else if (arg is SpreadElementSyntax)
                    {
                        arguments.Add(arg);
                        containsSpread = true;
                    }
                    else
                        throw new Error("Unknown argument type");

                    lastDefinedElement = arguments.Count;
                }
                else
                    throw new Error("Unknown function argument");

                ShouldVisitNextChild:
                lastIsComma = isComma;
            }

            if (arguments.Count > lastDefinedElement)
                arguments.RemoveRange(lastDefinedElement, arguments.Count - lastDefinedElement);

            if (!containsSpread)
            {
                // No spread elements present, wrap all elements in ArgumentSyntax and return as ArgumentListSyntax
                return CreateArgumentList(arguments);
            }

            // If spread elements are present, convert all elements into CollectionElements
            var collectionElements = new List<CollectionElementSyntax>();

            foreach (var node in arguments)
            {
                if (node is ExpressionSyntax expr)
                {
                    collectionElements.Add(SyntaxFactory.ExpressionElement(expr));
                }
                else if (node is SpreadElementSyntax spread)
                {
                    collectionElements.Add(spread);
                }
            }

            // Create a CollectionExpressionSyntax
            var collectionExpression = SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList(collectionElements));

            // Wrap in a single argument and return
            return CreateArgumentList(collectionExpression);
        }

        public override SyntaxNode VisitArgument([NotNull] ArgumentContext context)
        {
            ExpressionSyntax arg = null;
            if (context.expression() != null)
                arg = (ExpressionSyntax)Visit(context.expression());

            if (arg != null)
            {
                if (context.Multiply() == null)
                    return arg;
                else
                {
                    var invocationExpression = ((InvocationExpressionSyntax)InternalMethods.FlattenParam)
                        .WithArgumentList(
						    CreateArgumentList(arg) // Passing `arg` as the function argument
                        );

                    // Add the spread operator `..`
                    return SyntaxFactory.SpreadElement(invocationExpression);
                }
            }

            throw new Error("VisitArgument failed: unknown context type");
        }
        public override SyntaxNode VisitAssignmentOperator([NotNull] AssignmentOperatorContext context)
        {
            //Console.WriteLine("AssignmentOperator: " + context.GetText());
            return base.VisitAssignmentOperator(context);
        }

        public override SyntaxNode VisitBoolean([NotNull] BooleanContext context)
        {
            bool.TryParse(context.GetText(), out bool result);
            return SyntaxFactory.LiteralExpression(
                result ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression
            );
        }

        public override SyntaxNode VisitLiteral([NotNull] LiteralContext context)
        {
            if (context.NullLiteral() != null || context.Unset() != null)
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.NullLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.NullKeyword)
                );
            }
            else if (context.boolean() != null)
            {
                return Visit(context.boolean());
            }
            else if (context.StringLiteral() != null)
            {
                var str = context.StringLiteral().GetText();
                str = str.Substring(1, str.Length - 2); // Remove quotes
                if (str.Contains('\n') || str.Contains('\r'))
                {
                    List<string> processedSections = new();
                    int currentLine = context.Start.Line;

                    using (var reader = new StringReader(str))
                    {
                        StringBuilder sectionBuilder = new();
                        bool inContinuation = false; // Track whether we're inside a continuation section

                        while (reader.Peek() != -1)
                        {
                            var line = reader.ReadLine() ?? "";

                            // Check for the start of a continuation section
                            var trimmedLine = line.Trim();
                            if (trimmedLine.StartsWith("("))
                            {
                                sectionBuilder.AppendLine(trimmedLine);
                                inContinuation = true;
                            }
                            // Check for the end of a continuation section
                            else if (inContinuation && trimmedLine == ")")
                            {
                                sectionBuilder.Append(trimmedLine);
                                inContinuation = false;

                                var newstr = sectionBuilder.ToString();

                                processedSections.Add(MultilineString(sectionBuilder.ToString(), currentLine, "TODO"));
                                sectionBuilder.Clear();

                                continue; // Skip this line
                            }
                            else if (inContinuation)
                                sectionBuilder.AppendLine(line);
                        }
                    }

                    // Combine all processed sections
                    str = string.Join("", processedSections);
                }
                
                str = EscapedString(str, false);

                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(str)
                );
            }
            else if (context.numericLiteral() != null)
            {
                return NumericLiteralExpression(context.numericLiteral().GetText());
            }
            else if (context.bigintLiteral() != null)
            {
                var value = context.bigintLiteral().GetText();
                if (long.TryParse(value.TrimEnd('n'), out long bigint))
                {
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(bigint)
                    );
                }
                throw new ValueError($"Invalid bigint literal: {value}");
            }

            throw new ValueError($"Unknown literal: {context.GetText()}");
        }

        public override SyntaxNode VisitNumericLiteral([NotNull] NumericLiteralContext context)
        {
            return NumericLiteralExpression(context.GetText());
        }

        public override SyntaxNode VisitObjectLiteral([NotNull] ObjectLiteralContext context)
        {
            // Collect the property assignments
            var properties = new List<ExpressionSyntax>();
            foreach (var propertyAssignmentContext in context.propertyAssignment())
            {
                if (propertyAssignmentContext == null) continue;
                // Visit the property assignment to get key-value pairs
                var initializer = (InitializerExpressionSyntax)Visit(propertyAssignmentContext);
                properties.AddRange(initializer.Expressions);
            }

            // Create the object[] array
            var arrayExpression = SyntaxFactory.ArrayCreationExpression(
                SyntaxFactory.ArrayType(
                    SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object),
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
                    SyntaxFactory.SeparatedList(properties)
                )
            );

			return SyntaxFactory.ObjectCreationExpression(
	            CreateQualifiedName("KeysharpObject"),
				CreateArgumentList(arrayExpression),
	            null // No object initializers
            );
        }

        public override SyntaxNode VisitPropertyExpressionAssignment([NotNull] PropertyExpressionAssignmentContext context)
        {
            // Visit propertyName and singleExpression
            var propertyIdentifier = Visit(context.memberIdentifier());
            if (propertyIdentifier is IdentifierNameSyntax memberIdentifierName)
            {
                propertyIdentifier = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(memberIdentifierName.Identifier.Text));
            }
            // Keysharp.Scripting.Script.Vars[expression] should extract expression
            else if (propertyIdentifier is ElementAccessExpressionSyntax memberElementAccess)
            {
                propertyIdentifier = memberElementAccess.ArgumentList.Arguments.FirstOrDefault().Expression;
            }
            else
                throw new Error("Invalid property name expression identifier");

            var propertyValue = (ExpressionSyntax)Visit(context.expression());

            // Return an initializer combining the property name and value
            return SyntaxFactory.InitializerExpression(
                SyntaxKind.ComplexElementInitializerExpression,
                SyntaxFactory.SeparatedList(new[] { (ExpressionSyntax)propertyIdentifier, propertyValue })
            );
        }

        public override SyntaxNode VisitLiteralExpression([NotNull] LiteralExpressionContext context)
        {
            return base.VisitLiteralExpression(context);
        }

		public override SyntaxNode VisitBlockStatement([NotNull] BlockStatementContext context)
		{
			return VisitBlock(context.block());
		}

        public override SyntaxNode VisitBlock([NotNull] BlockContext context)
        {
            if (context.statementList() == null)
                return SyntaxFactory.Block();

            return Visit(context.statementList());
        }

        public override SyntaxNode VisitIfStatement([NotNull] IfStatementContext context)
        {
            var arguments = new List<ExpressionSyntax>() {
                (ExpressionSyntax)Visit(context.singleExpression())
            };
            var argumentList = CreateArgumentList(arguments);

            BlockSyntax ifBlock = (BlockSyntax)Visit(context.flowBlock());
			BlockSyntax elseProduction = null;

			var ifStatement = SyntaxFactory.IfStatement(
                SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Scripting.Script", "IfTest"),
                    argumentList),
                ifBlock
            );

			if (context.elseProduction() != null)
            {
				elseProduction = ((BlockSyntax)Visit(context.elseProduction()));

                if (elseProduction != null)
                {
					ifStatement = ifStatement.WithElse(
                        SyntaxFactory.ElseClause(
							SyntaxFactory.Token(SyntaxKind.ElseKeyword),
                            elseProduction
                        )
                    );
                }
			}

            return ifStatement;
        }

        public override SyntaxNode VisitReturnStatement([NotNull] ReturnStatementContext context)
        {
            ExpressionSyntax returnExpression;

            if (context.expression() != null)
            {
                returnExpression = (ExpressionSyntax)Visit(context.expression());
            }
            else
            {
                if (parser.currentFunc.Void)
                    return SyntaxFactory.ReturnStatement();

                return PredefinedKeywords.DefaultReturnStatement;
            }

            if (parser.currentFunc.Void)
                return SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(
                        EnsureValidStatementExpression(returnExpression)
                    ),
                    SyntaxFactory.ReturnStatement()
                );

            return SyntaxFactory.ReturnStatement(
                PredefinedKeywords.ReturnToken,
                returnExpression,
                PredefinedKeywords.SemicolonToken);
        }

        public override SyntaxNode VisitThrowStatement([NotNull] ThrowStatementContext context)
        {
            if (context.singleExpression() == null)
                return SyntaxFactory.ThrowStatement(
					SyntaxFactory.Token(SyntaxKind.ThrowKeyword),
					SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName("Error")
                    ),
                    PredefinedKeywords.SemicolonToken
                );

            var expression = (ExpressionSyntax)Visit(context.singleExpression());

            if (expression is LiteralExpressionSyntax)
            {
                // Wrap the literal in Keysharp.Core.Error
                return SyntaxFactory.ThrowStatement(
                    SyntaxFactory.Token(SyntaxKind.ThrowKeyword),
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName("Error"),
						CreateArgumentList(expression)
                    ),
                    PredefinedKeywords.SemicolonToken
                );
            }
            else
                expression = SyntaxFactory.ParenthesizedExpression(expression);

            // Otherwise, return a normal throw statement
            return SyntaxFactory.ThrowStatement(
				SyntaxFactory.Token(SyntaxKind.ThrowKeyword), 
                SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName("KeysharpException"), expression),
                PredefinedKeywords.SemicolonToken
            );
        }

        private SyntaxNode HandleTernaryCondition(ExpressionSyntax condition, ExpressionSyntax trueExpression, ExpressionSyntax falseExpression)
        {
            // Wrap the condition in Keysharp.Scripting.Script.IfTest(condition) to force a boolean
            var wrappedCondition = SyntaxFactory.InvocationExpression(
                CreateMemberAccess("Keysharp.Scripting.Script", "IfTest"),
                CreateArgumentList(condition)
            );

            // Create a ternary conditional expression: condition ? trueExpression : falseExpression
            return SyntaxFactory.ConditionalExpression(
                wrappedCondition,          // The condition, forced to a boolean
                trueExpression,            // Expression for true branch
                falseExpression            // Expression for false branch
            );
        }
        public override SyntaxNode VisitTernaryExpression([NotNull] TernaryExpressionContext context)
        {
            return HandleTernaryCondition((ExpressionSyntax)Visit(context.ternCond), (ExpressionSyntax)Visit(context.ternTrue), (ExpressionSyntax)Visit(context.ternFalse));
        }
        public override SyntaxNode VisitTernaryExpressionDuplicate([NotNull] TernaryExpressionDuplicateContext context)
        {
            return HandleTernaryCondition((ExpressionSyntax)Visit(context.ternCond), (ExpressionSyntax)Visit(context.ternTrue), (ExpressionSyntax)Visit(context.ternFalse));
        }

        public override SyntaxNode VisitFormalParameterArg([Antlr4.Runtime.Misc.NotNull] FormalParameterArgContext context)
        {
            // Treat as a regular parameter
            var parameterName = parser.ToValidIdentifier(context.identifier().GetText().Trim().ToLowerInvariant());

            ParameterSyntax parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
					.WithType(PredefinedKeywords.ObjectType);

			if (context.BitAnd() != null)
            {
                parser.currentFunc.VarRefs.Add(parameterName);
				parameter = parameter
                    .WithAttributeLists(SyntaxFactory.SingletonList(
                        SyntaxFactory.AttributeList(
                            SyntaxFactory.SeparatedList(new[] {
                                SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("ByRef"))
                            })
                        )
                    ));
			}

            // Handle default value assignment (:=) or optional parameter (QuestionMark)
            if (context.expression() != null)
            {
                var defaultValue = (ExpressionSyntax)Visit(context.expression());

                // Add [Optional] and [DefaultParameterValue(defaultValue)] attributes
                parameter = parser.AddOptionalParamValue(parameter, defaultValue);
            }
            // Handle optional parameter
            else if (context.QuestionMark() != null)
            {
                // If QuestionMark is present, mark the parameter as optional with null default value
                parameter = parser.AddOptionalParamValue(parameter, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            }

            return parameter;
        }

        public override SyntaxNode VisitLastFormalParameterArg([NotNull] LastFormalParameterArgContext context)
        {
            ParameterSyntax parameter;
            if (context.Multiply() != null)
            {
                var parameterName = parser.ToValidIdentifier(context.identifier()?.GetText().ToLowerInvariant().Trim() ?? "args");

                // Handle 'Multiply' for variadic arguments (params object[])
                parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                    .WithType(PredefinedKeywords.ObjectArrayType)
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)));
            }
            else if (context.formalParameterArg() != null)
            {
                parameter = (ParameterSyntax)VisitFormalParameterArg(context.formalParameterArg());
            }
            else
                throw new Error("Unknown last formal parameter type");
            return parameter;
        }

        public override SyntaxNode VisitAssignable([Antlr4.Runtime.Misc.NotNull] AssignableContext context)
        {
            //Console.WriteLine("Assignable: " + context.GetText());
            //Console.WriteLine(context.children[0].GetText());
            if (parser.currentFunc.Scope == eScope.Static)
                return SyntaxFactory.IdentifierName(parser.CreateStaticIdentifier(context.GetText()));
            return base.VisitAssignable(context);
        }

        public override SyntaxNode VisitInitializer([Antlr4.Runtime.Misc.NotNull] InitializerContext context)
        {
            //Console.WriteLine("Initializer: " + context.GetText());
            return base.VisitInitializer(context);
        }

        public override SyntaxNode VisitFunctionStatement([Antlr4.Runtime.Misc.NotNull] FunctionStatementContext context)
        {
            // Visit the singleExpression (the method to be called)
            ExpressionSyntax targetExpression = (ExpressionSyntax)Visit(context.primaryExpression());

            if (!(targetExpression is IdentifierNameSyntax || targetExpression is IdentifierNameSyntax
                || targetExpression is MemberAccessExpressionSyntax 
                || (targetExpression is InvocationExpressionSyntax ies && 
                    ((ies.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text.Equals("GetPropertyValue", StringComparison.OrdinalIgnoreCase))
                    || ies.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.Text.Equals("GetPropertyValue", StringComparison.OrdinalIgnoreCase)
                    ))))
                return SyntaxFactory.ExpressionStatement(EnsureValidStatementExpression(targetExpression));

            string methodName = ExtractMethodName(targetExpression);

            // Get the argument list
            ArgumentListSyntax argumentList;
            if (context.arguments() != null)
                argumentList = (ArgumentListSyntax)VisitArguments(context.arguments());
            else
                argumentList = SyntaxFactory.ArgumentList();

            return SyntaxFactory.ExpressionStatement(parser.GenerateFunctionInvocation(targetExpression, argumentList, methodName));
        }

        private void PushFunction(string funcName, TypeSyntax returnType = null)
        {
            parser.FunctionStack.Push((parser.currentFunc, parser.UserFuncs));

            // Create shallow copy
            parser.UserFuncs = new HashSet<string>(parser.UserFuncs, parser.UserFuncs.Comparer);

            parser.functionDepth++;

            parser.currentFunc = new Function(funcName, returnType);
        }

        private void PopFunction()
        {
            (parser.currentFunc, parser.UserFuncs) = parser.FunctionStack.Pop();
            parser.functionDepth--;
        }

        public override SyntaxNode VisitFunctionHead([NotNull] FunctionHeadContext context)
        {
            string userName = context.identifierName().GetText();
			PushFunction(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(userName));
            parser.currentFunc.UserDeclaredName = userName;

			VisitFunctionHeadPrefix(context.functionHeadPrefix());

            parser.currentFunc.Params.AddRange(((ParameterListSyntax)VisitFormalParameterList(context.formalParameterList())).Parameters);

            return null;
        }

        public override SyntaxNode VisitFunctionHeadPrefix([NotNull] FunctionHeadPrefixContext context)
        {
            parser.currentFunc.Async = context?.Async() != null && context.Async().Length > 0;
            parser.currentFunc.Public = parser.functionDepth < 2;
            parser.currentFunc.Static = !(parser.functionDepth > 1 && (context?.Static() == null || context.Static().Length == 0));
            parser.currentFunc.UserStatic = context?.Static() != null;

			return null;
        }

        public override SyntaxNode VisitFunctionExpressionHead([NotNull] FunctionExpressionHeadContext context)
        {
            if (context.functionHead() != null)
                return Visit(context.functionHead());

            PushFunction(Keywords.AnonymousLambdaPrefix + ++parser.lambdaCount);

            VisitFunctionHeadPrefix(context.functionHeadPrefix());

            parser.currentFunc.Params.AddRange(((ParameterListSyntax)VisitFormalParameterList(context.formalParameterList())).Parameters);

            return null;
        }

        public override SyntaxNode VisitFatArrowExpressionHead([NotNull] FatArrowExpressionHeadContext context)
        {
            if (context.functionExpressionHead() != null)
                return Visit(context.functionExpressionHead());

            PushFunction(Keywords.AnonymousFatArrowLambdaPrefix + ++parser.lambdaCount);
            VisitFunctionHeadPrefix(context.functionHeadPrefix());

            var parameterName = parser.ToValidIdentifier(context.identifierName()?.GetText().ToLowerInvariant().Trim() ?? "args");
            ParameterSyntax parameter;
            if (context.Multiply() != null)
            {
                // Handle 'Multiply' for variadic arguments (params object[])
                parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                    .WithType(PredefinedKeywords.ObjectArrayType)
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)));
            }
            else
            {
                TypeSyntax parameterType;
                if (context.BitAnd() != null)
                {
                    parser.currentFunc.VarRefs.Add(parameterName);
                    parameterType = SyntaxFactory.ParseTypeName("VarRef");
                }
                else
                    parameterType = PredefinedKeywords.ObjectType;

                parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                    .WithType(parameterType);

                if (context.QuestionMark() != null)
                    parameter = parser.AddOptionalParamValue(parameter, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            }

            parser.currentFunc.Params.Add(parameter);

            return null;
        }

        public SyntaxNode FunctionExpressionCommon(MethodDeclarationSyntax methodDeclaration, ParserRuleContext context)
        {
			// Case 1: If we are inside the auto-execute section and this is the only expression in the expression sequence
			// then consider it a top-level function. The method declaration will be added to the main
			// class in VisitExpressionSequence.
			if (parser.currentFunc.Name == Keywords.AutoExecSectionName &&
				context.Parent is ExpressionSequenceContext esc && esc.Parent is ExpressionStatementContext && esc.ChildCount == 1)
			{
				return methodDeclaration;
			}
			// Case 2: If we are in the main class (not inside a class declaration)
			// OR we are inside any method declaration besides the auto-execute one then add it as a closure.
			// Function expressions inside the auto-execute section are added as static nested functions.
			if (parser.currentClass.Name == Keywords.MainClassName || parser.currentFunc.Name != Keywords.AutoExecSectionName)
            {
                var methodName = methodDeclaration.Identifier.Text;
                var variableName = methodName.ToLowerInvariant();

                var modifiers = methodDeclaration.Modifiers;

                // Function expressions in AutoExec section should be static
                if (parser.currentFunc.Name == Keywords.AutoExecSectionName)
                {
                    // Remove 'public' from the existing modifiers
                    var updatedModifiers = modifiers
                        .Where(m => !m.IsKind(SyntaxKind.PublicKeyword))
                        .ToList();

                    // Ensure 'static' is included if not already present
                    if (!updatedModifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                        updatedModifiers.Add(Parser.PredefinedKeywords.StaticToken);

                    modifiers = SyntaxFactory.TokenList(updatedModifiers);

                    // Modify the top-level field declaration to not assign the closure
                    var mainClassBody = parser.mainClass.Body;
					for (int i = 0; i < mainClassBody.Count; i++)
					{
						if (mainClassBody[i] is FieldDeclarationSyntax fds && fds.Declaration.Variables.First().Identifier.Text == variableName)
						{
							var declarator = fds.Declaration.Variables.First();
							mainClassBody[i] = fds.ReplaceNode(
								declarator.Initializer.Value,
								SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
							);
							break;
						}
					}
				}

                // Create a delegate or closure and add it to the current function's body
                var delegateSyntax = SyntaxFactory.LocalFunctionStatement(
                        methodDeclaration.ReturnType,
                        methodDeclaration.Identifier)
                    .WithParameterList(methodDeclaration.ParameterList)
                    .WithBody(methodDeclaration.Body)
                    .WithModifiers(modifiers);

                var isStatic = modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

				InvocationExpressionSyntax funcObj = CreateFuncObj(
                    SyntaxFactory.CastExpression(
                        CreateQualifiedName("System.Delegate"),
                        SyntaxFactory.IdentifierName(methodName)
                    ),
                    !isStatic
                );

                parser.currentFunc.Body.Add(delegateSyntax);

                // If we are creating a closure in the auto-execute section then add a global
                // variable for the delegate and assign it's value at the beginning of AutoExecSection
                if (parser.currentFunc.Name == Keywords.AutoExecSectionName)
                {
                    parser.MaybeAddGlobalVariableDeclaration(variableName, true);
                    parser.currentFunc.Body.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(variableName), // Target variable
							PredefinedKeywords.EqualsToken,
							funcObj // Value to assign
                        )
                    ));
                }
                else if (isStatic)
                {
					ExpressionSyntax initializerValue = CreateFuncObj(
	                    SyntaxFactory.CastExpression(
							CreateQualifiedName("System.Delegate"),
		                    SyntaxFactory.IdentifierName(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(methodName))
	                    )
                    );

                    variableName = parser.CreateStaticIdentifier(variableName);

					parser.currentFunc.Body.Add(parser.CreateStaticVariableInitializer(
						SyntaxFactory.IdentifierName(variableName),
						initializerValue));
				}
                else
                {
                    var nullVariableDeclaration = SyntaxFactory.LocalDeclarationStatement(
                        CreateNullObjectVariable(variableName)
                    );

                    // Add the variable declaration to the beginning of the current function body
                    parser.currentFunc.Locals[variableName] = nullVariableDeclaration;

                    // Add the assignment statement to the `statements` list
                    parser.currentFunc.Body.Add(SyntaxFactory.ExpressionStatement(
                        CreateSimpleAssignment(variableName, funcObj)
                    ));
                }

                // Return the FuncObj variable wrapping the delegate
                return SyntaxFactory.IdentifierName(variableName).WithAdditionalAnnotations(new SyntaxAnnotation("FunctionDeclaration"));
            }
            else

            // Case 2: If inside a class declaration and not inside a method, for example
            // a class field is being assigned a fat arrow function
            {
                // Transform the method into an anonymous lambda function
                var isAsync = methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword);

                // Wrap the method body in a lambda
                var lambdaExpression = SyntaxFactory.ParenthesizedLambdaExpression()
                    .WithAsyncKeyword(isAsync ? SyntaxFactory.Token(SyntaxKind.AsyncKeyword) : default)
                    .WithParameterList(methodDeclaration.ParameterList)
                    .WithBlock(methodDeclaration.Body);

                // Return the Func invocation
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName("Func"),
                    CreateArgumentList(lambdaExpression)
                );
            }
        }

        public override SyntaxNode VisitFunctionDeclaration([NotNull] FunctionDeclarationContext context)
        {
            Visit(context.functionHead());

            BlockSyntax functionBody = (BlockSyntax)VisitFunctionBody(context.functionBody());
            parser.currentFunc.Body.AddRange(functionBody.Statements.ToArray());

            /*
            if (parser.functionDepth > 1)
            {
                var block = SyntaxFactory.Block(
                    SyntaxFactory.LocalDeclarationStatement(
                        CreateFuncObjDelegateVariable(parser.currentFunc.Name)
                    ),
                    SyntaxFactory.LocalFunctionStatement(
                        SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // Assuming return type is void
                        SyntaxFactory.Identifier(parser.currentFunc.Name) // Function name
                    )
                    .WithParameterList(parser.currentFunc.Params)
                    .WithBody(parser.currentFunc.Body)
                    .WithModifiers(
                    SyntaxFactory.TokenList(
                        Parser.PredefinedKeywords.StaticToken
                    )).WithAdditionalAnnotations(new SyntaxAnnotation("MergeStart"))
                );
                PopFunction();
                return block;
            }
            */

			var methodDeclaration = parser.currentFunc.Assemble();
            PopFunction();

            if (parser.currentFunc.Name == Keywords.AutoExecSectionName)
                return methodDeclaration;

			var commonResult = FunctionExpressionCommon(methodDeclaration, context);
            if (commonResult is IdentifierNameSyntax ins)
            {
                return SyntaxFactory.ExpressionStatement(EnsureValidStatementExpression(ins));
			}
            return commonResult;
		}

        public override SyntaxNode VisitFunctionExpression([NotNull] FunctionExpressionContext context)
        {
            Visit(context.functionExpressionHead());

            HandleScopeFunctions(context.block());

            //VisitVariableStatements(context.block());

            BlockSyntax functionBody = (BlockSyntax)Visit(context.block());
            parser.currentFunc.Body.AddRange(functionBody.Statements.ToArray());

            var methodDeclaration = parser.currentFunc.Assemble();
            PopFunction();

            return FunctionExpressionCommon(methodDeclaration, context);
        }

        public override SyntaxNode VisitFatArrowExpression([Antlr4.Runtime.Misc.NotNull] FatArrowExpressionContext context)
        {
			Visit(context.fatArrowExpressionHead());

			ExpressionSyntax returnExpression = (ExpressionSyntax)Visit(context.expression());

			BlockSyntax functionBody;

			if (parser.currentFunc.Void)
			{
				functionBody = SyntaxFactory.Block(
					SyntaxFactory.ExpressionStatement(EnsureValidStatementExpression(returnExpression)),
					SyntaxFactory.ReturnStatement()
				 );

			}
			else
				functionBody = SyntaxFactory.Block(
					SyntaxFactory.ReturnStatement(
						PredefinedKeywords.ReturnToken,
						returnExpression,
						PredefinedKeywords.SemicolonToken
					)
				);

			parser.currentFunc.Body.AddRange(functionBody.Statements.ToArray());

			var methodDeclaration = parser.currentFunc.Assemble();
			PopFunction();

			return FunctionExpressionCommon(methodDeclaration, context);
		}

        private BlockSyntax EnsureReturnStatement(BlockSyntax functionBody)
        {
            var statements = functionBody.Statements;

            bool hasReturn = statements.OfType<ReturnStatementSyntax>().Any();

            if (!hasReturn)
            {
                statements = statements.Add(PredefinedKeywords.DefaultReturnStatement);
            }

            return SyntaxFactory.Block(statements);
        }

        public override SyntaxNode VisitFormalParameterList([NotNull] FormalParameterListContext context)
        {
            var parameters = new List<ParameterSyntax>();

            if (parser.currentClass.Name != Keywords.MainClassName && parser.FunctionStack.Count == 1 && !parser.currentClass.isInitialization)
            {
                parameters.Add(PredefinedKeywords.ThisParam);
            }

            if (context != null)
            {
                foreach (var formalParameter in context.formalParameterArg())
                {
                    var parameter = (ParameterSyntax)VisitFormalParameterArg(formalParameter);

                    // Add the parameter to the list
                    parameters.Add(parameter);
                }
            }

            // Handle the last formal parameter argument if it exists
            if (context?.lastFormalParameterArg() != null)
            {
                var parameter = (ParameterSyntax)VisitLastFormalParameterArg(context.lastFormalParameterArg());
                if (context.lastFormalParameterArg().Multiply() != null)
                {
                    var identifier = parameter.Identifier.Text;
                    var substitute = Keywords.InternalPrefix + identifier.TrimStart('@');
                    parameter = parameter.WithIdentifier(SyntaxFactory.Identifier(substitute));

                    var statement = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object)
                    )
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(identifier))
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
									PredefinedKeywords.EqualsToken,
									SyntaxFactory.ObjectCreationExpression(
										CreateQualifiedName("Keysharp.Core.Array")
									)
                                    .WithArgumentList(
                                        CreateArgumentList(SyntaxFactory.IdentifierName(substitute))
                                    )
                                )
                            )
                        )
                    )
                );

                    parser.currentFunc.Body.Add(statement);
                }
                parameters.Add(parameter);
            }

            return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
        }

        public void HandleScopeFunctions(ParserRuleContext context)
        {
			var scopeFunctionDeclarations = parser.GetScopeFunctions(context, this);

			foreach (var fi in scopeFunctionDeclarations)
			{
				if (parser.currentFunc.Name != Keywords.AutoExecSectionName)
				{
					if (fi.Static)
					{
						var staticName = parser.CreateStaticIdentifier(fi.Name);
						// Declare the variable in the containing class
						parser.currentFunc.Statics.Add(staticName);
						var prevScope = parser.currentFunc.Scope;
						parser.currentFunc.Scope = eScope.Static;
						parser.AddVariableDeclaration(staticName);
						parser.currentFunc.Scope = prevScope;
					}
					else
					{
						var variableName = fi.Name.ToLowerInvariant();
						var nullVariableDeclaration = SyntaxFactory.LocalDeclarationStatement(
							CreateNullObjectVariable(variableName)
						);
						// Add the variable declaration to the beginning of the current function body
						parser.currentFunc.Locals[variableName] = nullVariableDeclaration;
					}
				}
				else
					parser.UserFuncs.Add(fi.Name);
			}
		}

        public override SyntaxNode VisitFunctionBody([NotNull] FunctionBodyContext context)
        {
            HandleScopeFunctions(context);

			//VisitVariableStatements(context);
            if (context.expression() != null)
            {
                var expression = (ExpressionSyntax)Visit(context.expression());
                if (parser.currentFunc.Void)
                    return SyntaxFactory.Block(
                        SyntaxFactory.ExpressionStatement(EnsureValidStatementExpression(expression)),
                        SyntaxFactory.ReturnStatement()
                    );
                return SyntaxFactory.Block(
                    SyntaxFactory.ReturnStatement(
						PredefinedKeywords.ReturnToken,
						expression,
						PredefinedKeywords.SemicolonToken
					)
                );
            }
            else if (context.block().statementList() == null || context.block().statementList().ChildCount == 0)
            {
                return SyntaxFactory.Block(PredefinedKeywords.DefaultReturnStatement);
            }
            return VisitStatementList(context.block().statementList());
            /*
            var statements = new List<StatementSyntax>();

            foreach (var statementContext in context.statementList().statement())
            {
                var statement = VisitStatement(statementContext);
                if (statement == null)
                    throw new Error("Invalid statement");
                if (statement is BlockSyntax block)
                    statements.AddRange(block.Statements);
                else
                    statements.Add((StatementSyntax)statement);
            }

            return SyntaxFactory.Block(statements);
            */
        }

        public override SyntaxNode VisitArrayLiteral(ArrayLiteralContext context)
        {
            // Visit the arrayElementList to get all the elements
            var elementsArgList = context.arguments() == null
                ? SyntaxFactory.ArgumentList()
                : (ArgumentListSyntax)Visit(context.arguments());
            var elementsInitializer = SyntaxFactory.InitializerExpression(
                    SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList(
                        elementsArgList.Arguments.Select(arg => arg.Expression)
                    )
                );

            // Wrap the array initializer in a call to 'new Keysharp.Core.Array(...)'
            var keysharpArrayCreation = SyntaxFactory.ObjectCreationExpression(
                CreateQualifiedName("Keysharp.Core.Array"), // Class name: Keysharp.Core.Array
                elementsArgList,
                null // No object initializers
            );

            return keysharpArrayCreation;
        }

        public override SyntaxNode VisitMapLiteral([NotNull] MapLiteralContext context)
        {
            ArgumentListSyntax argumentList = (ArgumentListSyntax)Visit(context.mapElementList());

			return SyntaxFactory.ObjectCreationExpression(
				CreateQualifiedName("Keysharp.Core.Map"), // Class name: Keysharp.Core.Map
				argumentList,
				null // No object initializers
			);
        }

        public override SyntaxNode VisitMapElementList([NotNull] MapElementListContext context)
        {
            var expressions = new List<ArgumentSyntax>();

            if (context == null || context.ChildCount == 0)
            {
                return SyntaxFactory.ArgumentList();
            }
            foreach (var mapElementContext in context.mapElement())
            {
                expressions.Add(SyntaxFactory.Argument((ExpressionSyntax)(Visit(mapElementContext.key))));
                expressions.Add(SyntaxFactory.Argument((ExpressionSyntax)(Visit(mapElementContext.value))));
            }

            // Wrap the expressions in an InitializerExpressionSyntax
            return CreateArgumentList(expressions);
        }
    }
}