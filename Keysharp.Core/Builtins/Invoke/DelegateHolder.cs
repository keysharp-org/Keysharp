namespace Keysharp.Builtins
{
	/// <summary>
	/// Creates a native callback pointer which can be used to call the target function object.
	/// The callback starts from a closed delegate which contains a slot id and forwards the
	/// arguments to Dispatch, where the arity and slot id are used to get the corresponding
	/// DelegateHolder, then pushes a new green thread (unless Fast mode is used) and calls
	/// the target function. The delegate is closed over a slot id rather than over the DelegateHolder
	/// because a pointer can then be cached and reused: GetFunctionPointerForDelegate is a heavy
	/// operation (performance tests show >10x slowdowns compared to caching it). For example
	/// CallbackCreate with argument count 1 gets slot id 0 for arity 1, a subsequent CallbackCreate gets
	/// slot id 1 etc, and later if the pointer is freed with CallbackFree and another CallbackCreate is
	/// done then the previously created delegate for the slot is reused. The worst case scenario is a lot
	/// of CallbackCreate calls without any freeing, which the user shouldn't do anyway because it means a
	/// memory leak.
	///
	/// A typed callback (CallbackCreate with an array of parameter types followed by the return type)
	/// shares all of that machinery. Its declared types decide only how each value is converted, because
	/// every integer-like value of 8 bytes or less arrives in an integer register or a stack slot and can
	/// be read at its declared width out of a long. So a typed signature whose values all travel in
	/// integer registers uses the very same arity delegates and slots, and only a floating-point parameter
	/// or return value needs a native signature of its own, emitted by TypedCallbackSignature.
	/// </summary>
	public class DelegateHolder : KeysharpObject, IPointable, IDisposable
	{
		// The most parameters a callback can have, which is how far the pre-declared delegates and slot buckets go.
		internal const int MaxArity = AritySlots.MaxArity;

		internal readonly Any funcObj;
		// The target resolved once, so an invocation does not repeat a by-name member lookup for a receiver
		// which cannot change. Null when funcObj is not directly callable and has to go through Invoke.
		readonly KeysharpFunc _fn;
		readonly bool _fast, _reference;
		readonly int _arity;
		readonly SchedulerRegistration _ownerState;
		readonly int _slotId;
		// Non-null only for a typed callback: how to convert each parameter, and the return value (default
		// when the return type is "void"). These decide conversion only; they do not affect the native
		// signature beyond which register file each value arrives in (see Struct.CallbackSlot).
		// All of these are written before AritySlots.Rent publishes the holder, whose lock and Volatile.Write
		// pair with Get's Volatile.Read, so Dispatch on another thread always sees them fully initialised.
		readonly Struct.CallbackConversion[] _typedParameters;
		readonly Struct.CallbackConversion _typedReturn;
		readonly bool _typedVoid;
		private long _ptr;
		internal ScriptEventScheduler OwnerScheduler => _ownerState.OwnerScheduler;

		// Native function pointer to pass into unmanaged code.
		public long Ptr { get => Volatile.Read(ref _ptr); internal set => Volatile.Write(ref _ptr, value); }

		/// <summary>
		/// Creates a holder and receiving a delegate.
		/// </summary>
		public DelegateHolder(Any function, int arity, bool fast, bool reference)
		{
			if (arity < 0 || arity > MaxArity)
				throw new ValueError($"A callback cannot have more than {MaxArity} parameters.");

			funcObj = function;
			_fn = Script.ResolveDirectCallTarget(function);
			_fast = fast;
			_reference = reference;
			_arity = arity;
			_ownerState = new(Script.TheScript?.EventScheduler, true);
			_ownerState.OwnerScheduler?.RegisterOwnedDelegate(this);
			_slotId = Publish(id => CallbackPointerCache.GetOrCreateForArity(arity, id));
		}

		/// <summary>
		/// Renting the slot is what publishes this holder to Dispatch, so registration has to come first: a stale
		/// pointer for the same slot could otherwise arrive before _ownerState exists. That leaves a window where
		/// a failure would strand a registered holder holding a persistence root which Dispose could never
		/// release, so anything failing after registration unwinds it here.
		/// </summary>
		private int Publish(Func<int, nint> createPointer)
		{
			var slotId = -1;

			try
			{
				slotId = AritySlots.Rent(_arity, this);
				Ptr = createPointer(slotId);
				return slotId;
			}
			catch
			{
				if (slotId >= 0)
					AritySlots.Return(_arity, slotId);

				_ownerState.OwnerScheduler?.UnregisterOwnedDelegate(this);
				_ownerState.Clear();
				throw;
			}
		}

		/// <summary>
		/// Creates a holder for a typed callback from conversions already resolved by <see cref="Dll.CallbackCreate"/>:
		/// one per parameter, followed by the return value's (or a default one when the return type is "void").
		/// The declared types only decide how each value is converted, so a signature whose values all travel in
		/// integer registers reuses the same arity-based delegates and slots as an untyped callback; only a
		/// floating-point parameter or return value needs a native signature of its own.
		/// </summary>
		internal DelegateHolder(Any function, Struct.CallbackConversion[] conversions, bool typedVoid, bool fast, bool cdecl)
		{
			funcObj = function;
			_fn = Script.ResolveDirectCallTarget(function);
			_fast = fast;
			_reference = false;
			_arity = conversions.Length - 1;
			_typedVoid = typedVoid;
			// Non-null even at arity 0, which is what marks this holder as typed for Dispatch.
			_typedParameters = conversions[.._arity];
			_typedReturn = conversions[_arity];
			_ownerState = new(Script.TheScript?.EventScheduler, true);
			_ownerState.OwnerScheduler?.RegisterOwnedDelegate(this);
			_slotId = Publish(id => TypedCallbackSignature.IsAllInteger(conversions)
								? CallbackPointerCache.GetOrCreateForArity(_arity, id)
								: TypedCallbackSignature.GetOrCreate(conversions, cdecl, id));
		}

		// Should only be called in CallbackFree. DelegateHolder shouldn't need a finalizer because
		// the reference is held in CallbackPointerCache until it's explicitly freed.
		void IDisposable.Dispose()
		{
			// Claim the disposal exactly once: CallbackFree can race another script thread, or the scheduler
			// teardown in DisposeOwnedByScheduler, and returning the slot twice would hand one id to two holders.
			if (Interlocked.Exchange(ref _ptr, 0) == 0)
				return;

			var ownerScheduler = OwnerScheduler;
			AritySlots.Return(_arity, _slotId);
			ownerScheduler?.UnregisterOwnedDelegate(this);
			_ownerState.Clear();
		}

		// Delegate definitions for arities 0..32
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity0();
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity1(long p0);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity2(long p0, long p1);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity3(long p0, long p1, long p2);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity4(long p0, long p1, long p2, long p3);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity5(long p0, long p1, long p2, long p3, long p4);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity6(long p0, long p1, long p2, long p3, long p4, long p5);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity7(long p0, long p1, long p2, long p3, long p4, long p5, long p6);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity8(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity9(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity10(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity11(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity12(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity13(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity14(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity15(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity16(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity17(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity18(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity19(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity20(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity21(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity22(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity23(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity24(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity25(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity26(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity27(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity28(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26, long p27);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity29(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26, long p27, long p28);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity30(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26, long p27, long p28, long p29);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity31(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26, long p27, long p28, long p29, long p30);
		[UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate long NativeCallbackArity32(long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, long p8, long p9, long p10, long p11, long p12, long p13, long p14, long p15, long p16, long p17, long p18, long p19, long p20, long p21, long p22, long p23, long p24, long p25, long p26, long p27, long p28, long p29, long p30, long p31);

		// Produces a closed delegate bound to slotId, which is then used to query the correct DelegateHolder.
		internal static Delegate CreateDelegateFor(int arity, int slotId) => arity switch
		{
			0 => (NativeCallbackArity0)(()
				=> Dispatch(slotId)),
			1 => (NativeCallbackArity1)((a0)
				=> Dispatch(slotId, a0)),
			2 => (NativeCallbackArity2)((a0, a1)
				=> Dispatch(slotId, a0, a1)),
			3 => (NativeCallbackArity3)((a0, a1, a2)
				=> Dispatch(slotId, a0, a1, a2)),
			4 => (NativeCallbackArity4)((a0, a1, a2, a3)
				=> Dispatch(slotId, a0, a1, a2, a3)),
			5 => (NativeCallbackArity5)((a0, a1, a2, a3, a4)
				=> Dispatch(slotId, a0, a1, a2, a3, a4)),
			6 => (NativeCallbackArity6)((a0, a1, a2, a3, a4, a5)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5)),
			7 => (NativeCallbackArity7)((a0, a1, a2, a3, a4, a5, a6)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6)),
			8 => (NativeCallbackArity8)((a0, a1, a2, a3, a4, a5, a6, a7)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7)),
			9 => (NativeCallbackArity9)((a0, a1, a2, a3, a4, a5, a6, a7, a8)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8)),
			10 => (NativeCallbackArity10)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9)),
			11 => (NativeCallbackArity11)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10)),
			12 => (NativeCallbackArity12)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11)),
			13 => (NativeCallbackArity13)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12)),
			14 => (NativeCallbackArity14)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13)),
			15 => (NativeCallbackArity15)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14)),
			16 => (NativeCallbackArity16)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15)),
			17 => (NativeCallbackArity17)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16)),
			18 => (NativeCallbackArity18)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17)),
			19 => (NativeCallbackArity19)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18)),
			20 => (NativeCallbackArity20)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19)),
			21 => (NativeCallbackArity21)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20)),
			22 => (NativeCallbackArity22)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21)),
			23 => (NativeCallbackArity23)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22)),
			24 => (NativeCallbackArity24)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23)),
			25 => (NativeCallbackArity25)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24)),
			26 => (NativeCallbackArity26)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25)),
			27 => (NativeCallbackArity27)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26)),
			28 => (NativeCallbackArity28)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27)),
			29 => (NativeCallbackArity29)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28)),
			30 => (NativeCallbackArity30)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29)),
			31 => (NativeCallbackArity31)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29, a30)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29, a30)),
			32 => (NativeCallbackArity32)((a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29, a30, a31)
				=> Dispatch(slotId, a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29, a30, a31)),
			_ => throw new ArgumentOutOfRangeException(nameof(arity))
		};


		/// <summary>
		/// NativeCallback delegate calls this function with its bound slot id, which we use in combination
		/// with the arity (args.Length) to query the corresponding DelegateHolder. The target function then runs
		/// on a new green thread, unless Fast mode was used, in which case it runs directly.
		/// </summary>
		/// <param name="slotId">Slot id for arity N corresponding to the DelegateHolder.</param>
		/// <param name="args">Argument list for the target function.</param>
		/// <returns>Result of the target function converted to a long.</returns>
		internal static long Dispatch(int slotId, params long[] args)
		{
			var dh = AritySlots.Get(args.Length, slotId);

			if (dh == null)
			{
				// Nothing can be dispatched, and there is no return value that would be less wrong than another.
				// Throwing does unwind through the caller's native frames, which is only safe when that caller is
				// our own DllCall, but it is what makes calling a freed pointer a catchable script error instead
				// of the silent corruption or crash it is in AutoHotkey. Exceptions from the callback itself do
				// not come through here: ExecuteCallback contains those.
				if (Script.TheScript?.hasExited != false)
					return 0L;

				throw new Error("Stale callback pointer");
			}

			// A typed callback interprets the same raw slots according to its declared types instead.
			return dh._typedParameters != null ? dh.InvokeTyped(args) : dh.InvokeUntyped(args);
		}

		private long InvokeUntyped(long[] rawArgs)
		{
			// In reference mode the script receives a pointer to the raw arguments, so boxing them would be
			// wasted work. Otherwise box each one, which a loop does without the delegate Array.ConvertAll needs.
			object[] args = null;

			if (!_reference)
			{
				args = new object[rawArgs.Length];

				for (var i = 0; i < args.Length; ++i)
					args[i] = rawArgs[i];
			}

			return ConvertResult(InvokeCallback(args, rawArgs));
		}

		// Converts each raw argument slot per its declared type, then returns the raw bits the declared return
		// type leaves in the return register (zero for "void", which the native signature ignores).
		private long InvokeTyped(long[] rawArgs)
		{
			var args = new object[_typedParameters.Length];

			for (var i = 0; i < args.Length; ++i)
				args[i] = Struct.ConvertCallbackArgument(_typedParameters[i], rawArgs[i]);

			var result = InvokeCallback(args, null);
			// A null result means the callback never ran (the scheduler was disposed, or the thread could not
			// start), so there is no value to convert; a declared struct return would reject null as a TypeError.
			return _typedVoid || result == null ? 0L : Struct.ConvertCallbackReturn(_typedReturn, result);
		}

		// Calls the target, resolving its result to null when it returned no value so that both the untyped and
		// the typed caller can apply their own conversion.
		private object CallTarget(object[] args) =>
			_fn != null ? _fn.Call(args) : Script.InvokeOrNull(funcObj, null, args);

		private object InvokeCallback(object[] args, long[] rawArgs)
		{
			var script = Script.TheScript;
			var targetScheduler = OwnerScheduler ?? script.EventScheduler;
			(object value, bool completed) execution;

			if (_fast)
			{
				// On the scheduler's own thread InvokeSynchronous would just call the delegate inline, so run the
				// body directly there: capturing it would otherwise allocate a closure and a delegate on every
				// single invocation, which is the case Fast mode exists to keep cheap. This also skips the
				// disposed check InvokeSynchronous opens with, deliberately: a callback arriving on the owning
				// thread after disposal queues nothing and is harmless, whereas throwing would cross the native
				// caller's frames.
				execution = targetScheduler.OwnsCurrentThread
							? ExecuteTarget(args, rawArgs)
							: targetScheduler.InvokeSynchronous(() => ExecuteTarget(args, rawArgs));
			}
			else
			{
				// Match AutoHotkey's callback behavior: incoming native callbacks are treated like
				// emergency interruptions, so they must not be blocked by the current thread's
				// critical/uninterruptible state or a higher current priority.
				if (targetScheduler.IsDisposed)
					return null;

				var launchPriority = script.Threads.CurrentThread.priority;
				var launched = targetScheduler.OwnsCurrentThread
							   ? RunInPseudoThread(targetScheduler, launchPriority, args, rawArgs)
							   : targetScheduler.InvokeSynchronous(() => RunInPseudoThread(targetScheduler, launchPriority, args, rawArgs));

				if (launched.status != ScriptEventExecutionResult.Executed)
					return null;

				execution = launched.execution;
			}

			if (execution.completed)
				script.ExitIfNotPersistent();

			return execution.value;
		}

		private (object value, bool completed) ExecuteTarget(object[] args, long[] rawArgs)
		{
			object val = null;
			var completed = false;

			try
			{
				if (_reference)
				{
					var gh = GCHandle.Alloc(rawArgs, GCHandleType.Pinned);

					try
					{
						unsafe
						{
							long* ptr = (long*)gh.AddrOfPinnedObject().ToPointer();
							val = CallTarget([(long)ptr]);
						}
					}
					finally
					{
						gh.Free();
					}
				}
				else
					val = CallTarget(args);

				completed = true;
			}
			catch (Exception ex)
			{
				_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
			}

			return (val, completed);
		}

		private (ScriptEventExecutionResult status, (object value, bool completed) execution) RunInPseudoThread(
			ScriptEventScheduler targetScheduler, long launchPriority, object[] args, long[] rawArgs)
		{
			using var thread = targetScheduler.StartPseudoThreadScope(launchPriority, true, false, false, ThreadKind.Callback);

			if (!thread.Started)
				return (thread.Result, (null, false));

			return (ScriptEventExecutionResult.Executed, ExecuteTarget(args, rawArgs));
		}

		// A callback coerces its return value to an integer exactly as AutoHotkey's RegisterCallbackCStub
		// does for an untyped callback: number_to_return = (UINT_PTR)TokenToInt64(result_token).
		// TokenToInt64 returns an integer as-is, truncates a float toward zero, parses the numeric value
		// of a string ("2" -> 2; non-numeric or "" -> 0), and yields 0 for an object or unset return.
		// User code never returns a raw pointer type (e.g. StrPtr returns a long), so nint isn't handled.
		internal static long ConvertResult(object val) => val switch
		{
			long l => l,
			double d => (long)d,
			bool b => b ? 1L : 0L,
			_ => val.Al()// string -> numeric value; object/unset -> 0
		};

		internal static void DisposeOwnedByScheduler(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return;

			foreach (var holder in scheduler.GetOwnedDelegatesSnapshot())
				(holder as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// Native entry points for typed callbacks whose values do not all travel in integer registers, which are the
	/// only ones needing a signature the pre-declared arity delegates cannot express. A signature is reduced to
	/// its register-file shape (see <see cref="Struct.CallbackSlot"/>), so only floating-point positions and the
	/// return kind vary; everything integer-like is declared as a long and read at its declared width by
	/// <see cref="DelegateHolder.Dispatch"/>. The stub reinterprets its floating-point arguments as raw bits and
	/// hands them to that same dispatcher, so typed and untyped callbacks share one dispatch path.
	///
	/// Both the emitted signature and the stub are cached, the latter per slot exactly as
	/// <see cref="CallbackPointerCache"/> does, since GetFunctionPointerForDelegate is far too costly to repeat
	/// per callback.
	/// </summary>
	internal static class TypedCallbackSignature
	{
		private static readonly Dictionary<string, Type> _delegateTypes = new();
		private static ModuleBuilder _module;
		private static int _nextId;

		private static readonly MethodInfo singleToBits = typeof(BitConverter).GetMethod(nameof(BitConverter.SingleToInt32Bits), [typeof(float)]);
		private static readonly MethodInfo doubleToBits = typeof(BitConverter).GetMethod(nameof(BitConverter.DoubleToInt64Bits), [typeof(double)]);
		private static readonly MethodInfo bitsToSingle = typeof(BitConverter).GetMethod(nameof(BitConverter.Int32BitsToSingle), [typeof(int)]);
		private static readonly MethodInfo bitsToDouble = typeof(BitConverter).GetMethod(nameof(BitConverter.Int64BitsToDouble), [typeof(long)]);
		private static readonly MethodInfo dispatch = typeof(DelegateHolder).GetMethod(nameof(DelegateHolder.Dispatch),
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, [typeof(int), typeof(long[])], null);

		internal static bool IsAllInteger(Struct.CallbackConversion[] conversions)
		{
			foreach (var conversion in conversions)
				if (conversion.Slot != Struct.CallbackSlot.Integer)
					return false;

			return true;
		}

		internal static nint GetOrCreate(Struct.CallbackConversion[] conversions, bool cdecl, int slotId)
		{
			var shape = (cdecl ? "c" : "s") + string.Concat(conversions.Select(c => (char)('a' + (int)c.Slot)));
			// Called under the cache's lock, which is also what guards _delegateTypes, _module and _nextId.
			return CallbackPointerCache.GetOrCreate(shape, slotId, () =>
			{
				if (!_delegateTypes.TryGetValue(shape, out var delegateType))
					_delegateTypes[shape] = delegateType = CreateDelegateType(conversions, cdecl);

				return CreateStub(conversions, slotId, delegateType);
			});
		}

		private static Type NativeTypeOf(Struct.CallbackSlot slot) => slot switch
		{
			Struct.CallbackSlot.Float32 => typeof(float),
			Struct.CallbackSlot.Float64 => typeof(double),
			_ => typeof(long)
		};

		// The native parameter types of a shape, which is its conversions minus the trailing return value.
		private static Type[] NativeParameterTypes(Struct.CallbackConversion[] conversions)
		{
			var parameterTypes = new Type[conversions.Length - 1];

			for (var i = 0; i < parameterTypes.Length; ++i)
				parameterTypes[i] = NativeTypeOf(conversions[i].Slot);

			return parameterTypes;
		}

		// Emits the stub body: pack every argument into a long[] of raw bits, call the shared dispatcher, then
		// reinterpret its result as the declared return type.
		private static Delegate CreateStub(Struct.CallbackConversion[] conversions, int slotId, Type delegateType)
		{
			var arity = conversions.Length - 1;
			var returnSlot = conversions[arity].Slot;
			var dm = new DynamicMethod("TypedCallbackStub", NativeTypeOf(returnSlot), NativeParameterTypes(conversions),
									   typeof(DelegateHolder).Module, skipVisibility: true);
			var il = dm.GetILGenerator();
			il.Emit(OpCodes.Ldc_I4, slotId);
			il.Emit(OpCodes.Ldc_I4, arity);
			il.Emit(OpCodes.Newarr, typeof(long));

			for (var i = 0; i < arity; ++i)
			{
				il.Emit(OpCodes.Dup);
				il.Emit(OpCodes.Ldc_I4, i);
				// Ldarg takes a 2-byte operand, so the index has to be passed as a short: the int overload
				// would write 4 bytes and leave two stray bytes in the instruction stream.
				il.Emit(OpCodes.Ldarg, (short)i);

				if (conversions[i].Slot == Struct.CallbackSlot.Float32)
				{
					il.Emit(OpCodes.Call, singleToBits);
					il.Emit(OpCodes.Conv_U8);// The reader only looks at the low 32 bits, so keep them unpolluted.
				}
				else if (conversions[i].Slot == Struct.CallbackSlot.Float64)
					il.Emit(OpCodes.Call, doubleToBits);

				il.Emit(OpCodes.Stelem_I8);
			}

			il.Emit(OpCodes.Call, dispatch);

			if (returnSlot == Struct.CallbackSlot.Float32)
			{
				il.Emit(OpCodes.Conv_I4);
				il.Emit(OpCodes.Call, bitsToSingle);
			}
			else if (returnSlot == Struct.CallbackSlot.Float64)
				il.Emit(OpCodes.Call, bitsToDouble);

			il.Emit(OpCodes.Ret);
			return dm.CreateDelegate(delegateType);
		}

		// A delegate type is the one thing DynamicMethod cannot express, so the shape's signature is emitted once.
		private static Type CreateDelegateType(Struct.CallbackConversion[] conversions, bool cdecl)
		{
			_module ??= AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Keysharp.TypedCallbacks"),
						AssemblyBuilderAccess.Run).DefineDynamicModule("Keysharp.TypedCallbacks");
			var parameterTypes = NativeParameterTypes(conversions);
			var builder = _module.DefineType("TypedCallback_" + (++_nextId),
											 TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
											 typeof(MulticastDelegate));
			builder.SetCustomAttribute(new CustomAttributeBuilder(
										   typeof(UnmanagedFunctionPointerAttribute).GetConstructor([typeof(CallingConvention)]),
										   [cdecl ? CallingConvention.Cdecl : CallingConvention.Winapi]));
			var ctor = builder.DefineConstructor(
						   MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
						   CallingConventions.Standard, [typeof(object), typeof(nint)]);
			ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
			var invoke = builder.DefineMethod("Invoke",
											  MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
											  NativeTypeOf(conversions[^1].Slot), parameterTypes);
			invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
			return builder.CreateType();
		}
	}

	/// <summary>
	/// Reserves a slot per arity, so a callback's native entry point can be a delegate closed over nothing but a
	/// small integer. Renting and returning are serialised per arity, while Dispatch reads without a lock.
	///
	/// An empty slot is what tells Dispatch that a pointer belongs to a rental which has since ended. That can
	/// only be detected while the slot sits free: once it is rented again the old pointer becomes a live entry
	/// point for the new holder, because a pointer is cached per (signature, slot) and deliberately outlives any
	/// one CallbackFree. Calling a freed callback pointer is undefined in AutoHotkey too.
	/// </summary>
	static class AritySlots
	{
		// The largest arity there is a bucket (and a pre-declared delegate type) for.
		internal const int MaxArity = 32;
		private const int PageShift = 6;
		private const int PageLength = 1 << PageShift;

		private sealed class SlotBucket
		{
			// Paged so that growing only appends: a page object is never replaced, so a reader which raced the
			// growth and is still holding the previous page array nevertheless reaches the live slot. Resizing a
			// flat array instead would let a reader see the new array before the copied elements, or keep reading
			// a stale copy, and report a live callback as stale.
			internal DelegateHolder[][] Pages = [new DelegateHolder[PageLength]];
			internal readonly Stack<int> Free = new(Enumerable.Range(0, PageLength).Reverse());
			internal readonly Lock Lock = new();
		}

		private static readonly SlotBucket[] _buckets = Enumerable.Range(0, MaxArity + 1).Select(_ => new SlotBucket()).ToArray();

		public static int Rent(int arity, DelegateHolder holder)
		{
			var b = _buckets[arity];

			lock (b.Lock)
			{
				if (b.Free.Count == 0)
					Grow(b);

				var id = b.Free.Pop();
				Volatile.Write(ref b.Pages[id >> PageShift][id & (PageLength - 1)], holder);
				return id;
			}
		}

		public static void Return(int arity, int id)
		{
			var b = _buckets[arity];

			lock (b.Lock)
			{
				Volatile.Write(ref b.Pages[id >> PageShift][id & (PageLength - 1)], null);
				b.Free.Push(id);
			}
		}

		public static DelegateHolder Get(int arity, int id)
		{
			var pages = Volatile.Read(ref _buckets[arity].Pages);
			var page = id >> PageShift;
			return page >= pages.Length ? null : Volatile.Read(ref pages[page][id & (PageLength - 1)]);
		}

		private static void Grow(SlotBucket b)
		{
			var pages = b.Pages;
			var oldCount = pages.Length;
			var grown = new DelegateHolder[oldCount + 1][];
			System.Array.Copy(pages, grown, oldCount);
			grown[oldCount] = new DelegateHolder[PageLength];
			Volatile.Write(ref b.Pages, grown);

			for (var id = (oldCount + 1) * PageLength - 1; id >= oldCount * PageLength; --id)
				b.Free.Push(id);
		}
	}

	/// <summary>
	/// Thread-safe cache of native callback pointers, keyed by signature and slot id. Both the pre-declared
	/// arity delegates and the emitted typed signatures go through here, because the invariant that matters is
	/// the same for either source: GetFunctionPointerForDelegate is far too costly to repeat per callback, so a
	/// pointer is created once per (signature, slot) and then reused for the lifetime of the process, including
	/// across CallbackFree followed by another CallbackCreate landing on the same slot. Holding the delegate
	/// here is also what keeps it alive, which is why DelegateHolder needs no finalizer.
	/// </summary>
	static class CallbackPointerCache
	{
		private static readonly Lock _lock = new();
		private static readonly Dictionary<(string signature, int slotId), (Delegate keepAlive, nint ptr)> _map = new();

		public static nint GetOrCreate(string signature, int slotId, Func<Delegate> create)
		{
			lock (_lock)
			{
				if (_map.TryGetValue((signature, slotId), out var hit))
					return hit.ptr;

				var del = create();
				RuntimeHelpers.PrepareDelegate(del);
				var ptr = Marshal.GetFunctionPointerForDelegate(del);
				_map[(signature, slotId)] = (del, ptr);
				return ptr;
			}
		}

		// The pre-declared all-long delegate for an arity, which is the signature of an untyped callback and of
		// any typed callback whose values all travel in integer registers. Its "a" prefix cannot collide with an
		// emitted shape's key, which always begins with the calling convention's "s" or "c".
		public static nint GetOrCreateForArity(int arity, int slotId) =>
			GetOrCreate("a" + arity, slotId, () => DelegateHolder.CreateDelegateFor(arity, slotId));
	}

}
