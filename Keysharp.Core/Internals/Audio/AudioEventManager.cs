using Keysharp.Builtins;
using Keysharp.Internals.Events;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// Engine-side state for one <c>Audio.OnDeviceChange</c> subscription. A device-change subscription filters
	/// only by kind, so that is all it adds to the shared subscription base.
	/// </summary>
	internal sealed class AudioEventRegistration(object callback, ScriptEventScheduler ownerScheduler,
		AudioEventManager manager, string kind)
		: EventSubscriptionBase(callback, ownerScheduler, manager.KeepsScriptRunning)
	{
		internal readonly AudioEventManager manager = manager;

		/// <summary>"Output", "Input" or "All"; anything else was rejected before this was constructed.</summary>
		internal readonly string kind = kind;

		internal override void Unregister() => manager.Unregister(this);

		internal override void Register() => manager.Register(this);
	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Audio.OnDeviceChange</c>. The backend reports only that
	/// something changed, so classification happens here by diffing the previous device snapshot against a fresh
	/// one. A notification whose diff is empty fires nothing, which absorbs the duplicate notifications every
	/// platform emits.
	/// </summary>
	internal sealed class AudioEventManager(Script script)
		: EventManagerBase<AudioEventRegistration, AudioEventManager.Watcher, ValueTuple>(script, false)
	{
		internal const string KindAdded = "Added";
		internal const string KindRemoved = "Removed";
		internal const string KindChanged = "Changed";
		internal const string KindDefaultChanged = "DefaultChanged";

		/// <summary>
		/// Holds the backend's notification registration. It is installed on the first subscription and released
		/// after the last, so a stopped hook leaves no native notification waking the process.
		/// </summary>
		internal sealed class Watcher(AudioEventManager owner) : IDisposable
		{
			private IAudioDeviceWatcher inner;

			internal bool Start()
			{
				if (inner != null)
					return true;

				var backend = owner.script.AudioService?.Backend;

				if (backend == null || !backend.IsAvailable || !backend.Supports(AudioCapability.DeviceChange))
					return false;

				inner = backend.WatchDevices(owner.OnNativeChange);
				return inner != null;
			}

			internal void Stop()
			{
				var toDispose = inner;
				inner = null;
				toDispose?.Dispose();
			}

			public void Dispose() => Stop();
		}

		// Diffing enumerates every device, which is slow enough that holding the registration gate across it
		// would block every subscribe and stop for the length of a device reconfiguration.
		private readonly Lock classifyGate = new();
		private AudioDeviceDescriptor[] lastDevices = [];
		private bool started;

		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;

		protected override Watcher CreateBackend() => new (this);

		/// <summary>Installs the native notification on the first subscription and removes it after the last.</summary>
		protected override void SyncNativeLocked()
		{
			var wanted = registrations.Count > 0 && !disposed;

			if (wanted == started)
				return;

			if (wanted)
			{
				if (EnsureBackend() is not { } watcher || !watcher.Start())
				{
					FailAllLocked();                          // no device notification here, so these can never fire
					return;
				}
			}
			else
			{
				Backend?.Stop();
			}

			started = wanted;
		}

		/// <summary>
		/// Seeds the baseline from the devices as they are now, so the first callback reports a change the script
		/// could not already see when it subscribed.
		/// </summary>
		protected override void PrepareRegistration(AudioEventRegistration reg, bool isFirst)
		{
			if (!isFirst)
				return;

			lock (classifyGate)
				lastDevices = Snapshot() ?? [];
		}

		// ---- native intake (arbitrary backend thread) ----------------------------------------

		/// <summary>
		/// Reached from a backend notification callback: the Pulse mainloop thread, a WASAPI COM thread, a Core
		/// Audio HAL listener. Classifying enumerates every device, which re-enters the audio API — on Pulse that
		/// is a documented self-deadlock, and on Windows the notification client may not block. So the work is
		/// handed to the thread pool and this returns immediately, which is the bounded work the contract allows.
		/// </summary>
		private void OnNativeChange()
		{
			if (disposed)
				return;

			_ = ThreadPool.UnsafeQueueUserWorkItem(static manager => manager.ClassifyAndDispatch(), this, preferLocal: false);
		}

		private void ClassifyAndDispatch()
		{
			if (disposed)
				return;

			AudioDeviceDescriptor[] current;
			AudioDeviceDescriptor[] previous;

			lock (classifyGate)
			{
				if (disposed || !HasSubscriptions)
					return;

				current = Snapshot();

				// A failed enumeration is not "no devices": treating it as one would report a bogus removal and
				// then leave that as the baseline, so the next notification would report a bogus addition back.
				if (current == null)
					return;

				previous = lastDevices;
				lastDevices = current;
			}

			foreach (var (kind, device) in Classify(previous, current))
				DispatchAll(kind, device);
		}

		private void DispatchAll(string kind, AudioDeviceDescriptor device)
		{
			// Resolved once per dispatch rather than per registration, and from the script this manager was built
			// for, which is the captured-service rule every object this class hands out already follows.
			var service = script.AudioService;
			AudioEventRegistration[] toFire;

			lock (gate)
			{
				if (disposed)
					return;

				toFire = [.. registrations];
			}

			foreach (var reg in toFire)
			{
				if (!reg.IsActive || !KindMatches(reg.kind, device.Kind))
					continue;

				// A removed device is wrapped as gone, so operating on it inside the handler raises the
				// actionable "no longer available" error.
				var wrapped = kind == KindRemoved
							  ? Ks.Audio.Device.WrapMissing(service, device)
							  : Ks.Audio.Device.Wrap(service, device);
				Fire(reg, reg.Callback, [reg.scriptObject, kind, wrapped]);
			}
		}

		private static bool KindMatches(string wanted, AudioDeviceKind kind)
			=> wanted == "All"
			   || (wanted == "Output" && kind == AudioDeviceKind.Output)
			   || (wanted == "Input" && kind == AudioDeviceKind.Input);

		private AudioDeviceDescriptor[] Snapshot()
		{
			try
			{
				var backend = script.AudioService?.Backend;

				if (backend == null || !backend.IsAvailable)
					return null;

				return [.. backend.EnumerateDevices(AudioDeviceKind.Output), .. backend.EnumerateDevices(AudioDeviceKind.Input)];
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Audio device enumeration failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Turns one "something changed" notification into the specific events a script asked for. Order is
		/// removals, then additions, then default changes, then other changes, so a handler that re-enumerates on
		/// the first event it sees never observes a half-applied transition.
		/// </summary>
		internal static List<(string Kind, AudioDeviceDescriptor Device)> Classify(AudioDeviceDescriptor[] previous, AudioDeviceDescriptor[] current)
		{
			var events = new List<(string, AudioDeviceDescriptor)>();

			foreach (var p in previous)
				if (!current.Any(c => c.Id == p.Id))
					events.Add((KindRemoved, p));

			foreach (var c in current)
				if (!previous.Any(p => p.Id == c.Id))
					events.Add((KindAdded, c));

			foreach (var c in current)
			{
				var match = previous.FirstOrDefault(p => p.Id == c.Id);

				if (match.Id == null)
					continue;                            // already reported as an addition

				if (match.IsDefault != c.IsDefault && c.IsDefault)
					events.Add((KindDefaultChanged, c));
				else if (match.Name != c.Name)
					events.Add((KindChanged, c));
			}

			return events;
		}
	}
}
