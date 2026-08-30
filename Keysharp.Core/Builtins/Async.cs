namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Waits for <paramref name="Value"/> to finish and returns what it produced.
		/// <para>
		/// This is Keysharp's <c>await</c>. It does not suspend the script thread the way C#'s <c>await</c>
		/// suspends a method — a Keysharp pseudo-thread runs to completion on its own frame — so it blocks the
		/// calling thread and pumps everything else, exactly as <c>Sleep</c>, <c>WinWait</c> and
		/// <c>RealThread.Wait</c> do. Timers, hotkeys and the GUI stay alive throughout.</para>
		/// <para>
		/// Because it pumps, it is an interruption point: another pseudo-thread can start while it waits, as it
		/// can inside <c>Sleep</c>. That matters most around <c>Ks.Lock</c>, whose ownership is per real thread
		/// and reentrant — a timer that acquires and releases the same lock during an <c>Await</c> releases the
		/// waiting thread's acquisition. Use <c>Critical</c> to hold a section closed across a wait.</para>
		/// </summary>
		/// <param name="Value">A <c>Task</c>, a CLR task reached through <c>Ks.Clr</c>, or a script object whose
		/// zero-argument <c>__Await()</c> method returns one. A <c>RealThread</c> is deliberately not accepted:
		/// it has two completions, and <c>worker.Task</c> (the entry function) and <c>worker.Terminated</c> (the
		/// OS thread) must be named apart.</param>
		/// <param name="Timeout">Milliseconds to wait. Default: wait indefinitely. Timing out does not cancel the work.</param>
		/// <returns>The value the work produced, or an empty string if it produced none.</returns>
		/// <exception cref="Error">The work was canceled.</exception>
		/// <exception cref="TimeoutError">The timeout elapsed before the work finished.</exception>
		/// <exception cref="TypeError"><paramref name="Value"/> is not something that finishes later.</exception>
		public static object Await(object Value, object Timeout = null)
		{
			// Not lenient: returning a non-task unchanged would make `Await(MakeDocx)` -- the missing-parens
			// typo -- quietly hand back the function object.
			var task = KeysharpTask.FromAwaitable(Value);

			if (task == null)
				return Errors.TypeErrorOccurred(Value, typeof(KeysharpTask));

			// Waiting on the entry task of the worker this call is running inside can never finish: that task is
			// settled by the very body doing the waiting.
			if (KeysharpTask.IsCurrentRealThreadTask(task))
				return Errors.TargetErrorOccurred("A real thread cannot wait on its own entry function.");

			// A Post back to this same real thread, awaited while this thread is uninterruptible, is the other
			// wait that can never finish: Await pumps, but the work needs a thread launch and Critical refuses it.
			if ((Value as KeysharpTask ?? KeysharpTask.Wrap(task)).IsUnservableSelfPost())
				return Errors.TargetErrorOccurred(
						"Cannot await work posted to this same real thread while the current thread is uninterruptible.");

			if (!Keysharp.Internals.Flow.WaitForCompletion(task, Timeout.Ai(-1)))
				return Errors.TimeoutErrorOccurred("Await timed out.");

			if (task.IsCanceled)
				return Errors.ErrorOccurred("The awaited work was canceled.");

			if (task.IsFaulted)
			{
				var error = (Value as KeysharpTask ?? KeysharpTask.Wrap(task)).GetMappedError();
				return Errors.ErrorOccurred(error) ? throw error : DefaultObject;
			}

			return KeysharpTask.ReadResult(task);
		}
	}
}
