using Keysharp.Builtins;
using StringBuffer = Keysharp.Builtins.Ks.StringBuffer;

namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Where an output argument's value goes once the call has written through the pointer it was given.
	/// </summary>
	internal enum OutputTarget : byte
	{
		Value,  //The __Value of the reference that was passed, or the parameter itself.
		Ptr,    //The Ptr property of an object that was passed by its address.
		Struct  //A struct pointer argument, whose value the Struct machinery reads back itself.
	}

	/// <summary>
	/// One argument's working storage: what a by-address argument's pointer refers to, whatever the argument
	/// has to hold for the duration of the call, and where its value goes once the call returns.
	/// </summary>
	internal struct ArgumentSlot
	{
		internal long Storage;
		internal GCHandle Handle;
		internal nint Bstr;
		//The parameter to write back to. A value never sits at index 0 (the list is type/value pairs), so 0 means
		//none -- which, like the two handle fields below, depends on the slots arriving zeroed. They do, because
		//they are stackalloc'd in a method the compiler emits localsinit for. Do NOT apply [SkipLocalsInit] to
		//this assembly without giving the two callers an explicit clear: garbage here writes back to arbitrary
		//parameter slots and frees garbage handles.
		internal int OutputParam;
		internal NativeTypeCode Code;
		internal OutputTarget Kind;
	}

	/// <summary>
	/// Turns one <see cref="Dll.DllCall"/>/<see cref="Com.ComCall"/> parameter list into the raw argument slots
	/// the generated call reads, and turns what comes back into script values.
	/// <para>A ref struct, because all of its working storage belongs to the calling frame: a call allocates
	/// only for what it genuinely hands to the callee, such as a narrowed copy of a string.</para>
	/// </summary>
	internal ref struct ArgumentHelper
	{
		internal Span<long> args;

		/// <summary>
		/// Bitwise info about the location of float and double type arguments, as well as the return type:
		/// bit i is set if argument i is floating point, and bit n if the return value is.
		/// </summary>
		internal ulong floatingTypeMask;

		/// <summary>
		/// True when a type or value could not be converted. An <see cref="Errors"/> helper only throws while the
		/// script has not suppressed the error, so on the suppressed path the parse simply stops -- and the call
		/// must not go ahead: the remaining slots hold no arguments, and an output slot registered before the
		/// value that failed would have <see cref="CopyBack"/> read through an address the callee never filled.
		/// </summary>
		internal bool Failed => failed;

		private Span<ArgumentSlot> slots;
		private readonly bool isCom;
		private bool failed;
		private bool returnByAddress;
		private bool hresult;
		private bool hasReturn;
		private bool voidReturn;
		private bool disposed;
		private NativeTypeCode returnCode;
		private Func<long, object> structReturnConverter;

		/// <summary>
		/// <paramref name="args"/> and <paramref name="slots"/> must be stack memory belonging to the caller,
		/// because a by-address argument is handed the address of its own slot and the callee writes there.
		/// </summary>
		internal ArgumentHelper(object[] parameters, Span<long> args, Span<ArgumentSlot> slots, bool isCom = false)
		{
			this.args = args;
			this.slots = slots;
			this.isCom = isCom;
			returnCode = NativeTypeCode.Int;//An omitted return type is an integer.

			try
			{
				ConvertParameters(parameters);
			}
			catch
			{
				//A parse error throws out of the constructor, before the caller's using can own the instance,
				//so anything already pinned has to be released here or it stays pinned for the process's life.
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;

			for (var i = 0; i < slots.Length; i++)
			{
				ref var slot = ref slots[i];

				if (slot.Handle.IsAllocated)
					slot.Handle.Free();

				if (slot.Bstr != 0)
					Marshal.FreeBSTR(slot.Bstr);
			}
		}

		private void ConvertParameters(object[] parameters)
		{
			var argCount = parameters.Length / 2;

			//The return type, when present, is the unpaired last element. Handling it first keeps the loop
			//below purely about type/value pairs, and its bit in the mask needs only the argument count.
			if ((parameters.Length & 1) != 0)
				ParseReturnType(parameters[^1], argCount);

			for (var n = 0; n < argCount; n++)
			{
				if (!ConvertArgument(parameters, n))
				{
					failed = true;
					return;
				}
			}
		}

		private void ParseReturnType(object rawTag, int argCount)
		{
			hasReturn = true;
			var code = NativeTypeCode.Invalid;

			if (rawTag is string tag)
			{
				var span = tag.AsSpan().Trim();

				//The return type may name the calling convention first, which the generated call always uses.
				if (span.Length >= 5 && span[..5].Equals("cdecl", StringComparison.OrdinalIgnoreCase))
				{
					span = span[5..].TrimStart();

					if (span.Length == 0)//A bare convention keeps the default return type.
					{
						hasReturn = false;
						return;
					}
				}

				//A return type may carry the same trailing '*'/'P' an argument does, meaning the function hands
				//back the address of the value rather than the value; it is dereferenced below.
				if (span.Length != 0)
				{
					var last = span[^1];

					if (last == '*' || (last | 0x20) == 'p')
					{
						returnByAddress = true;
						span = span[..^1].TrimEnd();
					}
				}

				code = NativeType.Parse(span);
			}
			else if (TrySetStructReturn(rawTag, out code))
				return;

			if (code == NativeTypeCode.Invalid)
			{
				_ = Errors.ValueErrorOccurred($"Arg or return type of {rawTag} is invalid.");
				failed = true;
				return;
			}

			returnCode = code;

			switch (code)
			{
				case NativeTypeCode.Void:
					voidReturn = true;
					break;

				case NativeTypeCode.HResult:
					hresult = true;
					hasReturn = false;//Needed for ComCall OSError.
					break;

				case NativeTypeCode.Float:
				case NativeTypeCode.Double:
					floatingTypeMask |= 1UL << argCount;
					break;
			}
		}

		/// <summary>
		/// Converts the <paramref name="n"/>th type/value pair into its argument slot. Returns false when the
		/// pair could not be converted and the call must not proceed.
		/// </summary>
		private unsafe bool ConvertArgument(object[] parameters, int n)
		{
			var rawTag = parameters[n * 2];
			var valueIndex = n * 2 + 1;
			var p = parameters[valueIndex];
			var hasPtrSuffix = false;
			var code = NativeTypeCode.Invalid;

			if (rawTag is string tag)
			{
				var span = tag.AsSpan().Trim();

				if (span.Length != 0)
				{
					//The argument's address is passed instead of its value when the type carries a trailing
					//'*' or 'P'.
					var last = span[^1];
					hasPtrSuffix = last == '*' || (last | 0x20) == 'p';
					code = NativeType.Parse(hasPtrSuffix ? span[..^1].TrimEnd() : span);
				}
			}
			else if (Struct.TryResolvePointerClass(rawTag, out var pointerType, out var targetType))
			{
				//A struct pointer class passes the address of the struct value, materializing one when needed.
				ConvertPtr(n, NormalizeStructPointerArg(parameters, p, n, valueIndex, pointerType, targetType));
				return true;
			}
			else if (Struct.TryResolveClass(rawTag, out var structType))
			{
				//A numeric type class is handled as the built-in type it mirrors, so that e.g. a Float64
				//argument is passed in a floating-point register rather than as raw bits in an integer one.
				code = Struct.GetPrimitiveTypeCode(structType);

				if (code == NativeTypeCode.Invalid)
				{
					args[n] = ReadStructValueArg(p, structType);
					return true;
				}
			}

			if (code is NativeTypeCode.Invalid or NativeTypeCode.Void)
			{
				_ = Errors.ValueErrorOccurred($"Arg or return type of {rawTag} is invalid.");
				return false;
			}

			if (hasPtrSuffix)
			{
				var isPtrType = code is NativeTypeCode.Ptr or NativeTypeCode.UPtr;
				var isPtrObject = false;

				//An object passed to "ptr*" seeds the slot with its own pointer and receives the written
				//value back through that property; any other reference (a VarRef, whatever its type) is
				//unwrapped to the value it holds, so an in/out parameter starts from the script's value.
				if (p is Any kso)
				{
					object kptr;

					if (isPtrType && ((kso is IPointable ip && (kptr = (object)ip.Ptr) != null)
									  || (kptr = Script.GetPropertyValueOrNull(kso, "ptr")) != null))
					{
						isPtrObject = true;
						p = kptr;
					}
					else if (Script.GetPropertyValueOrNull(kso, "__Value") is object inner)
						p = inner;
				}

				//The argument's own slot holds the value, seeded with whatever came in so that an in/out
				//parameter carries it, and the call is handed that slot's address to write through.
				ref var slot = ref slots[n];
				slot.Storage = code switch
				{
					NativeTypeCode.Float => BitConverter.SingleToInt32Bits(p.Af()),
					NativeTypeCode.Double => BitConverter.DoubleToInt64Bits(p.Ad()),
					_ => p.Al()
				};
				args[n] = (nint)Unsafe.AsPointer(ref slot.Storage);
				AddOutput(n, valueIndex, code, isPtrObject ? OutputTarget.Ptr : OutputTarget.Value);
				return true;
			}

			switch (code)
			{
				case NativeTypeCode.Ptr:
				case NativeTypeCode.UPtr:
					ConvertPtr(n, p);
					break;

				case NativeTypeCode.Int:
				case NativeTypeCode.HResult:
					args[n] = p.Ai();
					break;

				case NativeTypeCode.UInt:
					args[n] = p.Aui();
					break;

				case NativeTypeCode.Int64:
				case NativeTypeCode.UInt64:
					args[n] = p.Al();
					break;

				case NativeTypeCode.Short:
					args[n] = unchecked((short)p.Al());
					break;

				case NativeTypeCode.UShort:
					args[n] = unchecked((ushort)p.Al());
					break;

				case NativeTypeCode.Char:
					args[n] = unchecked((sbyte)p.Al());
					break;

				case NativeTypeCode.UChar:
					args[n] = unchecked((byte)p.Al());
					break;

				case NativeTypeCode.Float:
					floatingTypeMask |= 1UL << n;
					//Deliberately the 32-bit pattern in the low half of the slot, NOT a double. The generated
					//call loads every floating slot as a double, so these eight bytes reach the callee's XMM
					//register verbatim -- and a float parameter is read from that register's low 32 bits, which
					//is exactly where they are. Widening to a double here would move them and pass garbage.
					args[n] = BitConverter.SingleToInt32Bits(p.Af());
					break;

				case NativeTypeCode.Double:
					floatingTypeMask |= 1UL << n;
					args[n] = BitConverter.DoubleToInt64Bits(p.Ad());
					break;

				default:
					return ConvertString(parameters, valueIndex, n, code, p);
			}

			return true;
		}

		/// <summary>
		/// Passes a string argument. A script's own string is passed by its characters, pinned for the duration
		/// of the call; one that has to survive being written into is entangled with a <see cref="StringBuffer"/>
		/// which owns the memory and hands the result back afterwards.
		/// </summary>
		private bool ConvertString(object[] parameters, int paramIndex, int n, NativeTypeCode code, object p)
		{
			if (code == NativeTypeCode.BStr)
			{
				if (p is string bs)
				{
					var bstr = Marshal.StringToBSTR(bs);
					slots[n].Bstr = bstr;
					args[n] = bstr;
					return true;
				}
			}
			//A string is always passed by reference, so a reference holding one is an output even without a '*'.
			else if (p is Any kso && Script.GetPropertyValueOrNull(kso, "__Value") is object inner)
			{
				AddOutput(n, paramIndex, NativeTypeCode.Ptr, OutputTarget.Value);

				if (inner is string entangled)
				{
					var buffer = code == NativeTypeCode.AStr ? new StringBuffer(entangled, null, "ANSI") : new StringBuffer(entangled);
					slots[n].Handle = GCHandle.Alloc(buffer, GCHandleType.Normal);
					buffer.EntangledString = kso;
					parameters[paramIndex] = buffer;
					args[n] = buffer.Ptr;
					return true;
				}

				p = inner;
			}

			if (p is string s)
			{
				if (code == NativeTypeCode.AStr)
				{
					//A .NET string is already NUL terminated internally, so only the narrowed copy needs one added.
					var ansi = new byte[Encoding.ASCII.GetByteCount(s) + 1];
					_ = Encoding.ASCII.GetBytes(s, ansi);
					args[n] = Pin(n, ansi);
				}
				else
					args[n] = Pin(n, s);

				return true;
			}

			if (p is StringBuffer sb)
			{
				parameters[paramIndex] = sb;
				sb.UpdateBufferFromEntangledString();
				args[n] = sb.Ptr;
				return true;
			}

			_ = Errors.TypeErrorOccurred(p, typeof(string));
			return false;
		}

		private void ConvertPtr(int n, object p)
		{
			if (p is long lptr)
				args[n] = lptr;
			else if (p is string s)
				args[n] = s.Al();
			else if (p is IPointable ip)//Before the Any test: a Buffer is both, and this reads the long without boxing it.
				args[n] = ip.Ptr;
			else if (p is Any kso && Script.GetPropertyValueOrNull(kso, "ptr") is object kptr)
				args[n] = kptr.Al();

#if WINDOWS
			else if (Marshal.IsComObject(p))
			{
				var pUnk = Marshal.GetIUnknownForObject(p);
				args[n] = pUnk;
				_ = Marshal.Release(pUnk);
			}

#endif
			else
				args[n] = Pin(n, p);
		}

		/// <summary>
		/// Pins <paramref name="value"/> for the duration of the call and returns its address.
		/// </summary>
		private long Pin(int n, object value)
		{
			ref var handle = ref slots[n].Handle;
			handle = GCHandle.Alloc(value, GCHandleType.Pinned);
			return handle.AddrOfPinnedObject();
		}

		private void AddOutput(int n, int paramIndex, NativeTypeCode code, OutputTarget kind)
		{
			ref var slot = ref slots[n];
			slot.OutputParam = paramIndex;
			slot.Code = code;
			slot.Kind = kind;
		}

		/// <summary>
		/// Writes back every argument whose address was passed, now that the call has filled it in.
		/// </summary>
		internal unsafe void CopyBack(object[] parameters)
		{
			for (var i = 0; i < slots.Length; i++)
			{
				ref var slot = ref slots[i];
				var pi = slot.OutputParam;

				if (pi == 0)
					continue;

				if (slot.Kind == OutputTarget.Struct)
				{
					if (parameters[pi] is Any structRef && Script.GetPropertyValueOrNull(structRef, "__Value") is Struct structValue)
						_ = Script.SetPropertyValue(structRef, "__Value", Struct.GetOutputValue(structValue));

					continue;
				}

				var address = (nint)args[i];

				if (parameters[pi] is StringBuffer sb)
				{
					sb.UpdateEntangledStringFromBuffer();
					parameters[pi] = sb.EntangledString;
				}
				else if (parameters[pi] is Any kso)
					_ = Script.SetPropertyValue(kso, slot.Kind == OutputTarget.Ptr ? "ptr" : "__Value", NativeType.ReadMemory(slot.Code, address));
				else
					parameters[pi] = NativeType.ReadMemory(slot.Code, address);
			}
		}

		/// <summary>
		/// Turns the contents of the return register into the value the script receives.
		/// </summary>
		internal unsafe object ConvertReturnValue(long value)
		{
			if (voidReturn)
				return Script.DefaultObject;

			if (structReturnConverter != null)
				return structReturnConverter(value);

			//A '*'-suffixed return type means the register holds the address of the value, so read the value
			//out of it: its own width, since only that many bytes at the address are the callee's to define.
			//A null address is left as 0 rather than faulting; the callee reported no value.
			if (returnByAddress)
			{
				value = value == 0 ? 0
						: NativeType.SizeOf(returnCode) == 8 ? Unsafe.ReadUnaligned<long>((void*)value)
						: Unsafe.ReadUnaligned<int>((void*)value);
			}

			//An omitted return type on a COM call is an HRESULT, which reports a failure as an OSError.
			if (hresult || (isCom && !hasReturn))
				return Errors.OSErrorOccurredForHR(unchecked((int)value));

			//Only the declared width of the return register is meaningful, so narrow types are truncated (and
			//sign- or zero-extended) rather than passed through raw, since the remaining bits are unspecified.
			switch (returnCode)
			{
				case NativeTypeCode.Int: return (long)unchecked((int)value);

				case NativeTypeCode.UInt: return (long)unchecked((uint)value);

				case NativeTypeCode.Short: return (long)unchecked((short)value);

				case NativeTypeCode.UShort: return (long)unchecked((ushort)value);

				case NativeTypeCode.Char: return (long)unchecked((sbyte)value);

				case NativeTypeCode.UChar: return (long)unchecked((byte)value);

				//Widened the same way NumGet widens a float, so 1.2345f comes back as 1.2345.
				case NativeTypeCode.Float: return NativeType.WidenFloat(BitConverter.Int32BitsToSingle(unchecked((int)value)));

				case NativeTypeCode.Double: return BitConverter.Int64BitsToDouble(value);

				case NativeTypeCode.AStr:
				{
					var ansi = (nint)value;
					var str = Marshal.PtrToStringAnsi(ansi);
					Marshal.FreeHGlobal(ansi);
					return str;
				}

				case NativeTypeCode.Str:
				case NativeTypeCode.WStr:
				case NativeTypeCode.BStr:
				{
					var str = Marshal.PtrToStringUni((nint)value);
					_ = Objects.ObjFree(value);//If this string came from us, it will be freed, else no action.
					return str;
				}

				default: return value;//Int64, UInt64, Ptr and UPtr are already the whole register.
			}
		}

		// Returns true when the return type was fully handled here. Otherwise builtinCode receives the built-in
		// type to use instead (for a numeric type class), or Invalid when rawReturnType is not a struct class.
		private bool TrySetStructReturn(object rawReturnType, out NativeTypeCode builtinCode)
		{
			builtinCode = NativeTypeCode.Invalid;

			if (Struct.TryResolvePointerClass(rawReturnType, out _, out var pointerTargetType))
			{
				returnCode = NativeTypeCode.Ptr;
				structReturnConverter = ptr => Struct.IsPrimitive(pointerTargetType)
											   ? Struct.ReadPrimitiveValue(pointerTargetType, ptr)
											   : ptr == 0 ? null : Script.Invoke(Script.TheScript.Vars.Statics[pointerTargetType], "At", ptr);
				return true;
			}

			if (!Struct.TryResolveClass(rawReturnType, out var structType))
				return false;

			// A numeric type class converts directly rather than being instantiated, so it behaves exactly like the
			// built-in type it mirrors — including floats, which are returned in a different register.
			builtinCode = Struct.GetPrimitiveTypeCode(structType);

			if (builtinCode != NativeTypeCode.Invalid)
				return false;

			returnCode = NativeTypeCode.Ptr;

			if (Struct.GetSize(structType) > sizeof(long))
				_ = Errors.ValueErrorOccurred("Struct return values larger than 8 bytes are not supported yet.");

			structReturnConverter = slot =>
			{
				var result = Struct.CreateInstance(structType);
				Struct.WriteArgumentSlot(result, slot);
				return result;
			};
			return true;
		}

		private static long ReadStructValueArg(object input, Type structType)
		{
			var value = GetStructValue(structType, input);
			var size = Struct.GetSize(structType);

			if (size > sizeof(long))
				return (long)Errors.ValueErrorOccurred("Struct arguments larger than 8 bytes are not supported yet.", null, 0L);

			return Struct.ReadArgumentSlot(value);
		}

		private object NormalizeStructPointerArg(object[] parameters, object value, int n, int paramIndex, Type pointerType, Type targetType)
		{
			// The caller's ref, read through the property so a subclass overriding __Value is honored. The plain-ref
			// fast path lives inside GetPropertyValueOrNull, so reading it this way costs nothing extra.
			var targetRef = value as VarRef;
			var input = targetRef != null ? Script.GetPropertyValueOrNull(targetRef, "__Value") : value;
			var hasInput = input != null;

			if (!hasInput && targetRef == null)
				return 0L;

			if (hasInput && Struct.IsStructInstance(input, pointerType))
			{
				var pointerValue = (Struct)input;
				return pointerValue.GetPrimitiveValue();
			}

			var structValue = GetStructValue(targetType, input);

			if (targetRef != null)
			{
				parameters[paramIndex] = new VarRef(() => structValue, value => _ = Script.SetPropertyValue(targetRef, "__Value", value));
				AddOutput(n, paramIndex, NativeTypeCode.Ptr, OutputTarget.Struct);
			}
			else if (!ReferenceEquals(structValue, input))
				parameters[paramIndex] = structValue;

			return structValue;
		}

		private static Struct GetStructValue(Type structType, object input)
		{
			if (Struct.IsStructInstance(input, structType))
				return (Struct)input;

			var value = Struct.CreateInstance(structType);

			if (input != null)
				Struct.SetInputValue(value, input);

			return value;
		}
	}
}
