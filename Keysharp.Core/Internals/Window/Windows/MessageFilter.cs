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
			if (script.trayMessageWindow is { } trayWindow && trayWindow.IsTrayMessage(m))
			{
				var forwarded = Message.Create(script.MainWindowHandle, (int)UserMessages.AHK_NOTIFYICON,
					(nint)UserMessages.AHK_NOTIFYICON, m.LParam);
				var claimed = CallEventHandlers(ref forwarded, buffered);
				m.Result = forwarded.Result;
				return claimed || trayWindow.Handle == 0 || script.IsDisposed || script.hasExited;
			}

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

		internal bool MessageClaimed(ref Message m, bool invokeHandlers = true)
		{
			if (handledMsg == m)
			{
				handledMsg = null;
				return false;
			}

			return invokeHandlers && CallEventHandlers(ref m);
		}

		public bool PreFilterMessage(ref Message m)
		{
			var isTrayMessage = script.trayMessageWindow?.IsTrayMessage(m) == true;

			// Most messages have no monitor, which is cheaper to rule out than the window's ownership.
			if (!isTrayMessage && !script.GuiData.onMessageHandlers.ContainsKey(m.Msg))
			{
				handledMsg = null;
				return false;
			}

			if (m.HWnd != 0 && !isTrayMessage)
			{
				// Ignore IME windows and other helper forms
				var ctl = Control.FromHandle(m.HWnd);

				if (ctl == null || !(ctl.FindForm() is KeysharpForm))
					return false;
			}

			var claimed = CallEventHandlers(ref m, true);
			// Stash a message about to be dispatched so its window procedure knows it was handled here. Like AHK's flag
			// around DispatchMessage it is set after the callbacks, which may pump or send messages of their own.
			handledMsg = claimed ? null : m;
			return claimed;
		}
	}

	internal sealed class TrayMessageWindow : NativeWindow
	{
		private const int NotifyIconMessage = 0x0800;
		// NotifyIcon is sealed and exposes no HWND or message hook. Its window stays alive until disposal.
		private static readonly FieldInfo windowField = typeof(NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
		private readonly Script script;

		internal TrayMessageWindow(Script script, NotifyIcon icon)
		{
			this.script = script;
			if (windowField?.GetValue(icon) is not NativeWindow window)
				throw new InvalidOperationException("Cannot access the WinForms tray notification window.");

			if (window.Handle == 0)
				window.CreateHandle(new CreateParams());

			AssignHandle(window.Handle);
		}

		internal bool IsTrayMessage(Message message) => Handle != 0 && message.Msg == NotifyIconMessage && message.HWnd == Handle;

		internal void DispatchNotification(nint notification)
		{
			if (Handle == 0 || script.IsDisposed || script.hasExited)
				return;

			var message = Message.Create(Handle, NotifyIconMessage, (nint)UserMessages.AHK_NOTIFYICON, notification);
			base.WndProc(ref message);
		}

		protected override void WndProc(ref Message message)
		{
			if (IsTrayMessage(message)
				&& (script.msgFilter.MessageClaimed(ref message) || Handle == 0 || script.IsDisposed || script.hasExited))
				return;

			base.WndProc(ref message);
		}
	}
}
#endif
