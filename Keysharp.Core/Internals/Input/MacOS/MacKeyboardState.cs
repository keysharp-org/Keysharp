#if OSX
using Keysharp.Internals.Input.Hooks;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.MacOS
{
	/// <summary>Owns the observed and sender-predicted state of the Quartz keyboard stream.</summary>
	internal sealed class MacKeyboardState
	{
		internal enum Origin : byte { KeysharpSynthetic, PhysicalHid, ForeignSynthetic, Unknown }
		private readonly Lock sync = new();
		private uint observedModifiers;
		private bool observedCapsDown;
		private uint? postedModifiers;
		private bool? postedCapsLock;
		private int sendDepth;
		private uint touchedModifiers;
		private bool touchedCapsLock;
		private uint? pendingModifiers;
		private bool? pendingCapsLock;
		private long modifierRevision;
		private long modifierSenderRevision;
		private long capsLockSenderRevision;

		internal uint ObservedModifiers { get { lock (sync) return observedModifiers; } }
		internal (long Modifiers, long CapsLock) SenderRevisions
			{ get { lock (sync) return (modifierSenderRevision, capsLockSenderRevision); } }

		internal void Resync(uint sourceState = MacNativeInput.kCGEventSourceStateHIDSystemState)
		{
			lock (sync)
			{
				foreach (var vk in ModifierLRVks)
				{
					if (!TryQuery(vk, sourceState, out var down))
						continue;
					var mask = ModifierLRMaskFromVK(vk);
					if (down) observedModifiers |= mask;
					else observedModifiers &= ~mask;
				}

				if (TryQuery(VK_CAPITAL, sourceState, out var capsDown))
					observedCapsDown = capsDown;
				var flags = MacNativeInput.CGEventSourceFlagsState(sourceState);
				ObserveStateCore(observedModifiers,
					(flags & MacNativeInput.kCGEventFlagMaskAlphaShift) != 0);
			}
		}

		internal bool ApplyFlagsChanged(uint vk, ulong flags, Origin origin, bool? queriedDown,
			MacNativeInput.InjectedEventKind injectedKind, long expectedModifierRevision = -1,
			long expectedCapsLockRevision = -1)
		{
			lock (sync)
			{
				var isCaps = vk == VK_CAPITAL;
				// Side-specific only: observedModifiers tracks each side separately, and AggregateFlag() has no
				// entry for a neutral VK, so a neutral one would resolve to "up" and clear both sides at once.
				var mask = SideSpecificModifierLRMaskFromVK(vk);
				if (mask == 0 && !isCaps)
					return false;

				var down = origin == Origin.KeysharpSynthetic
					? (injectedKind & MacNativeInput.InjectedEventKind.KeyUp) == 0
					: queriedDown ?? ((flags & AggregateFlag(vk)) != 0
						&& (isCaps ? !observedCapsDown : (observedModifiers & mask) == 0));

				if (isCaps) observedCapsDown = down;
				else if (down) observedModifiers |= mask;
				else observedModifiers &= ~mask;

				if (origin != Origin.KeysharpSynthetic)
					ObserveStateCore(observedModifiers,
						(flags & MacNativeInput.kCGEventFlagMaskAlphaShift) != 0,
						expectedModifierRevision < 0 || modifierSenderRevision == expectedModifierRevision,
						expectedCapsLockRevision < 0 || capsLockSenderRevision == expectedCapsLockRevision);
				return !down;
			}
		}

		internal EventMask ToEventMask(ulong flags)
		{
			lock (sync)
			{
				var result = EventMask.None;
				if ((observedModifiers & MOD_LSHIFT) != 0) result |= EventMask.LeftShift;
				if ((observedModifiers & MOD_RSHIFT) != 0) result |= EventMask.RightShift;
				if ((observedModifiers & MOD_LCONTROL) != 0) result |= EventMask.LeftCtrl;
				if ((observedModifiers & MOD_RCONTROL) != 0) result |= EventMask.RightCtrl;
				if ((observedModifiers & MOD_LALT) != 0) result |= EventMask.LeftAlt;
				if ((observedModifiers & MOD_RALT) != 0) result |= EventMask.RightAlt;
				if ((observedModifiers & MOD_LWIN) != 0) result |= EventMask.LeftMeta;
				if ((observedModifiers & MOD_RWIN) != 0) result |= EventMask.RightMeta;
				if ((flags & MacNativeInput.kCGEventFlagMaskAlphaShift) != 0) result |= EventMask.CapsLock;
				return result;
			}
		}

		internal void BeginSend(uint nativeModifiers, bool nativeCapsLock)
		{
			lock (sync)
			{
				if (sendDepth++ != 0)
					return;
				postedModifiers = nativeModifiers;
				modifierRevision++;
				postedCapsLock = nativeCapsLock;
				modifierSenderRevision++;
				capsLockSenderRevision++;
				touchedModifiers = 0;
				touchedCapsLock = false;
				pendingModifiers = null;
				pendingCapsLock = null;
			}
		}

		internal void EndSend()
		{
			lock (sync)
			{
				if (sendDepth == 0 || --sendDepth != 0)
					return;
				if (pendingModifiers is { } nativeModifiers)
				{
					var merge = MODLR_MASK & ~touchedModifiers;
					postedModifiers = (postedModifiers.GetValueOrDefault() & ~merge) | (nativeModifiers & merge);
					modifierRevision++;
				}
				if (pendingCapsLock is { } nativeCapsLock && !touchedCapsLock)
					postedCapsLock = nativeCapsLock;
				touchedModifiers = 0;
				touchedCapsLock = false;
				pendingModifiers = null;
				pendingCapsLock = null;
			}
		}

		internal uint GetModifiers(Func<uint> initialize)
		{
			lock (sync)
			{
				return postedModifiers ??= initialize();
			}
		}

		internal void PostModifier(uint mask, bool down, Func<uint, bool> post)
		{
			uint? previous;
			uint previousTouched;
			uint value;
			long revision;
			lock (sync)
			{
				previous = postedModifiers;
				previousTouched = touchedModifiers;
				value = postedModifiers.GetValueOrDefault();
				value = down ? value | mask : value & ~mask;
				if (sendDepth != 0)
					touchedModifiers |= mask;
				postedModifiers = value;
				revision = ++modifierRevision;
				modifierSenderRevision++;
			}

			var posted = false;
			try { posted = post(value); }
			finally
			{
				if (!posted)
					lock (sync)
					{
						if (modifierRevision == revision)
						{
							postedModifiers = previous;
							touchedModifiers = previousTouched;
							modifierRevision++;
						}
					}
			}
		}

		internal void SetModifiers(uint value)
		{
			lock (sync)
			{
				if (sendDepth != 0) touchedModifiers |= postedModifiers.GetValueOrDefault(value) ^ value;
				postedModifiers = value;
				modifierRevision++;
				modifierSenderRevision++;
			}
		}

		internal bool GetCapsLock(Func<bool> initialize)
		{
			lock (sync)
			{
				return postedCapsLock ??= initialize();
			}
		}

		internal void SetCapsLock(bool on)
		{
			lock (sync)
			{
				if (sendDepth != 0 && postedCapsLock.GetValueOrDefault(on) != on) touchedCapsLock = true;
				postedCapsLock = on;
				capsLockSenderRevision++;
			}
		}

		internal void ObserveModifiers(uint modifiers, long expectedSenderRevision = -1)
		{
			lock (sync)
			{
				observedModifiers = modifiers;
				ObserveStateCore(modifiers, false,
					expectedSenderRevision < 0 || modifierSenderRevision == expectedSenderRevision, updateCapsLock: false);
			}
		}

		internal void ObserveCapsLock(bool on)
		{
			lock (sync)
			{
				if (sendDepth == 0)
					postedCapsLock = on;
				else
				{
					pendingCapsLock = on;
					touchedCapsLock = false;
				}
			}
		}

		private void ObserveStateCore(uint modifiers, bool capsLock, bool updateModifiers = true,
			bool updateCapsLock = true)
		{
			if (sendDepth == 0)
			{
				if (updateModifiers)
				{
					postedModifiers = modifiers;
					modifierRevision++;
				}
				if (updateCapsLock) postedCapsLock = capsLock;
				return;
			}

			if (updateModifiers)
			{
				pendingModifiers = modifiers;
				touchedModifiers = 0;
				modifierRevision++;
			}
			if (updateCapsLock)
			{
				pendingCapsLock = capsLock;
				touchedCapsLock = false;
			}
		}

		internal static bool TryQuery(uint vk, uint sourceState, out bool down)
		{
			down = false;
			if (!KeyCodes.TryMapVkToMacCode(vk, out var keyCode))
				return false;
			try
			{
				down = MacNativeInput.CGEventSourceKeyState(sourceState, (ushort)keyCode);
				return true;
			}
			catch { return false; }
		}

		internal static bool? PhysicalModifierDown(uint vk, ulong flags)
		{
			var (key, other) = vk switch
			{
				VK_LSHIFT => (MacNativeInput.NX_DEVICELSHIFTKEYMASK, MacNativeInput.NX_DEVICERSHIFTKEYMASK),
				VK_RSHIFT => (MacNativeInput.NX_DEVICERSHIFTKEYMASK, MacNativeInput.NX_DEVICELSHIFTKEYMASK),
				VK_LCONTROL => (MacNativeInput.NX_DEVICELCTLKEYMASK, MacNativeInput.NX_DEVICERCTLKEYMASK),
				VK_RCONTROL => (MacNativeInput.NX_DEVICERCTLKEYMASK, MacNativeInput.NX_DEVICELCTLKEYMASK),
				VK_LMENU => (MacNativeInput.NX_DEVICELALTKEYMASK, MacNativeInput.NX_DEVICERALTKEYMASK),
				VK_RMENU => (MacNativeInput.NX_DEVICERALTKEYMASK, MacNativeInput.NX_DEVICELALTKEYMASK),
				VK_LWIN => (MacNativeInput.NX_DEVICELCMDKEYMASK, MacNativeInput.NX_DEVICERCMDKEYMASK),
				VK_RWIN => (MacNativeInput.NX_DEVICERCMDKEYMASK, MacNativeInput.NX_DEVICELCMDKEYMASK),
				_ => (0UL, 0UL)
			};
			// Event flags describe this transition; a live key-state query can already reflect a later one.
			if (key != 0 && ((flags & (key | other)) != 0 || (flags & AggregateFlag(vk)) == 0))
				return (flags & key) != 0;
			return null;
		}

		private static ulong AggregateFlag(uint vk) => vk switch
		{
			VK_LSHIFT or VK_RSHIFT => MacNativeInput.kCGEventFlagMaskShift,
			VK_LCONTROL or VK_RCONTROL => MacNativeInput.kCGEventFlagMaskControl,
			VK_LMENU or VK_RMENU => MacNativeInput.kCGEventFlagMaskAlternate,
			VK_LWIN or VK_RWIN => MacNativeInput.kCGEventFlagMaskCommand,
			VK_CAPITAL => MacNativeInput.kCGEventFlagMaskAlphaShift,
			_ => 0
		};
	}
}
#endif
