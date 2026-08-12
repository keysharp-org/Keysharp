using Keysharp.Builtins;
#if !WINDOWS
#if LINUX
using Gdk;
#endif

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

	internal class MessageFilter
	{
		private readonly Script script;
		internal Message handledMsg;
#if LINUX
		private FilterFunc gtkFilter;
		private bool filterAttached;
#endif

		internal MessageFilter(Script associatedScript)
		{
			script = associatedScript;
		}

		internal void Attach()
		{
#if LINUX
			if (filterAttached)
				return;

			gtkFilter = GtkFilter;
			var root = Gdk.Screen.Default?.RootWindow;
			if (root != null)
			{
				try
				{
					root?.AddFilter(gtkFilter);
					filterAttached = true;
				}
				catch
				{
					Diagnostics.Debug.WriteLine("Failed to attach GTK message filter");
				}
			}
#endif
		}

		internal void Detach()
		{
#if LINUX
			if (!filterAttached || gtkFilter == null)
				return;

			var root = Gdk.Screen.Default?.RootWindow;
			root?.RemoveFilter(gtkFilter);
			gtkFilter = null;
			filterAttached = false;
#endif
		}

		internal bool CallEventHandlers(ref Message m, bool buffered = false)
		{
			if (script.GuiData.onMessageHandlers.TryGetValue(m.Msg, out var monitor))
			{
				object eventInfo = 0L;
				long hwnd = m.HWnd;

				if (MessagesEqual(handledMsg, m))
				{
					eventInfo = A_TickCount;
				}

				object[] args = [m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg, m.HWnd.ToInt64()];

				if (buffered)
				{
					foreach (var registration in monitor.GetRegistrationsSnapshot())
					{
						var targetScheduler = registration.OwnerScheduler ?? script.EventScheduler;
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

		private static bool MessagesEqual(Message left, Message right)
			=> left.HWnd == right.HWnd
				&& left.Msg == right.Msg
				&& left.WParam == right.WParam
				&& left.LParam == right.LParam
				&& left.Result == right.Result;

#if LINUX
		private FilterReturn GtkFilter(IntPtr xevent, Event evnt)
		{
			if (evnt?.Window == null)
				return FilterReturn.Continue;

			// No Win32 message on GTK; use the GDK event type as the message id.
			var msg = new Message
			{
				HWnd = evnt.Window.Handle,
				Msg = (int)evnt.Type,
				WParam = 0,
				LParam = 0,
				Result = 0,
			};

			handledMsg = msg;
			_ = CallEventHandlers(ref msg, true);
			return FilterReturn.Continue;
		}
#endif
	}
}
#endif
