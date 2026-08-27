using Keysharp.Builtins;
using System.Runtime.CompilerServices;

namespace Keysharp.Internals.Window
{
	internal static class MsgMonitorExtensions
	{
		internal sealed class BufferedMessageQueuedEvent(MsgMonitorRegistration registration, Script script, object[] args, object eventInfo, long hwnd)
		{
			internal ScriptEventExecutionResult Execute()
				=> registration.TryExecuteBuffered(script, args, eventInfo, hwnd, out _);
		}

		private static ScriptEventExecutionResult ExecuteRegistration(this MsgMonitorRegistration registration, Script script, object[] args, object eventInfo, long hwnd, bool skipUninterruptible, bool allowEmergencyOverflow, out long result)
		{
			result = 0L;
			var targetScheduler = registration.OwnerScheduler;
			registration.InstanceCount++;

			try
			{
				if (targetScheduler.IsDisposed)
					return ScriptEventExecutionResult.Dropped;

				if (targetScheduler.OwnsCurrentThread)
					return InvokeRegistrationOnSchedulerThread(targetScheduler, registration, args, eventInfo, hwnd, skipUninterruptible, allowEmergencyOverflow, out result);

				var execution = targetScheduler.InvokeSynchronous(() =>
				{
					var status = InvokeRegistrationOnSchedulerThread(targetScheduler, registration, args, eventInfo, hwnd, skipUninterruptible, allowEmergencyOverflow, out var localResult);
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
		private static ScriptEventExecutionResult InvokeRegistrationOnSchedulerThread(ScriptEventScheduler targetScheduler, MsgMonitorRegistration registration, object[] args, object eventInfo, long hwnd, bool skipUninterruptible, bool allowEmergencyOverflow, out long result)
		{
			result = 0L;
			using var thread = targetScheduler.StartPseudoThreadScope(0, skipUninterruptible, false, allowEmergencyOverflow, ThreadKind.Message);

			if (!thread.Started)
				return thread.Result;

			try
			{
				var tv = thread.ThreadVariables;
				tv.eventInfo = eventInfo;
				tv.hwndLastUsed = hwnd;
				result = registration.Callback.Call(args).Al();
			}
			catch (Exception ex)
			{
				_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
				result = 0L;
			}

			return ScriptEventExecutionResult.Executed;
		}

		internal static ScriptEventExecutionResult TryExecuteBuffered(this MsgMonitorRegistration registration, Script script, object[] args, object eventInfo, long hwnd, out long result)
		{
			result = 0L;

			if (!registration.IsActive)
				return ScriptEventExecutionResult.Dropped;

			if (registration.InstanceCount >= registration.MaxInstances)
				return ScriptEventExecutionResult.LocalBlocked;

			var executionResult = registration.ExecuteRegistration(script, args, eventInfo, hwnd, false, false, out result);

			if (executionResult == ScriptEventExecutionResult.Executed)
				script.ExitIfNotPersistent();

			return executionResult;
		}

		internal static bool TryExecuteEmergency(this MsgMonitor monitor, Script script, object[] args, object eventInfo, long hwnd, out long result)
		{
			result = 0L;

			if (monitor == null)
				return false;

			var executedAny = false;

			foreach (var registration in monitor.GetRegistrationsSnapshot())
			{
				if (!registration.IsActive || registration.InstanceCount >= registration.MaxInstances)
					continue;

				var executionResult = registration.ExecuteRegistration(script, args, eventInfo, hwnd, true, true, out result);

				if (executionResult != ScriptEventExecutionResult.Executed)
					continue;

				executedAny = true;

				if (result != 0L)
					break;
			}

			if (executedAny)
				script.ExitIfNotPersistent();

			return executedAny;
		}
	}
}
