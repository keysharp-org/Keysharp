namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A mutual-exclusion lock for code shared between <see cref="RealThread"/>s. Scripts know it as
		/// <c>Lock</c>; the CLR type is named differently because <c>Lock</c> is <see cref="System.Threading.Lock"/>
		/// in C#, the same reason <c>Func</c> is <c>KeysharpFunc</c>.
		/// <para>
		/// <see cref="LockRun"/> remains the way to hold a lock for the duration of one call and is not duplicated
		/// here. This class exists for the two things <c>LockRun</c> cannot express: acquiring with a timeout, and
		/// holding a lock across several statements. A <c>Lock</c> is itself an object, so <c>LockRun</c> accepts
		/// one and the two forms exclude each other.</para>
		/// <para>
		/// The lock is held by a real thread, not by a script thread, and it is reentrant: the same real thread may
		/// acquire it repeatedly, and must release it once per acquisition. <see cref="Acquire"/> blocks that whole
		/// real thread, so acquiring on the main thread stalls the script's message loop until it succeeds — pass a
		/// timeout there rather than waiting indefinitely on work a worker might be slow to finish.</para>
		/// <para>
		/// Because ownership is per real thread, a pseudo-thread that interrupts a lock holder on the same real
		/// thread (a timer, a hotkey) counts as the same owner: it can re-enter the lock, and a <see cref="Release"/>
		/// there releases the interrupted thread's acquisition. Guard sections should therefore be short and should
		/// not span a point where the thread can be interrupted, or the interrupting thread should use
		/// <c>Critical</c>.</para>
		/// </summary>
		[UserDeclaredName("Lock")]
		public class KeysharpLock : KeysharpObject
		{
			// Monitor is used directly on this object so that LockRun(lockObj, …), which locks whatever object it is
			// given, and Acquire/Release refer to the same monitor. There is deliberately no IsHeld: its answer is
			// stale the instant it is produced, so nothing can act on it — Acquire(0) is the usable form.

			public KeysharpLock(params object[] args) : base(args) { }

			/// <summary>
			/// Acquires the lock, blocking until it is free or <paramref name="timeout"/> milliseconds elapse.
			/// </summary>
			/// <param name="timeout">Milliseconds to wait. Default: wait indefinitely.</param>
			/// <returns>True if the lock was acquired, false if the timeout elapsed first.</returns>
			public object Acquire(object timeout = null)
			{
				var timeoutVal = timeout.Ai(-1);
				return System.Threading.Monitor.TryEnter(this, timeoutVal < 0 ? Timeout.Infinite : timeoutVal);
			}

			/// <summary>
			/// Releases the lock once. Must be called by the real thread that acquired it, once per successful
			/// <see cref="Acquire"/>.
			/// </summary>
			public object Release()
			{
				if (!System.Threading.Monitor.IsEntered(this))
					return Errors.ErrorOccurred("Cannot release a lock this thread does not hold.");

				System.Threading.Monitor.Exit(this);
				return DefaultObject;
			}

			public override string ToString() => "Lock";
		}
	}
}
