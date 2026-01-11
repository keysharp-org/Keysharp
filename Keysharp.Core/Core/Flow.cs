using Keysharp.Core.Scripting.Script;
using static Keysharp.Core.Errors;

using Timer1 = System.Timers.Timer;

namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for flow-related functions.
	/// </summary>
	public static class Flow
	{
		internal const int intervalUnspecified = int.MinValue + 303;// Use some negative value unlikely to ever be passed explicitly:

		/// <summary>
		/// Prevents the current thread from being interrupted by other threads, or enables it to be interrupted.
		/// </summary>
		/// <param name="onOffNumeric">
		/// If blank or omitted, it defaults to On. Otherwise, specify one of the following:<br/>
		///     On: The current thread is made critical, meaning that it cannot be interrupted by another thread.<br/>
		///     Off: The current thread immediately becomes interruptible, regardless of the settings of Thread Interrupt.<br/>
		///         See Critical Off for details.<br/>
		///     (Numeric): Specify a positive number to turn on Critical but also change the number of milliseconds between checks of the internal message queue.<br/>
		///             See Message Check Interval for details.<br/>
		///         Specifying 0 turns off Critical.<br/>
		///         Specifying -1 turns on Critical but disables message checks.<br/>
		/// </param>
		public static object Critical(object onOffNumeric = null)
		{
			var script = Script.TheScript;
			script.FlowData.callingCritical = true;
			var tv = script.Threads.CurrentThread;
			var on = onOffNumeric == null;
			long freq = !on ? (onOffNumeric.ParseLong(false) ?? 0L) : 0L;

			if (!on)
			{
				var b = Options.OnOff(onOffNumeric.As());

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
			tv.isCritical = onOffNumeric == null // i.e. omitted or blank is the same as "ON". See comments above.
							|| on
							|| freq > 0L; // Non-zero integer also turns it on. Relies on short-circuit boolean order.

			if (tv.isCritical) // Critical has been turned on. (For simplicity even if it was already on, the following is done.)
			{
				tv.configData.peekFrequency = freq;
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
				tv.allowThreadToBeInterrupted = true;
			}

			// The thread's interruptibility has been explicitly set; so the script is now in charge of
			// managing this thread's interruptibility.
			script.FlowData.callingCritical = false;
			return ret;
		}

		/// <summary>
		/// Exits the current thread or the entire script if non-persistent.
		/// The exit is achieved by throwing an exception which will be caught in the catch
		/// clause that wraps all threads.
		/// </summary>
		/// <param name="exitCode">An integer that is returned to the caller.</param>
		public static object Exit(object exitCode = null)
		{
			A_ExitReason = exitCode.Al();
			Environment.ExitCode = exitCode.Ai();
			throw new UserRequestedExitException();
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
				script.mainWindow.CheckedInvoke(() =>
				{
					_ = ExitAppInternal(ExitReasons.Exit, exitCode, true);
				}, true);
				var start = DateTime.UtcNow;

				while (!script.hasExited && (DateTime.UtcNow - start).TotalSeconds < 5)
					_ = Sleep(500);
			}

			return DefaultObject;
		}

		/// <summary>
		/// Returns whether the passed in value is true and the script is running.
		/// This is used in the generated code to evaluate loop variables.
		/// </summary>
		/// <param name="obj">The value to examine.</param>
		/// <returns>True if the value is true and the script is running, else false.</returns>
		public static bool IsTrueAndRunning(object obj)
		{
			var script = Script.TheScript;
			var b = (obj is bool ob ? ob : Script.ForceBool(obj)) && !script.hasExited;

			//Use Environment.TickCount because it's the fastest and we don't want to add extra time to each loop.
			//Its precision is around 15ms which is the amount we're testing for, so it should be ok.
			//https://stackoverflow.com/questions/243351/environment-tickcount-vs-datetime-now
			if (b && script.loopShouldDoEvents)
			{
				TryDoEvents(true, false);
			}

			return b;
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
			Script.TheScript.onExitHandlers.ModifyEventHandlers(Functions.GetFuncObj(callback, null, true), addRemove.Al(1L));
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

			if (mt > 0)
				monitor.maxInstances = Math.Clamp((int)mt, 1, Script.maxThreadsLimit);
			else if (mt < 0)
				monitor.maxInstances = (int)(-mt);

			monitor.funcs.ModifyEventHandlers(Functions.GetFuncObj(callback, null, true), mt);

			if (mt == 0 && monitor.funcs.Count == 0)
				_ = gd.onMessageHandlers.TryRemove(msg, out var _);

			return DefaultObject;
		}

		/// <summary>
		/// Prevents the script from exiting automatically when its last thread completes, allowing it to stay running in an idle state.
		/// </summary>
		/// <param name="persist">If omitted, it defaults to true.<br/>
		/// If true, the script will be kept running after all threads have exited,<br/>
		/// even if none of the other conditions for keeping the script running are met.<br/>
		/// If false, the default behavior is restored.
		/// </param>
		/// <returns>The previous persistence boolean value.</returns>
		public static object Persistent(object persist = null)
		{
			var b = persist.Ab(true);
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
			if (script.scriptName == "*")
				return DefaultObject;
			//Just calling Application.Restart will not always trigger ExitAppInternal().
			script.mainWindow.CheckedBeginInvoke(() =>
			{
				A_ExitReason = ExitReasons.Reload;
#if WINDOWS
				Application.Restart();//This will pass the same command line args to the new instance that were passed to this instance.
#else
				Application.Instance.Restart();
#endif
				ExitAppInternal(ExitReasons.Reload);
			}, true, true);
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
			IFuncObj func = null;
			TimerWithTag timer = null;
			var script = Script.TheScript;

			if (once)
				p = -p;

			if (f is string s)//Make sure they don't keep adding the same function object via string.
			{
				if (script.FlowData.cachedFuncObj.TryGetValue(s, out var tempfunc))
					func = tempfunc;
				else
					script.FlowData.cachedFuncObj[s] = func = Functions.GetFuncObj(s, null);
			}

			if (f != null && func == null)
			{
				func = f as FuncObj;

				if (func == null)
					return (long)Errors.TypeErrorOccurred(f, typeof(nint));
			}

			if (func != null && script.FlowData.timers.TryGetValue(func, out timer))
			{
			}
			else if (f == null)
				timer = script.Threads.CurrentThread.currentTimer;//This means: use the timer which has already been created for this thread/timer event which we are currently inside of.

			if (timer != null)
			{
				if (p == 0)
				{
					if (func == null)
					{
						var temptimer = script.FlowData.timers.Where((kv) => kv.Value == timer).FirstOrDefault();

						if (temptimer.Key != null)
							_ = script.FlowData.timers.TryRemove(temptimer.Key, out _);
					}
					else
						_ = script.FlowData.timers.TryRemove(func, out _);

					timer.Stop();
					timer.Dispose();
					script.ExitIfNotPersistent();
					return DefaultObject;
				}
				else
				{
					if (p == long.MaxValue)
					{
						if (pri != timer.Tag.Al())//Period omitted and timer existed, but priority was specified, so just update the priority.
						{
							timer.Tag = pri;
						}
						else//Period omitted, timer did exist, and priority was omitted so reset it at its current interval.
						{
							timer.Stop();
							timer.Start();
						}
					}
					else
					{
						if (timer.Interval == p)
							timer.Interval = int.MaxValue;
						timer.Interval = (int)p;
					}

					return DefaultObject;
				}
			}
			else if (p != 0)
			{
				if (p == long.MaxValue)//Period omitted and timer didn't exist, so create one with a 250ms interval.
					p = 250;

				_ = script.FlowData.timers.TryAdd(func, timer = new ());
				timer.Tag = (int)pri;
				timer.Interval = (int)p;
			}
			else//They tried to stop a timer that didn't exist
				return DefaultErrorObject;

			timer.Tick += (ss, ee) =>
			{
				var script = Script.TheScript;//Avoid a capture.
				var v = script.Threads;

				//If script has exited or we don't receive a TimerWithTag object, just exit
				if (A_HasExited || (ss is not TimerWithTag t))
					return;

				if (!t.Enabled)//A way of checking to make sure the timer is not already executing.
					return;

				//If there are not enough threads, then this will exit and retry. Even a single shot
				//timer will keep trying until it gets through.
				//Note that this means there is no real "queueing" of timer events. Rather,
				//they just keep getting retried.
				//The reason for this is that if a timer event calls Sleep() which calls DoEvents(),
				//we can't also call those functions here or else the program will freeze/crash.
				if ((!A_AllowTimers.Ab() && script.totalExistingThreads > 0)
						|| !v.AnyThreadsAvailable() || !script.Threads.IsInterruptible())
				{
					t.PushToMessageQueue();
					return;
				}

				var pri = t.Tag.Ai();
				var tv = v.CurrentThread;

				if (pri >= tv.priority)
				{
					t.Enabled = false;
					var btv = v.PushThreadVariables(pri, true, false, false, true);
					_ = TryCatch(() =>
					{
						btv.Item2.currentTimer = timer;
						btv.Item2.eventInfo = func;
						var ret = func.Call();
						_ = v.EndThread(btv);
					}, true, btv);//Pop on exception because EndThread() above won't be called.

					if (once)
					{
						_ = script.FlowData.timers.TryRemove(func, out _);
						t.Stop();
						t.Dispose();
						script.ExitIfNotPersistent();
					}
					else if (script.FlowData.timers.TryGetValue(func, out var existing))//They could have disabled it, in which case it wouldn't be in the dictionary.
						existing.Enabled = true;
					else
						script.ExitIfNotPersistent();//Was somehow removed, such as in a window close handler, so attempt to exit.
				}
			};
			script.mainWindow.CheckedInvoke(timer.Start, true);

			if (script.totalExistingThreads >= script.MaxThreadsTotal)
				timer.Pause();
			else if (timer.Interval == 1)
				timer.PushToMessageQueue();

			//script.mainWindow.CheckedBeginInvoke(timer.Start, true, true);
			return DefaultObject;
		}

		/// <summary>
		/// Calls DoEvents() but catches and discards all errors, except if <paramref name="allowExit"/>
		/// is true in which case UserRequestedExitException is allowed through.
		/// </summary>
		/// <param name="allowExit"/>If true then UserRequestedExitException is allowed through
		/// so the program can exit, otherwise all exceptions are discarded.
		internal static void TryDoEvents(bool allowExit = true, bool yieldTick = true)
		{
			var start = yieldTick ? Environment.TickCount : default;
			var script = Script.TheScript;
			script.loopShouldDoEvents = false;
			script.tickTimer.Start();

			if (allowExit)
			{
				try
				{
#if WINDOWS
					Application.DoEvents();
#else
					Application.Instance.RunIteration();
#endif
				}
				catch (UserRequestedExitException)
				{
					throw;
				}
				catch
				{
				}
			}
			else
			{
				try
				{
#if WINDOWS
					Application.DoEvents();
#else
					Application.Instance.RunIteration();
#endif
				}
				catch
				{
				}
			}

			if (yieldTick && start.Equals(Environment.TickCount))
				//0 tells this thread to relinquish the remainder of its time slice to any thread of equal priority that is ready to run.
				//If there are no other threads of equal priority that are ready to run, execution of the current thread is not suspended.
				System.Threading.Thread.Sleep(1);

			if (script.hasExited)
				throw new UserRequestedExitException();
		}

		/// <summary>
		/// Waits the specified amount of time before continuing.
		/// </summary>
		/// <param name="delay">The amount of time to pause in milliseconds.</param>
		public static object Sleep(object delay = null)
		{
			var d = delay.Al(-1L);
			var script = Script.TheScript;

			//Be careful with Application.DoEvents(), it has caused spurious crashes in my years of programming experience.
			if (d == 0L)
			{
				var tc = Environment.TickCount;
				TryDoEvents(true, false);
				if (Environment.TickCount - tc == 0)
					System.Threading.Thread.Sleep(0);
			}
			else if (d == -1L)
			{
				TryDoEvents(true, false);
			}
			else if (d == -2)//Sleep indefinitely until all InputHooks are finished.
			{
				while (!script.hasExited && script.input != null && script.input.InProgress())
				{
					TryDoEvents(); //Does events once per tick
				}
			}
			else
			{
				var stop = DateTime.UtcNow.AddMilliseconds(d);//Using Application.DoEvents() is a pseudo-sleep that blocks until the timeout, but doesn't freeze the window.

				while (DateTime.UtcNow < stop && !script.hasExited)
				{
					TryDoEvents(); //Does events once per tick
				}
			}

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

			if (!(bool)A_IconFrozen && !script.NoTrayIcon)
				script.Tray.Icon = fd.suspended ? script.suspendedIcon : script.normalIcon;

			return DefaultObject;
		}

		/// <summary>
		/// Pauses the current thread, or pauses/unpauses the underlying thread.
		/// </summary>
		/// <param name="threadState">
		/// If omitted, it pauses the current thread. Otherwise, specify one of the following values:<br/>
		///     1: Pauses the underlying thread.<br/>
		///     0: Unpauses the underlying thread.<br/>
		///    -1: Changes the underlying thread paused state to the opposite of its previous state (On or Off).
		/// </param>
		public static object Pause(object threadState = null)
		{
			if (threadState == null)
			{
				var tv = TheScript.Threads.CurrentThread;
				tv.isPaused = true;
				var prevAllowTimers = A_AllowTimers;
				A_AllowTimers = false;

				while (tv.isPaused)
					Flow.TryDoEvents();

				A_AllowTimers = prevAllowTimers;
			}
			else
			{
				var state = Conversions.ConvertOnOffToggle(threadState);
				switch (state)
				{
					case ToggleValueType.Toggle: ThreadAccessors.A_IsPaused = !ThreadAccessors.A_IsPaused; break;
					case ToggleValueType.Off: ThreadAccessors.A_IsPaused = false; break;
					default: ThreadAccessors.A_IsPaused = true; break;
				}
			}
			return DefaultObject;
		}

		/// <summary>
		/// Sets the priority or interruptibility of threads. It can also temporarily disable all timers.<br/>
		/// The subFunction, Value1, and Value2 parameters are dependent upon each other and their usage is described below.
		/// </summary>
		/// <param name="subFunction">Specify one of the following:<br/>
		///     NoTimers: Prevents interruptions from any timers until the current thread either ends, executes Thread "NoTimers", false, or is interrupted by another thread that allows timers (in which case timers can interrupt the interrupting thread until it finishes).<br/>
		///     Priority: Changes the priority level of the current thread.<br/>
		///     Interrupt: Changes the duration of interruptibility for newly launched threads.
		/// </param>
		/// <param name="val1">Has a different meaning depending on subFunction:
		///     NoTimers: True to disallow timers, else false to allow timers.  Default: true.<br/>
		///     Priority: The thread priority as an integer in the range -2147483648 and 2147483647.<br/>
		///     Interrupt: The time in milliseconds that each newly launched thread is uninterruptible. Default: 17.
		/// </param>
		public static object Thread(object subFunction, object value1 = null)
		{
			var sf = subFunction.As();
			var script = Script.TheScript;

			if (string.Compare(sf, "notimers", true) == 0)
				A_AllowTimers = !(Options.OnOff(value1.As()) ?? false);
			else if (string.Compare(sf, "priority", true) == 0)
				script.Threads.CurrentThread.priority = value1.Al();
			else if (string.Compare(sf, "interrupt", true) == 0)
				script.uninterruptibleTime = value1.Ai(script.uninterruptibleTime);

			return DefaultObject;
		}

		/// <summary>
		/// Throws the specified error object.
		/// </summary>
		/// <param name="errorObject">The error object to throw.<br/>
		/// </param>
		[StackTraceHidden]
		public static object Throw(object errorObject = null)
		{
			if (errorObject is Exception ex)
				ExceptionDispatchInfo.Capture(ex).Throw();
			else if (errorObject == null)
			{
				throw new Error();
			} 
			throw new Error("Invalid error object");
		}

		/// <summary>
		/// Internal helper to handle exiting the Script.TheScript.
		/// </summary>
		/// <param name="exitReason">The <see cref="ExitReason"/> for exiting the Script.TheScript.</param>
		/// <param name="exitCode">The exit code to return from the script when it exits.</param>
		/// <returns>True if exiting was interrupted by a non empty callback return value, else false.</returns>
		internal static bool ExitAppInternal(ExitReasons exitReason, object exitCode = null, bool useThrow = true)
		{
			var script = Script.TheScript;
			var fd = script.FlowData;

			if (script.hasExited)//This can be called multiple times, so ensure it only runs through once.
				return false;

			script.tickTimer.Stop();
			Dialogs.CloseMessageBoxes();
			Dialogs.CloseToolTips();
			var ec = exitCode.Ai();
			A_ExitReason = exitReason.ToString();
			var allowInterruption_prev = fd.allowInterruption;//Save current setting.
			fd.allowInterruption = false;
			var result = script.onExitHandlers.InvokeEventHandlers(A_ExitReason, exitCode);

			//If it wasn't a critical shutdown and any exit handlers returned a non empty value, abort the exit.
			if (exitReason >= ExitReasons.None && Script.ForceLong(result) != 0L)
			{
				A_ExitReason = "";
				fd.allowInterruption = allowInterruption_prev;
				return true;
			}
			else
				script.onExitHandlers.Clear();

			// Do this here so if destructors throw then no dialogs are shown
			script.SuppressErrorOccurredDialog = true;

			GC.Collect();
			GC.WaitForPendingFinalizers();
			DestructorPump.RunPendingDestructors();

			foreach (var t in Reflections.GetNestedTypes([script.ProgramType])
				.OrderBy(Reflections.GetInheritanceDepth))
			{
				var fields = t.GetFields(BindingFlags.Static | BindingFlags.Public);

				foreach (var val in fields.Select(f => f.GetValue(null)))
				{
					if (val is Any kso) CallDeleteSilent(kso);
				}

				if (script.Vars.Statics.TryGetValue(t, out Any kso2) && kso2.HasOwnPropInternal("__Delete"))
					CallDeleteSilent(kso2);
			}

			void CallDeleteSilent(Any kso)
			{
				try
				{
					kso.HasFinalizer = false;
					InvokeMeta(kso, "__Delete");
					if (kso is IDisposable dis) dis.Dispose();
				}
				catch { }
			}

			script.hasExited = true;//At this point, we are clear to exit, so do not allow any more calls to this function.
			fd.allowInterruption = allowInterruption_prev;
			HotkeyDefinition.AllDestruct();
			StopMainTimer();

			foreach (var kv in script.FlowData.timers)
				kv.Value.Stop();

			Gui.DestroyAll();
			script.Stop();
			Environment.ExitCode = ec;
			script.mainWindow?.Close();

			if (useThrow)
				throw new UserRequestedExitException();
			return false;
		}

		/// <summary>
		/// Internal helper to create and start the main timer.
		/// This is used whenever a joystick hook is present.
		/// </summary>
		internal static void SetMainTimer()
		{
			var script = Script.TheScript;
			var mainTimer = Script.TheScript.FlowData.mainTimer;

			if (mainTimer == null)
			{
				mainTimer = new (10);
				mainTimer.Elapsed += (o, e) => { };
				script.FlowData.mainTimer = mainTimer;
				mainTimer.Start();
			}
		}

		/// <summary>
		/// Internal helper to call <see cref="Sleep"/> in between the toggling of <see cref="AllowInterruption"/> off then back on.
		/// </summary>
		/// <param name="duration">The time in milliseconds to sleep for.</param>
		internal static void SleepWithoutInterruption(object duration = null)
		{
			var fd = Script.TheScript.FlowData;
			fd.allowInterruption = false;
			_ = Sleep(duration);
			fd.allowInterruption = true;
		}

		/// <summary>
		/// Internal helper to stop the main timer and set it to null.
		/// This is used when exiting the Script.TheScript.
		/// </summary>
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

		/// <summary>
		/// Internal helper to wrap an <see cref="Action"/> within a try/catch block.<br/>
		/// This is done because the try/catch blocks have a lot of scaffolding code that is needed for handling<br/>
		/// threads and error conditions and it would be very verbose and unmaintainable to repeat it everywhere.
		/// </summary>
		/// <param name="action">The action to perform in the try block.</param>
		/// <param name="pop">True to pop the current thread by calling <see cref="Threads.EndThread(bool, bool)"/> when an exception is caught.<br/>
		/// Pass true for this when the action internally calls <see cref="Threads.BeginThread(bool)"/> before executing code that<br/>
		/// could potentially thrown an exception.
		/// </param>
		/// <param name="btv">The thread object that was created before calling this function.</param>
		/// <returns>True if no errors occurred, else false if any catch blocks were reached.</returns>
		internal static bool TryCatch(Action action, bool pop, (bool, ThreadVariables) btv)
		{
			var t = Script.TheScript.Threads;

			try
			{
				action();
				return true;
			}
			catch (Error kserr)
			{
				//Processed would still be false of the user did a throw statement in the Script.TheScript.
				//But if we're throwing from inside of Keysharp, Processed should always be true.
				if (!kserr.Processed)
					_ = ErrorOccurred(kserr, kserr.ExcType);

				if (!kserr.Handled && !TheScript.SuppressErrorOccurredDialog)
				{
					var (__pushed, __btv) = t.BeginThread();
					_ = ErrorDialog.Show(kserr, false);
					_ = t.EndThread((__pushed, __btv));
				}

				if (pop)
					_ = t.EndThread(btv);

				return false;
			}
			catch (Exception mainex)
			{
				var ex = mainex.InnerException ?? mainex;

				if (ex is UserRequestedExitException)
				{
					if (pop)
						_ = t.EndThread(btv);

					return true;
				}
				else if (ex is Error kserr)
				{
					if (!kserr.Processed)
						_ = ErrorOccurred(kserr, kserr.ExcType);

					if (!kserr.Handled && !TheScript.SuppressErrorOccurredDialog)
					{
						var (__pushed, __btv) = t.BeginThread();
						_ = ErrorDialog.Show(kserr, false);
						_ = t.EndThread((__pushed, __btv));
					}
				}
				else if (!TheScript.SuppressErrorOccurredDialog)
				{
					var (__pushed, __btv) = t.BeginThread();
					_ = ErrorDialog.Show(ex);
					_ = t.EndThread((__pushed, __btv));
				}

				if (pop)
					_ = t.EndThread(btv);

				return false;
			}
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

	public static partial class KeysharpEnhancements
	{
		/// <summary>
		/// Iterates through all timers in existence and returns the number of them which are enabled.
		/// </summary>
		/// <returns>The number of currently enabled timers.</returns>
		public static long EnabledTimerCount() => Script.TheScript.FlowData.timers.Count(kv => kv.Value.Enabled);
	}

	internal class FlowData
	{
		/// <summary>
		/// Whether a thread can be interrupted/preempted by subsequent thread.
		/// </summary>
		internal bool allowInterruption = true;
		internal ConcurrentDictionary<string, IFuncObj> cachedFuncObj = new (StringComparer.OrdinalIgnoreCase);
		internal bool callingCritical;
		internal Timer1 mainTimer;
		internal int NoSleep = -1;
		internal bool persistentValueSetByUser;
		internal HashSet<object> initializedUserStaticVariables = new();

		/// <summary>
		/// Internal property to track whether the script's hotkeys and hotstrings are suspended.
		/// </summary>
		internal bool suspended;

		internal ConcurrentDictionary<IFuncObj, TimerWithTag> timers = new ();
	}
}