namespace Keysharp.Core.Common.Invoke
{
	/// <summary>
	/// Creates a native callback pointer which can be used to call the target function object.
	/// The callback starts from a closed delegate which contains a slot id and forwards the
	/// arguments to Dispatch, where the arity and slot id are used to get the corresponding
	/// DelegateHolder, then pushes a new green thread (unless Fast mode is used) and calls 
	/// the target function. The DelegateHolder itself is not bound because GetFunctionPointerForDelegate
	/// is a heavy operation (performance tests show >10x slowdowns compared to caching it), so instead
	/// we reserve "slots" for each arity. For example CallbackCreate with argument count 1 gets 
	/// slot id 0 for arity 1, a subsequent CallbackCreate gets slot id 1 etc, and later if the
	/// pointer is freed with CallbackFree and another CallbackCreate is done then the previously 
	/// created delegate for the slot is reused. The worst case scenario is a lot of CallbackCreate
	/// calls without any freeing, which the user shouldn't do anyway because it means a memory leak.
	/// </summary>
	public class DelegateHolder : KeysharpObject, IPointable, IDisposable
	{
		internal Any funcObj;
		readonly bool _fast, _reference;
		readonly int _arity;
		private int _slotId;
		// Used to prevent slot reuse collisions
		internal int _assignedGeneration;

		// Native function pointer to pass into unmanaged code.
		public long Ptr { get; internal set; }

		/// <summary>
		/// Creates a holder and receiving a delegate.
		/// </summary>
		public DelegateHolder(Any function, int arity, bool fast, bool reference)
		{
			funcObj = function;
			_fast = fast;
			_reference = reference;
			_arity = arity;

			int slotId = AritySlots.Rent(arity, this, out _assignedGeneration);
			_slotId = slotId;

			Ptr = AritySlotPointerCache.GetOrCreate(arity, slotId);
		}

		// Should only be called in CallbackFree. DelegateHolder shouldn't need a finalizer because
		// the reference is held in AritySlotPointerCache until it's explicitly freed.
		public void Dispose()
		{
			if (Ptr != 0) { 
				AritySlots.Return(_arity, _slotId);
				Ptr = 0;
			}
		}

		// Delegate defintions for arities 0..32
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
		/// with the arity (args.Length) to query the corresponding DelegateHolder. We then create a new green 
		/// thread an run the target function there with the received arguments.
		/// </summary>
		/// <param name="slotId">Slot id for arity N corresponding to the DelegateHolder.</param>
		/// <param name="args">Argument list for the target function.</param>
		/// <returns>Result of the target function converted to a long.</returns>
		private static long Dispatch(int slotId, params long[] args)
		{
			var (dh, gen) = AritySlots.Get(args.Length, slotId);
			if (dh == null || gen != dh._assignedGeneration)
			{
				if (Script.TheScript.hasExited) return 0L;
				throw new Error("Stale callback pointer");
			}

			object val = null;
			var state = (false, (ThreadVariables)null);
			_ = Flow.TryCatch(() =>
			{
				var script = Script.TheScript;

				if (!dh._fast)
					state = script.Threads.BeginThread();

				if (dh._reference)
				{
					var gh = GCHandle.Alloc(args, GCHandleType.Pinned);

					try
					{
						unsafe
						{
							long* ptr = (long*)gh.AddrOfPinnedObject().ToPointer();
							val = Script.Invoke(dh.funcObj, "Call", (long)ptr);
						}
					}
					finally { gh.Free(); }
				}
				else
				{
					val = Script.Invoke(dh.funcObj, "Call", System.Array.ConvertAll(args, item => (object)item));
				}

				if (state.Item1)
					_ = script.Threads.EndThread(state);

				script.ExitIfNotPersistent();
			}, !state.Item1, state);
			return ConvertResult(val);
		}

		internal static long ConvertResult(object val) => val switch
		{
			long l => l,
			bool b => b ? 1L : 0L,
			double d => (long)d,
			string s => s.Length == 0 ? 0L : 0L,
			_ => 0L
		};
	}

	// Thread-safe way to reserve a slot for a given arity. Additionally keeps track of the generation
	// for the slot to prevent accidental reuses of previous slots.
	static class AritySlots
	{
		private sealed class SlotBucket
		{
			public DelegateHolder[] Slots = new DelegateHolder[64];
			public int[] Generations = new int[64];
			public Stack<int> Free = new Stack<int>(Enumerable.Range(0, 64).Reverse());
			public readonly Lock Lock = new();
		}

		private static readonly SlotBucket[] _buckets = Enumerable.Range(0, 33).Select(_ => new SlotBucket()).ToArray();

		public static int Rent(int arity, DelegateHolder holder, out int genOut)
		{
			var b = _buckets[arity];
			lock (b.Lock)
			{
				if (b.Free.Count == 0)
					Grow(b);
				int id = b.Free.Pop();
				genOut = b.Generations[id];
				Volatile.Write(ref b.Slots[id], holder);
				return id;
			}
		}

		public static void Return(int arity, int id)
		{
			var b = _buckets[arity];
			lock (b.Lock)
			{
				Volatile.Write(ref b.Slots[id], null);
				unchecked { b.Generations[id]++; }
				b.Free.Push(id);
			}
		}

		public static (DelegateHolder holder, int gen) Get(int arity, int id)
		{
			var b = _buckets[arity];
			var h = Volatile.Read(ref b.Slots[id]);
			var g = Volatile.Read(ref b.Generations[id]);
			return (h, g);
		}

		private static void Grow(SlotBucket b)
		{
			int oldLen = b.Slots.Length;
			System.Array.Resize(ref b.Slots, oldLen * 2);
			System.Array.Resize(ref b.Generations, oldLen * 2);
			for (int i = b.Slots.Length - 1; i >= oldLen; --i)
				b.Free.Push(i);
		}
	}

	// Thread-safe cache to store delegates for a given arity-slotId combination
	static class AritySlotPointerCache
	{
		private static readonly Lock _lock = new();
		private static readonly Dictionary<long, (Delegate keepAlive, nint ptr)> _map = new();

		public static nint GetOrCreate(int arity, int slotId)
		{
			long key = ((long)arity << 40) | (uint)slotId;
			lock (_lock)
			{
				if (_map.TryGetValue(key, out var hit))
					return hit.ptr;

				var del = DelegateHolder.CreateDelegateFor(arity, slotId);
				RuntimeHelpers.PrepareDelegate(del);
				var ptr = Marshal.GetFunctionPointerForDelegate(del);
				_map[key] = (del, ptr);
				return ptr;
			}
		}
	}

	/// <summary>
	/// Manages executable memory in 512-byte pages, providing fixed 64-byte chunks for DelegateHolder.
	/// Automatically allocates new pages when needed and reuses freed chunks.
	/// This is needed because VirtualAlloc is quite a heavy function, best called as few times as possible.
	///
	/// The implementation uses a Treiber stack to keep it mostly lock-free.
	/// </summary>
	public sealed class ExecutableMemoryPoolManager
	{
		private const int PageSize = 512;
		private const int ChunkSize = 64;
		private readonly Lock _lock = new();

		// Treiber‑stack head of free chunks (0 == empty)
		private nint _freeList;

		// All allocated pages
		private readonly List<nint> _pages = new List<nint>();

		// Current page and offset
		private nint _currentPage;
		private int _currentOffset = 0;

#if LINUX || OSX
		[DllImport("libc", SetLastError = true)]
		private static extern nint mmap(nint addr, nint length, int prot, int flags, int fd, nint offset);
		[DllImport("libc", SetLastError = true)]
		private static extern int munmap(nint addr, nint length);

		private const int PROT_READ = 1;
		private const int PROT_WRITE = 2;
		private const int PROT_EXEC = 4;
		private const int MAP_PRIVATE = 2;
		private const int MAP_ANONYMOUS = 0x20;
#endif

		public ExecutableMemoryPoolManager()
		{
			lock (_lock)
			{
				// eagerly allocate first page so _currentPage != 0
				var page = AllocatePage();
				Volatile.Write(ref _currentPage, page);
				// leave _currentOffset == 0
				_pages.Add(page);
			}
		}

		/// <summary>
		/// Rents a 32-byte executable chunk.
		/// </summary>
		public nint Rent()
		{
			// 1) Try lock‑free pop from the free list
			if (TryPopFree(out var recycled))
				return recycled;

			// 2) Fast bump‑pointer carve
			FastCarve:

			while (true)
			{
				// use CAS to bump _currentOffset, and we use the old offset as our memory
				int oldOffset = Volatile.Read(ref _currentOffset);
				int newOffset = oldOffset + ChunkSize;

				if (newOffset <= PageSize) // this means we wouldn't fit inside the current page
				{
					// try to claim [oldOffset, newOffset)
					if (Interlocked.CompareExchange(ref _currentOffset, newOffset, oldOffset) == oldOffset)
					{
						var page = Volatile.Read(ref _currentPage);
						return page + oldOffset;
					}

					// else another thread won the CAS — retry
					continue;
				}

				// fall through to slow‑path to grab lock and allocate a fresh page
				break;
			}

			// 3) Slow path: need a new page
			lock (_lock)
			{
				// it's possible another thread already replaced the page,
				// so check one more time if there's room on the current page:
				if (_currentOffset + ChunkSize <= PageSize)
				{
					goto FastCarve;
				}

				// allocate brand‑new page, reset offset
				var page = AllocatePage();
				Volatile.Write(ref _currentPage, page);
				Volatile.Write(ref _currentOffset, ChunkSize);
				_pages.Add(page);
				// return the first chunk
				return page;
			}
		}

		private bool TryPopFree(out nint ptr)
		{
			while (true)
			{
				var head = Volatile.Read(ref _freeList);

				if (head == 0)
				{
					// no reusable chunks available
					ptr = 0;
					return false;
				}

				// read the next pointer stored at address head
				var next = Marshal.ReadIntPtr(head);

				// try to swing _freeList from head → next
				if (Interlocked.CompareExchange(ref _freeList, next, head) == head)
				{
					ptr = head;
					return true;
				}

				// else retry
			}
		}

		private void PushFree(nint ptr)
		{
			while (true)
			{
				var head = Volatile.Read(ref _freeList);
				// write the old head into the first pointer‑sized bytes of the freed chunk,
				// using the chunk as a node in the Treiber stack
				Marshal.WriteIntPtr(ptr, head);

				// try to swing _freeList from head → ptr
				if (Interlocked.CompareExchange(ref _freeList, ptr, head) == head)
					return;

				// else another thread modified it — retry
			}
		}


		public void Return(nint ptr)
		{
			if (ptr == 0) return;

			PushFree(ptr);
		}

		/// <summary>
		/// Releases all allocated pages.
		/// </summary>
		public void Dispose()
		{
			lock (_lock)
			{
#if WINDOWS

				foreach (var page in _pages)
					WindowsAPI.VirtualFree(page, 0, (uint)VirtualAllocExTypes.MEM_RELEASE);

#elif LINUX || OSX

				foreach (var page in _pages)
					munmap(page, (nint)PageSize);

#endif
				_pages.Clear();
				_currentPage = 0;
				_currentOffset = 0;
			}
		}

		private nint AllocatePage()
		{
#if WINDOWS
			var ptr = WindowsAPI.VirtualAlloc(0, (nint)PageSize, (uint)VirtualAllocExTypes.MEM_COMMIT, (uint)AccessProtectionFlags.PAGE_EXECUTE_READWRITE);
			return ptr == 0 ? throw new InvalidOperationException($"VirtualAlloc failed: {Marshal.GetLastWin32Error()}") : ptr;
#elif LINUX || OSX
			var ptr = mmap(0, (nint)PageSize, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);

			if (ptr == new nint(-1))
				throw new InvalidOperationException("mmap failed");

			return ptr;
#else
#error Unsupported OS for AllocatePage
			return 0;
#endif
		}
	}
}