namespace Keysharp.Builtins
{
	/// <summary>
	/// Miscellaneous public facing functions which don't fit anywhere else.
	/// Add to this class sparingly because functions should be well organized.
	/// </summary>
	[PublicHiddenFromUser]
	public static class Misc
	{
		/// <summary>
		/// Used by the parser to generate code to handle reference arguments to method calls on objects.
		/// This is not needed for static function calls with reference arguments.
		/// This should never be needed to be manually called by a script.
		/// It is only used by the parser when generating C# code.
		/// </summary>
		/// <param name="i">The index of the arguments passed for the current method call.</param>
		/// <param name="o">The value to pass to the function.</param>
		/// <param name="r">The <see cref="Action"/> to call after the function returns to assign the value back out to the passed in variable.</param>
		/// <returns>A <see cref="RefHolder"/> object that contains all of the passed in info, which will be passed to the method call.</returns>
		public static RefHolder Mrh(int i, object o, Action<object> r) => new (i, o, r);

		public static object MakeVarRef(Func<object> getter, Action<object> setter)
		{
			var v = getter();
			return Refs.DeclaresValue(v) ? v : new VarRef(getter, setter);
		}

	}
}
