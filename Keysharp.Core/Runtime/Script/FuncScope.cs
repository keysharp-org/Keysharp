namespace Keysharp.Runtime
{
	/// <summary>How a function's variable is declared, which is what an error about it calls it.</summary>
	public enum VarKind : byte
	{
		Local,
		ImplicitLocal,
		Parameter,
		Static,
		ImplicitStatic,
		Global,
		/// <summary>A nested function, or a function, class, module or read-only built-in variable an import binds, which takes no write.</summary>
		Constant
	}

	/// <summary>
	/// The variables of an executing user function, for dynamic references, A_ThisFunc, ListVars and closures resolved by
	/// name. <see cref="Script.EnterScope"/> publishes it in the executing function's call stack frame, so the scope
	/// visible at any point is the nearest enclosing user function's.
	/// </summary>
	public sealed class FuncScope
	{
		/// <summary>Returns the value of one of the function's variables (null when it has none), <see cref="Undeclared"/> or <see cref="Global"/>.</summary>
		public delegate object Reader(object name);

		/// <summary>Assigns one of the function's variables and returns the value, or returns <see cref="Undeclared"/> or <see cref="Global"/>.</summary>
		public delegate object Writer(object name, object value);

		private sealed class Marker { }

		// A name the function holds no write for: an Undeclared one is written only as a built-in variable, a Global one as
		// any global.
		public static readonly object Undeclared = new Marker(), Global = new Marker();

		internal static bool IsOwn(object read) => read is not Marker;

		/// <summary>A name the function declares or holds: the spelling it is declared by and how.</summary>
		public readonly record struct Declaration(string Name, VarKind Kind);

		/// <summary>The function's AHK-visible name, for A_ThisFunc and the ListVars header (empty for an anonymous function).</summary>
		public readonly string Name;

		internal readonly Reader Read;
		internal readonly Writer Write;
		// What a write finds for a name no declaration covers: Global in an assume-global function, else Undeclared.
		internal readonly object Unheld;
		// Non-capturing, so the C# compiler caches one delegate per function, which keys the one table every call shares.
		private readonly System.Func<Declaration[]> factory;
		private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Func<Declaration[]>, Table> tables = new();
		private Table declarations;

		// A function's declarations in order, for ListVars, and by name.
		private sealed class Table(Declaration[] rows)
		{
			internal static readonly Table Empty = new([]);
			internal readonly Declaration[] Rows = rows;
			internal readonly System.Collections.Generic.Dictionary<string, Declaration> ByName = rows
				.DistinctBy(row => row.Name, System.StringComparer.OrdinalIgnoreCase)
				.ToDictionary(row => row.Name, System.StringComparer.OrdinalIgnoreCase);
		}

		public FuncScope(string name, Reader read, Writer write, System.Func<Declaration[]> declarations, bool assumeGlobal)
		{
			Name = name ?? "";
			Read = read;
			Write = write;
			factory = declarations;
			Unheld = assumeGlobal ? Global : Undeclared;
		}

		private Table Declarations => declarations ??= factory == null ? Table.Empty : tables.GetValue(factory, static build => new(build()));

		internal bool TryGetDeclaration(string key, out Declaration declaration) => Declarations.ByName.TryGetValue(key, out declaration);

		/// <summary>True (with <paramref name="value"/> set) when <paramref name="name"/> is one of this function's variables.</summary>
		internal bool TryGetValue(object name, out object value)
		{
			value = Read(name);

			if (IsOwn(value))
				return true;

			value = null;
			return false;
		}

		/// <summary>This scope's variables as name/value pairs, for ListVars, read at the moment of enumeration.</summary>
		internal System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> Enumerate()
		{
			foreach (var row in Declarations.Rows)
				if (row.Kind != VarKind.Constant && TryGetValue(row.Name, out var value))
					yield return new System.Collections.Generic.KeyValuePair<string, object>(row.Name, value);
		}
	}
}
