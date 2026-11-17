using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	internal static class StructMarshal
	{
		// The memory type a numeric type class names to NumGet and NumPut, or Invalid for any other class. A pointer
		// to a numeric class names its target's kind of number at pointer size, as AutoHotkey reads it: Float64.Ptr
		// reads a Double on a 64-bit process, and a pointer to an unsigned integer reads signed there. [v2.1-alpha.34+]
		internal static NativeTypeCode GetNumericTypeCode(Class cls)
		{
			if (!Struct.TryResolveClass(cls, out var definition))
				return NativeTypeCode.Invalid;

			if (definition.NativePointerTarget is not { } target)
				return definition.NumericKind;

			return target.NumericKind switch
			{
				NativeTypeCode.Invalid => NativeTypeCode.Invalid,
				NativeTypeCode.Float or NativeTypeCode.Double => IntPtr.Size == 8 ? NativeTypeCode.Double : NativeTypeCode.Float,
				NativeTypeCode.UChar or NativeTypeCode.UShort or NativeTypeCode.UInt => IntPtr.Size == 8 ? NativeTypeCode.Int64 : NativeTypeCode.UInt,
				_ => IntPtr.Size == 8 ? NativeTypeCode.Int64 : NativeTypeCode.Int
			};
		}

		// Integer slots carry raw bits; floating-point parameters require their native register class.
		internal enum CallbackSlot : byte { Integer, Float32, Float64 }

		// Resolved once when the callback is registered. Aggregate arguments need fresh storage because script may retain them.
		internal readonly struct CallbackConversion(NativeTypeCode kind, StructDefinition aggregate, long size = 0, bool isArray = false)
		{
			internal readonly NativeTypeCode Kind = kind;
			internal readonly StructDefinition Aggregate = aggregate;
			internal readonly long Size = size;
			internal readonly bool IsArray = isArray;

			// Which register file the value travels in, and so whether it needs a native signature of its own.
			internal CallbackSlot Slot => Kind switch
			{
				NativeTypeCode.Float => CallbackSlot.Float32,
				NativeTypeCode.Double => CallbackSlot.Float64,
				_ => CallbackSlot.Integer
			};

			// An aggregate whose slot holds its address rather than its bytes, and which is returned through memory the caller provides.
			internal bool ByAddress => Aggregate != null && !IsArray && !InRegister(Size);
		}

		// Windows x64 passes any aggregate, in a register when it is 1, 2, 4 or 8 bytes and by address otherwise, as AutoHotkey does.
		// Other ABIs classify aggregates by their fields, which is not implemented, so there only the shapes every ABI passes alike are allowed.
		private static readonly bool passesAnyAggregate = OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

		internal static bool InRegister(long size) => size is 1 or 2 or 4 or 8;

		// Resolves how a declared callback type is passed and converted. Returns false and sets error if unsupported.
		internal static bool TryGetCallbackConversion(StructDefinition definition, bool isReturn, out CallbackConversion conversion, out string error)
		{
			var info = definition.Layout;
			error = null;

			if (info.IsInvalid)
			{
				conversion = default;
				error = StructDefinition.InvalidMessage;
				return false;
			}

			if (info.IsArray)
			{
				conversion = default;

				if (isReturn)
				{
					error = "An array type cannot be a callback return type.";
					return false;
				}

				conversion = new(NativeTypeCode.Invalid, definition, isArray: true);
				return true;
			}

			if (info.IsNativePrimitive)
			{
				conversion = new(info.PrimitiveKind, null);
				return true;
			}

			conversion = default;
			error = AggregateError(definition);
			if (error != null)
				return false;

			conversion = new(NativeTypeCode.Invalid, definition, info.Size);
			return true;
		}

		internal static string AggregateError(StructDefinition definition) =>
			definition.Layout.Size == 0 ? $"A native value of type {definition.Name} must not be empty."
			: passesAnyAggregate ? null
			: !InRegister(definition.Layout.Size) ? $"A native value of type {definition.Name} must be 1, 2, 4 or 8 bytes on this platform."
			: definition.IsAllFloatingPoint() ? $"A native value of type {definition.Name} contains only floating-point fields, which is not supported on this platform."
			: definition.IsUnaligned() ? $"A native value of type {definition.Name} contains unaligned fields, which is not supported on this platform."
			: null;

		// The struct a native value of the type is read from: the input itself, or a new instance assigned the input through
		// its __Value, as AutoHotkey converts it. Returns null after a reported error.
		internal static Struct ToStruct(StructDefinition definition, object input)
		{
			if (Struct.IsStructInstance(input, definition))
				return (Struct)input;

			var value = Struct.CreateInstance(definition);

			if (value != null && input != null && !value.TrySetInputValue(input))
				return null;

			return value;
		}

		// Converts one incoming argument from the raw bits it arrived in to the script value for its declared
		// type. Reading the declared width out of the slot is what makes a single long-wide native parameter
		// able to carry any integer-like type.
		internal static unsafe object ConvertCallbackArgument(in CallbackConversion conversion, long slot)
		{
			switch (conversion.Kind)
			{
				case NativeTypeCode.Char: return (long)(sbyte)slot;
				case NativeTypeCode.UChar: return (long)(byte)slot;
				case NativeTypeCode.Short: return (long)(short)slot;
				case NativeTypeCode.UShort: return (long)(ushort)slot;
				case NativeTypeCode.Int: return (long)(int)slot;
				case NativeTypeCode.UInt: return (long)(uint)slot;
				case NativeTypeCode.Float: return (double)BitConverter.Int32BitsToSingle((int)slot);
				case NativeTypeCode.Double: return BitConverter.Int64BitsToDouble(slot);
				case NativeTypeCode.Int64:
				case NativeTypeCode.Ptr: return slot;// Already the whole slot.
			}

			if (conversion.IsArray)
				return Struct.CreateView(conversion.Aggregate, slot)?.GetOutputValue() ?? Script.DefaultObject;

			// A copy, since the script may keep it after the memory it arrived in is gone.
			return Struct.CreateCopy(conversion.Aggregate, conversion.ByAddress ? slot : (long)&slot)?.GetOutputValue() ?? Script.DefaultObject;
		}

		// Converts a callback's return value to the raw bits its declared type leaves in the return register: for an aggregate
		// returned through memory, the address of that memory, which the caller passed as buffer. A number is read leniently,
		// as AutoHotkey reads a callback's result, and a struct as an argument is converted. This runs in the callback's
		// thread, which reports an error raised here as its own.
		internal static unsafe long ConvertCallbackReturn(in CallbackConversion conversion, object value, long buffer)
		{
			long l;
			double d;

			switch (conversion.Kind)
			{
				case NativeTypeCode.Char: return value.TryCoerceLong(out l) ? (sbyte)l : 0L;
				case NativeTypeCode.UChar: return value.TryCoerceLong(out l) ? (byte)l : 0L;
				case NativeTypeCode.Short: return value.TryCoerceLong(out l) ? (short)l : 0L;
				case NativeTypeCode.UShort: return value.TryCoerceLong(out l) ? (ushort)l : 0L;
				case NativeTypeCode.Int: return value.TryCoerceInt(out var i) ? i : 0L;
				case NativeTypeCode.UInt: return value.TryCoerceLong(out l) ? unchecked((uint)l) : 0L;
				case NativeTypeCode.Float: return value.TryCoerceDouble(out d) ? BitConverter.SingleToInt32Bits((float)d) : 0L;
				case NativeTypeCode.Double: return value.TryCoerceDouble(out d) ? BitConverter.DoubleToInt64Bits(d) : 0L;
				case NativeTypeCode.Int64:
				case NativeTypeCode.Ptr: return value.TryCoerceLong(out l) ? l : 0L;
			}

			if (!conversion.ByAddress)
				return ToStruct(conversion.Aggregate, value) is { } result && TryReadArgumentSlot(result, conversion.Size, out var slot) ? slot : 0;

			// A value that could not be converted leaves zeros, so the caller never reads uninitialized memory.
			var source = ToStruct(conversion.Aggregate, value) is { } converted && converted.TryGetDataPointer(out var address) ? address : 0;

			if (source != 0)
				System.Buffer.MemoryCopy((void*)source, (void*)buffer, conversion.Size, conversion.Size);
			else
				NativeMemory.Clear((void*)buffer, (nuint)conversion.Size);

			return buffer;
		}

		internal static unsafe bool TryReadArgumentSlot(Struct value, long size, out long slot)
		{
			slot = 0;
			if (!value.TryGetDataPointer(out var address))
				return false;

			long bits = 0;
			System.Buffer.MemoryCopy((void*)address, &bits, sizeof(long), size);
			slot = bits;
			return true;
		}

		internal static unsafe void WriteArgumentSlot(Struct value, long slot)
		{
			var size = value.Layout.Size;

			System.Buffer.MemoryCopy(&slot, (void*)value.Ptr, size, size);
		}

	}
}
