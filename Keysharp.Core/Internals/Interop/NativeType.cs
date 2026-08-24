using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// One native type, as named by the type strings a script writes in a <see cref="Keysharp.Builtins.Dll.DllCall"/>
	/// parameter list or a <see cref="Keysharp.Builtins.External.NumGet"/>/<see cref="Keysharp.Builtins.External.NumPut"/>
	/// type argument. The numeric types are kept contiguous so <see cref="NativeType.IsNumeric"/> is a range test.
	/// </summary>
	internal enum NativeTypeCode : byte
	{
		Invalid,
		Int, UInt, Int64, UInt64, Short, UShort, Char, UChar, Float, Double, Ptr, UPtr,
		Str, WStr, AStr, BStr, HResult, Void
	}

	/// <summary>
	/// Turns a type string into a <see cref="NativeTypeCode"/>. Every caller sits on a hot path that runs once per
	/// argument per call, so this neither allocates nor lower-cases: it dispatches on the length, which leaves at
	/// most four candidates, and compares each case-insensitively in place.
	/// </summary>
	internal static class NativeType
	{
		internal static NativeTypeCode Parse(ReadOnlySpan<char> tag) => tag.Length switch
		{
			3 => Is(tag, "int") ? NativeTypeCode.Int
			: Is(tag, "ptr") ? NativeTypeCode.Ptr
			: Is(tag, "str") ? NativeTypeCode.Str
			: NativeTypeCode.Invalid,
			4 => Is(tag, "uint") ? NativeTypeCode.UInt
			: Is(tag, "char") ? NativeTypeCode.Char
			: Is(tag, "uptr") ? NativeTypeCode.UPtr
			: Is(tag, "wstr") ? NativeTypeCode.WStr
			: Is(tag, "astr") ? NativeTypeCode.AStr
			: Is(tag, "bstr") ? NativeTypeCode.BStr
			: Is(tag, "void") ? NativeTypeCode.Void
			: NativeTypeCode.Invalid,
			5 => Is(tag, "int64") ? NativeTypeCode.Int64
			: Is(tag, "short") ? NativeTypeCode.Short
			: Is(tag, "uchar") ? NativeTypeCode.UChar
			: Is(tag, "float") ? NativeTypeCode.Float
			: NativeTypeCode.Invalid,
			6 => Is(tag, "uint64") ? NativeTypeCode.UInt64
			: Is(tag, "ushort") ? NativeTypeCode.UShort
			: Is(tag, "double") ? NativeTypeCode.Double
			: NativeTypeCode.Invalid,
			7 => Is(tag, "hresult") ? NativeTypeCode.HResult : NativeTypeCode.Invalid,
			_ => NativeTypeCode.Invalid
		};

		/// <summary>
		/// The number of bytes a value of this type occupies in memory. A type that names no storage of its own
		/// (Void, Invalid) is 0; the string types are the pointer that is actually passed.
		/// </summary>
		internal static int SizeOf(NativeTypeCode code) => code switch
		{
			NativeTypeCode.Char or NativeTypeCode.UChar => 1,
			NativeTypeCode.Short or NativeTypeCode.UShort => 2,
			NativeTypeCode.Int or NativeTypeCode.UInt or NativeTypeCode.Float or NativeTypeCode.HResult => 4,
			NativeTypeCode.Invalid or NativeTypeCode.Void => 0,
			_ => 8
		};

		/// <summary>
		/// Whether this type names a number that can be read from or written to memory directly, which is the
		/// whole set NumGet and NumPut accept.
		/// <para>Written out rather than as a range test over the enum's order. A range test compiles to the same
		/// thing but reordering <see cref="NativeTypeCode"/> would silently widen it, and admitting one more type
		/// here is not a harmless mistake: NumPut bounds the write with <see cref="SizeOf"/> and then writes
		/// through the arm the code selects, so a type whose two widths disagree overruns a passing check.</para>
		/// </summary>
		internal static bool IsNumeric(NativeTypeCode code) => code switch
		{
			NativeTypeCode.Int or NativeTypeCode.UInt or NativeTypeCode.Int64 or NativeTypeCode.UInt64
			or NativeTypeCode.Short or NativeTypeCode.UShort or NativeTypeCode.Char or NativeTypeCode.UChar
			or NativeTypeCode.Float or NativeTypeCode.Double or NativeTypeCode.Ptr or NativeTypeCode.UPtr => true,
			_ => false
		};

		/// <summary>
		/// Whether a value of this type travels in a floating-point register.
		/// </summary>
		internal static bool IsFloating(NativeTypeCode code) => code is NativeTypeCode.Float or NativeTypeCode.Double;

		/// <summary>
		/// Reads one value of <paramref name="code"/> from memory and boxes it as the script value it maps to.
		/// Unaligned, because a script picks its own offsets and a struct field is routinely read off its natural
		/// boundary. Everything integral widens to long, which is the only integer a script has. This is a switch
		/// statement rather than an expression because the arms have to keep their own types: an expression would
		/// find one common type for them all and hand a script a Float where it asked for an Integer.
		/// </summary>
		internal static unsafe object ReadMemory(NativeTypeCode code, nint address)
		{
			var p = (void*)address;

			switch (code)
			{
				case NativeTypeCode.Int:
				case NativeTypeCode.HResult: return (long)Unsafe.ReadUnaligned<int>(p);

				case NativeTypeCode.UInt: return (long)Unsafe.ReadUnaligned<uint>(p);

				case NativeTypeCode.Short: return (long)Unsafe.ReadUnaligned<short>(p);

				case NativeTypeCode.UShort: return (long)Unsafe.ReadUnaligned<ushort>(p);

				case NativeTypeCode.Char: return (long)unchecked((sbyte)Unsafe.ReadUnaligned<byte>(p));

				case NativeTypeCode.UChar: return (long)Unsafe.ReadUnaligned<byte>(p);

				case NativeTypeCode.Double: return Unsafe.ReadUnaligned<double>(p);

				case NativeTypeCode.Float: return WidenFloat(Unsafe.ReadUnaligned<float>(p));

				//A string output slot holds the address of a string, so it dereferences to the string itself,
				//with StrGet's guard against a null or implausibly low address. None of them are freed: as
				//AutoHotkey puts it, there is no way for Str* to know how or whether the callee requires the
				//string to be freed.
				case NativeTypeCode.Str:
				case NativeTypeCode.WStr:
				case NativeTypeCode.BStr: return Keysharp.Builtins.Strings.StrGet(Unsafe.ReadUnaligned<long>(p));

				case NativeTypeCode.AStr: return Marshal.PtrToStringAnsi((nint)Unsafe.ReadUnaligned<long>(p)) ?? "";

				default: return Unsafe.ReadUnaligned<long>(p);//Int64, UInt64, Ptr and UPtr read the full eight bytes.
			}
		}

		/// <summary>
		/// Writes one value straight to <paramref name="addr"/>. Unaligned, because a script picks its own
		/// offsets and a struct field is routinely written off its natural boundary. Returns false when
		/// <paramref name="number"/> does not name a number, which AutoHotkey rejects rather than writing
		/// whatever a failed coercion defaulted to.
		/// </summary>
		internal static unsafe bool WriteMemory(nint addr, NativeTypeCode code, object number)
		{
			var p = (void*)addr;

			//The pointer-width types also accept an object carrying an address; every other type needs a value
			//that actually reads as a number.
			if (code is NativeTypeCode.UInt64 or NativeTypeCode.Ptr or NativeTypeCode.UPtr && number is Any kso)
			{
				if (!Reflections.TryGetPtrProperty(kso, out var ksoAddr))
					return false;

				number = ksoAddr;
			}
			else if (!number.TryCoerceLong(out _))
				return false;

			switch (code)
			{
				case NativeTypeCode.Int: Unsafe.WriteUnaligned(p, number.Ai()); break;

				case NativeTypeCode.UInt: Unsafe.WriteUnaligned(p, number.Aui()); break;

				case NativeTypeCode.Float: Unsafe.WriteUnaligned(p, number.Af()); break;

				case NativeTypeCode.Short: Unsafe.WriteUnaligned(p, unchecked((short)number.Ai())); break;

				case NativeTypeCode.UShort: Unsafe.WriteUnaligned(p, unchecked((ushort)number.Aui())); break;

				case NativeTypeCode.Char:
				case NativeTypeCode.UChar: Unsafe.WriteUnaligned(p, unchecked((byte)number.Ai())); break;

				case NativeTypeCode.Double: Unsafe.WriteUnaligned(p, number.Ad()); break;

				default: Unsafe.WriteUnaligned(p, number.Al()); break;//Int64, UInt64, Ptr and UPtr.
			}

			return true;
		}

		/// <summary>
		/// Widens a float to the double a script sees. Going through the shortest text that reads back as the
		/// same float keeps the decimal a script prints ("1.2345"), where a plain cast would carry the binary
		/// error of the narrower type into every digit (1.2344999313354492). Formatting into stack space keeps
		/// this allocation-free, and invariant on both sides makes the round trip independent of the locale.
		/// </summary>
		internal static double WidenFloat(float value)
		{
			Span<char> text = stackalloc char[32];
			return value.TryFormat(text, out var written, default, CultureInfo.InvariantCulture)
				   ? double.Parse(text[..written], NumberStyles.Float, CultureInfo.InvariantCulture)
				   : value;
		}

		// The first character is compared inline, and every name here is lower case, so a candidate that cannot
		// match is rejected without entering the full comparison. That leaves roughly one real comparison per
		// parse instead of one per candidate in the length's bucket.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool Is(ReadOnlySpan<char> tag, string name) =>
		(tag[0] | 0x20) == name[0] && tag.Equals(name, StringComparison.OrdinalIgnoreCase);
	}
}
