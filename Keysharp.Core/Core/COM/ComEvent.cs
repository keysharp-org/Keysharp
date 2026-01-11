#if WINDOWS
using Antlr4.Runtime.Misc;
using Keysharp.Core.Common.Keyboard;

namespace Keysharp.Core.COM
{
	internal class ComEvent
	{
		internal Dispatcher dispatcher;
		internal KeysharpObject sinkObj;
		internal object[] thisArg;
		private readonly bool logAll;
		private readonly Dictionary<string, MethodPropertyHolder> methodMapper = new (10, StringComparer.OrdinalIgnoreCase);
		private readonly string prefix;

		internal ComEvent(Dispatcher disp, object sink, bool log)
		{
			var script = Script.TheScript;
			dispatcher = disp;
			thisArg = [disp.Co!];
			logAll = log;

			if (sink is string s)
			{
				prefix = s;

				foreach (var kv in script.ReflectionsData.stringToTypeLocalMethods)
				{
					if (string.Compare(kv.Key, "Main", true) != 0 &&
							string.Compare(kv.Key, AutoExecSectionName, true) != 0)
					{
						if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						{
							methodMapper[kv.Key.Remove(0, prefix.Length)] = kv.Value.First().Value.First().Value;
						}
					}
				}

				if (methodMapper.Count > 0)
					dispatcher.EventReceived += Dispatcher_EventReceivedGlobalFunc;
				else
					_ = KeysharpEnhancements.OutputDebugLine($"No suitable global methods were found with the prefix {prefix} which could be used as COM event handlers. No COM event handlers will be triggered.");
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
			if (prefix is null) return;
			if (logAll)
				_ = KeysharpEnhancements.OutputDebugLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to a global function with {e.Arguments.Length} + 1 args.");

			var thisObj = thisArg[0];

			if (thisObj != null && methodMapper.TryGetValue(e.Name, out var mph))
			{
				var args = e.Arguments.Concat(thisArg);
				TheScript.Threads.LaunchThreadInMain(() =>
				{
					e.IsHandled = true;
					e.Result = mph.CallFunc(null, args);
				});
			}
		}

		private void Dispatcher_EventReceivedObjectMethod(object sender, DispatcherEventArgs e)
		{
			e.IsHandled = false;
			if (sinkObj is null) return;
			if (logAll)
				_ = KeysharpEnhancements.OutputDebugLine($"Dispatch ID {e.DispId}: {e.Name} received to be dispatched to an object method with {e.Arguments.Length} + 1 args.");

			var (obj, target) = Script.GetMethodOrProperty(sinkObj, e.Name, -1, checkBase: true, throwIfMissing: false, invokeMeta: true);
			if (target == null) return;

			var allArgs = new object[e.Arguments.Length + 1];
			System.Array.Copy(e.Arguments, allArgs, e.Arguments.Length);
			allArgs[^1] = thisArg[0];

			TheScript.Threads.LaunchThreadInMain(() =>
			{
				e.IsHandled = true;
				e.Result = Script.Invoke(sinkObj, e.Name, allArgs);
			});
		}
	}
}

#endif