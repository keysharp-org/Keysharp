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
		private Encoding enc;

		private int eolconv = 0;

		private BinaryReader br;

		private BinaryWriter bw;

		private bool disposed = false;

		// Any stream, not just a FileStream: a File can also be opened over memory a script already holds.
		private Stream fs;

		// The object whose memory a memory-backed file is reading and writing. Held so that it cannot be
		// collected while this File still points into it; null for a path-backed file.
		private object memorySource;

		private TextReader tr;

		private TextWriter tw;

		public object AtEOF
		{
			get
			{
				// Compare the position with the length rather than decoding a character: the content may be
				// binary and therefore not valid text in the current encoding, and the documented meaning of
				// AtEOF is that the file pointer has reached the end. A non-seekable stream has no meaningful
				// end, which the documentation already calls out, so it reports 0.
				if (br != null)
					return br.BaseStream.CanSeek && br.BaseStream.Position >= br.BaseStream.Length ? 1L : 0L;
				else if (tr != null)
					return tr.Peek() == -1 ? 1L : 0L;
				else
					return 0L;
			}
		}

		/// <summary>
		/// The stream the file reads and writes, so a hash can stream the content instead of loading all of
		/// it into memory. Null until the file is opened.
		/// </summary>
		internal Stream BaseStream => fs;

		public object Encoding
		{
			get => enc.BodyName;
			set => enc = Files.GetEncoding(value);
		}

		// Only a file on disk has an OS handle; a memory-backed file reports 0, as an unopened one does.
		public object Handle => fs is FileStream ffs ? ffs.SafeFileHandle.DangerousGetHandle().ToInt64() : 0L;

		public object Length
		{
			get => fs != null ? fs.Length : 0L;
			set => fs?.SetLength(value.Al());
		}

		public object Pos
		{
			get
			{
				if (br != null)
					return br.BaseStream.Position;
				else if (bw != null)
					return bw.BaseStream.Position;
				else
					return 0L;
			}

			set => Seek(value);
		}
		public KeysharpFile(params object[] args) : base(args) { }

		public KeysharpFile(StreamWriter sw) : base(null)
		{
			tw = sw;
			enc = sw.Encoding;
		}

		public KeysharpFile(StreamReader sr) : base(null)
		{
			tr = sr;
			enc = sr.CurrentEncoding;
		}

		/// <summary>
		/// Initializes a File over memory the script already holds.
		/// </summary>
		/// <param name="args">
		/// The source object, which must expose both <c>Ptr</c> and <c>Size</c> - a <see cref="Buffer"/> or a
		/// <see cref="Struct"/>, or any later type providing the pair - optionally followed by an encoding name
		/// for the text methods. <see cref="Files.FileOpen"/> supplies a fully resolved parameter set instead,
		/// which is what opens a path.
		/// </param>
		/// <returns>An empty value; the constructed object is the instance being initialized.</returns>
		/// <exception cref="ValueError">Thrown when no source is given.</exception>
		/// <exception cref="TypeError">Thrown when the source exposes no usable Ptr and Size.</exception>
		public override object __New(params object[] args)
		{
			if (args == null || args.Length == 0)
				return Errors.ValueErrorOccurred("File requires a source. Use FileOpen to open a path, or pass a Buffer to read and write its memory.");

			// FileOpen routes through here with the open parameters already resolved; anything else is a script
			// calling File() directly, which means a memory source.
			if (args.Length < 6 || args[1] is not FileMode)
				return NewOverMemory(args);

			var filename = args[0].As();
			var m = (FileMode)args[1];
			var a = (FileAccess)args[2];
			var s = (FileShare)args[3];
			enc = (Encoding)args[4];
			eolconv = (int)args[5].Al();

			if (filename == "*")
			{
				if ((a & FileAccess.Read) == FileAccess.Read)
					tr = Console.In;

				if ((a & FileAccess.Write) == FileAccess.Write)
					tw = Console.Out;
			}
			else if (filename == "**")
			{
				if ((a & FileAccess.Read) == FileAccess.Read)
					tr = Console.In;

				if ((a & FileAccess.Write) == FileAccess.Write)
					tw = Console.Error;
			}
			else
			{
				var exists = false;

				if (filename.StartsWith("h*", StringComparison.OrdinalIgnoreCase))
				{
					var handleString = filename.Substring(2);
					var handle = handleString.ParseLong();

					if (handle.HasValue)
					{
						exists = true;
						fs = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(new nint(handle.Value), false), a, 4096);
					}
				}
				else
				{
					if (System.IO.File.Exists(filename))
						exists = true;

					fs = new FileStream(filename, m, a, s);
				}

				if ((a & FileAccess.Read) == FileAccess.Read)
					br = new BinaryReader(fs, enc);

				if ((a & FileAccess.Write) == FileAccess.Write)
					bw = new BinaryWriter(fs, enc);

				if (!exists && bw != null)
				{
					if (enc is UTF8Encoding u8)
					{
						if (u8.Preamble.Length > 0)
							bw.Write(u8.Preamble);
					}
					else if (enc is UnicodeEncoding u16)
					{
						if (u16.Preamble.Length > 0)
							bw.Write(u16.Preamble);
					}
				}
				else if (exists && br != null)
				{
					if (enc is UTF8Encoding u8)
					{
						if (u8.Preamble.Length > 0)
							_ = br.BaseStream.Seek(u8.Preamble.Length, SeekOrigin.Begin);
					}
					else if (enc is UnicodeEncoding u16)
					{
						if (u16.Preamble.Length > 0)
							_ = br.BaseStream.Seek(u16.Preamble.Length, SeekOrigin.Begin);
					}
				}
			}

			return DefaultObject;
		}

		/// <summary>
		/// Opens this File over the memory of an object exposing Ptr and Size, so that the read, write, seek
		/// and position members operate on that memory instead of a file on disk.
		/// </summary>
		/// <param name="args">The source object, optionally followed by an encoding name.</param>
		/// <returns>An empty value.</returns>
		private object NewOverMemory(params object[] args)
		{
			var source = args[0];

			// The same Ptr/Size duck typing RawRead and RawWrite already accept, so a Buffer, StringBuffer,
			// Struct or any future type exposing both works without naming it here.
			if (source == null
					|| !Reflections.TryGetPtrProperty(source, out var ptr) || ptr == 0
					|| !Reflections.TryGetSizeProperty(source, out var size) || size < 0)
				return Errors.TypeErrorOccurred(source, typeof(Buffer));

			// Qualified: this class has an Encoding property, which shadows the type name here.
			enc = args.Length > 1 && args[1] != null ? Files.GetEncoding(args[1]) : System.Text.Encoding.UTF8;
			// Hold the source so its memory cannot be reclaimed while this File still points into it.
			memorySource = source;

			unsafe
			{
				// Fixed capacity: the memory belongs to the source object and cannot be grown, so a write past
				// the end is refused rather than silently reallocating.
				fs = new BorrowedMemoryStream((byte*)ptr, size);
			}

			br = new BinaryReader(fs, enc);
			bw = new BinaryWriter(fs, enc);
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

		internal KeysharpFile(string filename, FileMode mode, FileAccess access, FileShare share, Encoding encoding, long eol) : base(filename, mode, access, share, encoding, eol) { }

		~KeysharpFile() => Dispose(false);

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
			bw?.Flush();
			tw?.Flush();
			fs?.Flush();
			return DefaultObject;
		}

		internal virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				br?.Close();
				bw?.Close();
				tr?.Close();
				tw?.Close();
				fs?.Close();
				disposed = true;
			}
		}

		public object RawRead(object buffer, object bytes = null)
		{
			var buf = buffer;
			var count = (bytes is null ? long.MinValue : bytes.ToLong());
			int len = 0;

			if (br != null)
			{
				byte[] val;

				if (buf is Array arr)
				{
					val = count != long.MinValue ? br.ReadBytes((int)count) : br.ReadBytes(arr.Count);
					len = Math.Min(val.Length, arr.Count);

					for (var i = 0; i < len; i++)
						arr.array[i] = val[i];//Access the underlying ArrayList directly for performance.
				}
				else if (Reflections.TryGetPtrProperty(buf, out var ptr))
				{
					int buflen = Reflections.TryGetSizeProperty(buf, out var sz) ? (int)sz : int.MinValue;
					len = count == long.MinValue ? buflen : (buflen != int.MinValue ? Math.Min((int)count, buflen) : (int)count);
					if (len < 0) return Errors.ErrorOccurred("Invalid byte count");

					val = br.ReadBytes(len);
					len = Math.Min(val.Length, len);
					unsafe
					{
						var byteArr = (byte*)(nint)ptr;

						for (var i = 0; i < len; i++)
							byteArr[i] = val[i];
					}
				}
				else
					return Errors.ErrorOccurred("Invalid buffer");
			}
			return (long)len;
		}

		public long RawWrite(object data, object bytes = null)
		{
			var buf = data;
			var count = (bytes is null ? long.MinValue : bytes.ToLong());
			var len = 0;

			if (bw != null)
			{
				if (buf is Array arr)
				{
					len = count != long.MinValue ? Math.Min(arr.Count, (int)count) : arr.Count;
					bw.Write(arr.array.ConvertAll(el => (byte)el.ParseLong().Value).ToArray(), 0, len);//No way to know what is in the array since they are objects, so convert them to bytes.
				}
				else if (buf is string s)
				{
					var byteBuf = enc.GetBytes(s);
					len = count != long.MinValue ? Math.Min(byteBuf.Length, (int)count) : byteBuf.Length;
					bw.Write(byteBuf, 0, len);
				}
				else if (Reflections.TryGetPtrProperty(buf, out var ptr))
				{
					int buflen = Reflections.TryGetSizeProperty(buf, out var sz) ? (int)sz : int.MinValue;
					len = count == long.MinValue ? buflen : (buflen != int.MinValue ? Math.Min((int)count, buflen) : (int)count);
					if (len < 0) return (long)Errors.ErrorOccurred("Invalid byte count", 0L);

					unsafe
					{
						var byteBuf = new byte[len];
						Marshal.Copy((nint)ptr, byteBuf, 0, len);
						bw.Write(byteBuf);
					}
				}
				else
					return (long)Errors.ErrorOccurred("Invalid buffer", 0L);
			}

			return len;
		}

		public string Read(object characters)
		{
			var s = "";
			var count = characters.Al();
			char[] buf = null;
			var read = 0;

			if (count > 0)
				buf = new char[count];

			if (br != null)
			{
				if (count > 0)
					read = br.Read(buf, 0, (int)count);
				else
					s = br.ReadString();
			}
			else if (tr != null)
			{
				if (count > 0)
					read = tr.Read(buf, 0, (int)count);
				else
					s = tr.ReadToEnd();
			}

			if (read > 0)
				s = new string(buf, 0, read);

			s = HandleReadEol(s);
			return s ?? DefaultObject;
		}

		public object ReadChar() => br != null ? (long)br.ReadByte() : DefaultObject;

		public object ReadDouble() => br != null ? br.ReadDouble() : DefaultObject;

		public object ReadFloat() => br != null ? (double)br.ReadSingle() : DefaultObject;

		public object ReadInt() => br != null ? (long)br.ReadInt32() : DefaultObject;

		public object ReadInt64() => br != null ? br.ReadInt64() : DefaultObject;

		public string ReadLine()
		{
			var s = "";

			if (br != null)
				s = br.ReadLine();
			else if (tr != null)
				s = tr.ReadLine();

			return s;
		}

		public object ReadShort() => br != null ? (long)br.ReadInt16() : DefaultObject;

		//Char in this case is meant to be 1 byte, according to the AHK DllCall() documentation.
		public object ReadUChar() => br != null ? (long)br.ReadByte() : DefaultObject;

		public object ReadUInt() => br != null ? (long)br.ReadUInt32() : DefaultObject;

		public object ReadUShort() => br != null ? (long)br.ReadUInt16() : DefaultObject;

		public object Seek(object distance, object origin = null)
		{
			var distanceVal = distance.ToLong();
			var originVal = (origin is null ? long.MinValue : origin.ToLong());
			SeekOrigin so;

			if (originVal == 0)
				so = SeekOrigin.Begin;
			else if (originVal == 1)
				so = SeekOrigin.Current;
			else if (originVal == 2)
				so = SeekOrigin.End;
			else if (distanceVal < 0)
				so = SeekOrigin.End;
			else
				so = SeekOrigin.Begin;

			if (br != null)
				_ = br.BaseStream.Seek(distanceVal, so);
			else if (bw != null)//Only need to do 1, because they both have the same underlying stream.
				_ = bw.Seek((int)distanceVal, so);

			return DefaultObject;
		}

		public long Write(object @string)
		{
			var s = @string.As();
			var len = 0L;

			if (bw != null)
			{
				s = HandleWriteEol(s);
				var bytes = enc.GetBytes(s);
				bw.Write(bytes);
				len = bytes.Length;
			}
			else if (tw != null)
			{
				tw.Write(s);
				len = enc.GetByteCount(s);
			}

			return len;
		}

		public long WriteChar(object num)
		{
			if (bw != null)
			{
				bw.Write((byte)num.Al());//Char in this case is meant to be 1 byte, according to the AHK DllCall() documentation.
				return 1L;
			}
			else
				return 0L;
		}

		public long WriteDouble(object num)
		{
			if (bw != null)
			{
				bw.Write(num.Ad());
				return 8L;
			}
			else
				return 0L;
		}

		public long WriteFloat(object num)
		{
			if (bw != null)
			{
				bw.Write((float)num.Ad());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteInt(object num)
		{
			if (bw != null)
			{
				bw.Write(num.Ai());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteInt64(object num)
		{
			if (bw != null)
			{
				bw.Write(num.Al());
				return 8L;
			}
			else
				return 0L;
		}

		public long WriteLine(object @string)
		{
			var s = @string.As();
			byte[] bytes;
			var len = 0L;

			if (s != "")
				len = Write(s);

			s = eolconv == 4 ? "\r\n" : "\n";

			if (bw != null)
			{
				bytes = enc.GetBytes(s);
				bw.Write(bytes);
				len += bytes.Length;
			}
			else if (tw != null)
			{
				tw.Write(s);
				len += enc.GetByteCount(s);
			}

			return len;
		}

		public long WriteShort(object num)
		{
			if (bw != null)
			{
				bw.Write((short)num.Al());
				return 2L;
			}
			else
				return 0L;
		}

		public long WriteUChar(object num)
		{
			if (bw != null)
			{
				bw.Write((byte)num.Al());
				return 1L;
			}
			else
				return 0L;
		}

		public long WriteUInt(object num)
		{
			if (bw != null)
			{
				bw.Write((uint)num.Al());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteUShort(object num)
		{
			if (bw != null)
			{
				bw.Write((ushort)num.Al());
				return 2L;
			}
			else
				return 0L;
		}

		void IDisposable.Dispose()
		{
			Dispose(true);
			HasFinalizer = false;
		}

		private string HandleReadEol(string s)
		{
			if (eolconv == 4)
				s = s.Replace("\r\n", "\n");
			else if (eolconv == 8)
				s = s.Replace("\r", "\n");

			return s;
		}

		private string HandleWriteEol(string s)
		{
			if (eolconv == 4)
				s = s.Replace("\n", "\r\n");

			return s;
		}
	}
}
