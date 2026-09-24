# Keysharp Detailed Reference

This document contains detailed platform, implementation, and AutoHotkey v2 compatibility notes. For the concise project introduction and quick-start instructions, see the [main README](../README.md).

Jump directly to:

- [Windows platform support](#windows-platform-support)
- [Windows setup](#installing-on-windows)
- [Linux platform support](#linux-platform-support)
- [Linux setup](#installing-on-linux)
- [macOS platform support](#macos-platform-support)
- [macOS setup](#installing-on-macos)
- [Cross-platform capability matrix](#cross-platform-capability-matrix)
- [AutoHotkey v2 differences](#differences)
- [Code acknowledgements](#code-acknowledgements)

## Prerequisites
* If the .NET 10 runtime is not installed on your machine, download it from the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

## Windows Platform Support

Windows has the best feature implementation rate and very high AutoHotkey v2 compatibility. The largest differences are:
* Object destruction logic, which happens non-deterministically due to C# garbage collection
* GUI rendering, because WinForms is used as the backend. WinForms uses lazy initialization which means for example that GUIs render blank before they've been showed. Additionally WinForms has some differences concerning Z-ordering of controls and label rendering.

### Installing on Windows
* Download and run the Keysharp installer from the [Releases](https://github.com/keysharp-org/Keysharp/releases) page.
	+ The install path can be optionally added to the $PATH varible, so you can run it from the command line from anywhere.
		+ The path entry will be removed upon uninstall.
	+ It also registers Keysharp.exe as the default program to open `.ks` and `.cks` files. So after installing, double click any `.ks` source script or `.cks` compiled script to run it.
	+ On Windows, the installer adds a right-click "Compile" action for `.ahk` and `.ks` source scripts, which writes a `.cks` compiled script next to the source file.

### Portable run on Windows
* Download and unzip the zip file from the [Releases](https://github.com/keysharp-org/Keysharp/releases) page.
	+ CD to the unzipped folder.
	+ Run `.\Keysharp.exe yourfilename.ahk`

### Building from source on Windows
* Download the latest version of [Visual Studio 2026](https://visualstudio.microsoft.com/vs/community/) or VS Code.
	+ This should install the .NET 10 SDK. If it doesn't, you need to install it manually from the link above.
* Open Keysharp.sln
* Build all (building the installer is not necessary).
* CD to bin\release\net10.0-windows (or \debug\, depending whether using Debug or Release mode)
* Run `.\Keysharp.exe yourtestfile.ahk`

To build the release MSI installer and portable ZIP, run `Keysharp.Install\package-windows.ps1` from PowerShell. Packaging output is written to `dist\`. It is the only Windows packager: add `-Msix` to also build the Microsoft Store / sideload `.msix` (a second, self-contained publish plus the Windows SDK's `makeappx`/`makepri`, so it is off by default), and `-SkipMsi` to produce just the ZIP.

The MSI is built by `Keysharp.Install\windows\wix\Keysharp.Installer.wixproj` ([WiX v5](https://wixtoolset.org/)), whose toolset restores from NuGet — the .NET SDK is the only prerequisite. The project is deliberately not a member of `Keysharp.sln`, because it packages a staged directory that does not exist until the script has published and staged; build it through the script, or by hand with `-p:PayloadDir=<staged app folder>`.

| Switch | Effect |
|---|---|
| `-RuntimeIdentifier win-x64` \| `win-arm64` | Target architecture; defaults to the build machine's. Both produce an MSI and a ZIP. |
| `-SkipPublish` | Re-stage and repackage the existing publish output. |
| `-SkipMsi` | Produce only the portable ZIP. |

The MSI is per-machine and installs to `%ProgramFiles%\Keysharp`, so it requires administrator rights — WiX v5 has no supported way to build a package that can install either way. Without admin rights, use the portable ZIP and run `Keysharp.exe --install`, which registers the file associations, the context-menu verbs and the PATH entry for the current user only. It writes to `HKCU` and needs no elevation; `Keysharp.exe --uninstall` reverses it. Add `machine` to either command to force the machine-wide variant, which does require elevation.

PATH and shell integration are separate MSI features. The Customize page lets you deselect either, and an unattended install can do the same, for example `msiexec /i keysharp.msi /qn ADDLOCAL=Core` to install neither.

## Linux Platform Support
Linux support is in active development. The following table summarises what works and what requires user action.

| Platform / compositor | Without system components | Standalone system components add / enable | Notes |
|---|---|---|---|
| **X11** | Core runtime and script-owned GUI; global desktop features require the corresponding component | Full input hooks/synthesis, `BlockInput`, and reliable hotkeys/hotstrings via `keysharp-input`; authorized screen capture and window/clipboard grants via `keysharp-desktop` | X11 cannot technically enforce capture authorization, so the desktop broker is an awareness gate there |
| **Wayland – GNOME** | Core runtime; permission-scoped input and desktop operations are unavailable | `keysharp-desktop` supplies the Shell provider for window automation, Taskbar integration with the stock overview dash, and a per-user broker for authorized capture; `keysharp-input` supplies reliable input | Enabling a newly installed Shell extension may require logging out and back in |
| **Wayland – Cinnamon** | Core runtime; permission-scoped input and desktop operations are unavailable | `keysharp-desktop` supplies the Shell provider for push window events, window actions, mouse synthesis, and Taskbar badge/progress integration with the grouped-window-list, plus the brokered capture path; `keysharp-input` supplies reliable keyboard input | A Cinnamon restart or logout may be needed after first provider installation |
| **Wayland – KWin / KDE Plasma** | Core runtime; permission-scoped input and desktop operations are unavailable | Window queries/control and authorized screen capture via the `keysharp-desktop` KWin provider; native Plasma Taskbar badge/progress integration; reliable input via `keysharp-input` | Window access follows the broker's permission, revocation, and audit path; `keysharp-desktop` also owns the KWin-restricted desktop entry |
| **Wayland – COSMIC** | Core runtime; permission-scoped input and desktop operations are unavailable | `keysharp-desktop` supplies authorized compositor capture with a whole-desktop portal fallback, plus window listing, active state, geometry, focus, close, minimize, maximize and restore; `keysharp-input` supplies reliable input | The broker implementation is covered by fake compositor and portal tests but remains unverified on a real COSMIC session; coordinate hit-testing is limited to the active window because no stacking order is exposed |
| **Wayland – other compositors**<br>Sway, Hyprland, Wayfire, labwc, etc. | Core runtime; permission-scoped input and desktop operations are unavailable | `keysharp-desktop` supplies whichever capture, window, clipboard, pointer and cursor operations the compositor advertises; `keysharp-input` supplies reliable input | wlroots protocols provide capture and basic window control; Hyprland also provides authenticated cursor query and absolute motion |

### Installing on Linux

Keysharp runs on its own. Two standalone system components, [`keysharp-input`](https://github.com/keysharp-org/keysharp-input) and [`keysharp-desktop`](https://github.com/keysharp-org/keysharp-desktop), add the privileged input and desktop features listed in the table above; see [System components](#system-components).

On NixOS, use the flake rather than the steps below — see [Keysharp on NixOS and COSMIC](linux-nixos.md). On Arch-based systems Keysharp is also available as an [AUR package](https://aur.archlinux.org/packages/keysharp-git).

#### All three projects at once

`keysharp-linux-setup.sh` on the [Releases](https://github.com/keysharp-org/Keysharp/releases) page resolves and installs all three system-wide. Installation requires root; the diagnosis and dry-run modes do not. Download it, then run it:

```sh
sudo sh ./keysharp-linux-setup.sh
```

It carries no payload. Each project is downloaded from its own latest release, verified against that release's `SHA256SUMS`, and installed by that project's own installer. On a dpkg host it fetches the three `.deb` files and installs them in one apt transaction, so Keysharp's recommendations resolve; elsewhere it installs the three tarballs, components first. A component already installed at a compatible client ABI is skipped, so a rerun installs only what is missing.

`--dry-run` reports the plan without downloading. `--skip-input` and `--skip-desktop` leave a component out. `--keysharp-version`, `--input-version` and `--desktop-version` pin a version instead of taking the latest; there is no cross-project version lock, because the client ABI is what decides compatibility.

Keysharp recommends the virtual packages `keysharp-input-client-abi-0` and `keysharp-desktop-client-abi-0`. Any installed alternative that provides the same client ABI is left in place. Product versions select release artifacts; the client ABI decides compatibility. Keysharp still runs when either component is absent, but its corresponding privileged features are unavailable.

The `.deb` recommends WebKitGTK (`libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37`) for `Gui.WebView`, so a normal `apt install` brings the version available for the distribution. The tarball and `keysharp-linux-setup.sh` do not install recommendations; install the available package separately if a script uses `WebView`.

#### Keysharp on its own

The Keysharp `.deb` and the Keysharp tarball each install Keysharp and nothing else. From the tarball:

```sh
sudo bash ./install.sh
```

The installer:

* Installs the Linux runtime dependencies, and the .NET 10 runtime if your distribution provides it. If it does not, install it manually using [these instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux).
* Registers Keysharp as the default program for `.ks` and `.cks` files, so double-clicking a script runs it.
* Creates a `/usr/local/bin/keysharp` symlink so you can run Keysharp from anywhere.
* Installs nothing but Keysharp. Use `keysharp-linux-setup.sh` to add the components, or each component's own installer.

Run `install.sh` without `sudo` to install under `$HOME/.local` instead. Keysharp still uses any system component already present.

#### Verifying the installation

```sh
keysharp-input probe
keysharp-desktop probe
```

Each command reports service readiness and available operations. On GNOME and Cinnamon, enable the installed Shell extension after installation; this may require logging out and back in.

#### Uninstalling

For the `.deb`, run `sudo apt remove keysharp`. This does not remove either component in the same transaction. Components apt installed as a recommendation are marked automatic, so a later `sudo apt autoremove` can remove one only when Keysharp and every other package have stopped recommending or depending on its ABI. Components installed manually keep their manual state. Permission grants remain under `/var/lib/keysharp-permissions/v1`.

The tarball's uninstaller removes only Keysharp. It always leaves both components installed, because other applications may use them, and it never deletes permission grants. Remove a component separately only after establishing that nothing else needs it:

```sh
sudo /usr/local/share/doc/keysharp-input/uninstall.sh
sudo /usr/local/share/doc/keysharp-desktop/uninstall.sh
```

#### Installation channels

The `.deb` application under `/usr` and the root tarball application under `/usr/local` are alternative system-wide channels, not layers. A fresh installation refuses to cross channels, so uninstall the current system-wide application before switching. This does not apply to running the extracted application in place, or to a non-root installation under `$HOME/.local`.

The same rule applies to the components: a component `.deb` refuses to unpack over a tarball-installed component under `/usr/local`, whose unit files would otherwise shadow the package's. Run that component's portable uninstaller first.

Until an apt repository is published, `keysharp-linux-setup.sh` is the way to install the `.deb` channel: it fetches each project's verified package directly, rather than adding an unsigned package source.

#### System components

`keysharp-input` provides global input hooks, synthesis, `BlockInput`, idle and device-state queries, and reliable hotkeys and hotstrings. `keysharp-desktop` provides authorized capture, window and clipboard integration, including KWin, GNOME, and Cinnamon providers.

On X11 and Wayland, foreign-window queries/control, capture, global cursor queries, and brokered clipboard access go through those component libraries. X11 display topology, keyboard layout, modifier state, and lock state also come from keysharp-desktop. Wayland foreign windows receive positive process-local synthetic handles, which remain stable while the window is observed and work with `ahk_id` like native handles. Keysharp keeps script-owned GUI and local GUI events in GTK/Eto. Linux supports enabling and disabling those script-owned windows, but neither X11 nor Wayland has a faithful equivalent of Win32 `EnableWindow` for another application's window, so `WinSetEnabled()` raises `OSError` for a foreign target. KWin Wayland can raise and lower foreign windows, but does not expose foreign title mutation or a way to ask the client to redraw. Overlay presentation prefers a layer-shell surface when available, then a GNOME/Cinnamon shell actor for click-through overlays, and otherwise an Eto window. Desktop operations use the same permission scopes on both session types; X11 authorization is an application consent boundary because the X server still accepts direct calls from other same-session applications.

[Linux native integration](linux-native-integration.md) describes the API split, permission identities, and platform limits.

Both are independently versioned MIT-licensed projects with their own installation and removal lifecycle. Applications call their stable C libraries, `libkeysharp-input.so.0` and `libkeysharp-desktop.so.0`; the libraries hide the private service protocol. Both helpers pin the same `keysharp-permissions` source submodule, so the grant-store contract has one implementation without adding a third runtime package.

System installations authenticate the connecting process and share permanent grants keyed by user, executable identity, and capability scope. A new scope opens a polkit prompt; a denial or cancelled prompt is not stored. A user installation of keysharp-desktop uses an Allow Always/Deny dialog and a separate private grant store; it cannot grant access to the system input service. Cursor position, display topology, keyboard layout, idle time, modifier state, and lock-toggle state are ungated. A script or DLL run by an interpreter shares that interpreter executable’s identity; scripts are not separately authenticated. Protected installed executable paths retain grants across upgrades, while changed user-writable executables require renewed authorization. Grants remain until revoked:

```sh
keysharp-input permissions list
keysharp-input permissions revoke --hash <hash> input-monitoring
keysharp-desktop permissions list
keysharp-desktop permissions revoke --hash <hash> screen-capture
```

The capability names match those used by `#Requires capability` and `RequestCapabilities`. The two components share the same versioned grant store, so a scope granted through one is visible to the other when applicable.

#### VS Code extension

For thqby's **AutoHotkey v2 Language Support** VS Code extension, create an `AutoHotkey.exe` compatibility symlink because the extension requires that filename:
```sh
mkdir -p ~/.local/bin
ln -sf "$(command -v keysharp)" ~/.local/bin/AutoHotkey.exe
```
Then use `/home/YOUR_USERNAME/.local/bin/AutoHotkey.exe` as the interpreter path in the extension. The extension is designed for AutoHotkey on Windows, so static language features and running scripts are the most compatible features; Windows-specific debugging, help, and compiler integration will not work.

### Building from source on Linux
* Install the .NET 10 SDK (not just the runtime) as described in "Installing on Linux"
* In the same parent folder as keysharp, clone the Keysharp branch of [the Keysharp fork of Eto](https://github.com/keysharp-org/Eto/tree/Keysharp); if keysharp is at `foo/keysharp`, clone Eto to `foo/Eto` by running `git clone -b Keysharp https://github.com/keysharp-org/Eto.git` from within `foo`.
* Run `Keysharp.Install/package-linux.sh`. It packages Keysharp alone; the two components are built and released from their own repositories.
* The unpacked tree is placed in `dist/staging/linux-x64/keysharp-linux-x64`, and the installable tarball is `dist/keysharp-linux-x64.tar.gz`. If `dpkg-deb` is installed, a Debian package such as `dist/keysharp_<version>_amd64.deb` is also created.
* The staged folder and tarball are portable, so both source repositories can be safely deleted after packaging.
* **Alternatively**, on arch-based systems keysharp is provided as an [AUR package](https://aur.archlinux.org/packages/keysharp-git)

## macOS Platform Support
macOS support is in active development. The following table summarises what works and what requires user action.

| Feature | Status | Notes |
|---|---|---|
| Script execution | Working | Parser, compiler, and runtime are functional |
| Hotkeys / Hotstrings | Working | The suppression-capable hook requires **Input Monitoring** and **Accessibility** on first use. #/Win maps to the Command key, !/Alt maps to the Option key |
| Keyboard & mouse send | Working | Requires **Accessibility** permission on first use |
| Global keyboard/mouse hooks | Working | Requires **Input Monitoring** and **Accessibility** because the hook can suppress events |
| Cursor confinement | Partial | `ClipCursor` suppresses out-of-bounds movement; requires **Input Monitoring** and **Accessibility** permissions |
| GUI windows | Working | Eto.Forms backend; some controls differ from Windows |
| Screen capture / pixel functions | Working | Requires **Screen Recording** permission on first use |
| Monitor brightness / DDC-CI | Partial | Built-in panel (and Apple's own displays) via DisplayServices; other external monitors over DDC/CI. No permission needed. Apple Silicon only — the Intel path is implemented but untested, and some USB-C hubs and docks do not carry the DDC channel. `Monitor.GetVCP()`/`SetVCP()` work on external monitors only, as a built-in panel has no DDC/CI connection |
| Window management | Partial | Accessibility API; foreign-app control requires permission |
| Registry APIs | Not supported | Windows-only |
| COM APIs | Unverified | `ComObject` and friends are backed by Apple Events, not COM. Implemented but not yet exercised on macOS hardware. Requires **Automation** permission per controlled application. See [ComObject off Windows](#comobject-off-windows) |

Permissions are requested automatically when first needed, or up front with `#Requires capability` (see [Additions and Improvements](#additions-and-improvements) below). Grant them in **System Settings → Privacy & Security**.

### Installing on macOS

macOS 15 or later is required. Separate `osx-arm64` assets for Apple Silicon and `osx-x64` assets for Intel Macs are available on the [Releases](https://github.com/keysharp-org/Keysharp/releases) page.

#### DMG — user install, no administrator password required

The DMG contains `Keysharp.app`, `Keyview.app`, `Install.command`, and `Uninstall.command`.

Double-click **Install.command** (it runs in Terminal) to:
1. Copy `Keysharp.app` and `Keyview.app` to `/Applications`.
2. Optionally install the `keysharp` and `keyview` terminal commands to `/usr/local/bin` (requests an administrator password).
3. Optionally install the VS Code AutoHotkey v2 extension compatibility shim at `~/.local/bin/AutoHotkey.exe`.

Alternatively, drag both apps to the **Applications** folder shortcut inside the DMG, or to any folder of your choice (e.g. `~/Applications/`).

**First-launch Gatekeeper workaround** — because the app is not notarized, macOS will block it on the first open. Right-click (or Control-click) `Keysharp.app` → **Open**, then click **Open** in the prompt. Do the same for `Keyview.app`. After that one-time step the apps open normally.

Alternatively, in Terminal:
```sh
xattr -dr com.apple.quarantine /Applications/Keysharp.app
xattr -dr com.apple.quarantine /Applications/Keyview.app
```

The equivalent manual setup for the terminal commands is:
```sh
sudo ln -sf /Applications/Keysharp.app/Contents/MacOS/Keysharp /usr/local/bin/keysharp
sudo ln -sf /Applications/Keyview.app/Contents/MacOS/Keyview /usr/local/bin/keyview
```

Without terminal commands, use `Keyview.app` to write and run scripts. Keyview finds the sibling `Keysharp` binary automatically, whether the apps live in `/Applications/`, `~/Applications/`, or directly on a mounted DMG volume.

For thqby's **AutoHotkey v2 Language Support** VS Code extension, answer "Yes" to the compatibility shim prompt in `Install.command`. It creates `~/.local/bin/AutoHotkey.exe`; then use `/Users/YOUR_USERNAME/.local/bin/AutoHotkey.exe` as the interpreter path in the extension.

The extension is designed for AutoHotkey on Windows, so static language features and running scripts are the most compatible features; Windows-specific debugging, help, and compiler integration will not work.

#### PKG — system install, requires administrator password

The `.pkg` installer places both apps in `/Applications/`. After copying the apps, it shows two prompts (as the logged-in user):
- Whether to install the `keysharp` and `keyview` terminal commands in `/usr/local/bin`.
- Whether to install the VS Code AutoHotkey v2 extension compatibility shim at `~/.local/bin/AutoHotkey.exe`.

Install from Finder by double-clicking the `.pkg` and following the installer prompts (you will be asked for your administrator password), or from Terminal:
```sh
sudo installer -pkg Keysharp-osx-<architecture>.pkg -target /
```

Apply the same first-launch Gatekeeper workaround as above for each app after installation.

#### macOS permissions

On first use, macOS will ask for several permissions:

| Permission | Required for |
|---|---|
| **Input Monitoring** | Hotkeys, hotstrings, and reading keyboard/mouse input |
| **Accessibility** | `WindowMonitoring`, `WindowControl`, input synthesis, and suppression-capable global hooks |
| **Screen Recording** | `ScreenCapture`; also `WindowMonitoring`, because macOS omits foreign window titles from the current window-inventory API without this grant |

Grant each permission in **System Settings → Privacy & Security** when prompted. Keysharp will wait up to 60 seconds for each permission to be granted before continuing, but usually the script will have to be restarted after granting capabilities. You can also request permissions explicitly at the top of a script:
```ahk
#Requires capability InputMonitoring, ScreenCapture
```

#### Uninstalling

Both the DMG and the PKG bundle an uninstaller that removes the app(s), terminal commands, the package receipt (PKG installs), and stored settings/cache data — no manual `rm` commands needed.

**DMG install** — open the mounted DMG and double-click **Uninstall.command** (it runs in Terminal). Eject the DMG and empty the Trash afterwards if you also dragged the apps there yourself — macOS Launch Services can still launch apps sitting in the Trash until it's emptied.

**PKG install** — run the bundled uninstaller from a terminal:
```sh
sudo keysharp-uninstall
```

If you removed the apps by hand instead and `.ks`/`.ahk` files still open in Keysharp, the apps are most likely still sitting in the Trash — empty it, since Launch Services can launch apps from there even though Spotlight does not index it.

macOS may retain granted permissions (Accessibility, Input Monitoring, Screen Recording) even after the app is removed. To revoke them, open **System Settings → Privacy & Security**, select each category, and remove any Keysharp or Keyview entries — the uninstaller cannot do this for you.

If you reinstall a different build (e.g. switching between a locally-built, ad-hoc-signed, and notarized version) and permissions seem stuck — toggles that won't stay on, or the app not appearing/disappearing from a permission list — the old TCC grant may be tied to the previous code signature. Reset *every* permission category for Keysharp/Keyview with `tccutil`:
```sh
tccutil reset All org.keysharp.keysharp
tccutil reset All org.keysharp.keyview
```
`All` clears every TCC entry for that bundle ID (Accessibility, Input Monitoring, Screen Recording, and any others macOS may have recorded), for all versions of the app sharing that bundle ID. macOS will prompt again next time each permission is needed.

### Building from source on macOS

* Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
* In the same parent folder as `keysharp`, clone the Keysharp branch of [the Keysharp fork of Eto](https://github.com/keysharp-org/Eto/tree/Keysharp). If `keysharp` is at `foo/keysharp`, clone Eto to `foo/Eto`:
  ```sh
  git clone -b Keysharp https://github.com/keysharp-org/Eto.git
  ```
* Run the packaging script to produce a release DMG and PKG:
  ```sh
  bash ./Keysharp.Install/package-macos.sh
  ```
  The script selects `osx-arm64` or `osx-x64` from the host architecture. Set `RID` explicitly to cross-publish, for example `RID=osx-x64 bash ./Keysharp.Install/package-macos.sh`. Output is written to `dist/`:
  - `Keysharp-<rid>.dmg` — drag-and-drop user install
  - `Keysharp-<rid>.pkg` — system install with terminal commands
* For a quick debug run without packaging, build and run directly:
  ```sh
  dotnet build Keyview/Keyview.csproj -c Debug
  open bin/Debug/net10.0/<rid>/Keyview.app
  ```
* The signing and notarization steps are skipped by default (no developer account required). To enable ad-hoc signing for local testing: `ADHOC_SIGN=true bash ./Keysharp.Install/package-macos.sh`.

### GUI menus and editing shortcuts

Unlike Windows, macOS drives the standard text-editing shortcuts — Copy (⌘C), Cut (⌘X), Paste (⌘V), Select All (⌘A), Undo/Redo (⌘Z / ⇧⌘Z) — through the application's **Edit menu** rather than the text control itself. A window with no menu therefore has none of those shortcuts in its text fields.

To keep scripts working unchanged, Keysharp automatically gives each GUI (and its dialogs) a minimal macOS menu bar — an App menu (with Quit) and the standard Edit menu — so these shortcuts work out of the box. The File, Window, and View menus that macOS apps usually add are omitted, since they aren't useful for most script GUIs. A GUI that defines its own menu via `Gui.MenuBar` still gets the Edit menu merged in, positioned ahead of the script's own menus, and the merge is preserved when the script changes its menu at runtime.

On every platform, a GUI's client area begins below its menu bar. `Gui.Show` width and height and `Gui.GetClientPos` describe that area, and control `Add`/`Move`/`GetPos` coordinates, `ContextMenu` and `DropFiles` event coordinates and `CoordMode "Client"` share its origin; `Gui.GetPos` and `Gui.Move` describe the outer window.

Use the **`-AppMenu`** GUI option to opt out for a given window — for example a borderless or kiosk-style GUI that should contribute no menu bar:

```ahk
MyGui := Gui("-AppMenu")   ; no App/Edit menus; editing shortcuts will not work
```

`+AppMenu` (the default) restores it. The option has no effect on Windows (which has no application-level menu) and on Linux (whose toolkit handles editing shortcuts without one).

## Cross-Platform Capability Matrix

This is a concise view of which AutoHotkey 2.1 features Keysharp implements. For full details and current notes, see [capabilities.md](capabilities.md).

<!-- CAPABILITIES_OVERVIEW:START -->
Status legend:
- 🟢 Full: Implemented and generally usable
- 🟡 Partial: Implemented with known limitations or gaps
- 🟠 Planned: Not implemented yet, but intended
- 🔴 Unsupported: Not supported
- ⚪ Unknown: Not yet verified
- `Partial*` on non-Windows `Control*()` functions means script-owned Keysharp controls are supported, but controls in foreign applications are not.

| Capability | Windows | Linux (X11) | Linux (Wayland) | macOS | Notes |
|---|---|---|---|---|---|
| Parser and runtime execution | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Script execution is provided by Keysharp.Core. Source parsing is an optional Roslyn-free component; lowering and C# compilation are supplied by the optional compiler component. |
| Directives and preprocessing | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | OS-specific directives supported via compile constants. |
| File and directory operations | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | macOS recycle/trash and privacy-scoped file access still evolving. |
| Keyboard/Mouse send (synthetic input) | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Requires platform permissions on macOS. |
| Global keyboard hooks | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Linux uses evdev/uinput, macOS uses CGEventTap. |
| Global mouse hooks | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Suppression/injection semantics differ by platform. |
| Hotkeys/Hotstrings | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Linux and macOS use a suppression-capable global hook, which requires InputMonitoring and InputControl. |
| Script-owned window management | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Creating and driving the script's own GUI windows. Built on WinForms (Windows) and Eto (Linux/macOS); the object model, events, controls, menus, ListView and TreeView all behave the same. Remaining differences: the ActiveX and Custom control types are Win32-only, ListView supports only the Report view off Windows, raw Win32 style options are ignored, the WebView control renders with whichever browser engine the platform provides, per-monitor DPI re-layout is Windows-only, and a client cannot position its own window on Wayland without a compositor backend. |
| Foreign window management (non-Keysharp apps) | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | On Linux, Control* functions are not supported for foreign apps; use the included AtSpi library for cross-process control interaction. COSMIC supplies native listing, active state, geometry, polling-based events, focus, close, minimize, maximize and restore. wlroots compositors can supply listing, active/state facts and the same actions except geometry. Coordinate hit-testing remains limited without stacking order, and general move/resize is unavailable. macOS currently relies on Accessibility APIs with permission requirements. |
| Tray icon and menu | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Tray icon, its menu and TrayTip notifications. On Linux the tray depends on the desktop providing a StatusNotifier/AppIndicator host - some environments need an extension before an icon appears at all - and notifications go through the desktop notification service. macOS uses a status item in the menu bar. |
| Screen capture and pixel/image functions | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Pixel/image search and screen capture depend on platform-specific backends. On Linux, keysharp-desktop provides authorized X11, Wayland compositor, and portal capture. |
| Clipboard | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Text, image, URI, custom MIME, wait, and change-notification operations use the native platform clipboard backends. See ClipboardAll() for the Wayland multi-format restore limitation. |
| Sound APIs | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Audio device/endpoint support differs by platform. These are the AutoHotkey-compatible functions; the Keysharp-only Ks.Audio class adds polyphonic playback, exact device selection, per-application sessions and recording alongside them. |
| Registry APIs | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Windows Registry APIs are Windows-only. |
| COM APIs | 🟢 Full | 🟡 Partial | 🟡 Partial | ⚪ Unknown | Real COM on Windows; the same late-bound surface is backed by D-Bus on Linux and by Apple Events on macOS, so target strings and member names differ per platform. The macOS backend is implemented but not yet verified on hardware. Functions that need vtables, reference counts or raw pointers throw off Windows. |
<!-- CAPABILITIES_OVERVIEW:END -->

## Overview

Keysharp is a fork and improvement of the abandoned IronAHK project, which itself was a C# re-write of the C++ AutoHotkey project.

Keysharp runs on Windows, Linux, and macOS. Windows currently has the broadest compatibility, while Linux and macOS support continue to improve.

This project is in the alpha testing stage and is not yet recommended for production systems.

Some general notes about Keysharp's implementation of the [AutoHotkey v2 specification](https://www.autohotkey.com/docs/v2/):

* The operation of Keysharp is different than AutoHotkey. While AutoHotkey is an interpreted scripting language, Keysharp actually creates a compiled .NET executable and runs it.

* The process for reading and running a script is:
	+ Keysharp.exe discovers the optional scripting components below `components/scripting`.
	+ The Roslyn-free parser component parses the script and generates a Document Object Model (DOM) tree.
	+ The compiler component lowers the DOM to C# and compiles it with Roslyn.
	+ The C# program code is compiled into an in-memory executable.
	+ The executable is ran in memory as a new process.
	+ Optionally output the generated C# code to a .cs file for debugging purposes with the `--transpile` option, without running the script.
	+ Optionally output the generated executable to an .exe file for running standalone in the future with the `--compile exe` option, without running the script.

* Keysharp supports `.ahk` and `.ks` source files and `.cks` compiled scripts. Installers associate supported files with Keysharp and provide an editing action through Keyview where supported.

* Keyview is the graphical script editor included with Keysharp. It shows generated C# and validation feedback while editing, and supports opening, saving, running, and compiling source files.
	+ It gives real-time feedback so you can see immediately when you have a syntax error.
	+ It is recommended that you use this to write code.
	+ The features are very primitive at the moment, and help improving it would be greatly appreciated.

Despite our best efforts to remain compatible with the AutoHotkey v2 spec, there are differences. Some of these differences are a reduction in functionality, and others are an increase. There are also slight syntax changes.

## ComObject off Windows

`ComObject` keeps its name on every platform, the way `DllCall` does, because the late-bound automation model carries over even though the machinery underneath does not. On Linux it drives **D-Bus**; on macOS it drives **Apple Events**. The concepts line up closely enough to share one surface:

| COM (Windows) | D-Bus (Linux) | Apple Events (macOS) |
|---|---|---|
| type library | introspection XML | sdef scripting dictionary |
| object | service plus object path | application plus object specifier |
| method call | method call | command event |
| property get/put | `Properties.Get`/`Set` | get/set events |
| collections | child object nodes | elements (`every`, by index, name or id) |
| running object table | bus name ownership | running application |
| `CoCreateInstance` | service activation | launching the application |
| connection point events | signals | distributed notifications |
| `VARIANT` | signature strings | four-character descriptor types |

The syntax is the same on every platform; the targets are not. The target string and every member name differ per platform and per application, so a script that runs on more than one branches.

```ahk
; Linux — a D-Bus service; "system:" selects the system bus
nm := ComObject("system:org.freedesktop.NetworkManager")

; macOS — an application, by bundle id, name, path, or "pid:1234"
finder := ComObject("com.apple.Finder")
MsgBox finder.Windows[1].Name
```

Common to both backends:

* `ComObject()` starts the service or application if it is not running; `ComObjActive()` and `ComObjGet()` attach only, and throw otherwise. Neither platform has monikers, so `ComObjGet` is an alias of `ComObjActive`.
* `ComObjQuery()` narrows which face of the object is used: a D-Bus interface on Linux, a scripting suite on macOS. Passing it up front as `ComObject`'s second argument does the same thing.
* `ComValue()` uses a D-Bus signature on Linux or a four-character descriptor on macOS, while also accepting compatible `VT_` constants. `VarType` preserves either notation and is assignable. `ComObjType()` accepts only a `ComObject`.
* `ComCall()`, `ObjAddRef()`, `ObjRelease()`, `ComObjValue()`, `ComObjFlags()`, `ComObjFromPtr()` and `ComObjArray()` throw an error stating the reason. They depend on vtables, reference counts or raw interface pointers, none of which exist on either backend.

### macOS specifics

An Apple Events object is a **query, not a handle**: `window 1 of application "Finder"` is resolved by the target application each time it is used, so a stale object fails when it is used rather than when it is created. Narrowing a collection therefore costs nothing until the value is read.

Parameters follow the Apple Events model, which is the reverse of the D-Bus one: the first unnamed argument is the *direct parameter*, and every other value must be **named**.

```ahk
finder := ComObject("com.apple.Finder")
finder.Windows[1].Close(Saving: "no")        ; the receiver is the direct parameter
doc := finder.Make(New: "document")           ; named parameters carry the rest
for w in finder.Windows                       ; one round trip, then iterate
    MsgBox w.Name
MsgBox finder.Windows.Count
```

Member names come from the application's scripting dictionary, whose terms contain spaces. Keysharp folds spaces and underscores away and ignores case, so a term such as `file name` is reachable as `FileName`, `filename` or `file_name`. When a name is defined by two suites with different events, the error lists them and `ComObjQuery()` picks one.

Controlling another application needs **Automation** permission, granted per target application in **System Settings → Privacy & Security → Automation**. Keysharp asks the system for it before the first event so the prompt is attributed properly; a refusal reports what to grant. This is separate from the Accessibility permission the window functions use.

`ComObjConnect()` maps to distributed notifications. These carry less than D-Bus signals or COM connection points do: no scripting definition describes them, they are not scoped to a particular object, and many applications publish none. A notification whose name begins with the target's bundle id calls the matching handler, so `com.example.App.stateChanged` calls `Prefix_stateChanged(name, payload, comObj)`. The naming convention is not a rule — Music still posts under `com.apple.iTunes` — so an application that changed its bundle id publishes names this does not match.

## Differences

### Behaviors and Functionality
* Linux support is partial. See [Linux Platform Support](#linux-platform-support) above for a detailed breakdown by display server and compositor.
	+ Control commands only work on windows created by the running Keysharp process. This is because "controls" don't exist in Linux the same way they do in Windows.
		+ As an alternative it's recommended to use [AtSpi.ks](https://github.com/keysharp-org/Keysharp/blob/master/Keysharp/Scripts/AtSpi.ks): running it directly displays AtSpiViewer which can be used to inspect windows, and it also contains methods to manipulate windows and controls similarly to Acc/UIA in Windows.
	+ GUI support is mostly implemented, but some controls are missing or incomplete.
	+ Registry functions are not supported. The COM functions are, but they address D-Bus rather than COM — see [ComObject off Windows](#comobject-off-windows).
* Keysharp follows the .NET memory model.
	+ There is no variable caching with strings vs numbers. All variables are C# objects.
	+ Values not stored in variables are like regular variables, only eligible to be freed once they go out of scope.
		```
		FileOpen("test.txt", "w").Write("hello") ; The temporary file object does not get deleted at the end of the line, only possibly at the end of the current scope.
		```
	+ Object destructors/finalizers run at an unpredictable point after the object becomes unreachable. `Collect()` does not wait for finalizers, so free a resource that must be released promptly with an explicit method.
	+ Object destructors (`__Delete()`) are implemented with C# finalizers, which are quite heavy-weight and are not automatically present for all objects. The finalizer state is determined at object creation based on whether `__Delete()` is present in the prototype chain, or at the point `__Delete()` is defined. If `__Delete()` is defined later in the prototype chain then instance finalizers are not automatically activated; the activation can be forced manually by temporarily reassigning a different base for the instance.
	+ On script exit all non-local variables are enumerated, finalizers disabled, and `__Delete()` called if present. This also includes class static variables.
* AutoHotkey says about the inc/dec ++/-- operators on empty variables: "Due to backward compatibility, the operators ++ and -- treat blank variables as zero, but only when they are alone on a line".
	+ Keysharp breaks this and will instead create a variable, initialize it to zero, then increment it.
	+ For example, a file with nothing but the line `x++` in it, will end with a variable named x which has the value of 1.
* Keysharp supports fixed [operator overloads](https://keysharp-org.github.io/KeysharpDocs/Objects.htm#Custom_Classes_operators) on classes and structs, including their class objects (`static +(Right)`). Unary and binary forms coexist; equality pairs must be declared together in the same scope. `?()` customizes truth testing, and `++()`/`--()` customize increment/decrement. Native numeric operations retain direct dispatch.
* The concat-assign operator `.=` is not optimized to modify the left operand inplace, meaning calling it in a loop will be very slow. If many concats are required then use a `StringBuffer` instead.
* Function objects behave mostly the same as in AutoHotkey.
	+ The underlying function object class is named `KeysharpFunc`, instead of `Func`, because C# already contains a built in class named `Func`.
		+ Scripts only ever use the AutoHotkey name: `MsgBox is Func` works, `MsgBox is KeysharpFunc` does not.
	+ A callback parameter takes a function reference, `SetTimer(MyFunc)`. A string raises `TypeError`, as in AutoHotkey v2, so resolve a name known only at run time first: `SetTimer(%"MyFunc"%)`.
		+ A `Gui` built with an event sink, `Gui(Options, Title, EventObj)`, takes the name of one of that sink's methods in `OnEvent`/`OnNotify`/`OnCommand`/`OnMessage`, as in AutoHotkey. That is the one place a callback is named; elsewhere `ObjBindMethod(obj, "Method")` binds a method by name.
		+ `Hotkey()`'s `Action` takes an alt-tab action or another hotkey's name, and `Hotstring()`'s `Replacement` takes replacement text.
	+ A callback is a function or any object with a `Call` method, kept as given, so `A_HotIf`, `A_Timers` and callback properties return that object. Keysharp also calls an object that has only `__Call`, through it, which AutoHotkey does not.
	+ Registering a callback validates it as AutoHotkey does: one that cannot take the site's arguments (`OnExit`/`OnError` 2, `OnClipboardChange` 1, `OnMessage` 4, `SetTimer` 0, `Hotkey`/`HotIf` 1, a `Menu` item 3, each GUI event its own count) raises a `ValueError`, and an uncallable object a `MethodError`. An object is judged by the `MinParams`, `MaxParams` and `IsVariadic` properties it defines, and one defining `MinParams`, or `MaxParams` where that is read, is taken to be callable. `Hotstring`, `Sort` and GUI event-sink method names are not checked. Registering a callback again with `OnExit`, `OnError`, `OnClipboardChange`, `OnMessage`, a GUI event or an `Overlay` event keeps it in place (`OnMessage` applies the new `MaxThreads`), while each `Clipboard.OnChange`-style call makes a separate hook. Each closure is a distinct callback, even one from the same fat arrow as another.
	+ Most built-in functions can also be used as function objects.
* The `File` object is internally named `KeysharpFile` so that it doesn't conflict with `System.IO.File`. As with `Func` and `Object`, only the AutoHotkey name is usable from a script; the internal name appears solely in low-level diagnostics such as stack traces.
* `Reload` compiles a replacement before asking the old instance to exit (`WM_CLOSE` on Windows, `SIGTERM` on Linux/macOS); `OnExit` may veto. Windows headless scripts exit first.
* Linux/macOS `SIGTERM` runs `OnExit` with `Close` (`Reload` during a pending reload); a second signal during exit forces termination. On Windows, title-bar close hides the main window; external `WM_CLOSE` exits.
* `A_Args` is a built-in variable which every module and function shares and a script may assign any value, unset included. In AutoHotkey it is a variable of the main module only, so another module has its own, and assigning it in a function without a global declaration creates a local variable.
* Error stack traces start from where the error was thrown, not where it was constructed.
* `throw` and `Throw()` pass any value unchanged to `catch`, including an instance of a class which does not extend `Error`. A bare `catch` matches only `Error` values; `catch Any` matches every value. An uncaught non-`Error` value is still wrapped as `Error(Value)` for `OnError` and the default diagnostic, while AutoHotkey passes the original value to `OnError`. An object's own `Message` value property supplies the wrapper's message; inherited properties and getters are ignored.
* An exception which is not a Keysharp error, such as one thrown by `#CSharp` code, reaches `OnError` when it ends the thread, as an `Error` describing it, or its inner exception when it wraps one.
* `Map` internally uses a real hashmap, which means item access, insertions and removals are faster, which is especially true for larger datasets. To keep at least partial compatibility with AutoHotkey the `Map` object is copied and sorted before enumeration, which means modifying the `Map` during enumeration will not have the same effect as in AutoHotkey.
* `AddStandard()` detects menu items by string, instead of ID, because WinForms doesn't expose the ID.
* `CallbackCreate()` does not support the `CDecl/C` option because the program will be run in 64-bit mode.
	+ Passing string pointers to `DllCall()` when passing a created callback is recommended against. See explanation above under `StrPtr()`.
	+ Usage of the created callback will be inefficient, so usage of `CallbackCreate()` is discouraged.
	+ `CallbackFree()` raises `ValueError` for an invalid or already-freed address; AutoHotkey leaves most invalid addresses undefined.
* `ControlMove()` and `ControlSetPos()` operate relative to their immediate parent, which may not be the main window if they are contained in a nested control.
* `DirCopy()` extracts archives with .NET rather than the OS shell, so the supported formats are the same on every platform: `.zip`, `.tar`, `.tar.gz` and `.tgz` are extracted into *Dest* as a folder. AutoHotkey's format list instead depends on the Windows version (and RAR/7z are not supported at all here).
	+ A plain `.gz` holds a single compressed file rather than an archive of entries, so *Dest* names the decompressed **file** and its parent folder is created if needed. This is the one case where *Dest* is not a directory.
* `DllCall()` has the following caveats:
	+ Pass `&Variable` for a writable `Str`, `WStr` or `AStr` argument when the native function can shorten its value. The copied-back string ends at the first null terminator. Passing a plain `Str` value lets native code write directly into the .NET string, whose old length remains and whose contents may be shared with a literal.
	+ Use `Ptr` and `StringBuffer` for double pointer parameters such as `LPTSTR*`. This is recommended over the use of `StrPtr()`.
	+ A call may pass at most 63 arguments.
	+ A `Float` value read back out of a call — a `Float` return value or a `Float*` output variable — widens to the shortest Float that round-trips, so `1.2345` stays `1.2345`. `NumGet` widens the same way, where AutoHotkey carries the binary error of the narrower type into the decimal digits (`1.2344999313354492`).
* Encoding names — wherever one is accepted: `FileEncoding`, `A_FileEncoding`, `FileRead`, `FileOpen`, `File.Encoding`, `StrGet`, `StrPut`, `Base64.Encode`, `Url.Encode`, `Url.Decode` and the `Crypt` class — take AutoHotkey's `UTF-8`, `UTF-8-RAW`, `UTF-16`, `UTF-16-RAW`, `CPnnn` and `nnn`, and additionally `ASCII` and any name .NET knows, such as `windows-1252`. A name which cannot be resolved raises a `ValueError`; it is never quietly substituted, since that would silently read or write the wrong bytes. An empty name means the native UTF-16 encoding, where AutoHotkey uses the active ANSI code page (CP0).
* `NumGet()` and `NumPut()` match a type name in full, where AutoHotkey looks only at its first character (after an optional leading `U`).
	+ AutoHotkey therefore reads `"Str"` as a 2-byte short, `"Int16"` as **eight** bytes (it searches the whole name for a `6`), and accepts abbreviations such as `"i"`, `"D"` and `"Integer"`. Keysharp raises a `ValueError` for all of these, so a type that does not name a number cannot silently read or write the wrong bytes.
	+ The accepted names are `Int`, `UInt`, `Int64`, `UInt64`, `Short`, `UShort`, `Char`, `UChar`, `Float`, `Double`, `Ptr` and `UPtr`.
	+ A value that does not read as a number raises a `ValueError` rather than being written as a zero. The pointer-width types additionally accept an object carrying a `Ptr`.
* `ObjPtr()` returns an IUnknown `ComValue` with the pointer wrapped in it, whereas `ObjPtrAddRef()` returns a raw pointer.
* `Sleep()` works, but uses `Application.DoEvents()` internally which is not a good programming practice and can lead to hard to solve bugs.
	+ For this reason, it's recommended that users use timers for repeated execution rather than a loop with calls to `Sleep()`.
	+ It will not do any sleeping if shutdown has been initiated.
* `StrPtr()` works slightly differently because C# strings are constant.
	+ `StrPtr(variable)` returns a custom `StringBuffer` object which is entangled with the original string. When this object is used with DllCall, NumPut etc, then the `StringBuffer` is used as the pointer, and the entangled string is updated after the function call.
	+ `StrPtr("literal")` with a literal string will pin the string from garbage collection and return the actual address of the string. This string must not be modified, and should be freed after use with `ObjFree()`.
	+ Instead of `StrPtr` it is recommended to use a `StringBuffer` instance instead.
* `TrayTip()` functions slightly differently.
	+ Muting the sound played by the tip is not supported with the `Mute` option. The sound will be whatever the user has configured in their system settings.
	+ The option `4` to use the program's tray icon is not supported. It is always shown in the title of the tip.
	+ The option `32` to use the large version of the program's tray icon is not supported. Windows will always show the small version.
* Pointers returned by `StrPtr()` must be freed by passing the value to a new function named `ObjFree()`.
	+ `StrPtr()` does not return the address of the string, instead it returns the address of a copy of the bytes of the string.
* Deleting a tab via `GuiCtrl.Delete()` does not reassociate the controls that it contains with the next tab. Instead, they are all deleted.
* The size and positioning of some GUI components will be slightly different than AutoHotkey because WinForms uses different defaults.
	+ There is an additional positioning option `xc` and `yc` which position the control relative to the container. For example inside a tab `xc+10` would position the control 10 pixels from the left side of the tab control.
	+ GroupBoxes can be used as containers by calling `GuiObj.UseGroup(GroupBox)`, and to exit the group call `GuiObj.UseGroup()`.
* The class name for statusbar/statusstrip objects created by Keysharp is "WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1". However, for accessing a statusbar created by another, non .NET program, the class name is still "msctls_statusbar321".
* Menu items, whether shown or not, have no impact on threading.
* Using the class name with `ClassNN` on .NET controls gives long, version specific names such as "WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1" for a statusbar/statusstrip.
	+ This is because simpler class names can't be specified in code the way they can in AutoHotkey with calls to `CreatWindowEx()`.
	+ These long names may change from machine to machine, and may change for the same GUI if you edit its code.
	+ There is an new `NetClassNN` property alongside `ClassNN`.
	+ The class names of all GUI controls created in Keysharp are prefixed with the string "Keysharp", eg: `KeysharpButton`, `KeysharpEdit` etc...
	+ `NetClassNN` will give values like 'KeysharpButton6' (note that the final digit is the same for the `ClassNN` and the `NetClassNN`).
	+ Due to the added simplicity, `NetClassNN` is preferred over `ClassNN` for WinForms controls created with Keysharp.
	+ This is used internally in the index operator for the Gui class, where if a control with a matching `ClassNN` is not found, then controls are searched for their `NetClassNN` values.
* If a `ComObject` with `VarType` of `VT_DISPATCH` and a null pointer value is assigned a non-null pointer value, its type does not change. The `Ptr` member remains available.
* `A_LineNumber` is not a reliable indicator of the line number because the preprocessor condenses the code before parsing and compiling it.
* The Optimization section of the `#HotIf` documentation doesn't apply to Keysharp because it uses compiled code, thus the expressions are never re-evaluated.
* The standalone `#ErrorStdOut` directive sends subsequent load-time errors and uncaught runtime errors to standard error instead of a dialog. The `--errorstdout` command-line switch applies to load-time errors from the start of loading. A GUI-subsystem process's error stream is visible when captured by piping or redirection. For example:
	+ `.\Keysharp.exe .\test.ahk | more`
	+ `.\Keysharp.exe .\test.ahk | more > out.txt`
* `#Warn Experimental` emits a compiler warning once per resolved experimental class import: `Image`, `Audio`, `Monitor`, `Clr` or `Overlay`. Off by default; `#Warn All` enables it. Dynamic object access is not checked. These APIs may change or be removed without deprecation.
* The `#App { ConsoleApp: true }` key is Keysharp-only, and is the equivalent of Ahk2Exe's `;@Ahk2Exe-ConsoleApp`. It makes `--compile exe` produce a console application rather than the default GUI one, which is what a command-line script needs:
	+ A shell waits for the program to exit and reports its exit code, and its standard streams are the terminal's, so `FileAppend(text, "*")` prints and `FileOpen("*", "r")` reads typed input without any redirection.
	+ On Windows this is the executable's PE subsystem field, which the shell reads before the process starts. It belongs to the generated host shape in `#App` because runtime code cannot substitute for it.
	+ Without it the executable stays a GUI one, so a double-clicked script never flashes a console window. That is also the trade-off: a console-subsystem executable launched from Explorer gets a console window of its own.
	+ It is ignored when the script is interpreted or compiled to a `.cks`, since neither writes an executable, and it is inert on Linux and macOS, where executables have no subsystem and a shell always waits.
* `FileInstall(Source, Dest, Overwrite?)` extracts a file embedded via `#App { Files: [...] }` (matching its canonical script-relative path) to `Dest`. When no matching payload is embedded — including source/in-memory execution — `Source` is resolved relative to the script and copied with `FileCopy` semantics; copying a file onto itself is a no-op. Both branches require `Dest`'s parent directory to exist, and an existing `Dest` is replaced only when `Overwrite` is true. Unlike Ahk2Exe, Keysharp does not scan `FileInstall` calls to decide what to embed: the final `Files` list is the single authority.
* If a script is compiled then none of Keysharp or AutoHotkey command parameters apply.

### Syntax
* AutoHotkey `unset` is implemented as `null`. `IsSet(x)` is equivalent to `x == null`.
* Use of the dereference syntax `%expression%` inside functions is highly discouraged. This is because using it will cause every function call to construct an object which captures all local variables, and depending on the number of variables the performance loss may be significant.
* `Goto` statements cannot use any type of variable. They must be labels known at compile time and function just like goto statements in C#.
* `Goto` statements being called as a function like `Goto("Label")` are not supported. Instead, just use `goto Label`.
* The `#Requires` directive differs in the following ways:
	+ In addition to supporting `AutoHotkey`, it also supports `Keysharp`.
	+ Sub versions such as -alpha and -beta are not supported. Only the four numerical values values contained in the assembly version in the form of `0.0.0.0` are supported.
	+ A new `capability` form requests one or more platform permissions together at script startup, before hotkeys are registered. A platform can still show one authorization dialog per independent system service:
		```
		#Requires capability InputMonitoring, ScreenCapture
		```
		The eight canonical capability names are case-insensitive. Other spellings and aliases are rejected:
		| Name | Description |
		|---|---|
		| `InputMonitoring` | Observe keyboard/mouse input or poll arbitrary key/button state. Modifier state, lock-toggle state, and cursor position are ungated. |
		| `InputControl` | Synthesize or suppress keyboard/mouse input, including `BlockInput`. A suppressing hook needs both InputMonitoring and InputControl. |
		| `WindowMonitoring` | Query, enumerate, identify, or subscribe to events from foreign windows and controls. |
		| `WindowControl` | Activate or mutate foreign windows and controls. Target resolution can also require WindowMonitoring. |
		| `ScreenCapture` | Capture screen pixels (`PixelGetColor`, `ImageSearch`, `Image`). |
		| `AudioCapture` | Permission for audio capture, required by `Audio.Recorder.Start()` before it opens a device. |
		| `CameraCapture` | Reserve permission for camera capture. No current capture API consumes it. |
		| `ClipboardMonitoring` | Read or listen to the clipboard. Clipboard writes remain ungated. |
* For any `__Enum()` class method, it should have a parameter value of 2 when returning `Array` or `Map`, since their enumerators have two fields.
* RegEx uses PCRE2 engine powered by the PCRE.NET library. There are a few limitations compared to the AutoHotkey implementation:
	+ The following options are different:
		+ `S`: Studies the pattern to try improve its performance.
			+ This is not supported. All RegEx objects are internally created with the `PcreOptions.Compiled` option specified, so performance should be reasonable.
		+ `u`: This new option disables optimizations PCRE2_NO_AUTO_POSSESS, PCRE2_NO_START_OPTIMIZE, and PCRE2_NO_DOTSTAR_ANCHOR. This option can be useful when using callouts, since these optimizations might prevent some callouts from happening.
	+ Callouts differ in a few ways:
		+ A callout names a top-level function, or a nested function or closure held in a variable of the function calling `RegExMatch` or `RegExReplace`.
		+ A named callout must be enclosed in `""`, `''`, or `{}`.

### Additions and Improvements
* Modified/extended functions:
	+ `ComObjConnect()` takes an optional third parameter as a boolean (default: `false`) which specifies whether to write additional information to the debug output tab when events are received.
	+ `DateAdd()` and `DateDiff()` support taking a value of `"L"` for the `TimeUnits` parameter to add miLliseconds or return the elapsed time in milliseconds, respectively.
		+ See the new accessors `A_NowMs`/`A_NowUTCMs`.
	+ `Exit(ExitCode?)` exits the current pseudo-thread, as in AHK.
		+ Terminating a *different* pseudo-thread is `threadObj.Exit(ExitCode?)`, reached via `A_Thread.Underlying` or `A_RealThread.Threads[i]`.
		+ Targeting an underlying pseudo-thread marks it to exit when it next resumes and reaches a cooperative event/message check (`TryDoEvents`). It does not asynchronously abort managed code.
		+ A later request made before the target exits replaces its pending exit code.
	+ `FileGetSize()` supports `G` and `T` for gigabytes and terabytes.
	+ `ImageSearch()` takes an options string as a fifth parameter, rather than inserted in the string before the `ImageFile` parameter.
	+ `Log(Number, Base := 10)` is by default base 10, but it can accept a double as the second parameter to specify a custom base.
		+ In `SetTimer()`:
			+ In the callback function, `A_EventInfo` is set to the function object used to create the timer.
			+ This allows the handler to alter the timer by passing the function object back to another call to `SetTimer()`.
			+ Timers are not disabled when the program menu is shown.
	+ `Run/RunWait()` can take an extra string for the argument instead of appending it to the program name string. However, the original functionality still works too.
		+ The new signature is: `Run/RunWait(Target [, WorkingDir, Options, &OutputVarPID, Args])`.
	+ `SubStr()` uses a default of 1 for the second parameter, `StartingPos`, to relieve the caller of always having to specify it.
* New miscellaneous functions:
	+ `Collect()`: Calls `GC.Collect()` to force a memory collection.
		+ This rarely ever has to be used in properly written code.
		+ Calling `Collect()` may not always have an immediate effect. For example if an object is assigned to a variable inside a function and then the variable is assigned an empty string then calling `Collect()` after it will not cause the object destructor to be called. Only after the function has returned will the object be considered to have no references and `Collect()` starts working.
		+ If an object destructor needs to be called immediately then it may better to call `Object.__Delete()` manually.
	+ `EnvUpdate()`: Retained from AutoHotkey v1 as a cross-platform environment notification mechanism. Windows broadcasts `WM_SETTINGCHANGE`; Linux publishes pending `EnvSet()` changes to the D-Bus activation environment and systemd user manager; macOS publishes them to the current launchd session. Linux and macOS updates affect future session-managed processes only and are not persistent.
	+ `FormatCs()`: An alternative to `Format()`. The syntax used in `FormatCs()` is exactly that of `string.Format()` in C#, except with 1-based indexing.
		+ Full documentation for the C# formatting rules can be found [here](https://learn.microsoft.com/en-us/dotnet/api/system.string.format).
	+ `Mail(Recipients, Subject, Message, Options)`: Sends an email through an SMTP server. Returns once the server has accepted the message.
		+ `Recipients`: One address as a string, or several as an `Array` of strings. An empty `Array`, an empty address, or an entry which is not a string raises.
		+ `Subject`: Subject of the message.
		+ `Message`: Message body.
		+ `Options`: A `Map`. `host` and `from` are required, because SMTP has no default for either. An unrecognized key raises rather than becoming a header. `host`, `from` and `replyto` each also accept a one-element `Array`, so a script assembling options can use one shape throughout.
			+ "host": Required. The server as "hostname" or "hostname:port". Port 25 if none is given; a bracketed IPv6 literal keeps its own colons.
			+ "from": Required. The sender's address.
			+ "cc": A further recipient, or an `Array` of them.
			+ "bcc": A blind-copy recipient, or an `Array` of them. These reach the server but do not appear in the message headers.
			+ "replyto": The address replies should go to, when it differs from `from`.
			+ "attachments": A file path, or an `Array` of them. A path which cannot be read raises.
			+ "headers": A `Map` of additional SMTP header names and values.
		+ Raises `TypeError` for `Recipients` or `Options` of the wrong type, `ValueError` for a missing or misshapen option or an address or port which cannot be parsed, and `OSError` when an attachment cannot be read or the server rejects the message or cannot be reached.
		+ The connection is unauthenticated and unencrypted, so this reaches a relay which accepts the script's machine rather than a provider requiring a login.
	+ `RandomSeed(Integer)`: Reinitializes the random number generator for the current thread with a specified numerical seed.
	+ `RequestCapabilities(capabilities*) => Object`: Requests one or more platform permissions and returns an object describing the outcome.
		+ `capabilities`: zero or more capability name strings, each optionally comma- or space-delimited. Recognised names are the same as for `#Requires capability` above.
		+ When called with no arguments, returns the current status of all capabilities without prompting.
		+ Returns an `Object` with a property for each capability (`"Granted"`, `"Denied"`, `"NotApplicable"`, or `"Unsupported"`) and an `IsGranted` property (`1`/`0`) indicating whether every *requested* capability was granted or not applicable.
		+ On Linux, `InputMonitoring` belongs to `keysharp-input`, while the desktop scopes belong to `keysharp-desktop`. With system installations, both helpers can manage the shared `InputControl` grant: it is batched with `InputMonitoring` when both are requested, or with desktop scopes when requested alongside them. A request spanning both helpers may require one polkit authentication per helper. Successful grants persist until manually revoked.
			```
			caps := RequestCapabilities("InputMonitoring", "ScreenCapture")
			if caps.IsGranted
				MsgBox "All permissions granted"
			MsgBox caps.ScreenCapture   ; "Granted", "Denied", "NotApplicable", or "Unsupported"

			; Query current status without prompting:
			caps := RequestCapabilities()
			```
		+ Prefer `#Requires capability` for scripts that need permissions from startup. Use `RequestCapabilities` directly when you need to check or request permissions at a specific point in script execution, or when you want to inspect the current status.
	+ `IsComponentAvailable(Capability)`: Returns true when the fixed first-party `"parser"` or `"compiler"` deployment unit is installed or embedded, compatible, and loadable. The aliases `"parsing"` and `"compilation"` are accepted. This check loads the requested unit, so checking `"compiler"` can load Roslyn. Unknown names raise a `ValueError`.
	+ `ValidateScript(Code)` and `CompileScript(Code)`: Check a script without running it, as the `--validate-syntax` and `--validate` switches do. `Code` is a script file path when a file by that name exists, and script source otherwise. `ValidateScript` only parses, with the parser component, so it is fast and loads no Roslyn; `CompileScript` compiles in memory with the compiler component, which also catches lowering and C# errors. Both return `{IsValid, Errors, Warnings}`, where `Errors` and `Warnings` are Arrays of messages (from `ValidateScript`, each prefixed with its file, line and column), and both raise an `Error` when their component is not installed.
	+ `RunScript(Code, Async := false, Callback?, Name := "*", Executable?, Options?)`: Dynamically compiles and runs source text or a script file, or runs an already-compiled `.cks`/`.dll`. `Async` controls whether the call returns immediately. `Callback`, when present, accepts the returned `ScriptProcess` and is queued on the scheduler of the script thread which called `RunScript`; the pending callback keeps that script alive. `Options` is a command-line string or an Array of individual arguments. Source compilation requires the compiler component in the calling process.
		+ Returns a `ScriptProcess` encapsulating the child and all three redirected streams. `HasExited` reports its state. `ExitCode` and `ExitTime` (YYYYMMDDHH24MISS) return `""` until it exits. `StdOut`, `StdErr` and `StdIn` are `File` objects; standard input remains available to the child because an internal transport carries a dynamically compiled assembly. `Kill()` terminates the child. `Close()` closes the streams, cancels a pending exit callback and releases the process resources without terminating the child.
		+ A synchronous call drains standard output and error concurrently while it waits, so a child cannot deadlock by filling either pipe. For an asynchronous call, read both streams while a verbose child is running.
* New `Clipboard` class (available from the `KS` module) covering everything the clipboard holds, not just text. `A_Clipboard`, `ClipboardAll()`, `ClipWait()` and `OnClipboardChange()` are unchanged and remain the AutoHotkey-compatible surface.
	+ There is one clipboard per session, so the class has no instances — every member is used directly, and `Clipboard()` raises an error.
	+ **Every getter returns `""` when the clipboard does not hold that content**, so `if (files := Clipboard.Files)` is the idiom.
	+ Typed content — the portable surface:
		+ `Clipboard.Text` (get/set): identical to `A_Clipboard`. Setting `""` clears the clipboard.
		+ `Clipboard.Image` (get/set): gets an `Image`; the setter accepts anything `Image(source)` does — an `Image`, a file path, a bitmap handle, or `"HBITMAP:n"`. `Image.FromClipboard()` is an alias of the getter.
		+ `Clipboard.Files` (get/set): an `Array` of paths. The setter publishes them with copy semantics.
		+ `Clipboard.Html` (get/set): the HTML **fragment**. On Windows the `CF_HTML` header is added on write and stripped on read, so what a script sets is what other applications paste.
		+ `Clipboard.Rtf` (get/set): RTF source.
	+ State:
		+ `Clipboard.IsEmpty`, `Clipboard.Clear()`.
		+ `Clipboard.Formats => Array`: every advertised format, under the names **this** platform uses (`"HTML Format"`, `"FileDrop"` on Windows; `"text/html"`, `"text/uri-list"` elsewhere). Deliberately not normalized — a script reading it is platform-specific by construction.
		+ `Clipboard.Has(Kind) => Boolean`: `Kind` is `"Text"`, `"Image"`, `"Files"`, `"Html"`, `"Rtf"`, or a platform-native format name.
	+ Raw access — the escape hatch, as non-portable as the format names themselves:
		+ `Clipboard.GetData(Format) => Buffer`: one format's bytes exactly as the platform stores them. This is how private/application formats (Excel's `Biff12`, Visual Studio's `MSDEVColumnSelect`) are read.
		+ `Clipboard.Set(Bag)`: publishes several formats in **one** transaction, so they coexist and the change fires once — `Clipboard.Set({ Text: "Hello", Html: "<b>Hello</b>" })`. Keys are kind names or native format names; values are a String, Buffer, Image, or Array of paths. A `Map` is accepted as well as an object, because an object-literal key must be an identifier and a name like `"HTML Format"` can only be spelled as a Map key.
	+ Save and restore: `Clipboard.All` (get/set) is the `ClipboardAll()` / `A_Clipboard := saved` pair spelled so the round trip is visible:
		```
		saved := Clipboard.All
		Clipboard.Text := "temporary"
		Clipboard.All := saved
		```
	+ Waiting and events:
		+ `Clipboard.Wait(Timeout?, WaitFor?)`: as `ClipWait`, and additionally accepts a kind name — `"Text"`, `"Any"`, `"Image"`, `"Files"`, `"Html"` or `"Rtf"`. `ClipWait` accepts those too.
		+ `Clipboard.OnChange(Callback) => ClipboardHook`: calls `Callback(Hook, DataType)` on every change, where `DataType` is 0 (empty), 1 (text or files) or 2 (other), as for `OnClipboardChange`. The returned hook is a started `EventHook` that keeps the script running, and `Clipboard.Hooks` lists the running ones. Prefer it for a closure, since unregistering from `OnClipboardChange` needs the same function object back. The hook is independent of the `OnClipboardChange` handler chain: its return value is discarded, and its callback runs on its own pseudo-thread.
	+ Platform notes: on a Wayland session driven through a shell extension (Cinnamon/Muffin) the compositor's selection source can advertise only one type, so `Clipboard.Set` and `ClipboardAll` restore a single, most-useful representation rather than every format.
* New debugging functions:
	+ `ShowDebug()`: Shows the main window and focuses the debug output tab.
	+ `OutputDebugLine()`: The same as `OutputDebug()` but appends a linebreak at the end of the string.
* New `Crypt` class, holding hashing, key derivation, symmetric encryption and cryptographically secure random values (`#Import "Ks" { Crypt }`):
	+ A String or `StringBuffer` is taken as its **UTF-8** bytes, so a digest is the one any other tool prints for the same text. Pass an `Encoding` — the names `A_FileEncoding` takes — to use a different one, and note that a name which cannot be resolved raises rather than falling back. A `Buffer` is used as it stands; an open `File` is accepted by anything that hashes, but not by `Crypt.Encrypt`.
	+ A digest is returned as uppercase hexadecimal; compare digests case-insensitively, since the tool a checksum came from may print it in lowercase.
	+ `Crypt.Hash(Value, Algorithm := "SHA256", Encoding := "UTF-8") => String`: hashes with `MD5`, `SHA1`, `SHA256`, `SHA384`, `SHA512` or `CRC32`, spelled with or without the `-`. An open `File` is read as a stream and left at the position it was on.
	+ `Crypt.HashFile(Path, Algorithm := "SHA256") => String`: the same over a file, read as a stream so that its size does not matter.
	+ `Crypt.CRC32(Value, Encoding := "UTF-8") => Integer`: Calculates the CRC32 polynomial of an object. `Crypt.Hash(Value, "CRC32")` returns the same checksum as hexadecimal.
	+ `Crypt.Encrypt(Value, Key, Algorithm := "AES", Mode := "CBC", IV?, Encoding := "UTF-8") => Buffer` and `Crypt.Decrypt(...)` with the same parameters: symmetric encryption. `AES` is the only cipher so far and `Mode` is `CBC`, `ECB` or `CFB`; the cipher is a parameter rather than part of the method name so that another one is a value this accepts, not a new method.
		+ With *IV* omitted, each call draws a random 16-byte initialization vector and writes it in front of the ciphertext, where `Crypt.Decrypt` reads it back. Encrypting the same text twice therefore gives different results, which is the point: a fixed vector lets anyone holding the output see which encrypted values are equal. Supply *IV* only to match a format defined elsewhere — it is then used as it stands and is **not** written to the result, so `Crypt.Decrypt` needs the same one back.
		+ `Mode := "GCM"` **authenticates** as well as encrypts: an altered message is detected on decryption and raises, where a chaining mode would decrypt it to rubbish without complaint. Its nonce is 12 bytes rather than 16, and its authentication tag is appended to the result. Prefer it unless a format defined elsewhere dictates otherwise.
		+ `Mode := "CFB"` is CFB8, the feedback size .NET and Windows CNG both default to — not the CFB128 that OpenSSL's plain `-aes-256-cfb` means.
	+ `Crypt.RandomBytes(Count) => Buffer` returns cryptographically secure bytes for a vector, a salt or a key.
	+ `Crypt.PBKDF2(Password, Salt, Iterations := 600000, Length := 32, Algorithm := "SHA256", Encoding := "UTF-8") => Buffer` stretches a password into key material, which is what makes a passphrase usable as a key: `Crypt.Encrypt` otherwise takes the key exactly as it is given, zero-padded to the cipher's key size. *Algorithm* is `SHA1`, `SHA256`, `SHA384` or `SHA512` — .NET rejects `MD5` for derivation on every platform, so it is not offered. The salt need not be secret but must differ per password, and must be stored alongside whatever the key protects.
	+ `Crypt.SecureRandom(Min, Max) => Double`: Generates a secure cryptographic random number.
		+ Returns an `Integer` if neither argument is a `Double`. The range includes *Max*, as `Random`'s does.
	+ Data encrypted by the earlier `AES()` function does **not** decrypt with `Crypt.Decrypt` as it stands. That function derived its vector from the key instead of storing one, which is what made it deterministic, and that derivation has been removed. The vector it used was the first 16 bytes of SHA-1 over the 32-byte zero-padded key — with the key taken as UTF-16, since that was the old default encoding — so old data can still be read by rebuilding that vector and passing it as *IV* along with `Encoding := "UTF-16"`. The key padding, CBC mode and PKCS7 padding are otherwise unchanged.
* New file functions:
	+ `FileFullPath(Filename) => String`: Returns the full path to `Filename`.
	+ `FileCreateTemp() => String`: Creates an empty temporary file and return its full path.
* New math functions:
	+ `Sinh(Number) => Double`
	+ `Cosh(Number) => Double`
	+ `Tanh(Number) => Double`
* New RegEx functions:
	+ `RegExMatchCs()` and `RegExReplaceCs()` which use the C# style regular expression syntax rather than PCRE2.
		+ `OutputVar` in `RegExMatchCs()` will be of type `RegExMatchInfo`, the same class `RegExMatch()` produces, and a miss stores an empty value exactly as `RegExMatch()` does.
		+ PCRE exceptions are not thrown when there is an error, instead C# regex exceptions are thrown.
		+ To learn more about C# regular expressions, see [here](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions).
		+ The following options are different from PCRE:
			+ `-A`: Forces the pattern to be anchored; that is, it can match only at the start of Haystack. Under most conditions, this is equivalent to explicitly anchoring the pattern by means such as `^`.
				+ -This is not supported, instead just use `^` or `\A` in your regex string.
			+ `-C`: Enables the auto-callout mode.
				+ -This is not supported. C# regular expressions don't support calling an event handler for each match. You must manually iterate through the matches yourself.
			+ `-D`: Forces dollar-sign ($) to match at the very end of Haystack, even if Haystack's last item is a newline. Without this option, $ instead matches right before the final newline (if there is one). Note: This option is ignored when the `m` option is present.
				+ -This is not supported, instead just use `$`. However, this will only match `\n`, not `\r\n`. To match the `CR/LF` character combination, include `\r?$` in the regular expression pattern.
			+ `-J`: Allows duplicate named subpatterns.
				+ -This is not supported.
			+ `-S`: Studies the pattern to try improve its performance.
				+ -This is not supported. All RegEx objects are internally created with the `RegexOptions.Compiled` option specified, so performance should be reasonable.
			+ `-U`: Ungreedy.
				+ -This is not supported, instead use `?` after: `*, ?, +, and {min,max}`.
			+ `-X`: Enables PCRE features that are incompatible with Perl.
				+ -This is not supported because it's Perl specific.
			+ ``` `a `n `r ```: Causes specific characters to be recognized as newlines.
				+ -This is not supported.
			+ `\K` is not supported, instead, try using `(?<=abc)`.
* New string functions:
	+ `ReplaceLineEndings(Str, EndOfLine?) => String`: Makes all line endings in a string (CR LF, CR, LF, form feed, NEL, U+2028 and U+2029) match `EndOfLine`, which defaults to `` `n `` on every platform.
* Window functions:
	+ `WinFromPoint(X, Y)`: Gets the window at a specific screen position.
	+ `WinMinimizeAllUndo()`: Unminimizes top-level windows without clearing maximization; respects `DetectHiddenWindows`. Unlike AHK's shell undo, needs no preceding `WinMinimizeAll()` call.
* Keysharp-only members on AutoHotkey classes:
	+ They need no `#Import`, but AutoHotkey v2.1 does not define them, so a script using one is not portable to AutoHotkey.
	+ `Array`:
		+ All comparisons compare the actual underlying values, so `"1" != 1`.
			+ This differs from the comparison rules in conditional statements, but makes more sense when searching arrays.
		+ On an empty array, `Filter` and `Map` return an empty array and `FindIndex` and `IndexOf` return 0, whatever `StartIndex` is. On any other array, a `StartIndex` of 0 or whose absolute value exceeds the length throws.
		+ `Contains(Value) => Boolean`: Returns `true` if `Value` is contained in the array, else `false`.
		+ `Filter(Callback: (value [, index]) => Boolean, StartIndex := 1) => Array`: Applies a filter to each element of the array and returns a new array consisting of all elements for which `Callback` returned `true`.
		+ `FindIndex(Callback: (value [, index]) => Boolean, StartIndex := 1) => Integer`: Returns the index of the first element for which `Callback` returned `true`, starting at `StartIndex`. Returns 0 if `Callback` never returned `true`.
			+ If `StartIndex` is negative, the search starts from the end of the array and moves toward the beginning.
		+ `IndexOf(Value, StartIndex := 1) => Integer`: Returns the index of the first item in the array which equals value, starting at `StartIndex`. Returns 0 if value is not found.
			+ If `StartIndex` is negative, the search starts from the end of the array and moves toward the beginning.
		+ `Join(Separator := ',') => String`: Joins together the string representation of all array elements, separated by `Separator`.
		+ `Map(Callback: (value [, index]) => Any, StartIndex := 1) => Array`: Maps each element of the array, starting at `StartIndex`, into a new array where the mapping in `Callback` performs some operation.
			```
			lam := (x, i) => x * i
			arr := [10, 20, 30]
			arr2 := arr.Map(lam) ; [10, 40, 90]
			```
		+ `Remove(Value) => Boolean`: Removes the first occurrence of `Value` and returns `true` if one was found and removed, else `false`. Omitting `Value` removes the first element which has no value. A match is decided by `IndexOf`'s rule, which `Contains` uses too.
		+ `Sort(Callback: (a, b) => Integer) => this`: Sorts the array in place. The callback should use the usual logic of returning -1 when `a < b`, 0 when `a == b` and 1 otherwise.
		+ `ToString() => String`: Returns a string representation of the array.
	+ `Buffer`:
		+ `__Item[]`: Indexer which can be used to read a byte at a 1-based offset.
			+ Throws an `IndexError` if the offset out of range.
	+ `Func`:
		+ `IsClosure`, `IsMethod` and `Params` expose Keysharp function metadata.
	+ `InputHook.KeyOpt("{All}", ...)` affects keyboard input only. Keysharp adds composable `{Keyboard}` and `{Mouse}` (buttons and wheels); mouse movement uses `VisibleMouseMove`.
	+ `Map`:
		+ `ToString() => String`: Returns a string representation of the map.
	+ `String`:
		+ `Length`: The number of characters in the string.
		+ `StartsWith(Token [, CaseSense]) => Boolean` and `EndsWith(Token [, CaseSense]) => Boolean`: Determines whether the string begins or ends with a given string.
* Modified/extended accessors:
	+ `A_EventInfo` is not limited to positive values when reporting the mouse wheel scroll amount.
		+ When scrolling up, the value will be positive, and negative when scrolling down.
* New accessors:
	+ All of these live in the `KS` module, so a script must import the ones it uses: `#import KS { A_DirSeparator }`.
	+ `A_PeekFrequency` gets or sets the current thread's message-check interval in milliseconds.
	+ `A_ClipboardTimeout` can be used at any point in the program to get or set the value normally specified by `#ClipboardTimeout`.
	+ `A_DefaultHotstringCaseSensitive` returns the default hotstring case sensitivity mode.
	+ `A_DefaultHotstringConformToCase` returns the default hotstring case conformity mode.
	+ `A_DefaultHotstringDetectWhenInsideWord` returns the default hotstring word detection mode.
	+ `A_DefaultHotstringDoBackspace` returns the default hotstring backspacing mode.
	+ `A_DefaultHotstringDoReset` returns the default hotstring resetting mode.
	+ `A_DefaultHotstringEndCharRequired` returns the default hotstring ending character mode.
	+ `A_DefaultHotstringEndChars` returns the default hotstring ending characters.
	+ `A_DefaultHotstringKeyDelay` returns the default hotstring key delay length in milliseconds.
	+ `A_DefaultHotstringNoMouse` returns whether mouse clicks are prevented from resetting the hotstring recognizer because `#Hotstring NoMouse` was specified.
	+ `A_DefaultHotstringOmitEndChar` returns the default hotstring ending character replacement mode.
	+ `A_DefaultHotstringPriority` returns the default hotstring priority.
	+ `A_DefaultHotstringSendMode` returns the default hotstring sending mode.
	+ `A_DefaultHotstringSendRaw` returns the default hotstring raw sending mode.
	+ `A_DirSeparator` returns the directory separator character which is `\` on Windows and `/` elsewhere.
	+ `A_GuiTheme` gets/sets the application-wide GUI theme. Accepted values are `Classic`, `System`, and `Dark`. `System` selects the operating-system theme; Eto follows later system-theme changes, while WinForms resolves the setting when it is applied. On Linux and macOS, `Classic` selects Eto's light theme.
	+ `A_KsCorePath` provides the full path to the Keysharp.Core.dll file.
	+ `A_LoopRegValue` which makes it easy to get a registry value when using `Loop Reg`.
	+ `A_MaxThreads` returns the value `n` specified with `#MaxThreads n`.
	+ `A_NoTrayIcon` returns whether the tray icon is hidden, including when its startup state was selected by `#NoTrayIcon` or `#TrayIcon`.
	+ `A_NowMs`/`A_NowUTCMs` returns the current local/UTC time formatted to include milliseconds like so "YYYYMMDDHH24MISS.ff".
		+ These can be used with `DateAdd()`/`DateDiff()` using `"L"` for the `TimeUnits` parameter.
	+ `A_RealThread` is the real OS thread the current pseudo-thread runs on, as a `RealThread` object.
		+ On the main thread it is literally the same object as `RealThread.Main`, so `A_RealThread == RealThread.Main` is the test for "am I on the main thread". See the `RealThread` class under *New classes*.
	+ `A_Thread` is the current pseudo-thread as a `Thread` object.
		+ Every per-pseudo-thread fact is a property on the object rather than its own importable global, so the surface extends without new names. See the `Thread` class under *New classes*.
		+ There is exactly one object per pseudo-thread, so "is this the one I am in" is `thr == A_Thread`.
	+ `A_WinActivateForce` returns whether the forceful method of activating a window is in effect because `#WinActivateForce` was specified.
	+ `A_Timers` returns a `Map` from each timer's callback, the object passed to `SetTimer`, to its enabled state.
* New classes:
	+ `Audio`: Cross-platform audio — polyphonic playback, device discovery and endpoint control. Available from the `KS` module: `#Import Ks { Audio }`. It is independent of the AutoHotkey `Sound*` functions, which are unchanged and keep their own fuzzy device selector and monophonic file playback.
		+ `Audio.Clip` is an immutable decoded sound, from `Audio.Load(path)` or `Audio.FromPcm(buffer, rate, channels, format)`. `FromPcm` copies the bytes synchronously, so the caller may reuse its `Buffer` immediately. Only WAV is decoded without a platform codec; `Audio.IsFormatSupported()` reports what this host can actually decode, and every other container is refused by name rather than played as noise.
		+ `Audio.Output` is an explicit mixer and one native stream. `Open()`, `Prepare(clip)` and then `Play(clip)` — after preparation a play performs no decode, resample or blocking call, so it is safe to call from a frame timer. `VoicePolicy` is `"Oldest"`, `"RoundRobin"` or `"Reject"`, and `StopAll()` silences every voice immediately. One script may hold 16 outputs open, because dropping an output does not close it.
		+ `Audio.Play(source)` is the one-line convenience: it plays a clip or a path through a hidden per-script output and raises rather than returning blank, since a convenience call has nowhere to report a bounded refusal.
		+ Every admitted play returns a stable `Audio.Playback` with `Volume`, `Pan`, `Loop`, `Mute`, `PositionMilliseconds`, `Peak`, `Pause`, `Resume` and `Stop`. Its identity never rebinds, so a handle kept after its voice was stolen controls nothing rather than controlling a stranger. A bounded refusal from `Audio.Output.Play` is a blank return; callers who do not need control ignore the result.
		+ `Audio.Devices()` and `Audio.DefaultDevice()` return `Audio.Device` snapshots carrying `Id`, `Name`, `Kind`, `Status`, `IsDefault`, `IsRunning`, `Volume`, `Mute`, `Refresh()` and `ToClr()`. The `Id` is opaque, exact and unique across outputs and inputs, and is the only durable selector: prefixes, enumeration indexes and `Name:Index` are deliberately not accepted.
		+ `IsRunning` answers whether any application currently holds a live stream on a device, which is what makes "is my microphone being recorded" expressible. It always returns a Boolean. `Status` distinguishes `"Running"`, `"Idle"`, `"Unknown"` and `"Missing"`; `IsRunning` is true only for `"Running"`. An unavailable backend does not establish removal. Running is not the same as audible.
		+ `Audio.OnDeviceChange(callback, kind)` fires `callback(hook, change, device)` for `"Added"`, `"Removed"`, `"Changed"` and `"DefaultChanged"`, returning a started `Audio.DeviceHook`, an `EventHook` that, like an `OnMessage` for `WM_DEVICECHANGE`, does not keep the script running; `Audio.Hooks` lists the running ones. `Audio.Sessions()` and `Audio.SetApplicationVolume()` control other applications where the platform has a per-application model; `Audio.Meter` gives native level observation an explicit lifetime; `Audio.Recorder` captures a microphone or system output to a WAV file or to memory.
		+ Levels are 0 through 100 and pan is -100 through 100, matching `SoundSetVolume` rather than the 0..1 the platform APIs use. Out-of-range input raises a `ValueError` rather than being clamped. An absent value reads as `""`, and an unsupported capability raises an `OSError` naming what the host is missing.
		+ Platform support: Windows uses WASAPI and is verified on hardware. The Linux libpulse backend compiles and has been exercised against a real PulseAudio server for enumeration, playback and recording, though its volume, session and meter paths have not; the macOS AudioQueue backend compiles against the audio contracts but has never been run. macOS has no public per-application audio model, so `Audio.IsSessionControlSupported` is false there.
	+ `App`: The running application — its identity, its invocation and its exit state. Available from the `KS` module: `#Import Ks { App }`, or `#Import Ks` and then `Ks.App`.
		+ `#App` declares what must be fixed before the application starts; `App` reports the application while it runs. Where they name the same fact, `App` reads the running assembly, not the declaration — normally the same string, but not under a host that supplies its own. Several application facts are AutoHotkey's and keep their `A_` names; several `#App` keys have no script-visible runtime reader at all.
		+ Every member is read-only. `Name`, `Title`, `Description`, `Configuration`, `Company`, `Product`, `Copyright` and `Trademark` are the assembly metadata the matching `#App` key sets, and `Version` is its assembly version — two to four components from 0 to 65534, so never a semver prerelease tag. `A_AhkVersion` is the AutoHotkey compatibility level targeted and `A_KsVersion` is the engine build; `App.Version` is your application's.
		+ Reading a key that was never declared returns `""` and never raises, in both compatibility modes. `App.Company != ""` is therefore the has-a-value test — `HasOwnProp` is not, because every declared property is reported present whether or not the key was set. `App.Name` is the exception: it is an identity rather than an attribute, so without the key it is the script's file name without its extension and is never blank.
		+ `App.Title` is assembly metadata, not the default dialog or window title, which remains `A_ScriptName`.
		+ `App.CommandLine` is the *process's* invocation: the host executable followed by every argument. In an interpreted run token 0 is the Keysharp executable and any engine switches precede the script path, so it is not the script's own input — that is `A_Args`.
		+ `App.ExitReason` and `App.ExitCode` report an exit in progress to code that is **not** an `OnExit` callback — a `__Delete`, a timer, a library — and stay readable through the whole teardown. `ExitReason` is `""` when nothing is exiting and `""` again once a callback cancels an exit. Inside a callback the pending code for that exit is the callback's second parameter, which remains the authority; `App.ExitCode` reports the status currently armed.
		+ The `#App` keys `Icon`, `TrayIcon`, `GuiTheme`, `ConsoleApp`, `ErrorStdOut`, `SingleInstance`, `HookMutexName`, `DesktopEntry` and `Files` are deliberately absent: each either already has a live spelling that would disagree with the declared one, or is a build decision with no runtime state to report.
	+ `Base64`: Converts between binary data and Base64 text. Available from the `KS` module: `#Import "Ks" { Base64 }`.
		+ `Base64.Encode(Value [, Encoding := "UTF-8"]) => String`: Converts a `Buffer` — or a string or `StringBuffer`, taken as its **UTF-8** bytes unless another `Encoding` is named — to Base64 text.
		+ `Base64.Decode(Text) => Buffer`: Returns the bytes Base64 text stands for. Text which is not well-formed Base64 raises a `ValueError`.
	+ `Url`: Percent-encoding, as URLs and form bodies use it. Available from the `KS` module: `#Import "Ks" { Url }`. Named `Url` rather than `Uri` because the CLR's own `Uri` is reachable through `Clr` and the two should not collide.
		+ `Url.Encode(Text [, Encoding := "UTF-8"]) => String`: Percent-encodes everything outside the RFC 3986 unreserved set (`A-Z a-z 0-9 - . _ ~`), which is what a path segment, a query value and a form field each need. A space becomes `%20` and never `+`, since `+` means a space only inside a form body and would corrupt a path or a query value. Escaping is per byte, so `Url.Encode("ä")` is `%C3%A4` by default and `Url.Encode("ab", "UTF-16")` is `a%00b%00`.
		+ `Url.Decode(Text [, Encoding := "UTF-8"]) => String`: Resolves percent-escapes. A `%` which is not followed by two hexadecimal digits stands for itself, since a real URL commonly carries one, and `+` stands for itself for the same reason it is never produced.
		+ There is no query-string builder: with these, one is `"?q=" Url.Encode(q)`.
	+ `Boolean`: The type of a truth value, extending `Integer`. Available from the `KS` module.
		+ Every built-in operator that yields a truth value yields a `Boolean` (a custom operator may return any set value): a comparison (`a > b`, `a = b`, `a != b`), a negation (`!a`), and `Map.Has()`. The `true` and `false` keywords are `Boolean` values too.
		+ It behaves as the Integer 1 or 0 everywhere: `Type()` reports `"Integer"`, `x is Integer` is true, it compares equal to 1 and 0, it does arithmetic as one, and it converts to `"1"` and `"0"`. AutoHotkey v2 has no boolean type and the global namespace is AutoHotkey's, which is why the name is in `KS` rather than global — but only the *name* needs the import, never the values.
		+ `x is Boolean` is the only thing that distinguishes one from an ordinary Integer, and `Ks.Json.Encode` is the one place the distinction is visible in output: a `Boolean` is written as JSON `true`/`false` where the Integer 1 is written as `1`.
			```
			#Import "Ks" { Boolean, Json }
			Type(1 > 0)              ; "Integer"
			(1 > 0) is Boolean       ; 1
			(1 > 0) is Integer       ; 1
			1 is Boolean             ; 0
			Json.Encode(Map("ok", 1 > 0))   ; {"ok":true}
			Json.Encode(Map("ok", 1))       ; {"ok":1}
			```
		+ `Boolean(Value) => Boolean`: converts a value, deciding it exactly as `if` would — `Boolean("")` and `Boolean("0")` are false, `Boolean("x")` and `Boolean([])` are true. An unset value raises, as `if` on an unset variable does.
	+ `Clr`: Experimental CLR interop with regular AutoHotkey syntax, meaning easy access to CLR libraries.
		+ `Clr.Load(AssemblyOrPath)` loads a CLR assembly from a dll file or assembly name, and returns a `ManagedAssembly` or `ManagedNamespace` object. Example: `System := Clr.Load("System")`
			+ `ManagedNamespace` can be accessed with property access syntax to get namespaces and types (`ManagedType`). Example: `linq := System.Linq.Enumerable`
			+ `ManagedType` may be accessed for static methods/properties, or called to create a new `ManagedInstance`.
			+ `ManagedInstance` may be accessed with normal AutoHotkey syntax for properties, methods, and indexer access. Example: `linq.Where(nums, isOdd)`
			+ Basic type marshalling between AutoHotkey and CLR is supported (including function objects), more complicated types may not currently work.
			+ A script function handed to a CLR API runs on the script thread that created it, in its own pseudo-thread, so `A_*` values, `Critical` and GUI access all behave normally and an error in it is reported rather than killing the process. One the CLR invokes on its own thread — `Parallel.ForEach`, PLINQ, `Task.Run` — is therefore queued rather than run in place, and the CLR caller receives the return type's default value instead of the script's answer. A comparer or predicate passed into a synchronous call still runs inline and still returns normally.
			+ An enum-typed parameter or property takes a plain Integer, since a script has no enum type: `File.SetUnixFileMode(path, 0x180)`. The value does not have to be a declared member, so a flag combination can be built in script with `|`. The member itself works equally well when fetched through `Clr` (`System.StringComparison.OrdinalIgnoreCase`), and an enum coming back from CLR stays a wrapped member rather than widening to an Integer — take its name with `.ToString()`. A value with no numeric reading raises a `TypeError`, as it does for any other integral parameter.
			+ A `Buffer` or any object exposing numeric `Ptr` and `Size` properties passed to a `byte[]` parameter, property or field arrives as a copy of its bytes: `System.Convert.ToBase64String(buf)`. The CLR code receives a new array, so writes to it never reach the source. `Span<byte>` and `ReadOnlySpan<byte>` parameters view the original storage directly. The `byte[]` overload is preferred over a span overload. The same rules apply at the `#CSharp` boundary. Invalid generic signatures raise a `ValueError`.
		+ `Clr.GetNamespaceName(ManagedNamespace)` returns the full intenal namespace name of the namespace wrapped by `ManagedNamespace`.
		+ `Clr.GetTypeName(ManagedType)` returns the full internal type name of the type wrapped by `ManagedType`.
		+ `Clr.Wrap(Value)` returns any value as an ordinary `Ks.Clr` object, so its own full CLR surface is reachable late-bound. This is how a `Map` and an `Array` reach theirs — a `Map` is a CLR `IDictionary<object, object>` and an `Array` an `IList`, so a CLR API declaring one accepts them directly. The result is always a view over the value itself, and wrapping something already wrapped is the identity.
		+ To reach the .NET object a built-in is a facade over, call that type's own `ToClr()`. It exists only on the types which have one, so its presence is the signal that there is a native object behind the class: `Ks.Task` (the underlying CLR task), `Gui`, `Gui.Control` and `Menu` (the backing toolkit object, whose concrete type is platform-dependent and unspecified), `Ks.Http` and `Ks.Http.Response` (the `HttpClient` and `HttpResponseMessage`), `Ks.Image` (the live underlying bitmap, with pending work materialized; stale after the next transform or `Dispose`), and `Ks.Audio.Device` and `Ks.Audio.Session` (the platform's own object, or an `OSError` where the platform has none). `ToClr()` is a deliberate escape hatch: work done through the result bypasses the owning class's own state and event wiring.
		+ Inline C# (`#CSharp`) holding a `ManagedInstance` reads the raw wrapped object from its `Native` property. Script member access on a wrapper always dispatches to the wrapped object, so `Native` is unreachable from script.
	+ `Font`: A font as a value object, carrying the same information `Gui.SetFont(Options, FontName)` takes but addressable one property at a time. Available from the `KS` module: `#Import "Ks" { Font }`.
		+ `Font(Options := "", Name := "")` takes SetFont's two arguments in SetFont's order, so an existing call converts by moving its arguments across.
		+ Properties: `Name`, `Size`, `Color`, `Weight`, `Quality`, `Bold`, `Italic`, `Underline`, `Strike`. Each is independently **optional** and reads back as `""` when unset. `Bold` is `Weight` viewed as a boolean (reading is `Weight >= 700`, writing sets 700 or 400).
		+ `Options` renders the font back into a SetFont option string containing **only** the properties that are set — a font that names nothing but a family yields `""`. The family is not part of it, because SetFont takes the name separately: `SetFont(f.Options, f.Name)`. A `norm` is emitted first when some style is explicitly off, since that is the only way an option string can turn one off.
		+ `Font.UiDefault` queries the desktop interface font; `Font.GuiDefault` describes a new GUI's font. `Font.Emoji` selects the preferred installed emoji family or falls back to the interface font. Each read returns an independent object with size and styles when native lookup succeeds; before toolkit initialization it may contain only a fallback family. These selections do not set text color.
		+ `Font.Monospace` is the platform's fixed-pitch family — the first of the usual candidates that is actually installed.
		+ `Font.Exists(name)` reports whether a family is installed, and `Font.Families` lists them. Worth asking: a missing family renders silently in a fallback face, so nothing else will tell you.
		+ Two fonts describing the same thing compare equal, so `f1 = f2` works on value rather than identity. The family compares case-insensitively.
		+ Size must be finite, positive and fit native precision; Weight is an integer from 1 to 1000, and Quality from 0 to 5. Color is opaque RGB. A missing GUI family preserves the current family.
		+ GUI/image weights below 700 render as normal, otherwise bold; Windows RichEdit preserves numeric weights. On Windows, Quality controls both GUI fonts and image text. Other platforms, and per-range RichEdit formatting, raise Error for a non-default Quality.
		+ `Options` emits `norm` to clear a style, resetting all styles. Pass the Font object directly in the options position to preserve unspecified styles.
		+ `Gui.FontHandle` is a read-only native handle describing the current GUI font: an HFONT on Windows, a `PangoFontDescription*` on Linux and an `NSFont*` on macOS. It is borrowed: do not free it or retain it across font changes or GUI destruction. Repeated reads return the same handle until the font changes; a colour-only `SetFont` keeps it.
		+ `Font` can be extended: `class MonoFont extends Font { __New() => super.__New("s10", "Consolas") }`.
		+ `Gui.Font` and `Gui.Control.Font` get and set a `Font`. Reading returns a detached **snapshot**: mutating it changes nothing until it is assigned back, and one `Font` can be handed to any number of controls without aliasing them together. Assigning applies only the properties the font actually sets, including an explicit false style without resetting unrelated styles. `SetFont` also accepts a Font in its options position; an explicit font name overrides the object's Name.
			```
			#Import "Ks" { Font }
			f := MyGui.Font          ; detached copy
			f.Size := 14
			f.Bold := true
			MyGui.Font := f          ; now it applies
			MyCtrl.Font := Font("s20 italic cBlue", "Georgia")
			```
		+ `Image`'s text calls take a `Font` in the options position, supplying its font attributes and seeding `DrawText`'s colour argument when omitted. An explicit font-name argument overrides the object's family and must be a string.
			```
			img.DrawText("hi", 10, 10, , Font("s14 bold cRed", "Verdana"))
			sz := img.MeasureText("hi", Font.Monospace)
			```
	+ `HashMap`: Extends `Map` without sorting before enumeration, so its enumeration order is unspecified.
	+ `Http`: HTTP requests. Available from the `KS` module: `#Import "Ks" { Http }`. Either the static shortcuts, which share one stateless client, or a session — `Http(Options)` — which carries default headers, credentials and a cookie jar across its own requests.
		```
		class Http
		{
			static Get(Url [, Options])            => Http.Response
			static Post(Url [, Body, Options])     => Http.Response
			static Request(Method, Url [, Body, Options]) => Http.Response
			static Download(Url, Path [, Options]) => Http.Response   ; straight to a file
			static GetAsync / PostAsync / RequestAsync / DownloadAsync   ; the same four, returning a Task
			__New([Options])                       ; a session
			Headers => Map                         ; live session defaults, all four readable and writable
			Timeout => Number                      ; seconds; -1 waits indefinitely
			BaseUrl => String
			OnData  => Func                        ; "" when there is none
			Get / Post / Request / Download / *Async ; as above, through this session
			Close()                                ; release this session's connections
			ToClr()  => Any                        ; the underlying HttpClient
		}
		class Http.Response
		{
			Status     => Integer                  ; 200
			StatusText => String                   ; "OK"
			IsSuccess  => Boolean                  ; 2xx
			Headers    => Map                      ; case-insensitive, response and content headers together
			Text       => String                   ; decoded per the response charset, else UTF-8
			Body       => Buffer                   ; the raw bytes
			Url        => String                   ; where the response came from, after redirects
			Json()     => Any                      ; the same as Json.Decode(Text)
			ToClr()    => Any                      ; the underlying HttpResponseMessage
		}
		```
		+ **A non-2xx status is not an error.** The server answered, and the body of a failed request is usually where the reason is; `IsSuccess` is the test. What does raise:
			+ an `OSError` when the request never reached a reply — the host does not resolve, the connection is refused or drops, TLS fails;
			+ a `TimeoutError` when nothing arrives within `Timeout`;
			+ a `ValueError` for an empty or non-absolute URL that no `BaseUrl` completes, a scheme other than http and https, an unusable method, header, proxy or option value, an unrecognized option key, a session option given per request, and `Handler` given alongside the options it would override;
			+ a `TypeError` for a `Body` that is an object other than a `Buffer`, `Headers` that is not a `Map`, an `OnData` that is not callable, or a `Handler` that is not an `HttpMessageHandler`;
			+ an `Error` when a session has been closed, or when `OnData` stopped the transfer — the task is canceled, and the synchronous form raises what `Await` raises for canceled work.
		+ Options are given as a `Map` or an object, with **PascalCase** keys. An unrecognized key raises a `ValueError` rather than being ignored, so a typo is reported where it is written.
			+ `Headers`, `Timeout`, `OnData` and `BaseUrl` may be given per request or as session defaults, and a session reads and writes each of them back as a property. A request's own value wins, and `Headers` merges key by key — a request value of `""` removes a header for that request.
			+ `Body` and `Json` describe **one** request, so giving either to `Http(Options)` raises.
			+ `Auth`, `Proxy`, `IgnoreCertificateErrors` and `Handler` configure the connection, which .NET freezes once a request has been sent, so giving one **per request** raises.
			+ `Headers`: a `Map` of header names to values, compared case-insensitively as HTTP compares them. `Content-Type` is applied to the body, and a request with no body ignores every `Content-*` name because there is nothing to describe; every other name is sent as given, including one .NET's own parser would refuse. Unless it is overridden a request carries `User-Agent: Keysharp/<version>`, which `""` removes like any other header.
			+ `Body`: a String, sent as UTF-8 `text/plain`, or a `Buffer`, sent as `application/octet-stream`. Any other object raises a `TypeError`, since it would otherwise be stringified into the request. `Post(Url, Body)` and `Request(Method, Url, Body)` take the same thing positionally.
			+ `Json`: any value, encoded with `Json.Encode` and sent as `application/json`. Giving both `Body` and `Json` raises, since each *is* the body.
			+ `Timeout`: a positive number of seconds, default 30, or `-1` to wait indefinitely; anything else raises. It bounds the wait for the response headers and then each further piece of the body — an **idle** timeout, not a total one, so a slow large download is not cut off while a dead connection still is.
			+ `OnData`: see below.
		+ The connection options:
			+ `Auth`: `[User, Password]`, or the documented string `"Default"` for the logged-in user's credentials. The server's challenge then selects Basic, Digest, NTLM, Negotiate or Kerberos — which is also the only way to reach the latter two. An API that answers with something other than a 401 challenge never gets asked, so preemptive Basic is `Headers` with `"Basic " Base64.Encode(user ":" password)`.
			+ `Proxy`: a proxy URL, with credentials in the usual `http://user:pass@host:port` userinfo form. `""` forces a direct connection; omitting it uses the system proxy.
			+ `IgnoreCertificateErrors`: accepts any server certificate, for a self-signed host on a private network.
			+ `Handler`: an `HttpMessageHandler` built through `Clr`, used as-is. Client certificates, TLS version pinning, a custom validation callback, redirect policy, connection limits and HTTP/2 settings are all reachable this way rather than through options of their own. It **is** the connection, so giving `Auth`, `Proxy` or `IgnoreCertificateErrors` with it raises rather than silently doing nothing, and a handler the script built is not disposed by `Close`.
		+ `OnData(Chunk, Received, Total)` receives the body as it arrives, so a large response never has to be a large script value. `Chunk` is a `Buffer`, `Received` counts the bytes so far, and `Total` is the declared length or `-1` when the server declares none. Bytes accumulate for about 100 ms, or up to 1 MB, before each call — the two bounds keep the callback rate off a fast link and the chunk size off a slow one, without ever dropping a byte. Since the body went to the callback, `Body` and `Text` on the response are then empty.
			+ It runs as a pseudo-thread on the script thread that asked for the request, so `A_*`, GUI access and error reporting all behave normally, and the transfer waits for it — which is what keeps memory bounded. The wait does not occupy a thread, and the idle `Timeout` is not charged for the callback's own runtime, so a slow callback is never reported as a dead server. It is admitted unconditionally, as a message AutoHotkey *sends* rather than posts is: `Critical`, an exhausted `#MaxThreads` and a higher-priority thread neither delay it nor lose it, because the transfer is already waiting on it and a synchronous request would otherwise be waiting on the very thread that has to serve it. As with any callback, it may declare fewer than three parameters.
			+ **Returning a non-zero Integer stops the transfer**, as it prevents an exit from `OnExit`. The task is then canceled, and the synchronous form raises what `Await` raises for canceled work. Take care with a fat arrow, whose value is its last expression: `(chunk, *) => f.RawWrite(chunk)` returns the byte count and therefore stops after the first chunk. Write `(chunk, *) => (f.RawWrite(chunk), 0)`.
		+ `Http.Download(Url, Path [, Options])` fetches straight to a file, carrying the session's headers, credentials and timeout — which the global `Download` cannot. The file is the body's sink, so giving `OnData` with it raises; a session's `OnData` default belongs to that session's other calls and is simply not applied here. An error page is a body like any other, so a non-2xx status is saved and `IsSuccess` is the test before trusting the file. The file is opened only once the response headers arrive, so a request that never reaches a reply leaves an existing file alone, and the `Response` it returns carries the status and headers with an empty `Body`. The global `Download` is the same thing with AutoHotkey's signature and its `ftp` support. Streaming to any other sink is `OnData`:
			```
			#Import "Ks" { Http, Url }

			r := Http.Get("https://api.github.com/search/issues?q=" Url.Encode("repo:x/y is:open"))
			if r.IsSuccess
			    MsgBox r.Json()["total_count"]

			api := Http({BaseUrl: "https://example.com/api/", Headers: Map("Authorization", "Bearer " token)})
			api.Post("items", , {Json: {Name: "x"}})       ; session headers and cookies apply
			Await(api.GetAsync("items")).Text

			f := FileOpen("big.iso", "w")
			Http.Get(url, {OnData: (chunk, got, total) => (f.RawWrite(chunk), pb.Value := 100 * got / total, 0)})
			f.Close()
			```
		+ There is no WebSocket, HTTP server, multipart body, cookie inspection or retry policy. `System.Net.WebSockets` and `System.Net.Sockets` are reachable through `Clr` on every platform.
	+ `Json`: Converts between JSON text and script values. Available from the `KS` module: `#Import "Ks" { Json }`, then `Json.Encode(Value)` and `Json.Decode(JsonText)`.
		+ `Json.Encode(Value [, Indent, NullValue]) => String`: Returns the JSON text for a script value.
			+ A `Map` becomes a JSON object, an `Array` becomes a JSON array, and any other object contributes its own value properties (a dynamic property is skipped rather than invoked, because encoding a value must not run script code). A `Map` enumerates in sorted key order, so encoding one sorts its keys and the same map always produces the same text regardless of insertion order.
			+ `Indent` follows the convention of JavaScript's `JSON.stringify` and Python's `json.dumps`: omitted, `""` or `0` writes the compact single-line form (the default); a number writes that many spaces per level; a string of spaces **or** of tabs is used as the indent unit itself, as in ``Json.Encode(Value, "`t")``. The widest indent is 127; a mix of spaces and tabs, or any other string, raises a `ValueError`.
			+ Indented output separates lines with a single line feed on every platform — deliberately not the platform line ending, so that the same value always produces the same bytes and a hash taken over encoded JSON (a lock file, a cache key) is not host-dependent.
			+ Quotes are escaped but non-ASCII text is not, so `Json.Encode("äöü")` is `"äöü"` rather than a run of `\uXXXX` escapes.
			+ A reference cycle, or nesting deeper than 128 levels, raises a `ValueError`. Two distinct but equal containers are not a cycle.
		+ `Json.Decode(JsonText [, CaseSense := true, NullValue])`: Returns the script value for JSON text.
			+ A JSON object becomes a `Map` and a JSON array becomes an `Array`. An integral number becomes an `Integer`, and anything else — including a value too large for a 64-bit integer — becomes a `Float`.
			+ `CaseSense` is the case sensitivity given to **every** `Map` in the result, spelled as for `Map.CaseSense`: `true` (the default, matching `Map()`), `false`, or `"Locale"`. It has to be chosen here because `Map.CaseSense` cannot be assigned once a map holds entries. With `false`, keys differing only in case collapse into a single entry, as they do in any case-insensitive `Map`.
			+ Trailing commas and `//` and `/* */` comments are accepted, because hand-written configuration files commonly carry them. Everything else follows the JSON grammar; malformed text, or nesting deeper than the 128 levels `Encode` also allows, raises a `ValueError`.
		+ A `Boolean` — the `true` and `false` keywords, or any comparison, negation or `Map.Has()` result — is written as JSON `true`/`false`, where the Integer 1 or 0 is written as a number. That is what makes booleans survive a round trip, and `x is Boolean` is what tells the two apart in a decoded document.
		+ Nulls: JSON has a `null`; the language has no value a container can hold for it. With no marker a JSON `null` decodes to **unset**, which means what `unset` means everywhere else — a `Map` key is simply absent, and an `Array` element is a hole that keeps the array's `Length`.
			+ So `Json.Decode('{"a":null,"b":""}')` gives a Map with only `b`, and `Json.Decode('[1,null,3]')` gives a 3-element Array whose element 2 is a hole. `Has()` is the test, and a `null` no longer collides with an empty string.
			+ `NullValue` overrides that on `Decode` — it is what a JSON `null` becomes — and on `Encode` it is the value written back out as `null`. Supply the same marker to both to tell a `null` apart from an *absent* key, which is the one distinction unset cannot carry.
			+ `Encode` has no default marker, because defaulting it to anything would silently turn every occurrence of that value into a null.
			+ An object marker is matched by identity, so it cannot collide with data; any other value is matched by value, which is a caller deliberately nominating every occurrence of it.
			```
			#Import "Ks" { Json }
			NULL := Object()

			Json.Decode('{"a":null}').Has("a")            ; 0 — the key is simply not there
			Json.Decode('[1,null,3]').Length              ; 3 — element 2 is a hole

			cfg := Json.Decode(FileRead("config.json"), caseSense: false, nullValue: NULL)
			if (cfg["Timeout"] == NULL)
				cfg["timeout"] := 30       ; the key lookup is case-insensitive
			FileOpen("config.json", "w").Write(Json.Encode(cfg, 2, NULL))

			Json.Encode(Map("a", NULL))            ; {"a":{}} — no marker, so it is just an object
			Json.Encode(Map("a", NULL), , NULL)    ; {"a":null}
			```	+ `StringBuffer`: Can be used for passing string memory to `DllCall()` which will be written to inside of the call.
		+ There are two methods for creating a `StringBuffer`:
			+ `StringBuffer(str := "") => StringBuffer`: Creates a `StringBuffer` with a string of `str` and a capacity of 256.
			+ `StringBuffer(str, capacity) => StringBuffer`: Creates a `StringBuffer` with a string of `str` and a capacity of `Max(16, capacity)`.
		+ `StringBuffer` is implicitly castable to `String`.
			```
			sb := StringBuffer("hello")
			MsgBox(sb) ; Shows "hello".
			```
		+ As an alternative to passing a `Buffer` object with type `Ptr` to a function which will allocate and place string data into the buffer, the caller can instead use a `StringBuffer` object to hold the new string.
			+ This relieves the caller of having to create a `Buffer` object, then call `StrGet()` on the new string data.
			+ `wsprintf()` is one such example.
				```
				; Using a Buffer:
				ZeroPaddedNumber := Buffer(20)
				DllCall("wsprintf", "Ptr", ZeroPaddedNumber, "Str", "%010d", "Int", 432, "Cdecl")
				MsgBox(StrGet(ZeroPaddedNumber)) ; Shows "0000000432".

				; Using a StringBuffer:
				sb := StringBuffer()
				DllCall("wsprintf", "Ptr", sb, "Str", "%010d", "Int", 432, "Cdecl")
				MsgBox(sb) ; No need to use StrGet() anymore.
				```
		+ `StringBuffer` internally uses a `StringBuilder` which is how C# P/Invoke handles string pointers.
	+ `Thread`: The current pseudo-thread, obtained from `A_Thread`.
		+ `Thread` is a **class**, not a function, and calling it runs the AHK sub-functions unchanged — `Thread "NoTimers"`, `Thread "Priority", n`, `Thread "Interrupt", n`. One name therefore covers the thread settings and the thread object, which is what lets `A_Thread`'s type simply be `Thread`. It stays a global name (no import needed for `thr is Thread`) because `Thread` was already global as a function; the consequence is that `Thread is Func` is now false.
			```
			class Thread          ; script name; the CLR type is KeysharpThread, as Func is KeysharpFunc
			{
				static Call(SubFunction [, Value1, Value2])   ; the AHK Thread() function
				Id => Integer            ; 48-bit creation sequence << 16 | 16-bit zero-based stack position
				Index => Integer         ; 1-based stack position; 1 is the oldest active pseudo-thread
				IsActive => Boolean      ; false once this pseudo-thread has ended
				Kind => String           ; what launched it: Auto, Hotkey, Hotstring, Timer, Event, Message,
				                         ; Callback, Input, WinEvent, Com, Clr, RealThread, or "" when the
				                         ; launch site does not name one. Event covers every other
				                         ; registered handler: GUI, menu, OnExit, OnClipboardChange, the
				                         ; Monitor, Clipboard and Audio hooks, and Overlay pointer handlers.
				Elapsed => Integer       ; ms since launch
				Priority => Integer      ; get/set; same storage as Thread "Priority"
				Critical => Boolean      ; get/set; the object form of the Critical function
				Paused => Boolean        ; get/set; the object form of Pause. Setting it takes effect when
				                         ; that thread next resumes. A_IsPaused is this on Underlying
				IsInterruptible => Boolean ; read-only
				Underlying => Thread     ; the pseudo-thread this one interrupted, or ""
				Exit([ExitCode := 0]) => Integer   ; cooperative; returns the target's Id
				ExitCode => Integer | "" ; the code its Exit ended it with, from the start of its unwind; "" otherwise
			}
			```
		+ Pseudo-thread state is pooled and reused, so a `Thread` object captures its ID and re-checks it on every access. Once its pseudo-thread ends, `Id`, `Index` and `ExitCode` still answer from captured values and `IsActive` reports false, while everything else throws `TargetError` — a stored object can never silently describe a later pseudo-thread that reused the slot.
		+ Boolean members follow the library-wide naming rule: `IsActive`/`IsInterruptible` are read-only, `Critical`/`Paused` are settable.
		+ `Critical`, an exhausted `#MaxThreads` and a visible menu refuse a *thread launch*; they do not stop message dispatch. A posted continuation, and on the main thread a pending `__Delete`, are still served at the uninterruptible thread's pump points, matching AHK, where a Critical thread inside `Sleep` still processes sent messages. Queued timers and hotkeys wait until launches are admitted again, in the order their own queue holds them. `RealThread.Send` is not in this group: its callback runs as a thread of its own on the target, so an uninterruptible target refuses it outright rather than deferring it.
		+ Outside a loop `A_Index` is a value of the current pseudo-thread, 0 when it starts, which a script may assign.
		+ There is deliberately no `IsCurrent` property, and likewise no `IsMain` on `RealThread` — identity comparison answers both questions. `RealThread.IsAlive` reports whether the OS thread has not yet terminated.
		+ `Exit(n)`, or `thr.Exit(n)` on a Thread object, ends that thread with code n, which becomes its `ExitCode` and, as AutoHotkey's pending exit code, the code the script exits with if it exits when that thread ends; a launched thread's code is otherwise dropped, while the auto-execute section's stays pending until the next thread ends.
		+ A `Thread` object may be read from any real thread, but every setter and `Exit` throw `TargetError` when called from a real thread other than its owner. Pseudo-thread stacks are per real thread and are mutated without locking.
	+ `RealThread`: Manages real threads which are not related to the green threads that are used for the rest of the project.
		+ A `RealThread` is created by calling the `RealThread` class static instance.
			```
			class RealThread
			{
				static Call(Callback [, Arguments*]) => RealThread ; Runs `Callback` on a new real thread.
				RealThread(Callback [, Arguments*])
				static Main => RealThread ; The script's main thread.
				Id => Integer             ; Managed id of the backing OS thread.
				IsAlive => Boolean        ; True while the OS thread has not terminated.
				Task => Task              ; The entry function's eventual result (workers only).
				Terminated => Task        ; Completes when the OS thread is gone (workers only).
				Threads => Array          ; The active ScriptThreads of this real thread, oldest first.
				Post(Callback [, Arguments*]) => Task      ; Queue work there; the Task is its result.
				Send(Callback [, Arguments*]) => Any       ; Run work there now, block, return its value.
				Exit([ExitCode := 0]) => Task              ; Shutdown request; returns Terminated.
			}

			ThreadFunc(obj)
			{
				; Long running operation to run on a real thread.
			}

			theThread := RealThread(ThreadFunc, 1)  ; Create and start the thread, passing 1 as obj.
			result := Await(theThread.Task)         ; Wait for the body and take its value.
			```
		+ **A worker has two completions and they are not the same event.** `Task` is the entry function's result and settles the moment the body leaves. `Terminated` settles when the OS thread is gone. A worker that registered a timer, hotkey or callback keeps serving them long after its body returned, so `Await(worker.Task)` returns while `worker.IsAlive` is still true; `Exit` is how such a worker is shut down.
		+ `Task` follows the ordinary `Task` rules: it succeeds with the body's return value, fails with the body's error — which is carried by the task without calling `OnError` at the throw site, rethrows when awaited, and is reported as an unobserved failure if nobody ever looks — or is canceled if `Exit` ended the body first. `Task.Status` and its `Is*` predicates report which. The same boundary applies to `Post` and `Send`: queued work faults its task, while the synchronous form rethrows to its caller.
		+ `Post` queues work and hands back the `Task` carrying its result: ignore it for fire-and-forget, `Then` it to react, or `Await` it to wait. Because it queues as a thread launch, an uninterruptible target or an exhausted `#MaxThreads` **defers** the work.
		+ `Send` is not `Await(Post(...))` spelled differently. A call to your own thread is a direct call, the main thread is reached through the UI framework, and the request is served past queued work whose launch is parked — after which an uninterruptible target refuses it outright. Use `Send` when a busy target should fail fast and `Await(Post(...))` when it should be waited for.
		+ `RealThread.Main.Post(fn)` and `RealThread.Main.Send(fn)` are the supported ways to move work back onto the main thread from a worker. `A_RealThread` is the calling thread's object; on the main thread it is literally the same object as `RealThread.Main`, so `A_RealThread == RealThread.Main` is the test for "am I on the main thread".
		+ `Task`, `Terminated` and `Exit` throw `TargetError` on `RealThread.Main` and on adopted threads, which have no body and no end this class controls. `Post` and `Send` raise on a thread that is no longer alive.
		+ `Exit` called on the worker's own thread (`A_RealThread.Exit()`) unwinds immediately and does not return, so its task is unobservable there.
	+ `Task`: Work that finishes later. Every CLR call returning a .NET `Task` hands one of these back, so `Ks.Clr` and `#CSharp` agree on what work-in-flight looks like.
		```
		class Task
		{
			static Call(Value) => Task          ; wrap native work or an object implementing __Await()
			Status => String                    ; "Pending" | "Succeeded" | "Failed" | "Canceled"
			IsPending => Boolean                ; true until a terminal outcome
			IsSucceeded => Boolean              ; true only after successful completion
			IsFailed => Boolean                 ; true only after completion with an error
			IsCanceled => Boolean               ; true only after cancellation
			Result => Any                       ; the successful value, "" otherwise. Never waits.
			Error => Any                        ; the failure as a catchable error object, "" otherwise
			ToClr() => Any                      ; the underlying CLR task, for IsCompleted/ContinueWith/…
			Wait([Timeout := -1]) => Boolean    ; true if it finished; false only on timeout; never cancels it
			Then(OnSuccess [, OnFailure]) => Task ; handle a value or error on this script thread
			                                      ; cancellation propagates; returned work is flattened
			static WhenAll(Tasks*) => Task      ; finishes when all do; Result preserves input order
			static WhenAny(Tasks*) => Task      ; transfers the first value, failure or cancellation
			static Create(Producer) => Task      ; Producer receives Succeed, Fail and Cancel callbacks
		}
		```
		+ `Status` is the canonical value — **one read, one instant**, so a decision spanning more than one state gets a consistent snapshot. The four `Is*` predicates ask the same question one at a time; each is its own read, so two of them read in sequence can straddle a transition. Use a predicate for a single question and `Status` when it matters that the answers agree. Prefer them to comparing `Status` against a string literal: `t.IsFaild` raises, `t.Status = "Faild"` is silently false, and `==` compares case-sensitively where `=` does not.
		+ `Result` is a snapshot and never blocks. `Await(task)` and `Wait` are the waiting forms, and both pump, so timers, hotkeys and the GUI stay alive while a script waits.
		+ `Then` reacts without blocking. `OnSuccess` receives the unwrapped value after success; optional `OnFailure` receives the same catchable `Error` exposed by `Task.Error` and can recover the returned chain. Either may declare no parameter. Requiring more than one raises `ValueError` immediately when the target signature can be resolved; a method bound by name is checked when its concrete member can be found, otherwise invocation performs the check. Cancellation invokes neither callback and propagates unchanged. The selected callback runs as a pseudo-thread on the script thread where `Then` was called. Tasks and CLR tasks or value tasks returned at any depth are flattened, and a custom awaitable returned directly is adopted. A `RealThread` is not awaitable, so return `worker.Task` (or `worker.Terminated`) rather than the worker itself. Raw `task.ToClr().ContinueWith` instead uses CLR scheduling and provides neither this script affinity nor result flattening.
		+ A pending `Then` keeps its owner alive until its callback has run. Background work returned by that callback does not itself make a script persistent; a downstream `Then` registers its own callback and therefore its own lifetime root. If the owner is torn down before a pending callback can run, the returned task fails instead of hanging.
		+ `Task.Create(Producer)` turns a callback-shaped API — a hotkey, a GUI control, a device notification — into a task. `Producer` runs synchronously before `Create` returns and receives only the positional prefix it declares, up to `Succeed`, `Fail` and `Cancel`; its own return value is ignored. Each settlement function returns true only if it won: `Succeed([Value])` settles from `Value` and adopts asynchronous work's eventual value, failure or cancellation, `Fail([Reason])` accepts an `Error` or description, and `Cancel()` produces a canceled task. A producer error before settlement fails the returned task; after settlement it has no effect.
			```
			gate := Task.Create(Succeed => Hotkey("Esc", (*) => Succeed("user")))
			winnerValue := Await(Task.WhenAny(gate, Download()))
			```
		+ A task has no public `Cancel()` because a consumer cannot safely cancel work it did not create. Cancellation belongs to the producer: use the `Cancel` callback inside `Task.Create`, or pass a cancellation token to CLR work. Likewise there is no `Delay`/`Completed`/`Run`: CLR tasks cross the ordinary boundary, and `RealThread(Callback)` runs script code on another thread.
		+ A producer or `Then` callback error becomes that operation's failed Task without calling `OnError` at the throw site. A failure nobody ever looks at is reported as an ordinary script error when the task is collected — the same terms .NET reports one on, so a task you are about to `Await` is never reported out from under you.
		+ `Wait` returns true when the task reaches any terminal outcome before the timeout and false only when the timeout elapses first. It neither rethrows nor marks the task's failure observed; inspect `Status` or the `Is*` predicates, while `Error` and `Await` observe a failure and expose the same error object. A timeout stops only that wait and does not cancel the work. `WhenAny` transfers only the winner's outcome; it neither observes nor cancels the losers. `WhenAll` fails if an input fails, or is canceled if at least one input is canceled and none fail.
		+ `Await`, `Task(Value)`, `WhenAll` and `WhenAny` also accept a script object implementing `__Await()`. The zero-argument method must return a `Task`, CLR task or value task, or another object implementing `__Await()`; protocol chains are followed until native work is reached. It is called synchronously on each consumption, so an implementation should cache stable work rather than create a fresh operation each time, especially for a `ValueTask`. An exception raised there is synchronous unless `Then` or `Succeed` is consuming it, in which case it becomes that operation's outcome. This lets a domain object expose asynchronous work without inheriting from `Task`. A `RealThread` is deliberately not accepted: it has two completions, so `worker.Task` and `worker.Terminated` must be named apart.

	+ `Await(value [, timeout := -1])`: Waits for work that finishes later — a `Task`, a CLR task reached through `Ks.Clr`, or an object implementing `__Await()` — and returns what it produced. This is Keysharp's `await`.
		+ It does not suspend the way C#'s `await` suspends a method: a Keysharp pseudo-thread runs to completion on its own frame. Instead it blocks the calling thread and pumps everything else, exactly as `Sleep` and `WinWait` do, so timers, hotkeys and the GUI stay alive throughout.
		+ Because it pumps, it is an interruption point: another pseudo-thread can start while it waits, just as inside `Sleep`. That matters most around `Lock`, whose ownership is per real thread and reentrant — a timer that acquires and releases the same lock during an `Await` releases the waiting thread's acquisition. Use `Critical` to hold a section closed across a wait.
		+ A failure is rethrown as its catchable Keysharp error; canceled work raises a base `Error` (cancellation is not failure, so `Task.Error` stays empty). A timeout raises `TimeoutError` without canceling the work. `task.Wait(timeout)` is the non-throwing form and returns false only on timeout. Passing something that does not finish later raises a `TypeError` rather than silently handing the value back.
		```
		#import KS { Task, Await }
		docx := Await(MakeDocx(html))                 ; block here, stay responsive
		d := MakeDocx(html), p := MakePdf(html)       ; or run both, then join
		results := Await(Task.WhenAll(d, p))
		MakePdf(html).Then(path => FileAppend(path, out))       ; or never block at all
		```
	+ `Lock`: Guards code shared between real threads. This is the whole mechanism — the free function `LockRun` has been removed, because a lock that accepts any object has to document which objects are bad locks (a number boxes afresh per call site; a string literal is shared process-wide), and a type that can only be a lock cannot be misused that way. The scoped form is ordinary script code:
		```
		LockedCall(lock, callback, args*)
		{
		    lock.Acquire()
		    try
		        return callback(args*)
		    finally
		        lock.Release()
		}
		```
		```
		class Lock
		{
			static Call() => Lock
			Acquire([Timeout := -1]) => Boolean ; true once held, false if the timeout elapsed first
			Release()                           ; once per successful Acquire, from the acquiring real thread
		}
		```
		+ The lock belongs to a *real* thread and is reentrant. `Acquire` blocks the whole real thread, so acquiring on the main thread stalls the message loop — pass a timeout there.
	+ New class `Image` provides cross-platform image capture and manipulation. Capture with `Image.FromDesktop()`, `Image.FromMonitor(Number)`, `Image.FromRect(X, Y, Width, Height)` or `Image.FromWindow(WinTitle [, Options, WinText, ExcludeTitle, ExcludeText])`; load with `Image.FromFile(Path)`, `Image.FromBitmap(Handle)` or `Image.FromClipboard()` (the `Clipboard.Image` getter, returning `""` when no image is available). `Image.Create(Width, Height [, Background])` creates an ARGB canvas; omitted or empty `Background` is transparent.
		+ `FromWindow` accepts a mode string or an object containing `Mode` and optional `Decorations`. The supported modes are `"BitBlt"`, `"BitBltOpaque"`, `"PrintWindow"`, `"PrintWindowOpaque"` and `"FullContent"` (the default). The Opaque modes temporarily turn off window transparency; FullContent uses PrintWindow with PW_RENDERFULLCONTENT. Names are case-insensitive; empty strings, numbers and unknown names raise `ValueError`. Linux and macOS validate the mode but use their own capture technique. `Decorations` defaults to false and requests client-area-only capture on KWin; other backends capture a fixed extent. Capture availability and occlusion behavior depend on the backend. `FromRect` and the other capture factories use absolute screen coordinates and ignore Pixel `CoordMode`.
		+ Paint with `Clear`, `DrawLine`, `DrawRect`/`FillRect`, `DrawRoundRect`/`FillRoundRect`, `DrawEllipse`/`FillEllipse`, `DrawImage`, and `DrawText(Text, X, Y [, Color, Options, FontName])`. Text options use Gui font syntax such as `"s16 bold italic"`; `MeasureText(Text [, Options, FontName])` returns `{Width, Height}`. Colors accept names, opaque `0xRRGGBB`, and `0xAARRGGBB`; preserve a fully transparent alpha with an eight-digit string or `"Transparent"`, because numeric `0x00RRGGBB` is indistinguishable from opaque RGB. Raster geometry transforms (`Scale`, `Resize`, `Rotate`, `Flip`, `Crop`) and color transforms (`Grayscale`, `Alpha`, `Brightness`, `Contrast`) are chainable and applied lazily. Save with `Save(Filename)` or `ToBitmap()`, preview with `Show([Title, Wait])`, and duplicate with `Copy()`.
		+ Build reusable vector geometry with `Image.Path([FillRule])`. A path provides `MoveTo`, `LineTo`, `CubicTo`, `ArcTo`, `Close`, `AddRect`, `AddRoundRect`, `AddEllipse`, `AddPolygon`, `AddPath`, `Clear`, and `Clone`; paint it with `FillPath` or `DrawPath`. `Image.Brush.LinearGradient` and `Image.Brush.RadialGradient` create immutable gradient descriptions accepted by both path methods and by `DrawText`'s Color parameter.
		+ `Image.Transform` is the drawing coordinate system. It reads as a detached `{ScaleX, ScaleY, OffsetX, OffsetY, SkewX, SkewY}` object, and assignment changes only the supplied own data properties. `Clip(Path)` intersects a path captured under the current transform, while `Clip()` resets clipping. `SaveState()` captures both drawing transform and clip for `RestoreState(State)`. Existing shapes, text, and `DrawImage` use the transform and clip captured when each call is queued; `Clear`, pixel access, and raster transforms ignore them. `Copy()` starts with identity drawing state and is the supported way to duplicate an Image.
		+ `Search(Needle [, X, Y, Width, Height, Variation, Trans, Direction])` returns `{X, Y}` or `""`; `SearchAll` takes the same parameters and returns an array of `{X, Y}` objects, empty when none match. `SearchPixel(Color [, X, Y, Width, Height, Variation, Direction])` returns `{X, Y, Color}` or `""`, where `Color` is the matched full ARGB pixel. Region parameters default independently to the origin/far edge and are clamped to the image. Coordinates remain absolute image pixels. Matching is RGB-only; `Variation` is a per-channel tolerance clamped to 0 through 255 and `Trans` is a needle color that matches anything.
		+ Image search `Direction` is `"TopLeft"` (default), `"TopRight"`, `"BottomLeft"` or `"BottomRight"` for row scans; `"LeftTop"`, `"LeftBottom"`, `"RightTop"` or `"RightBottom"` for column scans; or `"Center"` for the match nearest the region center. The first word selects the outer scan order and the second the inner order. `SearchPixel` supports only the four row-scan names. All names are case-insensitive; empty strings, numbers and unknown names raise `ValueError`. The separate screen `ImageSearch` function retains its `*DirN` option spelling with values 1–9, rejecting missing, malformed and out-of-range values.
		+ `GetPixel(X, Y)` returns full ARGB and `SetPixel(X, Y, Color)` writes a pixel. `GetPixelData([BytesPerPixel := 4, Buffer])` returns tightly packed bytes; 4 means RGBA and 1 means grayscale. An optional destination exposing `Ptr` and `Size` must hold at least `Width * Height * BytesPerPixel` bytes; the same object is returned and bytes beyond the required length are untouched. `SetPixelData(Data [, BytesPerPixel := 4])` and `Image.FromBuffer(Data, Width, Height [, BytesPerPixel := 4])` consume the same layout. `OriginX`/`OriginY` and `ScaleX`/`ScaleY` describe the capture's screen origin and pixel scale. Rotate and Flip invalidate the origin, and Rotate also invalidates the scale; invalid metadata reads `""`, and `SetOrigin` restores it explicitly. Using a disposed Image raises an error.
		* New KS class `Overlay` provides a click-through, always-on-top image surface. Use `Overlay()` when `SetImage` will supply its size, `Overlay(x, y, width, height)` for a drawable blank canvas, or `Overlay.FromImage(source [, x, y, width, height])` to start with a copied image. Geometry uses native screen units while the platform chooses the backing-pixel density. Draw through the borrowed `Canvas` image and call `Present()` to publish a completed frame without changing visibility. `SetImage(source [, x, y, width, height])` copies an image and applies geometry together; `Redraw(callback [, x, y, width, height])` gives `callback(canvas)` a private target-sized canvas and presents it as one completed frame. Canvas operations that replace or transform its pixels are refused; use `Canvas.Copy()` for an independent image. `Show`, `Move`, `Hide`, and `Destroy` control the surface; `Width`/`Height` resize its display rectangle without discarding the canvas. `Opacity` is clamped to 0 through 255; `ClickThrough`, `IsVisible`, `Hwnd`, and `OnEvent` expose presentation and pointer state. One pointer event's handlers run as one chain under the `Gui.OnEvent` rule: a non-empty return, 0 included, or an uncaught error stops the rest. Registrations are keyed to the registering real thread, run on that thread, and a removal affects only that thread's matching registration. `Highlight` and, on Linux/macOS, `ToolTip` use the same primitive. GNOME/Cinnamon Wayland draw a click-through overlay inside the shell itself, so it is not a window and takes no taskbar entry; its frames are transferred as bounded PNG data. An interactive overlay (`ClickThrough := false`) has to be a real surface there and does appear in the window list. macOS behavior is unverified.
* Syntax:
	+ The spread operator `*` may be used multiple times in one function call: `MyFunc(arr1*, arr2*)`.
	+ The 40 character limit for hotstring abbreviations has been removed. There is no limit to the length.
	* Reference parameters for functions using `&` are supported with the following improvements and caveats:
	+ Passing class members, array indexes and map values by reference is supported.
		+ `func(&classobj.classprop)`
		+ `func(&myarray[5])`
		+ `func(&mymap["mykey"])`
	+ Reference parameters in functions work for class methods, global functions, built in functions, lambdas and function objects.
	+ Preprocessor directives are supported using the familiar syntax of C#.
		+ `#if symbol` is used to enable a section of code if symbol is defined.
		+ By default, the following are defined:
			+ `WINDOWS` if you are running the script on Microsoft Windows.
			+ `LINUX` if you are running the script on linux.
			+ `KEYSHARP`
		+ `#else` can be used to take an alternate path if the preceding `#if` evaluates to `false`.
		+ `#elif symbol` can be used to evaluate another symbol if the preceding `#if` or `#elif` evaluate to `false`.
		+ All preprocessor blocks must end with an `#endif`
		+ New preprocessor symbols can be defined using `#define symbol`.
		+ Logical statements can be evaluated using the operators `&&`, `||` and `!`.
		+ Evaluation of preprocessor statements are case insensitive.
		+ Some examples are:
			```
			#if WINDOWS
				MsgBox("Windows")
			#elif LINUX
				MsgBox("linux")
			#else
				MsgBox("Unsupported OS")
			#endif

			#if !(WINDOWS || LINUX)
				MsgBox("Unsupported OS")
			#endif

			#if 1
				MsgBox("Always true")
			#endif

			#if 0
				MsgBox("Always false")
			#endif

			#define NEW_DEFINE
			#if NEW_DEFINE
				MsgBox("True because of new definition")
			#endif
			```
* Miscellaneous behavior:
	* In addition to the `AHK` module, a `KS` module has been added which contains extra variables and methods added to Keysharp. Accessing them requires using the `import` statement, eg `#import KS { HashMap, Sinh }`.
		+ These include all new classes, functions and variables mentioned here (eg `HashMap`, `Sinh` etc).
		+ Note: class method/property additions are always included and do not need to be imported (eg `String` or `Buffer` extra methods).
	* Finite choices:
		+ Public states, kinds, reasons and modes use stable documented strings. AHK spellings and numeric contracts remain part of compatibility. Keysharp additions accept case-insensitive names and reject unknown choices with `ValueError`; internal enum names do not define the public vocabulary.
		+ Read-only Boolean predicates answer frequent, unambiguous questions and remain Boolean. Read the canonical status once when a decision distinguishes several changing states. There are no public enum objects or constant containers.
	* Boolean property naming:
		+ On a class, an `Is` prefix marks a read-only predicate (`Thread.IsActive`, `Thread.IsInterruptible`, `Func.IsBuiltIn`, `Func.IsVariadic`).
		+ A settable boolean is named for the state itself (`Thread.Critical`, `Thread.Paused`), so it reads as `obj.Paused := true` rather than as a question.
		+ This follows AutoHotkey, which never prefixes a settable boolean property — `InputHook.VisibleText`, `InputHook.CaseSensitive`, `GuiControl.Enabled`, `GuiControl.Visible` — while using `Is` on some read-only ones (`Func.IsBuiltIn`, `Func.IsVariadic`, alongside unprefixed `InputHook.InProgress`, `File.AtEOF`, `GuiControl.Focused`).
		+ The `A_Is*` variables are a separate namespace and keep their AHK spelling: `A_IsCritical`, `A_IsPaused`, `A_IsSuspended`.
	+ A compiled script can be reloaded.
		+ AutoHotkey does not support reloading a compiled script.
	+ When sending a string through `SendMessage()` using the `WM_COPYDATA` message type, the caller is no longer responsible for creating the special `COPYDATA` struct.
		+ Instead, just pass `WM_COPYDATA (0x4A)` as the message type and the string as the `lparam`, and `SendMessage()` will handle it internally.
		+ Note, this will send the string as UTF-16 Unicode. If you need to send to a program which expects ASCII, then you'll need to manually create the `COPYDATA` struct.
	+ New preprocessor directives:
		+ `#Module` starts or reopens a module, and `#Import` binds module objects or selected members. Script globals are implicitly wildcard-exportable; names beginning with `_` require an explicit import. Ordinary imported aliases remain explicitly importable and visible through the module object, but only `#Import Export` forwards them to wildcard consumers. Bare imports bind the module object; default exports and standalone `Export` declarations do not exist.
			+ A dynamic reference (`%"Name"%`) resolves local declarations and imported names, including names supplied only by a wildcard import.
			+ A wildcard import of the `Ks` module supplies no name the module assigns, as with any wildcard, so `A_PeekFrequency := 35` after `#import KS { * }` creates the module's own variable, and assigning the built-in takes `#import KS { A_PeekFrequency }`.
			+ An `#Import` in a function or class body binds its names to that scope only. A wildcard there supplies no name the scope assigns or declares, and assigning a variable imported by name writes through to it.
			+ `#Warn NamedArg` checks a call by name against what the name binds to, an import included, so `Overlay(nosuch: 1)` after `#import KS { Overlay }` warns, as `Buffer(nosuch: 1)` does.
			+ `extends` resolves a base class as any name in the module does, so a class of the `Ks` module or of another module is a base class only through an import or a module object, as in `class MyMap extends HM` after `#Import Ks { HashMap as HM }`.
		+ `#CSharp` embeds C# members in the script assembly for hot loops, buffer work and interop.
			+ Use `#CSharp` … `#EndCSharp` for an inline block, `#CSharp "helper.cs"` for a file on the module search path, or `#CSharp <Helper>` for `Helper.cs` in the same Lib folders searched by `#Include <Helper>`. The library form also uses #Include's underscore fallback. Blocks may appear at module scope or directly in a class.
			+ At module scope, `public static` methods are callable locally, through the module object and by an explicit `{ Name }` import. `[Export]` additionally exposes one to `{ * }`; the `Default` option is no longer supported.
			+ In class blocks, public methods and property accessors are script-visible; non-public members remain C# helpers, `init` is read-only, and fields are not exposed. `[Static]` selects the class-static side.
			+ Values use the same conversions as `Ks.Clr`. Unsupported public signatures are rejected, and CLR exceptions are mapped to catchable Keysharp errors where possible.
			+ Usings are shared within a module but isolated between modules. Script preprocessor symbols and C# `unsafe` are supported. Calls use normal script dispatch, so group substantial work across the boundary.
		+ `#Package [*i] id [version]` resolves a NuGet package for `Ks.Clr` and inline C# at compile time. It follows `NuGet.Config` and supports managed, resource and native assets, but not package build hooks. `*i` makes a missing package optional.
		+ `Clr.LoadPackage(id, version?, optional?)` loads a NuGet package at runtime. Prefer `#Package` for known dependencies.
		+ `#App { key: value, ... }` declares final application facts: identity and metadata, packaged assets, generated host shape, presentation defaults, and fixed integration identifiers. Its blocks merge into one key/value map; no key acts as a source command or observes intermediate state.
			```
			#App {
				Name: "MyTool",              ; assembly identity (Assembly.GetName().Name)
				Title: "My Tool",            ; assembly metadata attributes, read back by the App class
				Description: "Does things",
				Company: "Acme Corp",
				Product: "MyTool",
				Copyright: "© Acme",
				Trademark: "Acme™",
				Configuration: "Release",
				Version: "1.2.0",            ; AssemblyVersion + FileVersion; visible in Explorer properties
				Icon: "assets/app.ico",      ; application presentation icon; also stamped into a compiled exe
				GuiTheme: "Dark",            ; presentation default applied before runtime UI and auto-execute errors
				ConsoleApp: false,           ; generated Windows executable subsystem
				HookMutexName: "MyMutex",    ; fixed hook-interoperability namespace
				DesktopEntry: "com.acme.MyTool", ; Linux desktop file, Wayland app_id and Taskbar identity
				Files: ["assets/*.wav"],     ; embedded into the compiled script; extracted with FileInstall
			}
			```
			+ The block uses normal object-literal syntax (comments, multi-line, trailing commas), but every value must be a compile-time constant: a quoted string, a number, `true`/`false`, scalar `.` concatenation, or an array. Concatenation spells booleans as `1`/`0`; unary minus accepts numeric constants only. Unknown keys, malformed values and missing `Icon`/`Files` paths are compile-time errors. `Version` has 2–4 decimal components, each from 0 through 65534.
			+ Any number of blocks may appear at the top level of the main module (an `#Include`d file is fine; a `#Module` or an imported module file is not). They are evaluated in source order, and a later occurrence of a key wins, including duplicates within one block. `Files: []` clears an earlier file list.
			+ The manifest is embedded in the compiled assembly as the JSON resource `Keysharp.App.json`, so a `.cks` or compiled exe can be inspected without executing it, and the runtime applies the keys before any tray icon, window or dialog exists — regardless of where in the file the block sits. An assembly without that resource simply has no manifest; a present but unreadable or malformed resource is a hard load error.
			+ On Linux, `DesktopEntry` names the installed desktop file with or without its `.desktop` suffix. It also becomes each Wayland window's `app_id` and the `Taskbar` launcher target, keeping the window, icon, badge and progress under one application identity. The standard `DESKTOP_ENTRY` environment variable overrides this declaration for packagers and launchers. If neither is present, the identity is `keysharp.desktop`.
			+ `Icon` and every `Files` entry must be relative to the main script's directory (the program include root), even when the `#App` block comes from an included file. This is the same root used by `FileInstall` in source mode. `\` and `/` are separators on every platform, while the manifest stores one canonical `/`-separated logical path and never the resolved build-machine path. `Files` patterns (`*`/`?`) expand in the file-name segment only. Files are embedded only for artifact-producing compile modes; source/in-memory execution retains and validates the logical list but `FileInstall` copies the live script-relative file.
			+ Standalone directives select compiler or execution policy. `#ErrorStdOut` changes diagnostic routing when the parser reaches it, while `#SingleInstance` selects launch policy and `#NoTrayIcon`/`#TrayIcon` form a source-ordered tray-state family whose startup values use the internal manifest transport.
		+ `#App Icon` is the application-wide icon for the artifact, windows and dialogs, and is the tray fallback. `#TrayIcon` selects the tray-specific startup state: bare `#TrayIcon` restores that fallback (or Keysharp's built-in default) and makes the tray visible. The file form is `#TrayIcon FileName [, IconNumber]`: `FileName` is relative to the main script's directory even when the directive comes from an included file, and the compiler embeds the selected icon as the tray default restored by `TraySetIcon("*")`, so the source file is not needed at run time. While that default is active, `A_IconFile` is blank and `A_IconNumber` is 1. With no selector, the first icon group is used; a positive integer selects a 1-based icon group, a negative integer selects the absolute resource ID, and a nonnumeric quoted string selects a named resource from a managed .NET assembly. Numeric icon selection from a native module is Windows-only.
			+ `#TrayIcon` and `#NoTrayIcon` are applied in source order. A later `#TrayIcon`, with or without a file, cancels an earlier `#NoTrayIcon`; a later `#NoTrayIcon` hides an icon selected earlier.
	+ Command line switches may start with `/`, `-` or `--`, and must appear before the script or assembly input. After the input is found, all remaining arguments are passed to the script or assembly entry point. The exception is `--compile asm`, which runs nothing: further script paths there are read as additional scripts to compile.
	+ Started with no input at all, Keysharp looks for a script named after its own executable — `Keysharp.ahk`, `Keysharp.ks`, then `Keysharp.cks` — first in the working directory and then beside the executable. Every package ships the Dash as `Keysharp.cks` at the install root, so that probe is what opens it on a bare launch; a script of your own placed alongside takes precedence.
	+ Command line switches
		- `--script`
		  Causes a compiled script to ignore its main code and instead executes the provided script. For this to apply, `--script` must be the first command line argument.
		  Example: `CompiledScript.exe /script /ErrorStdOut MyScript.ahk "Script's arg 1"`
		- `--version`, `-v`
		  Displays Keysharp version.
		- `--transpile`
		  Outputs the generated .cs file shown in Keyview without running the script. A script using `#CSharp` also gets a `Scriptname.inline.cs` tooling view of its inline units.
		- `--compile exe [--dest <path>] <script>`
		  Outputs a standalone .exe that still requires .NET 10. `--dest` accepts a file or folder. Package files are copied beside the output. The script is not run.
		- `--compile exe-min [--dest <path>] <script>`
		  Like `exe`, but embeds package files in Scriptname.dll. The script is not run.
		- `--compile <script>`
		  Outputs a `.cks` assembly and its package assets. The script is not run.
		- `--compile asm [--dest <path|*>] <script>`
		  Like `--compile`, with explicit file, folder or `*` output. `dll` is an alias for `asm`. The script is not run.
		- `--compile asm <script> <script>...`
		  Compiles several scripts in one run, each to a `.cks` beside its own source. They share one warm parser and compiler, so this is much faster than one invocation per script. `--dest` and `--transpile` each name a single output and so take a single script.
		- `--validate`, `/validate`
		  Compiles but does not run the script. Can be used to check for load-time errors.
		- `--validate-syntax`
		  Parses without lowering, compiling, or loading Roslyn. `--syntax-only` and `--parse-only` are aliases.
		- `--with-parser`, `--with-compiler`, `--with-component <parser|compiler>`
		  Includes the selected optional first-party deployment unit in a `.cks` or executable. Compiler use by `Ks.RunScript` or `Ks.CompileScript`, and parser use by `Ks.ValidateScript`, is detected and included automatically. The generic form accepts unit IDs, not capability aliases.
		- `--without-parser`, `--without-compiler`, `--without-component <parser|compiler>`
		  Excludes a selected deployment unit, including an automatically detected compiler. This supports capability-gated code which remains usable when the compiler is intentionally absent.
		- `--asm`, `--assembly`
		  Reads pre-compiled assembly code from the file or StdIn and runs it. If omitted, the default type `Keysharp.CompiledMain.Program` and method `Main` are used. A custom entry point can be specified with `--asm:Namespace.Type.Method`, splitting the type and method at the last dot. A `.cks` or `.dll` input is treated as an assembly even when `--asm` is omitted.
		  Examples: `Keysharp.exe --asm Script.cks arg1 arg2`, `Keysharp.exe Script.cks arg1 arg2`, `Keysharp.exe --asm:My.Namespace.Type.Main Script.dll arg1 arg2`
		- `--daemon`, `--daemon stop`, `--daemon ping <script>`
		  Starts, stops, or diagnostics-checks the background compile daemon. Plain script runs and `--validate` use the daemon by default in release builds, but not in debug builds. Set `KEYSHARP_DAEMON=1` (or `true`, `yes`, `on`) to force daemon use, or `KEYSHARP_DAEMON=0` (or `false`, `no`, `off`) to bypass it. Only a compilation the daemon can be asked for is offloaded: it is sent a script path and nothing else, so any switch that changes what gets compiled (`--define`, `--include`, `--cpN`, the component switches) keeps the work in the calling process. `--errorstdout` does not, and so is allowed alongside `--validate`. The daemon never restores `#Package` dependencies; a run whose packages are missing is compiled again in the calling process, which fetches them, and `--validate` reports them as unrestored just as it does without a daemon.
* Gui specific:
	+ Miscellaneous behavior:
		+ When specifying colors for GUI components, the list of supported known colors can be found [here](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.knowncolor).
		+ New Gui option `+AutoScroll` shows scrollbars when a window's contents are larger than its client area.
		+ `Picture` supports clearing the picture by setting the `Value` property to empty.
		+ `UpDown` supports new options to relieve the caller of having to use native Windows API calls:
			+ `IncrementXXX` to specify an increment other than 1.
				+ `MyGui.Add("UpDown", "x5 y55 vMyNud Increment10", 1)`
			+ `Hex` to show the numeric value in hexadecimal.
		+ Gui controls support taking a boolean `Autosize` (default: `false`) argument in the `Add()` method to allow them to optimally size themselves.
		+ Loading icons from .NET DLLs is supported by passing the name of the icon resource in place of the icon number.
			+ To set the tray icon to the built in suspended icon:
				+ `TraySetIcon(A_KsCorePath, "Keysharp_s.ico")`
			+ To set a menu item to the same:
				+ `parentMenu.SetIcon("Menu caption", A_KsCorePath, "Keysharp_s.ico")`
		+ Rich text boxes are supported by passing `RichEdit` to `Gui.Add()`. The same options from `Edit` are supported with the following caveats:
			+ `Multiline` is `true` by default.
			+ `WantReturn` and `Password` are not supported.
			+ `Uppercase` and `Lowercase` are supported, but only for key presses, not for pasting.
			+ `Gui.Control.Value` gets/sets the displayed text only. The control's own members (below) reach everything else, and `Submit()` returns the plain text unless the control has `AltSubmit`, in which case it returns the RTF.
	+ New methods and properties:
		+ `Gui`:
			+ `Visible`: Gets/sets whether the window is visible or not.
		+ `Menu`:
			+ `HideItem()`, `ShowItem()` and `ToggleItemVis()`: Shows, hides or toggles the visibility of a specific menu item.
			+ `MenuItemName()`: Gets the name of a menu item, rather than having to use `DllCall()`.
			+ `SetForeColor()`: Sets the fore (text) color of a menu item.
			+ `MenuItemCount`: Gets the number of sub items within a menu.
		+ `ListView`:
			+ `DeleteCol(Column) => Boolean` Removes a column and returns `true` if the column was found and deleted, else `false`.
		+ `TabControl`:
			+ `SetTabIcon(TabIndex, ImageIndex)`: Relieves the caller of having to use `SendMessage()`.
		+ `TreeView`:
			+ `GetNode(ItemID) => TreeNode`: Retrieves a raw Winforms `TreeNode` object based on the passed in ID.
		+ `RichEdit`: the control returned by `Gui.Add("RichEdit", ...)` carries its own members, because a range of characters in it has a font, two colours and a paragraph of its own. Every character position is 1-based and indexes the same text `Value` returns, one character per line break — so a position computed with `InStr()` or `RegExMatch()` over `Value` can be handed straight to `SetFormat()`. A position of `0` means "the current selection" wherever a range is asked for.
			+ Content: `RichText` (the whole control as RTF), `SelectedText`, `SelectedRichText`, `TextLength`, `LineCount`, `Modified`, `ReadOnly`, `WordWrap`, `DetectUrls`, `HideSelection`, `Zoom`.
			+ Selection and caret: `SelectionStart`, `SelectionLength`, `CurrentLine`, `CurrentCol`, `FirstVisibleLine`, `Select(Start [, Length])`, `SelectAll()`, `ScrollCaret()`.
			+ Lines and positions: `GetLine(Line)`, `LineLength(Line)`, `LineFromPos(Pos)`, `PosFromLine(Line)`, `PosFromPoint(X, Y)`, `PointFromPos(Pos) => {X, Y}`.
			+ Editing: `CanUndo`, `CanRedo`, `Undo()`, `Redo()`, `ClearUndo()`, `Cut()`, `Copy()`, `Paste()`, `Append(Text)`, `Replace(Start, Length, Text)`, `Find(Needle [, Start, Options])` where the options are any of `MatchCase`, `WholeWord` and `Reverse`. `Find()` reports the 1-based position or `0` and leaves the selection alone.
			+ Formatting: `SetFormat(Start, Length [, Options, FontName])` and `GetFormat([Start, Length]) => Font`, plus `GetBackColor([Start, Length])` and the paragraph pair `SetParagraph(Start, Length, Options)` / `GetParagraph([Start, Length])`. The formatting options are `Gui.SetFont`'s, extended with `Background<colour>` and `BackgroundDefault`; a `Ks.Font` object is accepted in their place. Anything the options do not mention is left as it was, which is what lets a highlighter colour a token without also deciding its size or weight — and, read back, an attribute that is not the same throughout the range comes back as `""`, the same as an unset one on any other `Ks.Font`. The paragraph options are `Left`, `Center`, `Right`, `Indent<n>`, `HangingIndent<n>`, `RightIndent<n>`, `Bullet` and `-Bullet`; `GetParagraph` reads only the paragraph the range starts in and returns them as a string `SetParagraph` accepts.
			+ Batching: `BeginUpdate()` / `EndUpdate()` freeze the control and remember what was selected and scrolled to, so a re-highlight neither flickers nor drags the caret across the document. Pairs nest. **Wrap a whole highlighting pass in one**: on Windows it is worth roughly 30x, since without it each formatted range repaints.
			+ Files: `LoadFile(Path [, Format])` and `SaveFile(Path [, Format])`, where the format is `"RTF"`, `"Text"`, or omitted to go by the file's extension.
			+ Events: `Change` (as `Edit` has, and formatting does not raise it), plus `SelectionChange(ctrl, start, length)` and `LinkClick(ctrl, text, start, length)`.
			+ Platform differences: only the Win32 control serves the whole surface. Off Windows there is no undo history (`CanUndo`/`CanRedo` are false and `Undo()`/`Redo()` do nothing), `DetectUrls` and `HideSelection` read back as false, `Zoom` scales the control's own font instead of magnifying, `GetFormat()` reports the formatting at the start of the range rather than detecting variation across it, and `SelectedRichText`, `SetParagraph`/`GetParagraph`, `PosFromPoint`/`PointFromPos` and `FirstVisibleLine` raise an error saying so. On Linux specifically, GTK's text widget knows nothing of RTF at all, so `RichText` and the RTF form of `LoadFile`/`SaveFile` raise as well; colours, fonts and styles are unaffected, and they are what syntax highlighting needs.
	+ New classes:
		+ `WinEvent`: Subscribes to window activation, existence, movement, minimize, restore, title and caret events across platforms.
			+ Import it with `#import KS { WinEvent }`. `WinEvent(WinTitle?, WinText?, ExcludeTitle?, ExcludeText?)` returns an idle hook using standard window criteria and the title-matching settings in effect at construction.
			+ Assign `OnActive`, `OnNotActive`, `OnExist`, `OnNotExist`, `OnMove`, `OnMinimize`, `OnRestore`, `OnTitleChange` or `OnCaretMove`, then call `Start()`. Each callback receives `(Hook, Hwnd, Time)` and sets the Last Found Window. Move and caret callbacks also set `A_EventInfo` to `{X, Y, Width, Height}`.
			+ Changing a slot while running restarts event capture with the new set. Clearing the last slot stops the hook. `Start()` records the initial window state without reporting it, so callbacks report subsequent changes only.
			+ A running `WinEvent` keeps the script persistent and ends on `Stop()`, owner-thread exit or script exit. `WinEvent.Hooks` returns a snapshot of all running instances.
			+ Windows uses `SetWinEventHook`; Linux uses keysharp-desktop plus AT-SPI for caret events; macOS uses Accessibility observers. Linux and macOS require their respective window-monitoring permission.
		+ `EventHook`: The common base of `WinEvent`, `MonitorHook`, `ClipboardHook`, `Audio.DeviceHook`, `Clr.EventSubscription` and `InputHook`.
			+ Import it with `#import KS { EventHook }`. It exposes `InProgress`, `EndReason`, `Start()` and `Stop()` and cannot be constructed directly. `WinEvent` and `InputHook` start idle; the other factories return started hooks.
			+ A stopped hook can be restarted with the same callback and filter. Its run belongs to the thread that called `Start()` and normally ends with that thread. `InputHook` retains its AutoHotkey callback-thread and lifetime rules.
			+ `EndReason` is `"Stopped"`, `"Exit"` or `"Failed"`; `InputHook` also retains its AutoHotkey end reasons. Stopping drops callbacks queued for that run, except `InputHook.OnEnd`.
			+ `WinEvent`, `ClipboardHook` and `InputHook` keep the script persistent. `MonitorHook`, `Audio.DeviceHook` and `Clr.EventSubscription` do not. Each host class exposes a `Hooks` snapshot of its running hooks.
		+ `Monitor`: One display, carrying the metadata and device control the AHK-compatible `MonitorGet*` functions do not expose — model, manufacturer, serial, a stable id, refresh rate, physical size, orientation, connection kind, and brightness / raw DDC-CI control.
			+ It is part of the `KS` module; import it with `#import KS { Monitor }`. It does not replace the AHK `MonitorGet*` functions, which are unchanged apart from now raising a `ValueError` on an out-of-range monitor index instead of silently substituting the primary (matching AutoHotkey v2). `Image.FromMonitor()` inherits that same validation.
			+ It is where the Keysharp-specific screen facts live: the primary monitor's work area is `Monitor.Primary.WorkArea`, its scale is `Monitor.Primary.Scale`, and the whole desktop is `Monitor.VirtualScreen` — each available for *any* monitor, not just the primary. The AHK-standard `A_ScreenWidth`, `A_ScreenHeight` and `A_ScreenDPI` remain global. Note `A_ScreenDPI` is **not** `Monitor.Dpi`: the former is the system's logical text-size DPI (normally 96), the latter is computed from the panel's physical size. Hoist the rect when reading more than one field — `wa := Monitor.Primary.WorkArea` once, then `wa.Width`/`wa.Height` — since each read re-enumerates the topology.
			+ A `Monitor` is a **snapshot** of the topology plus a **live** handle to the device: identity and geometry are read once when the object is created, so a loop over `Monitor.All` sees one consistent picture, while `Brightness` and the VCP methods always talk to the hardware at the moment they are called. `Refresh()` re-reads the snapshot in place and returns the same object, or `""` when that monitor is no longer attached.
			+ Metadata beyond plain geometry costs a native query, so it is resolved on the first property that needs it and then cached on the object; constructing a `Monitor`, or reading only its geometry, never pays for it. Any field the display does not report is `""` rather than a fabricated value.
			+ `Id` is derived from the panel's EDID identity and is the value to persist (for example, to restore a window layout per monitor set); pass it back to `Monitor.FromId()`. Panels that report no serial are disambiguated by connector on Windows and Linux, making the id stable per *port* rather than per panel; on macOS such a panel falls back to a per-*model* id that two identical displays would share.
				```
				class Monitor
				{
					static Call([n]) => Monitor    ; By 1-based index, matching MonitorGet's numbering; omitted = the primary monitor.
					static Count => Integer        ; The number of monitors.
					static Primary => Monitor
					static All => Array            ; Every monitor, in index order, from ONE topology enumeration.
					static VirtualScreen => Object ; The union of every monitor: { X, Y, Width, Height }. The origin is negative when a display sits left of/above the primary.
					static FromPoint(x, y) => Monitor    ; The monitor containing a native screen point, or the nearest one when it falls in a gap.
					static FromMouse() => Monitor
					static FromWindow([winTitle, winText, excludeTitle, excludeText]) => Monitor  ; The monitor a window overlaps most.
					static FromId(id) => Monitor   ; The monitor whose Id matches, or "" when it is not attached.
					static OnChange(callback) => MonitorHook  ; Display-configuration changes; see below.
					static Hooks => Array          ; Every running OnChange hook, in start order. A snapshot.

					; Identity
					Index => Integer             ; 1-based, matching MonitorGet's numbering.
					Name => String               ; The OS name: "\\.\DISPLAY1", "DP-1", or the localized name on macOS.
					Model, Manufacturer, Serial  ; EDID panel identity, or "".
					Id => String                 ; Stable across reboot and re-plug; the value to persist.
					Adapter => String            ; The graphics adapter driving this monitor, or "".
					Connection => String         ; "HDMI" | "DisplayPort" | "eDP" | "DVI" | "VGA" | "Internal" | "".
					IsPrimary => Boolean
					IsInternal => Boolean        ; A built-in laptop/all-in-one panel rather than an external monitor.

					; Geometry, in the same native screen coordinates MonitorGet reports
					X, Y, Width, Height => Integer
					Bounds, WorkArea => Object   ; { X, Y, Width, Height }
					Scale => Float               ; Authored-size scale; 1.0 is 100%. Scales dimensions, never absolute positions.
					Dpi                          ; Derived from the physical size, in the same units as W/H; "" if unknown.
					PhysicalWidth, PhysicalHeight ; Millimetres, or "".
					RefreshRate => Float         ; Hz — 59.94, not 59 — or "".
					Orientation => Integer       ; Clockwise rotation of the desktop content: 0, 90, 180 or 270.
					Refresh() => Monitor         ; Re-read in place; returns this object, or "" if the monitor is gone.

					; Device control — each is a real hardware transaction, deliberately not cached
					Brightness => Integer        ; Get/set, 0-100. OSError naming the reason where unsupported.
					IsBrightnessSupported => Boolean ; A real probe of the device, so it costs one brightness read.
					GetVCP(Code) => Object       ; Raw DDC/CI (MCCS) feature => { Current, Maximum }. Code 0-255.
					SetVCP(Code, Value)          ; Code 0-255, Value 0-65535. See the warning below.
				}
				```
			+ `Monitor.OnChange(Callback)` returns a started `MonitorHook`, an `EventHook` that does not keep the script running. The callback receives `(Hook, Change)`, where `Change` is `"Topology"` when the set of attached monitors changed (plug/unplug, dock/undock) and `"Settings"` when the same monitors are attached but their resolution, position, scale or primary assignment changed; `Monitor.Count` gives the number attached after the change. Both kinds are reported on every platform, and a notification that changes nothing is dropped. A handler holding a `Monitor` should call its `Refresh()` — which returns falsy if that is the display that was just unplugged — or simply re-read `Monitor.All`.
			+ **`SetVCP()` is a foot-gun.** Writing an input-source or power code will switch the monitor away from this computer, and a few displays react badly to codes they document but mishandle; verify a code against the monitor's own MCCS documentation first. The feature is read back before it is written, so a code the monitor does not implement raises an `OSError` rather than reporting a success the display silently ignored.
			+ Platform support for `Monitor`:
				+ Windows: Metadata from the DisplayConfig API plus the EDID Windows caches under the monitor's registry key; brightness through WMI for the built-in panel and DDC/CI (dxva2) for external monitors.
				+ Linux: EDID from `/sys/class/drm/*/edid`, identical under X11 and Wayland; refresh rate and rotation from XRandR or `wl_output`. Brightness uses the kernel backlight class for the built-in panel (a direct sysfs write where permitted, else logind's `SetBrightness`) and DDC/CI over `/dev/i2c-*` for external monitors, which needs `i2c-dev` loaded and access to the bus — the packaged udev rule grants it, and the error message names the fix when it is missing.
				+ macOS: CoreGraphics answers identity, physical size, rotation and refresh rate directly, so no EDID parsing is involved; `Model` and `Adapter` are always `""` because CoreGraphics exposes no product-name or adapter API, and `Connection` only distinguishes the built-in panel. Brightness uses DisplayServices for the built-in panel and Apple's own displays, and DDC/CI for every other external monitor.
				+ Display-change events come from `SystemEvents.DisplaySettingsChanged` on Windows, GDK's monitor signals on Linux (covering X11 and Wayland alike), and `CGDisplayRegisterReconfigurationCallback` on macOS.

### Removals
* Removed/reduced functions:
	+ `Download()`: Supports only the `*0` option; any other numerical value raises a `ValueError`. `http`, `https` and `ftp` URLs are supported; a `gopher` URL raises. An HTTP status outside 2xx saves whatever the server sent, as in AutoHotkey. An FTP directory URL saves the server's own plain-text `LIST` output, where WinInet writes an HTML listing; a path that is neither a file nor a directory raises rather than leaving an empty file.
	+ `ListLines()`: Non-functional because C# doesn't support it.
	+ `FormatTime(YYYYMMDDHH24MISS?, Format?)`: The `R`, `Dn` or `Tn` options in `YYYYMMDDHH24MISS` are not supported, except for 0x80000000 to disallow user overrides.
		+ Specify a particular format or order with `Format`.
		+ [Here](https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings) is a list of the C# style `DateTime` formatters which are supported.
	+ `ObjAddRef()` and `ObjPtrAddRef()` do not have an effect for non-COM objects. Instead, use the following:
		+ `newref := theobj ; adds 1 to the reference count`
		+ `newref := "" ; subtracts 1 from the reference count`
	+ When passing `"Interrupt"` as the first argument to `Thread()`, the third argument for `LineCount` is not supported because Keysharp does not support line level awareness.
	* Syntax:
	+ The address of a variable cannot be taken using the reference operator.
		+ It returns a VarRef object as in AutoHotkey. Its read-only `Name` property contains the referenced variable or property name when one is available.
* Miscellaneous behavior:
	+ Pausing the whole *script* is not supported, because a Keysharp script is actually a running program.
		+ The tray menu's Pause item has been removed. `Pause()` and `A_IsPaused` remain and act on pseudo-threads, as in AHK: `Pause()` suspends the calling pseudo-thread until something clears its flag, and `Pause(1|0|-1)` sets, clears or toggles the flag on the *underlying* thread, which observes it when it resumes.
		+ A paused thread keeps pumping, so hotkeys and work posted from another real thread still run and can unpause it — but timers are suspended for the duration, matching AHK. An `ExitApp` releases it.
	+ The `/script` command line switch for compiled scripts does not apply and is therefore not implemented.
	* The `/Debug` command line switch is not implemented.
	+ The Help menu item is not implemented yet.
* Gui specific:
	+ Miscellaneous behavior:
		+ Static text controls do not send the Windows `API WM_CTLCOLORSTATIC (0x0138)` message to their parent controls like they do in AutoHotkey.
		+ Tooltips do not automatically disappear when clicking on them.
		+ Double click handlers for buttons are not supported.
		+ UpDown controls with paired buddy controls are not supported. Keysharp just uses the regular NumericUpDown control in C#.
			+ The options `16`, `Horz` and `Wrap` have no effect.
			+ The min and max values cannot be swapped.
		+ For slider events, the second parameter passed to the event handler will always be `0` because it's not possible to retrieve the method by which the slider was moved in C#.
		+ Only `Tab3` is supported, no older tab functionality is present.
		+ When adding a `ListView`, the `Count` option is not supported because C# can't preallocate memory for a `ListView`.
	+ Removed/reduced functions:
		+ `IL_Create()` only takes one parameter: `LargeIcons`. `InitialCount` and `GrowCount` are no longer needed because memory is handled internally.
		+ `LoadPicture()` does not accept a `GDI+` argument as an option.
		+ `PixelGetColor()` ignores the `Mode` parameter.
		+ `DirSelect()`:
			+ The `1`, `3` and `5` options don't apply and the New Folder button will always be shown.
			+ Modality cannot be configured with `Gui.Opt("+OwnDialogs")` because the folder select dialog is always modal.
			+ Restricting folder navigation is not supported.
		+ `MsgBox()`:
			+ The modality options are ignored.
			+ The message box will block the window that launched it by default. If `+OwnDialogs` is in effect, then all GUIs in the script are blocked until it is dismissed.
			+ System modal dialog boxes are no longer supported on Windows.
			+ The help option `16384` is ignored.
		+ `OnMessage()` doesn't observe any of the behavior mentioned in the documentation regarding the message check interval because it's implemented in a different way.
			+ A GUI object is required for `OnMessage()` to be used.
			+ A posted message numbered above `0x0311` which arrives while the script is uninterruptible is processed first and its callbacks are called once a thread can start, so a non-empty return only stops the remaining callbacks. AutoHotkey leaves such a message in the queue until then, so a callback can still claim it.
			+ Off Windows there is no native message queue to monitor, so the input messages are synthesized from the toolkit events the script's own GUI raises: `WM_MOUSEMOVE`, the left/right/middle button down, up and double-click messages, `WM_MOUSEWHEEL`, `WM_KEYDOWN`/`WM_KEYUP`, `WM_SYSKEYDOWN`/`WM_SYSKEYUP` and `WM_CHAR`. They carry the same payloads as on Windows — the control's handle, `MK_*` flags and packed coordinates in `wParam`/`lParam`, a virtual key code for the key messages — and the callback's last-found window is the GUI the control belongs to. Any other message number is never delivered, messages sent to windows the script does not own cannot be observed at all, and a click on a single-line `Edit` is missed because GTK's entry consumes the button press before the toolkit raises an event for it. `Gui.OnMessage()` and `GuiCtrl.OnMessage()` are fed from the same source after the global monitors, and are addressed the way Windows addresses them: a message that went to the GUI window reaches the former, one that went to a control reaches the latter. This is verified on X11 and Wayland; macOS runs the same code but is untested.
		

### HMAC, Base32 and hexadecimal conversion

`#Import Ks { Crypt, Base32, Hex }` exposes the Keysharp extensions `Crypt.Hmac(Value, Key, Algorithm := "SHA256", Encoding := "UTF-8")`, `Base32.Encode(Value, Encoding := "UTF-8")` and `Base32.Decode(Text)`.

HMAC accepts `SHA1`, `SHA256`, `SHA384` and `SHA512` with the same case-insensitive, hyphen-tolerant names as `Hash`; `MD5` and `CRC32` raise `ValueError`. Its result is uppercase hexadecimal, matching `Hash`. Use `Hex.Decode()` to convert a digest to raw bytes in a Buffer. String and `StringBuffer` inputs use the chosen encoding; Buffers retain their bytes. A File is accepted as `Value` with the same streaming and position-preservation rules as `Hash`, but cannot serve as `Key`.

`Hex.Encode(Value, Encoding := "UTF-8")` accepts the same input types as Base32 and Base64 and returns uppercase hexadecimal without separators or a prefix. `Hex.Decode(Text)` accepts an even number of hexadecimal digits in either case and returns a Buffer, including an empty Buffer for empty text. Whitespace, `0x` prefixes, odd lengths and other invalid characters raise `ValueError`. Text can be read from the decoded bytes with `StrGet(Bytes, Bytes.Size, "UTF-8")`.

Base32 encodes with the RFC 4648 alphabet and `=` padding. Decode accepts lowercase and unpadded secrets, returns a Buffer, and rejects invalid characters (including whitespace), malformed padding, impossible lengths and nonzero unused bits with `ValueError`. The RFC 4648, RFC 2202, RFC 4231 and RFC 6238 script vectors are verified on Windows; Linux and macOS remain unverified. The user-facing Crypt reference includes a TOTP example that uses `Hex.Decode(Crypt.Hmac(...))` for dynamic truncation.

### ListView row and cell colours

`Gui.ListView.SetRowColor(Row, TextColor?, BackColor?)`, `SetCellColor(Row, Column, TextColor?, BackColor?)` and `ClearColors()` are Keysharp extensions. Setters accept the existing GUI colour names, hexadecimal strings and RGB integers. Rows and columns are existing 1-based indices. An omitted, empty or `"Default"` colour clears that component's override; a cell inherits the row component and then the control default. An invalid index or malformed colour string raises `ValueError` without applying either colour. All three methods return the control.

Colours are indexed assignments: every successful `Insert`, `Delete`, `InsertCol`, `DeleteCol` and sort clears all row and cell colours. An unsorted `Add` or ordinary `Modify` retains them; adding into a sorted control can invalidate them through sorting. Reapply assignments after changing row or column structure. WinForms uses native row/subitem colours without owner drawing; Eto uses cell formatting. Selected-row rendering follows the platform's selection theme. Windows native colour properties and invalidation are verified without showing a window; Linux/macOS rendering remains unverified.

## Code acknowledgements

* The initial IronAHK developers 2010 - 2015
* [Cross platform INI file processor](https://www.codeproject.com/articles/20053/a-complete-win-ini-file-utility-class)
* [Eto.Forms](https://github.com/picoe/Eto)
* [Logical string comparison](https://www.codeproject.com/Articles/22175/Sorting-Strings-for-Humans-with-IComparer), [cddl 1.0](https://opensource.org/licenses/cddl1.php)
* [NAudio](https://github.com/naudio/NAudio)
* [P/Invoke calls](https://www.pinvoke.net)
* [PictureBox derivation](https://www.codeproject.com/articles/717312/pixelbox-a-picturebox-with-configurable-interpolat)
* [Program icon](https://thenounproject.com/icon/mechanical-keyboard-switch-2987081/) is a derivative of work by [Bamicon](https://thenounproject.com/bamicon/)
* [Scintilla editor for .NET](https://github.com/desjarlais/Scintilla.NET)
* [Scintilla setup code in Keyview](https://github.com/robinrodricks/ScintillaNET.Demo)
* [Semver version parsing](https://github.com/WalkerCodeRanger/semver)
* [Using SendMessage() with string](https://gist.github.com/BoyCook/5075907)
* Various posts on [Stack Overflow](https://stackoverflow.com/)

## Contributing and Support

Please use the [issue tracker](https://github.com/keysharp-org/Keysharp/issues) for bug reports, compatibility gaps, and feature requests.
