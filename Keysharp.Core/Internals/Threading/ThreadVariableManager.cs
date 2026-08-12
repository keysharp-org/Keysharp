using Keysharp.Builtins;
namespace Keysharp.Internals.Threading
{
	internal class ThreadVariableManager
	{
		/// <summary>
		/// Since AHK doesn't actually use threads, and instead behaves more as a stack-based system, using the built-in [ThreadStatic] attribute for members
		/// will not work.
		/// So we implement our own, but take a few steps for efficiency. We don't want to have to allocate an object every time a pseudo-thread is launched
		/// in response to a button click, hotkey, timer event, etc...
		/// So we create a SlimStack to be used as an object pool which we push and pop each time a thread starts and finishes.
		/// </summary>
		internal SlimStack<ThreadVariables> threadVars;
		internal int PseudoThreadCount => Math.Max(0, threadVars.Index - 1);

		internal ThreadVariableManager(int size)
		{
			threadVars = new (size, () => new ThreadVariables());
		}

		internal ThreadVariables TryGetPseudoThread(int index)
			// #MaxThreads is capped at 255, so every valid index fits in the encoded 16-bit field.
			=> (uint)index <= ushort.MaxValue ? threadVars.TryGet(index + 1) : null;

		/// <summary>
		/// The active pseudo-threads of this real thread, oldest first. Skips the permanent context holder at slot 0
		/// and any slot whose ID has since been cleared, so every entry describes a live pseudo-thread at the moment
		/// it was read. Used by <c>RealThread.Threads</c>, which is a diagnostic/control surface, never a launch path.
		/// </summary>
		internal List<ThreadVariables> SnapshotPseudoThreads()
		{
			var count = PseudoThreadCount;
			var list = new List<ThreadVariables>(count);

			for (var i = 0; i < count; i++)
			{
				var tv = TryGetPseudoThread(i);

				if (tv != null && tv.pseudoThreadId != 0L)
					list.Add(tv);
			}

			return list;
		}

		internal void PopThreadVariables(ThreadVariables threadVarsToPop, bool checkThread = true)
		{
			if (threadVarsToPop == null)
				return;

			var ctid = Thread.CurrentThread.ManagedThreadId;

			//Do not check threadVars for null, because it should have always been created with a call to PushThreadVariables() before this.
			if (ctid != Script.TheScript.ManagedMainThreadID
					|| threadVars.Index > 0)//Never pop the last object on the main thread.
			{
				//Diagnostics.Debug.WriteLine($"About to pop with {threadVars.Index} existing threads");
				if (threadVars.TryPop(out var tv))
				{
					if (!ReferenceEquals(tv, threadVarsToPop))
					{
						_ = Errors.ErrorOccurred("Severe threading error: Tried to pop a ThreadVariables object that was not at the top of the stack. This should never happen.", null, Keyword_ExitApp);
						return;
					}

					if (checkThread && ctid != tv.threadId)
					{
						_ = Errors.ErrorOccurred($"Severe threading error: ThreadVariables.threadId {tv.threadId} did not match the current thread id {ctid}. This should never happen.", null, Keyword_ExitApp);
						return;
					}
				}
			}
		}

		internal ThreadVariables PushThreadVariables(long priority, bool skipUninterruptible,
				bool isCritical = false, ThreadKind kind = ThreadKind.None)
		{
			var script = Script.TheScript;
			var pushed = threadVars.TryPush(out var tv);

			if (pushed)
			{
				tv.Init();
				tv.threadId = Thread.CurrentThread.ManagedThreadId;
				tv.kind = kind;

				// The first entry is a permanent context holder, not a script pseudo-thread.
				// Exclude it from both the creation sequence and the encoded stack position.
				var pseudoThreadIndex = threadVars.Index - 2;

				if (pseudoThreadIndex >= 0)
				{
					ulong sequence;

					do
						sequence = unchecked((ulong)Interlocked.Increment(ref script.pseudoThreadSequence)) & 0x0000FFFFFFFFFFFFUL;
					while (sequence == 0u); // Keep every valid ID nonzero, including after sequence wraparound.

					tv.pseudoThreadId = unchecked((long)((sequence << 16)
						| (ushort)pseudoThreadIndex));
				}
				tv.priority = priority;

				if (!skipUninterruptible)
				{
					if (!tv.isCritical)
						tv.isCritical = isCritical;

					tv.ApplyUninterruptibleStartupWindow();
				}
			}

#if DEBUG
			else
				_ = Diagnostics.Debug.WriteLine($"Thread stack limit exceeded");

#endif
			return tv;
		}
	}
}
