using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>Engine-side state for one <c>Script.OnMessage</c> subscription.</summary>
	internal sealed class ScriptMessageRegistration(object callback, ScriptEventScheduler ownerScheduler, ScriptMessageManager manager,
		Func<int, string, long, object> senderFactory)
		: EventSubscriptionBase(callback, ownerScheduler, manager.KeepsScriptRunning)
	{
		internal readonly ScriptMessageManager manager = manager;
		internal readonly Func<int, string, long, object> SenderFactory = senderFactory;

		internal override void Unregister() => manager.Unregister(this);

		internal override void Register() => manager.Register(this);
	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Script.OnMessage</c>. Messages arrive through the script's
	/// <see cref="ScriptEndpoint"/>, which listens whether or not a hook exists, so there is no native source to manage.
	/// <para>
	/// Unlike other hooks, a callback's return value matters: it is the reply. So a message visits the hooks one after
	/// another in start order, each on its own thread, until one returns a value other than an empty string, as
	/// AutoHotkey calls <c>OnMessage</c> callbacks. Hooks do not keep the script running, as <c>OnMessage</c> does not.
	/// </para>
	/// </summary>
	internal sealed class ScriptMessageManager(Script script)
		: EventManagerBase<ScriptMessageRegistration, IDisposable, NoPayload>(script, false)
	{
		private static readonly byte[] EmptyReply = [ScriptEndpoint.Empty];

		protected override ThreadKind CallbackThreadKind => ThreadKind.Message;

		protected override IDisposable CreateBackend() => null;

		protected override void SyncNativeLocked() { }

		/// <summary>Delivers one message and returns the reply frame: its kind, then its bytes.</summary>
		internal async Task<byte[]> DispatchAsync(byte kind, ReadOnlyMemory<byte> payload, int senderPid, string senderPath, long senderProcessStartTicks)
		{
			var snapshot = new TaskCompletionSource<ScriptMessageRegistration[]>(TaskCreationOptions.RunContinuationsAsynchronously);

			// The hooks are read on the main thread, behind the work queued there, so a message that arrives while the
			// auto-execute section runs meets the hooks that section registers.
			if (!script.MainEventScheduler.EnqueueCallback(() =>
			{
				lock (gate)
					snapshot.SetResult(disposed ? [] : [.. registrations]);
			}))
				return EmptyReply;

			object data = null, sender = null;

			foreach (var reg in await snapshot.Task.ConfigureAwait(false))
			{
				if (!reg.IsRunning || reg.OwnerScheduler is not { IsDisposed: false } scheduler)
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

					if (!reg.IsRunning)
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
						object callback = null, hook = null;

						try
						{
							lock (gate)
							{
								if (disposed || !reg.IsRunning)
									return;

								callback = reg.Callback;
								hook = reg.scriptObject;
							}

							if (data == null)
							{
								data = ScriptMessageCodec.Decode(kind, payload.Span);
								sender = reg.SenderFactory(senderPid, senderPath, senderProcessStartTicks);
							}

							var result = Script.InvokeOrNull(callback, null, hook, data, sender);

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
							GC.KeepAlive(hook);
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
