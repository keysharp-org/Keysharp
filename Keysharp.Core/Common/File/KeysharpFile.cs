namespace Keysharp.Core.Common.File
{
	public class KeysharpFile : KeysharpObject, IDisposable
	{
		private Encoding enc;

		private int eolconv = 0;

		private BinaryReader br;

		private BinaryWriter bw;

		private bool disposed = false;

		private FileStream fs;

		private TextReader tr;

		private TextWriter tw;

		public object AtEOF
		{
			get
			{
				if (br != null)
					return br.PeekChar() == -1 ? 1L : 0L;
				else if (tr != null)
					return tr.Peek() == -1 ? 1L : 0L;
				else
					return 0L;
			}
		}

		public object Encoding
		{
			get => enc.BodyName;
			set => enc = Files.GetEncoding(value);
		}

		public object Handle => fs != null ? fs.SafeFileHandle.DangerousGetHandle().ToInt64() : 0L;

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

		public KeysharpFile(StreamWriter sw)
		{
			tw = sw;
			enc = sw.Encoding;
		}

		public KeysharpFile(StreamReader sr)
		{
			tr = sr;
			enc = sr.CurrentEncoding;
		}

		public override object __New(params object[] args)
		{
			if (args == null || args.Length == 0)
				return DefaultObject;

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
					var handle = filename.Substring(2).ParseLong(false);

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

		internal KeysharpFile(string filename, FileMode mode, FileAccess access, FileShare share, Encoding encoding, long eol) : base(filename, mode, access, share, encoding, eol) { }

		~KeysharpFile() => Dispose(false);

		public void Close()
		{
			br?.Close();
			bw?.Close();
			tr?.Close();
			tw?.Close();
			fs?.Close();
			disposed = true;
		}

		public virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				Close();
			}
		}

		public object RawRead(object obj0, object obj1 = null)
		{
			var buf = obj0;
			var count = obj1.Al(long.MinValue);
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
				else if (Reflections.GetPtrProperty(buf) is long ptr && ptr != 0)
				{
					int buflen = int.MinValue;
					if (buf is Any && TryGetPropertyValue(out object maybeSize, buf, "Size"))
						buflen = maybeSize.Ai(int.MinValue);
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

		public long RawWrite(object obj0, object obj1 = null)
		{
			var buf = obj0;
			var count = obj1.Al(long.MinValue);
			var len = 0;

			if (bw != null)
			{
				if (buf is Array arr)
				{
					len = count != long.MinValue ? Math.Min(arr.Count, (int)count) : arr.Count;
					bw.Write(arr.array.ConvertAll(el => (byte)el.ParseLong(false).Value).ToArray(), 0, len);//No way to know what is in the array since they are objects, so convert them to bytes.
				}
				else if (buf is string s)
				{
					var bytes = enc.GetBytes(s);
					len = count != long.MinValue ? Math.Min(bytes.Length, (int)count) : bytes.Length;
					bw.Write(bytes, 0, len);
				}
				else if (Reflections.GetPtrProperty(buf) is long ptr && ptr != 0)
				{
					int buflen = int.MinValue;
					if (buf is Any && TryGetPropertyValue(out object maybeSize, buf, "Size"))
						buflen = maybeSize.Ai(int.MinValue);
					len = count == long.MinValue ? buflen : (buflen != int.MinValue ? Math.Min((int)count, buflen) : (int)count);
					if (len < 0) return (long)Errors.ErrorOccurred("Invalid byte count", 0L);

					unsafe
					{
						var bytes = new byte[len];
						Marshal.Copy((nint)ptr, bytes, 0, len);
						bw.Write(bytes);
					}
				}
				else
					return (long)Errors.ErrorOccurred("Invalid buffer", 0L);
			}

			return len;
		}

		public string Read(object obj)
		{
			var s = "";
			var count = obj.Al();
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
			return s;
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

		public object Seek(object obj0, object obj1 = null)
		{
			var distance = obj0.Al();
			var origin = obj1.Al(long.MinValue);
			SeekOrigin so;

			if (origin == 0)
				so = SeekOrigin.Begin;
			else if (origin == 1)
				so = SeekOrigin.Current;
			else if (origin == 2)
				so = SeekOrigin.End;
			else if (distance < 0)
				so = SeekOrigin.End;
			else
				so = SeekOrigin.Begin;

			if (br != null)
				_ = br.BaseStream.Seek(distance, so);
			else if (bw != null)//Only need to do 1, because they both have the same underlying stream.
				_ = bw.Seek((int)distance, so);

			return DefaultObject;
		}

		public long Write(object obj)
		{
			var s = obj.As();
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

		public long WriteChar(object obj)
		{
			if (bw != null)
			{
				bw.Write((byte)obj.Al());//Char in this case is meant to be 1 byte, according to the AHK DllCall() documentation.
				return 1L;
			}
			else
				return 0L;
		}

		public long WriteDouble(object obj)
		{
			if (bw != null)
			{
				bw.Write(obj.Ad());
				return 8L;
			}
			else
				return 0L;
		}

		public long WriteFloat(object obj)
		{
			if (bw != null)
			{
				bw.Write((float)obj.Ad());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteInt(object obj)
		{
			if (bw != null)
			{
				bw.Write(obj.Ai());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteInt64(object obj)
		{
			if (bw != null)
			{
				bw.Write(obj.Al());
				return 8L;
			}
			else
				return 0L;
		}

		public long WriteLine(object obj)
		{
			var s = obj.As();
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

		public long WriteShort(object obj)
		{
			if (bw != null)
			{
				bw.Write((short)obj.Al());
				return 2L;
			}
			else
				return 0L;
		}

		public long WriteUChar(object obj)
		{
			if (bw != null)
			{
				bw.Write((byte)obj.Al());
				return 1L;
			}
			else
				return 0L;
		}

		public long WriteUInt(object obj)
		{
			if (bw != null)
			{
				bw.Write((uint)obj.Al());
				return 4L;
			}
			else
				return 0L;
		}

		public long WriteUShort(object obj)
		{
			if (bw != null)
			{
				bw.Write((ushort)obj.Al());
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