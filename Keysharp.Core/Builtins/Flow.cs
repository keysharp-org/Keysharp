using Keysharp.Runtime;
using static Keysharp.Builtins.Errors;

using Timer1 = System.Timers.Timer;

namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for flow-related functions.
	/// </summary>
	public static class Flow
	{

		/// <summary>
		/// Prevents the current thread from being interrupted by other threads, or enables it to be interrupted.
		/// </summary>
		/// <param name="setting">
		/// If blank or omitted, it defaults to On. Otherwise, specify one of the following:<br/>
		///     On: The current thread is made critical, meaning that it cannot be interrupted by another thread.<br/>
		///     Off: The current thread immediately becomes interruptible, regardless of the settings of Thread Interrupt.<br/>
		///         See Critical Off for details.<br/>
		///     (Numeric): Specify a positive number to turn on Critical but also change the number of milliseconds between checks of the internal message queue.<br/>
		///             See Message Check Interval for details.<br/>
		///         Specifying 0 turns off Critical.<br/>
		///         Specifying -1 turns on Critical but disables message checks.<br/>
		/// </param>
		public static object Critical(object setting = null)
		{
			var script = Script.TheScript;
			script.FlowData.callingCritical = true;
			var tv = script.Threads.CurrentThread;
			var on = setting.IsNullOrEmpty();
			var freq = on ? ThreadVariables.DefaultUninterruptiblePeekFrequency : setting.Al();

			if (!on)
			{
				var b = Options.OnOff(setting.As());

				if (b != null)
				{
					on = b.Value;

					if (on)
						freq = ThreadVariables.DefaultUninterruptiblePeekFrequency;
				}
			}

			var ret = tv.isCritical ? tv.configData.peekFrequency : 0L;
			// v1.0.46: When the current thread is critical, have the script check messages less often to
			// reduce situations where an OnMessage or GUI message must be discarded due to "thread already
			// running".  Using 16 rather than the default of 5 solves reliability problems in a custom-menu-draw
			// script and probably many similar scripts -- even when the system is under load (though 16 might not
			// be enough during an extreme load depending on the exact preemption/timeslice dynamics involved).
			// DON'T GO TOO HIGH because this setting reduces response time for ALL messages, even those that
			// don't launch script threads (especially painting/drawing and other screen-update events).
			// Some hardware has a tickcount granularity of 15 instead of 10, so this covers more variations.
			// v1.0.48: Below supports "Critical 0" as meaning "Off" to improve compatibility with A_IsCritical.
			// In fact, for performance, only the following are no recognized as turning on Critical:
			//     - "On"
			//     - ""
			//     - Integer other than 0.
			// Everything else is considered to be "Off", including "Off", any non-blank string that
			// doesn't start with a non-zero number, and zero itself.
			tv.isCritical = on // i.e. omitted or blank is the same as "On". See comments above.
							|| freq != 0L; // Non-zero integer also turns it on. Relies on short-circuit boolean order.

			if (tv.isCritical) // Critical has been turned on. (For simplicity even if it was already on, the following is done.)
			{
				tv.configData.peekFrequency = freq;
				tv.configData.defaultIsCritical = true;
				tv.allowThreadToBeInterrupted = false;
				// Ensure uninterruptibility never times out.  IsInterruptible() relies on this to avoid the
				// need to check g->ThreadIsCritical, which in turn allows global_maximize_interruptibility()
				// and DialogPrep() to avoid resetting g->ThreadIsCritical, which allows it to reliably be
				// used as the default setting for new threads, even when the auto-execute thread itself
				// (or the idle thread) needs to be interruptible, such as while displaying a dialog->
				// In other words, g->ThreadIsCritical only represents the desired setting as set by the
				// script, and isn't the actual mechanism used to make the thread uninterruptible.
				tv.UninterruptibleDuration = -1;
			}
			else // Critical has been turned off.
			{
				// Since Critical is being turned off, allow thread to be immediately interrupted regardless of
				// any "Thread Interrupt" settings.
				tv.configData.peekFrequency = 5;
				tv.configData.defaultIsCritical = false;
				tv.allowThreadToBeInterrupted = true;
			}

			// The thread's interruptibility has been explicitly set; so the script is now in charge of
			// managing this thread's interruptibility.
			script.FlowData.callingCritical = false;
			script.RecordMessageCheck();
			return ret;
		}

		/// <summary>
		/// Exits the current thread or the entire script if non-persistent.
		/// The exit is achieved by throwing an exception which will be caught in the catch
		/// clause that wraps all threads.
		/// </summary>
		/// <param name="exitCode">The process exit code to apply when the pseudo-thread exits. Defaults to zero.</param>
		/// <returns>Does not return: the current pseudo-thread exits immediately. To terminate a different
		/// pseudo-thread, call <c>Exit</c> on its <c>Thread</c> object (<c>A_Thread.Underlying.Exit()</c>,
		/// <c>A_RealThread.Threads[1].Exit()</c>).</returns>
		public static object Exit(object exitCode = null)
		{
			// Requesting an exit on the current pseudo-thread throws, so this never returns normally unless there is
			// no pseudo-thread to exit at all.
			return Script.TheScript.Threads.RequestExit(exitCode.Ai());
		}

		/// <summary>
		/// Terminates the script unconditionally.
		/// This is equivalent to choosing "Exit" from the script's tray menu or main menu.
		/// </summary>
		/// <param name="exitCode">If omitted, it defaults to 0 (zero is traditionally used to indicate success).<br/>
		/// Otherwise, specify an integer between -2147483648 and 2147483647 that is returned to its caller when the script exits.<br/>
		/// This code is accessible to any program that spawned the script, such as another script (via RunWait) or a batch (.bat) file.</param>
		public static object ExitApp(object exitCode = null)
		{
			var script = Script.TheScript;

			if (!script.hasExited)//This can be called multiple times, so ensure it only runs through once.
			{
				try
				{
					if (script.mainWindow != null)
						Script.InvokeOnUIThread(() => _ = Keysharp.Internals.Flow.ExitAppInternal(ExitReasons.Exit, exitCode, true));
					else
						_ = Keysharp.Internals.Flow.ExitAppInternal(ExitReasons.Exit, exitCode, true);
				}
				catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException(ex, out UserRequestedExitException userExit))
				{
					throw userExit;
				}
				var start = DateTime.UtcNow;

				while (!script.hasExited && (DateTime.UtcNow - start).TotalSeconds < 5)
					_ = Sleep(500);
			}

			return DefaultObject;
		}


		/// <summary>
		/// Registers a function to be called automatically whenever the script exits.
		/// </summary>
		/// <param name="callback">The function to call, which accepts two parameters<br/>
		/// 1: The exit reason (one of the words from the table below).<br/>
		/// 2: The exit code passed to <see cref="Exit"/> or <see cref="ExitApp"/>.<br/>
		/// </param>
		/// <param name="addRemove">If omitted, it defaults to 1. Otherwise, specify one of the following numbers:<br/>
		///     1: Call the callback after any previously registered callbacks.<br/>
		///    -1: Call the callback before any previously registered callbacks.<br/>
		///     0: Remove the callback if it was already contained in the list.
		/// </param>
		public static object OnExit(object callback, object addRemove = null)
		{
			Script.TheScript.onExitHandlers.ModifyGlobalEventHandlers(Functions.GetKeysharpFunc(callback, null, true), addRemove.Al(1L));
			return DefaultObject;
		}

		/// <summary>
		/// Registers a function to be called automatically whenever the script receives the specified message.
		/// </summary>
		/// <param name="msgNumber">The number of the message to monitor or query, which should be between 0 and 4294967295 (0xFFFFFFFF).</param>
		/// <param name="callback">The name of a function to call whenever the specified message is received.<br/>
		/// The callback accepts four parameters as follows:<br/>
		///     The message's WPARAM value.<br/>
		///     The message's LPARAM value.<br/>
		///     The message number, which is useful in cases where a callback monitors more than one message.<br/>
		///     The HWND(unique ID) of the window or control to which the message was sent.The HWND can be used directly in a WinTitle parameter.
		/// </param>
		/// <param name="maxThreads">If omitted, it defaults to 1, meaning the callback is limited to one thread at a time.<br/>
		/// This is usually best because otherwise, the script would process messages out of chronological order whenever the callback interrupts itself.
		/// </param>
		public static object OnMessage(object msgNumber, object callback, object maxThreads = null)
		{
			var msg = msgNumber.Al();
			var mt = maxThreads.Al(1);
			var gd = Script.TheScript.GuiData;
			var monitor = gd.onMessageHandlers.GetOrAdd(msg);
			monitor.ModifyRegistration(Functions.GetKeysharpFunc(callback, null, true), mt);

			if (mt == 0 && monitor.IsEmpty)
				_ = gd.onMessageHandlers.TryRemove(msg, out var _);

#if !WINDOWS

			//A GUI built before this call has no motion handlers yet, and they are only wired while something
			//is listening for WM_MOUSEMOVE, so the ones already on screen have to be revisited here.
			if (msg == Keysharp.Internals.Os.Windows.WindowsAPI.WM_MOUSEMOVE)
				Keysharp.Internals.Window.Unix.EtoMessageSource.SyncMotionHooks();

#endif
			return DefaultObject;
		}

		/// <summary>
		/// Prevents the script from exiting automatically when its last thread completes, allowing it to stay running in an idle state.
		/// </summary>
		/// <param name="setting">If omitted, it defaults to true.<br/>
		/// If true, the script will be kept running after all threads have exited,<br/>
		/// even if none of the other conditions for keeping the script running are met.<br/>
		/// If false, the default behavior is restored.
		/// </param>
		/// <returns>The previous persistence boolean value.</returns>
		public static object Persistent(object setting = null)
		{
			var b = setting.Ab(true);
			var script = Script.TheScript;
			var old = script.persistent;
			script.persistent = script.FlowData.persistentValueSetByUser = b;

			if (!script.persistent)
				Script.TheScript.ExitIfNotPersistent();//Will internally call CheckedBeginInvoke().

			return old;
		}

		/// <summary>
		/// Replaces the currently running instance of the program with a new one.
		/// </summary>
		public static object Reload()
		{
			var script = Script.TheScript;
			if (script.scriptPath == "*")
				return DefaultObject;
			//Just calling Application.Restart will not always trigger ExitAppInternal().
			Script.PostToUIThread(() =>
			{
				A_ExitReason = ExitReasons.Reload;
#if WINDOWS
				Application.Restart();//This will pass the same command line args to the new instance that were passed to this instance.
#else
				Application.Instance.Restart();
#endif
				Keysharp.Internals.Flow.ExitAppInternal(ExitReasons.Reload);
			});
			var start = DateTime.UtcNow;

			while (!script.hasExited && (DateTime.UtcNow - start).TotalSeconds < 5)
				_ = Sleep(500);

			return DefaultObject;
		}

		/// <summary>
		/// Causes a function to be called automatically and repeatedly at a specified time interval.
		/// </summary>
		/// <param name="function">The function object to call.<br/>
		/// The callback accepts two parameters as follows:<br/>
		///     The function object itself.<br/>
		///     The date/time the timer was triggered as a YYYYMMDDHH24MISS string.<br/>
		/// </param>
		/// <param name="period">If omitted and the timer does not exist, it will be created with a period of 250.<br/>
		/// If omitted and the timer already exists, it will be reset at its former period unless Priority is specified.<br/>
		/// Otherwise, the absolute value of this parameter is used as the approximate number of milliseconds that must pass before<br/>
		/// the timer is executed. The timer will be automatically reset. It can be set to repeat automatically or run only once:<br/>
		///     If Period is greater than 0, the timer will automatically repeat until it is explicitly disabled by the Script.TheScript.<br/>
		///     If Period is less than 0, the timer will run only once.For example, specifying -100 would call Function 100 ms from now then delete the timer as though SetTimer Function, 0 had been used.<br/>
		///     If Period is 0, the timer is marked for deletion. If a thread started by this timer is still running, the timer is deleted after the thread finishes (unless it has been reenabled);<br/>
		///         otherwise, it is deleted immediately. In any case, the timer's previous Period and Priority are not retained.
		/// </param>
		/// <param name="priority">If omitted, it defaults to 0. Otherwise, specify an integer between 0 and 4<br/>
		/// to indicate this timer's thread priority. See <see cref="Threads"/> for details.<br/>
		/// To change the priority of an existing timer without affecting it in any other way, omit Period.
		/// </param>
		/// <exception cref="TypeError"></exception>
		public static object SetTimer(object function = null, object period = null, object priority = null)
		{
			var f = function;
			var p = period.Al(long.MaxValue);
			var pri = priority.Al();
			var once = p < 0;
			var func = default(KeysharpFunc);
			var script = Script.TheScript;
			var ownerScheduler = script.EventScheduler;
			ScriptTimerState timer = null;

			if (once)
				p = -p;

			if (f == null)
				timer = script.Threads.CurrentThread.currentTimer;//This means: use the timer which has already been created for this thread/timer event which we are currently inside of.
			else
			{
				func = Functions.GetKeysharpFunc(f, null);

				if (func == null)
					return (long)Errors.TypeErrorOccurred(f, typeof(KeysharpFunc));

				//Timer callbacks are invoked with no arguments, so a function that requires parameters can never
				//run. Reject it HERE rather than at every tick: the invocation path swallows callback exceptions,
				//so the only symptom used to be a timer that silently never fired.
				if (!func.CanAcceptArgCount(0))
					return (long)Errors.ValueErrorOccurred(
						$"SetTimer callback requires at least {func.MinParams} parameter(s), but timer callbacks are called with none.",
						//Mph.QualifiedName, not Name: a bound function's script-visible Name is empty, and an error that
						//names nothing is worse than one naming the target it was bound to.
						func.Mph.QualifiedName, DefaultErrorLong);

				timer = script.FlowData.timers.Find(func, ownerScheduler);
			}

			if (p == 0)
			{
				script.FlowData.timers.DisableOrDelete(timer);
				script.ExitIfNotPersistent();
				return DefaultObject;
			}

			if (timer == null)
			{
				if (func == null)
					return DefaultObject;

				if (p == long.MaxValue)//Period omitted and timer didn't exist, so create one with a 250ms interval.
					p = 250;

				_ = script.FlowData.timers.Upsert(func, ownerScheduler, p, once, pri);
				return DefaultObject;
			}

			if (p == long.MaxValue)
			{
				if (priority != null)//Period omitted and timer existed, but priority was specified, so just update the priority.
					script.FlowData.timers.UpdatePriority(timer, pri);
				else//Period omitted, timer did exist, and priority was omitted so reset it at its current interval.
					script.FlowData.timers.ResetTimer(timer);

				return DefaultObject;
			}

			_ = script.FlowData.timers.Upsert(timer.Callback, timer.OwnerScheduler, p, once, priority != null ? pri : timer.Priority);

			return DefaultObject;
		}

		/// <summary>
		/// Waits the specified amount of time before continuing.
		/// </summary>
		/// <param name="delay">The amount of time to pause in milliseconds.</param>
		public static object Sleep(object delay = null)
		{
			Keysharp.Internals.Flow.Sleep(delay.Ai(-1));
			return DefaultObject;
		}

		/// <summary>
		/// Disables or enables all or selected hotkeys.
		/// </summary>
		/// <param name="newState">
		/// If omitted, it defaults to -1. Otherwise, specify one of the following values:<br/>
		///     1: Suspends all hotkeys and hotstrings except those explained the Remarks section.<br/>
		///     0: Re-enables the hotkeys and hotstrings that were disable above.<br/>
		///    -1: Changes to the opposite of its previous state (On or Off).
		/// </param>
		public static object Suspend(object newState)
		{
			var state = Conversions.ConvertOnOffToggle(newState.As());
			var script = Script.TheScript;
			var fd = script.FlowData;
			fd.suspended = state == ToggleValueType.Toggle ? !fd.suspended : (state == ToggleValueType.On);

			if (!(bool)A_IconFrozen && !script.NoTrayIcon && script.EnsureTrayMenu())
				script.Tray.Icon = fd.suspended ? script.suspendedIcon : script.normalIcon;

			return DefaultObject;
		}

		/// <summary>
		/// Pauses the current thread, or pauses/unpauses the underlying thread.
		/// </summary>
		/// <param name="underlyingThreadState">
		/// If omitted, it pauses the current thread. Otherwise, specify one of the following values:<br/>
		///     1: Pauses the underlying thread.<br/>
		///     0: Unpauses the underlying thread.<br/>
		///    -1: Changes the underlying thread paused state to the opposite of its previous state (On or Off).
		/// </param>
		public static object Pause(object underlyingThreadState = null)
		{
			if (underlyingThreadState == null)
			{
				var tv = TheScript.Threads.CurrentThread;
				tv.isPaused = true;
				var threads = TheScript.Threads;
				var prevAllowTimers = threads.AllowTimers;
				threads.AllowTimers = false;

				Keysharp.Internals.Flow.WaitWithMessagePump(() => tv.isPaused);

				threads.AllowTimers = prevAllowTimers;
			}
			else
			{
				var state = Conversions.ConvertOnOffToggle(underlyingThreadState);
				switch (state)
				{
					case ToggleValueType.Toggle: ThreadAccessors.A_IsPaused = !ThreadAccessors.A_IsPaused; break;
					case ToggleValueType.Off: ThreadAccessors.A_IsPaused = false; break;
					default: ThreadAccessors.A_IsPaused = true; break;
				}
			}
			return DefaultObject;
		}

		// The AutoHotkey `Thread` function is not a function here: `Thread` is the script thread class, and calling
		// it runs the same sub-functions. See KeysharpThread.staticCall — one name covers the thread settings and the
		// thread object, which is what lets A_Thread's type simply be `Thread`.

		/// <summary>
		/// Throws the specified error object.
		/// </summary>
		/// <param name="value">The error object to throw.<br/>
		/// </param>
		[StackTraceHidden]
		public static object Throw(object value = null)
		{
			if (value is Error ex)
				ExceptionDispatchInfo.Capture(ex.Exception).Throw();
			else if (value == null)
			{
				throw new Error();
			}
			throw new Error("Invalid error object");
		}


        /// <summary>
        /// Special exception class to signal that the user has requested exiting the currently running
		/// pseudothread with Exit().
        /// Note this does not derive from Error so that it can be properly distinguished in
        /// catch statements.
        /// </summary>
        public class UserRequestedExitException : Exception
		{
			public UserRequestedExitException()
			{ }
		}

		/// <summary>
		/// The various reasons for exiting the script.
		/// </summary>
		public enum ExitReasons
		{
			Critical = -2, Destroy = -1, None = 0, Error, LogOff, Shutdown, Close, Menu, Exit, Reload, Single
		}
	}

	internal class FlowData : IDisposable
	{
		internal FlowData()
		{
			// The manager is a waker: it signals each due timer's owner scheduler (WakeForTimerCheck), whose pump runs
			// the due-check (ScriptEventScheduler.EnqueueDueTimers -> EnqueueTimer).
			timers = new();
		}

		/// <summary>
		/// Whether a thread can be interrupted/preempted by subsequent thread.
		/// </summary>
		internal bool allowInterruption = true;
		internal bool callingCritical;
		// True while OnExit callbacks are being invoked. A nested exit request during that window (a callback that
		// throws, errors, or calls ExitApp/Reload) must terminate directly instead of re-running the callbacks — see
		// ExitAppInternal. Matches AHK: an OnExit error terminates the script, and calling ExitApp in a callback
		// prevents the remaining callbacks.
		internal bool exitHandlersRunning;
		internal Timer1 mainTimer;
		internal int NoSleep = -1;
		internal bool persistentValueSetByUser;
		internal HashSet<object> initializedUserStaticVariables = new();

		/// <summary>
		/// Internal property to track whether the script's hotkeys and hotstrings are suspended.
		/// </summary>
		internal bool suspended;

		internal ScriptTimerManager timers;

		public void Dispose()
		{
			if (mainTimer != null)
			{
				mainTimer.Stop();
				mainTimer.Dispose();
				mainTimer = null;
			}

			timers?.Dispose();
		}
	}
}
