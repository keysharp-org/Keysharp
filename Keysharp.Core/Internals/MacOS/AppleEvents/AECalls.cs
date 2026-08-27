#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>One outgoing Apple event, described in script terms so that every descriptor is built, sent and
	/// read on the one thread that owns sending.</summary>
	internal sealed class AECallRequest
	{
		internal AETarget Target;
		internal uint EventClass;
		internal uint EventId;
		internal AEContext Context;
		internal int TimeoutMs = AECalls.DefaultTimeoutMs;

		/// <summary>The direct parameter as an object specifier, which is what get, set and count address.</summary>
		internal IReadOnlyList<AESpecifierStep> DirectSpecifier;

		internal bool HasDirectValue;
		internal object DirectValue;
		internal string DirectTypeName;

		internal List<(uint Keyword, object Value, string TypeName)> Parameters;
	}

	/// <summary>
	/// Sends Apple events. Every send happens on one dedicated thread: a reply comes back to whichever thread sent
	/// the event, and sending from the main thread would both risk reentrancy and stall timers, hotkeys and the
	/// GUI for as long as the other application takes to answer. The script thread instead waits on the result
	/// while pumping, exactly as the D-Bus layer does on Linux.
	/// </summary>
	internal static class AECalls
	{
		/// <summary>Matches the Linux backend rather than the one minute Apple events conventionally use, so a
		/// hung peer behaves the same way on both platforms.</summary>
		internal const int DefaultTimeoutMs = 25_000;

		/// <summary>Targets already known to be permitted. A refusal is not cached, so granting permission and
		/// trying again works without restarting the script.</summary>
		private static readonly ConcurrentDictionary<string, bool> permitted = new (StringComparer.Ordinal);

		private static readonly BlockingCollection<Action> queue = new ();
		private static readonly Lock workerGate = new ();
		private static Thread worker;

		// ---- the public surface ------------------------------------------------------------------

		/// <summary>Reads a property or an element, which on the wire is a get event addressed at a specifier.</summary>
		internal static object GetData(AETarget target, IReadOnlyList<AESpecifierStep> specifier, AEContext context, int timeoutMs = DefaultTimeoutMs)
			=> Send(new AECallRequest
		{
			Target = target,
			EventClass = AE.CoreSuite,
			EventId = AE.EventGetData,
			DirectSpecifier = specifier,
			Context = context,
			TimeoutMs = timeoutMs
		});

		internal static void SetData(AETarget target, IReadOnlyList<AESpecifierStep> specifier, object value,
									 string typeName, AEContext context, int timeoutMs = DefaultTimeoutMs)
			=> _ = Send(new AECallRequest
		{
			Target = target,
			EventClass = AE.CoreSuite,
			EventId = AE.EventSetData,
			DirectSpecifier = specifier,
			Parameters = [(AE.KeyAEData, value, typeName)],
			Context = context,
			TimeoutMs = timeoutMs
		});

		internal static long CountElements(AETarget target, IReadOnlyList<AESpecifierStep> container, uint classCode,
										   AEContext context, int timeoutMs = DefaultTimeoutMs)
		{
			var result = Send(new AECallRequest
			{
				Target = target,
				EventClass = AE.CoreSuite,
				EventId = AE.EventCountElements,
				DirectSpecifier = container,
				Parameters = [(AE.KeyAEObjectClass, new ComValue("type", AEFourCharCode.Unpack(classCode)), null)],
				Context = context,
				TimeoutMs = timeoutMs
			});
			return result?.Al() ?? 0L;
		}

		/// <summary>
		/// Sends one event and returns what the reply's direct object holds. Blocks the calling thread only in the
		/// sense that it pumps: other pseudo-threads keep running while the other application works.
		/// </summary>
		internal static object Send(AECallRequest request)
		{
			EnsurePermitted(request.Target);
			EnsureWorker();
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

			queue.Add(() =>
			{
				try
				{
					completion.SetResult(Execute(request));
				}
				catch (Exception ex)
				{
					completion.SetException(ex);
				}
			});
			// The native send has its own deadline; the outer wait allows a little longer so the inner timeout is
			// the one that fires and the error names the application rather than the plumbing.
			var task = (Task)completion.Task;
			bool completed;

			try
			{
				completed = task.WaitInterruptible(request.TimeoutMs + 5_000);
			}
			catch (AggregateException ae) when (ae.InnerException != null)
			{
				throw ae.InnerException;
			}

			if (!completed)
				throw new AEException(AE.ErrAETimeout, $"{request.Target} did not answer within {request.TimeoutMs} ms.");

			return completion.Task.GetAwaiter().GetResult();
		}

		// ---- the sending thread -------------------------------------------------------------------

		private static void EnsureWorker()
		{
			if (worker != null)
				return;

			lock (workerGate)
			{
				if (worker != null)
					return;

				worker = new Thread(Pump)
				{
					IsBackground = true,
					Name = "Keysharp Apple Events"
				};
				worker.Start();
			}
		}

		private static void Pump()
		{
			foreach (var work in queue.GetConsumingEnumerable())
			{
				try
				{
					work();
				}
				catch (Exception ex)
				{
					// Execute already reports failures through its completion source; anything reaching here would
					// otherwise take the whole process down with it.
					Diagnostics.Debug.WriteLine($"Apple event dispatch failed: {ex.Message}");
				}
			}
		}

		private static object Execute(AECallRequest request)
		{
			using var address = request.Target.MakeAddress();
			using var @event = AE.NewEvent(request.EventClass, request.EventId, address);

			if (request.DirectSpecifier != null)
			{
				using var specifier = AESpecifiers.Build(request.DirectSpecifier);
				AE.PutParam(@event, AE.KeyDirectObject, specifier);
			}
			else if (request.HasDirectValue)
			{
				using var direct = AEMarshal.ToDescriptor(request.DirectValue, request.Context, request.DirectTypeName);
				AE.PutParam(@event, AE.KeyDirectObject, direct);
			}

			if (request.Parameters != null)
				foreach (var (keyword, value, typeName) in request.Parameters)
				{
					using var parameter = AEMarshal.ToDescriptor(value, request.Context, typeName);
					AE.PutParam(@event, keyword, parameter);
				}

			// Apple event timeouts are counted in sixtieths of a second, not milliseconds.
			var ticks = (nint)Math.Max(1, (long)request.TimeoutMs * 60 / 1000);
			var status = AE.AESendMessage(ref @event.Desc, out var replyDesc, AE.KAEWaitReply | AE.KAECanInteract, ticks);

			// Checked before the reply is wrapped: a failed send leaves nothing worth disposing, and handing the
			// descriptor to a disposer on that path would be disposing whatever the call left behind.
			if (status != 0)
				throw new AEException(status, $"{request.Target}: {AE.DescribeStatus(status, "The Apple event")}");

			using var reply = new AEValue(replyDesc);

			ThrowIfErrorReply(ref reply.Desc, request);

			if (!AE.TryGetParam(ref reply.Desc, AE.KeyDirectObject, out var result))
				return "";

			using (result)
				return AEMarshal.FromDescriptor(ref result.Desc, request.Context);
		}

		/// <summary>
		/// An application reports failure inside the reply rather than through the send status, so the reply has to
		/// be inspected even when the send itself succeeded.
		/// </summary>
		private static void ThrowIfErrorReply(ref AEDesc reply, AECallRequest request)
		{
			if (!AE.TryGetParam(ref reply, AE.KeyErrorNumber, out var numberDesc))
				return;

			long number;

			using (numberDesc)
				number = AE.GetInt64(ref numberDesc.Desc);

			if (number == 0)
				return;

			var message = "";

			if (AE.TryGetParam(ref reply, AE.KeyErrorString, out var textDesc))
				using (textDesc)
					message = AE.GetString(ref textDesc.Desc);

			if (string.IsNullOrEmpty(message))
				message = AE.DescribeStatus((int)number, "The command");

			throw new AEException((int)number, $"{request.Target}: {message}");
		}

		/// <summary>
		/// Asks the system whether this process may control the target, which is what makes the consent prompt
		/// appear attributed to Keysharp with its stated reason rather than as a bare refusal at the first send.
		/// The check is by application, not by event, so it uses the wildcard.
		/// <para>
		/// Deliberately not on the sending thread, and deliberately not on a deadline: the prompt is answered by a
		/// person, and asking on the one thread that owns sending would hold up events to every other application
		/// until they did. The script thread waits by pumping, so it stays responsive meanwhile.
		/// </para>
		/// </summary>
		private static void EnsurePermitted(AETarget target)
		{
			if (permitted.ContainsKey(target.CacheKey))
				return;

			var task = Task.Run(() =>
			{
				using var address = target.MakeAddress();
				return AE.AEDeterminePermissionToAutomateTarget(ref address.Desc, AE.TypeWildCard, AE.TypeWildCard, 1);
			});

			try
			{
				task.WaitInterruptible();
			}
			catch (AggregateException ae) when (ae.InnerException != null)
			{
				throw ae.InnerException;
			}

			var status = task.GetAwaiter().GetResult();

			if (status != 0)
				throw new AEException(status, $"{target}: {AE.DescribeStatus(status, "Automation permission")}");

			_ = permitted.TryAdd(target.CacheKey, true);
		}
	}
}
#endif
