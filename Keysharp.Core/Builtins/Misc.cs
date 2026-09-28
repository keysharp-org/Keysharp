namespace Keysharp.Builtins
{
	/// <summary>
	/// Miscellaneous runtime helpers emitted by the compiler.
	/// </summary>
	[PublicHiddenFromUser]
	public static class Misc
	{
		public static object MakeVarRef(Func<object> getter, Action<object> setter)
			=> MakeVarRef(getter, setter, "");

		public static object MakeVarRef(Func<object> getter, Action<object> setter, string name)
		{
			var v = getter();
			return Refs.DeclaresValue(v) ? v : new VarRef(getter, setter, name);
		}

		// A variable's one reference -- a compiled local's, which it lives in, or a FieldRef -- takes its name from the
		// first & of it.
		public static object MakeVarRef(VarRef variable, string name)
		{
			var value = variable.__Value;

			if (Refs.DeclaresValue(value))
				return value;

			if (variable.Name.Length == 0)
				variable.Name = name;

			return variable;
		}

		/// <summary>
		/// The one reference to the variable in static field <paramref name="field"/> of <paramref name="owner"/>, which
		/// carries the variable's memory for native code.
		/// </summary>
		public static VarRef FieldRef(Type owner, string field, Func<object> getter, Action<object> setter)
			=> Script.TheScript.Vars.FieldRefs.GetOrAdd((owner, field),
				static (_, made) => new VarRef(made.getter, made.setter, "", true), (getter, setter));

		/// <summary>
		/// What the compiler passes where AutoHotkey passes a naked variable itself: the variable's reference. An unset
		/// variable is passed as the unset value it holds, so the callee reports it as it would the value, and a variable
		/// holding a reference stands for what that refers to, as it does with <c>&amp;</c>.
		/// </summary>
		public static object RefIfSet(object variable)
		{
			if (variable is not VarRef { IsPlain: true } reference)
				return variable;

			var value = reference.__Value;
			return value == null ? null : Refs.DeclaresValue(value) ? value : reference;
		}
	}
}
