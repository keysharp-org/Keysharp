using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Text in memory native code can write to, since a .NET string cannot be. A variable keeps one as the memory StrPtr
	/// returns and a Str argument is passed, and what native code writes there reaches the variable, up to the first
	/// null, as AutoHotkey takes a variable's length from its contents. A script's StringBuffer is one with a name.
	/// </summary>
	internal sealed class StringMemory
	{
		// A variable's memory starts with room for MAX_PATH characters with the terminator, so a function writing a path
		// into a variable nobody sized stays inside it.
		private const int MinimumRoom = 259;

		// Once an address is handed out, on the pinned object heap, so the address stays the same while the text fits.
		private char[] chars = [];
		private bool pinned;
		// The room VarSetStrCapacity asked for, allocated only once native code needs the memory, since a script also
		// sizes a variable to speed up concatenation, which the memory plays no part in.
		private int reserved;
		// A variable's value last copied in or taken out: while the variable still holds it, the memory is the newer.
		private string synced = "";
		private long position;

		internal int Room => chars.Length == 0 ? reserved : chars.Length - 1;
		internal long Position => position;

		// A variable's own memory, on its one reference. A reference made afresh each time, such as to a property, keeps
		// none, since memory only it kept would be freed while its address was still in use.
		internal static StringMemory Of(object reference) => reference is VarRef { IsVariable: true } variable
			? variable.Memory ?? Interlocked.CompareExchange(ref variable.Memory, new(), null) ?? variable.Memory : null;

		internal long Address
		{
			get
			{
				if (!pinned)
				{
					pinned = true;
					Resize(Room);
				}

				return (long)Marshal.UnsafeAddrOfPinnedArrayElement(chars, 0);
			}
		}

		// The memory holding a variable's value, which keeps what native code wrote there since.
		internal char[] Load(string value)
		{
			if (chars.Length <= value.Length)
				Resize(Math.Max(value.Length, chars.Length != 0 ? Room * 2 : reserved != 0 ? reserved : MinimumRoom));

			if (!ReferenceEquals(value, synced))
			{
				value.CopyTo(chars);
				chars[value.Length] = '\0';
				synced = value;
			}

			return chars;
		}

		// Memory Load has to grow is grown on the pinned heap directly, rather than moved there afterwards.
		internal long AddressOf(string value)
		{
			if (chars.Length <= value.Length)
				pinned = true;

			_ = Load(value);
			return Address;
		}

		// What native code left in a variable's memory, or null when that is unchanged or the variable was assigned since.
		internal string Written(string current = null)
		{
			if (current != null && !ReferenceEquals(current, synced))
				return null;

			var text = NativeType.UpToNull(chars);
			return text.SequenceEqual(synced) ? null : synced = new string(text);
		}

		// A variable's room grows until 0 frees it, as in AutoHotkey, and its memory is blank either way.
		internal void Reserve(int capacity)
		{
			if (capacity == 0 || capacity > Room)
			{
				chars = [];
				reserved = capacity;
			}
			else if (chars.Length != 0)
				chars[0] = '\0';

			synced = "";
		}

		// Appends at the position, growing the room as needed, and returns the new position.
		internal long Append(string text)
		{
			var end = position + text.Length;

			if (end >= chars.Length)
				Resize((int)Math.Max(end, Room * 2L));

			text.CopyTo(chars.AsSpan((int)position));
			position = end;
			chars[position] = '\0';
			return position;
		}

		// Moves the position, clamped to the room, or for a negative value to the first null, where a function's output ends.
		internal long Seek(long pos) => position = Math.Min(Room, pos < 0 ? NativeType.UpToNull(chars).Length : pos);

		internal void Clear()
		{
			position = 0;

			if (chars.Length != 0)
				chars[0] = '\0';
		}

		// Keeps the text which fits.
		internal void SetRoom(int capacity) => Resize(capacity);

		// The same text, room and position in memory of its own.
		internal StringMemory Copy() => new() { chars = [.. chars], reserved = reserved, synced = synced, position = position };

		// The text up to the first null or the position, whichever is further.
		public override string ToString() => new(chars, 0, (int)Math.Max(NativeType.UpToNull(chars).Length, position));

		private void Resize(int capacity)
		{
			var next = pinned ? GC.AllocateArray<char>(capacity + 1, true) : new char[capacity + 1];
			chars.AsSpan(0, Math.Min(chars.Length, capacity)).CopyTo(next);
			chars = next;
			position = Math.Min(position, capacity);
		}
	}
}
