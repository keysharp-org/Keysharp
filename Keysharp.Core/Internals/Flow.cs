using Keysharp.Builtins;
using Timer1 = System.Timers.Timer;

namespace Keysharp.Internals
{
	internal static class Flow
	{
		[ThreadStatic]
		internal static int noUiPumpScore;

		private sealed class DialogInterruptibilityScope : IDisposable
		{
			private readonly ThreadVariables threadVariables;
			private readonly bool previousAllowThreadToBeInterrupted;
			private bool disposed;

			internal DialogInterruptibilityScope(ThreadVariables threadVariables)
			{
				this.threadVariables = threadVariables;
				previousAllowThreadToBeInterrupted = threadVariables.allowThreadToBeInterrupted;
			}

			public void Dispose()
			{
				if (disposed)
					return;

				threadVariables.allowThreadToBeInterrupted = previousAllowThreadToBeInterrupted;
				disposed = true;
			}
		}

		private sealed class NoOpScope : IDisposable
		{
			internal static readonly NoOpScope Instance = new();

			public void Dispose()
			{
			}
		}

		internal static bool TryGetException<TException>(Exception ex, out TException found)
			where TException : Exception
		{
			found = null;

			if (ex == null)
				return false;

			if (ex is TException matched)
			{
				found = matched;
				return true;
			}

			if (ex is AggregateException agg)
			{
				foreach (var inner in agg.InnerExceptions)
				{
					if (TryGetException(inner, out found))
						return true;
				}
			}

			return ex.InnerException != null && TryGetException(ex.InnerException, out found);
		}

		internal static IDisposable BeginDialogInterruptibilityScope()
		{
			var script = Script.TheScript;

			if (script == null || Volatile.Read(ref script.totalExistingThreads) == 0)
				return NoOpScope.Instance;

			var tv = script.Threads.CurrentThread;

			if (tv == null || tv.allowThreadToBeInterrupted)
				return NoOpScope.Instance;

			var scope = new DialogInterruptibilityScope(tv);
			tv.allowThreadToBeInterrupted = true;
			return scope;
		}

		// As in AHK, the rare abnormal endings share the one word "Close" (lib/vars.cpp GetExitReasonString).
		internal static string ExitReasonName(Keysharp.Builtins.Flow.ExitReasons reason) => reason switch
		{
			Keysharp.Builtins.Flow.ExitReasons.Critical => "Close",
			Keysharp.Builtins.Flow.ExitReasons.Destroy => "Close",
			Keysharp.Builtins.Flow.ExitReasons.None => "None",
			Keysharp.Builtins.Flow.ExitReasons.Error => "Error",
			Keysharp.Builtins.Flow.ExitReasons.Logoff => "Logoff",
			Keysharp.Builtins.Flow.ExitReasons.Shutdown => "Shutdown",
			Keysharp.Builtins.Flow.ExitReasons.Close => "Close",
			Keysharp.Builtins.Flow.ExitReasons.Menu => "Menu",
			Keysharp.Builtins.Flow.ExitReasons.Exit => "Exit",
			Keysharp.Builtins.Flow.ExitReasons.Reload => "Reload",
			Keysharp.Builtins.Flow.ExitReasons.Single => "Single",
			_ => (string)Errors.ErrorOccurred($"Unknown exit reason \"{reason}\". Expected Critical, Destroy, None, Error, Logoff, Shutdown, Close, Menu, Exit, Reload or Single.", DefaultErrorString)
		};

		internal static bool ExitAppInternal(Script script, Keysharp.Builtins.Flow.ExitReasons exitReason, object exitCode = null, bool useThrow = true)
		{
			if (script == null || script.IsDisposed)
				return false;

			var fd = script.FlowData;

			if (script.hasExited)
				return false;

			var reasonName = ExitReasonName(exitReason);

			if (reasonName.Length == 0)
				return false;

			_ = exitCode.TryCoerceInt(out var ec);
			var threads = script.Threads;
			var allowInterruptionPrev = threads.allowInterruption;
			threads.allowInterruption = false;

			// Invoke OnExit callbacks — UNLESS a nested exit is requested while they are already running (a callback
			// that errors or calls ExitApp/Reload): then skip straight to termination so we don't re-run them and loop
			// forever. InvokeExitHandlers (not InvokeEventHandlers) launches each as an EMERGENCY (like a synchronous
			// window-message response) so it starts despite the allowInterruption=false just set above, and honours a
			// non-zero (veto) return without force-exiting.
			object result = null;

			if (!fd.exitHandlersRunning)
			{
				fd.exitHandlersRunning = true;

				try
				{
					// App.ExitReason remains unavailable until every callback accepts the proposed exit.
					result = script.onExitHandlers.InvokeExitHandlers(reasonName, ec);
				}
				finally
				{
					fd.exitHandlersRunning = false;
				}
			}

			// If an OnExit handler requested a nested exit (it called ExitApp/Reload, or errored into one), that nested
			// ExitAppInternal already ran the ENTIRE teardown below and set hasExited before its
			// UserRequestedExitException was swallowed back in the handler-invocation path above. Re-running the teardown
			// here would fire every __Delete/Dispose a second time, so restore interruption state and bail. On a normal
			// (non-nested) exit hasExited is still false at this point, so teardown proceeds as before.
			if (script.hasExited)
			{
				threads.allowInterruption = allowInterruptionPrev;
				return false;
			}

			_ = result.TryCoerceLong(out var resultCode);

			if (exitReason >= Keysharp.Builtins.Flow.ExitReasons.None && resultCode != 0L)
			{
				threads.allowInterruption = allowInterruptionPrev;
				return true;
			}

			// Only an exit that is certain closes the dialogs of other threads.
			Dialogs.CloseDialogs(script);
			ToolTips.DestroyAll(script);

			// The exit is certain from here: every callback has had its chance to cancel it and none did. Publishing
			// the reason and arming the exit code at this one point is what lets Ks.App.ExitReason mean "the script
			// is going down and nothing can stop it" — the guard a __Delete, a timer or a library needs — and lets
			// Ks.App.ExitCode be read back by the teardown sweep below. Neither may move earlier: before the veto
			// check they would publish an exit that was then cancelled.
			fd.exitReason = exitReason;
			Environment.ExitCode = ec;
			script.onExitHandlers.Clear();
			script.SuppressErrorOccurredDialog = true;

			ReleaseVariables(script);

			// A blocking full collection costs in proportion to the whole heap, so pay it only when something can be found,
			// and again while a __Delete leaves more unreferenced.
			for (var pass = 0; pass < 4 && script.DestructorPump.HasRegistrations; pass++)
			{
				GC.Collect();

				if (!script.DestructorPump.RunPendingDestructors(exiting: true))
					break;
			}

			script.hasExited = true;
			script.WakeAllEventSchedulers();
			threads.allowInterruption = allowInterruptionPrev;
			HotkeyDefinition.AllDestruct(script);
			StopMainTimer(script);

			if (script.KeyboardData.blockInput)
				_ = Keysharp.Builtins.Keyboard.ScriptBlockInput(ToggleValueType.Off);

			if (script.KeyboardData.blockMouseMove)
				_ = Keysharp.Builtins.Keyboard.ScriptBlockInput(ToggleValueType.MouseMoveOff);

			script.FlowData.timers.Clear();

			Gui.DestroyAll(script);
			KeysharpFile.FlushAll(script);
			script.Dispose();

#if !WINDOWS
			script.InvokeOnUIThread(() => Eto.Forms.Application.Instance?.Quit());
#endif

			if (useThrow)
				throw new Keysharp.Builtins.Flow.UserRequestedExitException();

			return false;
		}

		// As in AutoHotkey, the script's global and static variables holding an object are released, leaving them unset,
		// and what that leaves unreferenced, such as a File in an Array, is given its __Delete and freed by the collections
		// which follow, in no particular order. Any other value stays for a __Delete to read. A class object lives on in the
		// script's registry, so a class's own __Delete is called here. A method of its own, as a frame which read the values
		// can keep them reachable until it returns.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void ReleaseVariables(Script script)
		{
			foreach (var t in Reflections.GetNestedTypes([script.ProgramType]).OrderBy(Reflections.GetInheritanceDepth))
			{
				// [PublicHiddenFromUser] fields are not part of the script's variable space, and a read-only one holds a
				// function, which no variable does.
				foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public))
					if (!f.IsInitOnly && !f.IsLiteral && !f.IsDefined(typeof(PublicHiddenFromUser), false) && f.GetValue(null) is Any)
						f.SetValue(null, null);

				if (script.Vars.Statics.IsInitialized(t) && script.Vars.Statics.TryGetValue(t, out Class cls) && cls.HasOwnPropInternal("__Delete"))
				{
					cls.HasFinalizer = false;

					try
					{
						_ = Script.InvokeMeta(cls, "__Delete");
					}
					catch
					{
					}
				}
			}
		}

		internal static void SetMainTimer()
		{
			var script = Script.TheScript;
			var mainTimer = script.FlowData.mainTimer;

			if (mainTimer == null)
			{
				// As AHK's POLL_JOYSTICK_IF_NEEDED on each tick of its main timer: joystick hotkeys fire from polling,
				// which runs on the UI thread, one poll at a time.
				var pollQueued = 0;
				mainTimer = new Timer1(10);
				mainTimer.Elapsed += (o, e) =>
				{
					if (script.HotkeyData.joyHotkeyCount != 0 && Interlocked.Exchange(ref pollQueued, 1) == 0)
						script.PostToUIThread(() =>
						{
							Volatile.Write(ref pollQueued, 0);
							Keysharp.Internals.Input.Joystick.Joystick.PollJoysticks();
						});
				};
				script.FlowData.mainTimer = mainTimer;
				mainTimer.Start();
			}
		}

		internal static void Sleep(int delay = -1)
		{
			var script = Script.TheScript;

			if (delay == 0)
			{
				var tc = Environment.TickCount;
				TryDoEvents();
				if (Environment.TickCount - tc == 0)
					System.Threading.Thread.Sleep(0);
			}
			else if (delay == -1)
			{
				TryDoEvents();
			}
			else if (delay == -2)
				WaitUntil(() => script.hasExited || script.input == null || !script.input.InProgress());
			else if (delay > 0)
				WaitUntil(() => script.hasExited, delay);
		}

		internal static void SleepWithoutInterruption(int duration = -1)
		{
			// Saved and restored, as AHK's g_AllowInterruption_prev, so a caller already uninterruptible stays so, and
			// restored even when an exit request ends the sleep.
			var threads = Script.TheScript.Threads;
			var allowInterruptionPrev = threads.allowInterruption;
			threads.allowInterruption = false;

			try
			{
				Sleep(duration);
			}
			finally
			{
				threads.allowInterruption = allowInterruptionPrev;
			}
		}

		/// <summary>
		/// Waits until <paramref name="condition"/> holds, testing it only when this thread is woken: whatever makes it
		/// true has to announce that with a message, scheduled work, or a scheduler's <c>Wake</c>. Use the overload with a
		/// polling interval for conditions with no notification. A thread that owns no pump re-tests every
		/// <see cref="Script.SLEEP_INTERVAL"/>. False when <paramref name="timeoutMs"/> runs out; negative waits forever.
		/// </summary>
		internal static bool WaitUntil(Func<bool> condition, int timeoutMs = -1) => WaitUntil(condition, timeoutMs, 0);

		/// <summary>
		/// Waits until <paramref name="condition"/> holds, testing it every <paramref name="intervalMs"/> as AutoHotkey's
		/// waits do: for a condition nothing announces, such as a window search or a key state. A thread that runs
		/// script events pumps them meanwhile and otherwise blocks; any other, such as a hook thread, sleeps. False when
		/// <paramref name="timeoutMs"/> runs out; negative waits forever.
		/// </summary>
		internal static bool WaitUntil(Func<bool> condition, int timeoutMs, int intervalMs)
		{
			var script = Script.TheScript;
			// Script.EventScheduler may resolve to another thread's scheduler, which this thread cannot pump.
			var scheduler = script?.CurrentSchedulerIfCreated;
			var pump = script != null && (script.IsOnMainThread || scheduler != null);
			var start = Environment.TickCount64;

			for (var nextTest = start; ;)
			{
				var now = Environment.TickCount64;

				if (now >= nextTest)
				{
					if (condition())
						return true;

					nextTest = now + intervalMs;
					// Window and accessibility queries can consume the remaining timeout themselves.
					now = Environment.TickCount64;
				}

				var remaining = timeoutMs < 0 ? long.MaxValue : start + timeoutMs - now;

				if (remaining <= 0)
					return false;

				if (pump)
				{
					var wait = intervalMs > 0 ? Math.Min(remaining, Math.Max(0, nextTest - now)) : remaining;
					TryDoEvents(scheduler, waitMs: wait == long.MaxValue ? Timeout.Infinite : (int)wait);
				}
				else
				{
					// Nothing can wake a thread without a pump, so the woken form falls back to the same slices as TryDoEvents.
					var slice = intervalMs > 0 ? nextTest - now : Script.SLEEP_INTERVAL;
					System.Threading.Thread.Sleep((int)Math.Clamp(Math.Min(slice, remaining), 1, int.MaxValue));
				}
			}
		}

		internal static void StopMainTimer(Script script)
		{
			var mainTimer = script.FlowData.mainTimer;

			if (mainTimer != null)
			{
				mainTimer.Stop();
				mainTimer.Dispose();
				script.FlowData.mainTimer = null;
			}
		}

		internal static bool TryCatch(Action action)
		{
			try
			{
				action();
				return true;
			}
			catch (Exception mainex) when (CallStack.RememberAndCatch(mainex))
			{
				return Errors.ReportUncaught(mainex);
			}
		}

		// Pumps this thread's scheduler and, on the main thread, the native UI loop. waitMs 0 pumps without waiting,
		// Timeout.Infinite blocks until woken and a positive value caps the block; whatever a caller's loop waits for has
		// to wake it with a message, scheduled work, or the scheduler's Wake. Blocked queued work is retried on a bounded
		// cadence, and a thread that owns no pump sleeps in slices of at most SLEEP_INTERVAL instead of blocking.
		// An explicit scheduler retains its owning Script, even after Script.TheScript changes.
		// pumpUi:false suppresses native dispatch while retaining scheduler and exit handling.
		internal static void TryDoEvents(ScriptEventScheduler scheduler = null, bool propagateExit = true, int waitMs = 0, bool pumpUi = true)
		{
			var script = scheduler?.Owner ?? Script.TheScript;
			var currentThread = script.Threads.CurrentThread;
			// Stamped before the pump as well: a handler that a dispatched message runs inline (a COM event, a subclassed
			// window procedure) starts a fresh interval, as AHK's MsgSleep resets mLastPeekTime on taking a message,
			// instead of finding a check overdue on its first line and pumping again from inside this pump.
			currentThread.lastPeekTick = Environment.TickCount;

			try
			{
				// A disposed script (ExitApp disposes before the unwind reaches this pump) has no schedulers left;
				// resolving one would throw and mask the exit propagation below.
				// Each pumped message may run a handler that pumps in turn, so without stack for another level the
				// messages wait for this handler to return.
				if (!script.IsDisposed && System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
				{
					scheduler ??= script.EventScheduler;

					// The uninterruptible startup window ends without a wake. Retry blocked work at its deadline
					// or the worker loop's peek interval, whichever comes first.
					if (waitMs != 0 && scheduler.HasBlockedQueuedWork)
					{
						var peek = ThreadVariables.DefaultUninterruptiblePeekFrequency;

						if (!currentThread.allowThreadToBeInterrupted && currentThread.UninterruptibleDuration > 0)
						{
							var untilInterruptible = currentThread.threadStartTick + currentThread.UninterruptibleDuration - Environment.TickCount64;

							if (untilInterruptible > 0)
								peek = Math.Min(untilInterruptible, peek);
						}

						waitMs = CapWait(waitMs, peek);
					}
					else if (waitMs != 0 && scheduler.OwnsCurrentThread && scheduler.HasQueuedEvents())
						// An outer pump may have consumed the wake before a callback enters a nested wait.
						waitMs = 0;

					if (pumpUi && noUiPumpScore == 0 && script.IsOnMainThread)
					{
#if WINDOWS
						// The signal also covers hosts whose synchronization context does not post native messages.
						if (waitMs != 0 && scheduler.OwnsCurrentThread)
						{
							scheduler.WaitForMessagesOrPumpSignal(waitMs);
							waitMs = 0;
						}

						Application.DoEvents();
#else
						if (Script.EtoApplication is { } app)
						{
							var nativeWait = 0;

							// A post that lands in the native loop lets one wait see messages and scheduler wakes alike. A
							// foreign host context has no wake the native loop sees, so both are serviced in bounded slices.
							if (script.UIThreadContext is null or EtoSynchronizationContext)
								nativeWait = waitMs;
							else
								waitMs = CapWait(waitMs, Script.SLEEP_INTERVAL);

							app.RunIteration(nativeWait);

							// Zeroed only once the wait was served, so an iteration that throws still falls back to bounded slices.
							if (nativeWait != 0)
								waitMs = 0;
						}
#endif
					}

					if (waitMs != 0 && scheduler.OwnsCurrentThread)
					{
						scheduler.WaitForPumpSignal(waitMs);
						waitMs = 0;
					}

					scheduler.PumpThreadQueuedEventsCore();
				}
			}
			catch (Exception ex) when (!propagateExit || !TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out _))
			{
			}
			finally
			{
				currentThread = script.Threads.CurrentThread;
				currentThread.lastPeekTick = Environment.TickCount;
			}

			if (propagateExit)
			{
				if (script.hasExited)
					throw new Keysharp.Builtins.Flow.UserRequestedExitException();

				script.Threads.ThrowIfExitRequested(currentThread);
			}

			// If no pump could wait, give up the time in bounded slices.
			if (waitMs != 0)
				System.Threading.Thread.Sleep(CapWait(waitMs, Script.SLEEP_INTERVAL));

			static int CapWait(int waitMs, long cap) => waitMs < 0 ? (int)cap : (int)Math.Min(waitMs, cap);
		}

		/// <summary>
		/// Waits for task completion without observing its result or exception. Pumps the calling thread's scheduler
		/// so its continuations can run; like Sleep, the wait allows new pseudo-threads. Threads without a scheduler
		/// block normally. Unlike this script-facing wait, TaskExtensions only pumps the UI thread.
		/// </summary>
		/// <param name="task">The task to wait for.</param>
		/// <param name="timeoutMs">Milliseconds to wait, or negative to wait indefinitely.</param>
		/// <returns>True if the task finished, false if the timeout elapsed first.</returns>
		internal static bool WaitForCompletion(Task task, int timeoutMs)
		{
			if (task == null || task.IsCompleted)
				return true;

			// Script.EventScheduler can resolve to another thread's scheduler, which this thread cannot pump.
			var scheduler = Script.TheScript?.CurrentSchedulerIfCreated;
			using var wakeCancellation = new CancellationTokenSource();
			var wakeContinuation = task.ContinueWith(static (_, s) => ((ScriptEventScheduler)s)?.Wake(), scheduler, wakeCancellation.Token,
				TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

			try
			{
				if (scheduler == null)
					return wakeContinuation.Wait(timeoutMs < 0 ? Timeout.Infinite : timeoutMs) || task.IsCompleted;

				var deadline = timeoutMs < 0 ? long.MaxValue : Environment.TickCount64 + timeoutMs;

				do
				{
					// Pumps once even for a zero timeout, so the queue is still serviced.
					var remaining = timeoutMs < 0 ? Timeout.Infinite : (int)Math.Max(0, deadline - Environment.TickCount64);
					TryDoEvents(scheduler, propagateExit: true, waitMs: remaining);
				} while (!task.IsCompleted && Environment.TickCount64 < deadline);

				return task.IsCompleted;
			}
			finally
			{
				// A timed-out or interrupted wait must not leave its scheduler attached to a long-lived task.
				wakeCancellation.Cancel();
			}
		}

		/// <summary>
		/// Suspends <paramref name="tv"/> while its paused flag is set, pumping so that a hotkey or posted work can
		/// clear it, as AHK's MsgWaitUnpause does; the flag itself holds off timers. Shared by <c>Pause()</c>, which
		/// sets the flag on the calling thread, and by the resume point in <c>Threads.EndThread</c>, which is where a
		/// thread paused by <c>Pause(1)</c>/<c>A_IsPaused</c>/<c>thr.Paused</c> while it was interrupted observes it.
		/// <para>
		/// It never throws. This runs inside <c>EndThread</c>, which is reached during exception unwinds, so
		/// propagating an exit from here could replace an in-flight exception; the predicate watches for shutdown
		/// instead and lets the thread go.</para>
		/// </summary>
		internal static void WaitWhilePaused(ThreadVariables tv)
		{
			// A thread that ends inside the wait returns to it, rather than starting another wait for the same pause.
			if (tv == null || !tv.IsPaused || tv.waitingWhilePaused)
				return;

			var script = Script.TheScript;

			if (script == null)
				return;

			tv.waitingWhilePaused = true;

			try
			{
				while (tv.IsPaused && tv.requestedExitCode == null && !script.hasExited && !script.IsDisposed)
					TryDoEvents(propagateExit: false, waitMs: Timeout.Infinite);
			}
			finally
			{
				tv.waitingWhilePaused = false;
			}
		}
	}
}
