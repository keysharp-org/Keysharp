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
			else if (input is Any a)
				return true;

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
				var str = dd.ToString();

				if (double.IsFinite(dd) && dd == Math.Truncate(dd) && str.IndexOf('E') < 0 && str.IndexOf('e') < 0)
					return str + ".0";

				return str;
			}
			else if (input is Any)
			{
				if (input is Map map)
				{
					var buffer = new StringBuilder();
					_ = buffer.Append(BlockOpen);
					var first = true;

					foreach (var (k, v) in map)
					{
						if (first)
							first = false;
						else
							_ = buffer.Append(DefaultMulticast);

						_ = buffer.Append(DoubleQuote);
						_ = buffer.Append(ForceString(k));
						_ = buffer.Append(DoubleQuote);
						_ = buffer.Append(AssignPre);

						if (v == null)
						{
							_ = buffer.Append(NullTxt);
							continue;
						}

						var obj = v is System.Array || v is Map || v is KeysharpFunc;// Delegate;

						if (!obj)
							_ = buffer.Append(DoubleQuote);

						_ = buffer.Append(ForceString(v));

						if (!obj)
							_ = buffer.Append(DoubleQuote);
					}

					_ = buffer.Append(BlockClose);
					return buffer.ToString();
				}
				else if (input is Builtins.Array array)
				{
					var buffer = new StringBuilder();
					_ = buffer.Append(ArrayOpen);
					var first = true;

					foreach (var item in array)
					{
						if (first)
							first = false;
						else
							_ = buffer.Append(DefaultMulticast);

						_ = buffer.Append(ForceString(item));
					}

					_ = buffer.Append(ArrayClose);
					return buffer.ToString();
				}
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
