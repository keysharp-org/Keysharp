namespace Keysharp.Core
{
	public class Primitive : Any
	{
		internal static bool IsNative(object item) => item is string || item is long || item is double;
		internal static Type MapPrimitiveToNativeType(object item)
		{
			if (item is string)
				return typeof(Keysharp.Core.@String);
			else if (item is long)
				return typeof(Keysharp.Core.Integer);
			else
				return typeof(Keysharp.Core.Float);
		}
	}

	public class String : Primitive
	{
		/// <summary>
		/// Converts a value to a string.
		/// </summary>
		/// <param name="value">The value to convert.</param>
		/// <returns>The result of converting value to a string, or value itself if it was a string.</returns>
		public static string Call(object @this, object value) => value.As();
	}

	public class Integer : Primitive
	{
		/// <summary>
		/// Converts a numeric string or floating-point value to an integer.
		/// </summary>
		/// <param name="value">The object to be converted</param>
		/// <returns>The converted value as a long.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		public static object Call(object @this, object value)
		{
			if (value.ParseLong(out long l))
				return l;
			else if (value.ParseDouble(out double d, false, true))
				return (long)d;
			return Errors.TypeErrorOccurred(value, typeof(double));
		}
	}

	public class Float : Primitive
	{
		/// <summary>
		/// Converts a numeric string or integer value to a floating-point number.
		/// </summary>
		/// <param name="value">The object to be converted</param>
		/// <returns>The converted value as a double.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		public static object Call(object @this, object value)
		{
			try
			{
				return value is double d ? d : value.Ad();
			}
			catch (Exception)
			{
				return Errors.TypeErrorOccurred(value, typeof(double));
			}
		}
	}
}
