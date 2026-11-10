using Keysharp.Builtins;
using Keysharp.Internals.Events;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Input
{
	internal sealed class InputEventManager(Script owner)
		: EventManagerBase<CallbackRegistration, InputEventManager.Watcher, NoPayload>(owner, false)
	{
		internal sealed class Watcher(InputEventManager manager) : IDisposable
		{
			// A delayed failure belongs to its original run, even after another run starts.
			internal sealed class Run
			{
				internal IDisposable Subscription;
				internal InputDeviceInfo[] Devices;
				internal volatile bool Failed;
			}
			internal Run Current { get; private set; }

			internal bool Start()
			{
				if (Current != null) return true;
				var run = new Run();

				try
				{
					run.Subscription = Platform.WatchInputDevices(manager.script, manager.QueueRefresh,
						() => { run.Failed = true; manager.QueueRefresh(); });
					if (run.Subscription == null) return false;
					run.Devices = manager.Snapshot();
					if (run.Devices == null || run.Failed) return false;
					Current = run;
					return true;
				}
				finally
				{
					if (Current != run)
						run.Subscription?.Dispose();
				}
			}
			public void Dispose()
			{
				var active = Current;
				Current = null;
				active?.Subscription.Dispose();
			}
		}

		private bool started;
		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;
		protected override string RegistrationName => "Input.OnDeviceChange";
		protected override Watcher CreateBackend() => new(this);

		protected override void SyncNativeLocked()
		{
			var wanted = registrations.Count != 0 && !disposed;
			if (wanted == started) return;

			if (wanted)
			{
				if (EnsureBackend() is not { } watcher || !watcher.Start())
				{
					FailAllLocked();
					return;
				}
			}
			else
			{
				started = false;
				Backend?.Dispose();
			}

			started = wanted;
		}

		private InputDeviceInfo[] Snapshot()
		{
			try { return script.IsDisposed ? null : script.HookThread.GetInputDevices(); }
			catch (Exception error)
			{
				Diagnostics.Debug.WriteLine($"Input device inventory failed: {error.Message}");
				return null;
			}
		}

		private void FailLocked()
		{
			try { Backend?.Dispose(); }
			finally
			{
				started = false;
				FailAllLocked();
			}
		}

		protected override void ProcessRefresh()
		{
			List<(string Change, InputDeviceInfo Device)> changes;
			CallbackRegistration[] active;
			Watcher.Run run;

			lock (gate)
			{
				if (disposed || registrations.Count == 0 || Backend?.Current is not { } currentRun) return;
				run = currentRun;
				if (run.Failed)
				{
					FailLocked();
					return;
				}
			}

			var current = Snapshot();
			lock (gate)
			{
				if (disposed || registrations.Count == 0 || !ReferenceEquals(Backend?.Current, run)) return;
				if (current == null || run.Failed)
				{
					FailLocked();
					return;
				}
				changes = Classify(run.Devices, current);
				run.Devices = current;
				if (changes.Count == 0) return;
				active = [.. registrations];
			}

			foreach (var (change, device) in changes)
			{
				var wrapped = new Ks.Input(device, script);
				foreach (var registration in active)
					FireNotification(registration, [change, wrapped]);
			}
		}

		internal static List<(string Change, InputDeviceInfo Device)> Classify(InputDeviceInfo[] previous, InputDeviceInfo[] current)
		{
			var remaining = new List<InputDeviceInfo>(current);
			var changes = new List<(string, InputDeviceInfo)>();
			foreach (var old in previous)
			{
				var index = remaining.FindIndex(device => old.Id != 0
					? device.Id == old.Id && device.Kind == old.Kind : Same(old, device));
				if (index < 0) changes.Add(("Removed", old));
				else
				{
					var device = remaining[index];
					remaining.RemoveAt(index);
					if (!Same(old, device)) changes.Add(("Changed", device));
				}
			}
			foreach (var device in remaining) changes.Add(("Added", device));
			return changes;
		}

		private static bool Same(InputDeviceInfo first, InputDeviceInfo second)
			=> first.Id == second.Id && first.Kind == second.Kind && first.Name == second.Name
				&& first.VendorId == second.VendorId && first.ProductId == second.ProductId && first.HardwareId == second.HardwareId
				&& first.Manufacturer == second.Manufacturer && first.Connection == second.Connection
				&& first.Location == second.Location && first.IsInternal == second.IsInternal;
	}
}
