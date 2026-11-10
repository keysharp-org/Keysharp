namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class NotificationTests : TestRunner
{
	[Test]
	public void RegistrationIdentityAndOrder()
	{
		using var manager = new NotificationManager(Script.TheScript);
		Assert.That(manager.Callbacks().Length, Is.Zero);
		Assert.That(manager.BackendCreations, Is.Zero, "Listing callbacks does not initialize a native source.");
		var order = new List<string>();
		var first = Record("first");
		var second = Record("second");
		var prefix = Record("prefix");
		manager.Change(first);
		manager.Change(second);
		manager.Change(second, -1);
		manager.Change(prefix, -1);
		manager.Publish(1L);
		Drain();
		Assert.That(order, Is.EqualTo(new[] { "prefix", "first", "second" }));
		var snapshot = manager.Callbacks();
		Assert.That(snapshot.Length, Is.EqualTo(3));
		Assert.That(snapshot[1], Is.SameAs(prefix));
		Assert.That(snapshot[2], Is.SameAs(first));
		Assert.That(snapshot[3], Is.SameAs(second));
		snapshot[1] = first;
		Assert.That(manager.Callbacks()[1], Is.SameAs(prefix), "Mutating a returned array cannot alter registrations.");

		var otherOwner = new ScriptEventScheduler(Script.TheScript, Environment.CurrentManagedThreadId, false);
		try
		{
			manager.Change(first, owner: otherOwner);
			Assert.That(manager.Count, Is.EqualTo(4), "Callback identity is scoped to its owning real thread.");
			Assert.That(manager.Callbacks().Length, Is.EqualTo(3), "The snapshot excludes another real thread's registrations.");
			manager.Change(first, 0);
			manager.Change(first, 0);
			Assert.That(manager.Count, Is.EqualTo(3), "Removal affects only the caller's registration.");
			Assert.That(manager.Callbacks().Length, Is.EqualTo(2));
			Assert.That(snapshot.Length, Is.EqualTo(3), "Removing a registration does not modify an earlier snapshot.");
			Assert.IsTrue(manager.RemoveOwned(otherOwner));
			Assert.That(manager.Count, Is.EqualTo(2));
		}
		finally
		{
			_ = manager.RemoveOwned(otherOwner);
			otherOwner.DisposeWorker();
		}

		KeysharpFunc Record(string value) => new((Func<object, object>)(_ => { order.Add(value); return ""; }));
	}

	[Test]
	public void UpdatedOptionsReplaceQueuedDelivery()
	{
		using var manager = new NotificationManager(Script.TheScript);
		var values = new List<object>();
		var callback = new KeysharpFunc((Func<object, object>)(value => { values.Add(value); return ""; }));
		manager.Change(callback, option: "Output");
		manager.Publish(1L);
		manager.Change(callback, option: "Input");
		Drain();
		Assert.IsEmpty(values, "Changing a filter invalidates pending delivery under the old filter.");
		Assert.That(manager.Count, Is.EqualTo(1));
		manager.Publish(2L);
		Drain();
		Assert.That(values, Is.EqualTo(new object[] { 2L }));
		manager.Change(callback, 0);
		Assert.That(manager.Count, Is.Zero, "Removal does not need the current filter.");
	}

	[TestCase(false), TestCase(true)]
	public void CallbacksAreSerialized(bool updateFilter)
	{
		using var manager = new NotificationManager(Script.TheScript);
		var order = new List<string>();
		KeysharpFunc callback = null;
		callback = new KeysharpFunc((Func<object, object>)(value =>
		{
			order.Add($"start {value}");
			if ((long)value == 1L)
			{
				if (updateFilter)
					manager.Change(callback, option: "Input");
				manager.Publish(2L);
				Drain();
			}
			order.Add($"end {value}");
			return "";
		}));
		manager.Change(callback);
		manager.Publish(1L);
		Drain();
		Assert.That(order, Is.EqualTo(new[] { "start 1", "end 1", "start 2", "end 2" }));
	}

	[Test]
	public void RemovalCancelsQueuedDelivery()
	{
		using var manager = new NotificationManager(Script.TheScript);
		var values = new List<object>();
		var callback = new KeysharpFunc((Func<object, object>)(value => { values.Add(value); return ""; }));
		manager.Change(callback);
		manager.Publish(1L);
		manager.Change(callback, 0);
		manager.Change(callback);
		Drain();
		Assert.IsEmpty(values, "Re-registering the same callback cannot revive the removed registration's work.");
		manager.Publish(2L);
		Drain();
		Assert.That(values, Is.EqualTo(new object[] { 2L }));
	}

	[TestCase(false), TestCase(true)]
	public void StartupFailureCanRetry(bool duringPreparation)
	{
		using var manager = new NotificationManager(Script.TheScript)
		{
			FailPreparation = duringPreparation,
			FailInstall = !duringPreparation
		};
		var calls = 0;
		var callback = new KeysharpFunc((Func<object, object>)(_ => { calls++; return ""; }));
		Assert.That(manager.TryChange(callback), Is.False);
		Assert.That(manager.Count, Is.Zero);
		Assert.That(manager.LastCreated.IsActive, Is.False, "An unsuccessful registration releases its owner root.");
		manager.FailPreparation = manager.FailInstall = false;
		Assert.IsTrue(manager.TryChange(callback), "An explicit registration retries a failed source.");
		manager.Publish(1L);
		Drain();
		Assert.That(calls, Is.EqualTo(1));
	}

	[Test]
	public void PermanentSourceLossReportsOncePerOwner()
	{
		using var manager = new NotificationManager(Script.TheScript);
		var otherOwner = new ScriptEventScheduler(Script.TheScript, Environment.CurrentManagedThreadId, false);
		var errors = new List<object>();
		var onError = new KeysharpFunc((Func<object, object, object>)((error, _) => { errors.Add(error); return -1L; }));
		_ = Errors.OnError(onError);
		var calls = 0;
		var first = new KeysharpFunc((Func<object, object>)(_ => { calls++; return ""; }));
		var second = new KeysharpFunc((Func<object, object>)(_ => ""));

		try
		{
			manager.Change(first);
			manager.Change(second);
			manager.Change(first, owner: otherOwner);
			var registrations = manager.Snapshot();
			manager.Publish(1L);
			manager.LoseSource();
			manager.LoseSource();
			Assert.That(manager.Count, Is.Zero);
			Assert.That(registrations.All(registration => !registration.IsActive), Is.True);
			Assert.IsEmpty(errors, "Native source loss queues its error onto each owner's script thread.");
			Drain();
			otherOwner.PumpThreadQueuedEventsCore();
			Assert.That(errors.Count, Is.EqualTo(2), "Two registrations on one owner produce one error for that owner.");
			Assert.That(errors.All(error => error is OSError), Is.True);
			Assert.That(calls, Is.Zero, "Source loss cancels pending callbacks.");
			manager.Change(first);
			manager.Publish(2L);
			Drain();
			Assert.That(calls, Is.EqualTo(1), "Explicit registration can restart observation after source loss.");
		}
		finally
		{
			_ = manager.RemoveOwned(otherOwner);
			otherOwner.DisposeWorker();
			_ = Errors.OnError(onError, 0L);
		}
	}

	private static void Drain() => Keysharp.Internals.Flow.TryDoEvents(Script.TheScript.EventScheduler, propagateExit: false, pumpUi: false);

	// Synthetic intake exercises the queue and native-independent registration contract without device changes.
	private sealed class NotificationManager(Script script) : EventManagerBase<NotificationManager.Registration, IDisposable, NoPayload>(script, false)
	{
		internal int Count => registrations.Count;
		internal int BackendCreations;
		internal bool FailPreparation, FailInstall;
		internal Registration LastCreated;
		protected override string RegistrationName => "NotificationTests";
		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;
		protected override IDisposable CreateBackend()
		{
			BackendCreations++;
			return null;
		}
		protected override void SyncNativeLocked()
		{
			if (FailInstall && registrations.Count != 0)
				throw new InvalidOperationException("The source could not be installed.");
		}

		protected override void PrepareRegistration(Registration registration, bool isFirst)
		{
			if (FailPreparation)
				throw new InvalidOperationException("The source could not be inspected.");
		}

		internal void Change(object callback, long addRemove = 1, string option = "All", ScriptEventScheduler owner = null)
			=> Assert.IsTrue(TryChange(callback, addRemove, option, owner));

		internal bool TryChange(object callback, long addRemove = 1, string option = "All", ScriptEventScheduler owner = null)
			=> Modify(LastCreated = new(callback, owner ?? script.EventScheduler, option), addRemove,
				static (existing, replacement) => existing.Option == replacement.Option);

		internal Registration[] Snapshot() => registrations.ToArray();

		internal void LoseSource()
		{
			lock (gate)
				FailAllLocked();
		}

		internal void Publish(object value)
		{
			foreach (var registration in registrations.ToArray())
				FireNotification(registration, [value]);
		}

		internal sealed class Registration(object callback, ScriptEventScheduler owner, string option)
			: CallbackRegistration(callback, owner, true, holdsRoot: false)
		{
			internal string Option => option;
		}
	}
}
