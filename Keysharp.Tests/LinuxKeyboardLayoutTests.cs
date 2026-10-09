using Assert = NUnit.Framework.Legacy.ClassicAssert;

#if LINUX
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
#endif

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class LinuxKeyboardLayoutTests
{
#if LINUX
	[Test, Category("Misc")]
	public void XkbMapperMapsControlKeysWithReadyKeymap()
	{
		using var mapper = new LinuxXkbCharMapperProvider(() => 0);
		mapper.ConfigureLayout("evdev", "pc105", "us", null, null);
		Assert.AreNotEqual(nint.Zero, mapper.GetCurrentKeymapHandle());

		foreach (var (character, expectedVk) in new[]
		{
			('\b', VK_BACK), ('\t', VK_TAB), ('\r', VK_RETURN), ('\n', VK_RETURN),
		})
		{
			Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune(character), null,
				out var vk, out var needShift, out var needAltGr), $"{(int)character:X4}");
			Assert.AreEqual(expectedVk, vk);
			Assert.IsFalse(needShift);
			Assert.IsFalse(needAltGr);
		}
	}

	[Test, Category("Misc")]
	public void XkbMapperUsesConfiguredNonUsLayout()
	{
		using var mapper = new LinuxXkbCharMapperProvider(() => 0);
		mapper.ConfigureLayout("evdev", "pc105", "ee", null, null);

		Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune('ä'), null, out var vk, out _, out _));
		Assert.AreNotEqual(0u, vk);

		mapper.ConfigureLayout("evdev", "pc105", "us", null, null);
		Assert.IsFalse(mapper.TryMapRuneToKeystroke(new Rune('ä'), null, out _, out _, out _));
		Assert.IsFalse(mapper.TryMapRuneToKeystroke(new Rune('ä'), null, out _, out _, out _));
		mapper.ConfigureLayout("evdev", "pc105", "ee", null, null);
		Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune('ä'), null, out vk, out _, out _));
		Assert.AreNotEqual(0u, vk);
	}

	[Test, Category("Misc")]
	public void XkbMapperUsesActiveLayoutGroup()
	{
		var group = 1u;
		using var mapper = new LinuxXkbCharMapperProvider(() => group);
		mapper.ConfigureLayout("evdev", "pc105", "us,de", null, null);

		Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune('z'), null, out var vk, out var needShift, out var needAltGr));
		Assert.AreEqual((uint)'Y', vk);
		Assert.IsFalse(needShift);
		Assert.IsFalse(needAltGr);

		Assert.IsTrue(mapper.TryMapKeystrokeToRune((uint)'Y', false, false, out var rune));
		Assert.AreEqual('z', (char)rune.Value);

		group = 0;
		Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune('z'), null, out vk, out _, out _));
		Assert.AreEqual((uint)'Z', vk);
	}

	[Test]
	public void DesktopKeyboardStatePublishesRevisionDeltas()
	{
		Action<byte[]> publish = null;
		var calls = 0;
		var state = new DesktopKeyboardState((handler, _) =>
		{
			publish = handler;
			calls++;
			return new CallbackDisposable(() => { });
		});
		Assert.IsNull(state.Get());
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"mapRevision\":\"one\",\"keymap\":\"map text\",\"group\":1}"));
		Assert.AreEqual("map text", state.Get().Keymap);
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"mapRevision\":\"one\",\"group\":2}"));
		Assert.AreEqual("map text", state.Get().Keymap);
		Assert.AreEqual(2u, state.Get().Group);
		Assert.AreEqual(1, calls, "Current reads consume the pushed mirror without querying.");
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"mapRevision\":\"two\",\"validFields\":[\"mapRevision\"]}"));
		Assert.IsNull(state.Get().Keymap);
		Assert.IsFalse(state.Get().GroupKnown);
		state.Reset();
	}

	[Test]
	public void DesktopKeyboardStateDropsRetiredCallbacks()
	{
		Action<byte[]> publish = null;
		Action<Exception> fail = null;
		var retired = 0;
		var state = new DesktopKeyboardState((handler, onError) =>
		{
			publish = handler;
			fail = onError;
			return new CallbackDisposable(() => retired++);
		});
		state.Get();
		var oldPublish = publish;
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"group\":1}"));
		Assert.AreEqual(1u, state.Get().Group);
		fail(new IOException("service restarted"));
		Assert.IsNull(state.Get());
		oldPublish(Encoding.UTF8.GetBytes("{\"ok\":true,\"group\":8}"));
		Assert.IsNull(state.Get());
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"group\":2}"));
		Assert.AreEqual(2u, state.Get().Group);
		state.Reset();
		publish(Encoding.UTF8.GetBytes("{\"ok\":true,\"group\":9}"));
		Assert.IsNull(state.Get());
		Assert.AreEqual(2, retired);
		state.Reset();
	}

	[Test]
	public void DesktopKeyboardStateRejectsMalformedFields()
	{
		var malformed = DesktopKeyboardState.Parse("{\"ok\":true,\"group\":-1,\"modifiers\":\"0\",\"capsLock\":0,\"numLock\":false,\"scrollLock\":false,\"pointerMapping\":[\"3\",2,1]}");
		Assert.IsFalse(malformed.GroupKnown);
		Assert.IsFalse(malformed.ModifiersKnown);
		Assert.IsFalse(malformed.IndicatorsKnown);
		Assert.IsEmpty(malformed.PointerMapping);
	}

	private sealed class CallbackDisposable(Action dispose) : IDisposable
	{
		public void Dispose() => dispose();
	}

	[Test, Category("Internal")]
	public void XkbMapperReplacesDesktopKeymapAndMapsSpecialKeysLocally()
	{
		var snapshot = new DesktopKeyboardSnapshot { Keymap = ExportKeymap("us"), GroupKnown = true };
		using var mapper = new LinuxXkbCharMapperProvider(desktopStateOverride: () => snapshot);
		Assert.IsTrue(mapper.TryMapKeystrokeToRune((uint)'Y', false, false, out var rune));
		Assert.AreEqual('y', (char)rune.Value);
		Assert.IsTrue(mapper.TryMapVkToXKeycode(VK_F12, out var functionCode, false));
		Assert.AreNotEqual(0u, functionCode);
		Assert.IsTrue(mapper.TryMapVkToXKeycode(VK_LCONTROL, out var modifierCode, false));
		Assert.AreNotEqual(0u, modifierCode);
		snapshot = new DesktopKeyboardSnapshot { Keymap = ExportKeymap("de"), GroupKnown = true };
		Assert.IsTrue(mapper.TryMapKeystrokeToRune((uint)'Y', false, false, out rune));
		Assert.AreEqual('z', (char)rune.Value);
	}

	private static string ExportKeymap(string layout)
	{
		using var mapper = new LinuxXkbCharMapperProvider(() => 0);
		mapper.ConfigureLayout("evdev", "pc105", layout, null, null);
		var text = KeymapText(mapper.GetCurrentKeymapHandle(), 1);
		try { return Marshal.PtrToStringUTF8(text); }
		finally { Free(text); }
	}

	[DllImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_get_as_string")]
	private static extern nint KeymapText(nint keymap, int format);
	[DllImport("libc", EntryPoint = "free")]
	private static extern void Free(nint pointer);
#endif
}
