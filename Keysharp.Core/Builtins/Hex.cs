namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Converts between binary data and hexadecimal text. Scripts reach it through the Ks module:
		/// <c>#Import "Ks" { Hex }</c>, then <c>Hex.Encode(Value)</c> and <c>Hex.Decode(Text)</c>.
		/// </summary>
		public class Hex : KeysharpObject
		{
			/// <summary>
			/// Encodes binary data as uppercase hexadecimal text.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">A <see cref="Buffer"/>, <see cref="StringBuffer"/> or string.</param>
			/// <param name="encoding">The encoding a string or StringBuffer <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The hexadecimal text, with two characters per byte and no separators or prefix.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object Encode(object @this, object value, object encoding = null)
			{
				var enc = Files.GetEncodingOrDefault(encoding, System.Text.Encoding.UTF8);

				// A Buffer can be encoded without copying its memory to a managed byte array.
				if (value is Buffer b)
					return Convert.ToHexString(b.AsSpan());

				var raw = Conversions.ToByteArray(value, enc);
				return raw == null ? "" : Convert.ToHexString(raw);
			}

			/// <summary>
			/// Decodes hexadecimal text to bytes, accepting uppercase and lowercase letters.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="text">An even number of hexadecimal characters, without whitespace or a prefix.</param>
			/// <returns>A <see cref="Buffer"/> holding the decoded bytes.</returns>
			/// <exception cref="ValueError">Thrown if the text is not well-formed hexadecimal.</exception>
			[Static]
			public static object Decode(object @this, object text)
			{
				var s = text.As();

				try
				{
					return new Buffer(Convert.FromHexString(s));
				}
				catch (FormatException)
				{
					return Errors.ValueErrorOccurred("The text is not well-formed hexadecimal.", s);
				}
			}
		}
	}
}
