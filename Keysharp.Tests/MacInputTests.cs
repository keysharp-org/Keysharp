#if OSX
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class MacInputTests
{
	// The native-tap tests hand the tap an owner Script whose IsDisposed gates its callbacks; the process-wide
	// TheScript here is usually a prior fixture's retired (disposed) instance, so the fixture owns a live one.
	private Script script;

	[OneTimeSetUp]
	public void CreateOwnerScript() => script = new Script();

	[OneTimeTearDown]
	public void DisposeOwnerScript()
	{
		script?.Dispose();
		script = null;
	}

	[Test]
	public void CoveredWindowIdentity()
	{
		var bounds = new Rectangle(100, 100, 640, 480);
		MacNativeWindow[] windows =
		[
			new(11, 2, "Cover", "Target", bounds, true, 1),
			new(12, 1, "App", "Target", new Rectangle(120, 120, 320, 240), true, 1),
			new(13, 1, "App", "Target", bounds, false, 1)
		];

		Assert.IsTrue(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "Target", out var id));
		Assert.AreEqual(13u, id, "a covering window or off-screen state changed the identity");
		Assert.IsFalse(MacNativeWindows.TryMatchWindow(windows, 3, bounds, "Target", out _));
	}

	[Test]
	public void SameBoundsIdentity()
	{
		var bounds = new Rectangle(100, 100, 640, 480);
		MacNativeWindow[] windows =
		[
			new(11, 1, "App", "First", bounds, true, 1),
			new(12, 1, "App", "Second", bounds, true, 1)
		];

		Assert.IsTrue(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "Second", out var id));
		Assert.AreEqual(12u, id);
		Assert.IsFalse(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "Missing", out _));
		Assert.IsFalse(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "", out _));

		windows[0] = new(11, 1, "App", "", bounds, true, 1);
		Assert.IsFalse(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "", out _),
			"an unavailable title must not disambiguate windows with the same bounds");

		windows[0] = new(11, 1, "App", "Second", bounds, true, 1);
		Assert.IsFalse(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "Second", out _),
			"ambiguous bounds and titles must not choose an arbitrary window");
	}

	[Test]
	public void UntitledWindowIdentity()
	{
		var bounds = new Rectangle(100, 100, 640, 480);
		MacNativeWindow[] windows = [new(11, 1, "App", "", bounds, false, 1)];
		Assert.IsTrue(MacNativeWindows.TryMatchWindow(windows, 1, bounds, "Unavailable title", out var id));
		Assert.AreEqual(11u, id);
	}

	[Test]
	public void HiddenApplicationWindows()
	{
		var frame = new Rectangle(100, 100, 640, 480);
		var minimized = new MacNativeWindow(11, 1, "App", "Window", frame, false, 1);
		var hidden = new MacNativeWindow(11, 1, "App", "Window", frame, true, 1, isApplicationHidden: true);

		Assert.IsTrue(minimized.Visible, "minimization must not hide a window from title searches");
		Assert.IsFalse(minimized.VisibleOnScreen);
		Assert.IsFalse(hidden.Visible, "a hidden application's window must require DetectHiddenWindows");
		Assert.IsFalse(hidden.VisibleOnScreen);
	}

	[Test, Category("Input")]
	public void InjectedMetadata()
	{
		var kinds = new[]
		{
			MacNativeInput.InjectedEventKind.None,
			MacNativeInput.InjectedEventKind.KeyUp,
			MacNativeInput.InjectedEventKind.UnicodeText,
			MacNativeInput.InjectedEventKind.UnicodeText | MacNativeInput.InjectedEventKind.KeyUp,
			MacNativeInput.InjectedEventKind.Mouse
		};
		var values = new[] { 0L, KeyIgnore, KeyBlockThis, -1L, -(1L << 35), (1L << 35) - 1 };

		foreach (var kind in kinds)
		{
			foreach (var value in values)
			{
				var encoded = MacNativeInput.EncodeInjectedExtraInfo(value, kind);
				Assert.IsTrue(MacNativeInput.TryDecodeInjectedExtraInfo(encoded, out var decoded, out var decodedKind));
				Assert.AreEqual(value, decoded);
				Assert.AreEqual(kind, decodedKind);
			}
		}

		Assert.IsFalse(MacNativeInput.TryDecodeInjectedExtraInfo(KeyIgnore, out var legacy, out _));
		Assert.AreEqual(KeyIgnore, legacy);
	}

	// Regression: the Alt-Tab hook action synthesizes a bare VK_SHIFT to make ShiftAltTab move backward
	// (Cmd+Shift+Tab in the macOS App Switcher). MapMacKeyCodeToVk only yields the left/right-specific
	// modifiers, so without an explicit alias the neutral VK_SHIFT/VK_CONTROL/VK_MENU map to nothing and
	// CreateKeyboardEvent drops the event — leaving ShiftAltTab to advance forward like AltTab.
	[Test, Category("Input")]
	public void NeutralModifiers()
	{
		Assert.IsTrue(KeyCodes.TryMapVkToMacCode(VK_SHIFT, out var shift));
		Assert.AreEqual(0x38u, shift);    // kVK_Shift (left)
		Assert.IsTrue(KeyCodes.TryMapVkToMacCode(VK_CONTROL, out var control));
		Assert.AreEqual(0x3Bu, control);  // kVK_Control (left)
		Assert.IsTrue(KeyCodes.TryMapVkToMacCode(VK_MENU, out var menu));
		Assert.AreEqual(0x3Au, menu);     // kVK_Option (left)

		// The left/right-specific keys still resolve to their own key codes.
		Assert.IsTrue(KeyCodes.TryMapVkToMacCode(VK_LSHIFT, out var lshift));
		Assert.AreEqual(0x38u, lshift);
		Assert.IsTrue(KeyCodes.TryMapVkToMacCode(VK_RSHIFT, out var rshift));
		Assert.AreEqual(0x3Cu, rshift);
	}

	[Test, Category("Input")]
	public void EventOrigin()
	{
		var ev = MacNativeInput.CGEventCreateKeyboardEvent(nint.Zero, 0, true);
		try
		{
			MacNativeInput.CGEventSetIntegerValueField(ev, MacNativeInput.kCGEventSourceUnixProcessID, 0);
			MacNativeInput.CGEventSetIntegerValueField(ev, MacNativeInput.kCGEventSourceStateID,
				MacNativeInput.kCGEventSourceStateHIDSystemState);
			Assert.AreEqual(MacKeyboardState.Origin.PhysicalHid,
				MacNativeInput.ClassifyEventOrigin(ev, false));
			MacNativeInput.CGEventSetIntegerValueField(ev, MacNativeInput.kCGEventSourceStateID,
				MacNativeInput.kCGEventSourceStateCombinedSessionState);
			Assert.AreEqual(MacKeyboardState.Origin.ForeignSynthetic,
				MacNativeInput.ClassifyEventOrigin(ev, false));
			Assert.AreEqual(MacKeyboardState.Origin.KeysharpSynthetic,
				MacNativeInput.ClassifyEventOrigin(ev, true));
		}
		finally
		{
			if (ev != nint.Zero) MacNativeInput.CFRelease(ev);
		}
	}

	[Test, Category("Input")]
	public void MouseTapModifiers()
	{
		var mask = MacNativeInput.EventMaskFor(keyboard: false, mouse: true);
		Assert.AreNotEqual(0UL, mask & (1UL << (int)MacNativeInput.kCGEventFlagsChanged));
	}

	[Test, Category("Input")]
	public void UnicodeVirtualKey()
	{
		var down = MacNativeInput.CreateKeyboardEvent((uint)'A', true, KeyIgnore, 0);
		var up = MacNativeInput.CreateKeyboardEvent((uint)'A', false, KeyIgnore, 0);

		try
		{
			Assert.AreNotEqual(nint.Zero, down);
			Assert.AreNotEqual(nint.Zero, up);
			// A Unicode payload can legitimately accompany a virtual-key event. Classification must
			// therefore come from Keysharp's explicit metadata, never from payload presence.
			MacNativeInput.CGEventKeyboardSetUnicodeString(down, 1, "a");
			Span<char> unicode = stackalloc char[8];
			Assert.IsTrue(MacNativeInput.TryGetKeyboardUnicodeString(down, unicode, out var unicodeLength));
			Assert.Greater(unicodeLength, 0);

			AssertMetadata(down, KeyIgnore, MacNativeInput.InjectedEventKind.None);
			AssertMetadata(up, KeyIgnore, MacNativeInput.InjectedEventKind.KeyUp);
		}
		finally
		{
			if (down != nint.Zero) MacNativeInput.CFRelease(down);
			if (up != nint.Zero) MacNativeInput.CFRelease(up);
		}
	}

	[Test, Category("Input")]
	public void SidedModifiers()
	{
		var state = new MacKeyboardState();
		Assert.IsFalse(state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.KeysharpSynthetic,
			true, MacNativeInput.InjectedEventKind.None));
		Assert.IsFalse(state.ApplyFlagsChanged(VK_RSHIFT, 0, MacKeyboardState.Origin.KeysharpSynthetic,
			true, MacNativeInput.InjectedEventKind.None));
		Assert.IsTrue(state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.KeysharpSynthetic,
			false, MacNativeInput.InjectedEventKind.KeyUp));

		Assert.AreEqual(0u, state.ObservedModifiers & MOD_LSHIFT);
		Assert.AreEqual(MOD_RSHIFT, state.ObservedModifiers & MOD_RSHIFT);
		var mask = state.ToEventMask(MacNativeInput.kCGEventFlagMaskShift);
		Assert.IsFalse(mask.HasFlag(EventMask.LeftShift));
		Assert.IsTrue(mask.HasFlag(EventMask.RightShift));
	}

	[Test, Category("Input")]
	public void ForeignModifiers()
	{
		var state = new MacKeyboardState();
		Assert.IsFalse(state.ApplyFlagsChanged(VK_LSHIFT, MacNativeInput.kCGEventFlagMaskShift,
			MacKeyboardState.Origin.ForeignSynthetic, true, MacNativeInput.InjectedEventKind.None));
		Assert.IsFalse(state.ApplyFlagsChanged(VK_RSHIFT, MacNativeInput.kCGEventFlagMaskShift,
			MacKeyboardState.Origin.ForeignSynthetic, true, MacNativeInput.InjectedEventKind.None));
		Assert.IsTrue(state.ApplyFlagsChanged(VK_LSHIFT, MacNativeInput.kCGEventFlagMaskShift,
			MacKeyboardState.Origin.ForeignSynthetic, false, MacNativeInput.InjectedEventKind.None));

		Assert.AreEqual(MOD_RSHIFT, state.ObservedModifiers & (MOD_LSHIFT | MOD_RSHIFT));
	}

	[Test, Category("Input")]
	public void SendPrediction()
	{
		var state = new MacKeyboardState();
		state.ApplyFlagsChanged(VK_LSHIFT, MacNativeInput.kCGEventFlagMaskShift,
			MacKeyboardState.Origin.PhysicalHid, true, MacNativeInput.InjectedEventKind.None);
		state.BeginSend(MOD_LSHIFT, nativeCapsLock: false);
		state.PostModifier(MOD_LCONTROL, true, _ => true);
		state.SetCapsLock(true);
		state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.PhysicalHid, false,
			MacNativeInput.InjectedEventKind.None);
		state.ApplyFlagsChanged(VK_RMENU, MacNativeInput.kCGEventFlagMaskAlternate,
			MacKeyboardState.Origin.PhysicalHid, true, MacNativeInput.InjectedEventKind.None);

		Assert.AreEqual(MOD_LSHIFT | MOD_LCONTROL, state.GetModifiers(() => 0));
		Assert.IsTrue(state.GetCapsLock(() => false));
		state.EndSend();
		Assert.AreEqual(MOD_RALT, state.GetModifiers(() => 0));
		Assert.IsFalse(state.GetCapsLock(() => true));
	}

	[Test, Category("Input")]
	public void SenderVsNativeChanges()
	{
		var state = new MacKeyboardState();
		state.BeginSend(MOD_LSHIFT, nativeCapsLock: false);
		state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.PhysicalHid, false,
			MacNativeInput.InjectedEventKind.None);
		state.ApplyFlagsChanged(VK_RMENU, MacNativeInput.kCGEventFlagMaskAlternate,
			MacKeyboardState.Origin.PhysicalHid, true, MacNativeInput.InjectedEventKind.None);
		state.PostModifier(MOD_LCONTROL, true, _ => true);
		state.SetCapsLock(true);
		state.EndSend();

		Assert.AreEqual(MOD_LCONTROL | MOD_RALT, state.GetModifiers(() => 0));
		Assert.IsTrue(state.GetCapsLock(() => false));
	}

	[Test, Category("Input")]
	public void ModifierPostRollback()
	{
		var state = new MacKeyboardState();
		state.BeginSend(MOD_LSHIFT, nativeCapsLock: false);
		state.PostModifier(MOD_LCONTROL, true, _ => false);
		Assert.AreEqual(MOD_LSHIFT, state.GetModifiers(() => 0));

		Assert.Throws<InvalidOperationException>(() => state.PostModifier(MOD_LCONTROL, true,
			_ => throw new InvalidOperationException("post failed")));
		Assert.AreEqual(MOD_LSHIFT, state.GetModifiers(() => 0));
	}

	[Test, Category("Input")]
	public void StaleModifierCallback()
	{
		var state = new MacKeyboardState();
		state.BeginSend(MOD_LSHIFT, nativeCapsLock: false);
		var earlierRevision = state.SenderRevisions;
		state.PostModifier(MOD_LCONTROL, true, _ => true);
		state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.PhysicalHid, false,
			MacNativeInput.InjectedEventKind.None, earlierRevision.Modifiers, earlierRevision.CapsLock);
		state.EndSend();

		Assert.AreEqual(MOD_LSHIFT | MOD_LCONTROL, state.GetModifiers(() => 0));
		Assert.AreEqual(0, state.ObservedModifiers & MOD_LSHIFT);
	}

	[Test, Category("Input")]
	public void CapsLockObservation()
	{
		var state = new MacKeyboardState();
		state.BeginSend(MOD_LSHIFT, nativeCapsLock: false);
		var earlierRevision = state.SenderRevisions;
		state.SetCapsLock(true);
		state.ApplyFlagsChanged(VK_LSHIFT, 0, MacKeyboardState.Origin.PhysicalHid, false,
			MacNativeInput.InjectedEventKind.None, earlierRevision.Modifiers, earlierRevision.CapsLock);
		state.EndSend();

		Assert.AreEqual(0, state.GetModifiers(() => MOD_LSHIFT));
		Assert.IsTrue(state.GetCapsLock(() => false));
	}

	[Test, Category("Input")]
	public void UnicodeEvent()
	{
		Assert.AreEqual(1, MacNativeInput.NextUnicodeScalarLength("A"));
		Assert.AreEqual(2, MacNativeInput.NextUnicodeScalarLength("\U0001F600"));
		Assert.AreEqual(1, MacNativeInput.NextUnicodeScalarLength("\uD83Dx"));

		var text = "\U0001F600";
		var down = MacNativeInput.CreateUnicodeEvent(nint.Zero, text, KeyBlockThis, true);
		var up = MacNativeInput.CreateUnicodeEvent(nint.Zero, text, KeyBlockThis, false);
		try
		{
			Span<char> buffer = stackalloc char[2];
			Assert.IsTrue(MacNativeInput.TryGetKeyboardUnicodeString(down, buffer, out var length));
			Assert.AreEqual(2, length);
			Assert.AreEqual(text, buffer[..length].ToString());
			AssertMetadata(down, KeyBlockThis, MacNativeInput.InjectedEventKind.UnicodeText);
			AssertMetadata(up, KeyBlockThis,
				MacNativeInput.InjectedEventKind.UnicodeText | MacNativeInput.InjectedEventKind.KeyUp);
		}
		finally
		{
			if (down != nint.Zero) MacNativeInput.CFRelease(down);
			if (up != nint.Zero) MacNativeInput.CFRelease(up);
		}
	}

	[Test, Category("Input")]
	public void MouseMetadata()
	{
		Assert.AreEqual(MacNativeInput.kCGEventMouseMoved, MacNativeInput.MouseMoveType(MouseButton.NoButton));
		Assert.AreEqual(MacNativeInput.kCGEventLeftMouseDragged, MacNativeInput.MouseMoveType(MouseButton.Button1));
		Assert.AreEqual(MacNativeInput.kCGEventRightMouseDragged, MacNativeInput.MouseMoveType(MouseButton.Button2));
		Assert.AreEqual(MacNativeInput.kCGEventOtherMouseDragged, MacNativeInput.MouseMoveType(MouseButton.Button4));

		var ev = MacNativeInput.CreateMouseEvent(MacNativeInput.kCGEventLeftMouseDown,
			new MacNativeInput.CGPoint(10, 20), 0, KeyIgnore, clickCount: 2, eventNumber: 41);
		try
		{
			Assert.AreNotEqual(nint.Zero, ev);
			Assert.AreEqual(MacNativeInput.kCGEventLeftMouseDown, MacNativeInput.CGEventGetType(ev));
			Assert.AreEqual(2, MacNativeInput.CGEventGetIntegerValueField(ev, MacNativeInput.kCGMouseEventClickState));
			Assert.AreEqual(41, MacNativeInput.CGEventGetIntegerValueField(ev, MacNativeInput.kCGMouseEventNumber));
			AssertMetadata(ev, KeyIgnore, MacNativeInput.InjectedEventKind.Mouse);
		}
		finally
		{
			if (ev != nint.Zero) MacNativeInput.CFRelease(ev);
		}
	}

	[Test, Category("Input")]
	public void RelativeMouse()
	{
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		stream.ObserveMove(100, 200, stream.SenderRevision);
		Assert.IsTrue(stream.MoveRelative(5, -3, KeyIgnore));
		Assert.IsTrue(stream.MoveRelative(7, 4, KeyIgnore));
		Assert.AreEqual((105, 197), sink.Moves[0]);
		Assert.AreEqual((112, 201), sink.Moves[1]);
	}

	[Test, Category("Input")]
	public void MousePredictionRebase()
	{
		var sink = new FakeMouseEventSink { CursorX = 20, CursorY = 30 };
		var stream = new MacMouseEventStream(sink: sink);
		sink.Stream = stream;
		stream.ObserveMove(100, 200, stream.SenderRevision);
		stream.InvalidatePosition();

		Assert.IsTrue(stream.MoveRelative(5, -2, KeyIgnore));
		Assert.AreEqual((25, 28), sink.Moves.Single());
	}

	[TestCase(100.6, 200.2, 100, 200)]
	[TestCase(-100.6, -200.2, -101, -201)]
	[Category("Input")]
	public void MovementHold(double cursorX, double cursorY, int heldX, int heldY)
	{
		var sink = new FakeMouseEventSink { LocationX = cursorX, LocationY = cursorY };
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		var hold = MacMouseEventStream.HoldSeconds;

		Assert.IsTrue(stream.SetMovementSuppressed(true));
		Assert.IsTrue(stream.MovementSuppressed);
		// Stepping off the whole-point cursor position and back starts the hold and realigns WindowServer's pointer.
		CollectionAssert.AreEqual(new[] { (heldX + 1, heldY, hold), (heldX, heldY, hold) }, sink.Warps);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((heldX, heldY), (position.X, position.Y));

		sink.Warps.Clear();
		stream.RefreshHold();
		CollectionAssert.AreEqual(new[] { (heldX, heldY, hold) }, sink.Warps);

		sink.Warps.Clear();
		Assert.IsTrue(stream.SetMovementSuppressed(false));
		Assert.IsFalse(stream.MovementSuppressed);
		// A zero interval ends the hold at once; the interval in effect before suppression returns.
		CollectionAssert.AreEqual(new[] { (heldX + 1, heldY, 0.0), (heldX, heldY, 0.0) }, sink.Warps);
		Assert.AreEqual(0.25, sink.HoldInterval);

		sink.Warps.Clear();
		stream.RefreshHold();
		Assert.IsEmpty(sink.Warps);
	}

	[Test, Category("Input")]
	public void MovementHoldWithoutCursor()
	{
		var sink = new FakeMouseEventSink { FailLocation = true };
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		Assert.IsFalse(stream.SetMovementSuppressed(true));
		Assert.IsFalse(stream.MovementSuppressed);
		Assert.IsEmpty(sink.Warps);
		Assert.AreEqual(0.25, sink.HoldInterval);
	}

	[TestCase(1, 1)]
	[TestCase(2, 1)]
	[TestCase(2, 2)]
	[TestCase(2, 4)]
	[Category("Input")]
	public void MovementHoldWarpFailure(int failedStep, int failures)
	{
		var sink = new FakeMouseEventSink
		{
			CursorX = 50, CursorY = 60, LocationX = 50, LocationY = 60,
			FailWarpCall = failedStep, FailedWarpCount = failures
		};
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		Assert.IsFalse(stream.SetMovementSuppressed(true));
		Assert.IsFalse(stream.MovementSuppressed);
		Assert.AreEqual(0.25, sink.HoldInterval);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual(((int)sink.LocationX, (int)sink.LocationY), (position.X, position.Y),
			"a failed warp must not record a destination the cursor did not reach");
		Assert.IsTrue(stream.MoveRelative(-10, 0, KeyIgnore));
		Assert.AreEqual((position.X - 10, position.Y), sink.Moves.Single());
	}

	[TestCase(250, false, 1)]
	[TestCase(1001, false, 2)]
	[TestCase(250, true, 2)]
	[Category("Input")]
	public void MovementHoldDeadline(int elapsedMilliseconds, bool force, int expectedWarps)
	{
		var sink = new FakeMouseEventSink { LocationX = 50, LocationY = 60 };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp, timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		sink.Warps.Clear();
		sink.Timestamp += elapsedMilliseconds;
		Assert.IsTrue(stream.RefreshHold(force));
		Assert.AreEqual(expectedWarps, sink.Warps.Count,
			"expired or forced renewal must reset WindowServer's internal pointer");
		Assert.AreEqual((50.0, 60.0), (sink.LocationX, sink.LocationY));
	}

	[Test, Category("Input")]
	public void SuppressedMousePosition()
	{
		var sink = new FakeMouseEventSink { LocationX = 100, LocationY = 200, CursorX = 7, CursorY = 8, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));

		// Pending posts count before WindowServer applies them.
		Assert.IsTrue(stream.MoveRelative(5, -3, KeyIgnore));
		Assert.IsTrue(stream.MoveRelative(-2, 4, KeyIgnore));
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((103, 201), (position.X, position.Y));
		CollectionAssert.AreEqual(new[] { (105, 197), (103, 201) }, sink.Moves);
	}

	[Test, Category("Input")]
	public void MouseDisplayClamp()
	{
		var sink = new FakeMouseEventSink
		{
			CursorX = 90,
			CursorY = 50,
			DisplayBounds = [new Rectangle(-100, 0, 100, 100), new Rectangle(0, 150, 100, 100)]
		};
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		stream.MoveAbsolute(30, 120, KeyIgnore);
		Assert.AreEqual((30, 150), sink.Moves[^1]);
		Assert.IsTrue(stream.MoveRelative(500, 0, KeyIgnore));
		Assert.AreEqual((99, 150), sink.Moves[^1]);
		// Past an edge, moving back starts from the edge rather than from the overshoot.
		Assert.IsTrue(stream.MoveRelative(-10, 0, KeyIgnore));
		Assert.AreEqual((89, 150), sink.Moves[^1]);
		stream.MoveAbsolute(-20, 115, KeyIgnore);
		Assert.AreEqual((-20, 99), sink.Moves[^1]);
		stream.Button(MouseButton.Button1, true, 500, 500, KeyIgnore);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((99, 249), (position.X, position.Y));
	}

	[Test, Category("Input")]
	public void MouseRealign()
	{
		var sink = new FakeMouseEventSink { LocationX = 50.4, LocationY = 60.9, DisplayBounds = [new Rectangle(0, 0, 100, 100)] };
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;

		// Without suppression the warp must not hold physical movement afterwards.
		stream.Realign();
		CollectionAssert.AreEqual(new[] { (51, 60, 0.0), (50, 60, 0.0) }, sink.Warps);
		Assert.AreEqual(0.25, sink.HoldInterval);

		sink.Warps.Clear();
		stream.Warp(99, 20);
		CollectionAssert.AreEqual(new[] { (98, 20, 0.0), (99, 20, 0.0) }, sink.Warps);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((99, 20), (position.X, position.Y));

		Assert.IsTrue(stream.SetMovementSuppressed(true));
		sink.Warps.Clear();
		stream.Realign();
		var hold = MacMouseEventStream.HoldSeconds;
		CollectionAssert.AreEqual(new[] { (98, 20, hold), (99, 20, hold) }, sink.Warps);
	}

	[Test, Category("Input")]
	public void MouseWarpDisplayClamp()
	{
		var sink = new FakeMouseEventSink
		{
			LocationX = 50,
			LocationY = 50,
			DisplayBounds = [new Rectangle(0, 0, 100, 100)],
			ClampWarps = true
		};
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		stream.Warp(500, 50);
		Assert.AreEqual((99.0, 50.0), (sink.LocationX, sink.LocationY));
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((99, 50), (position.X, position.Y));
		Assert.IsTrue(stream.MoveRelative(-10, 0, KeyIgnore));
		Assert.AreEqual((89, 50), sink.Moves.Single());
	}

	[Test, Category("Input")]
	public void RecentMousePosition()
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20 };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.MoveAbsolute(30, 40, KeyIgnore);
		Assert.IsTrue(stream.IsRecentPosition(30, 40));
		Assert.IsFalse(stream.IsRecentPosition(30.5, 40));
		Assert.IsTrue(stream.IsRecentPosition(10, 20));

		sink.Timestamp += 60;
		Assert.IsFalse(stream.IsRecentPosition(30, 40));
		Assert.IsFalse(stream.IsRecentPosition(10, 20));
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Input")]
	public void HeldHardwareMoveAfterSyntheticMove(bool postApplied)
	{
		var sink = new FakeMouseEventSink { LocationX = 100, LocationY = 200, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		long deltaX = 10, deltaY = -4;
		Assert.IsTrue(stream.MeasureHardwareMove(100, 200, ref deltaX, ref deltaY));
		Assert.AreEqual((10L, -4L), (deltaX, deltaY));

		stream.MoveAbsolute(90, 210, KeyIgnore);
		if (postApplied)
			sink.ApplyPostedLocations();
		deltaX = 0;
		deltaY = 6;
		Assert.IsTrue(stream.MeasureHardwareMove(90, 210, ref deltaX, ref deltaY));
		Assert.AreEqual((10L, -4L), (deltaX, deltaY), "the synthetic offset must not cancel physical motion");
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((90, 210), (position.X, position.Y));

		deltaX = 3;
		deltaY = -2;
		Assert.IsTrue(stream.MeasureHardwareMove(90, 210, ref deltaX, ref deltaY));
		Assert.AreEqual((3L, -2L), (deltaX, deltaY), "a later held move has no synthetic offset");
	}

	[Test, Category("Input")]
	public void QueuedHeldHardwareMove()
	{
		var sink = new FakeMouseEventSink { LocationX = 100, LocationY = 50, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.RecordHardwareMove(100, 50);
		for (var i = 1; i <= 17; i++)
			stream.MoveAbsolute(100 + i, 50, KeyIgnore);
		sink.ApplyPostedLocations();
		long deltaX = 10, deltaY = 0;
		Assert.IsTrue(stream.MeasureHardwareMove(100, 50, ref deltaX, ref deltaY),
			"a queued held move retains its origin throughout the recent-position window");
		Assert.AreEqual((10L, 0L), (deltaX, deltaY));
	}

	[Test, Category("Input")]
	public void FreeHardwareMoveAfterSyntheticMove()
	{
		var sink = new FakeMouseEventSink { LocationX = 100, LocationY = 200, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		long deltaX = 10, deltaY = -4;
		Assert.IsTrue(stream.MeasureHardwareMove(100, 200, ref deltaX, ref deltaY));
		stream.MoveAbsolute(70, 220, KeyIgnore);
		sink.ApplyPostedLocations();

		deltaX = -20;
		deltaY = 15;
		Assert.IsFalse(stream.MeasureHardwareMove(80, 215, ref deltaX, ref deltaY));
		Assert.AreEqual((10L, -5L), (deltaX, deltaY), "free motion starts from the live cursor before the move");
		stream.ObserveMove(80, 215, stream.SenderRevision);
		Assert.IsTrue(stream.MoveRelative(1, 2, KeyIgnore));
		Assert.AreEqual((81, 217), sink.Moves[^1]);
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Input")]
	public void FirstHardwareMove(bool resetHistory)
	{
		var sink = new FakeMouseEventSink { LocationX = 100, LocationY = 200 };
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		long deltaX = 8, deltaY = 12;
		if (resetHistory)
		{
			Assert.IsTrue(stream.MeasureHardwareMove(100, 200, ref deltaX, ref deltaY));
			stream.ForgetHardwareMoves();
		}

		sink.LocationX = 20;
		sink.LocationY = -40;
		deltaX = 5;
		deltaY = -5;
		Assert.IsFalse(stream.MeasureHardwareMove(25, -45, ref deltaX, ref deltaY));
		Assert.AreEqual((5L, -5L), (deltaX, deltaY), "unknown hardware history must preserve the reported deltas");
	}

	[Test, Category("Input")]
	public void HardwareMoveReturnsToPostedPosition()
	{
		var sink = new FakeMouseEventSink { DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		stream.MoveAbsolute(100, 200, KeyIgnore);
		sink.ApplyPostedLocations();
		long deltaX = 10, deltaY = 0;
		Assert.IsFalse(stream.MeasureHardwareMove(110, 200, ref deltaX, ref deltaY));
		Assert.AreEqual((10L, 0L), (deltaX, deltaY));

		sink.LocationX = 110;
		deltaX = -10;
		Assert.IsFalse(stream.MeasureHardwareMove(100, 200, ref deltaX, ref deltaY));
		Assert.AreEqual((-10L, 0L), (deltaX, deltaY), "returning to a recent post is free motion without suppression");
	}

	[TestCase("Suppress", false)]
	[TestCase("Refresh", true)]
	[TestCase("Realign", false)]
	[TestCase("Realign", true)]
	[TestCase("Warp", false)]
	[TestCase("Warp", true)]
	[TestCase("Release", true)]
	[Category("Input")]
	public void PendingMousePostBeforeWarp(string operation, bool suppressed)
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		if (suppressed)
			Assert.IsTrue(stream.SetMovementSuppressed(true));
		sink.Warps.Clear();
		sink.ReadPendingCursor = false;
		stream.MoveAbsolute(30, 40, KeyIgnore);
		Assert.AreEqual((10.0, 20.0), (sink.LocationX, sink.LocationY));

		switch (operation)
		{
			case "Suppress": Assert.IsTrue(stream.SetMovementSuppressed(true)); break;
			case "Refresh": stream.RefreshHold(); break;
			case "Realign": stream.Realign(); break;
			case "Warp": stream.Warp(70, 80); break;
			case "Release": Assert.IsTrue(stream.SetMovementSuppressed(false)); break;
		}

		Assert.AreEqual(1, sink.PendingPostCount, "elapsed time does not acknowledge an asynchronous post");
		Assert.IsFalse(sink.ReadPendingCursor, "a pending destination takes precedence over an older cursor read");
		if (operation == "Warp")
			Assert.IsEmpty(sink.Warps, "an explicit warp waits for the pending post's acknowledgement");
		sink.ApplyPostedLocations();
		var expectedX = operation == "Warp" ? 70 : 30;
		var expectedY = operation == "Warp" ? 80 : 40;
		var hold = operation == "Suppress" || suppressed && operation != "Release" ? MacMouseEventStream.HoldSeconds : 0.0;
		var expectedWarps = operation == "Refresh"
			? new[] { (expectedX, expectedY, hold) }
			: new[] { (expectedX + 1, expectedY, hold), (expectedX, expectedY, hold) };
		CollectionAssert.AreEqual(expectedWarps, sink.Warps);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((expectedX, expectedY), (position.X, position.Y));
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Input")]
	public void DelayedMousePost(bool refresh)
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.MoveAbsolute(30, 40, KeyIgnore);
		sink.Timestamp += 2000;
		sink.ReadPendingCursor = false;
		if (refresh)
			Assert.IsTrue(stream.RefreshHold());
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((30, 40), (position.X, position.Y),
			"a delayed native post must retain its predicted destination regardless of elapsed time");
		Assert.AreEqual(1, sink.PendingPostCount);
		Assert.IsFalse(sink.ReadPendingCursor);
		sink.ApplyPostedLocations();
		Assert.IsTrue(stream.TryGetPosition(out position));
		Assert.AreEqual((30, 40), (position.X, position.Y));
	}

	[Test, Category("Input")]
	public void WarpAfterLatestPostAcknowledgement()
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		sink.Warps.Clear();
		stream.MoveAbsolute(30, 40, KeyIgnore);
		stream.MoveAbsolute(50, 60, KeyIgnore);
		stream.Warp(70, 80);
		Assert.IsEmpty(sink.Warps);
		stream.SetPostObservation(true);
		stream.AcknowledgePost(ulong.MaxValue);
		sink.ApplyNextPostedLocation();
		Assert.IsEmpty(sink.Warps, "an earlier post cannot complete a warp waiting for the latest post");
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((70, 80), (position.X, position.Y));
		sink.ApplyNextPostedLocation();
		Assert.AreEqual((70.0, 80.0), (sink.LocationX, sink.LocationY));
		Assert.IsTrue(stream.TryGetPosition(out position));
		Assert.AreEqual((70, 80), (position.X, position.Y));
	}

	[Test, Category("Input")]
	public void RelativePostAfterDeferredWarp()
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp, timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		sink.Warps.Clear();
		stream.MoveAbsolute(30, 40, KeyIgnore);
		stream.Warp(70, 80);
		Assert.IsTrue(stream.MoveRelative(1, -2, KeyIgnore));
		Assert.AreEqual((71, 78), sink.Moves[^1]);
		sink.ApplyNextPostedLocation();
		Assert.IsEmpty(sink.Warps, "an earlier acknowledgement must not restore a superseded warp");
		sink.ApplyNextPostedLocation();
		Assert.IsEmpty(sink.Warps);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((71, 78), (position.X, position.Y));
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Input")]
	public void FailedPostAfterPendingPost(bool deferredWarp)
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp, timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.MoveAbsolute(30, 40, KeyIgnore);
		if (deferredWarp)
			stream.Warp(70, 80);
		sink.FailMoves = 1;
		stream.MoveAbsolute(50, 60, KeyIgnore);
		Assert.AreEqual(1, sink.Moves.Count);
		var expected = deferredWarp ? (70, 80) : (30, 40);
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual(expected, (position.X, position.Y), "a failed post must preserve the earlier pending destination");
		Assert.IsTrue(stream.RefreshHold());
		Assert.IsTrue(stream.TryGetPosition(out position));
		Assert.AreEqual(expected, (position.X, position.Y));
		Assert.AreEqual((30.0, 40.0), (sink.LocationX, sink.LocationY));
		sink.ApplyPostedLocations();
		Assert.IsTrue(stream.TryGetPosition(out position));
		Assert.AreEqual(expected, (position.X, position.Y), "the earlier acknowledgement must still complete its deferred warp");
	}

	[Test, Category("Input")]
	public void LostPostAcknowledgementOnRecovery()
	{
		var sink = new FakeMouseEventSink
		{
			LocationX = 10, LocationY = 20, DeferPostedLocations = true, DropAcknowledgements = true
		};
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp, timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.MoveAbsolute(30, 40, KeyIgnore);
		stream.Warp(70, 80);
		sink.ApplyPostedLocations();
		Assert.AreEqual((30.0, 40.0), (sink.LocationX, sink.LocationY));
		stream.SetPostObservation(true, reset: true);
		Assert.AreEqual((70.0, 80.0), (sink.LocationX, sink.LocationY));
		Assert.IsTrue(stream.TryGetPosition(out var position));
		Assert.AreEqual((70, 80), (position.X, position.Y));
		sink.LocationX = sink.CursorX = 80;
		sink.LocationY = sink.CursorY = 90;
		Assert.IsTrue(stream.TryGetPosition(out position));
		Assert.AreEqual((80, 90), (position.X, position.Y), "recovery must retire a post whose callback was lost");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Input")]
	public void ExternalMouseWarpWhileSuppressed(bool queryPositionFirst)
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20, DeferPostedLocations = true };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		stream.MoveAbsolute(30, 40, KeyIgnore);
		Assert.IsTrue(stream.TryGetPosition(out var pendingPosition));
		Assert.AreEqual((30, 40), (pendingPosition.X, pendingPosition.Y));
		Assert.AreEqual((10.0, 20.0), (sink.LocationX, sink.LocationY));
		sink.ApplyPostedLocations();

		sink.LocationX = 70;
		sink.LocationY = 80;
		sink.CursorX = 70;
		sink.CursorY = 80;
		if (queryPositionFirst)
		{
			Assert.IsTrue(stream.TryGetPosition(out var position));
			Assert.AreEqual((70, 80), (position.X, position.Y));
		}
		Assert.IsTrue(stream.MoveRelative(1, -2, KeyIgnore));
		Assert.AreEqual((71, 78), sink.Moves[^1]);
	}

	[Test, Category("Input")]
	public void MouseDisplayReconfiguration()
	{
		var sink = new FakeMouseEventSink { DisplayBounds = [new Rectangle(0, 0, 100, 100)] };
		var stream = new MacMouseEventStream(sink, timestamp: () => sink.Timestamp,
			timestampFrequency: 1000);
		sink.Stream = stream;
		stream.MoveAbsolute(50, 50, KeyIgnore);
		Assert.AreEqual((50, 50), sink.Moves[^1]);

		sink.DisplayBounds = [new Rectangle(0, 0, 100, 100), new Rectangle(200, 0, 100, 100)];
		stream.MoveAbsolute(250, 50, KeyIgnore);
		Assert.AreEqual((250, 50), sink.Moves[^1], "a newly attached display must be reachable immediately");

		sink.DisplayBounds = [new Rectangle(0, 0, 100, 100)];
		stream.MoveAbsolute(250, 50, KeyIgnore);
		Assert.AreEqual((99, 50), sink.Moves[^1], "a removed display must stop accepting posted locations immediately");
	}

	[Test, Category("Input")]
	public void PhysicalClickAfterHoldRefresh()
	{
		var sink = new FakeMouseEventSink { LocationX = 10, LocationY = 20 };
		var stream = new MacMouseEventStream(sink, TimeSpan.FromMilliseconds(500),
			timestamp: () => sink.Timestamp, timestampFrequency: 1000);
		sink.Stream = stream;
		Assert.IsTrue(stream.SetMovementSuppressed(true));
		var physicalRevision = stream.SenderRevision;
		stream.ObserveButton(MouseButton.Button1, true, 10, 20, 1, physicalRevision);

		stream.RefreshHold();
		stream.ObserveButton(MouseButton.Button1, false, 10, 20, 1, physicalRevision);
		stream.Button(MouseButton.Button1, true, 10, 20, KeyIgnore);
		Assert.AreEqual(2, sink.Buttons.Single().ClickCount,
			"refreshing an unchanged cursor must preserve an overlapping physical click");
	}

	[Test, Category("Input")]
	public void NativeDragState()
	{
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		var physicalRevision = stream.SenderRevision;
		stream.ObserveButton(MouseButton.Button1, true, 10, 20, 1, physicalRevision);
		stream.MoveAbsolute(11, 21, KeyIgnore);
		stream.Button(MouseButton.Button1, true, 11, 21, KeyIgnore);
		stream.Button(MouseButton.Button1, false, 11, 21, KeyIgnore);
		stream.MoveAbsolute(12, 22, KeyIgnore);
		Assert.AreEqual(MouseButton.Button1, sink.DragButtons[0]);
		Assert.AreEqual(MouseButton.Button1, sink.DragButtons[1]);

		stream.ObserveButton(MouseButton.Button1, false, 10, 20, 1, physicalRevision);
		stream.MoveAbsolute(13, 23, KeyIgnore);
		Assert.AreEqual(MouseButton.NoButton, sink.DragButtons[2]);
	}

	[Test, Category("Input")]
	public void MouseButtonResync()
	{
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		stream.ResyncButtons(button => button is 0 or 3);
		stream.MoveAbsolute(10, 20, KeyIgnore);
		Assert.AreEqual(MouseButton.Button1, sink.DragButtons[0]);

		stream.ResetObservedButtons();
		stream.MoveAbsolute(20, 30, KeyIgnore);
		Assert.AreEqual(MouseButton.NoButton, sink.DragButtons[1]);
	}

	[Test, Category("Input")]
	public void StaleMouseMove()
	{
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		var staleRevision = stream.SenderRevision;
		stream.MoveAbsolute(50, 60, KeyIgnore);
		stream.ObserveMove(1, 2, staleRevision);
		Assert.IsTrue(stream.MoveRelative(1, 1, KeyIgnore));
		Assert.AreEqual((51, 61), sink.Moves[^1]);
	}

	[Test, Category("Input")]
	public void HorizontalScroll()
	{
		var ev = MacNativeInput.CreateScrollWheelEvent(240, MouseWheelScrollDirection.Horizontal, KeyIgnore);
		try
		{
			Assert.AreNotEqual(nint.Zero, ev);
			Assert.AreEqual(MacNativeInput.kCGEventScrollWheel, MacNativeInput.CGEventGetType(ev));
			Assert.AreEqual(0, MacNativeInput.CGEventGetIntegerValueField(ev, MacNativeInput.kCGScrollWheelEventDeltaAxis1));
			Assert.AreEqual(2, MacNativeInput.CGEventGetIntegerValueField(ev, MacNativeInput.kCGScrollWheelEventDeltaAxis2));
			Assert.AreEqual(240.0, MacHookThread.ReadScrollDelta(ev,
				MacNativeInput.kCGScrollWheelEventDeltaAxis2,
				MacNativeInput.kCGScrollWheelEventPointDeltaAxis2,
				MacNativeInput.kCGScrollWheelEventFixedPtDeltaAxis2));
			AssertMetadata(ev, KeyIgnore, MacNativeInput.InjectedEventKind.Mouse);
		}
		finally
		{
			if (ev != nint.Zero) MacNativeInput.CFRelease(ev);
		}
	}

	[Test, Category("Input")]
	public void PartialWheelDelta()
	{
		var ev = MacNativeInput.CreateScrollWheelEvent(60, MouseWheelScrollDirection.Vertical, KeyIgnore);
		try
		{
			Assert.AreEqual(1, MacNativeInput.CGEventGetIntegerValueField(ev,
				MacNativeInput.kCGScrollWheelEventIsContinuous));
			Assert.AreEqual(0, MacNativeInput.CGEventGetIntegerValueField(ev,
				MacNativeInput.kCGScrollWheelEventDeltaAxis1));
			Assert.AreEqual(0.0, MacNativeInput.CGEventGetDoubleValueField(ev,
				MacNativeInput.kCGScrollWheelEventPointDeltaAxis1));
			Assert.AreEqual(60.0, MacHookThread.ReadScrollDelta(ev,
				MacNativeInput.kCGScrollWheelEventDeltaAxis1,
				MacNativeInput.kCGScrollWheelEventPointDeltaAxis1,
				MacNativeInput.kCGScrollWheelEventFixedPtDeltaAxis1));
		}
		finally
		{
			if (ev != nint.Zero) MacNativeInput.CFRelease(ev);
		}
	}

	[Test, Category("Input")]
	public void ClickSeriesBounds()
	{
		long now = 100;
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink, TimeSpan.FromMilliseconds(500), 4, () => now, 1000);
		sink.Stream = stream;
		stream.Button(MouseButton.Button1, true, 10, 20, KeyIgnore);
		stream.Button(MouseButton.Button1, false, 10, 20, KeyIgnore);
		now += 499;
		stream.Button(MouseButton.Button1, true, 14, 16, KeyIgnore);
		Assert.AreEqual(2, sink.Buttons[^1].ClickCount);
		stream.Button(MouseButton.Button1, false, 14, 16, KeyIgnore);

		now += 501;
		stream.Button(MouseButton.Button1, true, 14, 16, KeyIgnore);
		Assert.AreEqual(1, sink.Buttons[^1].ClickCount);
		stream.Button(MouseButton.Button1, false, 14, 16, KeyIgnore);
		now++;
		stream.Button(MouseButton.Button1, true, 19, 16, KeyIgnore);
		Assert.AreEqual(1, sink.Buttons[^1].ClickCount);
		stream.Button(MouseButton.Button1, false, 19, 16, KeyIgnore);
		stream.Button(MouseButton.Button2, true, 19, 16, KeyIgnore);
		Assert.AreEqual(1, sink.Buttons[^1].ClickCount);
	}

	[Test, Category("Input")]
	public void UnmatchedMouseUp()
	{
		long now = 100;
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink, TimeSpan.FromMilliseconds(500), 4, () => now, 1000);
		sink.Stream = stream;
		stream.Button(MouseButton.Button1, false, 10, 20, KeyIgnore);
		stream.Button(MouseButton.Button1, true, 10, 20, KeyIgnore);
		stream.Button(MouseButton.Button1, false, 10, 20, KeyIgnore);

		Assert.AreEqual(1, sink.Buttons[0].ClickCount);
		Assert.AreEqual(1, sink.Buttons[1].ClickCount);
		Assert.AreEqual(sink.Buttons[1].EventNumber, sink.Buttons[2].EventNumber);
		now += 100;
		stream.Button(MouseButton.Button1, true, 10, 20, KeyIgnore);
		Assert.AreEqual(2, sink.Buttons[3].ClickCount);
	}

	[Test, Category("Input")]
	public void MousePostRollback()
	{
		var sink = new FakeMouseEventSink { CursorX = 10, CursorY = 20, FailMoves = 1, FailButtons = 1 };
		var stream = new MacMouseEventStream(sink);
		sink.Stream = stream;
		Assert.IsFalse(stream.MoveRelative(5, 5, KeyIgnore));
		Assert.IsTrue(stream.MoveRelative(1, 1, KeyIgnore));
		Assert.AreEqual((11, 21), sink.Moves.Single());

		stream.Button(MouseButton.Button1, true, 11, 21, KeyIgnore);
		stream.Button(MouseButton.Button1, false, 11, 21, KeyIgnore);
		stream.Button(MouseButton.Button1, true, 11, 21, KeyIgnore);
		Assert.AreEqual(1, sink.Buttons[0].ClickCount);
		Assert.AreEqual(1, sink.Buttons[1].ClickCount);
	}

	[Test, Category("Input")]
	public void SyntheticClickSeries()
	{
		long now = 100;
		var sink = new FakeMouseEventSink();
		var stream = new MacMouseEventStream(sink, TimeSpan.FromMilliseconds(500), 4, () => now, 1000);
		sink.Stream = stream;
		stream.ObserveButton(MouseButton.Button1, false, 10, 20, 1, stream.SenderRevision);
		now += 100;
		stream.Button(MouseButton.Button1, true, 10, 20, KeyIgnore);
		Assert.AreEqual(2, sink.Buttons[0].ClickCount);
	}

	[Test, Category("Input")]
	public void EventTapExit()
	{
		var driver = new FakeEventTapDriver { RunResult = 1 };
		using var terminated = new ManualResetEventSlim(false);
		MacEventTapState stateAtNotification = MacEventTapState.Created;
		var tap = new MacNativeEventTap(script, 1, (_, _) => false, () => { }, (failed, _) =>
		{
			stateAtNotification = failed.State;
			terminated.Set();
		}, driver);

		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(driver.RunEntered.Wait(1000));
		driver.ReleaseRun.Set();
		Assert.IsTrue(terminated.Wait(1000));
		Assert.AreEqual(MacEventTapState.Faulted, stateAtNotification);
		Assert.AreEqual(6, driver.ReleaseCount);
		Assert.IsTrue(tap.Stop());
	}

	[Test, Category("Input")]
	public void EventTapRecovery()
	{
		var driver = new FakeEventTapDriver();
		driver.RunResults.Enqueue(1);
		var resyncCount = 0;
		var terminationCount = 0;
		var tap = new MacNativeEventTap(script, 1, (_, _) => false,
			() => Interlocked.Increment(ref resyncCount), (_, _) => Interlocked.Increment(ref terminationCount), driver);

		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(SpinWait.SpinUntil(() => driver.CreateCount >= 2 && tap.IsRunning && Volatile.Read(ref resyncCount) == 1, 1000));
		Assert.AreEqual(1, resyncCount);
		Assert.AreEqual(0, terminationCount);
		Assert.IsTrue(tap.Stop());
		Assert.AreEqual(4, driver.ReleaseCount);
	}

	[Test, Category("Input")]
	public void EventTapStop()
	{
		var driver = new FakeEventTapDriver { RunResult = 2 };
		var terminationCount = 0;
		var tap = new MacNativeEventTap(script, 1, (_, _) => false, () => { }, (_, _) =>
			Interlocked.Increment(ref terminationCount), driver);

		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(driver.RunEntered.Wait(1000));
		Assert.IsTrue(tap.Stop());
		Assert.AreEqual(MacEventTapState.Stopped, tap.State);
		Assert.AreEqual(0, terminationCount);
		Assert.AreEqual(2, driver.ReleaseCount);
	}

	[Test, Category("Input")]
	public void StopDuringStartup()
	{
		var driver = new FakeEventTapDriver { BlockAddSource = true };
		var tap = new MacNativeEventTap(script, 1, (_, _) => false, () => { }, (_, _) => { }, driver);
		var starting = Task.Run(() => tap.Start(1000));
		Assert.IsTrue(driver.AddSourceEntered.Wait(1000));
		var stopping = Task.Run(tap.Stop);
		Assert.IsTrue(SpinWait.SpinUntil(() => tap.State == MacEventTapState.Stopping, 1000));
		driver.ReleaseAddSource.Set();

		Assert.IsFalse(starting.Result);
		Assert.IsTrue(stopping.Result);
		Assert.AreEqual(MacEventTapState.Stopped, tap.State);
		Assert.AreEqual(2, driver.ReleaseCount);
	}

	[Test, Category("Input")]
	public void WatchdogReenable()
	{
		var driver = new FakeEventTapDriver { RunResult = 2 };
		var tap = new MacNativeEventTap(script, 1, (_, _) => false,
			() => throw new InvalidOperationException("resync failure"), (_, _) => { }, driver);
		Assert.IsTrue(tap.Start(1000));
		Assert.AreNotEqual(nint.Zero, driver.Callback(nint.Zero,
			MacNativeInput.kCGEventTapDisabledByTimeout, (nint)123, nint.Zero));
		Assert.AreEqual(1, driver.EnableCount);
		Assert.IsTrue(tap.Stop());
	}

	[Test, Category("Input")]
	public void DisabledTapStartup()
	{
		var driver = new FakeEventTapDriver { Enabled = false };
		using var tap = new MacNativeEventTap(script, 1, (_, _) => false, () => { }, (_, _) => { }, driver);
		Assert.IsFalse(tap.Start(1000));
		StringAssert.Contains("not enabled", tap.StartupFailure);
		Assert.IsTrue(tap.Stop());
		Assert.AreEqual(2, driver.ReleaseCount);
	}

	[Test, Category("Input")]
	public void WatchdogEnableFailure()
	{
		var driver = new FakeEventTapDriver();
		var resyncCount = 0;
		using var tap = new MacNativeEventTap(script, 1, (_, _) => false,
			() => Interlocked.Increment(ref resyncCount), (_, _) => { }, driver);
		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(driver.RunEntered.Wait(1000));
		driver.Enabled = false;
		driver.FailEnable = true;
		Assert.AreEqual((nint)123, driver.Callback(nint.Zero,
			MacNativeInput.kCGEventTapDisabledByTimeout, (nint)123, nint.Zero));
		Assert.AreEqual(1, driver.EnableCount);
		Assert.AreEqual(0, resyncCount, "a failed native enable must not report successful recovery");
		Assert.IsTrue(tap.Stop());
	}

	[Test, Category("Input")]
	public void EventTapMaintenance()
	{
		var driver = new FakeEventTapDriver();
		for (var i = 0; i < 3; i++)
		{
			driver.RunResults.Enqueue(3);
			driver.RunAdvanceMilliseconds.Enqueue(100);
		}
		using var maintained = new ManualResetEventSlim(false);
		long maintainedAt = -1;
		var maintainedThread = 0;
		using var tap = new MacNativeEventTap(script, 1, (_, _) => false, () => { }, (_, _) => { }, driver,
			() =>
			{
				maintainedAt = driver.TimestampMilliseconds();
				maintainedThread = Environment.CurrentManagedThreadId;
				maintained.Set();
			});
		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(maintained.Wait(1000));
		Assert.AreEqual(300L, maintainedAt, "maintenance runs when its deadline passes despite earlier run-loop returns");
		Assert.AreEqual(driver.RunThread, maintainedThread, "maintenance must run on the event-tap thread");
		Assert.IsTrue(tap.Stop());
	}

	[Test, Category("Input")]
	public void StoppedWatchdog()
	{
		var driver = new FakeEventTapDriver { RunResult = 2 };
		var resyncCount = 0;
		var tap = new MacNativeEventTap(script, 1, (_, _) => false,
			() => Interlocked.Increment(ref resyncCount), (_, _) => { }, driver);
		Assert.IsTrue(tap.Start(1000));
		Assert.IsTrue(tap.Stop());

		Assert.AreNotEqual(nint.Zero, driver.Callback(nint.Zero,
			MacNativeInput.kCGEventTapDisabledByTimeout, (nint)123, nint.Zero));
		Assert.AreEqual(0, driver.EnableCount);
		Assert.AreEqual(0, resyncCount);
	}

	private static void AssertMetadata(nint ev, long expectedExtraInfo, MacNativeInput.InjectedEventKind expectedKind)
	{
		var encoded = MacNativeInput.CGEventGetIntegerValueField(ev, MacNativeInput.kCGEventSourceUserData);
		Assert.IsTrue(MacNativeInput.TryDecodeInjectedExtraInfo(encoded, out var extraInfo, out var kind));
		Assert.AreEqual(expectedExtraInfo, extraInfo);
		Assert.AreEqual(expectedKind, kind);
	}

	private sealed class FakeEventTapDriver : MacEventTapDriver
	{
		internal readonly ManualResetEventSlim AddSourceEntered = new(false);
		internal readonly ManualResetEventSlim ReleaseAddSource = new(false);
		internal readonly ManualResetEventSlim RunEntered = new(false);
		internal readonly ManualResetEventSlim ReleaseRun = new(false);
		internal readonly ConcurrentQueue<int> RunResults = new();
		internal readonly ConcurrentQueue<int> RunAdvanceMilliseconds = new();
		internal int RunResult { get; init; } = 3;
		internal int ReleaseCount;
		internal int EnableCount;
		internal int CreateCount;
		internal bool BlockAddSource;
		internal volatile bool Enabled = true;
		internal volatile bool FailEnable;
		internal int RunThread;
		private long timestampMilliseconds;
		internal MacNativeInput.CGEventTapCallBack Callback;
		private volatile bool valid = true;

		internal override nint CurrentRunLoop() => 1;
		internal override nint CreateTap(ulong eventMask, MacNativeInput.CGEventTapCallBack callback)
		{
			Callback = callback;
			Interlocked.Increment(ref CreateCount);
			return 2;
		}
		internal override nint CreateRunLoopSource(nint tap) => 3;
		internal override void AddSource(nint runLoop, nint source)
		{
			AddSourceEntered.Set();
			if (BlockAddSource)
				ReleaseAddSource.Wait();
		}
		internal override void RemoveSource(nint runLoop, nint source) { }
		internal override void EnableTap(nint tap, bool enable)
		{
			if (enable)
			{
				Interlocked.Increment(ref EnableCount);
				Enabled = !FailEnable;
			}
			else
				Enabled = false;
		}
		internal override bool IsTapValid(nint tap) => valid;
		internal override bool IsTapEnabled(nint tap) => Enabled;
		internal override long TimestampMilliseconds() => Volatile.Read(ref timestampMilliseconds);
		internal override int RunInDefaultMode(double seconds)
		{
			RunThread = Environment.CurrentManagedThreadId;
			RunEntered.Set();
			if (RunResults.TryDequeue(out var result))
			{
				if (RunAdvanceMilliseconds.TryDequeue(out var advance))
					Interlocked.Add(ref timestampMilliseconds, advance);
				return result;
			}
			ReleaseRun.Wait();
			return RunResult;
		}
		internal override void StopRunLoop(nint runLoop) => ReleaseRun.Set();
		internal override void Release(nint handle) => Interlocked.Increment(ref ReleaseCount);
	}

	private sealed class FakeMouseEventSink : MacMouseEventStream.Sink
	{
		internal readonly List<(MouseButton Button, bool Down, int ClickCount, long EventNumber)> Buttons = new();
		internal readonly List<(int X, int Y)> Moves = new();
		internal readonly List<MouseButton> DragButtons = new();
		internal readonly List<(int X, int Y, double Interval)> Warps = new();
		internal bool ReadPendingCursor;
		private readonly Queue<(int X, int Y, ulong Stamp)> pendingLocations = new();
		internal MacMouseEventStream Stream;
		internal long Timestamp = 1000;
		internal bool DeferPostedLocations;
		internal bool DropAcknowledgements;
		internal int PendingPostCount => pendingLocations.Count;
		internal int CursorX;
		internal int CursorY;
		internal double LocationX;
		internal double LocationY;
		internal bool FailLocation;
		internal bool ClampWarps;
		internal int FailWarpCall;
		internal int FailedWarpCount = 1;
		private int warpCalls;
		internal double HoldInterval = 0.25;
		internal Rectangle[] DisplayBounds = [];
		internal int FailMoves;
		internal int FailButtons;
		internal void ApplyPostedLocations()
		{
			while (pendingLocations.TryDequeue(out var location))
				ApplyPostedLocation(location.X, location.Y, location.Stamp);
		}
		internal void ApplyNextPostedLocation()
		{
			if (pendingLocations.TryDequeue(out var location))
				ApplyPostedLocation(location.X, location.Y, location.Stamp);
		}
		private void ApplyPostedLocation(int x, int y, ulong stamp)
		{
			CursorX = x;
			CursorY = y;
			LocationX = x;
			LocationY = y;
			if (!DropAcknowledgements)
				Stream?.AcknowledgePost(stamp);
		}
		internal override bool TryGetCursorPosition(out int x, out int y)
		{
			x = CursorX;
			y = CursorY;
			return true;
		}
		internal override bool TryGetCursorLocation(out double x, out double y)
		{
			ReadPendingCursor |= pendingLocations.Count > 0;
			x = LocationX;
			y = LocationY;
			return !FailLocation;
		}
		internal override bool WarpCursor(int x, int y)
		{
			Warps.Add((x, y, HoldInterval));
			if (++warpCalls >= FailWarpCall && warpCalls < FailWarpCall + FailedWarpCount)
				return false;
			if (ClampWarps)
			{
				x = Math.Clamp(x, DisplayBounds[0].Left, DisplayBounds[0].Right - 1);
				y = Math.Clamp(y, DisplayBounds[0].Top, DisplayBounds[0].Bottom - 1);
			}
			CursorX = x;
			CursorY = y;
			LocationX = x;
			LocationY = y;
			return true;
		}
		internal override double GetHoldInterval() => HoldInterval;
		internal override void SetHoldInterval(double seconds) => HoldInterval = seconds;
		internal override Rectangle[] GetDisplayBounds() => DisplayBounds;
		internal override bool PostMove(int x, int y, long extraInfo, MouseButton draggingButton, ulong postTimestamp)
		{
			if (FailMoves-- > 0)
				return false;
			Moves.Add((x, y));
			DragButtons.Add(draggingButton);
			if (DeferPostedLocations)
				pendingLocations.Enqueue((x, y, postTimestamp));
			else
				ApplyPostedLocation(x, y, postTimestamp);
			return true;
		}
		internal override bool PostButton(MouseButton button, bool down, int x, int y, long extraInfo,
			int clickCount, long eventNumber, ulong postTimestamp)
		{
			if (FailButtons-- > 0)
				return false;
			Buttons.Add((button, down, clickCount, eventNumber));
			if (DeferPostedLocations)
				pendingLocations.Enqueue((x, y, postTimestamp));
			else
				ApplyPostedLocation(x, y, postTimestamp);
			return true;
		}
	}
}

#endif