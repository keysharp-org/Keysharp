namespace Keysharp.Scripting
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

			var pb = input.ParseBool();

			if (pb.HasValue)
				return pb.Value;
			else if (input.ParseLong(out long l, false, false))
				return l != 0;
			else if (input.ParseDouble(out double d, false, true))
				return d != 0.0;
			else if (input is string s)
				return !string.IsNullOrEmpty(s);
			else if (input.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public) is MethodInfo[] mis)
			{
				foreach (var mi in mis)
					if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(bool))
						return (bool)mi.Invoke(input, [input]);
					else if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(long))
						return (long)mi.Invoke(input, [input]) != 0;
			}

			return true;//Any non-null, non-empty string is considered true.
		}

		internal static double ForceDouble(object input)
		{
			if (input == null)
				return 0.0D;
			else if (input.ParseDouble(out double d, false, true))
				return d;
			else if (input.ParseLong(out long l, false, false))
				return l;
			else if (input.ParseBool() is bool b)
				return b ? 1.0 : 0.0;
			else if (input is string)
				return 0.0D;
			else if (input.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public) is MethodInfo[] mis)
			{
				foreach (var mi in mis)
					if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(double))
						return (double)mi.Invoke(input, [input]);
			}
			else if (input is IConvertible)
				return Convert.ToDouble(input);

			return 0.0D;
		}

		internal static int ForceInt(object input)
		{
			if (input == null)
				return 0;
			else if (input.ParseLong(out long l, false, false))
				return (int)l;
			else if (input.ParseDouble(out double d, false, true))
				return (int)d;
			else if (input.ParseBool() is bool b)
				return b ? 1 : 0;
			else if (input is string)
				return 0;
			else if (input.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public) is MethodInfo[] mis)
			{
				foreach (var mi in mis)
					if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(int))
						return (int)mi.Invoke(input, [input]);
			}
			else if (input is IConvertible)
				return Convert.ToInt32(input);

			return 0;
		}

		public static long ForceLong(object input)
		{
			if (input == null)
				return 0L;
			else if (input.ParseLong(out long l, false, false))
				return l;
			else if (input.ParseDouble(out double d, false, true))
				return (long)d;
			else if (input.ParseBool() is bool b)
				return b ? 1L : 0L;
			else if (input is string)
				return 0L;
			else if (input.GetType().GetMethods(BindingFlags.Static | BindingFlags.Public) is MethodInfo[] mis)
			{
				foreach (var mi in mis)
					if (mi.Name == "op_Implicit" && mi.ReturnType == typeof(long))
						return (long)mi.Invoke(input, [input]);
			}
			else if (input is IConvertible)
				return Convert.ToInt64(input);

			return 0L;
		}

		public static string ForceString(object input)
		{
			if (input == null)
				return string.Empty;
			else if (input is string s)
				return s;
			else if (input is char c)
				return c.ToString();
			else if (input is bool b)
				return b ? "1" : "0";
			else if (input is byte[] arr)
				return Encoding.Unicode.GetString(arr);
			else if (input is decimal m)
				return m.ToString();
			else if (input is FuncObj fo)
				return fo.Name;
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

					var result = simple ? ForceLong(input).ToString(format) : ((int)(double)input).ToString("d");

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

					_ = buffer.Append(StringBound);
					_ = buffer.Append(ForceString(k));
					_ = buffer.Append(StringBound);
					_ = buffer.Append(AssignPre);

					if (v == null)
					{
						_ = buffer.Append(NullTxt);
						continue;
					}

					var obj = v is System.Array || v is Map || v is FuncObj;// Delegate;

					if (!obj)
						_ = buffer.Append(StringBound);

					_ = buffer.Append(ForceString(v));

					if (!obj)
						_ = buffer.Append(StringBound);
				}

				_ = buffer.Append(BlockClose);
				return buffer.ToString();
			}
			else if (input is Core.Array array)
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
			else
				return input.ToString();
		}

		internal static object ForceType(Type requested, object value)
		{
			if (requested == typeof(object) || requested.IsAssignableFrom(value.GetType()))
				return value;

			if (requested == typeof(double))
				return ForceDouble(value);

			//if (requested == typeof(int))
			//  return ForceInt(value);

			if (requested == typeof(long))
				return ForceLong(value);

			return requested == typeof(string) ? ForceString(value) : value;
		}
	}
}