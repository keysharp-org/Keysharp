namespace Keysharp.Builtins
{
	/// <summary>
	/// Mutable state threaded through a single property-tree render (see <see cref="PropPrinter"/>).
	/// Carrying this instead of a <c>ref int</c> tab level keeps the indentation and
	/// cycle-detection state together and makes the traversal exception-safe.
	/// </summary>
	internal sealed class PropPrintContext
	{
		/// <summary>Hard cap on recursion depth, a backstop for pathological (but acyclic) graphs.</summary>
		internal const int MaxDepth = 100;

		/// <summary>The buffer the rendered tree is written to.</summary>
		internal readonly Ks.StringBuffer Sb;

		/// <summary>
		/// Objects currently on the recursion stack, compared by reference so that any
		/// cycle (self-reference, A-&gt;B-&gt;A, base/prototype loops, ...) terminates
		/// instead of overflowing the stack.
		/// </summary>
		internal readonly HashSet<object> Visited;

		/// <summary>Current indentation level (number of leading tabs).</summary>
		internal int TabLevel;

		internal PropPrintContext(Ks.StringBuffer sb, int tabLevel = 0)
			: this(sb, tabLevel, new HashSet<object>(ReferenceEqualityComparer.Instance)) { }

		private PropPrintContext(Ks.StringBuffer sb, int tabLevel, HashSet<object> visited)
		{
			Sb = sb;
			TabLevel = tabLevel;
			Visited = visited;
		}

		internal string Indent => new ('\t', TabLevel);

		/// <summary>
		/// Creates a context that renders into a different buffer but shares this context's
		/// cycle-detection set. Used when an array/map renders a nested object into a
		/// temporary buffer so it can be inlined into the surrounding list.
		/// </summary>
		internal PropPrintContext Fork(Ks.StringBuffer sb, int tabLevel) => new (sb, tabLevel, Visited);
	}

	/// <summary>
	/// The single place that renders an object as an indented property tree (used by
	/// <see cref="Debug.GetVars"/>). Each builtin type
	/// that needs a custom representation is handled here by switching on its type and
	/// enumerating it through its public contract, rather than via per-type virtual
	/// overrides. This keeps all of the (interrelated) formatting, cycle detection and
	/// indentation logic in one file.
	/// </summary>
	internal static class PropPrinter
	{
		/// <summary>
		/// Renders <paramref name="obj"/> as an indented property tree into <paramref name="sb"/>.
		/// The public entry point (used by <see cref="Debug.GetVars"/>).
		/// </summary>
		internal static void Print(object obj, string name, Ks.StringBuffer sb) => Print(obj, name, new PropPrintContext(sb));

		/// <summary>
		/// Renders an arbitrary value under <paramref name="name"/>: a leaf line for
		/// non-objects and prototypes, otherwise the object's tree (guarded against cycles).
		/// This is the only entry point and the only thing recursion ever calls back into.
		/// </summary>
		private static void Print(object obj, string name, PropPrintContext ctx)
		{
			if (obj is not KeysharpObject kso)
			{
				WriteLeaf(ctx, name, LeafToken(obj), TypeName(obj));
				return;
			}

			// Prototype/base objects are not descended into: their members are getters meant
			// to run against instances, so evaluating them here is both noisy and throw-prone.
			if (kso.isPrototype)
			{
				WriteLeaf(ctx, name, SafeToString(kso), TypeName(kso));
				return;
			}

			// Reference-equality guard: terminates real cycles and absurdly deep graphs.
			if (ctx.Visited.Count >= PropPrintContext.MaxDepth || !ctx.Visited.Add(kso))
			{
				WriteLeaf(ctx, name, "<cycle>", TypeName(kso));
				return;
			}

			try
			{
				switch (kso)
				{
					case Ks.StringBuffer sbuf: WriteLeaf(ctx, name, sbuf.ToString(), TypeName(sbuf)); break;
					case Array arr: PrintArray(arr, name, ctx); break;
					case Map map: PrintMap(map, name, ctx); break;
					default: PrintObject(kso, name, ctx); break;
				}
			}
			finally { _ = ctx.Visited.Remove(kso); }
		}

		/// <summary>Renders a plain object: a header line followed by its own properties, one level in.</summary>
		private static void PrintObject(KeysharpObject obj, string name, PropPrintContext ctx)
		{
			WriteHeader(ctx, name, TypeName(obj));
			ctx.TabLevel++;
			WriteOwnProps(obj, ctx);
			ctx.TabLevel--;
		}

		/// <summary>Renders an array as <c>name: [a, b, ...] (Type)</c> followed by any own properties.</summary>
		private static void PrintArray(Array arr, string name, PropPrintContext ctx)
		{
			var type = TypeName(arr);
			var count = arr.Count;

			if (count > 0)
			{
				_ = ctx.Sb.Append(name.Length == 0 ? $"{ctx.Indent} [" : $"{ctx.Indent}{name}: [");

				var i = 0;

				foreach (var val in (IEnumerable<object>)arr)
				{
					var str = RenderMember(val, ctx);
					_ = ctx.Sb.Append(++i < count ? $"{str}, " : str);
				}

				_ = ctx.Sb.AppendLine($"] ({type})");
			}
			else if (name.Length == 0)
				_ = ctx.Sb.Append($"{ctx.Indent} [] ({type})");
			else
				_ = ctx.Sb.AppendLine($"{ctx.Indent}{name}: [] ({type})");

			ctx.TabLevel++;
			WriteOwnProps(arr, ctx);
			ctx.TabLevel--;
		}

		/// <summary>
		/// Renders a map as <c>name: {k: v, ...} (Type)</c> followed by any own properties.
		/// Iterating the public enumerator preserves each map type's own ordering (sorted for
		/// <see cref="Map"/>, insertion order for <c>HashMap</c>).
		/// </summary>
		private static void PrintMap(Map map, string name, PropPrintContext ctx)
		{
			var type = TypeName(map);
			var count = map.Count;

			if (count > 0)
			{
				if (name.Length == 0)
					_ = ctx.Sb.Append($"{ctx.Indent}\t{{");
				else
					_ = ctx.Sb.Append(ctx.Indent + name + ": " + "\t {");//Staged because the AStyle formatter misinterprets it.

				long i = 0;

				foreach (var (key, val) in (IEnumerable<(object, object)>)map)
				{
					var keyStr = RenderMember(key, ctx);

					// When the value renders as its own block, indent the key onto that block's line.
					if (val is KeysharpObject)
						keyStr = new string('\t', ctx.TabLevel + 1) + keyStr;

					var valStr = RenderMember(val, ctx);
					_ = ctx.Sb.Append(++i < count ? $"{keyStr}: {valStr}, " : $"{keyStr}: {valStr}");
				}

				_ = ctx.Sb.AppendLine($"}} ({type})");
			}
			else if (name.Length == 0)
				_ = ctx.Sb.Append($"{ctx.Indent} {{}} ({type})");
			else
				_ = ctx.Sb.AppendLine($"{ctx.Indent}{name}: {{}} ({type})");

			ctx.TabLevel++;
			WriteOwnProps(map, ctx);
			ctx.TabLevel--;
		}

		/// <summary>
		/// Enumerates an object's own (dynamic) properties and renders each one. Tolerates
		/// getters that throw by emitting an inline error marker rather than aborting the dump.
		/// Callers raise <see cref="PropPrintContext.TabLevel"/> beforehand so properties appear one level in.
		/// </summary>
		private static void WriteOwnProps(Any obj, PropPrintContext ctx)
		{
			Enumerator opi;

			try { opi = (Enumerator)KeysharpObject.OwnProps(obj); }
			catch { return; }

			while (true)
			{
				try { if (!opi.Advance(2)) break; }
				catch { break; }//If advancing the enumerator itself fails there's nothing left to do.

				// Enumerator.Current (the single-value form) yields only the key, so the
				// property name is available even when evaluating its value getter throws.
				var propName = opi.CurrentValue?.ToString() ?? "";
				object val;

				try { val = ((IEnumerator<(object, object)>)opi).Current.Item2; }
				catch (Exception ex)
				{
					WriteLeaf(ctx, propName, $"<error: {ex.Message}>", "");
					continue;
				}

				Print(val, propName, ctx);
			}
		}

		/// <summary>
		/// Returns the inline text for an array element or a map key/value. Objects are
		/// expanded into their own block (preceded by a newline so the block starts on a
		/// fresh, indented line); everything else becomes a single token.
		/// </summary>
		private static string RenderMember(object val, PropPrintContext ctx)
		{
			if (val is KeysharpObject)
			{
				_ = ctx.Sb.AppendLine();
				return RenderBlock(val, ctx);
			}

			return LeafToken(val);
		}

		/// <summary>Renders <paramref name="val"/> into its own trimmed block one level in, sharing cycle state.</summary>
		private static string RenderBlock(object val, PropPrintContext ctx)
		{
			var temp = ctx.Fork(new Ks.StringBuffer(), ctx.TabLevel + 1);
			Print(val, "", temp);
			return temp.Sb.ToString().TrimEnd(CrLf);
		}

		/// <summary>Returns the inline token for a non-object value: a quoted string, "null", or ToString().</summary>
		private static string LeafToken(object val)
		{
			if (val is null)
				return "null";

			if (val is string s)
				return "\"" + s + "\"";//Built piecemeal because the AStyle formatter misinterprets interpolated quotes.

			return val.ToString();
		}

		/// <summary>Writes the header line for an object: <c>name: (Type)</c>, or <c> (Type)</c> when unnamed.</summary>
		private static void WriteHeader(PropPrintContext ctx, string name, string type)
		{
			if (name.Length == 0)
				_ = ctx.Sb.AppendLine($"{ctx.Indent} ({type})");
			else
				_ = ctx.Sb.AppendLine($"{ctx.Indent}{name}: ({type})");
		}

		/// <summary>Writes a single leaf line: <c>name: value (Type)</c> (the type suffix is dropped when empty).</summary>
		private static void WriteLeaf(PropPrintContext ctx, string name, string value, string type)
		{
			var suffix = type.Length != 0 ? $" ({type})" : "";

			if (name.Length == 0)
				_ = ctx.Sb.AppendLine($"{ctx.Indent}{value}{suffix}");
			else
				_ = ctx.Sb.AppendLine($"{ctx.Indent}{name}: {value}{suffix}");
		}

		/// <summary>
		/// The display type name: the name a script knows the value's type by, so that this listing names
		/// types the same way <see cref="Types.Type"/> does rather than exposing internal CLR names.
		/// Empty for an unset value, which drops the type suffix.
		/// </summary>
		private static string TypeName(object val) => val == null ? "" : Types.Type(val);

		private static string SafeToString(object val)
		{
			try { return val?.ToString() ?? "null"; }
			catch (Exception ex) { return $"<error: {ex.Message}>"; }
		}
	}
}
