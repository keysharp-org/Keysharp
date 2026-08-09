#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.ExtensionMethods;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Misc")]
	public class ClipboardRecoveryTests
	{
		[Test]
		public void TaskWaitBeforeScriptInitializationDoesNotEnterMessagePump()
		{
			var previous = Script.TheScript;

			try
			{
				Script.TheScript = null;
				Assert.That(Task.Delay(30).WaitWithoutInterruption(1000), Is.True);
			}
			finally
			{
				Script.TheScript = previous;
			}
		}

		[Test]
		public void ClipboardRouterPromotesAfterTransientNegative()
		{
			var fallback = new FakeClipboard("fallback");
			var compositor = new FakeClipboard("compositor");
			var available = false;
			var router = new RecoveringLinuxClipboard(
				fallback, compositor, () => available, (_, _) => null, retryIntervalMs: 60_000);

			Assert.That(router.GetText(), Is.EqualTo("fallback"));
			available = true;
			Assert.That(router.GetText(), Is.EqualTo("compositor"));
			router.SetText("promoted");
			Assert.That(compositor.Text, Is.EqualTo("promoted"));
			Assert.That(fallback.Text, Is.EqualTo("fallback"));
		}

		[Test]
		public void ClipboardMonitorRetriesAndReattachesAfterSignalFailure()
		{
			var fallback = new FakeClipboard("fallback");
			var compositor = new FakeClipboard("compositor");
			var available = false;
			var attempts = 0;
			var callbacks = 0;
			var signals = new List<Action>();
			var errors = new List<Action<Exception>>();
			var compositorSubscriptions = new List<TrackingDisposable>();
			var router = new RecoveringLinuxClipboard(
				fallback,
				compositor,
				() => available,
				(onChanged, onError) =>
				{
					attempts++;
					signals.Add(onChanged);
					errors.Add(onError);
					var subscription = new TrackingDisposable();
					compositorSubscriptions.Add(subscription);
					return subscription;
				},
				retryIntervalMs: 60_000);

			using var subscription = (RecoveringClipboardSubscription)router.Subscribe(() => callbacks++);
			Assert.That(subscription.HasCompositorSubscription, Is.False);
			Assert.That(fallback.SubscriptionDisposed, Is.False);

			available = true;
			Assert.That(subscription.TryAttachCompositor(), Is.True);
			Assert.That(subscription.HasCompositorSubscription, Is.True);
			Assert.That(attempts, Is.EqualTo(1));
			Assert.That(fallback.SubscriptionDisposed, Is.False,
				"the fallback watch remains hot so demotion has no UI-thread reattachment gap");
			signals[0]();
			Assert.That(callbacks, Is.EqualTo(1));
			fallback.RaiseChanged();
			Assert.That(callbacks, Is.EqualTo(1), "fallback events are gated while the compositor stream is healthy");

			errors[0](new IOException("signal stream disconnected"));
			Assert.That(subscription.HasCompositorSubscription, Is.False);
			Assert.That(compositorSubscriptions[0].Disposed, Is.True);
			Assert.That(callbacks, Is.EqualTo(2), "a gated fallback event is reconciled when the preferred stream fails");

			Assert.That(subscription.TryAttachCompositor(), Is.True);
			Assert.That(subscription.HasCompositorSubscription, Is.True);
			Assert.That(attempts, Is.EqualTo(2));
			Assert.That(callbacks, Is.EqualTo(3), "promotion reconciles the preferred clipboard's current state");

			// A late duplicate error from the old stream must not tear down its replacement.
			errors[0](new IOException("late old-stream error"));
			Assert.That(subscription.HasCompositorSubscription, Is.True);
		}

		private sealed class FakeClipboard(string text) : IClipboard
		{
			private readonly TrackingDisposable subscription = new();
			private Action onChanged;

			internal string Text { get; private set; } = text;
			internal bool SubscriptionDisposed => subscription.Disposed;

			public string GetText() => Text;
			public void SetText(string value) => Text = value ?? "";
			public bool IsEmpty => string.IsNullOrEmpty(Text);
			public int ChangeType() => IsEmpty ? 0 : 1;
			public Bitmap GetImage() => null;
			public void SetImage(Bitmap image) { }
			// The format layer is not what these tests exercise (they cover the compositor/Eto routing and its
			// recovery), so it is stubbed against the text this fake carries.
			public string[] GetFormats() => IsEmpty ? [] : ["text/plain"];
			public bool Has(string format) => !IsEmpty && format == "text/plain";
			public string[] KindFormats(ClipboardKind kind) => kind == ClipboardKind.Text ? ["text/plain"] : [];
			public bool HasKind(ClipboardKind kind) => kind == ClipboardKind.Text && !IsEmpty;
			public byte[] GetData(string format) => Has(format) ? Encoding.UTF8.GetBytes(Text) : null;
			public string GetKindText(ClipboardKind kind) => kind == ClipboardKind.Text ? Text : "";
			public string[] GetFiles() => [];
			public void SetAll(IReadOnlyList<ClipboardEntry> entries)
				=> Text = entries?.FirstOrDefault(e => e.Kind == ClipboardKind.Text).Value as string ?? "";
			public byte[] CaptureAll() => [];
			public void RestoreAll(Keysharp.Builtins.ClipboardAll clip) { }
			public IDisposable Subscribe(Action callback)
			{
				onChanged = callback;
				return subscription;
			}

			internal void RaiseChanged() => onChanged?.Invoke();
		}

		private sealed class TrackingDisposable : IDisposable
		{
			internal bool Disposed { get; private set; }
			public void Dispose() => Disposed = true;
		}
	}
}
#endif
