using Assert = NUnit.Framework.Legacy.ClassicAssert;

#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Input.Unix;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
#endif

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxKeyboardLayoutTests
	{
#if LINUX
		[Test, Category("Misc")]
		public void XkbMapperUsesConfiguredNonUsLayout()
		{
			using var mapper = new LinuxXkbCharMapperProvider(() => 0);
			mapper.ConfigureLayout("evdev", "pc105", "ee", null, null);

			Assert.IsTrue(mapper.TryMapRuneToKeystroke(new Rune('ä'), null, out var vk, out _, out _));
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

		[Test, Category("Internal")]
		public void DesktopKeyboardSnapshotRetainsRevisionAcrossUnavailableReply()
		{
			long now = 0;
			int calls = 0;
			var revisions = new List<string>();
			var replies = new[]
			{
				"{\"ok\":true,\"mapRevision\":\"one\",\"keymap\":\"map text\",\"group\":1}",
				null,
				"{\"ok\":true,\"mapRevision\":\"one\",\"group\":2}",
				"{\"ok\":true,\"mapRevision\":\"two\",\"validFields\":[\"mapRevision\"],\"group\":0}"
			};
			var state = new DesktopKeyboardState(revision =>
			{
				revisions.Add(revision);
				return replies[calls++] is string reply ? Encoding.UTF8.GetBytes(reply) : null;
			}, () => now, action => action());
			Assert.AreEqual("map text", state.Get().Keymap);
			Assert.AreEqual(1u, state.Get().Group);
			Assert.AreEqual(1, calls);
			now = 16;
			Assert.AreEqual("map text", state.Get().Keymap);
			now = 266;
			var restored = state.Get();
			Assert.AreEqual("map text", restored.Keymap);
			Assert.AreEqual(2u, restored.Group);
			Assert.IsTrue(restored.GroupKnown);
			now = 282;
			var changed = state.Get();
			Assert.IsNull(changed.Keymap);
			Assert.IsFalse(changed.GroupKnown);
			Assert.IsFalse(changed.ModifiersKnown);
			Assert.IsFalse(changed.IndicatorsKnown);
			Assert.That(revisions, Is.EqualTo(new string[] { null, "one", "one", "one" }));
			var malformed = DesktopKeyboardState.Parse("{\"ok\":true,\"group\":-1,\"modifiers\":\"0\",\"capsLock\":0,\"numLock\":false,\"scrollLock\":false,\"pointerMapping\":[\"3\",2,1]}");
			Assert.IsFalse(malformed.GroupKnown);
			Assert.IsFalse(malformed.ModifiersKnown);
			Assert.IsFalse(malformed.IndicatorsKnown);
			Assert.IsEmpty(malformed.PointerMapping);
		}

		[Test, Category("Internal")]
		public void DesktopKeyboardStateOwnsTheRevisionSentToTheBroker()
		{
			long now = 0;
			var calls = 0;
			var revisions = new List<string>();
			var replies = new[]
			{
				null,
				"{\"ok\":true,\"mapRevision\":\"one\",\"keymap\":\"map text\"}",
				"{\"ok\":true,\"mapRevision\":\"one\",\"group\":2}"
			};
			var state = new DesktopKeyboardState(revision =>
			{
				revisions.Add(revision);
				return replies[calls++] is string reply ? Encoding.UTF8.GetBytes(reply) : null;
			}, () => now, action => action());

			Assert.IsNull(state.Get());
			now = 250;
			Assert.AreEqual("map text", state.Get().Keymap);
			now = 266;
			Assert.AreEqual("map text", state.Get().Keymap);
			Assert.That(revisions, Is.EqualTo(new string[] { null, null, "one" }));
		}

		[Test, Category("Internal")]
		public void DesktopKeyboardStateReadDoesNotRunTheQueryInline()
		{
			var calls = 0;
			Action pending = null;
			var state = new DesktopKeyboardState(_ =>
			{
				calls++;
				return Encoding.UTF8.GetBytes(
					"{\"ok\":true,\"mapRevision\":\"one\",\"keymap\":\"map text\"}");
			}, () => 0, action => pending = action);

			Assert.That(state.Get(), Is.Null);
			Assert.That(calls, Is.Zero);
			Assert.That(pending, Is.Not.Null);
			pending();
			Assert.That(state.Get()?.Keymap, Is.EqualTo("map text"));
			Assert.That(calls, Is.EqualTo(1));
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
}
