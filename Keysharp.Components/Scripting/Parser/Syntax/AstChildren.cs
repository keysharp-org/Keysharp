namespace Keysharp.Parsing.Syntax
{
	// Structural children, including scope bodies. Optional children can be null; each pass selects its own scope boundary.
	internal static class AstChildren
	{
		internal static IEnumerable<Node> Of(Node node)
		{
			switch (node)
			{
				case ProgramNode program:
					foreach (var child in program.Body) yield return child;
					break;
				case UnaryExpr unary: yield return unary.Operand; break;
				case BinaryExpr binary: yield return binary.Left; yield return binary.Right; break;
				case AssignExpr assignment: yield return assignment.Target; yield return assignment.Value; break;
				case TernaryExpr ternary: yield return ternary.Cond; yield return ternary.Then; yield return ternary.Else; break;
				case GroupExpr group: yield return group.Inner; break;
				case SequenceExpr sequence:
					foreach (var child in sequence.Items) yield return child;
					break;
				case DerefExpr dereference: yield return dereference.Name; break;
				case CallExpr call:
					yield return call.Callee;
					foreach (var child in Arguments(call.Args)) yield return child;
					break;
				case MemberExpr member: yield return member.Target; break;
				case DynMemberExpr member: yield return member.Target; yield return member.NameExpr; break;
				case IndexExpr index:
					yield return index.Target;
					foreach (var child in Arguments(index.Args)) yield return child;
					break;
				case ArrayExpr array:
					foreach (var child in Arguments(array.Elements)) yield return child;
					break;
				case MapExpr map:
					foreach (var entry in map.Entries) { yield return entry.Key; yield return entry.Value; }
					break;
				case ObjectExpr obj:
					foreach (var entry in obj.Entries) { yield return entry.Key; yield return entry.Value; }
					break;
				case FatArrowExpr function:
					foreach (var parameter in function.Params) yield return parameter.Default;
					yield return function.Body;
					yield return function.BlockBody;
					break;
				case ExpressionStmt expression: yield return expression.Expr; break;
				case Block block:
					foreach (var child in block.Body) yield return child;
					break;
				case IfStmt conditional:
					yield return conditional.Cond;
					yield return conditional.Then;
					yield return conditional.Else;
					break;
				case WhileStmt loop:
					yield return loop.Cond;
					yield return loop.Body;
					yield return loop.Until;
					yield return loop.Else;
					break;
				case LoopStmt loop:
					yield return loop.Count;
					yield return loop.Body;
					yield return loop.Until;
					yield return loop.Else;
					break;
				case SpecialLoopStmt loop:
					foreach (var argument in loop.Args ?? []) yield return argument;
					yield return loop.Body;
					yield return loop.Until;
					yield return loop.Else;
					break;
				case ForStmt loop:
					yield return loop.Enumerable;
					yield return loop.Body;
					yield return loop.Until;
					yield return loop.Else;
					break;
				case SwitchStmt choice:
					yield return choice.Value;
					yield return choice.CaseSense;
					foreach (var branch in choice.Cases)
					{
						foreach (var value in branch.Values) yield return value;
						foreach (var child in branch.Body) yield return child;
					}
					foreach (var child in choice.Default ?? []) yield return child;
					break;
				case TryStmt attempt:
					yield return attempt.Body;
					foreach (var catcher in attempt.Catches) yield return catcher.Body;
					yield return attempt.Else;
					yield return attempt.Finally;
					break;
				case ReturnStmt ret: yield return ret.Value; break;
				case ThrowStmt thrown: yield return thrown.Value; break;
				case DeclStmt declaration:
					foreach (var item in declaration.Items) yield return item;
					break;
				case HotkeyDef hotkey: yield return hotkey.Body; yield return hotkey.Func; break;
				case HotstringDef hotstring: yield return hotstring.Body; yield return hotstring.Func; break;
				case FunctionDecl function:
					foreach (var parameter in function.Params) yield return parameter.Default;
					yield return function.Body;
					yield return function.ArrowBody;
					break;
				case ClassDecl type:
					foreach (var import in type.Imports) yield return import;
					foreach (var field in type.Fields) { yield return field.Init; yield return field.TypeExpr; }
					foreach (var method in type.Methods)
					{
						foreach (var parameter in method.Params) yield return parameter.Default;
						yield return method.Body;
						yield return method.ArrowBody;
					}
					foreach (var property in type.Properties)
					{
						foreach (var parameter in property.Params) yield return parameter.Default;
						yield return property.GetBody;
						yield return property.GetArrow;
						yield return property.SetBody;
						yield return property.SetArrow;
					}
					foreach (var init in type.StaticInit) yield return init;
					foreach (var init in type.InstanceInit) yield return init;
					foreach (var child in type.Nested) yield return child;
					foreach (var directive in type.CSharpBlocks ?? []) yield return directive;
					break;
				case AppDirective app: yield return app.Value; break;
			}
		}

		private static IEnumerable<Node> Arguments(IEnumerable<Argument> arguments)
		{
			foreach (var argument in arguments) { yield return argument.Value; yield return argument.NameExpr; }
		}
	}
}
