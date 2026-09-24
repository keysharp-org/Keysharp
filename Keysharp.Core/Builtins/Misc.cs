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

		public static object MakeVarRef(StrongBox<object> box, string name)
		{
			var value = box.Value;
			return Refs.DeclaresValue(value) ? value : new VarRef(box, name);
		}

	}
}
