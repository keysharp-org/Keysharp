using Keysharp.Components.Input;

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class HookProviderTests : TestRunner
{
	[Test]
	public void Registration()
	{
		Assert.Throws<ArgumentNullException>(() => s.RegisterHookProvider(null));
		using var workerProvider = new Provider();
		Task.Run(() => Assert.Throws<InvalidOperationException>(() => s.RegisterHookProvider(workerProvider))).GetAwaiter().GetResult();
		Assert.That(workerProvider.Disposals, Is.Zero, "rejected registration leaves ownership with its caller");
		var provider = new Provider();
		var registration = s.RegisterHookProvider(provider);
		using var rejected = new Provider();
		Assert.Throws<InvalidOperationException>(() => s.RegisterHookProvider(rejected));
		Assert.That(rejected.Disposals, Is.Zero);
		registration.Dispose();
		registration.Dispose();
		Assert.That(provider.Disposals, Is.EqualTo(1));
		s.Dispose();
		Assert.That(provider.Disposals, Is.EqualTo(1));
		Assert.Throws<ObjectDisposedException>(() => s.RegisterHookProvider(new Provider()));
	}

	[Test]
	public void DeviceState()
	{
		var provider = new Provider();
		var hook = Install(provider);
		var events = ObserveKeys(out var drain);
		var devices = (Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null);
		var keyboard = (Ks.Input)devices.array[0];
		var other = (Ks.Input)devices.array[1];
		Assert.Throws<KeysharpException>(() => keyboard.GetKeyState("A"));
		Assert.That(provider.Key(0x100, 0x41), Is.False);
		Assert.That(provider.Forwards, Is.EqualTo(1));
		Assert.That(keyboard.GetKeyState("A"), Is.EqualTo(true));
		Assert.Throws<KeysharpException>(() => other.GetKeyState("A"));
		drain();
		var eventInfo = events.Single();
		var lookups = provider.Lookups;
		Assert.That(Script.GetPropertyValue(eventInfo, "IsInjected"), Is.EqualTo(false));
		Assert.That(provider.Lookups, Is.EqualTo(lookups));
		var eventDevice = (Ks.Input)Script.GetPropertyValue(eventInfo, "Device");
		Assert.That((eventDevice.Id, eventDevice.Kind, eventDevice.Name), Is.EqualTo((5L, "Keyboard", "First")));
		Assert.That(KeysharpObject.HasOwnProp(eventInfo, "DeviceId"), Is.Zero);
		Assert.That(Script.GetPropertyValue(eventInfo, "Device"), Is.SameAs(eventDevice));
		Assert.That(provider.Key(0x101, 0x41, 0x80), Is.False);
		Assert.That(keyboard.GetKeyState("A"), Is.EqualTo(false));
		hook.AddRemoveHooks(HookType.Keyboard);
		Assert.That(keyboard.GetKeyState("A"), Is.EqualTo(false), "changing mouse capture preserves keyboard descriptors");
		provider.Key(0x100, 0x41);
		provider.Key(0x100, 0x41, id: 6);
		provider.Key(0x101, 0x41, 0x80);
		Assert.That(other.GetKeyState("A"), Is.EqualTo(true));
		Assert.That(s.KeyState.GetState(0x41, KeyStateTypes.Physical), Is.False,
			"provider history tracks global transitions; device queries remain independent");
		Assert.That(Keyboard.GetKeyState("vkFFFFFFFF", "P"), Is.EqualTo(false));
		provider.Key(0x101, 0x41, 0x80, 6);

		s.KeyboardData.blockInput = true;
		try
		{
			Assert.That(provider.Key(0x100, 0xA1), Is.True);
			Assert.That(keyboard.GetKeyState("Shift"), Is.EqualTo(true));
			Assert.That(provider.Key(0x101, 0xA1, 0x80), Is.True);
			Assert.That(keyboard.GetKeyState("Shift"), Is.EqualTo(false));
		}
		finally { s.KeyboardData.blockInput = false; }
		provider.Devices[0] = new(5, "Keyboard", "Replacement", HardwareId: "Replacement");
		var replacement = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		Assert.Throws<KeysharpException>(() => keyboard.GetKeyState("A"));
		Assert.Throws<KeysharpException>(() => replacement.GetKeyState("A"), "a reused slot must not inherit earlier holds");
		provider.Key(0x100, 0x41);
		Assert.That(replacement.GetKeyState("A"), Is.EqualTo(true));
		hook.Dispose();
		Assert.Throws<KeysharpException>(() => keyboard.GetKeyState("A"));
		Assert.That(provider.Disposals, Is.EqualTo(1));
	}

	[Test]
	public void SenderProviderOutput()
	{
		var provider = new Provider();
		_ = Install(provider);
		var keyboard = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		using var sending = s.RegisterSenderProvider(new NoSender());
		var marker = (ulong)KeyIgnoreLevel(0);
		s.KeyboardData.blockInput = true;

		try
		{
			Assert.That(provider.Key(0x100, 0x41, extraInfo: marker), Is.False, "BlockInput leaves the script's own flagless output alone");
			_ = provider.Key(0x101, 0x41, 0x80, extraInfo: marker);
		}
		finally { s.KeyboardData.blockInput = false; }

		Assert.Throws<KeysharpException>(() => keyboard.GetKeyState("A"), "that output records no physical device state");
	}

	[TestCase(false), TestCase(true)]
	public void ReusedDeviceState(bool mouse)
	{
		var provider = new Provider();
		var kind = mouse ? "Mouse" : "Keyboard";
		provider.Devices[0] = new(5, kind, "First", HardwareId: "First");
		_ = Install(provider);
		var events = ObserveKeys(out var drain);
		var original = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		bool Press(uint vk, uint message) => mouse
			? provider.Host.MouseEvent(message, 10, 20, 0, 0, 1, 0, 5, 0, 0)
			: provider.Key(0x100, vk);
		Press(mouse ? VK_LBUTTON : 0x41, 0x201);
		Press(mouse ? VK_MBUTTON : 0x43, 0x207);
		Assert.That(original.GetKeyState(mouse ? "LButton" : "A"), Is.EqualTo(true));

		provider.Devices[0] = new(5, kind, "Replacement", HardwareId: "Replacement");
		Press(mouse ? VK_LBUTTON : 0x41, 0x201);
		Press(mouse ? VK_RBUTTON : 0x42, 0x204);
		drain();
		var replacement = mouse
			? (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0]
			: (Ks.Input)Script.GetPropertyValue(events.Last(), "Device");
		Assert.That(replacement.HardwareId, Is.EqualTo("Replacement"));
		Assert.Throws<KeysharpException>(() => original.GetKeyState(mouse ? "LButton" : "A"));
		Assert.That(replacement.GetKeyState(mouse ? "LButton" : "A"), Is.EqualTo(true),
			"a replacement's first down must survive an old held scan or button and lazy metadata lookup");
		Assert.That(replacement.GetKeyState(mouse ? "RButton" : "B"), Is.EqualTo(true));
		Assert.That(replacement.GetKeyState(mouse ? "MButton" : "C"), Is.EqualTo(false),
			"the previous attachment's other holds must be forgotten");
	}

	[Test]
	public void MissingMetadataIsUnchanged()
	{
		var provider = new Provider();
		var hook = Install(provider);
		provider.Devices[0] = new(5, "Keyboard", "First", HardwareId: "First")
			{ Manufacturer = null, Connection = null, Location = null };
		var previous = hook.GetInputDevices();
		provider.Devices[0] = new(5, "Keyboard", "First", HardwareId: "First");
		Assert.That(Keysharp.Internals.Input.InputEventManager.Classify(previous, hook.GetInputDevices()), Is.Empty);
	}

	[TestCase(false), TestCase(true)]
	public void UnavailableDeviceState(bool throws)
	{
		var provider = new Provider();
		var hook = Install(provider);
		var device = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		provider.Key(0x100, 0x41);
		Assert.That(device.GetKeyState("A"), Is.EqualTo(true));
		provider.DeviceInfoUnavailable = !throws;
		provider.DeviceInfoFailure = throws ? new InvalidOperationException("Device metadata unavailable.") : null;
		provider.Key(0x101, 0x41, 0x80);
		provider.DeviceInfoUnavailable = false;
		provider.DeviceInfoFailure = null;
		Assert.Throws<KeysharpException>(() => device.GetKeyState("A"), "unavailable identity invalidates earlier history");
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.Keyboard | HookType.Mouse));
		provider.Key(0x100, 0x42);
		Assert.That(device.GetKeyState("B"), Is.EqualTo(true));
	}

	[Test]
	public void DeviceLookupScope()
	{
		var provider = new Provider();
		_ = Install(provider);
		provider.Key(0x100, 0x41);
		Assert.That(provider.Lookups, Is.EqualTo(1));
		provider.Key(0x100, 0x42, id: 0);
		provider.Key(0x101, 0x41, 0x90);
		provider.Host.MouseEvent(0x200, 10, 20, 0, 0, 1, 0, 5, 1, 1);
		provider.Host.MouseEvent(0x20A, 10, 20, 120u << 16, 0, 1, 0, 5, 0, 0);
		Assert.That(provider.Lookups, Is.EqualTo(1), "only identified physical key or button transitions need validation");
	}

	[TestCase(0), TestCase(1), TestCase(2)]
	public void StaleDeviceMetadata(int query)
	{
		var provider = new Provider();
		_ = Install(provider);
		var events = ObserveKeys(out var drain);
		var original = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		provider.Key(0x100, 0x41);
		drain();
		void Replace()
		{
			provider.AfterDevices = provider.AfterDeviceInfo = null;
			provider.Devices[0] = new(5, "Keyboard", "Replacement", HardwareId: "Replacement");
			provider.Key(0x100, 0x42);
		}
		if (query == 0)
		{
			provider.AfterDevices = Replace;
			_ = Ks.Input.staticget_Devices(null);
		}
		else
		{
			provider.AfterDeviceInfo = Replace;
			if (query == 1) Assert.Throws<KeysharpException>(() => original.GetKeyState("A"));
			else _ = Script.GetPropertyValue(events.Single(), "Device");
		}
		var replacement = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		Assert.That(replacement.GetKeyState("B"), Is.EqualTo(true), "stale metadata must preserve newer attachment history");
		Assert.That(replacement.GetKeyState("A"), Is.EqualTo(false));
	}

	[Test]
	public void HookInstallation()
	{
		var provider = new Provider();
		var hook = Install(provider);
		hook.AddRemoveHooks(HookType.None);
		var context = UseQueuedMainContext();
		var input = new InputHook("V");
		_ = input.Start();
		Assert.That(input.InProgress, Is.True, "provider-backed InputHook works without a native input service or display");
		Assert.That(hook.HasKbdHook(), Is.True);
		_ = input.Stop();
		context.DrainAll();
		Assert.That(s.input, Is.Null);
		_ = Keyboard.InstallKeybdHook();
		Assert.That(provider.Selections.Last(), Is.EqualTo((true, false)));
#if WINDOWS
		var selections = provider.Selections.Count;
		hook.NativeFailureKinds = HookType.Mouse;
		hook.AddRemoveHooks(HookType.Mouse);
		Assert.That(provider.Selections.Count, Is.EqualTo(selections));
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.Keyboard));
		Assert.That(hook.kbdHook, Is.Not.EqualTo(IntPtr.Zero), "failed observer replacement restores the previous keyboard observer");
		Assert.That(hook.mouseHook, Is.EqualTo(IntPtr.Zero));
		hook.NativeFailureKinds = null;
#endif
		_ = Keyboard.InstallMouseHook();
		Assert.That(provider.Selections.Last(), Is.EqualTo((true, true)),
			"a provider can install capture without native input services, including in a headless host");
		_ = Keyboard.BlockInput("MouseMove");
		try
		{
			Assert.That(provider.Host.MouseEvent(0x200, 10, 20, 0, 0, 1, 0, 0, 2, 3), Is.True);
		}
		finally { _ = Keyboard.BlockInput("MouseMoveOff"); }
		Assert.That(provider.Host.MouseEvent(0x200, 10, 20, 0, 0, 1, 0, 0, 2, 3), Is.False);
	}

	[Test]
	public void CaptureReset()
	{
		var provider = new Provider();
		_ = Install(provider);
		var device = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		provider.Key(0x100, 0x41);
		Assert.That(device.GetKeyState("A"), Is.EqualTo(true));
		provider.Host.ResetState();
		Assert.Throws<KeysharpException>(() => device.GetKeyState("A"));
		var fresh = (Ks.Input)((Keysharp.Builtins.Array)Ks.Input.staticget_Devices(null)).array[0];
		Assert.Throws<KeysharpException>(() => fresh.GetKeyState("A"), "a capture gap invalidates prior physical history");
		provider.Key(0x100, 0x41);
		Assert.That(fresh.GetKeyState("A"), Is.EqualTo(true));
	}

#if WINDOWS
	[Test]
	public void NativeObservation()
	{
		var provider = new Provider();
		var hook = Install(provider);
		var events = ObserveKeys(out var drain);
		provider.Key(0x100, 0x41);
		drain();
		var device = (Ks.Input)Script.GetPropertyValue(events.Single(), "Device");
		Assert.That(device.GetKeyState("A"), Is.EqualTo(true), "first metadata access preserves the event's tracked state");
		hook.NativeKey(0x100, 0x41, 0x1E, 0);
		drain();
		Assert.That(events.Count, Is.EqualTo(1), "a forwarded physical event must not be processed twice");
		hook.NativeKey(0x101, 0x41, 0x1E, 0x90);
		drain();
		Assert.That(events.Count, Is.EqualTo(2), "native injected events still enter hook processing");
		Assert.That(provider.Forwards, Is.EqualTo(1), "an injected event must not forward a driver's pending packet");
		hook.AddRemoveHooks(HookType.None, true);
		Assert.That((hook.HasKbdHook(), hook.HasMouseHook(), hook.GetActiveHooks()),
			Is.EqualTo((true, true, HookType.Keyboard | HookType.Mouse)));
		Assert.That(provider.Selections.Last(), Is.EqualTo((true, true)), "SendInput removes native observers, not driver capture");
		provider.Key(0x100, 0x42);
		drain();
		Assert.That(events.Count, Is.EqualTo(2), "physical history continues without launching callbacks during SendInput");
		Assert.That(hook.physicalKeyState[0x42], Is.Not.Zero);
		provider.Key(0x100, VK_RSHIFT);
		hook.NativeLogicalModifiers = 0;
		Assert.That(hook.kbdMsSender.GetModifierLRState(true), Is.Zero);
		Assert.That(hook.kbdMsSender.modifiersLRLogical & MOD_RSHIFT, Is.Zero);
		Assert.That(hook.kbdMsSender.modifiersLRPhysical & MOD_RSHIFT, Is.EqualTo(MOD_RSHIFT));
		Assert.That(hook.physicalKeyState[VK_RSHIFT], Is.Not.Zero, "logical repair must preserve a captured physical hold");
		hook.NativeLogicalModifiers = null;
		provider.Key(0x101, VK_RSHIFT, 0x80);
		Assert.That(hook.physicalKeyState[VK_RSHIFT], Is.Zero);
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => 1L)));
		provider.Host.CaptureLost("Capture ended.");
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.None), "provider loss during SendInput leaves no capture until observers return");
		hook.AddRemoveHooks(HookType.Keyboard | HookType.Mouse, true);
		Assert.That((hook.HasKbdHook(), hook.HasMouseHook(), hook.GetActiveHooks()),
			Is.EqualTo((true, true, HookType.Keyboard | HookType.Mouse)));
		hook.NativeKey(0x100, 0x43, 0x2E, 0);
		drain();
		Assert.That(events.Count, Is.EqualTo(3), "native physical capture resumes after provider loss");
		Assert.That(KeysharpObject.HasOwnProp(events.Last(), "Device"), Is.Zero);
		hook.NativeKey(0x100, VK_RSHIFT, 0x36, 0);
		hook.kbdMsSender.modifiersLRLastPressed = 0;
		hook.NativeLogicalModifiers = 0;
		Assert.That(hook.kbdMsSender.GetModifierLRState(true), Is.Zero);
		Assert.That(hook.kbdMsSender.modifiersLRPhysical & MOD_RSHIFT, Is.Zero);
		Assert.That(hook.physicalKeyState[VK_RSHIFT], Is.Zero, "native capture repairs missed physical releases after provider loss");
		hook.NativeLogicalModifiers = null;
		provider.RejectSelection = true;
		hook.AddRemoveHooks(HookType.Keyboard);
		drain();
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.Keyboard | HookType.Mouse), "rejected recapture preserves native capture");
		hook.Dispose();
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.Keyboard | HookType.Mouse));
		Assert.That(provider.Disposals, Is.EqualTo(1));
	}
#endif

#if !WINDOWS
	[Test]
	public void InjectedStream()
	{
		var provider = new Provider();
		var hook = Install(provider);
		var events = ObserveKeys(out var drain);
		provider.Key(0x100, 0x41);
		drain();
		var device = (Ks.Input)Script.GetPropertyValue(events.Single(), "Device");
		s.timeLastInputPhysical = s.timeLastInputKeyboard = DateTime.UnixEpoch;
		provider.Key(0x101, 0x41, 0x90);
		drain();
		Assert.That(Script.GetPropertyValue(events.Last(), "IsInjected"), Is.EqualTo(true));
		Assert.That(device.GetKeyState("A"), Is.EqualTo(true), "injected release does not end a physical device hold");
		Assert.That(s.KeyState.GetState(0x41, KeyStateTypes.Physical), Is.True);
		provider.Key(0x100, 0x42, 0x10);
		Assert.That(device.GetKeyState("B"), Is.EqualTo(false), "injected presses do not create physical device holds");
		Assert.That(s.timeLastInputPhysical, Is.EqualTo(DateTime.UnixEpoch));
		Assert.That(s.timeLastInputKeyboard, Is.EqualTo(DateTime.UnixEpoch));
		Assert.That(provider.Forwards, Is.EqualTo(3));
		provider.Key(0x100, VK_RSHIFT);
		Assert.That(Keyboard.GetKeyState("RShift"), Is.EqualTo(true));
		provider.Key(0x101, VK_RSHIFT, 0x80);
		Assert.That(Keyboard.GetKeyState("RShift"), Is.EqualTo(false));
	}

	[Test]
	public void ReleaseAfterCaptureLoss()
	{
		var provider = new Provider();
		var hook = Install(provider);
		s.HotkeyData.whichHookAlways = HookType.Keyboard;
		provider.Host.CaptureLost("Capture ended.");
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.None));
		hook.Dispose();
		Assert.That(hook.GetActiveHooks(), Is.EqualTo(HookType.Keyboard), "removal restores existing capture demand after loss");
		Assert.That(provider.Disposals, Is.EqualTo(1));
	}
#endif

	[Test]
	public void Timeout()
	{
		var provider = new Provider();
		var hook = Install(provider);
		hook.Defer = true;
		hook.DispatchTimeoutMilliseconds = 10;
		Assert.That(provider.Key(0x100, 0x41), Is.False);
		hook.Drain();
		Assert.That(hook.physicalKeyState[0x41], Is.Zero, "an expired event must not run later");
		Assert.That(provider.Forwards, Is.Zero, "the provider forwards a timed-out event after the host returns false");
		Assert.That(provider.Selections.Last(), Is.EqualTo((false, false)));
	}

	[TestCase(false), TestCase(true)]
	public void InputEndAfterDeadline(bool fromWorker)
	{
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => 1L)));
		var provider = new Provider();
		var hook = Install(provider);
		_ = ObserveKeys(out var drain);
		var input = s.input;
		hook.DispatchTimeoutMilliseconds = 10;
		hook.OnKeyPost = () =>
		{
			Thread.Sleep(20);
			if (fromWorker) Task.Run(input.Stop).GetAwaiter().GetResult();
			else input.Stop();
		};
		Assert.That(provider.Key(0x100, 0x41), Is.False);
		drain();
		Assert.That(s.input, Is.Null, "ending an input must release its chain even after the event deadline");
	}

	[Test]
	public void ActiveRemoval()
	{
		var provider = new Provider();
		var hook = Install(provider);
		hook.Defer = true;
		hook.DispatchTimeoutMilliseconds = 10_000;
		var call = Task.Run(() => provider.Key(0x100, 0x41));
		Assert.IsTrue(hook.Queued.Wait(TimeSpan.FromSeconds(5)));
		try
		{
			hook.Dispose();
			Assert.That(provider.Disposals, Is.Zero, "removal leaves an admitted callback's provider alive");
		}
		finally
		{
			hook.Drain();
			Assert.IsTrue(call.Wait(TimeSpan.FromSeconds(5)));
		}
		Assert.That(call.Result, Is.False);
		Assert.That(provider.Disposals, Is.EqualTo(1));
		Assert.That(hook.physicalKeyState[0x41], Is.Zero);
	}

	[TestCase(0), TestCase(1), TestCase(2)]
	public void QueuedCaptureChange(int change)
	{
		var provider = new Provider();
		var hook = Install(provider);
		// A finalizer can queue a wake before the test replaces the main-thread transport.
		_ = s.MainEventScheduler.EnqueueCallback(() => { });
		var events = ObserveKeys(out var drain);
		if (change == 2) _ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => 1L)));
		provider.Key(0x100, 0x41);
		hook.Defer = true;
		hook.DispatchTimeoutMilliseconds = 10_000;
		var call = Task.Run(() => provider.Key(0x101, 0x41, 0x80));
		Assert.IsTrue(hook.Queued.Wait(TimeSpan.FromSeconds(5)));
		try
		{
			provider.RejectSelection = change == 2;
			hook.AddRemoveHooks(change == 0 ? HookType.Keyboard : HookType.None);
			if (change == 1) hook.AddRemoveHooks(HookType.Keyboard | HookType.Mouse);
		}
		finally
		{
			hook.Drain();
			Assert.IsTrue(call.Wait(TimeSpan.FromSeconds(5)));
		}
		drain();
		Assert.That(events.Count, Is.EqualTo(2), "an admitted release remains valid while its capture kind is active");
		Assert.That(s.KeyState.GetState(0x41, KeyStateTypes.Physical), Is.False);
	}

	private List<object> ObserveKeys(out Action drain)
	{
		var context = UseQueuedMainContext();
		var events = new List<object>();
		var callback = new KeysharpFunc((Action<object, object, object>)((_, _, _) => events.Add(ThreadAccessors.A_EventInfo)));
		var io = new InputHook("V") { OnKeyDown = callback, OnKeyUp = callback };
		_ = io.KeyOpt("{All}", "N");
		io.input.Start();
		s.input = io.input;
		drain = context.DrainAll;
		return events;
	}

	private Site Install(Provider provider)
	{
		var original = s.HookThread;
		var hook = new Site(s, provider);
		original.Stop();
		// Inject a hook with inert OS installation; driver tests must not hook the user's keyboard.
		typeof(Script).GetProperty("HookThread", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
			.SetValue(s, hook);
		hook.ChangeHookState([], HookType.Keyboard | HookType.Mouse, HookType.Keyboard | HookType.Mouse);
		return hook;
	}

	private sealed class Site(Script script, IHookProvider provider)
		: ProviderHookThread(script, "Keysharp.Tests", provider)
	{
		private readonly Queue<Action> queue = new();
		internal readonly ManualResetEventSlim Queued = new();
		internal bool Defer;
		internal Action OnKeyPost;
		protected internal override void Start() { }
		internal override object Invoke(Func<object> callback) => callback();
		protected override void AddRemoveNativeHooks(HookType kinds, bool temporary)
		{
#if WINDOWS
			if (kinds == NativeFailureKinds) kinds = HookType.None;
			kbdHook = (kinds & HookType.Keyboard) != 0 ? 1 : 0;
			mouseHook = (kinds & HookType.Mouse) != 0 ? 1 : 0;
#endif
			if (!UsesHookProvider) activeHooks = kinds;
		}
		protected override void QueueOnHookThread(Action callback)
		{
			if (!Defer) { callback(); return; }
			lock (queue) queue.Enqueue(callback);
			Queued.Set();
		}
		internal void Drain()
		{
			while (true)
			{
				Action callback;
				lock (queue) { if (queue.Count == 0) return; callback = queue.Dequeue(); }
				callback();
			}
		}
		internal override bool PostMessage(KeysharpMsg message)
		{
			if (message.message == (uint)UserMessages.AHK_INPUT_KEYDOWN) OnKeyPost?.Invoke();
			return base.PostMessage(message);
		}
#if WINDOWS
		internal uint? NativeLogicalModifiers;
		internal HookType? NativeFailureKinds;
		internal override uint GetModifierLRStateLogical() => NativeLogicalModifiers ?? base.GetModifierLRStateLogical();
		internal void NativeKey(uint message, uint vk, uint scan, uint flags)
		{
			var input = new Keysharp.Internals.Os.Windows.KBDLLHOOKSTRUCT
			{ vkCode = vk, scanCode = scan, flags = flags };
			_ = KeybdHookCallback(0, (nint)message, ref input);
		}
#endif
		internal override void Unhook()
		{
			activeHooks = HookType.None;
#if WINDOWS
			kbdHook = mouseHook = 0;
#endif
		}
		internal override void Unhook(nint hook) { }
	}

	private sealed class NoSender : ISenderProvider
	{
		public void KeybdEvent(ushort VirtualKey, ushort ScanCode, uint Flags, ulong ExtraInfo) { }
		public void MouseEvent(uint Flags, int X, int Y, uint MouseData, ulong ExtraInfo) { }
		public void Dispose() { }
	}

	private sealed class Provider : IHookProvider, IInputDeviceProvider
	{
		internal IHookHost Host;
		internal int Forwards, Disposals, Lookups;
		internal bool RejectSelection = false;
		internal bool DeviceInfoUnavailable;
		internal Exception DeviceInfoFailure;
		internal Action AfterDevices, AfterDeviceInfo;
		internal readonly List<(bool Keyboard, bool Mouse)> Selections = [];
		internal readonly Keysharp.Components.Input.InputDeviceInfo[] Devices =
			[new(5, "Keyboard", "First", HardwareId: "First"), new(6, "Keyboard", "Second", HardwareId: "Second")];
		public bool SetHooks(bool Keyboard, bool Mouse, IHookHost Host, out string Message)
		{
			this.Host = Host;
			Selections.Add((Keyboard, Mouse));
			Message = RejectSelection ? "Selection rejected." : "";
			return !RejectSelection;
		}
		internal bool Key(uint message, uint vk, uint flags = 0, long id = 5, ulong extraInfo = 0)
			=> Host.KeyboardEvent(message, vk, KeyCodes.MapVkToSc(vk), flags, 1, extraInfo, id);
		public void CallNextHook() => Forwards++;
		public void Dispose() => Disposals++;
		public Keysharp.Components.Input.InputDeviceInfo[] GetDevices()
		{
			var result = Devices.ToArray();
			AfterDevices?.Invoke();
			return result;
		}
		public bool TryGetDeviceInfo(long DeviceId, string Kind, out Keysharp.Components.Input.InputDeviceInfo Device)
		{
			Lookups++;
			if (DeviceInfoFailure != null) throw DeviceInfoFailure;
			if (DeviceInfoUnavailable) { Device = null; return false; }
			Device = Devices.FirstOrDefault(item => item.Id == DeviceId && item.Kind == Kind);
			AfterDeviceInfo?.Invoke();
			return Device != null;
		}
	}
}
