using Keysharp.Builtins;
#if WINDOWS
namespace Keysharp.Internals.Window.Windows
{
	internal class MessageFilter : IMessageFilter
	{
		Script script;
		internal Message? handledMsg;
		internal MessageFilter(Script associatedScript)
		{
			script = associatedScript;
		}

		internal bool CallEventHandlers(ref Message m, bool buffered = false)
		{
			if (script.IsDisposed)
				return false;

			if (script.GuiData.onMessageHandlers.TryGetValue(m.Msg, out var monitor))
			{
				buffered = buffered && m.Msg > 0x0311;
				object eventInfo = 0L;
				long hwnd = m.HWnd;
				hwnd = WindowsAPI.GetNonChildParent((nint)hwnd);

				if (handledMsg == m)
				{
					eventInfo = WindowsAPI.GetMessageTime();
				}

				object[] args = [m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg, m.HWnd.ToInt64()];

				if (buffered)
				{
					foreach (var registration in monitor.GetRegistrationsSnapshot())
					{
						var targetScheduler = registration.OwnerScheduler;
						var queuedEvent = new MsgMonitorExtensions.BufferedMessageQueuedEvent(registration, script, args, eventInfo, hwnd);
						targetScheduler.Enqueue(ScriptEventQueue.Normal, 0, queuedEvent.Execute);
					}
				}
				else if (monitor.TryExecuteEmergency(script, args, eventInfo, hwnd, out var result))
				{
					m.Result = (nint)result;

					if (m.Result != 0)
						return true;
				}
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
			return CallEventHandlers(ref m, true);
		}
	}
}
#endif
