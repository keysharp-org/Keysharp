namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Converts between binary data and RFC 4648 Base32 text. Scripts reach it through the Ks module:
		/// <c>#Import "Ks" { Base32 }</c>, then <c>Base32.Encode(Value)</c> and <c>Base32.Decode(Text)</c>.
		/// </summary>
		public class Base32 : KeysharpObject
		{
			/// <summary>
			/// Encodes binary data as uppercase Base32 text with padding.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">A <see cref="Buffer"/>, an <see cref="Array"/> of bytes, a <see cref="StringBuffer"/> or a string.</param>
			/// <param name="encoding">The encoding a string or StringBuffer <paramref name="value"/> is taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The Base32 text.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			/// <exception cref="TypeError">Thrown if the value holds no bytes.</exception>
			[Static]
			public static object Encode(object @this, object value, object encoding = null)
			{
				var enc = Files.GetEncodingOrDefault(encoding, System.Text.Encoding.UTF8);

				// A Buffer can be encoded without copying its memory to a managed byte array.
				if (value is Buffer b)
					return EncodeBytes(b.AsSpan());

				var raw = Conversions.ToByteArray(value, enc);
				return raw == null ? "" : EncodeBytes(raw);
			}

			/// <summary>
			/// Decodes Base32 text to bytes, accepting lowercase letters and omitted padding.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="text">The Base32 text to decode.</param>
			/// <returns>A <see cref="Buffer"/> holding the decoded bytes.</returns>
			/// <exception cref="ValueError">Thrown if the text is not well-formed Base32, including invalid
			/// characters, padding or nonzero unused bits.</exception>
			[Static]
			public static object Decode(object @this, object text)
			{
				var s = text.As();

				try
				{
					return new Buffer(DecodeBytes(s.AsSpan()));
				}
				catch (FormatException)
				{
					return Errors.ValueErrorOccurred("The text is not well-formed Base32.", s);
				}
			}

			private static string EncodeBytes(ReadOnlySpan<byte> bytes)
			{
				const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
				var encoded = new char[checked((int)(((long)bytes.Length + 4) / 5 * 8))];
				var accumulator = 0;
				var bits = 0;
				var index = 0;

				foreach (var value in bytes)
				{
					accumulator = (accumulator << 8) | value;
					bits += 8;

					while (bits >= 5)
					{
						bits -= 5;
						encoded[index++] = alphabet[(accumulator >> bits) & 31];
					}
				}

				if (bits > 0)
					encoded[index++] = alphabet[(accumulator << (5 - bits)) & 31];

				encoded.AsSpan(index).Fill('=');
				return new string(encoded);
			}

			private static byte[] DecodeBytes(ReadOnlySpan<char> text)
			{
				var data = text.TrimEnd('=');
				var remainder = data.Length % 8;
				var padding = text.Length - data.Length;

				if (remainder is 1 or 3 or 6 || (padding > 0 && (remainder == 0 || padding != 8 - remainder)))
					throw new FormatException();

				var decoded = new byte[(int)((long)data.Length * 5 / 8)];
				var accumulator = 0;
				var bits = 0;
				var index = 0;

				foreach (var ch in data)
				{
					var value = ch switch
					{
						>= 'A' and <= 'Z' => ch - 'A',
						>= 'a' and <= 'z' => ch - 'a',
						>= '2' and <= '7' => ch - '2' + 26,
						_ => throw new FormatException()
					};
					accumulator = (accumulator << 5) | value;
					bits += 5;

					if (bits >= 8)
					{
						bits -= 8;
						decoded[index++] = (byte)(accumulator >> bits);
					}
				}

				if ((accumulator & ((1 << bits) - 1)) != 0)
					throw new FormatException();

				return decoded;
			}
		}
	}
}
