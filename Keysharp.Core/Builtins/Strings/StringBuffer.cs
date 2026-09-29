namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Text in memory native code can write to: a function's output is received into one passed as Ptr, and text is
		/// built in one without copying. It is a script's name for a <see cref="StringMemory"/> and holds no logic of its
		/// own: each member forwards to the memory, adding only the errors a script sees.
		/// </summary>
		public class StringBuffer : KeysharpObject, IPointable
		{
			private StringMemory memory = new();
			internal string Text => memory.ToString();

			public StringBuffer(params object[] args) : base(args) { }

			/// <summary>
			/// Returns a copy with memory of its own holding the same text, capacity and position.
			/// </summary>
			public new object Clone()
			{
				var copy = (StringBuffer)MemberwiseClone();
				copy.memory = memory.Copy();
				return copy;
			}

			public static implicit operator string(StringBuffer s) => s.ToString();

			public object __New(object initialValue = null, object capacity = null)
			{
				if (!initialValue.CoerceString(out var text))
					return DefaultObject;

				if (!capacity.CoerceLong(out var room, Math.Max(text.Length, 256)))
					return DefaultObject;

				if (room is < 0 or >= int.MaxValue)
					return Errors.ValueErrorOccurred($"Invalid capacity {room}.", capacity);

				memory.SetRoom((int)room);
				_ = memory.Append(text);
				return DefaultObject;
			}

			/// <summary>
			/// The address of the memory, which stays the same while the text fits.
			/// </summary>
			public long Ptr => memory.Address;

			/// <summary>
			/// The size in bytes, excluding the null terminator.
			/// </summary>
			public long Size => (long)memory.Room * sizeof(char);

			/// <summary>
			/// The write position, in characters (see <see cref="Seek"/>).
			/// </summary>
			public long Pos
			{
				get => memory.Position;
				set => memory.Seek(value);
			}

			/// <summary>
			/// The capacity in characters, excluding the null terminator. Assigning it keeps the text which fits.
			/// </summary>
			public object Capacity
			{
				get => (long)memory.Room;

				set
				{
					if (!value.CoerceLong(out var capacity))
						return;

					if (capacity is < 0 or >= int.MaxValue)
						_ = Errors.ValueErrorOccurred($"Invalid capacity {capacity}.", value);
					else
						memory.SetRoom((int)capacity);
				}
			}

			/// <summary>
			/// Appends <paramref name="text"/> at the position, growing the capacity as needed, and returns the new position.
			/// </summary>
			public object Append(string text)
			{
				if (text == null)
					return Errors.ErrorOccurred("String cannot be unset");

				return memory.Append(text);
			}

			public object AppendLine(string text = "")
			{
				_ = Append(text);
				return memory.Append(DefaultNewLine);
			}

			public object Clear()
			{
				memory.Clear();
				return DefaultObject;
			}

			/// <summary>
			/// Moves the position, clamped to the capacity, or for a negative value to the first null, which is where a
			/// function's output ends. Returns the new position.
			/// </summary>
			public object Seek(object position)
			{
				if (!position.CoerceLong(out var pos))
					return DefaultObject;

				return memory.Seek(pos);
			}

			/// <summary>
			/// The text up to the first null or the position, whichever is further.
			/// </summary>
			public override string ToString() => Text;
		}
	}
}
