namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for external-related functions.
	/// </summary>
	public static class External
	{
		/// <summary>
		/// Returns the binary number stored at the specified address+offset.
		/// </summary>
		/// <param name="source">A <see cref="Buffer"/>-like object or memory address.</param>
		/// <param name="offset">If blank or omitted (or when using 2-parameter mode), it defaults to 0.<br/>
		/// Otherwise, specify an offset in bytes which is added to source to determine the source address.
		/// </param>
		/// <param name="type">One of the following strings: UInt, Int, Int64, Short, UShort, Char, UChar, Double, Float, Ptr or UPtr</param>
		/// <returns>The binary number at the specified address+offset.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown the address could not be determined.</exception>
		public unsafe static object NumGet(object source, object offset, object type = null)
		{
			int off;
			string t;
			var address = source;

			if (type == null)
			{
				off = 0;
				t = offset.As("UInt");
			}
			else
			{
				off = offset.Ai();
				t = type.As("UInt");
			}

			nint addr;
			var size = 0L;
			t = t.ToLower();

			if (address is Buffer abuf)//Put Buffer check first because it's faster and more likely.
			{
				address = abuf.Ptr;
				size = abuf.Size.Al();
			}
			else if (address is KeysharpObject kso && Script.TryGetPropertyValue(out object p, kso, "ptr")
					 && Script.TryGetPropertyValue(out object s, kso, "size"))
			{
				address = p;
				size = s.Al();
			}

			if (address is object[] objarr && objarr.Length > 0)//Assume the first element was a long which was an address.
				addr = new nint(objarr[0].Al());
			else if (address is long l)
				addr = new nint(l);

			//else if (address is int i)
			//  addr = new nint(i);
#if WINDOWS
			else if (t == "ptr" && Marshal.IsComObject(address))
			{
				var pUnk = Marshal.GetIUnknownForObject(address);
				addr = pUnk;//Ditto.
				_ = Marshal.Release(pUnk);
			}
#endif
			else
				return Errors.TypeErrorOccurred(address, typeof(nint), DefaultErrorObject);

			switch (t)
			{
				case "uint":
					if (size > 0 && (off + 4 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 4 > buffer size {size}.");

					return (long)(uint)Marshal.ReadInt32(addr, off);

				case "int":
					if (size > 0 && (off + 4 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 4 > buffer size {size}.");

					return (long)Marshal.ReadInt32(addr, off);

				case "short":
					if (size > 0 && (off + 2 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 2 > buffer size {size}.");

					return (long)Marshal.ReadInt16(addr, off);

				case "ushort":
					if (size > 0 && (off + 2 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 2 > buffer size {size}.");

					return (long)(ushort)Marshal.ReadInt16(addr, off);

				case "char":
					if (size > 0 && (off + 1 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 1 > buffer size {size}.");

					return (long)(sbyte)Marshal.ReadByte(addr, off);

				case "uchar":
					if (size > 0 && (off + 1 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 1 > buffer size {size}.");

					return (long)Marshal.ReadByte(addr, off);

				case "double":
					if (size > 0 && (off + 8 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 8 > buffer size {size}.");

					unsafe
					{
						var ptr = (double*)(addr + off).ToPointer();
						var val = *ptr;
						return val;
					}

				case "float":
					if (size > 0 && (off + 4 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 4 > buffer size {size}.");

					unsafe
					{
						var ptr = (float*)(addr + off).ToPointer();
						return double.Parse((*ptr).ToString());//Need to convert to string to make it exact, else there can be lots of rounding/trailing digits.
					}

				case "int64":
				case "ptr":
				case "uptr":
				default:
					if (size > 0 && (off + 8 > size))
						return Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {off} + length 8 > buffer size {size}.");

					var ipoff = nint.Add(addr, off);
					//var pp = (long*)ipoff.ToPointer();
					//return *pp;
					return Marshal.ReadIntPtr(ipoff).ToInt64();//Dereference here.
			}
		}

		/// <summary>
		/// Stores one or more numbers in binary format at the specified address+offset.
		/// </summary>
		/// <param name="type">One of the following strings: UInt, UInt64, Int, Int64, Short, UShort, Char, UChar, Double, Float, Ptr or UPtr.</param>
		/// <param name="number">The number to store.</param>
		/// <param name="target">A <see cref="Buffer"/>-like object or memory address.</param>
		/// <param name="offset">If omitted, it defaults to 0. Otherwise, specify an offset in bytes which is added to Target to determine the target address.</param>
		/// <returns>The address to the right of the last item written.</returns>
		/// <exception cref="IndexError">An <see cref="IndexError"/> exception is thrown if the offset exceeds the bounds of the memory or if it couldn't be determined.</exception>
		public static long NumPut(params object[] obj)
		{
			nint addr = 0;
			var offset = 0;
			var size = 0L;
			int lastPairIndex;
			var offsetSpecified = !((obj.Length & 1) == 1);
			object target;

			if (offsetSpecified)
			{
				lastPairIndex = obj.Length - 4;
				offset = obj.Ai(obj.Length - 1);
				target = obj[obj.Length - 2];
			}
			else
			{
				lastPairIndex = obj.Length - 3;
				target = obj[obj.Length - 1];
			}

			if (target is Buffer buf)//Put Buffer check first because it's faster and more likely.
			{
				size = buf.Size.Al();
				target = buf.Ptr;
			}
			else if (target is Any kso && Script.TryGetPropertyValue(out object p, kso, "ptr") && p != null
					 && Script.TryGetPropertyValue(out object s, kso, "size") && s != null)
			{
				size = s.Al();
				target = p;
			}

			if (target is long l)
				addr = new nint(l);
			else
				return (long)Errors.TypeErrorOccurred(target, typeof(nint), DefaultErrorLong);

			for (var i = 0; i <= lastPairIndex; i += 2)
			{
				var inc = 0;
				var type = obj[i] as string;
				var number = obj[i + 1];
				byte[] bytes;

				switch (type.ToLower())
				{
					case "int":
						bytes = BitConverter.GetBytes(number.Ai());
						inc = 4;
						break;

					case "uint":
						bytes = BitConverter.GetBytes(number.Aui());
						inc = 4;
						break;

					case "float":
						bytes = BitConverter.GetBytes(number.Af());
						inc = 4;
						break;

					case "short":
						bytes = BitConverter.GetBytes((short)number.Ai());
						inc = 2;
						break;

					case "ushort":
						bytes = BitConverter.GetBytes((ushort)number.Aui());
						inc = 2;
						break;

					case "char":
						bytes = [(byte)number.Ai()];
						inc = 1;
						break;

					case "uchar":
						bytes = [(byte)number.Ai()];
						inc = 1;
						break;

					case "double":
						bytes = BitConverter.GetBytes(number.Ad());
						inc = 8;
						break;

					case "int64":
						bytes = BitConverter.GetBytes(number.Al());
						inc = 8;
						break;

					case "uint64":
					case "ptr":
					case "uptr":
						if (number is Any kso)
							number = Reflections.GetPtrProperty(kso);

						bytes = BitConverter.GetBytes(number.Al());
						inc = 8;
						break;

					default:
						bytes = [];
						inc = 0;
						break;
				}

				var finalAddr = nint.Add(addr, offset);

				if (size > 0)
				{
					if ((offset + bytes.Length) <= size)
					{
						Marshal.Copy(bytes, 0, finalAddr, bytes.Length);
						offset += inc;
					}
					else
						return (long)Errors.IndexErrorOccurred($"Memory access exceeded buffer size. Offset {offset} + length {bytes.Length} > buffer size {size}.", DefaultErrorLong);
				}
				else if (addr != 0)
				{
					Marshal.Copy(bytes, 0, finalAddr, bytes.Length);
					offset += inc;
				}
				else
					return (long)Errors.IndexErrorOccurred($"Could not parse target {target} as a Buffer or memory address.", DefaultErrorLong);
			}

			return nint.Add(addr, offset).ToInt64();
		}
	}
}