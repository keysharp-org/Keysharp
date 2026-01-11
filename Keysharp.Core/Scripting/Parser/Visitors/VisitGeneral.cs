using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Keysharp.Core.Common.Invoke;
using Keysharp.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Keysharp.Scripting.Parser;
using static MainParser;

namespace Keysharp.Scripting
{
    internal partial class VisitMain : MainParserBaseVisitor<SyntaxNode>
    {
        public override SyntaxNode VisitHotIfDirective([NotNull] HotIfDirectiveContext context)
        {
            if (context.singleExpression() == null)
            {
                parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
                    ((InvocationExpressionSyntax)InternalMethods.HotIf)
                    .WithArgumentList(
						CreateArgumentList(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("")
                            )
                        )
                    )
                ));
            } else
            {
                var hotIfFunctionName = InternalPrefix + $"HotIf_{++parser.hotIfCount}";

                // Visit the singleExpression and wrap it in an anonymous function
                var conditionExpression = (ExpressionSyntax)Visit(context.singleExpression());
                var hotIfFunction = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // Return type: bool
                        SyntaxFactory.Identifier(hotIfFunctionName) // Function name
                    )
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            Parser.PredefinedKeywords.PublicToken,
                            Parser.PredefinedKeywords.StaticToken
                        )
                    )
					.WithParameterList(
						SyntaxFactory.ParameterList(
							SyntaxFactory.SingletonSeparatedList(
								SyntaxFactory.Parameter(
									SyntaxFactory.Identifier("thishotkey")
								)
								.WithType(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
							)
						)
					)
					.WithBody(
                        SyntaxFactory.Block(
                            SyntaxFactory.SingletonList<StatementSyntax>(
                                SyntaxFactory.ReturnStatement(
                                    PredefinedKeywords.ReturnToken,
                                    conditionExpression,
                                    PredefinedKeywords.SemicolonToken
                                )
                            )
                        )
                    );

                // Add the function declaration to the main class
                parser.mainClass.Body.Add(hotIfFunction);

                // Add the function call to parser.DHHR
                parser.DHHR.Add(
                    SyntaxFactory.ExpressionStatement(
                        ((InvocationExpressionSyntax)InternalMethods.HotIf)
                        .WithArgumentList(
							CreateArgumentList(
								GenerateFuncObjArgument(hotIfFunctionName)
                            )
                        )
                    )
                );
            }

            return null;
        }

        public override SyntaxNode VisitHotstringDirective([NotNull] HotstringDirectiveContext context)
        {
            var invocation = SyntaxFactory.InvocationExpression(
                CreateMemberAccess("Keysharp.Core.Keyboard", "Hotstring")
            );

            if (context.NoMouse() != null)
            {
                parser.DHHR.Insert(0,
                    SyntaxFactory.ExpressionStatement(
                        invocation
                        .WithArgumentList(
							CreateArgumentList(
								SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("MouseReset")),
                                SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                            )
                        )
                    )
                );
            } else if (context.EndChars() != null)
            {
                var endchars = EscapedString(context.HotstringOptions().GetText().Trim(), false);
                parser.DHHR.Insert(0,
                    SyntaxFactory.ExpressionStatement(
                        invocation
                        .WithArgumentList(
							CreateArgumentList(
                                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("EndChars")),
                                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(endchars))
                            )
                        )
                    )
                );
            } 
            else
            {
                parser.DHHR.Add(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.InvocationExpression(
                            CreateMemberAccess("Keysharp.Core.Keyboard", "HotstringOptions")
                        )
                        .WithArgumentList(
							CreateArgumentList(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(context.HotstringOptions().GetText().Trim())))
                        )
                    )
                );
            }
            return null;
        }

        public override SyntaxNode VisitInputLevelDirective([NotNull] InputLevelDirectiveContext context)
        {
            var value = Math.Clamp(context.ChildCount < 2 ? 0 : int.Parse(context.GetChild(1).GetText()), 0, 100);
            var expr = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));
            parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
					CreateMemberAccess("Keysharp.Core.Accessors", "A_InputLevel"),
					PredefinedKeywords.EqualsToken,
					(LiteralExpressionSyntax)expr
                )
            ));
            return null;
        }
        public override SyntaxNode VisitSuspendExemptDirective([NotNull] SuspendExemptDirectiveContext context)
        {
            var value = context.ChildCount < 2
                ? SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))
                : Visit(context.GetChild(1));
            parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
					CreateMemberAccess("Keysharp.Core.KeysharpEnhancements", "A_SuspendExempt"),
					PredefinedKeywords.EqualsToken,
					(LiteralExpressionSyntax)value
                )
            ));
            return null;
        }

        public override SyntaxNode VisitUseHookDirective([NotNull] UseHookDirectiveContext context)
        {
            ExpressionSyntax value = context.ChildCount < 2
                ? SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                : ((InvocationExpressionSyntax)InternalMethods.ForceBool)
                    .WithArgumentList(
						CreateArgumentList((ExpressionSyntax)Visit(context.GetChild(1)))
                    );
            parser.DHHR.Add(SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
					CreateMemberAccess(MainScriptVariableName, "ForceKeybdHook"),
					PredefinedKeywords.EqualsToken,
					value
                )
            ));
            return null;
        }

        public override SyntaxNode VisitHotkey([NotNull] HotkeyContext context)
        {
            parser.persistent = true;
            parser.isHotkeyDefinition = true;
            // Generate a unique function name
            var hotkeyFunctionName = InternalPrefix + $"Hotkey_{++parser.hotkeyCount}";
            MethodDeclarationSyntax hotkeyFunction = null;

            if (context.functionDeclaration() != null)
            {
                hotkeyFunction = (MethodDeclarationSyntax)Visit(context.functionDeclaration());
                hotkeyFunctionName = hotkeyFunction.Identifier.Text;
            }
            else
            {
                PushFunction(hotkeyFunctionName);

                // Visit the statement to generate the function body
                var hotkeyStatement = Visit(context.statement());

                if (hotkeyStatement is BlockSyntax bs)
                    parser.currentFunc.Body.AddRange(bs.Statements);
                else
                    parser.currentFunc.Body.Add((StatementSyntax)hotkeyStatement);

                // Create the hotkey function
                hotkeyFunction = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // Return type: string
                        SyntaxFactory.Identifier(hotkeyFunctionName) // Function name
                    )
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            Parser.PredefinedKeywords.PublicToken,
                            Parser.PredefinedKeywords.StaticToken
                        )
                    )
                    .WithParameterList(
                        SyntaxFactory.ParameterList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Parameter(
                                    SyntaxFactory.Identifier("thishotkey")
                                )
                                .WithType(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
                            )
                        )
                    )
                    .WithBody(parser.currentFunc.AssembleBody());

                PopFunction();

            }

            // Add the hotkey function to the main class
            parser.mainClass.Body.Add(hotkeyFunction);

            // Generate a HotkeyDefinition.AddHotkey call for each trigger
            foreach (var hotkeyTriggerContext in context.HotkeyTrigger())
            {
                var triggerText = hotkeyTriggerContext.GetText();
                triggerText = triggerText.Substring(0, triggerText.Length - 2);
                if (triggerText[^1] == '`' && (triggerText.Length < 2 || triggerText[^2] != '`'))
                    triggerText += '`';
                triggerText = EscapedString(triggerText, true);

                var addHotkeyCall = SyntaxFactory.ExpressionStatement(
                    ((InvocationExpressionSyntax)InternalMethods.AddHotkey)
                    .WithArgumentList(
						CreateArgumentList(
                            GenerateFuncObjArgument(hotkeyFunctionName),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0u)),
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(triggerText) // Trim trailing ::
                            )
                        )
                    )
                );

                // Add the generated statement to the DHHR list
                parser.DHHR.Add(addHotkeyCall);
            }

            parser.isHotkeyDefinition = false;

            return null; // No syntax node is returned
        }

        public override SyntaxNode VisitHotstring([NotNull] HotstringContext context)
        {
            parser.persistent = true;
            // Extract the hotstring triggers
            var triggers = context.HotstringTrigger()
                .Select(triggerContext => EscapedString(triggerContext.GetText()[..^2], true))
                .ToList();

            // Check if it's an expansion or a statement
            bool hasExpansion = context.hotstringExpansion() != null;
            string expansionText = hasExpansion ? context.hotstringExpansion().GetText() : "";
            if (hasExpansion)
            {
                if (context.hotstringExpansion().HotstringSingleLineExpansion() != null)
                    expansionText = EscapedString(expansionText, true);
                else
                    expansionText = EscapedString(MultilineString(expansionText.Trim(), context.hotstringExpansion().Start.Line, "TODO"), true);
            }

            // Generate the function if there's a statement
            string functionName = null;
            if (!hasExpansion)
            {
                functionName = InternalPrefix + $"Hotstring_{++parser.hotstringCount}";

                PushFunction(functionName);

                // Visit the statement to generate the function body
                var statementNode = Visit(context.statement());
                if (statementNode is BlockSyntax bs)
                    parser.currentFunc.Body.AddRange(bs.Statements);
                else
                    parser.currentFunc.Body.Add((StatementSyntax)statementNode);

                // Create the hotstring function
                var hotstringFunction = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object),
                        SyntaxFactory.Identifier(functionName)
                    )
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            Parser.PredefinedKeywords.PublicToken,
                            Parser.PredefinedKeywords.StaticToken
                        )
                    )
                    .WithParameterList(
                        SyntaxFactory.ParameterList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Parameter(
                                    SyntaxFactory.Identifier("thishotkey")
                                )
                                .WithType(SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object))
                            )
                        )
                    )
                    .WithBody(parser.currentFunc.AssembleBody());

                // Add the function to the main class
                parser.mainClass.Body.Add(hotstringFunction);

                PopFunction();
            }

            // Generate the AddHotstring calls
            foreach (var trigger in triggers)
            {
                var colonIndex = trigger.IndexOf(':', 1); // Find the first colon after the initial `:`
                var options = trigger.Substring(1, colonIndex - 1);
                var hotstringKey = trigger.Substring(colonIndex + 1);

                var addHotstringCall = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        CreateMemberAccess("Keysharp.Core.Common.Keyboard.HotstringManager", "AddHotstring")
                    )
                    .WithArgumentList(
						CreateArgumentList(
							SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(trigger)),
                            hasExpansion
                                ? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                                : GenerateFuncObjArgument(functionName),
                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal($"{options}:{hotstringKey}")),
                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(hotstringKey)),
                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(expansionText)),
                            SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                        )
                    )
                );

                // Add the generated statement to the DHHR list
                parser.DHHR.Add(addHotstringCall);
            }

            return null;
        }

        void ParseRemapKey(string remapKey, out string sourceKey, out string targetKey)
        {
            int index = -1;
            bool escape = false;
            int lastIndex = remapKey.Length - 2;

			for (int i = 0; i < remapKey.Length - 1; i++)
            {
                if (i == lastIndex)
                {
                    escape = false;
                }
                else if (remapKey[i] == '`' && remapKey[i + 1] != ':') // Detect escape character
                {
                    escape = !escape; // Toggle escape mode
                }
                else if (remapKey[i] == ':' && remapKey[i + 1] == ':' && !escape && i != 0)
                {
                    index = i;
                    break;
                }
                else
                {
                    escape = false;
                }
            }

            sourceKey = remapKey.Substring(0, index);
            if (sourceKey[^1] == '`' && (sourceKey.Length < 2 || sourceKey[^2] != '`'))
                sourceKey += '`';
            targetKey = remapKey.Substring(index + 2);
        }

        public override SyntaxNode VisitRemap([NotNull] RemapContext context)
        {
            parser.persistent = true;
            // Extract the source and target keys
            var remapKey = context.RemapKey();
            ParseRemapKey(remapKey.GetText(), out string sourceKey, out string targetKey);
            sourceKey = EscapedString(sourceKey, true);
            targetKey = EscapedString(targetKey, true);

            // Generate function names
            var downFunctionName = InternalPrefix + $"Hotkey_{++parser.hotkeyCount}";
            var upFunctionName = InternalPrefix + $"Hotkey_{++parser.hotkeyCount}";

            // Fix for v1.0.44.07: Set remap_dest_vk to 0xFF if hotkey_flag's length is only 1 because:
            // 1) It allows a destination key that doesn't exist in the keyboard layout (such as 6::ð in
            //    English).
            // 2) It improves performance a little by not calling c  except when the destination key
            //    might be a mouse button or some longer key name whose actual/correct VK value is relied
            //    upon by other places below.
            // Fix for v1.0.40.01: Since remap_dest_vk is also used as the flag to indicate whether
            // this line qualifies as a remap, must do it last in the statement above.  Otherwise,
            // the statement might short-circuit and leave remap_dest_vk as non-zero even though
            // the line shouldn't be a remap.  For example, I think a hotkey such as "x & y::return"
            // would trigger such a bug.

            uint remapSourceVk;
            uint? modifiersLR = null;
            var remapDestVk = 0u;
            var remapDestSc = 0u;
            var remapName = targetKey;
            var hotName = sourceKey;
            uint? modLR = null;

            string tempcp1, remapSource, remapDest, remapDestModifiers; // Must fit the longest key name (currently Browser_Favorites [17]), but buffer overflow is checked just in case.
            bool remapSourceIsCombo, remapSourceIsMouse, remapDestIsMouse, remapKeybdToMouse, remapWheel;
            var ht = Script.TheScript.HookThread;
            var kbLayout = GetKeyboardLayout(0);

            ht.TextToVKandSC(remapName = HotkeyDefinition.TextToModifiers(remapName, null), ref remapDestVk, ref remapDestSc, ref modLR, kbLayout);

            // These will be ignored in other stages if it turns out not to be a remap later below:
            remapSourceVk = ht.TextToVK(tempcp1 = HotkeyDefinition.TextToModifiers(hotName, null), ref modifiersLR, false, true, kbLayout);//An earlier stage verified that it's a valid hotkey, though VK could be zero.
            remapSourceIsCombo = tempcp1.Contains(HotkeyDefinition.COMPOSITE_DELIMITER);
            remapSourceIsMouse = MouseUtils.IsMouseVK(remapSourceVk);
            remapDestIsMouse = MouseUtils.IsMouseVK(remapDestVk);
            remapKeybdToMouse = !remapSourceIsMouse && remapDestIsMouse;
            remapWheel = MouseUtils.IsWheelVK(remapSourceVk) || MouseUtils.IsWheelVK(remapDestVk);
            remapSource = (remapSourceIsCombo ? "" : "*") +// v1.1.27.01: Omit * when the remap source is a custom combo.
                            (tempcp1.Length == 1 && char.IsUpper(tempcp1[0]) ? "+" : "") +// Allow A::b to be different than a::b.
                            hotName;// Include any modifiers too, e.g. ^b::c.

			//Escaping is done differently here than in AHK.
			//There, the generated code is escaped, then has to be unescaped every time the hotkey is processed.
			//Instead, we only escape quotes, and we do so in the C# way, not the AHK way.
			if (remapName[0] == '"')
                remapDest = $"\"";
            else
                remapDest = remapName;// But exclude modifiers here; they're wanted separately.

            remapDestModifiers = targetKey.Substring(0, targetKey.IndexOf(remapName));
            var remapDestKey = (Keys)remapDestVk;
            var remapSourceKey = (Keys)remapSourceVk;

            if (remapDestKey == Keys.Pause
                    && remapDestModifiers.Length == 0// If modifiers are present, it can't be a call to the Pause function.
                    && string.Compare(remapDest, "Pause", true) == 0) // Specifically "Pause", not "vk13".
            {
                // Pause is excluded because it is more common to create a hotkey to pause the script than
                // to remap something to the Pause/Break key, and that's how it was in v1.  Any other key
                // names are interpreted as remapping even if the user defines a function with that name.
                // Doing otherwise would be complicated and probably undesirable.
                return null;
            }

            // It is a remapping. Create one "down" and one "up" hotkey,
            // eg, "x::y" yields,
            // *x::
            // {
            // SetKeyDelay(-1), Send("{Blind}{y DownR}")
            // }
            // *x up::
            // {
            // SetKeyDelay(-1), Send("{Blind}{y Up}")
            // }
            // Using one line to facilitate code.
            // For remapping, decided to use a "macro expansion" approach because I think it's considerably
            // smaller in code size and complexity than other approaches would be.  I originally wanted to
            // do it with the hook by changing the incoming event prior to passing it back out again (for
            // example, a::b would transform an incoming 'a' keystroke into 'b' directly without having
            // to suppress the original keystroke and simulate a new one).  Unfortunately, the low-level
            // hooks apparently do not allow this.  Here is the test that confirmed it:
            // if (event.vkCode == 'A')
            // {
            //  event.vkCode = 'B';
            //  event.scanCode = 0x30; // Or use vk_to_sc(event.vkCode).
            //  return CallNextHookEx(g_KeybdHook, aCode, wParam, lParam);
            // }

            // Otherwise, remap_keybd_to_mouse==false.
            var blindMods = "";
            var temphk = new HotkeyDefinition(999, null, (uint)HotkeyTypeEnum.Normal, hotName, 0);//Needed only for parsing out modifiersConsolidatedLR;

            for (var i = 0; i < 8; ++i)
            {
                if ((temphk.modifiersConsolidatedLR & (1 << i)) != 0)
                {
                    if (!remapDestModifiers.Contains(KeyboardMouseSender.ModLRString[i * 2 + 1]))// This works around an issue with {Blind+}+x releasing RShift to press LShift.
                    {
                        blindMods += KeyboardMouseSender.ModLRString[i * 2];// < or >
                        blindMods += KeyboardMouseSender.ModLRString[i * 2 + 1];// One of ^!+#
                    }
                }
            }

            var extraEvent = ""; // Set default.
            // It seems unnecessary to set press-duration to -1 even though the auto-exec section might
            // have set it to something higher than -1 because:
            // 1) Press-duration doesn't apply to normal remappings since they use down-only and up-only events.
            // 2) Although it does apply to remappings such as a::B and a::^b (due to press-duration being
            //    applied after a change to modifier state), those remappings are fairly rare and supporting
            //    a non-negative-one press-duration (almost always 0) probably adds a degree of flexibility
            //    that may be desirable to keep.
            // 3) SendInput may become the predominant SendMode, so press-duration won't often be in effect anyway.
            // 4) It has been documented that remappings use the auto-execute section's press-duration.
            // The primary reason for adding Key/MouseDelay -1 is to minimize the chance that a one of
            // these hotkey threads will get buried under some other thread such as a timer, which
            // would disrupt the remapping if #MaxThreadsPerHotkey is at its default of 1.
            var p = $"{{Blind{blindMods}}}{extraEvent}{remapDestModifiers}{{{remapDest}{(remapWheel ? "" : " DownR")}}}";

            var downStatements = new List<StatementSyntax> {
                GenerateSetDelayInvocation(isMouse: remapDestIsMouse) // SetKeyDelay or SetMouseDelay
            };

            if (remapKeybdToMouse && !remapWheel)
            {
                // Since source is keybd and dest is mouse, prevent keyboard auto-repeat from auto-repeating
                // the mouse button (since that would be undesirable 90% of the time).  This is done
                // by inserting a single extra IF-statement above the Send that produces the down-event:
                // Generate the Keysharp.Core.Keyboard.GetKeyState invocation
                var getKeyStateInvocation = SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Keyboard", "GetKeyState"),
					CreateArgumentList(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(remapDest)))
                );

                // Generate the IfTest method invocation
                var ifElseInvocation = ((InvocationExpressionSyntax)InternalMethods.IfTest)
                    .WithArgumentList(
						CreateArgumentList(getKeyStateInvocation)
                    );

                // Generate the condition for the if statement
                var condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    ifElseInvocation,
                    SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                );

                // Generate the if statement
                var ifStatement = SyntaxFactory.IfStatement(
                    condition,
                    SyntaxFactory.Block(GenerateSendInvocation(p))
                );
                downStatements.Add(ifStatement);
            }
            else
                downStatements.Add(GenerateSendInvocation(p)); // Send "{Blind}{b DownR}"};
            downStatements.Add(PredefinedKeywords.DefaultReturnStatement);

            // Generate the "down" hotkey function
            var downFunction = SyntaxFactory.MethodDeclaration(
            SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // Return type: object
            SyntaxFactory.Identifier(downFunctionName) // Function name
            )
			.WithParameterList(
				SyntaxFactory.ParameterList(
					SyntaxFactory.SingletonSeparatedList(
						SyntaxFactory.Parameter(SyntaxFactory.Identifier("args"))
						.WithType(PredefinedKeywords.ObjectArrayType)
						.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)))
					)
				)
			)
			.WithModifiers(
                SyntaxFactory.TokenList(
                    Parser.PredefinedKeywords.PublicToken,
                    Parser.PredefinedKeywords.StaticToken
                )
            )
            .WithBody(
                SyntaxFactory.Block(
                    downStatements.ToArray()
                )
            );

            // Generate the "up" hotkey function
            var upFunction = SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(Parser.PredefinedKeywords.Object), // Return type: object
                    SyntaxFactory.Identifier(upFunctionName) // Function name
                )
                .WithParameterList(
                    SyntaxFactory.ParameterList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier("args"))
					        .WithType(PredefinedKeywords.ObjectArrayType)
					        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)))
                        )
                    )
                )
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        Parser.PredefinedKeywords.PublicToken,
                        Parser.PredefinedKeywords.StaticToken
                    )
                )
                .WithBody(
                    SyntaxFactory.Block(
                        GenerateSetDelayInvocation(isMouse: remapDestIsMouse), // SetKeyDelay or SetMouseDelay
                        GenerateSendInvocation($"{{Blind}}{{{remapDest} Up}}"), // Send "{Blind}{b Up}"
                        PredefinedKeywords.DefaultReturnStatement
                    )
                );

            // Add the functions to the main class
            parser.mainClass.Body.AddRange(downFunction, upFunction);

            // Add the "down" hotkey
            parser.DHHR.Add(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        CreateMemberAccess("Keysharp.Core.Common.Keyboard.HotkeyDefinition", "AddHotkey")
                    )
                    .WithArgumentList(
						CreateArgumentList(
                            GenerateFuncObjArgument(downFunctionName),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0u)),
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal($"{remapSource}")
                            )
                        )
                    )
                )
            );

            // Add the "up" hotkey
            parser.DHHR.Add(
                SyntaxFactory.ExpressionStatement(
                    ((InvocationExpressionSyntax)InternalMethods.AddHotkey)
                    .WithArgumentList(
						CreateArgumentList(
							GenerateFuncObjArgument(upFunctionName),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0u)),
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal($"{remapSource} up")
                            )
                        )
                    )
                )
            );

            return null;
        }

        private StatementSyntax GenerateSetDelayInvocation(bool isMouse)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    CreateMemberAccess("Keysharp.Core." + (isMouse ? "Mouse" : "Keyboard"), 
                        isMouse ? "SetMouseDelay" : "SetKeyDelay")
                )
                .WithArgumentList(
					CreateArgumentList(
						SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
                    )
                )
            );
        }

        private StatementSyntax GenerateSendInvocation(string text)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
					CreateMemberAccess("Keysharp.Core.Keyboard", "Send")
                )
                .WithArgumentList(
                    CreateArgumentList(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(text)
                        )
                    )
                )
            );
        }

        private ArgumentSyntax GenerateFuncObjArgument(string functionName)
        {
            return SyntaxFactory.Argument(
                ((InvocationExpressionSyntax)InternalMethods.Func)
                .WithArgumentList(
                    CreateArgumentList(
						SyntaxFactory.CastExpression(
							CreateQualifiedName("System.Delegate"),
							SyntaxFactory.IdentifierName(functionName)
						)
                    )
                )
            );
        }

        public void GenerateGeneralDirectiveStatements()
        {
            foreach (var item in parser.generalDirectives)
            {
                if (item.Value == null)
                    continue;

                if (item.Key.Substring(0, "assembly".Length).ToLowerInvariant() == "assembly")
                {
                    var assemblyName = item.Key.ToLowerInvariant().Substring("assembly".Length);
                    assemblyName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(assemblyName);

                    parser.AddAssembly($"System.Reflection.Assembly{assemblyName}Attribute", item.Value);
                    if (assemblyName == "Version")
                        parser.AddAssembly($"System.Reflection.AssemblyFileVersionAttribute", item.Value);

                    continue;
                }
                switch (item.Key.ToUpper())
                {
                    case "CLIPBOARDTIMEOUT":
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								CreateMemberAccess("Keysharp.Core.Accessors", "A_ClipboardTimeout"),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(uint.Parse(item.Value)))
                            )
                        ));
                        break;
                    case "HOTIFTIMEOUT":
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                CreateMemberAccess("Keysharp.Core.Accessors", "A_HotIfTimeout"),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(uint.Parse(item.Value)))
                            )
                        ));
                        break;
                    case "MAXTHREADS":
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								SyntaxFactory.MemberAccessExpression(
						            SyntaxKind.SimpleMemberAccessExpression,
						            SyntaxFactory.IdentifierName("MainScript"),
						            SyntaxFactory.IdentifierName("MaxThreadsTotal")
					            ),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(uint.Parse(item.Value)))
                            )
                        ));
                        break;
                    case "MAXTHREADSBUFFER":
                        var argument = (item.Value.ToLower() == "false" || item.Value == "0") ? 0 : 1;

                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								CreateMemberAccess("Keysharp.Core.Accessors", "A_MaxThreadsBuffer"),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(argument))
                            )
                        ));
                        break;
                    case "MAXTHREADSPERHOTKEY":
                        var threadCountValue = int.Parse(item.Value);
                        // Clamp the value between 1 and 255
                        var clampedValue = Math.Clamp(threadCountValue, 1, 255);
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								CreateMemberAccess("Keysharp.Core.Accessors", "A_MaxThreadsPerHotkey"),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(clampedValue))
                            )
                        ));
                        break;
                    case "NOTRAYICON":
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								SyntaxFactory.MemberAccessExpression(
									SyntaxKind.SimpleMemberAccessExpression,
									SyntaxFactory.IdentifierName("MainScript"),
									SyntaxFactory.IdentifierName("NoTrayIcon")
								),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                            )
                        ));
                        break;
                    case "WINACTIVATEFORCE":
                        parser.generalDirectiveStatements.Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
								SyntaxFactory.MemberAccessExpression(
									SyntaxKind.SimpleMemberAccessExpression,
									SyntaxFactory.IdentifierName("MainScript"),
									SyntaxFactory.IdentifierName("WinActivateForce")
								),
								PredefinedKeywords.EqualsToken,
								SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                            )
                        ));
                        break;
                }

            }
        }
    }
}
