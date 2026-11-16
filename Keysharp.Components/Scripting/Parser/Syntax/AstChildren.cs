namespace Keysharp.Parsing.Syntax
{
	// Structural children, including scope bodies. Optional children can be null; each pass selects its own scope boundary.
	internal static class AstChildren
	{
		private static readonly List<Stmt> emptyStmts = [];
		private static readonly List<Expr> emptyExprs = [];

		internal static ChildEnumerable Of(Node node) => node switch
		{
			ProgramNode program => new(program.Body),
			UnaryExpr unary => One(unary.Operand),
			BinaryExpr binary => Two(binary.Left, binary.Right),
			AssignExpr assignment => Two(assignment.Target, assignment.Value),
			TernaryExpr ternary => Three(ternary.Cond, ternary.Then, ternary.Else),
			GroupExpr group => One(group.Inner),
			SequenceExpr sequence => new(sequence.Items),
			DerefExpr dereference => One(dereference.Name),
			CallExpr call => new(CallArguments(call.Callee, call.Args)),
			MemberExpr member => One(member.Target),
			DynMemberExpr member => Two(member.Target, member.NameExpr),
			IndexExpr indexed => new(CallArguments(indexed.Target, indexed.Args)),
			ArrayExpr array => new(Arguments(array.Elements)),
			MapExpr map => new(MapEntries(map)),
			ObjectExpr obj => new(ObjectEntries(obj)),
			FatArrowExpr function => new(FunctionParts(function.Params, function.Body, function.BlockBody)),
			ExpressionStmt expression => One(expression.Expr),
			Block block => new(block.Body),
			IfStmt conditional => Three(conditional.Cond, conditional.Then, conditional.Else),
			WhileStmt loop => new(WhileParts(loop)),
			LoopStmt loop => new(LoopParts(loop)),
			SpecialLoopStmt loop => new(SpecialLoopParts(loop)),
			ForStmt loop => new(ForParts(loop)),
			SwitchStmt choice => new(SwitchParts(choice)),
			TryStmt attempt => new(TryParts(attempt)),
			ReturnStmt ret => One(ret.Value),
			ThrowStmt thrown => One(thrown.Value),
			UsingStmt scope => new(UsingParts(scope)),
			GotoStmt jump => One(jump.Expression),
			DeclStmt declaration => new(declaration.Items),
			HotkeyDef hotkey => Two(hotkey.Body, hotkey.Func),
			HotstringDef hotstring => Two(hotstring.Body, hotstring.Func),
			FunctionDecl function => new(FunctionParts(function.Params, function.Body, function.ArrowBody)),
			ClassDecl type => new(ClassParts(type)),
			AppDirective app => One(app.Value),
			StartupDirective startup => One(startup.Value),
			_ => default
		};

		private static ChildEnumerable One(Node first) => new(first, null, null, 1);
		private static ChildEnumerable Two(Node first, Node second) => new(first, second, null, 2);
		private static ChildEnumerable Three(Node first, Node second, Node third) => new(first, second, third, 3);

		// Keep fixed children and list walks as values; complex shapes retain their separate iterator state machines.
		internal readonly struct ChildEnumerable
		{
			private readonly object source;
			private readonly Node second, third;
			private readonly int count;

			internal ChildEnumerable(Node first, Node second, Node third, int count)
			{
				source = first;
				this.second = second;
				this.third = third;
				this.count = count;
			}

			internal ChildEnumerable(IReadOnlyList<Node> children)
			{
				source = children;
				second = third = null;
				count = -1;
			}

			internal ChildEnumerable(IEnumerable<Node> children)
			{
				source = children;
				second = third = null;
				count = -2;
			}

			public Enumerator GetEnumerator() => new(this);

			internal struct Enumerator : IDisposable
			{
				private readonly ChildEnumerable children;
				private IEnumerator<Node> iterator;
				private int index;

				internal Enumerator(ChildEnumerable children)
				{
					this.children = children;
					iterator = null;
					index = -1;
				}

				public Node Current => children.count switch
				{
					-2 => iterator.Current,
					-1 => ((IReadOnlyList<Node>)children.source)[index],
					_ => index switch { 0 => (Node)children.source, 1 => children.second, 2 => children.third, _ => null }
				};

				public bool MoveNext()
				{
					if (children.count == -2)
					{
						iterator ??= ((IEnumerable<Node>)children.source).GetEnumerator();
						return iterator.MoveNext();
					}

					var length = children.count == -1 ? ((IReadOnlyList<Node>)children.source).Count : children.count;
					return ++index < length;
				}

				public void Dispose() => iterator?.Dispose();
			}
		}

		private static IEnumerable<Node> Arguments(List<Argument> arguments)
		{
			foreach (var argument in arguments) { yield return argument.Value; yield return argument.NameExpr; }
		}

		private static IEnumerable<Node> CallArguments(Node target, List<Argument> arguments)
		{
			yield return target;
			foreach (var argument in arguments) { yield return argument.Value; yield return argument.NameExpr; }
		}

		private static IEnumerable<Node> MapEntries(MapExpr map)
		{
			foreach (var entry in map.Entries) { yield return entry.Key; yield return entry.Value; }
		}

		private static IEnumerable<Node> ObjectEntries(ObjectExpr obj)
		{
			foreach (var entry in obj.Entries) { yield return entry.Key; yield return entry.Value; }
		}

		private static IEnumerable<Node> FunctionParts(List<Param> parameters, Node body, Node arrow)
		{
			foreach (var parameter in parameters) yield return parameter.Default;
			yield return body;
			yield return arrow;
		}

		private static IEnumerable<Node> WhileParts(WhileStmt loop)
		{ yield return loop.Cond; yield return loop.Body; yield return loop.Until; yield return loop.Else; }

		private static IEnumerable<Node> LoopParts(LoopStmt loop)
		{ yield return loop.Count; yield return loop.Body; yield return loop.Until; yield return loop.Else; }

		private static IEnumerable<Node> ForParts(ForStmt loop)
		{ yield return loop.Enumerable; yield return loop.Body; yield return loop.Until; yield return loop.Else; }

		private static IEnumerable<Node> SpecialLoopParts(SpecialLoopStmt loop)
		{
			foreach (var argument in loop.Args ?? emptyExprs) yield return argument;
			yield return loop.Body;
			yield return loop.Until;
			yield return loop.Else;
		}

		private static IEnumerable<Node> SwitchParts(SwitchStmt choice)
		{
			yield return choice.Value;
			yield return choice.CaseSense;
			foreach (var branch in choice.Cases)
			{
				foreach (var value in branch.Values) yield return value;
				foreach (var child in branch.Body) yield return child;
			}
			foreach (var child in choice.Default ?? emptyStmts) yield return child;
		}

		private static IEnumerable<Node> TryParts(TryStmt attempt)
		{
			yield return attempt.Body;
			foreach (var catcher in attempt.Catches) yield return catcher.Body;
			yield return attempt.Else;
			yield return attempt.Finally;
		}

		private static IEnumerable<Node> UsingParts(UsingStmt scope)
		{
			foreach (var resource in scope.Resources) yield return resource;
			yield return scope.Body;
		}

		private static IEnumerable<Node> ClassParts(ClassDecl type)
		{
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
		}
	}
}
