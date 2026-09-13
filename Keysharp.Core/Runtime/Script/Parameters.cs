using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		/// <summary>
		/// The elements of a spread source (<c>f(arr*)</c>, <c>[arr*]</c>). Named arguments need no handling of
		/// their own: a collected variadic carries them as ordinary trailing elements, so spreading it re-emits
		/// them in place -- which is the whole forwarding story for <c>w(args*) =&gt; inner(args*)</c>.
		/// </summary>
		public static object[] FlattenValues(object obj)
		{
			var ke = Loops.MakeEnumerator(obj, 1L);

			if (ke is object && IsCallable(ke))
			{
				// Driven exactly the way MakeEnumerable drives a for-loop: one VarRef and one argument array for the
				// whole run, calling the normalized Enumerator directly. Invoke-by-name per element would re-resolve
				// "Call" and allocate a fresh params array every iteration for the same dispatch the for-loop
				// already performs without either.
				var l = new List<object>();
				object v1 = null;
				VarRef vf = new VarRef(v1);
				var callArgs = new object[] { vf };

				while (ke.Call(callArgs).IsCallbackResultNonEmpty())
					l.Add(vf.__Value);

				return l.ToArray();
			}
			else if (obj is IEnumerable en)
			{
				var l = new List<object>();
				l.AddRange(en.Flatten(false).Cast<object>());
				return l.ToArray();
			}
			else if (obj is IEnumerator<(object, object)> ieoo)
			{
				var l = new List<object>();

				while (ieoo.MoveNext())
					l.Add(ieoo.Current.Item1);

				return l.ToArray();
			}
			else
				return [obj];
		}

		/// <summary>
		/// The `name: value` arguments of one call, as name/value pairs. Emitted by the lowerer as the call's LAST
		/// argument, so named arguments ride in the ordinary argument array and every forwarding hop passes them
		/// through untouched; they are folded into positional slots where the callee's parameter list is finally
		/// known.
		/// </summary>
		public static object NamedArgs(params object[] nameValuePairs) => new Ks.NamedArgs(nameValuePairs);

		public static object Parameter(object[] values, object def, int index) => index < values.Length ? values[index] : def;

		public static void Parameters(string[] names, object[] values, object[] defaults)
		{
			for (var i = 0; i < names.Length; i++)
			{
				var init = i < values.Length ? values[i] : i < defaults.Length ? defaults[i] : null;
				_ = Script.TheScript.Vars.SetVariable(names[i], init);
			}
		}
	}
}
