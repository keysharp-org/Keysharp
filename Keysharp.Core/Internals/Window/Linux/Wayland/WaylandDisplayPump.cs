#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Bounded, serialized dispatch for clients which do not own a permanent Wayland event thread.</summary>
	internal static class WaylandDisplayPump
	{
		private sealed class SyncState { internal bool Done; }

		internal static bool Roundtrip(nint display, int timeoutMs)
		{
			if (display == 0)
				return false;
			var callback = WaylandNative.DisplaySync(display);
			if (callback == 0)
				return false;
			var state = new SyncState();
			var handle = GCHandle.Alloc(state);
			try
			{
				if (WaylandNative.ProxyAddListener(callback, CallbackListener.Pointer,
						GCHandle.ToIntPtr(handle)) != 0)
					return false;

				return DispatchUntil(display, () => state.Done, timeoutMs);
			}
			finally
			{
				WaylandNative.ProxyDestroy(callback);
				handle.Free();
			}
		}

		internal static bool DispatchPending(nint display)
			=> display != 0 && DispatchOnce(display, 0, null);

		internal static bool DispatchUntil(nint display, Func<bool> completed, int timeoutMs)
			=> display != 0 && WaitUntil(completed, timeoutMs,
				remaining => DispatchOnce(display, remaining, completed));

		internal static bool WaitUntil(Func<bool> completed, int timeoutMs, Func<int, bool> dispatch,
			Func<long> tickCount = null)
		{
			ArgumentNullException.ThrowIfNull(completed);
			ArgumentNullException.ThrowIfNull(dispatch);
			tickCount ??= () => Environment.TickCount64;
			var deadline = tickCount() + Math.Max(1, timeoutMs);
			while (!completed())
			{
				var remaining = deadline - tickCount();
				if (remaining <= 0 || !dispatch((int)Math.Min(int.MaxValue, remaining)))
					return false;
			}
			return true;
		}

		private static bool DispatchOnce(nint display, int timeoutMs, Func<bool> completed)
		{
			var prepareDeadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
			if (WaylandNative.DisplayDispatchPending(display) < 0)
				return false;
			if (completed?.Invoke() == true)
				return true;
			while (WaylandNative.DisplayPrepareRead(display) != 0)
			{
				if (WaylandNative.DisplayDispatchPending(display) < 0)
					return false;
				if (completed?.Invoke() == true)
					return true;
				if (Environment.TickCount64 >= prepareDeadline)
					return timeoutMs == 0;
			}
			var remaining = prepareDeadline - Environment.TickCount64;
			if (remaining <= 0)
				return CancelRead(display, timeoutMs == 0);
			var flush = WaylandNative.DisplayFlush(display);
			var wouldBlock = flush < 0 && Marshal.GetLastPInvokeError() == WaylandNative.EAGAIN;
			if (flush < 0 && !wouldBlock)
				return CancelRead(display, false);
			var fd = WaylandNative.DisplayGetFd(display);
			if (fd < 0)
				return CancelRead(display, false);
			var poll = new WaylandNative.PollFd
			{
				FileDescriptor = fd,
				Events = (short)(WaylandNative.POLLIN | (wouldBlock ? WaylandNative.POLLOUT : 0))
			};
			var pollTimeout = timeoutMs == 0 ? 0 : (int)Math.Min(int.MaxValue, remaining);
			var ready = WaylandNative.Poll(ref poll, 1, pollTimeout);
			if (ready < 0)
				return CancelRead(display, Marshal.GetLastPInvokeError() == WaylandNative.EINTR);
			if (ready == 0)
				return CancelRead(display, timeoutMs == 0);
			var events = poll.ReturnedEvents;
			if ((events & (WaylandNative.POLLERR | WaylandNative.POLLHUP | WaylandNative.POLLNVAL)) != 0)
				return CancelRead(display, false);
			if ((events & WaylandNative.POLLIN) == 0)
				return CancelRead(display, true);
			return WaylandNative.DisplayReadEvents(display) >= 0
				&& WaylandNative.DisplayDispatchPending(display) >= 0;
		}

		private static bool CancelRead(nint display, bool result)
		{
			WaylandNative.DisplayCancelRead(display);
			return result;
		}

		private static class CallbackListener
		{
			private static readonly DoneHandler onDone = (data, _, _) => State(data).Done = true;
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onDone);
			private static SyncState State(nint data) => (SyncState)GCHandle.FromIntPtr(data).Target;

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void DoneHandler(nint data, nint callback, uint serial);
		}
	}
}
#endif
