namespace Keysharp.Core
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
			if (v is VarRef || (v is KeysharpObject kso && kso.HasProp("__Value") != 0))
				return v;
			return new VarRef(getter, setter);
		}

		/// <summary>
		/// Returns a string showing all of the properties of an object.
		/// The string is also appended to sbuf.
		/// The traversal is recursive through all of the object's properties.
		/// </summary>
		/// <param name="obj">The object whose properties will be listed.</param>
		/// <param name="name">The name of the object.</param>
		/// <param name="sbuf">The <see cref="StringBuffer"/> to place the property info in.</param>
		/// <param name="tabLevel">The number of tabs to use for indenting the property tree.</param>
		/// <returns>sbuf.ToString()</returns>
		internal static string PrintProps(object obj, string name, StringBuffer sb, ref int tabLevel)
		{
			var indent = new string('\t', tabLevel);
			var fieldType = obj != null ? obj.GetType().Name : "";

			if (obj is KeysharpObject kso)
			{
				kso.PrintProps(name, sb, ref tabLevel);
			}
			else if (obj != null)
			{
				if (obj is string vs)
				{
					var str = "\"" + vs + "\"";//Can't use interpolated string here because the AStyle formatter misinterprets it.
					_ = sb.AppendLine($"{indent}{name}: {str} ({fieldType})");
				}
				else
					_ = sb.AppendLine($"{indent}{name}: {obj} ({fieldType})");
			}
			else
				_ = sb.AppendLine($"{indent}{name}: null");

			return sb.ToString();
		}
	}
}