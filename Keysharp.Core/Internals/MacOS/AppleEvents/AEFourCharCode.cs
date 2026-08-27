namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// Apple Events name everything with a four-character code packed into 32 bits, most significant byte first.
	/// Spaces count as characters, so "ID  " and "all " are four-character codes like any other. Unguarded so the
	/// sdef parser it serves can be tested off macOS.
	/// </summary>
	internal static class AEFourCharCode
	{
		internal static uint Pack(string code) => Pack(code.AsSpan());

		internal static uint Pack(ReadOnlySpan<char> code)
		{
			if (!TryPack(code, out var packed))
				throw new ArgumentException($"'{code}' is not a four-character code: it must be exactly four characters, each of them single-byte.");

			return packed;
		}

		internal static bool TryPack(ReadOnlySpan<char> code, out uint packed)
		{
			packed = 0;

			if (code.Length != 4)
				return false;

			for (var i = 0; i < 4; i++)
			{
				if (code[i] > 0xFF)
					return false;

				packed = (packed << 8) | code[i];
			}

			return true;
		}

		internal static string Unpack(uint code)
		{
			Span<char> chars =
			[
				(char)((code >> 24) & 0xFF),
				(char)((code >> 16) & 0xFF),
				(char)((code >> 8) & 0xFF),
				(char)(code & 0xFF)
			];
			return new string(chars);
		}
	}
}
