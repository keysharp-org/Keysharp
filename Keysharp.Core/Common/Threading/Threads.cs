namespace Keysharp.Core.Common.Threading
{
	public class Threads
	{
		/// <summary>
		/// Each thread has its own TVM. This means the main UI thread gets one, and the hook thread gets one.
		/// This allows the hook thread to run #HotIf evaluations separately without interfering with the main thread.
		/// It could conceivably lead to more threads being in existence than the user allowed, for a very brief moment.
		/// This shouldn't be a problem though.
		/// Note that we use ThreadLocal<T> here because it allows initialization for each thread, whereas
		/// [ThreadStatic] doesn't.
		/// Always add 1 to MaxThreadsTotal because the a dummy entry will always be added in the constructor.
		/// </summary>
		private readonly ThreadVariableManager tvm = new((int)Script.TheScript.MaxThreadsTotal + 1);

		internal ThreadVariables CurrentThread;

		internal ThreadVariables UnderlyingThread => tvm.threadVars.TryPeekSecond();

		int _timersPaused = 0;

		public Threads()
		{
			_ = PushThreadVariables(0, true, false, true);//Ensure there is always one thread in existence for reference purposes, but do not increment the actual thread counter.
		}

		public (bool, ThreadVariables) BeginThread(bool onlyIfEmpty = false)
		{
			var skip = Script.TheScript.FlowData.allowInterruption == false;//This will be false when exiting the program.
			return PushThreadVariables(0, skip, false, onlyIfEmpty, true);
		}

		public object EndThread((bool, ThreadVariables) btv, bool checkThread = false)
		{
			var script = Script.TheScript;
			var pushed = btv.Item1;
			var max = script.MaxThreadsTotal;

			if (pushed)
				btv.Item2.task = false;

			PopThreadVariables(pushed, checkThread);
			var newCount = Interlocked.Decrement(ref script.totalExistingThreads);

			// Resume timers since we have a free thread
			if (newCount == (max - 1))
			{
				_ = ResumeAllTimers();
			}

			return null;
		}

		public (bool, ThreadVariables) PushThreadVariables(long priority, bool skipUninterruptible,
				bool isCritical = false, bool onlyIfEmpty = false, bool inc = false)
		{
			var script = Script.TheScript;
			var max = script.MaxThreadsTotal;

			// Fast path: mimic what tvm would have done
			if (onlyIfEmpty && tvm.threadVars.Index != 0)
			{
				return (false, tvm.threadVars.TryPeek());
			}

			// If we should count this thread, bump the counter and maybe pause timers
			bool didPause = false;

			if (inc)
			{
				var newCount = Interlocked.Increment(ref script.totalExistingThreads);

				if (newCount == max)
				{
					// Pause all timers if out of available threads. This is done here instead of BeginThread
					// because not all threads are started with it (eg timer threads)
					didPause = PauseAllTimers();
				}
			}

			var (success, tv) = tvm.PushThreadVariables(priority, skipUninterruptible, isCritical, onlyIfEmpty);

			if (!success)
			{
				// Roll back counter (if we incremented it) and undo pause
				if (inc)
				{
					var oldCount = Interlocked.Decrement(ref script.totalExistingThreads);

					if (didPause && oldCount == max - 1)
						_ = ResumeAllTimers();
				}

				return (success, tv);
			}

			CurrentThread = tv;

			//We successfully pushed—and if inc == true, we’ve already counted it
			tv.task = true;
			return (true, tv);
		}

		internal bool PauseAllTimers()
		{
			if (Interlocked.Exchange(ref _timersPaused, 1) == 0)
			{
				foreach (var timer in TheScript.FlowData.timers.Values)
				{
					timer.Pause();
				}

				return true;
			}

			return false;
		}

		internal bool ResumeAllTimers()
		{
			if (Interlocked.Exchange(ref _timersPaused, 0) == 1)
			{
				foreach (var timer in TheScript.FlowData.timers.Values)
				{
					timer.Resume();
				}

				return true;
			}

			return false;
		}

		internal bool AnyThreadsAvailable()
		{
			var script = Script.TheScript;
			return Volatile.Read(ref script.totalExistingThreads) < script.MaxThreadsTotal;
		}

		internal ThreadVariables GetThreadVariables()
		{
			return tvm.GetThreadVariables();
		}

		internal bool IsInterruptible()
		{
			var script = Script.TheScript;

			if (!script.FlowData.allowInterruption)
				return false;

			if (Volatile.Read(ref script.totalExistingThreads) == 0)//Before _ks_UserMainCode() starts to run.1
				return true;

			var tv = CurrentThread;

			if (!tv.isCritical//Added this whereas AHK doesn't check it. We should never make a critical thread interruptible.
					&& !tv.allowThreadToBeInterrupted // Those who check whether g->AllowThreadToBeInterrupted==false should then check whether it should be made true.
					&& tv.UninterruptibleDuration > -1 // Must take precedence over the below.  g_script.mUninterruptibleTime is not checked because it's supposed to go into effect during thread creation, not after the thread is running and has possibly changed the timeout via 'Thread "Interrupt"'.
					&& (DateTime.UtcNow - tv.threadStartTime).TotalMilliseconds >= tv.UninterruptibleDuration// See big comment section above.
					&& !script.FlowData.callingCritical // In case of "Critical" on the first line.  See v2.0 comment above.
			   )
			{
				// Once the thread becomes interruptible by any means, g->ThreadStartTime/UninterruptibleDuration
				// can never matter anymore because only Critical (never "Thread Interrupt") can turn off the
				// interruptibility again, and it resets g->UninterruptibleDuration.
				tv.allowThreadToBeInterrupted = true; // Avoids issues with 49.7 day limit of 32-bit TickCount, and also helps performance future callers of this function (they can skip most of the checking above).

				if (!tv.isCritical)
					tv.configData.peekFrequency = ThreadVariables.DefaultPeekFrequency;
			}

			return tv.allowThreadToBeInterrupted;
		}

		internal void LaunchThreadInMain(Action act, long priority = 0, bool skipUninterruptible = false,
							 bool isCritical = false)//Determine later the optimal threading model.//TODO
		{
			try
			{
				var existingTv = GetThreadVariables();
				existingTv.WaitForCriticalToFinish();//Cannot launch a new task while a critical one is running.
				Script.TheScript.mainWindow.CheckedBeginInvoke(() =>
				{
					var threads = Script.TheScript.Threads;
					var btv = threads.PushThreadVariables(priority, skipUninterruptible, isCritical, false, true);//Always start each thread with one entry.

					if (btv.Item1)
					{
						_ = Flow.TryCatch(() =>
						{
							act();
							_ = threads.EndThread(btv);
						}, true, btv);//Pop on exception because EndThread() above won't be called.
					}
				}, true, false);
			}
			catch (Exception ex)
			{
				if (ex.InnerException != null)
					throw ex.InnerException;
				else
					throw;//Do not pass ex because it will reset the stack information.
			}
		}

		internal void LaunchInThread(long priority, bool skipUninterruptible,
									 bool isCritical, object func, object[] o, bool tryCatch)//Determine later the optimal threading model.//TODO
		{
			try
			{
				var existingTv = GetThreadVariables();
				existingTv.WaitForCriticalToFinish();//Cannot launch a new task while a critical one is running.
				Script.TheScript.mainWindow.CheckedBeginInvoke(() =>
				{
					object ret = null;
					var threads = Script.TheScript.Threads;
					var btv = threads.PushThreadVariables(priority, skipUninterruptible, isCritical, false, true);//Always start each thread with one entry.

					if (btv.Item1)
					{
						if (tryCatch)
						{
							_ = Flow.TryCatch(() =>
							{
								if (func is VariadicFunction vf)
									ret = vf(o);
								else if (func is IFuncObj ifo)
									ret = ifo.Call(o);
								else
									ret = "";

								_ = threads.EndThread(btv);
							}, true, btv);//Pop on exception because EndThread() above won't be called.
						}
						else
						{
							//Because this is not used in a try/catch block, we assume the function internally handles exceptions.
							//So even if an exception occurs, we will manually pop the thread variables with EndThread().
							if (func is VariadicFunction vf)
								ret = vf(o);
							else if (func is IFuncObj ifo)
								ret = ifo.Call(o);
							else
								ret = "";

							_ = threads.EndThread(btv);
						}
					}
				}, true, false);
			}
			catch (Exception ex)
			{
				if (ex.InnerException != null)
					throw ex.InnerException;
				else
					throw;//Do not pass ex because it will reset the stack information.
			}
		}

		internal void PopThreadVariables(bool pushed, bool checkThread = false)
		{
			tvm.PopThreadVariables(pushed, checkThread);
			CurrentThread = GetThreadVariables();
		}
	}
}