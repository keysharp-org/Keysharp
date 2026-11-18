# Cross-platform TODO backlog

Shared, OS-agnostic work from the pre-release source-TODO triage (2026-06-19): architectural
abstractions and features that aren't tied to one platform but need a per-OS implementation.

---

## Architecture: "Windows-first → common base + OS-derived" refactors

Several core classes were written Windows-first with a note to factor out a platform-neutral
base and OS-specific derivations. These are the highest-value structural cleanups, but they
touch core subsystems, so care is needed.

- [ ] **`InputType` → common base + OS-derived.** `Keysharp.Core/Internals/Input/InputType.cs:20`
  Most fields (buffer, end-chars, modifiers) are platform-agnostic; isolate the Windows-specific
  parts into a derived class so Linux/macOS can derive their own.

- [ ] **Hook message queue: pass object references, not array indices.**
  `Keysharp.Core/Internals/Input/Hooks/HookThread.cs:3631`
  `AHK_HOOK_HOTKEY` carries `hotkeyIDToPost` as a `wParam` index into `shk[]`. Passing the resolved
  object is cleaner and removes index/stale-array hazards. Deep change to the messaging path —
  test hotkey delivery thoroughly on every platform.



---

## Features that need a non-Windows implementation

- [ ] **Mouse button swap (logical → physical) on Linux.**
  `keysharp-desktop` exposes the X11 pointer map, but
  the native Linux sender still needs its desktop-specific primary-button setting. It must be
  mapped without double-swapping input that the compositor already remaps. macOS reads the HID
  setting and keeps Quartz's logical button events unchanged.

- [ ] **Joystick backend on macOS needs a first run.** `Internals/Input/MacOS/MacJoystick.cs`
  reads HID joysticks and gamepads through IOKit's HID manager and feeds the shared polling in
  `Internals/Input/Joystick/Joystick.cs`. It has only been compile-checked. Verify on hardware
  that devices list without an Input Monitoring prompt, that values are live before the first
  report, that hotplug is picked up, and how Xbox and PlayStation controllers lay out their axes.

---

## Manual verification before promoting Partial entries

### Linux

- [ ] **WinEvent caret events.** `WinEvent.OnCaretMove` comes from the AT-SPI `object:text-caret-moved`
  signal, shared by both Linux backends (accessibility is display-server agnostic). Verify that events
  arrive while typing in GTK and Qt text widgets, that the reported handle is the caret owner's
  top-level window, and that the `A_EventInfo` rectangle is in screen coordinates for a native Wayland
  client — those report their toplevel rooted at (0,0), so the rectangle is shifted by the window
  origin using a containment heuristic that has not been exercised on a real compositor yet.
- [ ] **Linux controller layout.** The mapping in `Internals/Input/Linux/LinuxJoystick.cs` is tested
  against simulated listings only. With an Xbox controller, compare `JoyX`/`JoyY`/`JoyZ`/`JoyR`/`JoyU`,
  `JoyPOV` and `Joy1` to `Joy10` against the same controller on Windows, in particular that the left
  trigger raises `JoyZ`, and confirm that a `Joy1::` hotkey fires on X11 and on Wayland.
- [ ] **Linux volume labels.** On disposable ext4, FAT/exFAT, NTFS, XFS and btrfs volumes, change and
  restore the label and record which filesystem utilities/privileges are required.
- [ ] **Linux BlockInput movement-only mode.** With the `keysharp-input` mouse hook active,
  confirm on a physical mouse that `MouseMove` suppresses motion while buttons and the wheel still
  pass through, then confirm that `MouseMoveOff` restores motion. Exercising an exclusive evdev grab
  and its synchronous hook decisions is intentionally not automated.

### macOS

Most of the points here already have working fallbacks — the point is confirming they behave
on real hardware and under real permission grants, not that they are missing.

- [ ] **Window activation fallback.** `Keysharp.Core/Internals/Window/MacOS/MacNativeWindows.cs:368`
  Fallback path for when `NSRunningApplication` activation is unavailable or denied — verify under
  restricted Accessibility/Automation permissions.

- [ ] **Char-mapper ASCII fallback.** `Keysharp.Core/Internals/Input/MacOS/MacCharMapperProvider.cs:505`
  Confirm non-ASCII layouts translate correctly and the ASCII source is only used
  as a fallback.

- [ ] **Hook snapshot fallback for key state.** `Keysharp.Core/Internals/Input/Hooks/MacOS/MacHookThread.cs:123`
  Falls back to hook snapshots when the native key-state query is unavailable — verify accuracy.

- [ ] **WinEvent caret events.** `WinEvent.OnCaretMove` registers `AXSelectedTextChanged` on each
  application element (only while a WinEvent with an OnCaretMove slot runs). Verify that events arrive while typing,
  that the reported handle is the window containing the edited element (`AXWindow`, falling back to
  the app's focused window), and that the `A_EventInfo` rectangle agrees with `CaretGetPos`.

- [ ] **BlockInput event-tap modes.** With InputControl granted (and InputMonitoring for movement-only mode), verify `On`/`Off`,
  send-time blocking and movement-only blocking. In particular, `MouseMove` must suppress physical
  movement without suppressing physical buttons or wheel events, and synthetic input must pass.

- [ ] **EnvUpdate launchd propagation.** Set and delete uniquely named variables with `EnvSet`, call
  `EnvUpdate`, and verify the environment inherited by a subsequently launched launchd service.
  Existing processes and persistent shell configuration must remain unchanged.

- [ ] **New side-effecting backends.** On disposable/test state, change and restore an external
  volume label (`diskutil renameVolume`, behind `DriveSetLabel`), then separately verify normal
  logoff/restart/shutdown requests. These are not safe to exercise in the automated suite.
