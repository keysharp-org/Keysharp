using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		public static bool ForceBool(object input)
		{
			if (input == null)
				return (bool)Errors.UnsetErrorOccurred("input", false);

			if (input is bool b)
				return b;
			else if (input is Any)
			{
				if (!TheScript.Operators.TryInvoke(OperatorKind.TruthTest, input, null, out var result))
					return true;

				return result is bool value ? value : ForceBool(result);
			}

			if (input.TryParseBool(out bool pb))
				return pb;
			else if (input.TryParseLong(out long l))
				return l != 0;
			else if (input.TryParseDouble(out double d, true))
				return d != 0.0;
			else if (input is string s)
				return !string.IsNullOrEmpty(s);

			return true;//Any non-null, non-empty string is considered true.
		}

		// AHK's FTOA: 17 significant digits, and ".0" on a finite number which prints with neither a point nor an exponent.
		internal static string FormatFloat(double d)
		{
			var str = d.ToString("G17", CultureInfo.InvariantCulture).Replace('E', 'e');
			return double.IsFinite(d) && str.AsSpan().IndexOfAny('.', 'e') < 0 ? str + ".0" : str;
		}

		// Per thread, since a nested collection's own ToString is reached through script dispatch.
		[ThreadStatic]
		private static HashSet<object> formattingCollections;

		/// <summary>
		/// An Array as [a, b] and a Map as [key: value], with strings quoted and a collection that contains itself shown as
		/// [...] where it recurs. The collections' own ToString passes <paramref name="callToString"/>, so an element with a
		/// ToString method shows its result, as String gives it; the diagnostic form runs no script code. Neither raises,
		/// since C# code, error messages included, formats a collection through its ToString too.
		/// </summary>
		internal static string FormatCollection(object collection, bool callToString)
		{
			var formatting = formattingCollections ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

			if (!formatting.Add(collection))
				return "[...]";

			var buffer = new StringBuilder("[");

			try
			{
				if (collection is Map map)
				{
					var first = true;

					foreach (var (k, v) in map)
					{
						_ = buffer.Append(first ? "" : ", ").Append(Element(k)).Append(": ").Append(Element(v));
						first = false;
					}
				}
				else
				{
					// By index, since an element's ToString may change the array.
					var items = ((Builtins.Array)collection).array;

					for (var i = 0; i < items.Count; i++)
						_ = buffer.Append(i > 0 ? ", " : "").Append(Element(items[i]));
				}
			}
			finally
			{
				_ = formatting.Remove(collection);
			}

			return buffer.Append(']').ToString();

			string Element(object v) => v switch
			{
				null => "unset",
				string s => "\"" + s + "\"",
				Any any when callToString => ElementText(any),
				Builtins.Array or Map => FormatCollection(v, callToString),
				_ => Errors.Describe(v)
			};
		}

		// An element's ToString result, or its type without one. What the element raises is contained as a try contains it,
		// unreported, and shown as <ERROR>; an Exit still ends the thread.
		private static string ElementText(Any element)
		{
			try
			{
				using var _ = Flow.EnterTry();
				if (!TryInvoke(element, "ToString", out var text))
					return Types.Type(element);

				return text is Any || !text.CoerceString(out var str) ? "<ERROR>" : str;
			}
			catch (Exception ex) when (!Internals.Flow.TryGetException<Builtins.Flow.UserRequestedExitException>(ex, out _))
			{
				return "<ERROR>";
			}
		}
	}
}
