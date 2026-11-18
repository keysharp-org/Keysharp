using Keysharp.Builtins;

namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Calls a native function with slots from the caller's stack frame and returns the raw result bits.
	/// </summary>
	internal unsafe delegate long NativeCall(nint fn, long* args);

	internal class DllData
	{
		// These caches are unbounded in principle but small in practice: delegateCache is keyed by the call's
		// shape (argument count plus which slots are floating point), of which a script uses a handful, and
		// procAddressCache by the function strings it writes.
		internal readonly ConcurrentDictionary<ulong, NativeCall> delegateCache = new ();
		// Signatures with more than 57 arguments, whose float mask does not fit alongside the count in the
		// packed key above. Vanishingly rare, so a tuple key costs nothing worth measuring.
		internal readonly ConcurrentDictionary<(int, ulong), NativeCall> wideDelegateCache = new ();
#if OSX
		internal readonly ConcurrentDictionary<string, NativeCall> packedInvokers = new (StringComparer.Ordinal);
#endif
		// The signature nearly every call has -- every argument and the return value an integer -- is indexed by
		// argument count alone, so the common case is an array read rather than a hash lookup.
		internal readonly NativeCall[] integerInvokers = new NativeCall[NativeInvoker.MaxArguments + 1];
		// Ordinal, matching the case-sensitive GetProcAddress/dlsym this memoises: a differently spelled function
		// name does not resolve there either, so an ignore-case cache would only paper over the failure for
		// whichever spelling happened to be looked up first, at the cost of a slower hash on every call.
		internal readonly ConcurrentDictionary<string, nint> procAddressCache = new (StringComparer.Ordinal);
#if WINDOWS
		// x64 register-duplication shims, one per (function, floating-point argument pattern).
		internal readonly ConcurrentDictionary<(nint, int), nint> shimCache = new ();
		internal readonly Lock shimLock = new ();
#endif
	}

	/// <summary>
	/// Generates and caches the machine code that actually performs a native call: one invoker per call
	/// signature, plus the x64 shim that duplicates floating point arguments into general purpose registers.
	/// <para>Kept apart from <see cref="Keysharp.Builtins.Dll"/>, which is the script-facing built-in: this
	/// half speaks only the platform ABI and knows nothing about script values.</para>
	/// </summary>
	internal static class NativeInvoker
	{
		internal static readonly MethodInfo DoubleToBits = typeof(BitConverter).GetMethod(nameof(BitConverter.DoubleToInt64Bits), [typeof(double)]);

		/// <summary>
		/// The mask needs one bit per native argument and one for the return register.
		/// </summary>
		internal const int MaxArguments = 63;

		/// <summary>
		/// Performs the call, compiling and caching an invoker for its signature the first time that signature
		/// is seen. Every argument slot is a <see cref="long"/> here; the mask says which ones the callee reads
		/// as floating point.
		/// </summary>
		/// <param name="fnPtr">The pointer to the native function to be called.</param>
		/// <param name="args">The argument list, which the call may write back through.</param>
		/// <param name="mask">64-bit mask containing information about floating point arguments and return value</param>
		/// <returns>The raw contents of the return register; a floating-point return arrives as its bits.</returns>
		internal static unsafe long NativeInvoke(nint fnPtr, Span<long> args, ulong mask, ReadOnlySpan<ArgumentSlot> slots = default)
		{
			int n = args.Length;

			//The callers check this before allocating their argument slots; re-checked here so that a future
			//caller which forgets gets a script-level error rather than an IndexOutOfRangeException out of the
			//invoker cache below.
			if ((uint)n > MaxArguments)
				return (long)Errors.ValueErrorOccurred($"A native call cannot take more than {MaxArguments} arguments.", ret: 0L);

			var dllData = TheScript.DllData;
			NativeCall invoke;

#if OSX
			if (packsStack && slots.Length > RegisterCount && GetPackedInvoker(dllData, slots, mask) is { } packed)
				invoke = packed;
			else
#endif
			if (mask == 0)
				invoke = dllData.integerInvokers[n] ??= CreateInvoker(n, 0);
			else if (n > 57)
				//The packed key below cannot carry mask bits this high, so these signatures get their own cache
				//rather than colliding. Rare enough to be worth a tuple key, but not worth re-JITting per call.
				invoke = dllData.wideDelegateCache.GetOrAdd((n, mask), static k => CreateInvoker(k.Item1, k.Item2));
			else
			{
				// pack n into bits 58–63, mask occupies bits 0–57
				ulong key = ((ulong)n << 58) | mask;

				invoke = dllData.delegateCache.GetOrAdd(key, static (_, state) => CreateInvoker(state.n, state.mask), (n, mask));
			}

#if WINDOWS

			// Under Windows x64 AutoHotkey passes the first four arguments in both general purpose and floating
			// point registers, so that a variadic callee, which reads them from the GPRs, sees them too. The
			// call is routed through a small shim which copies each such XMM into its GPR and jumps on.
			//
			// There is deliberately no ARM64 equivalent. AAPCS64 counts general and floating point argument
			// registers separately, so a value cannot occupy x_n and v_n at once the way it can occupy a GPR
			// and an XMM on x64 - duplicating it would consume an integer slot the next argument needs. A
			// non-variadic callee reads a double from v_n (which CreateInvoker's `double` parameter already
			// satisfies), while a Windows ARM64 variadic callee reads it from x_n, and DllCall has no way to
			// know which it is calling. Non-variadic calls are therefore correct and variadic calls with
			// floating point arguments are not; distinguishing them needs an explicit caller-supplied flag.
			if (isX64 && (mask & 0xFUL) != 0)
			{
				// Only argument slots count; a return bit inside the low four is not an argument.
				var pattern = (int)(mask & 0xFUL);

				if (n < 4)
					pattern &= ~(1 << n);

				if (pattern != 0)
					fnPtr = GetShim(dllData, fnPtr, pattern);
			}

#endif
			long result;

			fixed (long* p = args)
				result = invoke(fnPtr, p);

			ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
			return result;
		}

#if WINDOWS
		private static readonly bool isX64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;

		/// <summary>
		/// The most bytes <see cref="GetShim"/> can emit: four 5-byte MOVQs, a 6-byte JMP and an 8-byte address.
		/// </summary>
		private const int MaxShimBytes = (4 * 5) + 6 + 8;

		/// <summary>
		/// Fails to compile if a shim could ever overrun the chunk it is written into, since the two constants
		/// live in different files and the chunk next door holds another shim someone is about to jump to.
		/// </summary>
		private const uint ShimFitsChunk = ExecutableMemoryPoolManager.ChunkSize - MaxShimBytes;

		/// <summary>
		/// The shim for calling <paramref name="fnPtr"/> with the floating point arguments named by
		/// <paramref name="pattern"/> (a bit per slot 0-3) also in their general purpose registers.
		/// <para>Shims are built once and kept for the life of the script. Writing one into a chunk that was
		/// just executed, which a rent/return pair around every call amounts to, trips the processor's
		/// self-modifying-code detection and costs a pipeline flush per call, about 1.5 microseconds; a cached
		/// shim is written once and only ever executed afterwards.</para>
		/// </summary>
		private static unsafe nint GetShim(DllData dllData, nint fnPtr, int pattern)
		{
			var key = (fnPtr, pattern);

			if (dllData.shimCache.TryGetValue(key, out var shim))
				return shim;

			lock (dllData.shimLock)
			{
				if (dllData.shimCache.TryGetValue(key, out shim))
					return shim;

				shim = TheScript.ExecutableMemoryPoolManager.Rent();
				byte* ptr = (byte*)shim;

				// Emit MOVQ rcx←xmm0, rdx←xmm1, r8←xmm2, r9←xmm3 as needed. MOVQ from an XMM register to a
				// 64-bit GPR is `66 REX.W 0F 7E /r`: the 0x66 prefix and REX.W are both required, since
				// without REX.W the same opcode is MOVD and copies only the low 32 bits, and without the
				// 0x66 prefix it is the MMX MOVQ instead. ModRM is mod=11, reg=the XMM, rm=the GPR, with
				// REX.B extending rm to reach r8/r9.
				for (int i = 0; i < 4; i++)
				{
					if ((pattern & (1 << i)) == 0) continue;

					// rcx, rdx, r8, r9 as the low 3 bits of rm, the last two needing REX.B.
					const string rm = "\x01\x02\x00\x01";
					*ptr++ = 0x66;
					*ptr++ = (byte)(i < 2 ? 0x48 : 0x49);
					*ptr++ = 0x0F;
					*ptr++ = 0x7E;
					*ptr++ = (byte)(0xC0 | (i << 3) | rm[i]);
				}

				// Emit: JMP [RIP + 0]  => FF 25 00 00 00 00
				*ptr++ = 0xFF;* ptr++ = 0x25;* ptr++ = 0x00;* ptr++ = 0x00;* ptr++ = 0x00;* ptr++ = 0x00;

				// Followed immediately by the 64-bit absolute address
				*((long*)ptr) = fnPtr;
				System.Diagnostics.Debug.Assert(ptr + sizeof(long) - (byte*)shim <= ExecutableMemoryPoolManager.ChunkSize);
				dllData.shimCache[key] = shim;
				return shim;
			}
		}

		/// <summary>
		/// Drops every cached shim for <paramref name="fnPtr"/> and gives the executable chunks back. Only for a
		/// function pointer that is about to become invalid: an ordinary library export keeps its shim, which is
		/// the whole point of caching them.
		/// </summary>
		internal static void ReleaseShims(nint fnPtr)
		{
			if (fnPtr == 0)
				return;

			var dllData = TheScript.DllData;

			//The pattern is only four bits wide, so naming every key is cheaper than scanning the dictionary.
			for (var pattern = 1; pattern < 16; pattern++)
				if (dllData.shimCache.TryRemove((fnPtr, pattern), out var shim))
					TheScript.ExecutableMemoryPoolManager.Return(shim);
		}
#endif

		/// <summary>
		/// Generates an invoker for a signature using word-sized arguments and result registers.
		/// </summary>
		/// <param name="n">The number of argument slots expected.</param>
		/// <param name="mask">Bitmask containing info about possible floating point number argument positions</param>
		private static NativeCall CreateInvoker(int n, ulong mask)
		{
			var floatReturn = ((mask >> n) & 1) != 0;
			var parameterTypes = new Type[n];

			for (var i = 0; i < n; i++)
				parameterTypes[i] = ((mask >> i) & 1) != 0 ? typeof(double) : typeof(long);

			return EmitInvoker($"NativeCall_{n}_{mask}", parameterTypes, floatReturn);
		}

		private static unsafe NativeCall EmitInvoker(string name, Type[] parameterTypes, bool floatReturn)
		{
			var dm = new DynamicMethod(name, typeof(long), [typeof(nint), typeof(long*)], typeof(Dll).Module, skipVisibility: true);
			var il = dm.GetILGenerator();

			for (var i = 0; i < parameterTypes.Length; i++)
			{
				il.Emit(OpCodes.Ldarg_1);

				if (i != 0)
				{
					il.Emit(OpCodes.Ldc_I4, i * sizeof(long));
					il.Emit(OpCodes.Add);
				}

				il.Emit(OpCodes.Ldobj, parameterTypes[i]);
			}

			il.Emit(OpCodes.Ldarg_0);
			il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, floatReturn ? typeof(double) : typeof(long), parameterTypes);
			if (floatReturn)
				il.Emit(OpCodes.Call, DoubleToBits);
			il.Emit(OpCodes.Ret);
			return dm.CreateDelegate<NativeCall>();
		}

		internal static Type[] ParameterTypes(ReadOnlySpan<char> slots)
		{
			var types = new Type[slots.Length];
			for (var i = 0; i < types.Length; i++)
				types[i] = ParameterType((NativeTypeCode)slots[i]);
			return types;
		}

		internal static Type ParameterType(NativeTypeCode slot) => slot switch
		{
			NativeTypeCode.Float => typeof(float),
			NativeTypeCode.Double => typeof(double),
			NativeTypeCode.UChar => typeof(byte),
			NativeTypeCode.UShort => typeof(ushort),
			NativeTypeCode.UInt => typeof(uint),
			_ => typeof(long)
		};

#if OSX
		private const int RegisterCount = 8;
		private static readonly bool packsStack = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

		// Each register bank fills independently. Spilled scalars occupy their native widths,
		// while a supported non-HFA aggregate still occupies an eight-byte slot.
		private static NativeCall GetPackedInvoker(DllData data, ReadOnlySpan<ArgumentSlot> slots, ulong mask)
		{
			Span<char> shape = stackalloc char[slots.Length + 1];
			var integers = 0;
			var floats = 0;
			var packed = false;

			for (var i = 0; i < slots.Length; i++)
			{
				var type = slots[i].ScalarType;
				if (NativeType.IsFloating(type))
				{
					if (floats++ < RegisterCount)
						type = NativeTypeCode.Double;
				}
				else
					type = IntegerSlot(type, ref integers);

				shape[i] = (char)type;
				packed |= type is not (NativeTypeCode.Int64 or NativeTypeCode.Double);
			}

			if (!packed)
				return null;

			shape[^1] = (char)(((mask >> slots.Length) & 1) != 0 ? NativeTypeCode.Double : NativeTypeCode.Int64);
			if (data.packedInvokers.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(shape, out var invoker))
				return invoker;

			return data.packedInvokers.GetOrAdd(new string(shape), static key =>
				EmitInvoker("NativeCallPackedStack", ParameterTypes(key.AsSpan(..^1)), (NativeTypeCode)key[^1] == NativeTypeCode.Double));
		}
#endif

		// Integer registers already hold the extended value. Only a spill on Apple ARM64 needs its width.
		internal static NativeTypeCode IntegerSlot(NativeTypeCode type, ref int integers)
		{
#if OSX
			if (packsStack && integers++ >= RegisterCount)
				return type switch
				{
					NativeTypeCode.Char or NativeTypeCode.UChar => NativeTypeCode.UChar,
					NativeTypeCode.Short or NativeTypeCode.UShort => NativeTypeCode.UShort,
					NativeTypeCode.Int or NativeTypeCode.UInt or NativeTypeCode.HResult => NativeTypeCode.UInt,
					_ => NativeTypeCode.Int64
				};
#endif
			return NativeTypeCode.Int64;
		}
	}
}
