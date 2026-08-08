using Keysharp.Builtins;
using Timer1 = System.Timers.Timer;

namespace Keysharp.Internals
{
	internal static class Flow
	{
		internal const int IntervalUnspecified = int.MinValue + 303;

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

		internal static bool ExitAppInternal(Keysharp.Builtins.Flow.ExitReasons exitReason, object exitCode = null, bool useThrow = true)
		{
			var script = Script.TheScript;
			var fd = script.FlowData;

			if (script.hasExited)
				return false;

			Dialogs.CloseDialogs();
			Dialogs.CloseToolTips();
			var ec = exitCode.Ai();
			Accessors.A_ExitReason = exitReason.ToString();
			var allowInterruptionPrev = fd.allowInterruption;
			fd.allowInterruption = false;

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
					result = script.onExitHandlers.InvokeExitHandlers(Accessors.A_ExitReason, ec);
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
				fd.allowInterruption = allowInterruptionPrev;
				return false;
			}

			if (exitReason >= Keysharp.Builtins.Flow.ExitReasons.None && result.Al() != 0L)
			{
				Accessors.A_ExitReason = "";
				fd.allowInterruption = allowInterruptionPrev;
				return true;
			}

			script.onExitHandlers.Clear();
			script.SuppressErrorOccurredDialog = true;

			GC.Collect();
			GC.WaitForPendingFinalizers();
			DestructorPump.RunPendingDestructors();

			foreach (var t in Reflections.GetNestedTypes([script.ProgramType]).OrderBy(Reflections.GetInheritanceDepth))
			{
				// [PublicHiddenFromUser] fields are not part of the script's variable space, so reading them here
				// would force an initializer to run at exit for a field the script could never have touched.
				var fields = t.GetFields(BindingFlags.Static | BindingFlags.Public)
							  .Where(f => f.GetCustomAttribute<PublicHiddenFromUser>() == null);

				foreach (var val in fields.Select(f => f.GetValue(null)))
				{
					if (val is Any kso)
						CallDeleteSilent(kso);
				}

				if (script.Vars.Statics.IsInitialized(t)
					&& script.Vars.Statics.TryGetValue(t, out Class kso2)
					&& kso2.HasOwnPropInternal("__Delete"))
					CallDeleteSilent(kso2);
			}

			script.hasExited = true;
			script.ScheduleAllEventSchedulers();
			fd.allowInterruption = allowInterruptionPrev;
			HotkeyDefinition.AllDestruct();
			StopMainTimer();

			if (script.KeyboardData.blockInput)
				_ = Keysharp.Builtins.Keyboard.ScriptBlockInput(ToggleValueType.Off);

			if (script.KeyboardData.blockMouseMove)
				_ = Keysharp.Builtins.Keyboard.ScriptBlockInput(ToggleValueType.MouseMoveOff);

			script.FlowData.timers.Clear();

			Gui.DestroyAll();
			Environment.ExitCode = ec;
			script.Dispose();

#if !WINDOWS
			Script.InvokeOnUIThread(() => Eto.Forms.Application.Instance?.Quit());
#endif

			if (useThrow)
				throw new Keysharp.Builtins.Flow.UserRequestedExitException();

			return false;

			void CallDeleteSilent(Any kso)
			{
				try
				{
					kso.HasFinalizer = false;
					Script.InvokeMeta(kso, "__Delete");
					if (kso is IDisposable dis)
						dis.Dispose();
				}
				catch
				{
				}
			}
		}

		internal static void SetMainTimer()
		{
			var script = Script.TheScript;
			var mainTimer = script.FlowData.mainTimer;

			if (mainTimer == null)
			{
				mainTimer = new Timer1(10);
				mainTimer.Elapsed += (o, e) => { };
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
				TryDoEvents(true, false);
				if (Environment.TickCount - tc == 0)
					System.Threading.Thread.Sleep(0);
			}
			else if (delay == -1)
			{
				TryDoEvents(true, false);
			}
			else if (delay == -2)
			{
				WaitWithMessagePump(() => !script.hasExited && script.input != null && script.input.InProgress());
			}
			else
			{
				var stopTick = Environment.TickCount64 + delay;
				WaitWithMessagePump(() => !script.hasExited && Environment.TickCount64 < stopTick);
			}
		}

		internal static void SleepWithoutInterruption(int duration = IntervalUnspecified)
		{
			var fd = Script.TheScript.FlowData;
			var allowInterruptionPrev = fd.allowInterruption;   // save/restore (matches AHK's g_AllowInterruption_prev)
			fd.allowInterruption = false;                       // so a caller already uninterruptible (e.g. Critical) stays so
			Sleep(duration);
			fd.allowInterruption = allowInterruptionPrev;
		}

		internal static bool PollUntil(Func<bool> condition, int timeoutMs, int pollIntervalMs)
		{
			var deadline = Environment.TickCount64 + timeoutMs;

			while (true)
			{
				if (condition())
					return true;

				var remainingMs = deadline - Environment.TickCount64;

				if (remainingMs <= 0)
					return false;

				System.Threading.Thread.Sleep((int)Math.Min(pollIntervalMs, remainingMs));
			}
		}

		// Like PollUntil(), but if called on the script's main thread, pumps the UI message loop
		// while waiting instead of blocking it with Thread.Sleep(). This keeps the app responsive
		// while waiting for the user to grant a macOS permission (Accessibility, Input Monitoring,
		// Screen Recording, etc.) in System Settings.
		internal static bool PollUntilWithMessagePump(Func<bool> condition, int timeoutMs, int pollIntervalMs)
		{
			var script = Script.TheScript;

			if (!script.IsOnMainThread)
				return PollUntil(condition, timeoutMs, pollIntervalMs);

			var deadline = Environment.TickCount64 + timeoutMs;

			while (true)
			{
				if (condition())
					return true;

				var remainingMs = deadline - Environment.TickCount64;

				if (remainingMs <= 0)
					return false;

				var start = Environment.TickCount64;

				do
				{
					TryDoEvents();
					System.Threading.Thread.Sleep(10);
				}
				while (!condition() && Environment.TickCount64 - start < pollIntervalMs && Environment.TickCount64 < deadline);
			}
		}

		internal static void StopMainTimer()
		{
			var script = Script.TheScript;
			var mainTimer = script.FlowData.mainTimer;

			if (mainTimer != null)
			{
				mainTimer.Stop();
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
			catch (Exception mainex)
			{
				return HandleCaughtException(mainex);
			}
		}

		internal static bool HandleCaughtException(Exception mainex)
		{
			if (mainex is KeysharpException directKsErr)
				return HandleKeysharpException(directKsErr);

			var ex = mainex.InnerException ?? mainex;

			if (TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(mainex, out _))
				return true;

			if (ex is KeysharpException kserr)
				return HandleKeysharpException(kserr);

			if (!Script.TheScript.SuppressErrorOccurredDialog)
			{
				var dummy = new Error(mainex);
				_ = Errors.ErrorOccurred(dummy, Keywords.Keyword_Exit);
				if (!dummy.Handled)
					ShowHandledErrorDialog(ex, false);
			}

			return false;
		}

		private static bool HandleKeysharpException(KeysharpException kserr)
		{
			var userErr = kserr.UserError;
			if (userErr != null && !userErr.Processed)
				_ = Errors.ErrorOccurred(userErr, Keywords.Keyword_Exit);

			if (userErr != null && !userErr.Handled && !Script.TheScript.SuppressErrorOccurredDialog)
				ShowHandledErrorDialog(kserr, true);
			else if (userErr == null && !Script.TheScript.SuppressErrorOccurredDialog)
				ShowHandledErrorDialog(kserr, true);

			return false;
		}

		private static void ShowHandledErrorDialog(Exception ex, bool keysharpDialog)
		{
			static void ShowDialog(Exception innerEx, bool useKeysharpDialog)
			{
				if (useKeysharpDialog)
					_ = ErrorDialog.Show((KeysharpException)innerEx, false);
				else
					_ = ErrorDialog.Show(innerEx);
			}

			var script = Script.TheScript;
			var scheduler = script.CurrentSchedulerIfCreated ?? script.EventScheduler;
			var executionResult = scheduler.TryExecuteThreadLaunch(0, false, false, _ =>
			{
				ShowDialog(ex, keysharpDialog);
			});

			if (executionResult != ScriptEventExecutionResult.Executed)
				ShowDialog(ex, keysharpDialog);
		}

		internal static void TryDoEvents(bool propagateExit = true, bool yieldTick = true)
			=> TryDoEvents(null, propagateExit, yieldTick);

		// A null scheduler resolves through Script.EventScheduler. Callers which are already executing inside
		// a UI dispatch can suppress the nested native UI pump while retaining scheduler and exit handling.
		internal static void TryDoEvents(ScriptEventScheduler scheduler, bool propagateExit = true, bool yieldTick = true, bool pumpUi = true)
		{
			var start = yieldTick ? Environment.TickCount : default;
			var script = Script.TheScript;
			ThreadVariables currentThread = null;
			scheduler ??= script.EventScheduler;

			try
			{
				if (pumpUi && script.IsOnMainThread)
				{
#if WINDOWS
					Application.DoEvents();
#else
					Application.Instance?.RunIteration();
#endif
				}

				scheduler.PumpThreadQueuedEventsCore();
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

			if (yieldTick && start.Equals(Environment.TickCount))
				System.Threading.Thread.Sleep(1);
		}

		internal static void WaitWithMessagePump(Func<bool> keepWaiting, bool propagateExit = true)
		{
			while (keepWaiting())
				TryDoEvents(propagateExit);
		}
	}
}
