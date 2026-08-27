using Keysharp.Builtins;
#if !WINDOWS
using Keysharp.Internals.Os.Windows;

namespace Keysharp.Internals.Window.Unix
{
	internal struct Message
	{
		public nint HWnd;
		public int Msg;
		public nint WParam;
		public nint LParam;
		public nint Result;
	}

	/// <summary>
	/// The global OnMessage() monitors' sink. Off Windows the messages reaching it are synthesized by
	/// <see cref="EtoMessageSource"/> rather than taken off a native queue.
	/// </summary>
	internal class MessageFilter
	{
		private readonly Script script;
		internal Message handledMsg;

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
				object eventInfo = 0L;
				//The monitor's thread takes this as its last-found window, and AHK makes that the top-level
				//WINDOW even when the message went to a control (the control's own handle is still passed as the
				//callback's fourth argument). Scripts compare it against a GUI's Hwnd to tell which of their
				//windows a click landed in, which a control handle would never match.
				long hwnd = TopLevelOf(m.HWnd);

				if (MessagesEqual(handledMsg, m))
				{
					eventInfo = A_TickCount;
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

		/// <summary>
		/// The handle of the window a control belongs to, or the handle itself when it is already a window or
		/// belongs to no GUI of ours. Stands in for Win32's GetNonChildParent.
		/// </summary>
		private static nint TopLevelOf(nint handle)
		{
			if (Forms.Control.FromHandle(handle) is not Forms.Control control)
				return handle;

			for (var parent = control; parent != null; parent = parent.Parent)
				if (parent is KeysharpForm form)
					return form.Handle;

			return handle;
		}

		private static bool MessagesEqual(Message left, Message right)
			=> left.HWnd == right.HWnd
				&& left.Msg == right.Msg
				&& left.WParam == right.WParam
				&& left.LParam == right.LParam
				&& left.Result == right.Result;
	}
}
#endif
