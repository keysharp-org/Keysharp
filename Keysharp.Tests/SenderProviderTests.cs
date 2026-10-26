using Keysharp.Components.Input;

namespace Keysharp.Tests;

[TestFixture, Category("Curated")]
public class SenderProviderTests : TestRunner
{
	[Test, Category("Internal")]
	public void Registration()
	{
		Assert.Throws<ArgumentNullException>(() => s.RegisterSenderProvider(null));
		var sender = new Recorder();
		var registration = s.RegisterSenderProvider(sender);
		var rejected = new Recorder();
		Assert.Throws<InvalidOperationException>(() => s.RegisterSenderProvider(rejected));
		registration.Dispose();
		registration.Dispose();
		Assert.That(sender.Disposals, Is.EqualTo(1));
		Assert.That(rejected.Disposals, Is.Zero, "rejected registration retains caller ownership");

		var replacement = new Recorder();
		var replacementRegistration = s.RegisterSenderProvider(replacement);
		registration.Dispose();
		Assert.That(replacement.Disposals, Is.Zero, "an old handle cannot retire its replacement");
		s.Dispose();
		replacementRegistration.Dispose();
		Assert.That(replacement.Disposals, Is.EqualTo(1));
		Assert.Throws<ObjectDisposedException>(() => s.RegisterSenderProvider(rejected));
	}

#if WINDOWS
	[Test]
	public void Sending() => TestScript("sender-provider", false);

	[Test, Category("Internal")]
	public void OutputMarkers()
	{
		var hook = (WindowsHookThread)s.HookThread;
		var markers = new[] { KeyIgnore, KeyIgnoreAllExceptModifier, KeyIgnoreLevel(0), KeyIgnoreLevel(100) };
		Assert.That(markers.Any(marker => hook.IsKeybdEventArtificial(0, (ulong)marker) || hook.IsMouseEventArtificial(0, (ulong)marker)),
			Is.False, "another driver's ExtraInfo leaves physical input physical");

		using (s.RegisterSenderProvider(new Recorder()))
		{
			Assert.That(markers.All(marker => hook.IsKeybdEventArtificial(0, (ulong)marker) && hook.IsMouseEventArtificial(0, (ulong)marker)),
				Is.True, "a sender provider's driver output carries no injected flag");
			Assert.That(hook.IsKeybdEventArtificial(0, 0), Is.False);
			Assert.That(hook.IsKeybdEventArtificial(0, (ulong)KeyPhysIgnore), Is.False,
				"physical forwarding between scripts keeps its classification");
		}

		Assert.That(hook.IsKeybdEventArtificial(0, (ulong)KeyIgnore), Is.True,
			"output a removed provider already submitted keeps its classification");
	}

	[Test, Category("Internal")]
	public void CapsLock()
	{
		var hook = new CapsLockHook(s);
		s.HookThread.Stop();
		typeof(Script).GetProperty("HookThread", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(s, hook);
		var native = new Sender(s);
		hook.kbdMsSender = native;
		var provider = new Recorder();
		using var registration = s.RegisterSenderProvider(provider);
		s.Threads.CurrentThread.configData.storeCapsLockMode = true;

		string Send(string keys, bool capsLockOn)
		{
			hook.CapsLockOn = capsLockOn;
			Keyboard.SendPlay(keys);
			return Take(provider.Keys, key => $"{key.ScanCode}:{((key.Flags & 2) != 0 ? "U" : "D")}");
		}

		Assert.That(Send("{vk41sc01E}", true), Is.EqualTo("58:D,58:U,30:D,30:U,58:D,58:U"), "the batch turns CapsLock off and back on");
		Assert.That(Send("{vk41sc01E}", false), Is.EqualTo("30:D,30:U"));
		Assert.That(Send("{CapsLock}", true), Is.EqualTo("58:D,58:U,58:D,58:U"), "a send that turns CapsLock on itself is not toggled again");
		Assert.That(Send("{CapsLock down}{CapsLock down}{CapsLock up}", true), Is.EqualTo("58:D,58:U,58:D,58:D,58:U"),
			"a repeated down does not toggle CapsLock");
		Assert.That(Send("{CapsLock}{CapsLock down}", true), Is.EqualTo("58:D,58:U,58:D,58:U,58:D,58:U,58:D,58:U"),
			"a held CapsLock is released before the closing toggle, as ToggleKeyState does");
		Assert.That(Send("{vk41sc01E}{U+03A9}", true), Is.Empty);
		Assert.That(native.Native.Single(), Does.Not.EndWith("58:D,58:U"), "a native fallback carries no closing toggle");
		Assert.That(native.CapsLockRestores, Is.EqualTo(1), "it restores CapsLock as a native send does");
		native.ThrowNative = true;
		Assert.Throws<InvalidOperationException>(() => Send("{U+03A9}", true));
		native.ThrowNative = false;
		Assert.That(native.CapsLockRestores, Is.EqualTo(2), "a native fallback that throws still restores CapsLock");

		var calls = 0;
		provider.OnKey = () =>
		{
			if (++calls == 2)
				throw new InvalidOperationException("sender provider failed");
		};
		Assert.Throws<KeysharpException>(() => Keyboard.SendPlay("{vk41sc01E}"));
		provider.OnKey = null;
		Assert.That(Take(provider.Keys, key => $"{key.ScanCode}:{((key.Flags & 2) != 0 ? "U" : "D")}"), Is.EqualTo("58:D,58:U,58:D,58:U"),
			"a failure after the opening toggle releases CapsLock and turns it back on");
	}

	private static Recorder recorder;
	private static Sender sender;
	private static IDisposable registration;

	[TearDown]
	public void Retire()
	{
		registration?.Dispose();
		registration = null;
	}

	public static string Install()
	{
		var script = Script.TheScript;
		script.HookThread.kbdMsSender = sender = new Sender(script);
		registration = script.RegisterSenderProvider(recorder = new Recorder());
		return "";
	}

	public static string Keys() => Take(recorder.Keys, key => $"{key.ScanCode}:{((key.Flags & 2) != 0 ? "U" : "D")}");
	public static string Mouse() => Take(recorder.Mouse, mouse => $"{mouse.Flags}:{mouse.X}:{mouse.Y}");
	public static string Native() => Take(sender.Native, events => events, "|");
	public static string Disposals() => recorder.Disposals.ToString(CultureInfo.InvariantCulture);
	public static string Marked()
	{
		var hook = (WindowsHookThread)Script.TheScript.HookThread;
		return recorder.Keys.All(key => hook.IsSenderProviderOutput(key.ExtraInfo))
			&& recorder.Mouse.All(mouse => hook.IsSenderProviderOutput(mouse.ExtraInfo)) ? "marked" : "missing";
	}

	public static string FailKey(long Ordinal)
	{
		var calls = 0;
		recorder.OnKey = () =>
		{
			if (++calls != Ordinal) return;
			recorder.OnKey = null;
			throw new InvalidOperationException("sender provider failed");
		};
		return "";
	}

	public static string NestDuringKey()
	{
		recorder.OnKey = () =>
		{
			recorder.OnKey = null;
			Keyboard.SendPlay("{vk42sc030}");
			Keyboard.SendInput("{vk43sc02E}");
		};
		return "";
	}

	public static string NestHoldThenFail(long Depth)
	{
		void Hold(long remaining)
		{
			recorder.OnKey = remaining == 1 ? null : () => Hold(remaining - 1);
			Keyboard.SendPlay(remaining == 1 ? "{LControl down}" : "{vk42sc030}");
		}

		recorder.OnKey = () =>
		{
			Hold(Depth);
			recorder.OnKey = () =>
			{
				recorder.OnKey = null;
				throw new InvalidOperationException("sender provider failed");
			};
		};
		return "";
	}

	public static string RetireDuringKey()
	{
		recorder.OnKey = () =>
		{
			recorder.OnKey = null;
			registration.Dispose();
			if (recorder.Disposals != 0) throw new InvalidOperationException("the admitted send no longer owns the provider");
		};
		return "";
	}

	private static string Take<T>(List<T> items, Func<T, string> format, string separator = ",")
	{
		var text = string.Join(separator, items.Select(format));
		items.Clear();
		return text;
	}

	// Stands in for the user holding Control, which a plain key is sent without.
	public static string HoldControl()
	{
		sender.eventModifiersLR = MOD_LCONTROL;
		return "";
	}

	public static string ReleaseControl()
	{
		sender.eventModifiersLR = 0;
		return "";
	}

	private sealed class CapsLockHook(Script script) : WindowsHookThread(script, "Keysharp.Tests.SenderProvider")
	{
		internal bool CapsLockOn;
		internal override bool IsKeyToggledOn(uint vk) => vk == VK_CAPITAL ? CapsLockOn : base.IsKeyToggledOn(vk);
	}

	// Records native sends instead of injecting them, and reports the modifier state they would leave.
	private sealed class Sender(Script script) : WindowsKeyboardMouseSender(script)
	{
		internal readonly List<string> Native = [];
		internal int CapsLockRestores;
		internal bool ThrowNative;
		internal override uint GetModifierLRState(bool explicitlyGet = false) => eventModifiersLR;

		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			CapsLockRestores += vk == VK_CAPITAL && toggleValue == ToggleValueType.On ? 1 : 0;
			return ToggleValueType.Off;
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			if (sendMode != SendModes.Input || !UsesSenderProvider || !SendEventArrayToProvider())
			{
				Native.Add(string.Join(",", eventSi.Select(e => e.type == 1
					? $"{e.i.k.wScan}:{((e.i.k.dwFlags & 2) != 0 ? "U" : "D")}" : $"m{e.i.m.dwFlags}")));
				if (ThrowNative)
					throw new InvalidOperationException("native fallback failed");
			}
		}

		internal override void SendKeybdEvent(KeyEventTypes type, uint vk, uint sc, uint flags, long extra, bool repeat = false)
			=> throw new InvalidOperationException("unexpected native keyboard event");
	}
#endif

	private sealed class Recorder : ISenderProvider
	{
		internal int Disposals;
		internal readonly List<(ushort VirtualKey, ushort ScanCode, uint Flags, ulong ExtraInfo)> Keys = [];
		internal readonly List<(uint Flags, int X, int Y, uint MouseData, ulong ExtraInfo)> Mouse = [];
		internal Action OnKey { get; set; }

		public void KeybdEvent(ushort VirtualKey, ushort ScanCode, uint Flags, ulong ExtraInfo)
		{
			OnKey?.Invoke();
			Keys.Add((VirtualKey, ScanCode, Flags, ExtraInfo));
		}

		public void MouseEvent(uint Flags, int X, int Y, uint MouseData, ulong ExtraInfo)
			=> Mouse.Add((Flags, X, Y, MouseData, ExtraInfo));

		public void Dispose() => Disposals++;
	}
}
