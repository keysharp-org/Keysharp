#if LINUX
using Gdk;

namespace Keysharp.Core.Common.Window
{
	internal struct Message
	{
		public nint HWnd;
		public int Msg;
		public nint WParam;
		public nint LParam;
		public nint Result;
	}

	internal class MessageFilter
	{
		private readonly Script script;
		internal Message handledMsg;
		private FilterFunc gtkFilter;
		private bool filterAttached;

		internal MessageFilter(Script associatedScript)
		{
			script = associatedScript;
		}

		internal void Attach()
		{
			if (filterAttached)
				return;

			gtkFilter = GtkFilter;
			var root = Gdk.Screen.Default?.RootWindow;
			root?.AddFilter(gtkFilter);
			filterAttached = true;
		}

		internal void Detach()
		{
			if (!filterAttached || gtkFilter == null)
				return;

			var root = Gdk.Screen.Default?.RootWindow;
			root?.RemoveFilter(gtkFilter);
			gtkFilter = null;
			filterAttached = false;
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

				if (MessagesEqual(handledMsg, m))
				{
					eventInfo = A_TickCount;
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

		private static bool MessagesEqual(Message left, Message right)
			=> left.HWnd == right.HWnd
				&& left.Msg == right.Msg
				&& left.WParam == right.WParam
				&& left.LParam == right.LParam
				&& left.Result == right.Result;

		private FilterReturn GtkFilter(IntPtr xevent, Event evnt)
		{
			// No Win32 message on GTK; use the GDK event type as the message id.
			var msg = new Message
			{
				HWnd = evnt.Window.Handle,
				Msg = (int)evnt.Type,
				WParam = 0,
				LParam = 0,
				Result = 0,
			};

			_ = CallEventHandlers(ref msg);
			return FilterReturn.Continue;
		}
	}
}
#endif