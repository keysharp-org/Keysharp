namespace Keysharp.Core.Common.Strings
{
	unsafe public class StringBuffer : KeysharpObject, IPointable
	{
		/// <summary>
		/// Pointer to the unmanaged memory holding the buffer contents.
		/// </summary>
		private byte* _buffer;
		/// <summary>
		/// Capacity of the buffer in character units (not bytes).
		/// Does not include null terminator.
		/// </summary>
		private long _capacity = 0;
		/// <summary>
		/// Current write position (in character units) within the buffer.
		/// </summary>
		private long _position = 0;
		private Encoding _encoding;
		private int _bytesPerChar;
		public object EntangledString { get; set; }

		public StringBuffer(params object[] args) => __New(args);

		~StringBuffer()
		{
			if (_buffer != null)
				NativeMemory.Free(_buffer);
		}

		public static implicit operator string(StringBuffer s) => s.ToString();

		/// <summary>
		/// Initializes the buffer. All parameters are optional:
		/// <list type="bullet">
		///   <item><description><c>args[0]</c> (optional): initial string content; defaults to empty.</description></item>
		///   <item><description><c>args[1]</c> (optional): initial capacity in characters (excluding null terminator); defaults to larger of 256 or provided string length.</description></item>
		///   <item><description><c>args[2]</c> (optional): encoding specifier; pass "ANSI" (case-insensitive) for system ANSI encoding, otherwise defaults to Unicode.</description></item>
		/// </list>
		/// </summary>
		public new object __New(params object[] args)
		{
			var str = args.Length > 0 ? args[0].ToString() : "";
			var capacity = args.Length > 1 && args[1] != null ? args[1].Ai() + 1 : Math.Max(str.Length + 1, 256);
			_encoding = args.Length > 2 && args[2].As().Equals("ANSI", StringComparison.OrdinalIgnoreCase) ? Encoding.Default : Encoding.Unicode;
			_bytesPerChar = _encoding == Encoding.Unicode ? sizeof(char) : 1;
			_capacity = capacity;
			_buffer = (byte*)NativeMemory.Alloc((nuint)(_capacity * _bytesPerChar));
			_ = Append(str);
			return DefaultObject;
		}

		/// <summary>
		/// Gets the raw pointer address (as a long) to the unmanaged buffer.
		/// </summary>
		public long Ptr => (long)_buffer;

		/// <summary>
		/// Gets or sets the current write/read position in character units.
		/// </summary>
		public long Pos
		{
			get => _position;
			set => Seek(_position);
		}

		/// <summary>
		/// Gets or sets the buffer capacity (in chars). Expands or shrinks the unmanaged block.
		/// Shrinking the capacity causes a null-terminator to be added to the end.
		/// </summary>
		public object Capacity
		{
			get => _capacity;

			set
			{
				var newCapacity = (long)value;
				// ReAllocHGlobal will preserve existing bytes up to the new size
				_buffer = (byte*)NativeMemory.Realloc(_buffer, (nuint)((newCapacity + 1) * _bytesPerChar));

				if (newCapacity < _capacity)
				{
					NativeMemory.Clear(_buffer + (newCapacity * _bytesPerChar), (nuint)_bytesPerChar);
				}

				_capacity = newCapacity;
			}
		}

		public object UpdateEntangledStringFromBuffer() => EntangledString != null ? Script.SetPropertyValue(EntangledString, "__Value", ToString()) : null;
		public object UpdateBufferFromEntangledString()
		{
			if (EntangledString == null)
				return null;
			var str = Script.GetPropertyValue(EntangledString, "__Value") as string;
			str ??= "";
			var requiredCapacity = Math.Max(_capacity, str.Length);
			EnsureCapacity(requiredCapacity);
			Clear();
			Append(str);
			return str;
		}

		/// <summary>
		/// Appends <paramref name="text"/> to the buffer,
		/// expanding capacity if needed, and null-terminates.
		/// Returns the new position (in chars).
		/// </summary>
		public object Append(string text)
		{
			if (text == null) throw new Error("String cannot be unset");

			int len = text.Length;
			EnsureCapacity(_position + len);

			if (_bytesPerChar == 1)
			{
				byte[] bytes = _encoding.GetBytes(text);

				fixed (byte* src = bytes)
				{
					NativeMemory.Copy(src, _buffer + _position, (nuint)bytes.Length);
				}

				_position += len;
				_buffer[_position] = 0;
			}
			else
			{
				// Copy chars
				char[] bytes = text.ToCharArray();

				fixed (char* src = bytes)
				{
					NativeMemory.Copy(src, _buffer + (nint)_position * _bytesPerChar, (nuint)(bytes.Length * _bytesPerChar));
				}

				_position += len;
				*((char*)(_buffer + _position * _bytesPerChar)) = '\0';
			}

			return _position;
		}

		/// <summary>
		/// Appends <paramref name="text"/> followed by a newline.
		/// Returns the new position (in chars).
		/// </summary>
		public object AppendLine(string text = "")
		{
			_ = Append(text);
			return Append(DefaultNewLine);
		}

		/// <summary>
		/// Clears the buffer contents and resets position to zero.
		/// </summary>
		public object Clear()
		{
			_position = 0;

			if (_bytesPerChar == 1)
				_buffer[0] = 0;
			else
				*((char*)_buffer) = '\0';

			return DefaultObject;
		}

		/// <summary>
		/// Seeks to <paramref name="position"/> (clamped to capacity),
		/// or if negative, finds the current string length by scanning for a null terminator.
		/// Returns the new position.
		/// </summary>
		public object Seek(object position)
		{
			long pos = position.Al();

			if (pos < 0)
			{
				if (_bytesPerChar == 1)
				{
					// Find length up to first 0 byte
					byte* p = _buffer;
					_position = 0;

					while (p[_position] != 0)
						_position++;
				}
				else
				{
					// Find length up to first 0 wchar
					char* p = (char*)_buffer;
					_position = 0;

					while (p[_position] != '\0')
						_position++;
				}
			}
			else
			{
				_position = Math.Min(_capacity, pos);
			}

			return _position;
		}

		/// <summary>
		/// Ensures the buffer can hold <paramref name="requiredCapacity"/> characters.
		/// If <paramref name="exact"/> is false then the maximum of <paramref name="requiredCapacity"/>
		/// and double the current capacity is used.
		/// </summary>
		private void EnsureCapacity(long requiredCapacity, bool exact = false)
		{
			if (requiredCapacity > _capacity)
				Capacity = exact ? requiredCapacity : Math.Max(requiredCapacity, _capacity * 2);
		}

		public override void PrintProps(string name, StringBuffer sbuf, ref int tabLevel)
		{
			var indent = new string('\t', tabLevel);
			var fieldType = GetType().Name;
			var str = ToString();

			if (name.Length == 0)
				_ = sbuf.AppendLine($"{indent}{str} ({fieldType})");
			else
				_ = sbuf.AppendLine($"{indent}{name}: {str} ({fieldType})");
		}

		/// <summary>
		/// Reads the current buffer contents up to the null-terminator or current position (whichever is larger)
		/// and returns as a managed string.
		/// </summary>
		public override string ToString()
		{
			if (_buffer == null)
				return DefaultErrorString;

			if (_bytesPerChar == 1)
			{
				// Find length up to first 0 byte
				byte* p = _buffer;
				int len = 0;

				while (p[len] != 0)
					len++;

				// Decode exactly that many ANSI bytes
				return _encoding.GetString(new ReadOnlySpan<byte>(_buffer, Math.Max(len, (int)_position)));
			}
			else
			{
				// Find length up to first 0 wchar
				char* p = (char*)_buffer;
				int len = 0;

				while (p[len] != '\0')
					len++;

				// Construct a managed string from that many chars
				return new string(p, 0, Math.Max(len, (int)_position));
			}
		}
	}
}