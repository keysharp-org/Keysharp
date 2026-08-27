#if OSX
namespace Keysharp.Internals.AppleEvents
{
	/// <summary>
	/// Distributed notifications, which are the closest macOS equivalent of a connection point event. They are not
	/// described by any terminology and are not scoped to an object, so a subscription filters by the publishing
	/// application's bundle identifier and hands the payload over as it arrives.
	/// <para>
	/// Delivery happens on the run loop of the thread that registered, so one dedicated thread owns the observer
	/// and runs a run loop for as long as anything is subscribed. Nothing may throw out of the callback: it is
	/// called by Core Foundation, and an exception crossing that boundary takes the process down rather than
	/// merely killing a dispatch loop.
	/// </para>
	/// </summary>
	internal static class AENotifications
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void NotificationCallback(nint center, nint observer, nint name, nint @object, nint userInfo);

		private sealed class Subscription : IDisposable
		{
			internal string Prefix;
			internal Action<string, object> Handler;

			public void Dispose() => Remove(this);
		}

		/// <summary>
		/// One run of the listening thread. Stopping is a flag this owns rather than a signal aimed at the run
		/// loop, so a stop that arrives before the loop has started cannot be missed, and a listener that is
		/// winding down is never mistaken for one that is still serving.
		/// </summary>
		private sealed class Listener
		{
			internal nint ObserverId;
			internal Thread Thread;
			internal volatile bool Stopping;
		}

		private static readonly Lock gate = new ();
		private static readonly List<Subscription> subscriptions = [];
		// Created once and held for the process lifetime: the pointer handed to Core Foundation must stay valid,
		// and a field that was reassigned per listener could let an earlier delegate be collected while in use.
		private static readonly NotificationCallback callbackDelegate = OnNotification;
		private static readonly nint callbackPtr = Marshal.GetFunctionPointerForDelegate(callbackDelegate);

		// Each listener registers under its own observer identity, because an outgoing listener unregisters as it
		// unwinds and that can happen after its replacement has already registered.
		private static long lastObserverId;
		private static Listener current;

		/// <summary>
		/// Subscribes to every notification whose name starts with <paramref name="prefix"/>. Dispose the result to
		/// unsubscribe.
		/// </summary>
		internal static IDisposable Subscribe(string prefix, Action<string, object> handler)
		{
			var subscription = new Subscription { Prefix = prefix ?? "", Handler = handler };

			lock (gate)
			{
				subscriptions.Add(subscription);
				Start();
			}

			return subscription;
		}

		private static void Remove(Subscription subscription)
		{
			lock (gate)
			{
				_ = subscriptions.Remove(subscription);

				if (subscriptions.Count == 0)
					Stop();
			}
		}

		private static void Start()
		{
			// A listener that has been asked to stop, or whose loop failed, is not serving anything: either way a
			// fresh one is needed, and it gets its own identity so the two cannot interfere.
			if (current is { Stopping: false } listener && listener.Thread.IsAlive)
				return;

			var started = new Listener { ObserverId = (nint)Interlocked.Increment(ref lastObserverId) };
			var ready = new ManualResetEventSlim(false);
			started.Thread = new Thread(() => RunLoop(started, ready))
			{
				IsBackground = true,
				Name = "Keysharp distributed notifications"
			};
			current = started;
			started.Thread.Start();
			// Registering happens on that thread, so waiting here means the first notification cannot be missed
			// by a script that subscribes and immediately triggers the application.
			_ = ready.Wait(5_000);
		}

		private static void RunLoop(Listener listener, ManualResetEventSlim ready)
		{
			var center = CF.CFNotificationCenterGetDistributedCenter();
			var mode = CF.CreateString("kCFRunLoopDefaultMode");

			try
			{
				// A null name observes everything: subscriptions name a prefix rather than an exact notification,
				// so there is no set of names to hand Core Foundation up front.
				CF.CFNotificationCenterAddObserver(center, listener.ObserverId, callbackPtr, 0, 0,
												   CF.NotificationSuspensionBehaviorDeliverImmediately);
				ready.Set();

				// Run in slices rather than one indefinite call. Stopping is then a flag this loop reads, which a
				// stop arriving before the loop started cannot slip past — unlike asking the run loop to stop,
				// which does nothing if it is not running yet and would wedge the thread forever.
				while (!listener.Stopping)
					_ = CF.CFRunLoopRunInMode(mode, 1.0, 0);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"The notification listener stopped: {ex.Message}");
			}
			finally
			{
				ready.Set();

				try
				{
					CF.CFNotificationCenterRemoveEveryObserver(center, listener.ObserverId);
				}
				catch
				{
				}

				CF.CFRelease(mode);
			}
		}

		private static void Stop()
		{
			if (current == null)
				return;

			// The thread notices within one slice and unregisters its own observer identity as it unwinds, so a
			// listener started in the meantime is unaffected.
			current.Stopping = true;
			current = null;
		}

		/// <summary>
		/// Core Foundation calls this directly, so it must never let anything escape. Even the reporting of a
		/// failure is guarded.
		/// </summary>
		private static void OnNotification(nint center, nint observer, nint name, nint @object, nint userInfo)
		{
			try
			{
				var notification = CF.ReadString(name);

				if (notification.Length == 0)
					return;

				List<Subscription> matches = null;

				lock (gate)
				{
					foreach (var subscription in subscriptions)
						if (notification.StartsWith(subscription.Prefix, StringComparison.OrdinalIgnoreCase))
							(matches ??= []).Add(subscription);
				}

				if (matches == null)
					return;

				var payload = ReadUserInfo(userInfo);

				foreach (var subscription in matches)
				{
					try
					{
						subscription.Handler(notification, payload);
					}
					catch (Exception ex)
					{
						try
						{
							Diagnostics.Debug.WriteLine($"A notification handler failed: {ex.Message}");
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
		}

		private static object ReadUserInfo(nint userInfo)
		{
			var map = new Keysharp.Builtins.Map();

			if (userInfo == 0)
				return map;

			var count = (int)CF.CFDictionaryGetCount(userInfo);

			if (count <= 0)
				return map;

			var keys = Marshal.AllocHGlobal(nint.Size * count);
			var values = Marshal.AllocHGlobal(nint.Size * count);

			try
			{
				CF.CFDictionaryGetKeysAndValues(userInfo, keys, values);

				for (var i = 0; i < count; i++)
				{
					var key = Marshal.ReadIntPtr(keys, i * nint.Size);
					var value = Marshal.ReadIntPtr(values, i * nint.Size);
					var name = CF.ReadString(key);

					if (name.Length > 0)
						_ = map.Set(name, CF.ReadValue(value));
				}
			}
			finally
			{
				Marshal.FreeHGlobal(keys);
				Marshal.FreeHGlobal(values);
			}

			return map;
		}
	}
}
#endif
