#if WINDOWS
namespace Keysharp.Builtins.COM
{
	internal static class ComEnumeration
	{
		/// <summary>
		/// Creates an <see cref="Enumerator"/> for a COM object.
		/// </summary>
		/// <param name="o">The COM object to create an enumerator for.</param>
		internal static Enumerator CreateEnumerator(object o, int c)
		{
			IEnumerator enumerator = null;

			try
			{
				enumerator = (IEnumerator)Keysharp.Runtime.Script.Invoke(o, "_NewEnum");
			}
			catch (KeysharpException ex)
			{
				_ = Errors.ErrorOccurred($"Could not retrieve the _NewEnum() method on a COM object while trying to create an enumerator: {ex}");
			}

			return new Enumerator(
					   o,
					   c,
					   () => enumerator.MoveNext(),
					   () => enumerator.Current,
					   () =>
			{
				var val = enumerator.Current;
				return (val, Com.ComObjType(val));
			},
			() => enumerator.Reset());
		}
	}
}
#endif
