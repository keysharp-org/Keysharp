#if WINDOWS
using Keysharp.Runtime.Keyboard;

namespace Keysharp.Builtins.COM
{
	internal class ComEvent
	{
		private readonly Script owner;
		// The scheduler of the thread which connected the sink, which runs its events.
		private readonly ScriptEventScheduler ownerScheduler;
		internal Dispatcher dispatcher;
		internal KeysharpObject sinkObj;
		internal object[] thisArg;
		private readonly bool logAll;
		private readonly Dictionary<string, KeysharpFunc> methodMapper = new (10, StringComparer.OrdinalIgnoreCase);
		private readonly string prefix;

		internal ComEvent(Script owner, Dispatcher disp, object sink, bool log)
		{
			this.owner = owner;
			ownerScheduler = owner.EventScheduler;
			dispatcher = disp;
			thisArg = [disp.Co!];
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

		public void Unwire()
		{
			thisArg[0] = null;
			dispatcher.EventReceived -= Dispatcher_EventReceivedGlobalFunc;
			dispatcher.EventReceived -= Dispatcher_EventReceivedObjectMethod;
		}

		private static void FixArgs(object[] args)
		{
			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];

				if (arg is long || arg is double || arg is string)//The most likely cases.
					continue;

				if (arg is int ii)
					args[i] = (long)ii;
				else if (arg is uint ui)
					args[i] = (long)ui;
				else if (arg is float f)
					args[i] = (double)f;
				else if (arg is short s)
					args[i] = (long)s;
				else if (arg is ushort us)
					args[i] = (long)us;
				else if (arg is char c)
					args[i] = (long)c;
				else if (arg is byte b)
					args[i] = (long)b;
				else if (arg is nint ip)
					args[i] = ip.ToInt64();
				else if (Marshal.IsComObject(arg))
				{
					if (arg is IDispatch)
					{
						var punk = Marshal.GetIDispatchForObject(arg);
						args[i] =  new ComObject()
						{
							vt = VarEnum.VT_DISPATCH,
							Ptr = punk
						};
					}
					else
					{
						var punk = Marshal.GetIUnknownForObject(arg);
						args[i] = new ComValue()
						{
							vt = VarEnum.VT_UNKNOWN,
							Ptr = punk
						};
					}

					Marshal.ReleaseComObject(arg);
				}
			}
		}

		private void Dispatcher_EventReceivedGlobalFunc(object sender, DispatcherEventArgs e)
		{
			if (owner.IsDisposed || owner.hasExited)
				return;

			if (prefix is null) return;
			if (logAll)
				_ = Diagnostics.Debug.WriteLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to a global function with {e.Arguments.Length} + 1 args.");

			var thisObj = thisArg[0];

			if (thisObj != null && methodMapper.TryGetValue(e.Name, out var fn))
			{
				var args = e.Arguments.Concat(thisArg);
				Raise(e, () => fn.Call(args));
			}
		}

		private void Dispatcher_EventReceivedObjectMethod(object sender, DispatcherEventArgs e)
		{
			if (owner.IsDisposed || owner.hasExited)
				return;

			e.IsHandled = false;
			if (sinkObj is null) return;
			if (logAll)
				_ = Diagnostics.Debug.WriteLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to an object method with {e.Arguments.Length} + 1 args.");

			var (obj, target) = Script.GetMethodOrProperty(sinkObj, e.Name, -1, checkBase: true, throwIfMissing: false, invokeMeta: true);
			if (target == null) return;

			var allArgs = new object[e.Arguments.Length + 1];
			System.Array.Copy(e.Arguments, allArgs, e.Arguments.Length);
			allArgs[^1] = thisArg[0];
			Raise(e, () => Script.Invoke(sinkObj, e.Name, allArgs));
		}

		/// <summary>
		/// As for CLR events (ClrEventRegistration.Dispatch): on the owning thread the handler runs inside the raiser's call,
		/// which gets its result and any error, as in AutoHotkey, so it can read state that lives only for the call, such as
		/// window.event; from any other thread it is queued there as a new pseudo-thread.
		/// </summary>
		private void Raise(DispatcherEventArgs e, Func<object> handler)
		{
			if (ownerScheduler.OwnsCurrentThread)
			{
				var exit = new Threads.ExitState(Threads.Current);
				using var caught = Keysharp.Runtime.Flow.EnterTry();
				e.IsHandled = true;

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
				if (thisArg[0] == null)
					return ScriptEventExecutionResult.Dropped;

				using var thread = ownerScheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Com);

				if (!thread.Started)
					return thread.Result;

				try
				{
					_ = handler();
				}
				catch (Exception ex)
				{
					_ = Errors.ReportUncaught(ex);
				}

				return ScriptEventExecutionResult.Executed;
			});
		}
	}
}

#endif
