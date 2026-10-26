using Keysharp.Builtins;
using Keysharp.Components.Input;

namespace Keysharp.Internals.Input.Keyboard
{
	internal sealed class KeyState(Script script) : IDisposable
	{
		private readonly ProviderSlot<IKeyStateProvider> provider = new(script, "key-state provider");

		internal bool? GetState(uint vk, KeyStateTypes type)
		{
			if (provider.Enter() is not { } lease)
				return provider.IsRetired ? null : GetNativeState(vk, type);

			try
			{
				var mode = type switch { KeyStateTypes.Physical => "P", KeyStateTypes.Toggle => "T", _ => "L" };
				if (lease.Provider.TryGetKeyState(vk, mode, out var down)) return down;
			}
			catch (Exception ex) when (ex is not KeysharpException and not Keysharp.Builtins.Flow.UserRequestedExitException)
			{
				_ = Errors.OSErrorOccurredWithMessage($"Key-state provider query failed: {ex.Message}");
				return null;
			}
			finally
			{
				lease.Exit();
			}

			return provider.IsRetired ? null : GetNativeState(vk, type);
		}

		internal IDisposable Register(IKeyStateProvider replacement) => provider.Register(replacement);

		public void Dispose() => provider.Dispose();

		internal bool GetNativeState(uint vk, KeyStateTypes keyStateType)
		{
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;

			if (vk == 0)
				return false;

#if !WINDOWS
			if (keyStateType == KeyStateTypes.Physical && ht.UsesHookProvider && (MouseUtils.IsMouseVK(vk) ? ht.HasMouseHook() : ht.HasKbdHook()))
				return vk < ht.physicalKeyState.Length && (ht.physicalKeyState[vk] & KeyboardMouseSender.StateDown) != 0;
#endif
#if LINUX || OSX
			EnsureKeyStateQueryPermission(vk, keyStateType);
#endif
#if LINUX
			// Device snapshots represent overlapping holds on multiple keyboards or mice.
			if (keyStateType == KeyStateTypes.Physical
				&& (MouseUtils.IsMouseVK(vk)
					? Platform.Mouse.TryGetButtonStatePhysical(vk, out var isDown)
					: Platform.Keyboard.TryGetKeyStatePhysical(vk, out isDown)))
				return isDown;

			if (keyStateType != KeyStateTypes.Toggle)
				return ht.IsKeyDownLogical(vk);
#endif

			switch (keyStateType)
			{
				case KeyStateTypes.Toggle:
					return ht.IsKeyToggledOn(vk);

				case KeyStateTypes.Physical:
				{
					// Physical queries must not install hooks; absent a hook, AHK falls back to OS state.
					var isMouse = MouseUtils.IsMouseVK(vk);

					if (isMouse ? ht.HasMouseHook() : ht.HasKbdHook())
					{
						if (!isMouse)
						{
							bool? dummy = null;

							// Correct tracked modifiers before reading their physical state.
							if (ht.KeyToModifiersLR(vk, 0, ref dummy) != 0)
								_ = kbdMouseSender.GetModifierLRState(true);

							if (vk < ht.physicalKeyState.Length)
								return (ht.physicalKeyState[vk] & KeyboardMouseSender.StateDown) != 0;

							return false;
						}

						return (ht.physicalKeyState[vk] & KeyboardMouseSender.StateDown) != 0;
					}

					// macOS can expose physical keyboard state without a process-local hook.
					if (!isMouse && Platform.Keyboard.TryGetKeyStatePhysical(vk, out var physicalDown))
						return physicalDown;

					break;
				}
			}

			return ht.IsKeyDownLogical(vk);
		}

#if LINUX || OSX
		private void EnsureKeyStateQueryPermission(uint vk, KeyStateTypes keyStateType)
		{
			// Modifier and lock-toggle snapshots do not require input monitoring access.
			var lockToggle = keyStateType == KeyStateTypes.Toggle
				&& vk is VirtualKeys.VK_CAPITAL or VirtualKeys.VK_NUMLOCK or VirtualKeys.VK_SCROLL;

			if (lockToggle || KeyboardUtils.IsModifierVk(vk))
				return;

#if LINUX
			var required = MouseUtils.IsMouseVK(vk)
				? Keysharp.Internals.Input.Linux.KeysharpInputClient.Operations.QueryPointerButtons
				: Keysharp.Internals.Input.Linux.KeysharpInputClient.Operations.QueryKeyState;

			if (script.LinuxServices.Input.HasInputOperation(required))
				return;
#endif
			_ = script.Permissions.EnsureCapabilities(
				inputMonitoring: true, inputControl: false, operation: "read input state");
		}
#endif
	}
}
