namespace Keysharp.Builtins
{
	/// <summary>
	/// One cooperative script thread — what AutoHotkey calls a "thread", as opposed to the real operating-system
	/// threads that <see cref="Ks.RealThread"/> describes. The current one is <c>A_Thread</c>; the ones beneath it
	/// are reached through <see cref="Underlying"/>, and the whole stack of a real thread through
	/// <c>RealThread.Threads</c>. This is the extension point for per-thread facts, which is why they live here
	/// instead of becoming one importable <c>A_*</c> global each.
	/// <para>
	/// Scripts know this class as <c>Thread</c>, and calling it runs the AutoHotkey <c>Thread</c> function
	/// (<see cref="staticCall"/>) — <c>Thread "NoTimers"</c>, <c>Thread "Priority", n</c>, <c>Thread "Interrupt", n</c>
	/// — so one name covers the thread settings and the thread object. The CLR type keeps a distinct name because
	/// <c>Thread</c> is taken by <see cref="System.Threading.Thread"/>, the same reason <c>Func</c> is
	/// <c>KeysharpFunc</c>.</para>
	/// <para>
	/// Thread state is pooled and reused (see <see cref="ThreadVariableManager"/>), so every wrapper captures the ID
	/// of the thread it was made for and re-checks it on each access. Once that thread ends the wrapper reports
	/// <see cref="IsActive"/> false; <see cref="Id"/> and <see cref="Index"/> keep answering from captured values and
	/// everything else throws <c>TargetError</c>, rather than silently describing whichever thread reused the
	/// slot.</para>
	/// <para>
	/// Exactly one wrapper exists per thread, so identity comparison answers "is this the one I am in":
	/// <c>thr == A_Thread</c>. There is deliberately no <c>IsCurrent</c> property for that.</para>
	/// <para>
	/// A wrapper may be read from any real thread, but only its owner may change it: every setter and
	/// <see cref="Exit"/> throw <c>TargetError</c> elsewhere. That is the same restriction targeted <c>Exit()</c>
	/// always had, enforced by the object instead of by an error return.</para>
	/// </summary>
	[UserDeclaredName("Thread")]
	public sealed class KeysharpThread : KeysharpObject
	{
		private readonly ThreadVariableManager manager;
		private readonly ThreadVariables tv;
		private readonly long id;

		internal KeysharpThread(ThreadVariableManager manager, ThreadVariables tv) : base()
		{
			this.manager = manager;
			this.tv = tv;
			id = tv.pseudoThreadId;
		}

		/// <summary>
		/// Sets an aspect of the current thread's behavior, as the AutoHotkey <c>Thread</c> function.
		/// This is what a script's <c>Thread "NoTimers"</c> / <c>Thread "Priority", n</c> /
		/// <c>Thread "Interrupt", n</c> calls, so calling the class does not construct one — obtain the current
		/// thread from <c>A_Thread</c> instead.
		/// </summary>
		/// <param name="subFunction">NoTimers, Priority or Interrupt.</param>
		/// <param name="value1">Has a different meaning depending on subFunction:<br/>
		///     NoTimers: True to disallow timers, else false to allow timers. Default: true.<br/>
		///     Priority: The thread priority as an integer in the range -2147483648 and 2147483647.<br/>
		///     Interrupt: The time in milliseconds that each newly launched thread is uninterruptible. Default: 17.
		/// </param>
		public static object staticCall(object @this, object subFunction, object value1 = null, object value2 = null)
		{
			var sf = subFunction.As();
			var script = Script.TheScript;

			if (string.Compare(sf, "notimers", true) == 0)
				script.Threads.AllowTimers = !(Options.OnOff(value1.As()) ?? false);
			else if (string.Compare(sf, "priority", true) == 0)
				script.Threads.CurrentThread.priority = value1.Al();
			else if (string.Compare(sf, "interrupt", true) == 0)
				script.uninterruptibleTime = value1.Ai(script.uninterruptibleTime);

			return DefaultObject;
		}

		/// <summary>
		/// A nearly unique 64-bit identifier: a 48-bit script-wide creation sequence in bits 63-16 and the
		/// zero-based stack position in bits 15-0. Unique for the script's lifetime in practice, but only
		/// meaningful while the thread is active.
		/// </summary>
		public long Id => id;

		/// <summary>
		/// The one-based position of this thread in its real thread's stack. 1 is the oldest active thread,
		/// matching <c>RealThread.Threads[1]</c>.
		/// </summary>
		public long Index => (id & 0xFFFF) + 1L;

		/// <summary>True while this thread is still running.</summary>
		public bool IsActive => tv.pseudoThreadId == id;

		/// <summary>
		/// What launched this thread: <c>"Auto"</c>, <c>"Hotkey"</c>, <c>"Hotstring"</c>, <c>"Timer"</c>,
		/// <c>"Event"</c>, <c>"Message"</c>, <c>"Callback"</c>, <c>"Input"</c>, <c>"WinEvent"</c>, <c>"Com"</c>,
		/// <c>"Clr"</c> or <c>"RealThread"</c>, and an empty string when the launch site does not name one.
		/// <c>"Event"</c> covers every registered handler — GUI events, menu items, OnExit, OnClipboardChange —
		/// because they all dispatch through one registry.
		/// </summary>
		public string Kind
		{
			get
			{
				var value = Live().kind;
				return value == ThreadKind.None ? "" : value.ToString();
			}
		}

		/// <summary>Milliseconds elapsed since this thread was launched.</summary>
		public long Elapsed => Environment.TickCount64 - Live().threadStartTick;

		/// <summary>
		/// This thread's priority. Reads back what <c>Thread "Priority", n</c> or <c>A_Priority</c> set;
		/// every thread starts at 0 unless its launcher (SetTimer, Hotkey, Menu.Add, a hotstring
		/// definition) gave it one.
		/// </summary>
		public object Priority
		{
			get => Live().priority;
			set => Mutable().priority = value.Al();
		}

		/// <summary>
		/// Whether this thread is critical, i.e. cannot be interrupted. Setting it is equivalent to
		/// <c>Critical true</c>/<c>Critical false</c> on that thread — this is the object form of the
		/// <c>Critical</c> function, which is why it is not spelled <c>IsCritical</c>: on this class an
		/// <c>Is</c> prefix marks a read-only predicate (<see cref="IsActive"/>, <see cref="IsInterruptible"/>),
		/// and a settable mode reads as the mode itself. <c>A_IsCritical</c> keeps its AHK spelling.
		/// </summary>
		// Settable script properties are object-typed and convert in the setter. Historically they had to be: a
		// script's `true` arrives as an Integer, and a bool-typed setter would fail the dynamic invoke with an
		// InvalidCastException. ArgCoercer lifted that constraint; these stay object-typed by choice, because
		// several of them accept more than a Boolean.
		public object Critical
		{
			get => Live().isCritical;
			set
			{
				// Mirrors Flow.Critical: turning it on pins uninterruptibility so it never times out, turning
				// it off makes the thread immediately interruptible regardless of "Thread Interrupt".
				var on = value.Ab();
				var target = Mutable();
				target.isCritical = on;
				target.configData.defaultIsCritical = on;
				target.configData.peekFrequency = on
					? ThreadVariables.DefaultUninterruptiblePeekFrequency
					: ThreadVariables.DefaultPeekFrequency;
				target.allowThreadToBeInterrupted = !on;

				if (on)
					target.UninterruptibleDuration = -1;

				manager.Owner.RecordMessageCheck();
			}
		}

		/// <summary>
		/// Whether this thread is paused — the object form of the <c>Pause</c> function. <c>A_IsPaused</c> is this
		/// property on <see cref="Underlying"/>, because <c>Pause</c> targets the thread beneath the one calling it.
		/// </summary>
		public object Paused
		{
			get => Live().isPaused;
			set => Mutable().isPaused = value.Ab();
		}

		/// <summary>
		/// Whether this thread can currently be interrupted by a new one. False during the startup window set by
		/// <c>Thread "Interrupt"</c> and for the whole life of a critical thread.
		/// </summary>
		public bool IsInterruptible
		{
			get
			{
				var target = Live();
				// The full interruptibility rule (which also consults the script-wide flow state and can flip the
				// thread's own flag as the startup window expires) only applies to the running thread.
				return IsCurrentThread()
					   ? manager.Owner.Threads.IsInterruptible()
					   : target.allowThreadToBeInterrupted && !target.isCritical;
			}
		}

		/// <summary>
		/// The thread this one interrupted, or an empty string for the oldest one on its real thread. Named for
		/// the same relationship the internal <c>Threads.UnderlyingThread</c> and the A_IsPaused documentation
		/// ("the thread immediately underneath the current thread") already describe.
		/// </summary>
		public object Underlying
		{
			get
			{
				var position = (int)(Live().pseudoThreadId & 0xFFFF);

				if (position <= 0)
					return DefaultObject;

				var under = manager.TryGetPseudoThread(position - 1);
				return under == null || under.pseudoThreadId == 0L ? DefaultObject : Wrap(manager, under);
			}
		}

		/// <summary>
		/// Requests that this thread exit. The current thread exits immediately and this therefore does not return;
		/// any other one is marked and unwinds when it next resumes and processes events, which is cooperative and
		/// does not asynchronously abort managed code. A later request replaces a pending exit code.
		/// </summary>
		/// <param name="exitCode">The process exit code to apply when the thread exits. Default: 0.</param>
		/// <returns>This thread's ID.</returns>
		public object Exit(object exitCode = null) => manager.Owner.Threads.RequestExit(Mutable(), exitCode.Ai());

		public override string ToString() => $"Thread {id}";

		/// <summary>Wraps <paramref name="tv"/>, reusing the wrapper already cached on it when there is one.</summary>
		internal static KeysharpThread Wrap(ThreadVariableManager manager, ThreadVariables tv)
			=> tv.threadObject ??= new KeysharpThread(manager, tv);

		/// <summary>
		/// Whether this is the thread the calling code is running in. Not script-visible: one wrapper is cached per
		/// thread, so a script writes <c>thr == A_Thread</c>.
		/// </summary>
		private bool IsCurrentThread()
			=> IsActive
			   && System.Threading.Thread.CurrentThread.ManagedThreadId == tv.threadId
			   && ReferenceEquals(manager.Owner.Threads.CurrentThread, tv);

		/// <summary>
		/// The live thread state, or a <c>TargetError</c> if this wrapper outlived its thread.
		/// </summary>
		private ThreadVariables Live()
		{
			if (tv.pseudoThreadId != id)
				_ = Errors.TargetErrorOccurred($"Thread {id} is no longer active.");

			return tv;
		}

		/// <summary>
		/// As <see cref="Live"/>, but additionally rejects a caller on another real thread. Thread stacks are per
		/// real thread, so changing one from outside would corrupt state its owner is using unlocked.
		/// </summary>
		private ThreadVariables Mutable()
		{
			var target = Live();

			if (System.Threading.Thread.CurrentThread.ManagedThreadId != target.threadId)
				_ = Errors.TargetErrorOccurred($"Thread {id} belongs to another real thread and cannot be modified from this one.");

			return target;
		}
	}
}
