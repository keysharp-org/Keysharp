#if WINDOWS
using Keysharp.Runtime.Keyboard;

namespace Keysharp.Builtins.COM
{
	internal class ComEvent : IDisposable
	{
		private readonly Script owner;
		// The scheduler of the thread which connected the sink, which runs its events.
		private readonly ScriptEventScheduler ownerScheduler;
		internal Dispatcher dispatcher;
		internal KeysharpObject sinkObj;
		private volatile bool disconnected;
		private readonly bool logAll;
		private readonly Dictionary<string, KeysharpFunc> methodMapper = new (10, StringComparer.OrdinalIgnoreCase);
		private readonly string prefix;

		internal ComEvent(Script owner, Dispatcher disp, object sink, bool log)
		{
			this.owner = owner;
			ownerScheduler = owner.EventScheduler;
			dispatcher = disp;
			logAll = log;

			if (sink is string s)
			{
				prefix = s;

				if (!owner.ReflectionsData.typeToStringStaticMethods.ContainsKey(owner.CurrentModuleType))
					Reflections.FindAndCacheMethod(owner.CurrentModuleType, "", -1);

				foreach (var kv in owner.ReflectionsData.typeToStringStaticMethods[owner.CurrentModuleType])
				{
					if (string.Equals(kv.Key, AutoExecSectionName, StringComparison.OrdinalIgnoreCase))
						continue;

					if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Functions.MethodFunction(kv.Value.First().Value.mi) is { } fn)
						methodMapper[kv.Key.Remove(0, prefix.Length)] = fn;
				}

				if (methodMapper.Count > 0)
					dispatcher.EventReceived += Dispatcher_EventReceivedGlobalFunc;
				else
					_ = Diagnostics.Debug.WriteLine($"No suitable global methods were found with the prefix {prefix} which could be used as COM event handlers. No COM event handlers will be triggered.");
			}
			else if (sink is KeysharpObject ko)
			{
				sinkObj = ko;
				dispatcher.EventReceived += Dispatcher_EventReceivedObjectMethod;
			}
			else
				_ = Errors.ValueErrorOccurred($"The passed in sink object of type {sink.GetType()} was not either a string or a Keysharp object.");
		}

		public void Dispose()
		{
			if (disconnected) return;
			disconnected = true;
			dispatcher.EventReceived -= Dispatcher_EventReceivedGlobalFunc;
			dispatcher.EventReceived -= Dispatcher_EventReceivedObjectMethod;
			sinkObj = null;
			dispatcher.Dispose();
		}

		private static object[] EventArguments(DispatcherEventArgs e, ComValue source) => [..e.Arguments, source];

		private void Dispatcher_EventReceivedGlobalFunc(object sender, DispatcherEventArgs e)
		{
			if (disconnected || owner.IsDisposed || owner.hasExited)
				return;

			if (prefix is null) return;
			if (logAll)
				_ = Diagnostics.Debug.WriteLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to a global function with {e.Arguments.Length} + 1 args.");

			var thisObj = dispatcher.Co;

			if (thisObj != null && methodMapper.TryGetValue(e.Name, out var fn))
			{
				var args = EventArguments(e, thisObj);
				Raise(e, () => fn.Call(args));
			}
		}

		private void Dispatcher_EventReceivedObjectMethod(object sender, DispatcherEventArgs e)
		{
			if (disconnected || owner.IsDisposed || owner.hasExited)
				return;

			e.IsHandled = false;
			var sink = sinkObj;
			if (sink is null) return;
			if (logAll)
				_ = Diagnostics.Debug.WriteLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to an object method with {e.Arguments.Length} + 1 args.");

			var source = dispatcher.Co;
			if (source == null) return;
			var allArgs = EventArguments(e, source);
			Raise(e, () =>
			{
				if (!Script.TryInvoke(sink, e.Name, allArgs, out var result))
					e.IsHandled = false;
				return result;
			});
		}

		// Owning-thread events return their result and error to COM; foreign-thread events queue a new pseudo-thread.
		private void Raise(DispatcherEventArgs e, Func<object> handler)
		{
			if (ownerScheduler.OwnsCurrentThread)
			{
				var exit = new Threads.ExitState(Threads.Current);
				using var caught = Keysharp.Runtime.Flow.EnterTry();
				e.IsHandled = true;
				// Entered from a message, so as with a callback the handler starts with a fresh peek interval.
				owner.RecordMessageCheck();

				try
				{
					e.Result = handler();
				}
				// Exit ends only the handler, which AutoHotkey reports to the raiser as success; ExitApp goes on unwinding.
				catch (Exception ex) when (!owner.hasExited && Keysharp.Internals.Flow.TryGetException<Flow.UserRequestedExitException>(ex, out _))
				{
					exit.Restore();
				}

				return;
			}

			if (ownerScheduler.IsDisposed)
				return;

			_ = ownerScheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
			{
				// A sink disconnected while its event waited gets none.
				if (disconnected)
					return ScriptEventExecutionResult.Dropped;

				using var thread = ownerScheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Com);

				if (!thread.Started)
					return thread.Result;

				try
				{
					_ = handler();
				}
				catch (Exception ex) when (CallStack.RememberAndCatch(ex))
				{
					_ = Errors.ReportUncaught(ex);
				}

				return ScriptEventExecutionResult.Executed;
			});
		}
	}
}

#endif
