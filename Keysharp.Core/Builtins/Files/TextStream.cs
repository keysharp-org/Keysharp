namespace Keysharp.Builtins
{
	// AutoHotkey's FileOpen access modes.
	internal enum TextStreamMode
	{
		Read = 0,
		Write = 1,
		Append = 2,
		Update = 3
	}

	// One byte buffer keeps text reads, raw reads and the position consistent. Incomplete characters stay
	// in this buffer rather than the decoder; line breaks follow AutoHotkey's raw CR/LF contract.
	internal sealed class TextStream : IDisposable
	{
		// AutoHotkey's FileOpen flag bits, which numeric flags give directly.
		internal const long EolCrlf = 0x4;
		internal const long EolOrphanCr = 0x8;
		internal const long ShareRead = 0x100;
		internal const long ShareWrite = 0x200;
		internal const long ShareDelete = 0x400;
		internal const long ShareAll = ShareRead | ShareWrite | ShareDelete;
		internal const long UseHandle = 0x10000000;

		// AutoHotkey's TEXT_IO_BLOCK, and a smaller block for memory, which is read afresh on every call.
		private const int BlockSize = 8192;
		private const int MemoryBlockSize = 256;

		private readonly Stream stream;
		private readonly long eolFlags;
		private readonly bool keepReadAhead;
		private System.Text.Encoding encoding;
		private System.Text.Decoder decoder;
		private System.Text.Encoder encoder;
		private bool dbcs;

		// UTF-16 is copied as the code units it holds, as AutoHotkey copies it, rather than decoded and encoded.
		private bool utf16;

		// Match line breaks in raw bytes, before decoding, using native byte order for multi-byte units.
		private int unit;
		private uint lf, cr;

		// Bytes read from the stream but not consumed yet, buffer[readPos..readEnd]; the stream stands just past them.
		private byte[] buffer;
		private int readPos, readEnd;

		private bool Stateful => !utf16 && !dbcs && encoding.CodePage is not (65001 or 1201 or 12000 or 12001) && !encoding.IsSingleByte;

		// The last character written, so that `n translation does not make \r\r\n of a \r\n split across two writes.
		private char lastWritten;

		// Memory the script can mutate must not retain read-ahead bytes between calls.
		internal TextStream(Stream stream, System.Text.Encoding encoding, long eolFlags, bool keepReadAhead = true)
		{
			this.stream = stream;
			this.eolFlags = eolFlags;
			this.keepReadAhead = keepReadAhead;
			Encoding = encoding;
		}

		internal bool AtEof => readPos == readEnd && (!stream.CanSeek || stream.Position >= stream.Length);

		internal Stream BaseStream => stream;

		internal bool DetectedByteOrderMark { get; private set; }

		// Changing the encoding leaves buffered bytes intact, as AutoHotkey's SetCodePage does.
		internal System.Text.Encoding Encoding
		{
			get => encoding;

			set
			{
				encoding = value;
				decoder = null;
				encoder = null;
				utf16 = value.CodePage == 1200;
				// These stateless Windows DBCS pages have ASCII line breaks, which cannot occur inside a character.
				dbcs = value.CodePage is 932 or 936 or 949 or 950 or 1361;
				Span<byte> bytes = stackalloc byte[16];
				var length = value.GetBytes("\n", bytes);
				unit = length is 2 or 4 ? length : 1;
				lf = ReadUnit(bytes);
				_ = value.GetBytes("\r", bytes);
				cr = ReadUnit(bytes);
			}
		}

		// SafeFileHandle flushes writes; unread bytes must first be rolled back to the script's position.
		internal nint Handle
		{
			get
			{
				if (stream is not FileStream file || !(file.CanRead || file.CanWrite))
					return 0;

				DiscardReadAhead();
				return file.SafeFileHandle.DangerousGetHandle();
			}
		}

		internal long Length
		{
			get => stream.CanSeek ? stream.Length : 0L;

			set
			{
				if (value < 0 || !stream.CanSeek || !stream.CanWrite)
					return;

				DiscardReadAhead();
				stream.SetLength(value);
			}
		}

		internal long Position => stream.CanSeek ? stream.Position - (readEnd - readPos) : -1L;

		internal static System.Text.Encoding DetectByteOrderMark(ReadOnlySpan<byte> bytes, out int length)
		{
			if (bytes is [0xFF, 0xFE, ..])
			{
				length = 2;
				return System.Text.Encoding.Unicode;
			}

			if (bytes is [0xEF, 0xBB, 0xBF, ..])
			{
				length = 3;
				return System.Text.Encoding.UTF8;
			}

			length = 0;
			return null;
		}

		// The previous write's last character prevents inserting a second CR into a split CRLF.
		internal static string InsertCarriageReturns(string text, char previous)
		{
			StringBuilder result = null;
			var start = 0;

			for (var i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
			{
				if ((i > 0 ? text[i - 1] : previous) == '\r')
					continue;

				_ = (result ??= new StringBuilder(text.Length + 16)).Append(text, start, i - start).Append('\r');
				start = i;
			}

			return result == null ? text : result.Append(text, start, text.Length - start).ToString();
		}

		internal static TextStream Open(string path, TextStreamMode mode, FileShare share, System.Text.Encoding encoding, long eolFlags, bool byteOrderMark)
		{
			var (fileMode, access) = mode switch
			{
				TextStreamMode.Read => (FileMode.Open, FileAccess.Read),
				TextStreamMode.Write => (FileMode.Create, FileAccess.Write),
				_ => (FileMode.OpenOrCreate, FileAccess.ReadWrite)
			};
			var file = new FileStream(path, fileMode, access, share);

			try
			{
				var text = new TextStream(file, encoding, eolFlags);

				// Appending to a pipe, which has no start to look at, must not wait for input to arrive.
				if (mode is TextStreamMode.Read or TextStreamMode.Update || mode == TextStreamMode.Append && file.CanSeek)
				{
					_ = text.Fill();
					var detected = DetectByteOrderMark(text.buffer.AsSpan(0, text.readEnd), out text.readPos);

					if (detected != null)
					{
						text.Encoding = detected;
						text.DetectedByteOrderMark = true;
					}
				}

				if (byteOrderMark && encoding.CodePage is 65001 or 1200
						&& (mode == TextStreamMode.Write || mode != TextStreamMode.Read && file.CanSeek && file.Length == 0))
					_ = text.WriteBytes(encoding.Preamble);

				if (mode == TextStreamMode.Append)
					_ = text.Seek(0, SeekOrigin.End);

				return text;
			}
			catch
			{
				file.Dispose();
				throw;
			}
		}

		// Console input is already decoded, so apply the same CRLF and orphan-CR flags to its characters.
		internal static string TranslateLineEndings(string text, long eolFlags)
		{
			if ((eolFlags & (EolCrlf | EolOrphanCr)) == 0 || !text.Contains('\r'))
				return text;

			var result = new StringBuilder(text.Length);

			for (var i = 0; i < text.Length; i++)
			{
				if (text[i] != '\r')
					_ = result.Append(text[i]);
				else if (i + 1 < text.Length && text[i + 1] == '\n')
				{
					if ((eolFlags & EolCrlf) == 0)
						_ = result.Append('\r');
				}
				else
					_ = result.Append((eolFlags & EolOrphanCr) != 0 ? '\n' : '\r');
			}

			return result.ToString();
		}

		public void Dispose()
		{
			stream.Dispose();
			readPos = readEnd = 0;
		}

		internal void Flush()
		{
			if (stream.CanWrite)
				stream.Flush();
		}

		// Only Read(1) splits a CRLF. An oversized surrogate/decoding group stays unread unless no text was read yet.
		internal string Read(int maxChars, bool stopAtLineEnd = false)
		{
			if (maxChars <= 0 || !stream.CanRead)
				return "";

			lastWritten = '\0';
			var text = new StringBuilder();

			while (text.Length < maxChars)
			{
				if (!EnsureAvailable(unit))
				{
					DecodeTail(text, maxChars - text.Length);
					break;
				}

				var bytes = buffer.AsSpan(readPos, readEnd - readPos);
				var lineBreak = IndexOfLineBreak(bytes);

				if (lineBreak != 0)
				{
					var end = lineBreak < 0 ? bytes.Length - bytes.Length % unit : lineBreak;

					if (lineBreak < 0)
						end -= IncompleteTail(bytes[..end]);

					var textBytes = bytes[..end];
					var used = Decode(text, textBytes, maxChars - text.Length, !Stateful, out var needsMore);

					if (needsMore && lineBreak >= 0)
						used += Decode(text, textBytes[used..], maxChars - text.Length, true, out needsMore);

					readPos += used;

					if (needsMore || textBytes.IsEmpty)
					{
						if (EnsureAvailable(readEnd - readPos + 1))
							continue;

						DecodeTail(text, maxChars - text.Length);
						break;
					}

					if (used < textBytes.Length)
						break;

					continue;
				}

				if (stopAtLineEnd)
				{
					ConsumeLineEnd();
					break;
				}

				if (UnitAt(readPos) == lf)
				{
					_ = text.Append('\n');
					readPos += unit;
				}
				else if (!EnsureAvailable(2 * unit) || UnitAt(readPos + unit) != lf)
				{
					_ = text.Append((eolFlags & EolOrphanCr) != 0 ? '\n' : '\r');
					readPos += unit;
				}
				else if ((eolFlags & EolCrlf) != 0)
				{
					_ = text.Append('\n');
					readPos += 2 * unit;
				}
				else if (maxChars - text.Length >= 2)
				{
					_ = text.Append("\r\n");
					readPos += 2 * unit;
				}
				else if (maxChars == 1)
				{
					_ = text.Append('\r');
					readPos += unit;
				}
				else
					break;
			}

			EndRead();
			return text.ToString();
		}

		internal int ReadBytes(Span<byte> destination)
		{
			if (!stream.CanRead)
				return 0;

			lastWritten = '\0';
			if (!destination.IsEmpty)
				decoder = null;

			var buffered = Math.Min(readEnd - readPos, destination.Length);
			buffer.AsSpan(readPos, buffered).CopyTo(destination);
			readPos += buffered;
			return buffered == destination.Length ? buffered : buffered + stream.Read(destination[buffered..]);
		}

		internal string ReadLine()
		{
			if (stream.CanRead && !Stateful && EnsureAvailable(unit))
			{
				var bytes = buffer.AsSpan(readPos, readEnd - readPos);
				var lineBreak = IndexOfLineBreak(bytes);

				if (lineBreak >= 0)
				{
					var line = utf16 ? new string(MemoryMarshal.Cast<byte, char>(bytes[..lineBreak])) : encoding.GetString(bytes[..lineBreak]);
					readPos += lineBreak;
					ConsumeLineEnd();
					lastWritten = '\0';
					EndRead();
					return line;
				}
			}

			return Read(int.MaxValue, true);
		}

		internal bool Seek(long distance, SeekOrigin origin)
		{
			if (!stream.CanSeek)
				return false;

			var target = origin switch
			{
				SeekOrigin.Begin => distance,
				SeekOrigin.Current => Position + distance,
				_ => stream.Length + distance
			};

			if (target < 0)
				return false;

			readPos = readEnd = 0;
			stream.Position = target;
			decoder = null;
			lastWritten = '\0';
			return true;
		}

		internal int WriteBytes(ReadOnlySpan<byte> data)
		{
			if (!stream.CanWrite)
				return 0;

			DiscardReadAhead();
			decoder = null;
			stream.Write(data);
			return data.Length;
		}

		internal long WriteText(string text)
		{
			if (text.Length == 0 || !stream.CanWrite)
				return 0L;

			var last = text[^1];

			if ((eolFlags & EolCrlf) != 0)
				text = InsertCarriageReturns(text, lastWritten);

			lastWritten = last;

			if (utf16)
				return WriteBytes(MemoryMarshal.AsBytes(text.AsSpan()));

			Span<byte> bytes = stackalloc byte[1024];
			encoder ??= encoding.GetEncoder();
			var chars = text.AsSpan();
			var written = 0L;
			bool completed;

			do
			{
				encoder.Convert(chars, bytes, true, out var charsUsed, out var bytesUsed, out completed);
				written += WriteBytes(bytes[..bytesUsed]);
				chars = chars[charsUsed..];
			}
			while (!completed);

			return written;
		}

		// AutoHotkey's RollbackFilePointer: moves the stream back to the first byte not consumed, so that a write or a
		// handle given out lands where the script's position says.
		private void DiscardReadAhead()
		{
			if (readEnd > readPos && stream.CanSeek)
				_ = stream.Seek(readPos - readEnd, SeekOrigin.Current);

			readPos = readEnd = 0;
		}

		private int Decode(StringBuilder text, ReadOnlySpan<byte> bytes, int limit, bool flush, out bool needsMore)
		{
			needsMore = false;

			if (bytes.IsEmpty)
				return 0;

			if (utf16)
			{
				var units = MemoryMarshal.Cast<byte, char>(bytes);
				var count = Math.Min(units.Length, limit);
				_ = text.Append(units[..count]);

				if (!flush || count == limit || bytes.Length % 2 == 0)
					return count * 2;

				_ = text.Append((char)0xFFFD);
				return bytes.Length;
			}

			Span<char> chars = stackalloc char[512];
			decoder ??= encoding.GetDecoder();

			if (dbcs && decoder.GetCharCount(bytes, flush) >= limit)
				bytes = bytes[..PrefixForCharacters(bytes, limit)];

			var used = 0;

			while (limit > 0)
			{
				var rest = bytes[used..];
				var room = Math.Min(chars.Length, limit);
				Span<char> output = chars[..room];
				var groupFlush = flush;

				if (Stateful)
				{
					rest = StatefulPrefix(rest, room, ref groupFlush, out var count);
					needsMore = rest.IsEmpty;

					if (needsMore || count > limit && text.Length > 0)
						break;

					output = count <= chars.Length ? chars : new char[count];
				}
				else if (limit == 1 && !rest.IsEmpty)
					return used + DecodeOne(text, rest, flush);

				decoder.Convert(rest, output, groupFlush, out var bytesUsed, out var charsUsed, out var completed);
				_ = text.Append(output[..charsUsed]);
				used += bytesUsed;
				limit -= charsUsed;

				if (used == bytes.Length && completed)
					break;
			}

			return used;
		}

		private int PrefixForCharacters(ReadOnlySpan<byte> bytes, int count)
		{
			if (count == 1 && decoder.GetCharCount(bytes[..1], false) > 0)
				return 1;

			var low = 1;
			var high = bytes.Length;

			while (low < high)
			{
				var middle = low + (high - low) / 2;

				if (decoder.GetCharCount(bytes[..middle], false) >= count)
					high = middle;
				else
					low = middle + 1;
			}

			return high;
		}

		// Stateful decoders may emit a character while retaining later character bytes, even with a full output.
		// Commit only complete decoding groups; a first oversized group is returned whole, like a surrogate pair.
		private ReadOnlySpan<byte> StatefulPrefix(ReadOnlySpan<byte> bytes, int room, scoped ref bool flush, out int count)
		{
			var end = room == 1 && decoder.GetCharCount(bytes[..1], false) == 1 && decoder.GetCharCount(bytes[..1], true) == 1 ? 1 : 0;
			var whole = end == 0 ? decoder.GetCharCount(bytes, false) : 1;

			if (end == 0)
				end = whole >= room ? PrefixForCharacters(bytes, room) : bytes.Length;

			count = decoder.GetCharCount(bytes[..end], false);

			if (count != decoder.GetCharCount(bytes[..end], true) || count > room)
			{
				end = whole > 0 ? PrefixForCharacters(bytes, 1) : bytes.Length;
				var growth = 1;

				while (end < bytes.Length && decoder.GetCharCount(bytes[..end], false) != decoder.GetCharCount(bytes[..end], true))
				{
					end += Math.Min(growth, bytes.Length - end);
					growth = growth <= int.MaxValue / 2 ? growth * 2 : int.MaxValue;
				}
			}

			var group = bytes[..end];
			flush &= end == bytes.Length;
			count = decoder.GetCharCount(group, false);

			if (!flush && count != decoder.GetCharCount(group, true))
				return [];

			count = decoder.GetCharCount(group, flush);
			return group;
		}

		// One character. A surrogate pair is left for the next read, as in AutoHotkey, unless the read has nothing else
		// to return, so that Read(1) still advances past it. GetCharCount leaves the decoder as it is, so it first finds
		// how many bytes the character takes.
		private int DecodeOne(StringBuilder text, ReadOnlySpan<byte> bytes, bool flush)
		{
			var length = 1;

			while (decoder.GetCharCount(bytes[..length], flush && length == bytes.Length) == 0 && length < bytes.Length)
				length++;

			Span<char> chars = stackalloc char[8];
			var decodedCount = encoding.GetChars(bytes[..length], chars);
			var pair = decodedCount == 2 && char.IsSurrogatePair(chars[0], chars[1]);

			if (pair && text.Length > 0)
				return 0;

			if (decodedCount > 1 && !pair)
			{
				length = Math.Min(unit, bytes.Length);
				decodedCount = encoding.GetChars(bytes[..length], chars);
			}

			_ = text.Append(chars[..decodedCount]);
			return length;
		}

		// The end of the stream: what is left of a character cut short decodes as a replacement character.
		private void DecodeTail(StringBuilder text, int limit)
		{
			readPos += Decode(text, buffer.AsSpan(readPos, readEnd - readPos), limit, true, out _);
		}

		private void EndRead()
		{
			if (!keepReadAhead)
				DiscardReadAhead();
		}

		// Reads until at least count bytes are unread, short only at the end of the stream.
		private bool EnsureAvailable(int count)
		{
			while (readEnd - readPos < count)
				if (Fill() == 0)
					return false;

			return true;
		}

		// AutoHotkey's ReadAtLeast: moves the unread bytes to the front of the buffer and reads more after them.
		private int Fill()
		{
			buffer ??= new byte[keepReadAhead ? BlockSize : MemoryBlockSize];
			var unread = readEnd - readPos;
			buffer.AsSpan(readPos, unread).CopyTo(buffer);
			readPos = 0;
			readEnd = unread;

			if (readEnd == buffer.Length)
				System.Array.Resize(ref buffer, checked(buffer.Length * 2));

			var read = stream.Read(buffer.AsSpan(readEnd));
			readEnd += read;
			return read;
		}

		// An unfinished character stays in the byte buffer rather than in the decoder.
		private int IncompleteTail(ReadOnlySpan<byte> bytes)
		{
			if (bytes.IsEmpty)
				return 0;

			if (dbcs)
			{
				decoder ??= encoding.GetDecoder();
				return decoder.GetCharCount(bytes, false) == decoder.GetCharCount(bytes, true) ? 0 : 1;
			}

			if (encoding.CodePage != 65001)
				return encoding.CodePage == 1201 && bytes[^2] is >= 0xD8 and <= 0xDB ? 2 : 0;

			for (var back = 1; back <= Math.Min(3, bytes.Length); back++)
			{
				var lead = bytes[^back];

				if ((lead & 0xC0) == 0x80)
					continue;

				var length = lead is >= 0xF0 and <= 0xF4 ? 4 : lead is >= 0xE0 and <= 0xEF ? 3 : lead is >= 0xC2 and <= 0xDF ? 2 : 1;
				return length > back ? back : 0;
			}

			return 0;
		}

		private int IndexOfLineBreak(ReadOnlySpan<byte> bytes)
		{
			var index = unit switch
			{
				1 => bytes.IndexOfAny((byte)lf, (byte)cr),
				2 => MemoryMarshal.Cast<byte, ushort>(bytes).IndexOfAny((ushort)lf, (ushort)cr),
				_ => MemoryMarshal.Cast<byte, uint>(bytes).IndexOfAny(lf, cr)
			};
			return index < 0 ? -1 : index * unit;
		}

		private uint ReadUnit(ReadOnlySpan<byte> bytes) => unit switch
		{
			1 => bytes[0],
			2 => MemoryMarshal.Read<ushort>(bytes),
			_ => MemoryMarshal.Read<uint>(bytes)
		};

		private uint UnitAt(int offset) => ReadUnit(buffer.AsSpan(offset));

		private void ConsumeLineEnd()
		{
			var isCr = UnitAt(readPos) == cr;
			readPos += unit;

			if (isCr && EnsureAvailable(unit) && UnitAt(readPos) == lf)
				readPos += unit;
		}
	}
}
