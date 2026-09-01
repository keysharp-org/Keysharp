#if OSX
using System.Runtime.InteropServices;
using Keysharp.Builtins;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.Hooks;
using Keysharp.Internals.Input.Hooks.MacOS;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.MacOS
{
	internal static partial class MacNativeInput
	{
		private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
		private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		internal const uint kCGEventSourceStateCombinedSessionState = 0;
		internal const uint kCGEventSourceStateHIDSystemState = 1;
		internal const uint kCGHIDEventTap = 0;
		internal const uint kCGSessionEventTap = 1;
		internal const uint kCGHeadInsertEventTap = 0;
		internal const uint kCGEventTapOptionDefault = 0;
		internal const uint kCGEventSourceUserData = 42;
		internal const uint kCGEventSourceUnixProcessID = 41;
		internal const uint kCGEventSourceStateID = 45;
		internal const uint kCGKeyboardEventAutorepeat = 8;
		internal const uint kCGKeyboardEventKeycode = 9;
		internal const uint kCGMouseEventNumber = 0;
		internal const uint kCGMouseEventClickState = 1;
		internal const uint kCGMouseEventButtonNumber = 3;
		internal const uint kCGMouseEventDeltaX = 4;
		internal const uint kCGMouseEventDeltaY = 5;
		internal const uint kCGScrollWheelEventDeltaAxis1 = 11;
		internal const uint kCGScrollWheelEventDeltaAxis2 = 12;
		internal const uint kCGScrollWheelEventFixedPtDeltaAxis1 = 93;
		internal const uint kCGScrollWheelEventFixedPtDeltaAxis2 = 94;
		internal const uint kCGScrollWheelEventPointDeltaAxis1 = 96;
		internal const uint kCGScrollWheelEventPointDeltaAxis2 = 97;
		internal const uint kCGScrollWheelEventIsContinuous = 88;

		internal const uint kCGEventLeftMouseDown = 1;
		internal const uint kCGEventLeftMouseUp = 2;
		internal const uint kCGEventRightMouseDown = 3;
		internal const uint kCGEventRightMouseUp = 4;
		internal const uint kCGEventMouseMoved = 5;
		internal const uint kCGEventLeftMouseDragged = 6;
		internal const uint kCGEventRightMouseDragged = 7;
		internal const uint kCGEventKeyDown = 10;
		internal const uint kCGEventKeyUp = 11;
		internal const uint kCGEventFlagsChanged = 12;
		internal const uint kCGEventScrollWheel = 22;
		internal const uint kCGEventOtherMouseDown = 25;
		internal const uint kCGEventOtherMouseUp = 26;
		internal const uint kCGEventOtherMouseDragged = 27;
		internal const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
		internal const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;
		internal const uint kCGAnyInputEventType = 0xFFFFFFFF;

		internal const ulong kCGEventFlagMaskAlphaShift = 1UL << 16;
		internal const ulong kCGEventFlagMaskShift = 1UL << 17;
		internal const ulong kCGEventFlagMaskControl = 1UL << 18;
		internal const ulong kCGEventFlagMaskAlternate = 1UL << 19;
		internal const ulong kCGEventFlagMaskCommand = 1UL << 20;

		// sourceUserData is the only application-owned field which survives a CGEventPost round-trip.
		// Give all Keysharp events a private signature and keep the original AHK extra-info value in a
		// signed 36-bit payload. Presence of a layout-derived Unicode string is not event identity: Quartz
		// associates such a string with ordinary virtual-key events automatically.
		private const ulong MetadataSignatureMask = 0xFFFF_FF00_0000_0000;
		private const ulong MetadataSignature = 0x4B53_4900_0000_0000; // "KSI"
		private const int MetadataKindShift = 36;
		private const ulong MetadataKindMask = 0x0000_00F0_0000_0000;
		private const ulong MetadataPayloadMask = 0x0000_000F_FFFF_FFFF;
		private const ulong MetadataPayloadSign = 0x0000_0008_0000_0000;

		[Flags]
		internal enum InjectedEventKind : byte
		{
			None = 0,
			UnicodeText = 1,
			KeyUp = 2,
			Mouse = 4
		}

		private static readonly nint runLoopDefaultMode = CreateRunLoopMode("kCFRunLoopDefaultMode");

		[StructLayout(LayoutKind.Sequential)]
		internal struct CGPoint
		{
			public double X;
			public double Y;
			public CGPoint(double x, double y) { X = x; Y = y; }
		}

		internal delegate nint CGEventTapCallBack(nint proxy, uint type, nint cgEvent, nint userInfo);

		[LibraryImport(ApplicationServices)]
		internal static partial nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

		[LibraryImport(ApplicationServices)]
		internal static partial nint CGEventCreateMouseEvent(nint source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

		[LibraryImport(ApplicationServices, EntryPoint = "CGEventCreateScrollWheelEvent2")]
		internal static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

		[LibraryImport(ApplicationServices)]
		internal static partial nint CGEventSourceCreate(uint stateId);

		[LibraryImport(ApplicationServices)]
		internal static partial void CGEventPost(uint tap, nint cgEvent);

		[LibraryImport(ApplicationServices)]
		internal static partial void CGEventSetIntegerValueField(nint cgEvent, uint field, long value);

		[LibraryImport(ApplicationServices)]
		internal static partial long CGEventGetIntegerValueField(nint cgEvent, uint field);

		[LibraryImport(ApplicationServices)]
		internal static partial double CGEventGetDoubleValueField(nint cgEvent, uint field);

		[LibraryImport(ApplicationServices)]
		internal static partial uint CGEventGetType(nint cgEvent);

		[LibraryImport(ApplicationServices)]
		internal static partial ulong CGEventGetFlags(nint cgEvent);

		[LibraryImport(ApplicationServices)]
		internal static partial CGPoint CGEventGetLocation(nint cgEvent);

		[LibraryImport(ApplicationServices)]
		internal static partial int CGWarpMouseCursorPosition(CGPoint point);

		[LibraryImport(ApplicationServices)]
		internal static partial void CGEventSetFlags(nint cgEvent, ulong flags);

		[LibraryImport(ApplicationServices)]
		internal static partial ulong CGEventSourceFlagsState(uint sourceState);

		[LibraryImport(ApplicationServices)]
		internal static partial double CGEventSourceSecondsSinceLastEventType(uint stateID, uint eventType);

		[LibraryImport(ApplicationServices)]
		[return: MarshalAs(UnmanagedType.I1)]
		internal static partial bool CGEventSourceKeyState(uint sourceState, ushort virtualKey);

		[LibraryImport(ApplicationServices)]
		[return: MarshalAs(UnmanagedType.I1)]
		internal static partial bool CGEventSourceButtonState(uint sourceState, uint button);

		// UniChar is UTF-16. Keep the native entry point pointer-based so the sender can pass a
		// scalar-sized slice of the caller's string without allocating a temporary substring.
		[DllImport(ApplicationServices, EntryPoint = "CGEventKeyboardSetUnicodeString")]
		private static extern unsafe void CGEventKeyboardSetUnicodeStringCore(nint cgEvent, long stringLength, char* unicodeString);

		internal static void CGEventKeyboardSetUnicodeString(nint cgEvent, long stringLength, string unicodeString)
		{
			var length = (int)Math.Clamp(stringLength, 0L, unicodeString.Length);
			SetKeyboardUnicodeString(cgEvent, unicodeString.AsSpan(0, length));
		}

		private static unsafe void SetKeyboardUnicodeString(nint cgEvent, ReadOnlySpan<char> unicodeString)
		{
			fixed (char* p = unicodeString)
				CGEventKeyboardSetUnicodeStringCore(cgEvent, unicodeString.Length, p);
		}

		// Pointer-based signature (UniChar == C# char, both UTF-16) so callers can pass a stack/Span buffer
		// and TryGetKeyboardUnicodeString need not allocate a marshalled char[] per keystroke.
		[DllImport(ApplicationServices)]
		internal static extern unsafe void CGEventKeyboardGetUnicodeString(nint cgEvent, long maxStringLength, out long actualStringLength, char* unicodeString);

		[DllImport(ApplicationServices)]
		internal static extern nint CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, CGEventTapCallBack callback, nint userInfo);

		[LibraryImport(ApplicationServices)]
		internal static partial void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.I1)] bool enable);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFRunLoopGetCurrent();

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRunLoopAddSource(nint rl, nint source, nint mode);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRunLoopRemoveSource(nint rl, nint source, nint mode);

		[LibraryImport(CoreFoundation)]
		internal static partial int CFRunLoopRunInMode(nint mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRunLoopStop(nint rl);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.I1)]
		internal static partial bool CFMachPortIsValid(nint port);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRelease(nint cf);

		[LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
		private static partial nint CFStringCreateWithCString(nint allocator, string cStr, uint encoding);

		internal static nint RunLoopDefaultMode => runLoopDefaultMode;

		internal static ulong KeyboardEventMask =>
			(1UL << (int)kCGEventKeyDown) |
			(1UL << (int)kCGEventKeyUp) |
			(1UL << (int)kCGEventFlagsChanged);

		internal static ulong MouseEventMask =>
			(1UL << (int)kCGEventLeftMouseDown) |
			(1UL << (int)kCGEventLeftMouseUp) |
			(1UL << (int)kCGEventRightMouseDown) |
			(1UL << (int)kCGEventRightMouseUp) |
			(1UL << (int)kCGEventMouseMoved) |
			(1UL << (int)kCGEventLeftMouseDragged) |
			(1UL << (int)kCGEventRightMouseDragged) |
			(1UL << (int)kCGEventScrollWheel) |
			(1UL << (int)kCGEventOtherMouseDown) |
			(1UL << (int)kCGEventOtherMouseUp) |
			(1UL << (int)kCGEventOtherMouseDragged);

		internal static ulong EventMaskFor(bool keyboard, bool mouse) =>
			(keyboard ? KeyboardEventMask : 0)
			| (mouse ? MouseEventMask | (1UL << (int)kCGEventFlagsChanged) : 0);
		internal static bool IsKeyboardEvent(uint type) => type < 64 && (KeyboardEventMask & (1UL << (int)type)) != 0;
		internal static bool IsMouseEvent(uint type) => type < 64 && (MouseEventMask & (1UL << (int)type)) != 0;

		internal static MacKeyboardState.Origin ClassifyEventOrigin(nint cgEvent, bool hasKeysharpMetadata)
		{
			if (hasKeysharpMetadata)
				return MacKeyboardState.Origin.KeysharpSynthetic;

			var sourcePid = CGEventGetIntegerValueField(cgEvent, kCGEventSourceUnixProcessID);
			var sourceState = CGEventGetIntegerValueField(cgEvent, kCGEventSourceStateID);
			if (sourcePid > 0 || sourceState is -1 or 0)
				return MacKeyboardState.Origin.ForeignSynthetic;
			if (sourceState == kCGEventSourceStateHIDSystemState)
				return MacKeyboardState.Origin.PhysicalHid;

			return MacKeyboardState.Origin.Unknown;
		}

		private static nint CreateRunLoopMode(string value)
			=> CFStringCreateWithCString(nint.Zero, value, 0x08000100); // kCFStringEncodingUTF8

		internal static long EncodeInjectedExtraInfo(long extraInfo, InjectedEventKind kind)
		{
			if (extraInfo is < -(1L << 35) or >= (1L << 35))
				throw new ArgumentOutOfRangeException(nameof(extraInfo), "macOS injected event metadata supports signed 36-bit extra-info values.");

			var encoded = MetadataSignature
				| (((ulong)kind << MetadataKindShift) & MetadataKindMask)
				| (unchecked((ulong)extraInfo) & MetadataPayloadMask);
			return unchecked((long)encoded);
		}

		internal static bool TryDecodeInjectedExtraInfo(long encoded, out long extraInfo, out InjectedEventKind kind)
		{
			var raw = unchecked((ulong)encoded);
			if ((raw & MetadataSignatureMask) != MetadataSignature)
			{
				extraInfo = encoded;
				kind = InjectedEventKind.None;
				return false;
			}

			var payload = raw & MetadataPayloadMask;
			if ((payload & MetadataPayloadSign) != 0)
				payload |= ~MetadataPayloadMask;

			extraInfo = unchecked((long)payload);
			kind = (InjectedEventKind)((raw & MetadataKindMask) >> MetadataKindShift);
			return true;
		}

		// One process-wide CGEventSource reused across every Post* call. CGEventSourceCreate makes a heavy
		// window-server round-trip, so creating+releasing one per event was costly over a large Send or a
		// smooth MouseMove loop. Use the combined-session state, as Apple specifies for applications posting
		// within a login session; HID-system state is intended for hardware drivers. Keyboard sequencing does
		// not rely on this global state table because delivery is asynchronous -- PostKeyboard sets the sender's
		// predicted flags explicitly. A null (zero) source is still valid for CGEventCreate*, so whatever the
		// factory returns is cached and never retried. The process owns it for its lifetime (there is no per-
		// instance shutdown hook for this static class), so it is not released per event.
		private static nint sharedEventSource;
		private static bool sharedEventSourceInitialized;
		private static object sharedEventSourceLock;

		private static nint SharedEventSource()
			=> LazyInitializer.EnsureInitialized(
				ref sharedEventSource, ref sharedEventSourceInitialized, ref sharedEventSourceLock,
				static () => CGEventSourceCreate(kCGEventSourceStateCombinedSessionState));

		// Queried from WindowServer's own event counters, so unlike CGEventTap this needs no
		// Accessibility/Input Monitoring permission and reflects physical + synthetic input alike.
		internal static bool TryGetIdleTime(out long milliseconds)
		{
			var seconds = CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateCombinedSessionState, kCGAnyInputEventType);

			if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
			{
				milliseconds = 0L;
				return false;
			}

			milliseconds = (long)(seconds * 1000.0);
			return true;
		}

		private static ulong FlagsForModifiers(uint modifiersLR, bool? capsLockOn)
		{
			// Retain CapsLock, whose toggle state is independent of the transient modifiers managed by Send.
			// Do not copy the source's Shift/Ctrl/Option/Cmd flags: delivery is asynchronous, so they may still
			// describe an earlier synthetic event in this same Send operation.
			var flags = capsLockOn.HasValue
				? capsLockOn.Value ? kCGEventFlagMaskAlphaShift : 0
				: CGEventSourceFlagsState(kCGEventSourceStateHIDSystemState) & kCGEventFlagMaskAlphaShift;

			if ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) != 0)
				flags |= kCGEventFlagMaskShift;
			if ((modifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) != 0)
				flags |= kCGEventFlagMaskControl;
			if ((modifiersLR & (MOD_LALT | MOD_RALT)) != 0)
				flags |= kCGEventFlagMaskAlternate;
			if ((modifiersLR & (MOD_LWIN | MOD_RWIN)) != 0)
				flags |= kCGEventFlagMaskCommand;

			return flags;
		}

		internal static nint CreateKeyboardEvent(uint vk, bool keyDown, long extraInfo, uint modifiersLR,
			bool autoRepeat = false, bool? capsLockOn = null)
		{
			if (!KeyCodes.TryMapVkToMacCode(vk, out var keyCode))
				return nint.Zero;

			var ev = CGEventCreateKeyboardEvent(SharedEventSource(), (ushort)keyCode, keyDown);

			if (ev != nint.Zero)
			{
				CGEventSetFlags(ev, FlagsForModifiers(modifiersLR, capsLockOn));
				if (keyDown && autoRepeat)
					CGEventSetIntegerValueField(ev, kCGKeyboardEventAutorepeat, 1);
				var kind = keyDown ? InjectedEventKind.None : InjectedEventKind.KeyUp;
				CGEventSetIntegerValueField(ev, kCGEventSourceUserData, EncodeInjectedExtraInfo(extraInfo, kind));
			}

			return ev;
		}

		internal static bool PostKeyboard(uint vk, bool keyDown, long extraInfo, uint modifiersLR,
			bool autoRepeat = false, bool? capsLockOn = null)
		{
			var ev = CreateKeyboardEvent(vk, keyDown, extraInfo, modifiersLR, autoRepeat, capsLockOn);
			return PostAndRelease(ev);
		}

		internal static void PostUnicodeText(string text, long extraInfo)
		{
			if (string.IsNullOrEmpty(text))
				return;

			for (var offset = 0; offset < text.Length;)
			{
				var length = NextUnicodeScalarLength(text.AsSpan(offset));
				PostUnicodeChunk(text.AsSpan(offset, length), extraInfo);
				offset += length;
			}
		}

		// A CGEvent is the event tap's smallest suppressible unit. Keep one Unicode scalar per
		// event so suppressing one character can never discard adjacent injected text. A valid
		// surrogate pair stays atomic because Quartz cannot inject either half independently.
		internal static int NextUnicodeScalarLength(ReadOnlySpan<char> text)
			=> text.Length >= 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]) ? 2 : 1;

		internal static bool TryGetKeyboardUnicodeString(nint cgEvent, Span<char> buffer, out int length)
		{
			length = 0;
			if (cgEvent == nint.Zero || buffer.Length == 0)
				return false;

			long actualLength;

			unsafe
			{
				fixed (char* p = buffer)
					CGEventKeyboardGetUnicodeString(cgEvent, buffer.Length, out actualLength, p);
			}

			length = (int)Math.Clamp(actualLength, 0, buffer.Length);
			return length != 0;
		}

		private static void PostUnicodeChunk(ReadOnlySpan<char> text, long extraInfo)
		{
			var source = SharedEventSource();
			PostUnicodeEvent(source, text, extraInfo, true);
			PostUnicodeEvent(source, text, extraInfo, false);
		}

		private static void PostUnicodeEvent(nint source, ReadOnlySpan<char> text, long extraInfo, bool down)
		{
			var ev = CreateUnicodeEvent(source, text, extraInfo, down);
			if (!PostAndRelease(ev))
				throw new InvalidOperationException("Core Graphics could not create a Unicode keyboard event.");
		}

		internal static nint CreateUnicodeEvent(nint source, string text, long extraInfo, bool down)
			=> CreateUnicodeEvent(source, text.AsSpan(), extraInfo, down);

		private static nint CreateUnicodeEvent(nint source, ReadOnlySpan<char> text, long extraInfo, bool down)
		{
			var ev = CGEventCreateKeyboardEvent(source, 0, down);
			if (ev == nint.Zero)
				return nint.Zero;

			SetKeyboardUnicodeString(ev, text);
			// A Unicode payload is already the final literal text. Inheriting keyboard flags can transform it
			// or invoke an application shortcut, so clear modifiers (including CapsLock) explicitly.
			CGEventSetFlags(ev, 0);
			var kind = InjectedEventKind.UnicodeText | (down ? InjectedEventKind.None : InjectedEventKind.KeyUp);
			CGEventSetIntegerValueField(ev, kCGEventSourceUserData, EncodeInjectedExtraInfo(extraInfo, kind));
			return ev;
		}

		internal static bool PostMouseMove(int x, int y, long extraInfo, MouseButton draggingButton = MouseButton.NoButton)
			=> PostMouseEvent(MouseMoveType(draggingButton), new CGPoint(x, y), ToCGMouseButtonOrZero(draggingButton), extraInfo);

		internal static bool PostMouseButton(MouseButton button, bool down, int x, int y, long extraInfo, int clickCount = 1, long eventNumber = 0)
		{
			var cgButton = ToCGMouseButton(button);
			if (cgButton == uint.MaxValue)
				return false;

			var type = (button, down) switch
			{
				(MouseButton.Button1, true) => kCGEventLeftMouseDown,
				(MouseButton.Button1, false) => kCGEventLeftMouseUp,
				(MouseButton.Button2, true) => kCGEventRightMouseDown,
				(MouseButton.Button2, false) => kCGEventRightMouseUp,
				(_, true) => kCGEventOtherMouseDown,
				_ => kCGEventOtherMouseUp
			};

			return PostMouseEvent(type, new CGPoint(x, y), cgButton, extraInfo, Math.Max(1, clickCount), eventNumber);
		}

		internal static void PostMouseWheel(short delta, MouseWheelScrollDirection direction, long extraInfo)
		{
			PostAndRelease(CreateScrollWheelEvent(delta, direction, extraInfo));
		}

		internal static nint CreateScrollWheelEvent(short delta, MouseWheelScrollDirection direction, long extraInfo)
		{
			var clicks = delta / (int)MouseUtils.WHEEL_DELTA;
			if (clicks == 0)
				clicks = Math.Sign(delta);

			var source = SharedEventSource();
			var ev = direction == MouseWheelScrollDirection.Vertical
				? CGEventCreateScrollWheelEvent2(source, 1, 1, clicks, 0, 0)
				: CGEventCreateScrollWheelEvent2(source, 1, 2, 0, clicks, 0);

			if (ev != nint.Zero)
			{
				// Quartz's line constructor accepts only integers, while Windows/input service carry a
				// signed 120-unit delta (including partial notches). Mark partial values continuous
				// and populate the 16.16 fixed-point line field so the value survives our hook and
				// applications which consume precise scrolling.
				if (delta % (int)MouseUtils.WHEEL_DELTA != 0)
				{
					var preciseLines = delta / (double)MouseUtils.WHEEL_DELTA;
					var fixedDelta = (long)Math.Round(preciseLines * 65536.0);
					var lineField = direction == MouseWheelScrollDirection.Vertical
						? kCGScrollWheelEventDeltaAxis1
						: kCGScrollWheelEventDeltaAxis2;
					var fixedField = direction == MouseWheelScrollDirection.Vertical
						? kCGScrollWheelEventFixedPtDeltaAxis1
						: kCGScrollWheelEventFixedPtDeltaAxis2;
					CGEventSetIntegerValueField(ev, lineField, 0);
					CGEventSetIntegerValueField(ev, fixedField, fixedDelta);
					CGEventSetIntegerValueField(ev, kCGScrollWheelEventIsContinuous, 1);
				}

				CGEventSetIntegerValueField(ev, kCGEventSourceUserData,
					EncodeInjectedExtraInfo(extraInfo, InjectedEventKind.Mouse));
			}

			return ev;
		}

		internal static nint CreateMouseEvent(uint type, CGPoint point, uint button, long extraInfo, int clickCount = 0, long eventNumber = 0)
		{
			var ev = CGEventCreateMouseEvent(SharedEventSource(), type, point, button);

			if (ev != nint.Zero)
			{
				if (clickCount > 0)
					CGEventSetIntegerValueField(ev, kCGMouseEventClickState, clickCount);
				if (eventNumber != 0)
					CGEventSetIntegerValueField(ev, kCGMouseEventNumber, eventNumber);
				CGEventSetIntegerValueField(ev, kCGEventSourceUserData,
					EncodeInjectedExtraInfo(extraInfo, InjectedEventKind.Mouse));
			}

			return ev;
		}

		private static bool PostMouseEvent(uint type, CGPoint point, uint button, long extraInfo, int clickCount = 0, long eventNumber = 0)
			=> PostAndRelease(CreateMouseEvent(type, point, button, extraInfo, clickCount, eventNumber));

		private static bool PostAndRelease(nint ev)
		{
			if (ev == nint.Zero)
				return false;

			try { CGEventPost(kCGHIDEventTap, ev); }
			finally { CFRelease(ev); }
			return true;
		}

		internal static uint MouseMoveType(MouseButton draggingButton) => draggingButton switch
		{
			MouseButton.Button1 => kCGEventLeftMouseDragged,
			MouseButton.Button2 => kCGEventRightMouseDragged,
			MouseButton.Button3 or MouseButton.Button4 or MouseButton.Button5 => kCGEventOtherMouseDragged,
			_ => kCGEventMouseMoved
		};

		internal static MouseButton ToMouseButton(uint type, uint buttonNumber) => type switch
		{
			kCGEventLeftMouseDown or kCGEventLeftMouseUp => MouseButton.Button1,
			kCGEventRightMouseDown or kCGEventRightMouseUp => MouseButton.Button2,
			kCGEventOtherMouseDown or kCGEventOtherMouseUp => buttonNumber switch
			{
				2 => MouseButton.Button3,
				3 => MouseButton.Button4,
				4 => MouseButton.Button5,
				_ => MouseButton.NoButton
			},
			_ => MouseButton.NoButton
		};

		private static uint ToCGMouseButton(MouseButton button) => button switch
		{
			MouseButton.Button1 => 0,
			MouseButton.Button2 => 1,
			MouseButton.Button3 => 2,
			MouseButton.Button4 => 3,
			MouseButton.Button5 => 4,
			_ => uint.MaxValue
		};

		private static uint ToCGMouseButtonOrZero(MouseButton button)
		{
			var result = ToCGMouseButton(button);
			return result == uint.MaxValue ? 0 : result;
		}
	}

}
#endif
