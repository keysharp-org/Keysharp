#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>One outgoing Apple event, described in script terms. Its descriptors are built on the script thread,
	/// since that converts script values. Native sending happens on the target's worker; replies are decoded
	/// on the waiting script thread.</summary>
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
	/// Sends Apple events in order per target, on workers which exit when their queues empty.
	/// The script thread waits while pumping, so a slow target does not stall other applications.
	/// </summary>
	internal static class AECalls
	{
		/// <summary>Matches the Linux backend rather than the one minute Apple events conventionally use, so a
		/// hung peer behaves the same way on both platforms.</summary>
		internal const int DefaultTimeoutMs = 25_000;

		/// <summary>Targets already known to be permitted. A refusal is not cached, so granting permission and
		/// trying again works without restarting the script.</summary>
		private static readonly ConcurrentDictionary<string, bool> permitted = new (StringComparer.Ordinal);

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
			_ = result.TryCoerceLong(out var count);
			return count;
		}

		/// <summary>
		/// Sends one event and returns what the reply's direct object holds. Blocks the calling thread only in the
		/// sense that it pumps: other pseudo-threads keep running while the other application works.
		/// </summary>
		internal static object Send(AECallRequest request)
		{
			var permissionStatus = GetPermissionStatus(request.Target, true);
			if (permissionStatus != 0)
				throw new AEException(permissionStatus, $"{request.Target}: {AE.DescribeStatus(permissionStatus, "Automation permission")}");

			// Converting a value can raise an error or run a script's ToString method, so it happens here, on the
			// script thread. After a continued error nothing is sent.
			if (BuildParameters(request) is not { } parameters)
				return Script.DefaultObject;

			var completion = new TaskCompletionSource<AEValue>(TaskCreationOptions.RunContinuationsAsynchronously);
			var state = 0; // Pending, started, or cancelled; only pending sends can be cancelled.

			// The sending thread disposes the descriptors, since a timed-out wait below returns while it may still
			// be using them.
			try
			{
				var pid = AETargets.RunningPid(request.Target);
				var targetKey = pid != 0 ? $"pid:{pid}" : request.Target.CacheKey;
				AETargetQueue.Enqueue(targetKey, () =>
				{
					try
					{
						if (Interlocked.CompareExchange(ref state, 1, 0) == 0)
							completion.SetResult(Execute(request, parameters));
						else
							completion.SetCanceled();
					}
					catch (Exception ex)
					{
						completion.SetException(ex);
					}
					finally
					{
						Dispose(parameters);
					}
				});
			}
			catch
			{
				Dispose(parameters);
				throw;
			}
			// The native send has its own deadline; the outer wait allows a little longer so the inner timeout is
			// the one that fires and the error names the application rather than the plumbing.
			var task = completion.Task;
			var replyConsumed = false;

			try
			{
				if (!task.WaitInterruptible(request.TimeoutMs + 5_000))
					throw new AEException(AE.ErrAETimeout, $"{request.Target} did not answer within {request.TimeoutMs} ms.");

				using var reply = task.GetAwaiter().GetResult();
				replyConsumed = true;
				return ReadReply(reply, request);
			}
			catch (AggregateException ae) when (ae.InnerException != null)
			{
				throw ae.InnerException;
			}
			finally
			{
				if (!replyConsumed)
				{
					_ = Interlocked.CompareExchange(ref state, 2, 0);
					_ = task.ContinueWith(static completed =>
					{
						if (completed.IsCompletedSuccessfully)
							completed.Result.Dispose();
						else
							_ = completed.Exception;
					}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
				}
			}
		}

		/// <summary>
		/// The event's parameters, the direct object among them, or null when a conversion raised an error the script
		/// continued. A builder returns a null descriptor for that.
		/// </summary>
		private static List<(uint Keyword, AEValue Value)> BuildParameters(AECallRequest request)
		{
			var parameters = new List<(uint Keyword, AEValue Value)>();
			var built = false;

			try
			{
				var direct = request.DirectSpecifier != null ? AESpecifiers.Build(request.DirectSpecifier)
							 : request.HasDirectValue ? AEMarshal.ToDescriptor(request.DirectValue, request.Context, request.DirectTypeName)
							 : null;

				if (direct != null)
					parameters.Add((AE.KeyDirectObject, direct));
				else if (request.DirectSpecifier != null || request.HasDirectValue)
					return null;

				if (request.Parameters != null)
					foreach (var (keyword, value, typeName) in request.Parameters)
					{
						if (AEMarshal.ToDescriptor(value, request.Context, typeName) is not { } parameter)
							return null;

						parameters.Add((keyword, parameter));
					}

				built = true;
				return parameters;
			}
			finally
			{
				if (!built)
					Dispose(parameters);
			}
		}

		private static void Dispose(List<(uint Keyword, AEValue Value)> parameters)
		{
			foreach (var (_, value) in parameters)
				value.Dispose();
		}

		private static AEValue Execute(AECallRequest request, List<(uint Keyword, AEValue Value)> parameters)
		{
			using var address = request.Target.MakeAddress();
			using var @event = AE.NewEvent(request.EventClass, request.EventId, address);

			foreach (var (keyword, value) in parameters)
				AE.PutParam(@event, keyword, value);

			// Apple event timeouts are counted in sixtieths of a second, not milliseconds.
			var ticks = (nint)Math.Max(1, (long)request.TimeoutMs * 60 / 1000);
			var status = AE.SendOnWorker(ref @event.Desc, out var replyDesc, AE.KAEWaitReply | AE.KAECanInteract, ticks);

			// Checked before the reply is wrapped: a failed send leaves nothing worth disposing, and handing the
			// descriptor to a disposer on that path would be disposing whatever the call left behind.
			if (status != 0)
				throw new AEException(status, $"{request.Target}: {AE.DescribeStatus(status, "The Apple event")}");

			return new AEValue(replyDesc);
		}

		private static object ReadReply(AEValue reply, AECallRequest request)
		{
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
		/// Checks per-application Automation permission off the UI and sending threads.
		/// A requested consent prompt has no deadline; the caller waits while pumping.
		/// </summary>
		internal static bool EnsurePermitted(AETarget target, bool requestPrompt = true)
			=> GetPermissionStatus(target, requestPrompt) == 0;

		private static int GetPermissionStatus(AETarget target, bool requestPrompt)
		{
			if (permitted.ContainsKey(target.CacheKey))
				return 0;

			var task = Task.Run(() =>
			{
				using var address = target.MakeAddress();
				return AE.AEDeterminePermissionToAutomateTarget(ref address.Desc, AE.TypeWildCard, AE.TypeWildCard, requestPrompt ? (byte)1 : (byte)0);
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
			{
				Diagnostics.Debug.WriteLine($"{target}: {AE.DescribeStatus(status, "Automation permission")}");
				return status;
			}

			_ = permitted.TryAdd(target.CacheKey, true);
			return 0;
		}
	}
}
#endif
