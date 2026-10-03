namespace Keysharp.Builtins
{
	/// <summary>
	/// A file opened for input/output. The C# type is named <c>KeysharpFile</c> to avoid colliding with
	/// <see cref="System.IO.File"/>; scripts see it as <c>File</c> via
	/// <see cref="UserDeclaredNameAttribute"/>.
	/// </summary>
	[UserDeclaredName("File")]
	public class KeysharpFile : KeysharpObject, IDisposable
	{
		private bool disposed = false;

		// A file on disk, a handle or memory, kept once closed, when it reads and writes nothing; null for the standard
		// streams and a process's pipes.
		private TextStream stream;

		// The standard streams and a process's pipes, which carry text only, with FileOpen's `n and `r flags for the
		// standard streams and the encoding they were opened with, which a File cannot change.
		private TextReader reader;

		private TextWriter writer;

		private readonly long eolFlags;

		private readonly System.Text.Encoding textEncoding;

		// Keeps a file's stream reachable until Dispose, as its own finalizer would otherwise close the file in the
		// same collection as this File, before a __Delete which may still write to it.
		private GCHandle streamRoot;

		// The object whose memory a memory-backed file is reading and writing. Held so that it cannot be
		// collected while this File still points into it; null for a path-backed file.
		private object memorySource;

		public object AtEOF => stream != null ? (stream.AtEof ? 1L : 0L) : reader != null && reader.Peek() == -1 ? 1L : 0L;

		internal TextStream Stream => stream;

		/// <summary>
		/// The encoding of the text methods, named as AutoHotkey names it: UTF-8, UTF-16 or CPnnn, never with -RAW,
		/// which only decides whether a new file gets a byte order mark.
		/// </summary>
		public object Encoding
		{
			get => (stream?.Encoding ?? textEncoding ?? System.Text.Encoding.Default).CodePage switch
			{
				65001 => "UTF-8",
				1200 => "UTF-16",
				var codePage => $"CP{codePage}"
			};

			set
			{
				if (Files.TryGetEncoding(value, out var encoding) && stream != null)
					stream.Encoding = encoding;
			}
		}

		// Only a file on disk has an OS handle; a memory-backed file reports 0, as an unopened one does.
		public object Handle => (long)(stream?.Handle ?? 0);

		public object Length
		{
			get => stream?.Length ?? 0L;

			set
			{
				if (value.CoerceLong(out var length) && stream != null)
					stream.Length = length;
			}
		}

		public object Pos
		{
			get => stream?.Position ?? 0L;
			set => Seek(value);
		}

		public KeysharpFile(params object[] args) : base(args) { }

		public KeysharpFile(StreamWriter sw) : this(null, sw) { }

		public KeysharpFile(StreamReader sr) : this(sr, null) { }

		internal KeysharpFile(StringReader reader) : this(reader, null) { }

		internal KeysharpFile(TextReader reader, TextWriter writer, long eolFlags = 0) : base(null)
		{
			this.reader = reader;
			this.writer = writer;
			this.eolFlags = eolFlags;
			// Text captured from a process is held as the UTF-16 it was decoded to.
			textEncoding = writer?.Encoding ?? (reader as StreamReader)?.CurrentEncoding
						   ?? (reader == Console.In ? Console.InputEncoding : System.Text.Encoding.Unicode);

			if (writer != null)
				FlushAtExit();
		}

		internal KeysharpFile(TextStream stream) : base(null)
		{
			this.stream = stream;
			streamRoot = GCHandle.Alloc(stream);
			FlushAtExit();
		}

		/// <summary>
		/// Initializes a File over memory the script already holds. <see cref="Files.FileOpen"/> opens a path.
		/// </summary>
		/// <param name="args">
		/// The source object, which must expose both <c>Ptr</c> and <c>Size</c> - a <see cref="Buffer"/> or a
		/// <see cref="Struct"/>, or any later type providing the pair - optionally followed by an encoding name
		/// for the text methods.
		/// </param>
		/// <returns>An empty value; the constructed object is the instance being initialized.</returns>
		/// <exception cref="ValueError">Thrown when no source is given.</exception>
		/// <exception cref="TypeError">Thrown when the source exposes no usable Ptr and Size.</exception>
		public override object __New(params object[] args)
		{
			if (args == null || args.Length == 0)
				return Errors.ValueErrorOccurred("File requires a source. Use FileOpen to open a path, or pass a Buffer to read and write its memory.");

			var source = args[0];

			// The same Ptr/Size duck typing RawRead and RawWrite already accept, so a Buffer, StringBuffer,
			// Struct or any future type exposing both works without naming it here.
			if (source == null
					|| !Reflections.TryGetPtrProperty(source, out var ptr) || ptr == 0
					|| !Reflections.TryGetSizeProperty(source, out var size) || size < 0)
				return Errors.TypeErrorOccurred(source, typeof(Buffer));

			// Qualified: this class has an Encoding property, which shadows the type name here.
			var encoding = System.Text.Encoding.UTF8;

			if (args.Length > 1 && args[1] != null && !Files.TryGetEncoding(args[1], out encoding))
				return DefaultObject;

			// Hold the source so its memory cannot be reclaimed while this File still points into it.
			memorySource = source;

			unsafe
			{
				// Fixed capacity: the memory belongs to the source object and cannot be grown, so a write past
				// the end is refused rather than silently reallocating.
				stream = new TextStream(new BorrowedMemoryStream((byte*)ptr, size), encoding, 0, keepReadAhead: false);
			}

			return DefaultObject;
		}

		/// <summary>
		/// The stream behind a memory-backed File. It borrows memory owned by another object, so it cannot
		/// grow. Its purpose beyond <see cref="UnmanagedMemoryStream"/> is to refuse an overlong write as a
		/// script error: the base class raises a .NET exception which would escape a script's try/catch.
		/// Every write funnels through these three overloads, which is why the bounds check lives here rather
		/// than in each of the File class's Write methods.
		/// </summary>
		private sealed unsafe class BorrowedMemoryStream : UnmanagedMemoryStream
		{
			internal BorrowedMemoryStream(byte* pointer, long length) : base(pointer, length, length, FileAccess.ReadWrite) { }

			public override void Write(byte[] buffer, int offset, int count)
			{
				EnsureRoom(count);
				base.Write(buffer, offset, count);
			}

			public override void Write(ReadOnlySpan<byte> buffer)
			{
				EnsureRoom(buffer.Length);
				base.Write(buffer);
			}

			public override void WriteByte(byte value)
			{
				EnsureRoom(1);
				base.WriteByte(value);
			}

			private void EnsureRoom(long count)
			{
				if (Position + count > Length)
					_ = Errors.ErrorOccurred($"Writing {count} byte(s) at position {Position} would pass the end of the {Length}-byte memory this File was opened over.");
			}
		}

		public object Close()
		{
			Dispose(false);
			return DefaultObject;
		}

		/// <summary>
		/// Flushes any buffered data to the underlying file or stream.
		/// </summary>
		public object Flush()
		{
			stream?.Flush();
			writer?.Flush();
			return DefaultObject;
		}

		private void FlushAtExit() => _ = Script.TheScript.FlowData.openFiles.TryAdd(this, null);

		// As AutoHotkey does at exit, flushes every File still open, such as one only a class's static property holds,
		// since nothing does once the process ends.
		internal static void FlushAll(Script script)
		{
			foreach (var (file, _) in script.FlowData.openFiles)
			{
				try
				{
					if (!file.disposed)
						_ = file.Flush();
				}
				catch
				{
				}
			}
		}

		internal virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				stream?.Dispose();
				reader?.Close();
				writer?.Close();

				if (streamRoot.IsAllocated)
					streamRoot.Free();

				// A closed File reads and writes nothing, as in AutoHotkey, rather than raising the reader's or
				// writer's ObjectDisposedException, which a script cannot catch. A closed stream already does.
				reader = null;
				writer = null;
				disposed = true;
			}
		}

		/// <summary>
		/// Reads raw bytes into memory and advances the file pointer.
		/// </summary>
		/// <param name="buffer">A Buffer-like object, or an address, which then needs <paramref name="bytes"/>.</param>
		/// <param name="bytes">How many bytes to read; omit to fill the buffer.</param>
		/// <returns>The number of bytes read.</returns>
		public object RawRead(object buffer, object bytes = null)
		{
			if (buffer is string)
				return Errors.TypeErrorOccurred(buffer, typeof(Buffer));

			if (!TryGetMemory(buffer, bytes, out var ptr, out var count))
				return DefaultObject;

			unsafe
			{
				return (long)(stream?.ReadBytes(new Span<byte>((void*)ptr, count)) ?? 0);
			}
		}

		/// <summary>
		/// Writes raw bytes and advances the file pointer.
		/// </summary>
		/// <param name="data">A Buffer-like object; a string, whose UTF-16 is written as it stands; or an address,
		/// which then needs <paramref name="bytes"/>.</param>
		/// <param name="bytes">How many bytes to write; omit to write all of <paramref name="data"/>. A string allows
		/// two more bytes than it holds, for its terminating null character.</param>
		/// <returns>The number of bytes written.</returns>
		public long RawWrite(object data, object bytes = null)
		{
			if (data is string s)
			{
				var text = MemoryMarshal.AsBytes(s.AsSpan());

				if (!TryGetByteCount(bytes, text.Length, text.Length + sizeof(char), out var length))
					return 0L;

				if (stream == null)
					return 0L;

				var written = (long)stream.WriteBytes(text[..Math.Min(length, text.Length)]);
				ReadOnlySpan<byte> terminator = [0, 0];
				return length > text.Length ? written + stream.WriteBytes(terminator[..(length - text.Length)]) : written;
			}

			if (!TryGetMemory(data, bytes, out var ptr, out var count))
				return 0L;

			unsafe
			{
				return stream?.WriteBytes(new ReadOnlySpan<byte>((void*)ptr, count)) ?? 0L;
			}
		}

		/// <summary>
		/// Reads characters and advances the file pointer.
		/// </summary>
		/// <param name="characters">How many characters to read; omit to read to the end of the stream.</param>
		/// <returns>The characters read, or an empty string once the end has been reached.</returns>
		public string Read(object characters = null)
		{
			if (!characters.CoerceLong(out var count, long.MaxValue))
				return "";

			if (count < 0)
				return (string)Errors.ValueErrorOccurred("Invalid character count", count, DefaultObject);

			var max = (int)Math.Min(count, int.MaxValue);

			if (stream != null)
				return stream.Read(max);

			if (reader == null || max == 0)
				return "";

			string text;

			if (characters is null)
				text = reader.ReadToEnd();
			else
			{
				var chars = new char[max];
				text = new string(chars, 0, reader.Read(chars, 0, max));
			}

			return TextStream.TranslateLineEndings(text, eolFlags);
		}

		public object ReadChar() => TryRead(out sbyte value) ? (long)value : DefaultObject;

		public object ReadDouble() => TryRead(out double value) ? value : DefaultObject;

		public object ReadFloat() => TryRead(out float value) ? (double)value : DefaultObject;

		public object ReadInt() => TryRead(out int value) ? (long)value : DefaultObject;

		public object ReadInt64() => TryRead(out long value) ? value : DefaultObject;

		/// <summary>
		/// Reads a line of text, without its line ending, and advances the file pointer.
		/// </summary>
		/// <returns>The line, or an empty string at the end of the file.</returns>
		public string ReadLine() => stream?.ReadLine() ?? reader?.ReadLine() ?? "";

		public object ReadShort() => TryRead(out short value) ? (long)value : DefaultObject;

		public object ReadUChar() => TryRead(out byte value) ? (long)value : DefaultObject;

		public object ReadUInt() => TryRead(out uint value) ? (long)value : DefaultObject;

		public object ReadUShort() => TryRead(out ushort value) ? (long)value : DefaultObject;

		/// <summary>
		/// Moves the file pointer.
		/// </summary>
		/// <param name="distance">The distance to move, in bytes.</param>
		/// <param name="origin">0 for the start, 1 for the current position or 2 for the end; omitted, 2 when
		/// <paramref name="distance"/> is negative and 0 otherwise.</param>
		/// <returns>1 if the pointer moved, otherwise 0.</returns>
		public object Seek(object distance, object origin = null)
		{
			if (!distance.CoerceLong(out var distanceVal))
				return DefaultObject;

			if (!origin.CoerceLong(out var originVal, distanceVal < 0 ? 2L : 0L))
				return DefaultObject;

			return originVal is >= 0 and <= 2 && stream != null && stream.Seek(distanceVal, (SeekOrigin)originVal) ? 1L : 0L;
		}

		public long Write(object @string) => @string.CoerceString(out var s) ? WriteText(s) : 0L;

		public long WriteChar(object num) => num.CoerceLong(out var n) ? WriteNumber((sbyte)n) : 0L;

		public long WriteDouble(object num) => num.CoerceDouble(out var d) ? WriteNumber(d) : 0L;

		public long WriteFloat(object num) => num.CoerceDouble(out var d) ? WriteNumber((float)d) : 0L;

		public long WriteInt(object num) => num.CoerceInt(out var n) ? WriteNumber(n) : 0L;

		public long WriteInt64(object num) => num.CoerceLong(out var n) ? WriteNumber(n) : 0L;

		/// <summary>
		/// Writes text followed by a line feed, which the `n flag of FileOpen turns into \r\n.
		/// </summary>
		/// <returns>The number of bytes written.</returns>
		public long WriteLine(object @string = null)
		{
			if (!@string.CoerceString(out var s))
				return 0L;

			var written = WriteText(s);

			// As in AutoHotkey, the line ending follows only text that was written.
			return written == 0 && s.Length != 0 ? 0L : written + WriteText("\n");
		}

		public long WriteShort(object num) => num.CoerceLong(out var n) ? WriteNumber((short)n) : 0L;

		public long WriteUChar(object num) => num.CoerceLong(out var n) ? WriteNumber((byte)n) : 0L;

		public long WriteUInt(object num) => num.CoerceLong(out var n) ? WriteNumber((uint)n) : 0L;

		public long WriteUShort(object num) => num.CoerceLong(out var n) ? WriteNumber((ushort)n) : 0L;

		void IDisposable.Dispose()
		{
			Dispose(true);
			HasFinalizer = false;
		}

		// RawRead's and RawWrite's byte count: at most max, and the whole of whole when omitted.
		private static bool TryGetByteCount(object bytes, long whole, long max, out int count)
		{
			count = 0;

			if (!bytes.CoerceLong(out var requested, whole))
				return false;

			if (requested < 0 || requested > max || requested > int.MaxValue)
			{
				_ = Errors.ValueErrorOccurred("Invalid byte count.", requested);
				return false;
			}

			count = (int)requested;
			return true;
		}

		// AutoHotkey's RawX: the memory of a Buffer-like object, or an address, which needs an explicit byte count.
		private static bool TryGetMemory(object target, object bytes, out long ptr, out int count)
		{
			count = 0;

			// AutoHotkey's sanity check: no valid address lies in the lowest 64 KB.
			if (!Reflections.TryGetPtrProperty(target, out ptr) || (ulong)ptr < 65536)
			{
				_ = Errors.ValueErrorOccurred("Invalid buffer.");
				return false;
			}

			if (Reflections.TryGetSizeProperty(target, out var size))
				return TryGetByteCount(bytes, size, size, out count);

			if (bytes is null)
			{
				_ = Errors.ValueErrorOccurred("A byte count is required with an address.");
				return false;
			}

			return TryGetByteCount(bytes, 0, int.MaxValue, out count);
		}

		// As in AutoHotkey, a number cut short by the end of the file keeps zeros in its missing bytes, and nothing
		// read at all gives an empty value.
		private bool TryRead<T>(out T value) where T : unmanaged
		{
			value = default;
			return stream != null && stream.ReadBytes(MemoryMarshal.AsBytes(new Span<T>(ref value))) > 0;
		}

		private long WriteNumber<T>(T value) where T : unmanaged => stream?.WriteBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value))) ?? 0L;

		private long WriteText(string s)
		{
			if (stream != null)
				return stream.WriteText(s);

			if (writer == null)
				return 0L;

			if ((eolFlags & TextStream.EolCrlf) != 0)
				s = TextStream.InsertCarriageReturns(s, '\0');

			writer.Write(s);
			return writer.Encoding.GetByteCount(s);
		}
	}
}
