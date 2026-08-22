# Keysharp demos

Example scripts that showcase Keysharp APIs. These are **not** part of the Keysharp library
and are **not** bundled into installs — they live outside the `Keysharp/` project so nothing
here is picked up by the build/publish or the installer.

Some demos `#include` shared files (`Shell.ks` here, or the bundled `../Keysharp/Scripts/OCR.ks`)
with relative paths, so run them from a checkout of this repo. They aim to be genuinely useful and
cross-platform (Windows / Linux X11 + Wayland / macOS); where a capability is platform-limited, the
script's header comment says so.

| Demo | What it does | Keysharp features it shows |
|------|--------------|----------------------------|
| [OCRSnip.ks](OCRSnip.ks) | Region-select OCR snip: drag a box, OCR the text to the clipboard, box each recognized word (hover to see its text). Ctrl+Shift+drag to snip in one motion (or Ctrl+Shift+O to arm hands-free) · Esc/Right-click cancel · Ctrl+Shift+Backspace clear · Ctrl+Alt+Shift+Q exit. | `Overlay` selection UI + crosshair, `Image` + `OCR.ks`, `GetKeyState` click-drag polling, LButton blocking, hover tooltips |
| [WindowTiler.ks](WindowTiler.ks) | Snap the active window to halves / quarters / thirds / a 3×3 grid on its current monitor. CapsLock + a `Q W E / A S D / Z X C` grid (mirrors screen position) · CapsLock+R maximize/restore · CapsLock+1–4 switch region mode (Halves+Quarters / Grid 3×3 / Columns / Rows) · Ctrl+Alt+Shift+Q exit. | window actions (`WinMove`/`WinRestore`/`WinMaximize`/`WinActive`), multi-monitor `MonitorGetWorkArea`, dynamic `Hotkey()`, `Highlight` + `ToolTip` feedback |
| [WindowGrab.ks](WindowGrab.ks) | Grab any window from any point without activating it: Super+Left-drag moves it, Super+Right-drag fades it (left = clearer, right = opaque), Super+Right-click toggles 50% / opaque. | `MouseGetPos` window-under-cursor, `WinMove` (no activate), `WinGetTransparent`/`WinSetTransparent`, physical `GetKeyState` drag polling |
| [ClipboardHistory.ks](ClipboardHistory.ks) | Remembers text you copy; Ctrl+Alt+V opens a keyboard-driven **overlay** picker — 1-9 paste instantly, Up/Down + Enter pick, Esc cancels — pasting back into the window you were in. | `OnClipboardChange`, `A_Clipboard`, an `Image`-rendered `Overlay` list, `HotIf`-scoped hotkeys, `Send` paste (Cmd+V on macOS) |
| [InputHUD.ks](InputHUD.ks) | On-screen keyboard + mouse "keycaster": keys and buttons light up as you physically press them; the layout adapts per-OS; Ctrl+drag a HUD to move it, Ctrl+Right-drag to zoom; Ctrl+Alt+Shift+0 resets position & zoom; Ctrl+Alt+Shift+Q quits. | one `InputHook` capturing keys **and** mouse (non-suppressing), `Overlay` canvases drawn with `Image` + live `Update`, `HotIf`-scoped drag |

[Shell.ks](Shell.ks) is the small **shared support layer** the demos build on (not a standalone demo):
each demo `#include`s it for a persistent, click-to-dismiss cheat-sheet overlay of its shortcuts, a
customized tray icon/menu, and per-demo settings persistence (`demos.ini` in Keysharp's data folder).

Hotkeys are defined near the top of each script and are easy to rebind if they clash with your
desktop's shortcuts. Each demo prompts on first launch for the OS permissions listed in its header,
quits with Ctrl+Alt+Shift+Q, and shows a bottom-right cheat-sheet you can reopen with Ctrl+Alt+Shift+S
or from the tray icon's menu (which also carries demo-specific actions). Preferences — a card's
"Don't show on startup" tick, WindowTiler's region mode, InputHUD's HUD positions and zoom — persist to
`%AppData%\Keysharp\demos.ini` — `~/.config/Keysharp/demos.ini` on Linux and
`~/Library/Application Support/Keysharp/demos.ini` on macOS — so each demo comes back the way you left it.
