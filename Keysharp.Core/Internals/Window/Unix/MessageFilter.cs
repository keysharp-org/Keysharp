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

		internal MessageFilter(Script associatedScript)
		{
			script = associatedScript;
		}

		/// <summary>
		/// Runs the monitors inline, since a queued monitor returns too late to claim the message. A_EventInfo is
		/// the tick count, as for a message the Windows pre-filter took off the queue.
		/// </summary>
		internal bool CallEventHandlers(ref Message m)
		{
			if (script.IsDisposed || !script.GuiData.onMessageHandlers.TryGetValue(m.Msg, out var monitor))
				return false;

			//The monitor's thread takes this as its last-found window, and AHK makes that the top-level
			//window even when the message went to a control (the control's own handle is still passed as the
			//callback's fourth argument). Scripts compare it against a GUI's Hwnd to tell which of their
			//windows a click landed in, which a control handle would never match.
			long hwnd = TopLevelOf(m.HWnd);
			object[] args = [m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg, m.HWnd.ToInt64()];

			if (!monitor.TryExecuteEmergency(script, args, A_TickCount, hwnd, out var reply))
				return false;

			m.Result = (nint)reply;
			return true;
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
	}
}
#endif
