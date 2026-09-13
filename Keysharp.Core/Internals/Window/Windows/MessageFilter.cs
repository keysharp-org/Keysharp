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
				// Only a message PreFilterMessage took off the queue carries a post time.
				object eventInfo = buffered ? WindowsAPI.GetMessageTime() : 0L;
				buffered = buffered && m.Msg > 0x0311;
				long hwnd = m.HWnd;
				hwnd = WindowsAPI.GetNonChildParent((nint)hwnd);
				object[] args = [m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg, m.HWnd.ToInt64()];

				if (buffered
						? monitor.TryExecuteBeforeDispatch(script, args, eventInfo, hwnd, out var reply)
						: monitor.TryExecuteEmergency(script, args, eventInfo, hwnd, out reply))
				{
					m.Result = (nint)reply;
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

			var claimed = CallEventHandlers(ref m, true);
			// Stash a message about to be dispatched so KeysharpForm.WndProc knows it was handled here. Like AHK's flag
			// around DispatchMessage it is set after the callbacks, which may pump or send messages of their own.
			handledMsg = claimed ? null : m;
			return claimed;
		}
	}
}
#endif
