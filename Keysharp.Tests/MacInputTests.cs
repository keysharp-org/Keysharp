#if OSX
using System.Collections.Concurrent;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Input.Hooks;
using Keysharp.Internals.Input.Hooks.MacOS;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.MacOS;
using Keysharp.Internals.Window;
using Keysharp.Internals.Window.MacOS;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class MacInputTests
	{
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
			stream.ObserveMove(100, 200, stream.SenderRevision);
			stream.InvalidatePosition();

			Assert.IsTrue(stream.MoveRelative(5, -2, KeyIgnore));
			Assert.AreEqual((25, 28), sink.Moves.Single());
		}

		[Test, Category("Input")]
		public void NativeDragState()
		{
			var sink = new FakeMouseEventSink();
			var stream = new MacMouseEventStream(sink);
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
			var tap = new MacNativeEventTap(1, (_, _) => false, () => { }, (failed, _) =>
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
			var tap = new MacNativeEventTap(1, (_, _) => false,
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
			var tap = new MacNativeEventTap(1, (_, _) => false, () => { }, (_, _) =>
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
			var tap = new MacNativeEventTap(1, (_, _) => false, () => { }, (_, _) => { }, driver);
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
			var tap = new MacNativeEventTap(1, (_, _) => false,
				() => throw new InvalidOperationException("resync failure"), (_, _) => { }, driver);
			Assert.IsTrue(tap.Start(1000));
			Assert.AreNotEqual(nint.Zero, driver.Callback(nint.Zero,
				MacNativeInput.kCGEventTapDisabledByTimeout, (nint)123, nint.Zero));
			Assert.AreEqual(1, driver.EnableCount);
			Assert.IsTrue(tap.Stop());
		}

		[Test, Category("Input")]
		public void StoppedWatchdog()
		{
			var driver = new FakeEventTapDriver { RunResult = 2 };
			var resyncCount = 0;
			var tap = new MacNativeEventTap(1, (_, _) => false,
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
			internal int RunResult { get; init; } = 3;
			internal int ReleaseCount;
			internal int EnableCount;
			internal int CreateCount;
			internal bool BlockAddSource;
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
					Interlocked.Increment(ref EnableCount);
				else
					valid = false;
			}
			internal override bool IsTapValid(nint tap) => valid;
			internal override int RunInDefaultMode(double seconds)
			{
				RunEntered.Set();
				if (RunResults.TryDequeue(out var result))
					return result;
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
			internal int CursorX;
			internal int CursorY;
			internal int FailMoves;
			internal int FailButtons;
			internal override bool TryGetCursorPosition(out int x, out int y)
			{
				x = CursorX;
				y = CursorY;
				return true;
			}
			internal override bool PostMove(int x, int y, long extraInfo, MouseButton draggingButton)
			{
				if (FailMoves-- > 0)
					return false;
				Moves.Add((x, y));
				DragButtons.Add(draggingButton);
				return true;
			}
			internal override bool PostButton(MouseButton button, bool down, int x, int y, long extraInfo,
				int clickCount, long eventNumber)
			{
				if (FailButtons-- > 0)
					return false;
				Buttons.Add((button, down, clickCount, eventNumber));
				return true;
			}
		}
	}
}
#endif
