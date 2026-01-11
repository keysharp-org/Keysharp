#if WINDOWS
namespace Keysharp.Core.Common.Window
{
	internal class MessageFilter : IMessageFilter
	{
		Script script;
		internal Message? handledMsg;
		internal MessageFilter(Script associatedScript)
		{
			script = associatedScript;
		}

		internal bool CallEventHandlers(ref Message m)
		{
			if (script.GuiData.onMessageHandlers.TryGetValue(m.Msg, out var monitor))
			{
				var ptv = script.Threads.CurrentThread;

				if (!script.Threads.AnyThreadsAvailable() || ptv.priority > 0)
					return false;

				if (monitor.instanceCount >= monitor.maxInstances)
					return false;

				monitor.instanceCount++;
				object res = null;
				object eventInfo = 0L;
				long hwnd = m.HWnd;
				hwnd = WindowsAPI.GetNonChildParent((nint)hwnd);

				if (handledMsg == m)
				{
					eventInfo = WindowsAPI.GetMessageTime();
				}

				try
				{
					// The following is a modified version of InvokeEventHandlers, because
					// we need to assign both hwndLastUsed and eventInfo some custom values.
					var handlers = monitor.funcs;
					object[] args = [m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg, m.HWnd.ToInt64()];
					if (handlers.Any())
					{
						var script = Script.TheScript;

						foreach (var handler in handlers)
						{
							if (handler != null)
							{
								var (pushed, tv) = script.Threads.BeginThread();
								if (pushed)//If we've exceeded the number of allowable threads, then just do nothing.
								{
									tv.eventInfo = eventInfo;
									tv.hwndLastUsed = (long)hwnd;
									_ = Flow.TryCatch(() =>
									{
										res = handler.Call(args);
										_ = script.Threads.EndThread((pushed, tv));
									}, true, (pushed, tv));//Pop on exception because EndThread() above won't be called.

									if (Script.ForceLong(res) != 0L)
										break;
								}
							}
						}
						script.ExitIfNotPersistent();
					}
				}
				finally
				{
					monitor.instanceCount--;
				}

				m.Result = (nint)Script.ForceLong(res);

				if (m.Result != 0)
					return true;
			}

			return false;
		}

		public bool PreFilterMessage(ref Message m)
		{
			if (m.HWnd != 0)
			{
				// Ignore IME windows and other helper forms
				var ctl = Control.FromHandle(m.HWnd);

				if (ctl == null || !(ctl.FindForm() is KeysharpForm))
					return false;
			}

			// Stash the message for later comparison in WndProc to determine whether it's already
			// been handled here. See more thorough description in KeysharpForm.cs WndProc.
			handledMsg = m;
			return CallEventHandlers(ref m);
		}
	}
}
#endif