namespace Keysharp.Internals
{
	/// <summary>
	/// Parser for the 128-byte EDID 1.x base block, shared by every platform that can get its hands on the raw
	/// bytes: Windows reads them from the display device's registry key, Linux from
	/// <c>/sys/class/drm/*/edid</c> (world-readable, and identical under X11 and Wayland). macOS does not need
	/// this — CoreGraphics reports vendor/model/serial directly.
	/// <para>Only the fields Keysharp actually exposes are decoded. Anything the block does not contain stays
	/// empty/zero so callers can report "unknown" rather than a plausible-looking guess.</para>
	/// </summary>
	internal static class Edid
	{
		internal const int BlockSize = 128;

		private static ReadOnlySpan<byte> Magic => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

		/// <summary>Decoded subset of one EDID base block.</summary>
		internal readonly record struct Info(string Manufacturer, ushort ProductCode, uint SerialNumber,
			string ModelName, string SerialText, int WidthMm, int HeightMm)
		{
			/// <summary>
			/// The panel-identifying key: vendor + product code + whichever serial the panel reports. Empty when the
			/// block carried no vendor at all, so a caller can fall back to a platform device path instead of
			/// persisting something meaningless.
			/// </summary>
			internal string Key
			{
				get
				{
					if (string.IsNullOrEmpty(Manufacturer))
						return "";

					// The descriptor serial STRING is preferred over the numeric one: panels that ship a real
					// per-unit serial put it there, while the numeric field is often a constant across a model run.
					var serial = !string.IsNullOrEmpty(SerialText) ? SerialText
						: SerialNumber != 0 ? SerialNumber.ToString("X8") : "";
					return serial.Length > 0
						? $"{Manufacturer}{ProductCode:X4}-{serial}"
						: $"{Manufacturer}{ProductCode:X4}";
				}
			}

			/// <summary>Whether the key alone identifies one physical unit. False when the panel reported no serial,
			/// in which case the caller must add a connector/device-path disambiguator.</summary>
			internal bool KeyIsUnique => !string.IsNullOrEmpty(Manufacturer)
				&& (!string.IsNullOrEmpty(SerialText) || SerialNumber != 0);
		}

		/// <summary>
		/// Parses an EDID base block. Returns false for anything that is not a checksum-valid EDID 1.x block, so a
		/// truncated sysfs read or a stale registry blob is rejected rather than decoded into nonsense.
		/// </summary>
		internal static bool TryParse(ReadOnlySpan<byte> edid, out Info info)
		{
			info = default;

			if (edid.Length < BlockSize || !edid[..8].SequenceEqual(Magic))
				return false;

			byte sum = 0;

			for (var i = 0; i < BlockSize; i++)
				sum += edid[i];

			if (sum != 0)
				return false;

			var manufacturer = DecodeManufacturer((ushort)((edid[8] << 8) | edid[9]));
			var productCode = (ushort)(edid[10] | (edid[11] << 8));
			var serialNumber = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
			// Bytes 21/22 are the maximum image size in CENTIMETRES — a coarse fallback. The first detailed timing
			// descriptor carries the same size in millimetres, which is what a DPI calculation actually wants.
			// EDID 1.4 overloads this pair: when exactly ONE of the two is zero, the other holds an ASPECT RATIO
			// (landscape in byte 21, portrait in byte 22), not a size — reading it as centimetres reports a 16:9
			// panel as 790 mm wide and yields a nonsense DPI. Only a pair with both bytes set is a real size.
			var haveCoarseSize = edid[21] != 0 && edid[22] != 0;
			var widthMm = haveCoarseSize ? edid[21] * 10 : 0;
			var heightMm = haveCoarseSize ? edid[22] * 10 : 0;
			string modelName = "", serialText = "";
			var haveDetailedSize = false;

			for (var offset = 54; offset + 18 <= 126; offset += 18)
			{
				var descriptor = edid.Slice(offset, 18);

				// A non-zero pixel clock (bytes 0-1) marks a detailed timing descriptor rather than a text one.
				if (descriptor[0] != 0 || descriptor[1] != 0)
				{
					// Only the FIRST detailed timing is the preferred/native one, and it is the descriptor whose
					// image size describes the panel. A later detailed timing describes an alternate mode and must
					// not overwrite it.
					if (haveDetailedSize)
						continue;

					// Horizontal/vertical image size: low bytes at 12/13, high nibbles packed into byte 14.
					var detailedWidth = descriptor[12] | ((descriptor[14] & 0xF0) << 4);
					var detailedHeight = descriptor[13] | ((descriptor[14] & 0x0F) << 8);

					if (detailedWidth > 0 && detailedHeight > 0)
					{
						widthMm = detailedWidth;
						heightMm = detailedHeight;
						haveDetailedSize = true;
					}

					continue;
				}

				if (descriptor[2] != 0)
					continue;

				switch (descriptor[3])
				{
					case 0xFC: modelName = DecodeText(descriptor[5..]); break;
					case 0xFF: serialText = DecodeText(descriptor[5..]); break;
				}
			}

			info = new Info(manufacturer, productCode, serialNumber, modelName, serialText, widthMm, heightMm);
			return true;
		}

		/// <summary>
		/// Three 5-bit letters packed big-endian, 1 = 'A' (0x10AC -> "DEL"). Internal because macOS gets the same
		/// packed vendor id straight from CoreGraphics rather than from an EDID block, and must decode it
		/// identically or the same panel would report a different manufacturer there.
		/// </summary>
		internal static string DecodeManufacturer(ushort packed)
		{
			Span<char> chars =
			[
				(char)('A' - 1 + ((packed >> 10) & 0x1F)),
				(char)('A' - 1 + ((packed >> 5) & 0x1F)),
				(char)('A' - 1 + (packed & 0x1F)),
			];

			foreach (var c in chars)
				if (c is < 'A' or > 'Z')
					return "";

			return new string(chars);
		}

		/// <summary>Descriptor text is ASCII terminated by 0x0A and space-padded to 13 bytes.</summary>
		private static string DecodeText(ReadOnlySpan<byte> bytes)
		{
			var end = bytes.IndexOf((byte)0x0A);

			if (end < 0)
				end = bytes.Length;

			Span<char> chars = stackalloc char[end];
			var length = 0;

			for (var i = 0; i < end; i++)
			{
				var b = bytes[i];

				if (b is < 0x20 or > 0x7E)
					continue;

				chars[length++] = (char)b;
			}

			return new string(chars[..length]).Trim();
		}

		/// <summary>
		/// Maps a connector/output name ("DP-1", "HDMI-A-2", "eDP-1", "\\.\DISPLAY1") to the connection kind the
		/// script-facing <c>Connection</c> property reports. Returns "" when the name says nothing.
		/// </summary>
		internal static string ConnectionFromConnectorName(string name)
		{
			if (string.IsNullOrEmpty(name))
				return "";

			// Strip a DRM card prefix ("card0-DP-1") so both sysfs and RandR/Wayland spellings hit the same table.
			var dash = name.IndexOf('-');

			if (dash > 0 && name.StartsWith("card", StringComparison.OrdinalIgnoreCase))
				name = name[(dash + 1)..];

			if (name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase)) return "eDP";
			if (name.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase)) return "Internal";
			if (name.StartsWith("DSI", StringComparison.OrdinalIgnoreCase)) return "Internal";
			if (name.StartsWith("HDMI", StringComparison.OrdinalIgnoreCase)) return "HDMI";
			if (name.StartsWith("DP", StringComparison.OrdinalIgnoreCase)) return "DisplayPort";
			if (name.StartsWith("DisplayPort", StringComparison.OrdinalIgnoreCase)) return "DisplayPort";
			if (name.StartsWith("DVI", StringComparison.OrdinalIgnoreCase)) return "DVI";
			if (name.StartsWith("VGA", StringComparison.OrdinalIgnoreCase)) return "VGA";

			return "";
		}

		/// <summary>Whether a connection kind names a built-in panel.</summary>
		internal static bool IsInternalConnection(string connection)
			=> connection is "eDP" or "Internal";
	}
}
