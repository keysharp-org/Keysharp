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

		public static object MakeVarRef(Func<object> getter, Action<object> setter, string name) => new VarRef(getter, setter, name);

		// A variable's one reference -- a compiled local's, which it lives in, or a FieldRef -- takes its name from the
		// first & of it.
		public static object MakeVarRef(VarRef variable, string name)
		{
			if (variable.Name.Length == 0)
				variable.Name = name;

			return variable;
		}

		/// <summary>
		/// The one reference to the variable in static field <paramref name="field"/> of <paramref name="owner"/>, which
		/// carries the variable's memory for native code.
		/// </summary>
		public static VarRef FieldRef(Type owner, string field, Func<object> getter, Action<object> setter)
			=> Script.TheScript.Vars.VariableRefs.GetOrAdd((owner, field),
				static (_, made) => new VarRef(made.getter, made.setter, "", true), (getter, setter));

		public static VarRef MemberRef(Type owner, string member, ModuleBindingKind kind) =>
			Script.TheScript.Vars.MemberReference(owner, member, kind);

		public static object MemberGet(Type owner, string member, ModuleBindingKind kind) => Variables.ResolveMember(owner, member, kind).Get();

		public static object ModuleObject(Type owner) => Script.TheScript.Vars.ModuleObject(owner);

		public static object MemberSet(Type owner, string member, ModuleBindingKind kind, object value)
		{
			var target = Variables.ResolveMember(owner, member, kind);
			if (target.RequireWritable(VarUsage.Assign, member))
				target.Set(value);
			return value;
		}

		/// <summary>
		/// What the compiler passes where AutoHotkey passes a naked variable itself: the variable's reference. An unset
		/// variable is passed as the unset value it holds, so the callee reports it as it would the value. A held reference
		/// supplies its target to the native call.
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
