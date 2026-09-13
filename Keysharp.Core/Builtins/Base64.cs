namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Converts between binary data and Base64 text. Scripts reach it through the KS module:
		/// <c>#Import "Ks" { Base64 }</c>, then <c>Base64.Encode(value)</c> and <c>Base64.Decode(text)</c>.
		/// </summary>
		public class Base64 : KeysharpObject
		{
			/// <summary>
			/// Encodes binary data as Base64 text.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">A <see cref="Buffer"/>, <see cref="StringBuffer"/> or string.</param>
			/// <param name="encoding">The encoding a string or StringBuffer <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The Base64 text.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object Encode(object @this, object value, object encoding = null)
			{
				var enc = Files.GetEncodingOrDefault(encoding, System.Text.Encoding.UTF8);//Resolved first, so a misspelled name is reported even where no string is being converted.

				//A Buffer encodes straight out of its own memory; the general byte conversion would copy the whole of it first.
				if (value is Buffer b)
					return Convert.ToBase64String(b.AsSpan());

				var raw = Conversions.ToByteArray(value, enc);
				return raw == null ? "" : Convert.ToBase64String(raw);
			}

			/// <summary>
			/// Decodes Base64 text to bytes.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="text">The Base64 text to decode.</param>
			/// <returns>A <see cref="Buffer"/> holding the decoded bytes.</returns>
			/// <exception cref="ValueError">Thrown if the text is not well-formed Base64.</exception>
			[Static]
			public static object Decode(object @this, object text)
			{
				var s = text.As();

				try
				{
					return new Buffer(Convert.FromBase64String(s));
				}
				catch (FormatException)
				{
					return Errors.ValueErrorOccurred("The text is not well-formed Base64.", s);
				}
			}
		}
	}
}
