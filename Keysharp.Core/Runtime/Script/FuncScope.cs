namespace Keysharp.Runtime
{
	/// <summary>
	/// Exposes the variables of a currently-executing user function to code outside its C# frame — primarily
	/// closure resolution by name (e.g. a RegEx callout that names a closure, via
	/// <see cref="Keysharp.Builtins.Functions.GetKeysharpFunc"/>) and ListVars enumeration.<br/>
	/// A scope-publishing function (one that uses <c>%name%</c>, or calls a scope-consuming builtin) installs one
	/// in its prologue through <see cref="Script.EnterScope"/>, over its generated reader/writer lambdas. It is held
	/// <c>[ThreadStatic]</c> on <see cref="Script.executingUserFunc"/>; <see cref="Keysharp.Builtins.KeysharpFunc.Call"/>
	/// clears it on entry to any user function and restores it on return, so the scope visible at any point is the
	/// nearest enclosing user function — and only that one. The pseudo-thread push/pop resets and restores it across
	/// an interrupt boundary, so an interrupting thread (timer/hotkey) starts with none while a synchronous callout
	/// shares the calling function's.
	/// </summary>
	public sealed class FuncScope
	{
		/// <summary>
		/// Returns the value of one of the function's variables (the generated <c>KS_readVar</c> switch-expression
		/// lambda), or <see cref="Script.DerefMiss"/> when the name is not one of its locals/statics/closures (callers
		/// then fall back to the module/global store).
		/// </summary>
		public delegate object Reader(object name);

		/// <summary>
		/// Assigns one of the function's variables (the generated <c>KS_writeVar</c> switch-expression lambda) and
		/// returns the assigned value, or <see cref="Script.DerefMiss"/> when the name is not one of its variables.
		/// Used for in-body <c>%name% := …</c> writes; the FuncScope itself holds only the reader.
		/// </summary>
		public delegate object Writer(object name, object value);

		/// <summary>The function's exact AHK-visible name, for A_ThisFunc and the ListVars header (empty for an
		/// anonymous function). The Lowerer passes it as a literal to <see cref="Script.EnterScope"/>, so there is no
		/// need to carry the live KeysharpFunc.</summary>
		public readonly string Name;

		private readonly Reader reader;
		// Returns this scope's variable names (the reader's switch keys). The Lowerer passes a non-capturing
		// lambda, which the C# compiler caches as a singleton — so building the scope costs no per-call allocation
		// for the names, and the array itself is only materialised if ListVars actually enumerates.
		private readonly System.Func<string[]> namesFactory;

		public FuncScope(string name, Reader reader, System.Func<string[]> namesFactory)
		{
			Name = name ?? "";
			this.reader = reader;
			this.namesFactory = namesFactory;
		}

		/// <summary>
		/// True (with <paramref name="value"/> set) when <paramref name="name"/> is one of this function's
		/// variables; false for any other name.
		/// </summary>
		public bool TryGetVar(object name, out object value)
		{
			var v = reader(name);
			if (ReferenceEquals(v, Script.DerefMiss)) { value = null; return false; }
			value = v;
			return true;
		}

		/// <summary>This scope's variables as name/value pairs, for ListVars. Resolves each name through the reader,
		/// so values reflect the moment of enumeration (snapshot synchronously on the owning thread).</summary>
		public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> Enumerate()
		{
			var names = namesFactory?.Invoke();
			if (names == null)
				yield break;
			foreach (var n in names)
				if (TryGetVar(n, out var v))
					yield return new System.Collections.Generic.KeyValuePair<string, object>(n, v);
		}
	}
}
