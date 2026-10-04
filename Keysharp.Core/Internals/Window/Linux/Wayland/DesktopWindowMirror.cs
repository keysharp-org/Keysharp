#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal sealed class DesktopWindowMirror
	{
		private readonly object sync = new();
		private Dictionary<ulong, WaylandWindowInfo> windows = [];
		private Dictionary<ulong, WaylandWindowInfo> snapshot;
		private ulong epoch, sequence;
		private bool ready;
		private readonly HashSet<ulong> knownWindows = [];
		private TaskCompletionSource<bool> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal Task ChangeSignal { get { lock (sync) return changed.Task; } }

		private void SignalChange()
		{
			var previous = changed;
			changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
			previous.TrySetResult(true);
			Monitor.PulseAll(sync);
		}

		internal void Invalidate()
		{
			lock (sync)
			{
				ready = false;
				epoch = 0;
				sequence = 0;
				snapshot = null;
				windows.Clear();
				knownWindows.Clear();
				SignalChange();
			}
		}

		internal bool Apply(uint kind, ulong nextEpoch, ulong nextSequence, WaylandWindowInfo window, out WaylandWindowInfo previous)
		{
			lock (sync)
			{
				previous = window != null ? windows.GetValueOrDefault(window.ServiceHandle) : null;
				if (kind == 1)
				{
					ready = false;
					epoch = nextEpoch;
					sequence = nextSequence;
					snapshot = [];
					return true;
				}

				if (epoch != nextEpoch || (snapshot != null ? nextSequence != sequence : !ready || nextSequence != sequence + 1))
				{
					Invalidate();
					return false;
				}

				if (kind == 2 && snapshot != null && window != null)
				{
					snapshot[window.ServiceHandle] = window;
					knownWindows.Add(window.ServiceHandle);
				}
				else if (kind == 3 && snapshot != null)
				{
					windows = snapshot;
					snapshot = null;
					ready = true;
				}
				else if (kind >= 4 && kind <= 11 && window != null && snapshot == null)
				{
					sequence = nextSequence;
					knownWindows.Add(window.ServiceHandle);
					if (kind == 5)
						windows.Remove(window.ServiceHandle);
					else
						windows[window.ServiceHandle] = window;
				}
				else
				{
					Invalidate();
					return false;
				}

				if (kind != 2) SignalChange();
				return true;
			}
		}

		internal bool TryRead(out IReadOnlyList<WaylandWindowInfo> result)
		{
			lock (sync)
			{
				result = ready ? windows.Values.OrderBy(window => window.StackingOrder).ToArray() : [];
				return ready;
			}
		}

		internal bool WaitUntil(ulong expectedEpoch, ulong expectedSequence, int timeoutMs = 2_000)
		{
			var deadline = Environment.TickCount64 + timeoutMs;
			lock (sync)
			{
				while (epoch == expectedEpoch && (!ready || sequence < expectedSequence))
				{
					var remaining = deadline - Environment.TickCount64;
					if (remaining <= 0 || !Monitor.Wait(sync, (int)remaining))
						return false;
				}
				return ready && epoch == expectedEpoch && sequence >= expectedSequence;
			}
		}

		internal bool WaitReady(int timeoutMs = 2_000)
		{
			var deadline = Environment.TickCount64 + timeoutMs;
			lock (sync)
			{
				while (!ready)
				{
					var remaining = deadline - Environment.TickCount64;
					if (remaining <= 0 || !Monitor.Wait(sync, (int)remaining))
						return false;
				}
				return true;
			}
		}

		internal ulong Epoch { get { lock (sync) return epoch; } }
		internal bool KnowsWindow(ulong handle) { lock (sync) return ready && knownWindows.Contains(handle); }
	}
}
#endif
