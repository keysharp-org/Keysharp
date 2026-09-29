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

		// An Array or Map that contains itself prints "[...]" where it recurs instead of recursing until the stack overflows.
		private static string FormatCollection(object collection, HashSet<object> open)
		{
			open ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

			if (!open.Add(collection))
				return "[...]";

			string Element(object v) => v is Map or Builtins.Array ? FormatCollection(v, open) : ForceString(v);
			var buffer = new StringBuilder();
			var first = true;

			try
			{
				if (collection is Map map)
				{
					_ = buffer.Append(BlockOpen);

					foreach (var (k, v) in map)
					{
						if (first)
							first = false;
						else
							_ = buffer.Append(DefaultMulticast);

						_ = buffer.Append(DoubleQuote).Append(ForceString(k)).Append(DoubleQuote).Append(AssignPre);

						if (v == null)
						{
							_ = buffer.Append(NullTxt);
							continue;
						}

						var obj = v is System.Array || v is Map || v is KeysharpFunc;

						if (!obj)
							_ = buffer.Append(DoubleQuote);

						_ = buffer.Append(Element(v));

						if (!obj)
							_ = buffer.Append(DoubleQuote);
					}

					_ = buffer.Append(BlockClose);
				}
				else
				{
					_ = buffer.Append(ArrayOpen);

					foreach (var item in (Builtins.Array)collection)
					{
						if (first)
							first = false;
						else
							_ = buffer.Append(DefaultMulticast);

						_ = buffer.Append(Element(item));
					}

					_ = buffer.Append(ArrayClose);
				}
			}
			finally
			{
				_ = open.Remove(collection);
			}

			return buffer.ToString();
		}

		public static string ForceString(object input)
		{
			if (input == null)
				return string.Empty;
			else if (input is string s)
				return s;
			else if (input is bool b)
				return b ? "1" : "0";
			else if (input is long l)
				return l.ToString();
			else if (input is double dd)
			{
				// AHK's FTOA: 17 significant digits, and ".0" on a finite number which prints with neither a point nor an exponent.
				var str = dd.ToString("G17", CultureInfo.InvariantCulture).Replace('E', 'e');
				return double.IsFinite(dd) && str.AsSpan().IndexOfAny('.', 'e') < 0 ? str + ".0" : str;
			}
			else if (input is Any)
			{
				if (input is Map or Builtins.Array)
					return FormatCollection(input, null);
				else if (input is KeysharpFunc fo)
					return fo.Name;
				else
					return input.ToString();
			}
			else if (input is char c)
				return c.ToString();
			else if (input is byte[] arr)
				return Encoding.Unicode.GetString(arr);
			else if (input is decimal m)
				return m.ToString();
			else if (IsNumeric(input))
			{
				var t = input.GetType();
				var simple = t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(byte) || t == typeof(char);
				var integer = simple || (t == typeof(double) && Math.IEEERemainder((double)input, 1) == 0);
				var format = "f";
				var hex = false;// format.Contains('x');
				const string hexpre = "0x";

				if (integer)
				{
					if (!hex)
						format = "d";

					var result = simple ? Convert.ToInt64(input).ToString(format) : ((int)(double)input).ToString("d");

					if (hex)
						result = hexpre + result;

					return result;
				}

				var d = (double)input;

				if (hex)
				{
					var result = d.ToString("X");
					return hexpre + result;
				}

				return d.ToString(format).TrimEnd(zerochars);//Remove trailing zeroes for string compare.
			}
			else if (input.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public) is MethodInfo[] mis)
			{
				foreach (var mi in mis)
					if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(string))
						return (string)mi.Invoke(input, [input]);
			}
			return input.ToString();
		}
	}
}
