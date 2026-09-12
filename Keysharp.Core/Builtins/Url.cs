namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Percent-encoding, as URLs and form bodies use it. Scripts reach it through the KS module:
		/// <c>#Import "Ks" { Url }</c>, then <c>Url.Encode(text)</c> and <c>Url.Decode(text)</c>.
		/// <para>Named Url rather than Uri so the CLR's own <see cref="System.Uri"/>, reachable through
		/// <c>Ks.Clr</c>, keeps its name.</para>
		/// </summary>
		public class Url : KeysharpObject
		{
			private const string HexUpper = "0123456789ABCDEF";

			/// <summary>The RFC 3986 unreserved set, which is never escaped.</summary>
			private static bool IsUnreserved(byte b) =>
				(b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9')
				|| b == '-' || b == '.' || b == '_' || b == '~';

			/// <summary>
			/// Percent-encodes everything outside the RFC 3986 unreserved set, which is the escaping a path
			/// segment, a query value and a form field each need.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="text">The text to encode.</param>
			/// <param name="encoding">The encoding the text's bytes are taken in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The percent-encoded text. A space becomes <c>%20</c>, never <c>+</c>, which means a space
			/// only inside a form body and would corrupt a path or a query value.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			[Static]
			public static object Encode(object @this, object text, object encoding = null)
			{
				var s = text.As();
				var enc = Files.GetEncodingOrDefault(encoding, System.Text.Encoding.UTF8);

				if (s.Length == 0)
					return "";

				// An already-safe segment is the common case, and under an encoding that agrees with ASCII the
				// string answers for its own bytes, so nothing has to be encoded to find out. Width alone is not
				// enough: a single-byte encoding need not map A-Z to the ASCII values.
				if (enc is UTF8Encoding or ASCIIEncoding)
				{
					var clean = true;

					foreach (var c in s)
						if (c > 127 || !IsUnreserved((byte)c))
						{
							clean = false;
							break;
						}

					if (clean)
						return s;
				}

				// Escaping is per byte, not per character: a character outside the unreserved set is escaped as
				// each of the bytes the encoding gives it, which is the only form a URL can carry.
				var bytes = enc.GetBytes(s);
				var escapes = 0;

				foreach (var b in bytes)
					if (!IsUnreserved(b))
						escapes++;

				if (escapes == 0)
					return s;

				var sb = new StringBuilder(bytes.Length + (escapes * 2));

				foreach (var b in bytes)
				{
					if (IsUnreserved(b))
						_ = sb.Append((char)b);
					else
						_ = sb.Append('%').Append(HexUpper[b >> 4]).Append(HexUpper[b & 0xF]);
				}

				return sb.ToString();
			}

			/// <summary>
			/// Resolves percent-escapes.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="text">The text to decode.</param>
			/// <param name="encoding">The encoding the escaped bytes are read in, named as for
			/// <see cref="A_FileEncoding"/>. Defaults to UTF-8.</param>
			/// <returns>The decoded text. A <c>%</c> not followed by two hexadecimal digits stands for itself, as
			/// a real URL commonly carries one, and so does <c>+</c>, which means a space only inside a form
			/// body.</returns>
			/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
			[Static]
			public static object Decode(object @this, object text, object encoding = null)
			{
				var s = text.As();
				var enc = Files.GetEncodingOrDefault(encoding, System.Text.Encoding.UTF8);

				if (s.IndexOf('%') < 0)
					return s;

				var sb = new StringBuilder(s.Length);
				var run = new List<byte>(s.Length);

				// A run of bytes is decoded in one go, so a character spelled as several escapes survives. An
				// unreserved character joins the run as its own byte, which is what lets a wide encoding round-trip:
				// Encode("ab", "UTF-16") is "a%00b%00", where each character is half literal and half escape.
				void FlushRun()
				{
					if (run.Count == 0)
						return;

					_ = sb.Append(enc.GetString(run.ToArray()));
					run.Clear();
				}

				for (var i = 0; i < s.Length; i++)
				{
					if (s[i] == '%' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
					{
						run.Add((byte)((Uri.FromHex(s[i + 1]) << 4) | Uri.FromHex(s[i + 2])));
						i += 2;
						continue;
					}

					if (s[i] < 128)
					{
						run.Add((byte)s[i]);
						continue;
					}

					// Encode never leaves a character above ASCII unescaped, so one here is text the caller put
					// there. It is already a character and is carried across whole, keeping a surrogate pair intact.
					FlushRun();
					_ = sb.Append(s[i]);
				}

				FlushRun();
				return sb.ToString();
			}
		}
	}
}
