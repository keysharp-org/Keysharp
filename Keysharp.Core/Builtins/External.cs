namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for external-related functions.
	/// </summary>
	public static class External
	{
		/// <summary>
		/// The lowest address NumGet/NumPut will touch, matching AutoHotkey. It is a sanity check, not a promise
		/// about the address space: its job is to catch a zero or blank address that reached here by mistake and
		/// report it, rather than fault inside the read.
		/// <para>The value suits every platform, for different reasons. Windows guarantees the first 64KB is
		/// never mapped, which is where the number comes from; 64KB is also the usual Linux
		/// <c>vm.mmap_min_addr</c> default, and macOS reserves far more than this below the executable. Only a
		/// Linux system with that sysctl deliberately lowered could map memory beneath the floor, and a script
		/// reading it would get an IndexError here.</para>
		/// </summary>
		private const long MinValidAddress = 65536;

		/// <summary>
		/// Returns the binary number stored at the specified address+offset.
		/// </summary>
		/// <param name="source">A <see cref="Buffer"/>-like object or memory address.</param>
		/// <param name="offset">If blank or omitted (or when using 2-parameter mode), it defaults to 0.<br/>
		/// Otherwise, specify an offset in bytes which is added to source to determine the source address.
		/// </param>
		/// <param name="type">One of the following strings: UInt, Int, Int64, UInt64, Short, UShort, Char, UChar, Double, Float, Ptr or UPtr</param>
		/// <returns>The binary number at the specified address+offset.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown the address could not be determined.</exception>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if type does not name a number.</exception>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if the offset exceeds the bounds of the memory.</exception>
		public unsafe static object NumGet(object source, object offset, object type = null)
		{
			long off;
			string t;

			if (type == null)
			{
				off = 0;
				t = offset.As("UInt");
			}
			else
			{
				//Read as a long, not an int: a truncated offset can come back negative, which would slip past
				//the bounds check below and read far outside the buffer.
				off = offset.Al();
				t = type.As("UInt");
			}

			var code = NativeType.Parse(t);

			if (!NativeType.IsNumeric(code))
				return Errors.ValueErrorOccurred($"Type of {t} is not a number that can be read from memory.", t, DefaultObject);

			if (!TryResolveTarget(source, out var addr, out var size) && !TryResolveReadOnlyTarget(source, code, out addr))
				return Errors.TypeErrorOccurred(source, typeof(nint), DefaultObject);

			if (addr < MinValidAddress)
				return Errors.IndexErrorOccurred($"Could not parse target {source} as a Buffer or memory address.", DefaultObject);

			var width = NativeType.SizeOf(code);

			if (size > 0 && off + width > size)
				return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length {width} > buffer size {size}.", DefaultObject);

			return NativeType.ReadMemory(code, addr + (nint)off);
		}

		/// <summary>
		/// Stores one or more numbers in binary format at the specified address+offset.
		/// </summary>
		/// <param name="type">One of the following strings: UInt, UInt64, Int, Int64, Short, UShort, Char, UChar, Double, Float, Ptr or UPtr.</param>
		/// <param name="number">The number to store.</param>
		/// <param name="target">A <see cref="Buffer"/>-like object or memory address.</param>
		/// <param name="offset">If omitted, it defaults to 0. Otherwise, specify an offset in bytes which is added to Target to determine the target address.</param>
		/// <returns>The address to the right of the last item written.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the address could not be determined.</exception>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if a type does not name a number.</exception>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if the offset exceeds the bounds of the memory.</exception>
		public static long NumPut(params object[] obj)
		{
			long offset = 0;
			int lastPairIndex;
			object target;

			if ((obj.Length & 1) == 0)//An even count means a trailing offset follows the target.
			{
				lastPairIndex = obj.Length - 4;
				//A long, not an int: a truncated offset can come back negative and slip past the bounds check.
				offset = obj.Al(obj.Length - 1);
				target = obj[obj.Length - 2];
			}
			else
			{
				lastPairIndex = obj.Length - 3;
				target = obj[obj.Length - 1];
			}

			if (!TryResolveTarget(target, out var addr, out var size))
				return (long)Errors.TypeErrorOccurred(target, typeof(nint), DefaultErrorLong);

			if (addr < MinValidAddress)
				return (long)Errors.IndexErrorOccurred($"Could not parse target {target} as a Buffer or memory address.", DefaultErrorLong);

			for (var i = 0; i <= lastPairIndex; i += 2)
			{
				var t = obj[i] as string;
				var code = NativeType.Parse(t);

				if (!NativeType.IsNumeric(code))
					return (long)Errors.ValueErrorOccurred($"Type of {t ?? obj[i]} is not a number that can be written to memory.", obj[i], DefaultErrorLong);

				var width = NativeType.SizeOf(code);

				if (size > 0 && offset + width > size)
					return (long)Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {offset} + length {width} > buffer size {size}.", DefaultErrorLong);

				if (!NativeType.WriteMemory(addr + (nint)offset, code, obj[i + 1]))
					return (long)Errors.ValueErrorOccurred($"Value {obj[i + 1]} is not a number that can be written to memory.", obj[i + 1], DefaultErrorLong);

				offset += width;
			}

			return addr + offset;
		}

		/// <summary>
		/// Resolves a NumGet/NumPut target to the address it names, plus the size which bounds access through it.
		/// A bare address has no size, so nothing bounds it and <paramref name="size"/> stays 0.
		/// </summary>
		private static bool TryResolveTarget(object target, out nint addr, out long size)
		{
			size = 0;

			if (target is Buffer buf)//Put Buffer first because it's faster and more likely.
			{
				addr = (nint)buf.Ptr;
				size = buf.size;
				return true;
			}

			if (target is long l)
			{
				addr = (nint)l;
				return true;
			}

			//Anything else has to carry both a Ptr and a Size: without a size there is nothing to bound the
			//access against, which is why AutoHotkey's GetObjectPtrProperty requires both too. Going through
			//the shared helpers picks up IPointable (a StringBuffer, say) off the interface rather than paying
			//for a script-visible property lookup.
			if (target is Any && Reflections.TryGetPtrProperty(target, out var p) && Reflections.TryGetSizeProperty(target, out size))
			{
				addr = (nint)p;
				return true;
			}

			size = 0;
			addr = 0;
			return false;
		}

		/// <summary>
		/// The two sources only NumGet accepts: an argument list whose first element is the address, and a COM
		/// object read as its IUnknown. Neither carries a size, so neither is bounds-checked.
		/// </summary>
		private static bool TryResolveReadOnlyTarget(object source, NativeTypeCode code, out nint addr)
		{
			if (source is object[] objarr && objarr.Length > 0)
			{
				addr = (nint)objarr[0].Al();
				return true;
			}

#if WINDOWS

			//Marshal.IsComObject throws on null rather than returning false, and an unset source reaches here.
			if (code == NativeTypeCode.Ptr && source != null && Marshal.IsComObject(source))
			{
				var pUnk = Marshal.GetIUnknownForObject(source);
				addr = pUnk;
				_ = Marshal.Release(pUnk);//The object is kept alive by the caller, so the reference this took is not needed.
				return true;
			}

#endif
			addr = 0;
			return false;
		}
	}
}
