#if WINDOWS
using Keysharp.Runtime.Keyboard;

namespace Keysharp.Builtins.COM
{
	internal class ComEvent
	{
		private readonly Script owner;
		internal Dispatcher dispatcher;
		internal KeysharpObject sinkObj;
		internal object[] thisArg;
		private readonly bool logAll;
		private readonly Dictionary<string, MethodPropertyHolder> methodMapper = new (10, StringComparer.OrdinalIgnoreCase);
		private readonly string prefix;

		internal ComEvent(Script owner, Dispatcher disp, object sink, bool log)
		{
			this.owner = owner;
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

					if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						methodMapper[kv.Key.Remove(0, prefix.Length)] = kv.Value.First().Value;
					}
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

			if (thisObj != null && methodMapper.TryGetValue(e.Name, out var mph))
			{
				var args = e.Arguments.Concat(thisArg);
				var moduleType = ResolveModuleType(mph.mi?.DeclaringType);
				owner.Threads.LaunchThreadInMain(() =>
				{
					_ = Keysharp.Internals.Flow.TryCatch(() =>
					{
						e.IsHandled = true;
						if (moduleType != null)
						{
							var prev = owner.CurrentModuleType;
							owner.CurrentModuleType = moduleType;
							try
							{
								e.Result = mph.CallFunc(null, args);
							}
							finally
							{
								owner.CurrentModuleType = prev;
							}
						}
						else
						{
							e.Result = mph.CallFunc(null, args);
						}
					});
				}, kind: ThreadKind.Com);
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

			owner.Threads.LaunchThreadInMain(() =>
			{
				_ = Keysharp.Internals.Flow.TryCatch(() =>
				{
					e.IsHandled = true;
					e.Result = Script.Invoke(sinkObj, e.Name, allArgs);
				});
			}, kind: ThreadKind.Com);
		}

		private static Type ResolveModuleType(Type type)
		{
			for (var t = type; t != null; t = t.DeclaringType)
			{
				if (typeof(Keysharp.Runtime.Module).IsAssignableFrom(t))
					return t;
			}

			return null;
		}
	}
}

#endif
