using Keysharp.Builtins;
using System.Runtime.CompilerServices;

namespace Keysharp.Internals.Window
{
	internal static class MsgMonitorExtensions
	{
		internal sealed class BufferedMessageQueuedEvent(MsgMonitor monitor, Script script, object[] args, object eventInfo, long hwnd)
		{
			internal ScriptEventExecutionResult Execute()
				=> monitor.RunMonitors(script, args, eventInfo, hwnd, false, out _);
		}

		private static ScriptEventExecutionResult ExecuteRegistration(this MsgMonitorRegistration registration, Script script, object[] args, object eventInfo, long hwnd, bool emergency, out object result)
		{
			result = null;
			var targetScheduler = registration.OwnerScheduler;
			registration.InstanceCount++;

			try
			{
				if (targetScheduler.IsDisposed)
					return ScriptEventExecutionResult.Dropped;

				if (targetScheduler.OwnsCurrentThread)
					return InvokeRegistrationOnSchedulerThread(targetScheduler, registration, args, eventInfo, hwnd, emergency, out result);

				var execution = targetScheduler.InvokeSynchronous(() =>
				{
					var status = InvokeRegistrationOnSchedulerThread(targetScheduler, registration, args, eventInfo, hwnd, emergency, out var localResult);
					return (status, localResult);
				});
				result = execution.localResult;
				return execution.status;
			}
			finally
			{
				registration.InstanceCount--;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ScriptEventExecutionResult InvokeRegistrationOnSchedulerThread(ScriptEventScheduler targetScheduler, MsgMonitorRegistration registration, object[] args, object eventInfo, long hwnd, bool emergency, out object result)
		{
			result = null;

			// AHK keeps a message queued while the script is uninterruptible, so the chain waits; any other refusal, such
			// as #MaxThreads or priority, leaves the message unmonitored, as AHK's MsgMonitor does.
			if (!emergency && !targetScheduler.Owner.Threads.IsInterruptible())
				return ScriptEventExecutionResult.GlobalBlocked;

			using var thread = targetScheduler.StartPseudoThreadScope(0, emergency, false, emergency, ThreadKind.Message);

			if (!thread.Started)
				return ScriptEventExecutionResult.Dropped;

			try
			{
				var tv = thread.ThreadVariables;
				tv.eventInfo = eventInfo;
				tv.hwndLastUsed = hwnd;
				result = Script.InvokeOrNull(registration.Callback, null, args);
			}
			catch (Exception ex) when (CallStack.RememberAndCatch(ex))
			{
				// Errors and Exit leave the message unclaimed.
				_ = Errors.ReportUncaught(ex);
				result = null;
			}

			return ScriptEventExecutionResult.Executed;
		}

		private static ScriptEventExecutionResult RunMonitors(this MsgMonitor monitor, Script script, object[] args, object eventInfo, long hwnd, bool emergency, out object claim)
		{
			claim = null;
			var anyRan = false;
			var blocked = ScriptEventExecutionResult.Dropped;

			foreach (var registration in monitor.GetRegistrationsSnapshot())
			{
				// A callback already running its MaxThreads leaves the message unmonitored, as in AHK.
				if (!registration.IsActive || registration.InstanceCount >= registration.MaxInstances)
					continue;

				var status = registration.ExecuteRegistration(script, args, eventInfo, hwnd, emergency, out claim);

				if (status != ScriptEventExecutionResult.Executed)
				{
					if (status == ScriptEventExecutionResult.GlobalBlocked)
						blocked = status;

					continue;
				}

				anyRan = true;

				if (CallbackStop.NonEmpty(claim))
					return status;
			}

			claim = null;
			return anyRan ? ScriptEventExecutionResult.Executed : blocked;
		}

		/// <summary>
		/// Runs the chain for a posted message before it is dispatched, as AHK does while the script is interruptible.
		/// While it is not, the chain is queued for when it is and the message is dispatched meanwhile.
		/// </summary>
		internal static bool TryExecuteBeforeDispatch(this MsgMonitor monitor, Script script, object[] args, object eventInfo, long hwnd, out long reply)
		{
			reply = 0L;

			if (monitor.RunMonitors(script, args, eventInfo, hwnd, false, out var claim) == ScriptEventExecutionResult.GlobalBlocked)
			{
				var queuedEvent = new BufferedMessageQueuedEvent(monitor, script, args, eventInfo, hwnd);
				_ = script.EventScheduler.Enqueue(ScriptEventQueue.Normal, 0, queuedEvent.Execute);
				return false;
			}

			if (claim == null)
				return false;

			_ = claim.TryCoerceLong(out reply);
			return true;
		}

		internal static bool TryExecuteEmergency(this MsgMonitor monitor, Script script, object[] args, object eventInfo, long hwnd, out long reply)
		{
			reply = 0L;

			if (monitor == null)
				return false;

			_ = monitor.RunMonitors(script, args, eventInfo, hwnd, true, out var claim);

			if (claim == null)
				return false;

			_ = claim.TryCoerceLong(out reply);
			return true;
		}
	}
}
