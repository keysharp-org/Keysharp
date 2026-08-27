#if OSX
using Keysharp.Builtins;
using Keysharp.Internals.Input.Hooks.MacOS;

namespace Keysharp.Internals.Input.MacOS
{
	internal enum MacEventTapState : byte
	{
		Created,
		Starting,
		Running,
		Stopping,
		Stopped,
		Faulted
	}

	internal class MacEventTapDriver
	{
		internal static readonly MacEventTapDriver Native = new();
		internal virtual nint CurrentRunLoop() => MacNativeInput.CFRunLoopGetCurrent();
		internal virtual nint CreateTap(ulong eventMask, MacNativeInput.CGEventTapCallBack callback)
			=> MacNativeInput.CGEventTapCreate(MacNativeInput.kCGSessionEventTap,
				MacNativeInput.kCGHeadInsertEventTap, MacNativeInput.kCGEventTapOptionDefault,
				eventMask, callback, nint.Zero);
		internal virtual nint CreateRunLoopSource(nint tap)
			=> MacNativeInput.CFMachPortCreateRunLoopSource(nint.Zero, tap, nint.Zero);
		internal virtual void AddSource(nint runLoop, nint source)
			=> MacNativeInput.CFRunLoopAddSource(runLoop, source, MacNativeInput.RunLoopDefaultMode);
		internal virtual void RemoveSource(nint runLoop, nint source)
			=> MacNativeInput.CFRunLoopRemoveSource(runLoop, source, MacNativeInput.RunLoopDefaultMode);
		internal virtual void EnableTap(nint tap, bool enable) => MacNativeInput.CGEventTapEnable(tap, enable);
		internal virtual bool IsTapValid(nint tap) => MacNativeInput.CFMachPortIsValid(tap);
		internal virtual int RunInDefaultMode(double seconds)
			=> MacNativeInput.CFRunLoopRunInMode(MacNativeInput.RunLoopDefaultMode, seconds, false);
		internal virtual void StopRunLoop(nint runLoop) => MacNativeInput.CFRunLoopStop(runLoop);
		internal virtual void Release(nint handle) => MacNativeInput.CFRelease(handle);
	}

	internal sealed class MacNativeEventTap : IDisposable
	{
		private const int StopTimeoutMs = 3000;
		private const int MaxRecoveryAttempts = 3;
		private const long RecoveryWindowMs = 5000;
		private const double RunLoopPollSeconds = 0.5;
		private const int RunLoopFinished = 1;
		private const int RunLoopStopped = 2;
		private readonly Script owner;
		private readonly Func<uint, nint, bool> processEvent;
		private readonly Action tapReenabled;
		private readonly Action<MacNativeEventTap, string> tapTerminated;
		private readonly MacEventTapDriver driver;
		private readonly MacNativeInput.CGEventTapCallBack callback;
		private readonly ulong eventMask;
		private readonly Lock handleLock = new();
		private readonly Lock lifecycleLock = new();
		private readonly ManualResetEventSlim ready = new(false);
		private Thread thread;
		private nint tap;
		private nint source;
		private nint runLoop;
		private int state = (int)MacEventTapState.Created;
		private int timeoutDisableCount;

		internal MacNativeEventTap(MacHookThread owner, ulong eventMask)
			: this(owner.script, eventMask,
				(type, cgEvent) => owner.ProcessNativeKeyboardEvent(type, cgEvent)
					|| owner.ProcessNativeMouseEvent(type, cgEvent),
				owner.OnNativeTapReenabled, owner.OnNativeTapTerminated, MacEventTapDriver.Native)
		{
		}

		internal MacNativeEventTap(Script owner, ulong eventMask, Func<uint, nint, bool> processEvent,
			Action tapReenabled, Action<MacNativeEventTap, string> tapTerminated, MacEventTapDriver driver)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			this.eventMask = eventMask;
			this.processEvent = processEvent ?? throw new ArgumentNullException(nameof(processEvent));
			this.tapReenabled = tapReenabled ?? throw new ArgumentNullException(nameof(tapReenabled));
			this.tapTerminated = tapTerminated ?? throw new ArgumentNullException(nameof(tapTerminated));
			this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
			callback = OnEvent;
		}

		internal ulong EventMask => eventMask;
		internal bool IsRunning => State == MacEventTapState.Running;
		internal MacEventTapState State => (MacEventTapState)Volatile.Read(ref state);
		internal string StartupFailure { get; private set; }

		internal bool Start(int timeoutMs)
		{
			lock (lifecycleLock)
			{
				if (thread is { IsAlive: true })
					return IsRunning;
				if (State is MacEventTapState.Starting or MacEventTapState.Stopping)
					return false;

				StartupFailure = null;
				ready.Reset();
				SetState(MacEventTapState.Starting);
				thread = new Thread(Run) { IsBackground = true, Name = "Keysharp macOS CGEvent tap" };
				thread.Start();
			}

			if (!ready.Wait(timeoutMs))
			{
				StartupFailure = $"event tap startup timed out after {timeoutMs} ms";
				Stop();
				return false;
			}

			return IsRunning;
		}

		internal bool Stop()
		{
			if (thread == Thread.CurrentThread)
				return false;
			Thread threadToJoin;
			lock (lifecycleLock)
			{
				if (State is MacEventTapState.Starting or MacEventTapState.Running)
					SetState(MacEventTapState.Stopping);
				threadToJoin = thread;
			}

			lock (handleLock)
			{
				// Cleanup takes the same lock before releasing either handle, so Stop cannot
				// call CoreGraphics with a handle which the event-tap thread just freed.
				if (tap != nint.Zero)
					driver.EnableTap(tap, false);
				if (runLoop != nint.Zero)
					driver.StopRunLoop(runLoop);
			}

			var stopped = threadToJoin == null || !threadToJoin.IsAlive || threadToJoin.Join(StopTimeoutMs);
			if (!stopped)
			{
				Diagnostics.Debug.WriteLine($"macOS event-tap thread did not exit within {StopTimeoutMs} ms; native resources remain owned by that thread.");
				return false;
			}

			lock (lifecycleLock)
			{
				if (thread == threadToJoin)
					thread = null;
			}
			return true;
		}

		private void Run()
		{
			string terminalFailure = null;
			var everStarted = false;
			var recoveryAttempts = 0;
			var lastFailureAt = 0L;
			try
			{
				while (State != MacEventTapState.Stopping)
				{
					var reason = RunOnce(everStarted, out var started);
					everStarted |= started;
					if (State == MacEventTapState.Stopping)
						break;
					if (!everStarted)
					{
						StartupFailure = reason;
						terminalFailure = reason;
						break;
					}

					var now = Environment.TickCount64;
					recoveryAttempts = now - lastFailureAt < RecoveryWindowMs ? recoveryAttempts + 1 : 1;
					lastFailureAt = now;
					if (recoveryAttempts >= MaxRecoveryAttempts)
					{
						terminalFailure = reason;
						break;
					}

					Diagnostics.Debug.WriteLine($"macOS event tap lost ({reason}); recreating it.");
					lock (lifecycleLock)
					{
						if (State == MacEventTapState.Stopping)
							break;
						SetState(MacEventTapState.Starting);
					}
				}
			}
			catch (Exception ex)
			{
				terminalFailure = $"event-tap thread failed: {ex.Message}";
				StartupFailure ??= terminalFailure;
				Diagnostics.Debug.WriteLine($"macOS event-tap thread failed: {ex}");
			}
			finally
			{
				ready.Set();
				ReleaseHandles();
				bool failed;
				lock (lifecycleLock)
				{
					failed = State != MacEventTapState.Stopping && terminalFailure != null;
					SetState(failed ? MacEventTapState.Faulted : MacEventTapState.Stopped);
				}
				if (failed && everStarted && !owner.IsDisposed)
					tapTerminated(this, terminalFailure);
			}
		}

		private string RunOnce(bool recovering, out bool started)
		{
			started = false;
			try
			{
				var createdTap = driver.CreateTap(eventMask, callback);
				if (createdTap == nint.Zero)
					return "CGEventTapCreate returned null";
				var createdSource = driver.CreateRunLoopSource(createdTap);
				if (createdSource == nint.Zero)
				{
					driver.Release(createdTap);
					return "CFMachPortCreateRunLoopSource returned null";
				}

				lock (handleLock)
				{
					runLoop = driver.CurrentRunLoop();
					tap = createdTap;
					source = createdSource;
				}
				if (State == MacEventTapState.Stopping)
					return null;

				driver.AddSource(runLoop, source);
				lock (lifecycleLock)
				{
					if (State == MacEventTapState.Stopping)
						return null;
					started = true;
					SetState(MacEventTapState.Running);
				}
				ready.Set();
				if (recovering)
				{
					try { tapReenabled(); }
					catch (Exception ex) { Diagnostics.Debug.WriteLine($"macOS event-tap state resync failed: {ex}"); }
				}
				while (State == MacEventTapState.Running)
				{
					if (!driver.IsTapValid(tap))
						return "the event-tap Mach port became invalid";
					var result = driver.RunInDefaultMode(RunLoopPollSeconds);
					if (State == MacEventTapState.Running && result is RunLoopFinished or RunLoopStopped)
						return $"the event-tap run loop exited unexpectedly (result {result})";
				}
				return null;
			}
			catch (Exception ex) { return ex.Message; }
			finally { ReleaseHandles(); }
		}

		private void ReleaseHandles()
		{
			lock (handleLock)
			{
				if (source != nint.Zero)
				{
					if (runLoop != nint.Zero) driver.RemoveSource(runLoop, source);
					driver.Release(source);
				}
				if (tap != nint.Zero) driver.Release(tap);
				source = tap = runLoop = nint.Zero;
			}
		}

		private nint OnEvent(nint proxy, uint type, nint cgEvent, nint userInfo)
		{
			if (owner.IsDisposed)
				return cgEvent;

			try
			{
				if (type == MacNativeInput.kCGEventTapDisabledByTimeout || type == MacNativeInput.kCGEventTapDisabledByUserInput)
				{
					if (type == MacNativeInput.kCGEventTapDisabledByTimeout)
					{
						var count = Interlocked.Increment(ref timeoutDisableCount);
						Diagnostics.Debug.WriteLine($"macOS event tap was disabled by the watchdog (occurrence {count}); re-enabling it.");
					}

					var reenabled = false;
					try
					{
						lock (handleLock)
						{
							if (State == MacEventTapState.Running && tap != nint.Zero)
							{
								driver.EnableTap(tap, true);
								reenabled = true;
							}
						}
					}
					finally
					{
						// State resync is secondary to restoring the native input path.
						if (reenabled)
							tapReenabled();
					}
					return cgEvent;
				}

				if (State != MacEventTapState.Running || cgEvent == nint.Zero)
					return cgEvent;

				using var callbackBudget = HookThread.BeginHotIfCallback(HookThread.HotIfCallbackBudgetMilliseconds);
				return processEvent(type, cgEvent) ? nint.Zero : cgEvent;
			}
			catch (Exception ex)
			{
				// Exceptions must never cross the native callback boundary. Passing the event through
				// is safer than leaving user input suppressed after a Keysharp failure.
				Diagnostics.Debug.WriteLine($"macOS event-tap callback failed; passing the event through: {ex}");
				return cgEvent;
			}
		}

		private void SetState(MacEventTapState value) => Volatile.Write(ref state, (int)value);

		public void Dispose()
		{
			if (!Stop())
				throw new TimeoutException("The macOS event-tap thread did not stop; ownership was retained to avoid releasing resources still in use.");
		}
	}
}
#endif
