using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>Ordered request/reply delivery for Script.OnMessage. Registration and ownership use the common backend.</summary>
	internal sealed class ScriptMessageManager(Script script)
		: EventManagerBase<CallbackRegistration, IDisposable, NoPayload>(script, false)
	{
		private static readonly byte[] EmptyReply = [ScriptEndpoint.Empty];

		protected override ThreadKind CallbackThreadKind => ThreadKind.Message;
		protected override string RegistrationName => "Script.OnMessage";

		protected override IDisposable CreateBackend() => null;

		protected override void SyncNativeLocked() { }

		/// <summary>Delivers one message and returns the reply frame: its kind, then its bytes.</summary>
		internal async Task<byte[]> DispatchAsync(byte kind, ReadOnlyMemory<byte> payload, int senderPid, string senderPath, long senderProcessStartTicks)
		{
			var snapshot = new TaskCompletionSource<CallbackRegistration[]>(TaskCreationOptions.RunContinuationsAsynchronously);

			// Registrations are read on the main thread, behind the work queued there, so a message that arrives while the
			// auto-execute section runs meets the callbacks that section registers.
			if (!script.MainEventScheduler.EnqueueCallback(() =>
			{
				lock (gate)
					snapshot.SetResult(disposed ? [] : [.. registrations]);
			}))
				return EmptyReply;

			object data = null, sender = null;

			foreach (var reg in await snapshot.Task.ConfigureAwait(false))
			{
				if (!reg.IsActive || reg.OwnerScheduler is not { IsDisposed: false } scheduler)
					continue;

				var completed = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
				var claimed = 0;

				void Skip()
				{
					if (Interlocked.CompareExchange(ref claimed, 1, 0) == 0)
						_ = completed.TrySetResult(null);
				}

				ScriptEventExecutionResult Run()
				{
					if (Volatile.Read(ref claimed) != 0)
						return ScriptEventExecutionResult.Dropped;

					if (!reg.IsActive)
					{
						Skip();
						return ScriptEventExecutionResult.Dropped;
					}

					var status = scheduler.TryExecuteThreadLaunch(0, false, false, tv =>
					{
						// Teardown can skip a queued callback, but a running callback must supply its own completion.
						if (Interlocked.CompareExchange(ref claimed, 1, 0) != 0)
							return;

						byte[] encoded = null;
						object callback = null;

						try
						{
							lock (gate)
							{
								if (disposed || !reg.IsActive)
									return;

								callback = reg.Callback;
							}

							if (data == null)
							{
								data = ScriptMessageCodec.Decode(kind, payload.Span);
								sender = new Ks.KeysharpScript(senderPid, senderPath, senderProcessStartTicks);
							}

							var result = Script.InvokeOrNull(callback, null, data, sender);

							try
							{
								encoded = ScriptMessageCodec.EncodeReply(result);
							}
							finally
							{
								GC.KeepAlive(result);
							}
						}
						catch (Exception ex) when (CallStack.RememberAndCatch(ex))
						{
							_ = Errors.ReportUncaught(ex);
						}
						finally
						{
							GC.KeepAlive(callback);
							_ = completed.TrySetResult(encoded);
						}
					}, ThreadKind.Message);

					if (status == ScriptEventExecutionResult.Dropped)
						Skip();

					return status;
				}

				using var pending = scheduler.RegisterPendingCallback(Skip);

				if (pending == null)
					continue;

				if (!scheduler.Enqueue(ScriptEventQueue.Normal, 0, Run))
					Skip();

				if (await completed.Task.ConfigureAwait(false) is { } reply)
					return reply;
			}

			return EmptyReply;
		}
	}
}
