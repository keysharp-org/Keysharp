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
* If .NET 10 is not installed on your machine, download it from the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

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
* Download the latest version of [Visual Studio 2022](https://visualstudio.microsoft.com/vs/community/).
	+ This should install .NET 10. If it doesn't, you need to install it manually from the link above.
* Open Keysharp.sln
* Build all (building the installer is not necessary).
* CD to bin\release\net10.0-windows (or \debug\, depending whether using Debug or Release mode)
* Run `.\Keysharp.exe yourtestfile.ahk`

To build the release MSI installer and portable ZIP, run `Keysharp.Install\package-windows.ps1` from PowerShell. Packaging output is written to `dist\`.

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

| Platform / compositor | Without root access | Root-access helpers add / enable | Notes |
|---|---|---|---|
| **X11** | Full window management and screen capture; partial input hooks with X11, input synthesis, and hotkeys/hotstrings | Full input hooks/synthesis, `BlockInput`, reliable hotkeys/hotstrings via `keysharp-inputd` | Root mainly upgrades input control |
| **Wayland – GNOME** | Full window management and mouse synthesis via GNOME Shell extension | Keyboard synthesis, hooks, `BlockInput`, hotkeys/hotstrings via `keysharp-inputd`; screen capture permission via `keysharp-helper` | Shell extension must be enabled which requires a logout after install; `keysharp-helper` must be installed for capture authorization |
| **Wayland – Cinnamon** | Full window management, mouse synthesis, and push window events via Cinnamon extension, with Eval fallback when the extension is absent | Keyboard synthesis, hooks, `BlockInput`, hotkeys/hotstrings via `keysharp-inputd` | Installer enables and asks a running Cinnamon session to load/reload the extension; manual installs may still need a Cinnamon restart or logout |
| **Wayland – KWin / KDE Plasma** | Full window management via KWin scripting; mouse synthesis via FakeInput | Screen capture via `keysharp-helper`; keyboard synthesis, hooks, `BlockInput`, hotkeys/hotstrings via `keysharp-inputd` | `keysharp-helper` must be root-owned setuid with desktop file |
| **Wayland – other compositors**<br>Sway, Hyprland, COSMIC, Wayfire, labwc, etc. | Protocol-dependent window listing, active-window detection, activation, and screen capture | Input synthesis, hooks, `BlockInput`, hotkeys/hotstrings via `keysharp-inputd` | Depends on foreign-toplevel and screencopy protocol support |

### Installing on Linux
* Download and extract the Keysharp installer tarball from the [Releases](https://github.com/keysharp-org/Keysharp/releases) page.
+ Either run the .deb file to install, or run the install.sh script with sudo: `sudo bash ./install.sh` which does the following:
	+ Installs the Linux runtime dependencies and attempts to install the .NET 10 runtime if it is missing.
		+ If your distribution does not provide the .NET 10 runtime package, install it manually using the instructions [here](https://learn.microsoft.com/en-us/dotnet/core/install/linux).
	+ Registers Keysharp as the default program to open `.ks` and `.cks` files. So after installing, double click any `.ks` source script or `.cks` compiled script to run it.
	+ Creates a symlink at `/usr/local/bin/keysharp` so you can run it from the command line from anywhere.
	+ Installs root-owned `keysharp-inputd` daemon for evdev device access and a uinput virtual device, enabling reliable keyboard/mouse hooks, input synthesis, and `BlockInput` on both X11 and Wayland.
	+ Installs root-owned setuid `keysharp-helper`, the screen-capture trust gate: it prompts for and remembers the user's screen-capture consent, then performs or authorizes the grab. On KWin it does the `ScreenShot2` capture directly; on GNOME/Cinnamon it is the only caller the shell extensions accept for capture; on X11 and other Wayland compositors it prompts for consent before Keysharp's own grab (consent/awareness only there, since capture cannot be enforced). Without it (no root install) capture proceeds ungated as before.
	+ Installs a GNOME Shell extension (`keysharp@keysharp.io`) for the invoking desktop user, which is required in GNOME for screen capture, mouse location queries, and window automation. This requires a logout or reboot to take effect. If GNOME is not installed then this has no effect.
	+ Installs a Cinnamon extension (`keysharp@keysharp.io`) for the invoking desktop user, which improves Cinnamon Wayland window inventory, window events, window actions, and mouse synthesis. If Cinnamon is running, the installer asks it to load or reload the extension immediately.
+ Without sudo, Keysharp is installed under `$HOME/.local` and the privileged helpers are skipped. Linux input hooks/synthesis and Wayland screen capture will be unavailable until a root install is performed.

For thqby's **AutoHotkey v2 Language Support** VS Code extension, create an `AutoHotkey.exe` compatibility symlink because the extension requires that filename:
```sh
mkdir -p ~/.local/bin
ln -sf "$(command -v keysharp)" ~/.local/bin/AutoHotkey.exe
```
Then use `/home/YOUR_USERNAME/.local/bin/AutoHotkey.exe` as the interpreter path in the extension. The extension is designed for AutoHotkey on Windows, so static language features and running scripts are the most compatible features; Windows-specific debugging, help, and compiler integration will not work.

### Building from source on Linux
* Install the .NET 10 SDK (not just the runtime) as described in "Installing on Linux"
* In the same parent folder as keysharp, clone the Keysharp branch of [the Keysharp fork of Eto](https://github.com/keysharp-org/Eto/tree/Keysharp); if keysharp is at `foo/keysharp`, clone Eto to `foo/Eto` by running `git clone -b Keysharp https://github.com/keysharp-org/Eto.git` from within `foo`.
* Run `Keysharp.Install/package-linux.sh`
* A build folder and a tarball of said build folder will be placed in `dist/keysharp-linux-x64` and `dist/keysharp-linux-x64.tar.gz` respectively. If `dpkg-deb` is installed, a Debian package such as `dist/keysharp_<version>_amd64.deb` will also be created.
* The build folder and tarball can be installed via the steps in "Installing on Linux" above. The `.deb` can be installed with `sudo apt install ./dist/keysharp_<version>_amd64.deb`.
* The folder and tarball are portable so both source repositories can be safely deleted.
* **Alternatively**, on arch-based systems keysharp is provided as an [AUR package](https://aur.archlinux.org/packages/keysharp-git)

## macOS Platform Support
macOS support is in active development. The following table summarises what works and what requires user action.

| Feature | Status | Notes |
|---|---|---|
| Script execution | Working | Parser, compiler, and runtime are functional |
| Hotkeys / Hotstrings | Working | Requires **Input Monitoring** permission on first use. #/Win maps to the Command key, !/Alt maps to the Option key |
| Keyboard & mouse send | Working | Requires **Accessibility** permission on first use |
| Global keyboard/mouse hooks | Working | Requires **Input Monitoring** permission on first use |
| Cursor confinement | Partial | `ClipCursor` suppresses out-of-bounds movement; requires **Input Monitoring** and **Accessibility** permissions |
| GUI windows | Working | Eto.Forms backend; some controls differ from Windows |
| Screen capture / pixel functions | Working | Requires **Screen Recording** permission on first use |
| Monitor brightness / DDC-CI | Partial | Built-in panel (and Apple's own displays) via DisplayServices; other external monitors over DDC/CI. No permission needed. Apple Silicon only — the Intel path is implemented but untested, and some USB-C hubs and docks do not carry the DDC channel. `Monitor.GetVCP()`/`SetVCP()` work on external monitors only, as a built-in panel has no DDC/CI connection |
| Window management | Partial | Accessibility API; foreign-app control requires permission |
| Registry APIs | Not supported | Windows-only |
| COM APIs | Not supported | Windows-only |

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
| **Accessibility** | Controlling and querying other application windows |
| **Screen Recording** | `PixelGetColor`, `ImageSearch`, `Image` |

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
| Hotkeys/Hotstrings | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Depends on hook and key-state parity. |
| Script-owned window management | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Creating and driving the script's own GUI windows. Built on WinForms (Windows) and Eto (Linux/macOS); the object model, events, controls, menus, ListView and TreeView all behave the same. Remaining differences: the ActiveX and Custom control types are Win32-only, ListView supports only the Report view off Windows, raw Win32 style options are ignored, per-monitor DPI re-layout is Windows-only, and a client cannot position its own window on Wayland without a compositor backend. |
| Foreign window management (non-Keysharp apps) | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | On Linux, Control* functions are not supported for foreign apps; use the included AtSpi library for cross-process window/control interaction. macOS currently relies on Accessibility APIs with permission requirements. |
| Tray icon and menu | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Tray icon, its menu and TrayTip notifications. On Linux the tray depends on the desktop providing a StatusNotifier/AppIndicator host - some environments need an extension before an icon appears at all - and notifications go through the desktop notification service. macOS uses a status item in the menu bar. |
| Screen capture and pixel/image functions | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Pixel/image search and screen capture depend on platform-specific backends. |
| Clipboard | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Text, image, URI, custom MIME, wait, and change-notification operations use the native platform clipboard backends. See ClipboardAll() for the Wayland multi-format restore limitation. |
| Sound APIs | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Audio device/endpoint support differs by platform. |
| Registry APIs | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Windows Registry APIs are Windows-only. |
| COM APIs | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | COM is available on Windows only. |
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

## Differences

### Behaviors and Functionality
* Linux support is partial. See [Linux Platform Support](#linux-platform-support) above for a detailed breakdown by display server and compositor.
	+ Control commands only work on windows created by the running Keysharp process. This is because "controls" don't exist in Linux the same way they do in Windows.
		+ As an alternative it's recommended to use [AtSpi.ks](https://github.com/keysharp-org/Keysharp/blob/master/Keysharp/Scripts/AtSpi.ks): running it directly displays AtSpiViewer which can be used to inspect windows, and it also contains methods to manipulate windows and controls similarly to Acc/UIA in Windows.
	+ GUI support is mostly implemented, but some controls are missing or incomplete.
	+ Registry and COM functions are not supported.
* Keysharp follows the .NET memory model.
	+ There is no variable caching with strings vs numbers. All variables are C# objects.
	+ Values not stored in variables are like regular variables, only eligible to be freed once they go out of scope.
		```
		FileOpen("test.txt", "w").Write("hello") ; The temporary file object does not get deleted at the end of the line, only possibly at the end of the current scope.
		```
	+ Object destructors/finalizers are called at a random point in time, and `Collect()` should be used if they need to be invoked predictably.
	+ Object destructors (`__Delete()`) are implemented with C# finalizers, which are quite heavy-weight and are not automatically present for all objects. The finalizer state is determined at object creation based on whether `__Delete()` is present in the prototype chain, or at the point `__Delete()` is defined. If `__Delete()` is defined later in the prototype chain then instance finalizers are not automatically activated; the activation can be forced manually by temporarily reassigning a different base for the instance.
	+ On script exit all non-local variables are enumerated, finalizers disabled, and `__Delete()` called if present. This also includes class static variables.
* AutoHotkey says about the inc/dec ++/-- operators on empty variables: "Due to backward compatibility, the operators ++ and -- treat blank variables as zero, but only when they are alone on a line".
	+ Keysharp breaks this and will instead create a variable, initialize it to zero, then increment it.
	+ For example, a file with nothing but the line `x++` in it, will end with a variable named x which has the value of 1.
* The concat-assign operator `.=` is not optimized to modify the left operand inplace, meaning calling it in a loop will be very slow. If many concats are required then use a `StringBuffer` instead.
* Function objects behave mostly the same as in AutoHotkey.
	+ The underlying function object class is named `KeysharpFunc`, instead of `Func`, because C# already contains a built in class named `Func`.
		+ Scripts only ever use the AutoHotkey name: `MsgBox is Func` works, `MsgBox is KeysharpFunc` does not.
	+ Function objects can be created by passing the name of the function as a direct reference or as a string to `Func()`.
	+ Most built-in functions can also be used as function objects.
* The `File` object is internally named `KeysharpFile` so that it doesn't conflict with `System.IO.File`. As with `Func` and `Object`, only the AutoHotkey name is usable from a script; the internal name appears solely in low-level diagnostics such as stack traces.
* Error stack traces start from where the error was thrown, not where it was constructed.
* `Map` internally uses a real hashmap, which means item access, insertions and removals are faster, which is especially true for larger datasets. To keep at least partial compatibility with AutoHotkey the `Map` object is copied and sorted before enumeration, which means modifying the `Map` during enumeration will not have the same effect as in AutoHotkey.
* `AddStandard()` detects menu items by string, instead of ID, because WinForms doesn't expose the ID.
* `CallbackCreate()` does not support the `CDecl/C` option because the program will be run in 64-bit mode.
	+ Passing string pointers to `DllCall()` when passing a created callback is recommended against. See explanation above under `StrPtr()`.
	+ Usage of the created callback will be inefficient, so usage of `CallbackCreate()` is discouraged.
* `ControlMove()` and `ControlSetPos()` operate relative to their immediate parent, which may not be the main window if they are contained in a nested control.
* `DirCopy()` extracts archives with .NET rather than the OS shell, so the supported formats are the same on every platform: `.zip`, `.tar`, `.tar.gz` and `.tgz` are extracted into *Dest* as a folder. AutoHotkey's format list instead depends on the Windows version (and RAR/7z are not supported at all here).
	+ A plain `.gz` holds a single compressed file rather than an archive of entries, so *Dest* names the decompressed **file** and its parent folder is created if needed. This is the one case where *Dest* is not a directory.
* `DllCall()` has the following caveats:
	+ Use `Ptr` and `StringBuffer` for double pointer parameters such as `LPTSTR*`. This is recommended over the use of `StrPtr()`.
* `ObjPtr()` returns an IUnknown `ComValue` with the pointer wrapped in it, whereas `ObjPtrAddRef()` returns a raw pointer.
* `SetTimer()` uses a in the range 0-4, not -2147483648 and 2147483647.
* Encoding names — wherever one is accepted: `FileEncoding`, `A_FileEncoding`, `FileRead`, `FileOpen`, `File.Encoding`, `StrGet`, `StrPut`, `Base64Encode` and the `Crypt` class — take AutoHotkey's `UTF-8`, `UTF-8-RAW`, `UTF-16`, `UTF-16-RAW`, `CPnnn` and `nnn`, and additionally `ASCII` and any name .NET knows, such as `windows-1252`. A name which cannot be resolved raises a `ValueError`; it is never quietly substituted, since that would silently read or write the wrong bytes. An empty name means the native UTF-16 encoding, where AutoHotkey uses the active ANSI code page (CP0).
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
	+ GroupBoxes can be used as containers by calling `GuiObj.UseGroup(groupbox)`, and to exit the group call `GuiObj.UseGroup()`.
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
* The `#ErrorStdOut` directive will not print to the console unless piping is used. For example:
	+ `.\Keysharp.exe .\test.ahk | more`
	+ `.\Keysharp.exe .\test.ahk | more > out.txt`
* The `#ConsoleApp` directive is Keysharp-only, and is the equivalent of Ahk2Exe's `;@Ahk2Exe-ConsoleApp`. It makes `--compile exe` produce a console application rather than the default GUI one, which is what a command-line script needs:
	+ A shell waits for the program to exit and reports its exit code, and its standard streams are the terminal's, so `FileAppend(text, "*")` prints and `FileOpen("*", "r")` reads typed input without any redirection.
	+ On Windows this is the executable's PE subsystem field, which the shell reads before the process starts. Nothing done at runtime can substitute for it, which is why it is a build-time directive rather than a setting.
	+ Without it the executable stays a GUI one, so a double-clicked script never flashes a console window. That is also the trade-off: a console-subsystem executable launched from Explorer gets a console window of its own.
	+ It is ignored when the script is interpreted or compiled to a `.cks`, since neither writes an executable, and it is inert on Linux and macOS, where executables have no subsystem and a shell always waits.
* If a script is compiled then none of Keysharp or AutoHotkey command parameters apply.

### Syntax
* AutoHotkey `unset` is implemented as `null`. `IsSet(x)` is equivalent to `x == null`.
* Use of the dereference syntax `%expression%` inside functions is highly discouraged. This is because using it will cause every function call to construct an object which captures all local variables, and depending on the number of variables the performance loss may be significant.
* `Goto` statements cannot use any type of variable. They must be labels known at compile time and function just like goto statements in C#.
* `Goto` statements being called as a function like `Goto("Label")` are not supported. Instead, just use `goto Label`.
* The `#Requires` directive differs in the following ways:
	+ In addition to supporting `AutoHotkey`, it also supports `Keysharp`.
	+ Sub versions such as -alpha and -beta are not supported. Only the four numerical values values contained in the assembly version in the form of `0.0.0.0` are supported.
	+ A new `capability` form requests one or more platform permissions at script startup, before hotkeys are registered, so the user sees a single combined prompt rather than separate prompts on first use:
		```
		#Requires capability InputMonitoring, ScreenCapture
		```
		Recognised capability names (case-insensitive, aliases accepted):
		| Name | Aliases | Description |
		|---|---|---|
		| `InputMonitoring` | `hook`, `inputhook` | Monitor keyboard and mouse input (required for hotkeys/hotstrings) |
		| `InputInjection` | `synthinput`, `sendinput` | Synthesize keyboard and mouse input (`Send`, `Click`, etc.) |
		| `BlockInput` | | Suppress input events |
		| `ScreenCapture` | `capture`, `imagecapture` | Capture screen pixels (`PixelGetColor`, `ImageSearch`, `Image`) |
		| `AccessibilityAutomation` | `accessibility`, `automation` | Access UI accessibility trees (AT-SPI on Linux) |
* For any `__Enum()` class method, it should have a parameter value of 2 when returning `Array` or `Map`, since their enumerators have two fields.
* RegEx uses PCRE2 engine powered by the PCRE.NET library. There are a few limitations compared to the AutoHotkey implementation:
	+ The following options are different:
		+ `S`: Studies the pattern to try improve its performance.
			+ This is not supported. All RegEx objects are internally created with the `PcreOptions.Compiled` option specified, so performance should be reasonable.
		+ `u`: This new option disables optimizations PCRE2_NO_AUTO_POSSESS, PCRE2_NO_START_OPTIMIZE, and PCRE2_NO_DOTSTAR_ANCHOR. This option can be useful when using callouts, since these optimizations might prevent some callouts from happening.
	+ Callouts differ in a few ways:
		+ The callout function cannot be a closure, it must be a top-level function.
		+ Callouts do not set `A_EventInfo`.
		+ The callout function must be a top-level function.
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
	+ `ImageSearch()` takes an options string as a fifth parameter, rather than inserted in the string before the `imageFile` parameter.
	+ `Log(number, base := 10)` is by default base 10, but it can accept a double as the second parameter to specify a custom base.
		+ In `SetTimer()`:
			+ In the callback function, `A_EventInfo` is set to the function object used to create the timer.
			+ This allows the handler to alter the timer by passing the function object back to another call to `SetTimer()`.
			+ Timers are not disabled when the program menu is shown.
	+ `Run/RunWait()` can take an extra string for the argument instead of appending it to the program name string. However, the original functionality still works too.
		+ The new signature is: `Run/RunWait(target [, workingDir, options, &outputVarPID, args])`.
	+ `SubStr()` uses a default of 1 for the second parameter, `startingPos`, to relieve the caller of always having to specify it.
* New miscellaneous functions:
	+ `Collect()`: Calls `GC.Collect()` to force a memory collection.
		+ This rarely ever has to be used in properly written code.
		+ Calling `Collect()` may not always have an immediate effect. For example if an object is assigned to a variable inside a function and then the variable is assigned an empty string then calling `Collect()` after it will not cause the object destructor to be called. Only after the function has returned will the object be considered to have no references and `Collect()` starts working.
		+ If an object destructor needs to be called immediately then it may better to call `Object.__Delete()` manually.
	+ `EnvUpdate()`: Retained from AutoHotkey v1 as a cross-platform environment notification mechanism. Windows broadcasts `WM_SETTINGCHANGE`; Linux publishes pending `EnvSet()` changes to the D-Bus activation environment and systemd user manager; macOS publishes them to the current launchd session. Linux and macOS updates affect future session-managed processes only and are not persistent.
	+ `FormatCs()`: An alternative to `Format()`. The syntax used in `FormatCs()` is exactly that of `string.Format()` in C#, except with 1-based indexing.
		+ Full documentation for the C# formatting rules can be found [here](https://learn.microsoft.com/en-us/dotnet/api/system.string.format).
	+ `LockRun(lockobj, funcobj [, params*])`: Calls `funcobj` inside of a lock on `lockobj`, optionally passing `params` to it.
		+ This is used to ensure only one `RealThread` at a time executes the code in `funcobj`.
		+ `lockobj` must be an object (`Object()` or a `Lock`). A number throws `TypeError`: it boxes afresh at each call site, so every call would lock a different monitor and nothing would be serialized. A string works but is a poor lock, because identical literals are shared process-wide.
			```
			lockit := Object()
			sharedvar := 0
			LockRun(lockit, () => sharedvar++) ; If this were called in multiple threads, sharedvar would only ever be accessed by one thread at a time.
			```
	+ `Mail(recipients, subject, message, options)`: Sends an email.
		+ `recipients`: A list of receivers of the message.
		+ `subject`: Subject of the message.
		+ `message`: Message body.
		+ `options`: A `Map` with any the following optional key/value pairs:
			+ "attachments": A string or `Array` of strings of file paths to send as attachments.
			+ "bcc": A string or `Array` of strings of blind carbon copy recipients.
			+ "cc": A string or `Array` of strings of carbon copy recipients.
			+ "from": A string of comma separated from address.
			+ "replyto": A string of comma separated reply address.
			+ "host": The SMTP client hostname and port string in the form "hostname:port".
			+ "header": A string of additional header information.
	+ `RandomSeed(Integer)`: Reinitializes the random number generator for the current thread with a specified numerical seed.
	+ `RequestCapabilities(capabilities*) => Object`: Requests one or more platform permissions and returns an object describing the outcome.
		+ `capabilities`: zero or more capability name strings, each optionally comma- or space-delimited. Recognised names are the same as for `#Requires capability` above.
		+ When called with no arguments, returns the current status of all capabilities without prompting.
		+ Returns an `Object` with a property for each capability (`"Granted"`, `"Denied"`, `"NotApplicable"`, or `"Unsupported"`) and a `Granted` property (`1`/`0`) indicating whether every *requested* capability was granted or not applicable.
		+ On Linux, all input-related capabilities (`InputMonitoring`, `InputInjection`, `BlockInput`) plus `ScreenCapture` are batched into a single `keysharp-inputd` prompt when requested together, so the user sees at most one dialog per call.
			```
			caps := RequestCapabilities("InputMonitoring", "ScreenCapture")
			if caps.Granted
				MsgBox "All permissions granted"
			MsgBox caps.ScreenCapture   ; "Granted", "Denied", "NotApplicable", or "Unsupported"

			; Query current status without prompting:
			caps := RequestCapabilities()
			```
		+ Prefer `#Requires capability` for scripts that need permissions from startup. Use `RequestCapabilities` directly when you need to check or request permissions at a specific point in script execution, or when you want to inspect the current status.
	+ `ComponentAvailable(capability)`: Returns true when the fixed first-party `"parser"` or `"compiler"` deployment unit is installed or embedded, compatible, and loadable. The aliases `"parsing"` and `"compilation"` are accepted. This check loads the requested unit, so checking `"compiler"` can load Roslyn. Unknown names raise a `ValueError`.
	+ `ParseScript(code)`: Parses, lowers, and compilation-validates source text or a script file without running it. Returns an empty string on success or formatted errors on failure, and requires the compiler component. Use `--validate-syntax` when only Roslyn-free syntax validation is needed.
	+ `RunScript(code, callbackOrAsync?, name := "*", executable?)`: Dynamically parses, compiles, and runs the provided code. It requires the compiler component in the calling process. The default name `"*"` reflects that the script is fed to the target process via StdIn rather than loaded from disk. Optionally provide the script name; whether to run it asynchronously (non-unset non-zero `callbackOrAsync` causes async run without a callback); an executable path to run the compiled assembly (defaults to the current process).
		+ If `callbackOrAsync` is provided a function then it is called after the script has finished with the `ProcessInfo` as the only argument. Over multiple runs `RunScript` is faster than running the process manually and writing to StdIn because of assembly and compilation caching.
		+ Returns a `ProcessInfo` object encapsulating info and I/O for the process. Available properties: `HasExited`, `ExitCode`, `ExitTime` (YYYYMMDDHH24MISS), `StdOut`, `StdErr`, `StdIn` (as `KeysharpFile`). Available methods: `Kill()`.
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
		+ `Clipboard.Has(kind) => Boolean`: `kind` is `"Text"`, `"Image"`, `"Files"`, `"Html"`, `"Rtf"`, or a platform-native format name.
	+ Raw access — the escape hatch, as non-portable as the format names themselves:
		+ `Clipboard.GetData(format) => Buffer`: one format's bytes exactly as the platform stores them. This is how private/application formats (Excel's `Biff12`, Visual Studio's `MSDEVColumnSelect`) are read.
		+ `Clipboard.Set(bag)`: publishes several formats in **one** transaction, so they coexist and the change fires once — `Clipboard.Set({ Text: "Hello", Html: "<b>Hello</b>" })`. Keys are kind names or native format names; values are a String, Buffer, Image, or Array of paths. A `Map` is accepted as well as an object, because an object-literal key must be an identifier and a name like `"HTML Format"` can only be spelled as a Map key.
	+ Save and restore: `Clipboard.All` (get/set) is the `ClipboardAll()` / `A_Clipboard := saved` pair spelled so the round trip is visible:
		```
		saved := Clipboard.All
		Clipboard.Text := "temporary"
		Clipboard.All := saved
		```
	+ Waiting and events:
		+ `Clipboard.Wait(timeout?, waitFor?)`: as `ClipWait`, and additionally accepts a kind name — `"Text"`, `"Any"`, `"Image"`, `"Files"`, `"Html"` or `"Rtf"`. `ClipWait` accepts those too.
		+ `Clipboard.OnChange(callback, count?) => ClipboardHook`: calls `callback(hook, type)` on every change, where type is 0 (now empty), 1 (text or files) or 2 (anything else). The returned hook has `Stop()`, `Pause(newState?)`, `Paused`, `IsActive` and `Count`, matching `Ks.WinEvent`. Prefer this over `OnClipboardChange` when the callback is a closure: unregistering there requires the very same function object back.
	+ Platform notes: on a Wayland session driven through a shell extension (Cinnamon/Muffin) the compositor's selection source can advertise only one type, so `Clipboard.Set` and `ClipboardAll` restore a single, most-useful representation rather than every format.
* New debugging functions:
	+ `ShowDebug()`: Shows the main window and focuses the debug output tab.
	+ `OutputDebugLine()`: The same as `OutputDebug()` but appends a linebreak at the end of the string.
* New `Crypt` class, holding hashing, key derivation, symmetric encryption and cryptographically secure random values (`#Import "Ks" { Crypt }`):
	+ A String is taken as its **UTF-8** bytes, so a digest is the one any other tool prints for the same text. Pass an `encoding` — the names `A_FileEncoding` takes — to use a different one, and note that a name which cannot be resolved raises rather than falling back. A `Buffer` or an `Array` of bytes is used as it stands; an open `File` is accepted by anything that hashes, but not by `Crypt.Encrypt`.
	+ A digest is returned as uppercase hexadecimal; compare digests case-insensitively, since the tool a checksum came from may print it in lowercase.
	+ `Crypt.Hash(value, algorithm := "SHA256", encoding := "UTF-8") => String`: hashes with `MD5`, `SHA1`, `SHA256`, `SHA384`, `SHA512` or `CRC32`, spelled with or without the `-`. An open `File` is read as a stream and left at the position it was on.
	+ `Crypt.HashFile(path, algorithm := "SHA256") => String`: the same over a file, read as a stream so that its size does not matter.
	+ `Crypt.MD5(value, encoding := "UTF-8") => String`, and likewise `Crypt.SHA1`, `Crypt.SHA256`, `Crypt.SHA384` and `Crypt.SHA512`.
	+ `Crypt.CRC32(value, encoding := "UTF-8") => Integer`: Calculates the CRC32 polynomial of an object. `Crypt.Hash(value, "CRC32")` returns the same checksum as hexadecimal.
	+ `Crypt.Encrypt(value, key, algorithm := "AES", mode := "CBC", iv?, encoding := "UTF-8") => Buffer` and `Crypt.Decrypt(...)` with the same parameters: symmetric encryption. `AES` is the only cipher so far and `mode` is `CBC`, `ECB` or `CFB`; the cipher is a parameter rather than part of the method name so that another one is a value this accepts, not a new method.
		+ With *iv* omitted, each call draws a random 16-byte initialization vector and writes it in front of the ciphertext, where `Crypt.Decrypt` reads it back. Encrypting the same text twice therefore gives different results, which is the point: a fixed vector lets anyone holding the output see which encrypted values are equal. Supply *iv* only to match a format defined elsewhere — it is then used as it stands and is **not** written to the result, so `Crypt.Decrypt` needs the same one back.
		+ `mode := "GCM"` **authenticates** as well as encrypts: an altered message is detected on decryption and raises, where a chaining mode would decrypt it to rubbish without complaint. Its nonce is 12 bytes rather than 16, and its authentication tag is appended to the result. Prefer it unless a format defined elsewhere dictates otherwise.
		+ `mode := "CFB"` is CFB8, the feedback size .NET and Windows CNG both default to — not the CFB128 that OpenSSL's plain `-aes-256-cfb` means.
	+ `Crypt.RandomBytes(count) => Buffer` returns cryptographically secure bytes for a vector, a salt or a key.
	+ `Crypt.PBKDF2(password, salt, iterations := 600000, length := 32, algorithm := "SHA256", encoding := "UTF-8") => Buffer` stretches a password into key material, which is what makes a passphrase usable as a key: `Crypt.Encrypt` otherwise takes the key exactly as it is given, zero-padded to the cipher's key size. *algorithm* is `SHA1`, `SHA256`, `SHA384` or `SHA512` — .NET rejects `MD5` for derivation on every platform, so it is not offered. The salt need not be secret but must differ per password, and must be stored alongside whatever the key protects.
	+ `Crypt.SecureRandom(min, max) => Double`: Generates a secure cryptographic random number.
		+ Returns an `Integer` if neither argument is a `Double`. The range includes *max*, as `Random`'s does.
	+ Data encrypted by the earlier `AES()` function does **not** decrypt with `Crypt.Decrypt` as it stands. That function derived its vector from the key instead of storing one, which is what made it deterministic, and that derivation has been removed. The vector it used was the first 16 bytes of SHA-1 over the 32-byte zero-padded key — with the key taken as UTF-16, since that was the old default encoding — so old data can still be read by rebuilding that vector and passing it as *iv* along with `encoding := "UTF-16"`. The key padding, CBC mode and PKCS7 padding are otherwise unchanged.
* New file functions:
	+ `FileDirName(filename) => String`: Returns the full path to filename, without the actual filename or trailing directory separator character.
	+ `FileFullPath(filename) => String`: Returns the full path to filename.
	+ `FileCreateTemp() => String`: Creates an empty temporary file and return its full path.
* New math functions:
	+ `Sinh(value) => Double`
	+ `Cosh(value) => Double`
	+ `Tanh(value) => Double`
* New RegEx functions:
	+ `RegExMatchCs()` and `RegExReplaceCs()` which use the C# style regular expression syntax rather than PCRE2.
		+ `OutputVar` in `RegExMatchCs()` will be of type `RegExMatchInfoCs`.
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
	+ `Base64Decode(str) => Array`: Converts a Base64 string to a Buffer containing the decoded bytes.
	+ `Base64Encode(value, encoding := "UTF-8") => String`: Converts a byte array — or a string, taken as its **UTF-8** bytes unless another `encoding` is named — to a Base64 string.
	+ `NormalizeEol(str, eol) => String`: Makes all line endings in a string match the value passed in, or the default for the current environment.
	+ `Join(separator, params*) => String`: Joins each parameter together as a string, separated by `separator`.
		+ Pass params as `params*` if it's a collection.
* New window functions:
	+ `WinFromPoint(x, y)`: Gets the window at a specific screen position.
	+ `WinMaximizeAll()`: Maximizes all windows.
* New class methods:
	+ `Array`:
		+ All comparisons compare the actual underlying values, so `"1" != 1`.
			+ This differs from the comparison rules in conditional statements, but makes more sense when searching arrays.
		+ `Contains(value) => Boolean`: Returns `true` if `value` is contained in the array, else `false`.
		+ `Filter(callback: (value [, index]) => Boolean) => Array`: Applies a filter to each element of the array and returns a new array consisting of all elements for which `callback` returned `true`.
		+ `FindIndex(callback: (value [, index]) => Boolean, startIndex := 1) => Integer`: Returns the index of the first element for which `callback` returned `true`, starting at `startIndex`. Returns 0 if `callback` never returned `true`.
			+ If `startIndex` is negative, the search starts from the end of the array and moves toward the beginning.
		+ `IndexOf(value, startIndex := 1) => Integer`: Returns the index of the first item in the array which equals value, starting at `startIndex`. Returns 0 if value is not found.
			+ If `startIndex` is negative, the search starts from the end of the array and moves toward the beginning.
		+ `Join(separator := ',') => String`: Joins together the string representation of all array elements, separated by `separator`.
		+ `MaxIndex()` and `MinIndex()` from AutoHotkey v1 are still supported.
		+ `Remove(value) => Boolean`: Removes `value` from the array and returns `true` if found, else `false`.
		+ `MapTo(callback: (value [, index]) => Any, startIndex := 1) => Array`: Maps each element of the array, starting at `startIndex`, into a new array where the mapping in `callback` performs some operation.
			```
			lam := (x, i) => x * i
			arr := [10, 20, 30]
			arr2 := arr.MapTo(lam) ; [10, 40, 90]
			```
		+ `Sort(callback: (a, b) => Integer) => this`: Sorts the array in place. The callback should use the usual logic of returning -1 when `a < b`, 0 when `a == b` and 1 otherwise.	
	+ `Buffer`:
		+ `__Item[]`: Indexer which can be used to read a byte at a 1-based offset.
			+ Throws an `IndexError` if the offset out of range.
		+ `ToHex()`: Converts the contents to a hexadecimal string.
		+ `ToBase64()`: Converts the contents to a base64 string
		+ `ToByteArray()`: Converts the contents to a raw C# `byte[]`.
	+ `Object`:
		+ `OwnPropCount()`: Corresponds to the global function `ObjOwnPropCount()`.
	+ `Map`:
		+ `MaxIndex()` and `MinIndex()` from AutoHotkey v1 are still supported.
	+ `String`:
		+ `String.StartsWith(token [,comparison]) => Boolean` and `String.EndsWith(token [,comparison]) => Boolean`: Determines if the beginning or end of a string start/end with a given string.
* Modified/extended accessors:
	+ `A_EventInfo` is not limited to positive values when reporting the mouse wheel scroll amount.
		+ When scrolling up, the value will be positive, and negative when scrolling down.
* New accessors:
	+ All of these live in the `KS` module, so a script must import the ones it uses: `#import KS { A_DirSeparator }`.
	+ `A_AllowTimers` returns whether timers are allowed or not. It's also easier to set this value rather than call `Thread("NoTimers")`.
	+ `A_AssemblyCompany` returns the value set by the `#AssemblyCompany` directive.
	+ `A_AssemblyConfiguration` returns the value set by the `#AssemblyConfiguration` directive.
	+ `A_AssemblyCopyright` returns the value set by the `#AssemblyCopyright` directive.
	+ `A_AssemblyDescription` returns the value set by the `#AssemblyDescription` directive.
	+ `A_AssemblyName` returns the value set by the `#AssemblyName` directive.
	+ `A_AssemblyProduct` returns the value set by the `#AssemblyProduct` directive.
	+ `A_AssemblyTrademark` returns the value set by the `#AssemblyTrademark` directive.
	+ `A_AssemblyVersion` returns the value set by the `#AssemblyVersion` directive.
	+ `A_PeekFrequency` gets or sets the current thread's message-check interval in milliseconds.
	+ `A_ClipboardTimeout` can be used at any point in the program to get or set the value normally specified by `#ClipboardTimeout`.
	+ `A_CommandLine` returns the command line string. This is preferred over passing `GetCommandLine` to `DllCall()` as noted above.
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
	+ `A_GuiTheme` gets/sets the application-wide WinForms GUI theme. Accepted values: `Classic`, `System`, `Dark`. Windows only.
	+ `A_HasExited` returns whether shutdown has been initiated.
	+ `A_KeysharpCorePath` provides the full path to the Keysharp.Core.dll file.
	+ `A_LoopRegValue` which makes it easy to get a registry value when using `Loop Reg`.
	+ `A_MaxThreads` returns the value `n` specified with `#MaxThreads n`.
	+ `A_NoTrayIcon` returns whether the tray icon was hidden with #NoTrayIcon.
	+ `A_NowMs`/`A_NowUTCMs` returns the current local/UTC time formatted to include milliseconds like so "YYYYMMDDHH24MISS.ff".
		+ These can be used with `DateAdd()`/`DateDiff()` using `"L"` for the `TimeUnits` parameter.
	+ `A_RealThread` is the real OS thread the current pseudo-thread runs on, as a `RealThread` object.
		+ On the main thread it is literally the same object as `RealThread.Main`, so `A_RealThread == RealThread.Main` is the test for "am I on the main thread". See the `RealThread` class under *New classes*.
	+ `A_SuspendExempt` returns whether subsequent hotkeys and hotstrings will be exmpt from suspension because `#SuspendExempt true` was specified.
	+ `A_Thread` is the current pseudo-thread as a `Thread` object.
		+ Every per-pseudo-thread fact is a property on the object rather than its own importable global, so the surface extends without new names. See the `Thread` class under *New classes*.
		+ There is exactly one object per pseudo-thread, so "is this the one I am in" is `thr == A_Thread`.
	+ `A_TotalScreenHeight` returns the total height in pixels of the virtual screen.
	+ `A_TotalScreenWidth` returns the total width in pixels of the virtual screen.
	+ `A_UseHook` returns the value `n` specified with `#UseHook n`.
	+ `A_WinActivateForce` returns whether the forceful method of activating a window is in effect because `#WinActivateForce` was specified.
	+ `A_WorkAreaHeight` returns the height of the working area of the primary screen.
	+ `A_WorkAreaWidth` returns the width of the working area of the primary screen.
	+ `A_Timers` returns a `Map` of (`Func`, `Boolean`) pairs where the key is the function object of the timer and the value is the enabled state of the associated timer.
* New classes:
	+ `Boolean`: The type of a truth value, extending `Integer`. Available from the `KS` module.
		+ Every operator that yields a truth value yields a `Boolean`: a comparison (`a > b`, `a = b`, `a != b`), a negation (`!a`), and `Map.Has()`. The `true` and `false` keywords are `Boolean` values too.
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
		+ `Boolean(value) => Boolean`: converts a value, deciding it exactly as `if` would — `Boolean("")` and `Boolean("0")` are false, `Boolean("x")` and `Boolean([])` are true. An unset value raises, as `if` on an unset variable does.
	+ `Clr`: Experimental CLR interop with regular AutoHotkey syntax, meaning easy access to CLR libraries.
		+ `Clr.Load(asmOrPath)` loads a CLR assembly from a dll file or assembly name, and returns a `ManagedAssembly` or `ManagedNamespace` object. Example: `System := Clr.Load("System")`
			+ `ManagedNamespace` can be accessed with property access syntax to get namespaces and types (`ManagedType`). Example: `linq := System.Linq.Enumerable`
			+ `ManagedType` may be accessed for static methods/properties, or called to create a new `ManagedInstance`.
			+ `ManagedInstance` may be accessed with normal AutoHotkey syntax for properties, methods, and indexer access. Example: `linq.Where(nums, isOdd)`
			+ Basic type marshalling between AutoHotkey and CLR is supported (including function objects), more complicated types may not currently work.
			+ An enum-typed parameter or property takes a plain Integer, since a script has no enum type: `File.SetUnixFileMode(path, 0x180)`. The value does not have to be a declared member, so a flag combination can be built in script with `|`. The member itself works equally well when fetched through `Clr` (`System.StringComparison.OrdinalIgnoreCase`), and an enum coming back from CLR stays a wrapped member rather than widening to an Integer — take its name with `.ToString()`. A value with no numeric reading raises a `TypeError`, as it does for any other integral parameter.
		+ `Clr.GetNamespaceName(ManagedNamespace)` returns the full intenal namespace name of the namespace wrapped by `ManagedNamespace`.
		+ `Clr.GetTypeName(ManagedType)` returns the full internal type name of the type wrapped by `ManagedType`.
	+ `HashMap`: Extends `Map` and does not perform sorting before enumeration.
	+ `Json`: Converts between JSON text and script values. Available from the `KS` module: `#Import "Ks" { Json }`, then `Json.Encode(value)` and `Json.Decode(text)`.
		+ `Json.Encode(value [, indent, nullValue]) => String`: Returns the JSON text for a script value.
			+ A `Map` becomes a JSON object, an `Array` becomes a JSON array, and any other object contributes its own value properties (a dynamic property is skipped rather than invoked, because encoding a value must not run script code). A `Map` enumerates in sorted key order, so encoding one sorts its keys and the same map always produces the same text regardless of insertion order.
			+ `indent` follows the convention of JavaScript's `JSON.stringify` and Python's `json.dumps`: omitted, `""` or `0` writes the compact single-line form (the default); a number writes that many spaces per level; a string of spaces **or** of tabs is used as the indent unit itself, as in ``Json.Encode(value, "`t")``. The widest indent is 127; a mix of spaces and tabs, or any other string, raises a `ValueError`.
			+ Indented output separates lines with a single line feed on every platform — deliberately not the platform line ending, so that the same value always produces the same bytes and a hash taken over encoded JSON (a lock file, a cache key) is not host-dependent.
			+ Quotes are escaped but non-ASCII text is not, so `Json.Encode("äöü")` is `"äöü"` rather than a run of `\uXXXX` escapes.
			+ A reference cycle, or nesting deeper than 128 levels, raises a `ValueError`. Two distinct but equal containers are not a cycle.
		+ `Json.Decode(text [, caseSense := true, nullValue])`: Returns the script value for JSON text.
			+ A JSON object becomes a `Map` and a JSON array becomes an `Array`. An integral number becomes an `Integer`, and anything else — including a value too large for a 64-bit integer — becomes a `Float`.
			+ `caseSense` is the case sensitivity given to **every** `Map` in the result, spelled as for `Map.CaseSense`: `true` (the default, matching `Map()`), `false`, or `"Locale"`. It has to be chosen here because `Map.CaseSense` cannot be assigned once a map holds entries. With `false`, keys differing only in case collapse into a single entry, as they do in any case-insensitive `Map`.
			+ Trailing commas and `//` and `/* */` comments are accepted, because hand-written configuration files commonly carry them. Everything else follows the JSON grammar; malformed text, or nesting deeper than the 128 levels `Encode` also allows, raises a `ValueError`.
		+ A `Boolean` — the `true` and `false` keywords, or any comparison, negation or `Map.Has()` result — is written as JSON `true`/`false`, where the Integer 1 or 0 is written as a number. That is what makes booleans survive a round trip, and `x is Boolean` is what tells the two apart in a decoded document.
		+ Nulls: JSON has a `null`; the language has no value a container can hold for it. With no marker a JSON `null` decodes to **unset**, which means what `unset` means everywhere else — a `Map` key is simply absent, and an `Array` element is a hole that keeps the array's `Length`.
			+ So `Json.Decode('{"a":null,"b":""}')` gives a Map with only `b`, and `Json.Decode('[1,null,3]')` gives a 3-element Array whose element 2 is a hole. `Has()` is the test, and a `null` no longer collides with an empty string.
			+ `nullValue` overrides that on `Decode` — it is what a JSON `null` becomes — and on `Encode` it is the value written back out as `null`. Supply the same marker to both to tell a `null` apart from an *absent* key, which is the one distinction unset cannot carry.
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
				                         ; launch site does not name one. Event covers every registered
				                         ; handler (GUI, menu, OnExit, OnClipboardChange) — one registry
				                         ; dispatches them all, so they are not separable.
				Elapsed => Integer       ; ms since launch
				Priority => Integer      ; get/set; same storage as Thread "Priority"
				Critical => Boolean      ; get/set; the object form of the Critical function
				Paused => Boolean        ; get/set; the object form of Pause. A_IsPaused is this on Under
				IsInterruptible => Boolean ; read-only
				Underlying => Thread     ; the pseudo-thread this one interrupted, or ""
				Exit([ExitCode := 0]) => Integer   ; cooperative; returns the target's Id
			}
			```
		+ Pseudo-thread state is pooled and reused, so a `Thread` object captures its ID and re-checks it on every access. Once its pseudo-thread ends, `Id` and `Index` still answer from captured values and `IsActive` reports false, while everything else throws `TargetError` — a stored object can never silently describe a later pseudo-thread that reused the slot.
		+ Boolean members follow the library-wide naming rule: `IsActive`/`IsInterruptible` are read-only, `Critical`/`Paused` are settable.
		+ There is deliberately no `IsCurrent` property, and likewise no `IsMain`/`IsAlive` on `RealThread` — anything derivable from an identity comparison or from `Status` is left out.
		+ A `Thread` object may be read from any real thread, but every setter and `Exit` throw `TargetError` when called from a real thread other than its owner. Pseudo-thread stacks are per real thread and are mutated without locking.
	+ `RealThread`: Manages real threads which are not related to the green threads that are used for the rest of the project.
		+ A `RealThread` is created by calling the `RealThread` class static instance.
			```
			class RealThread
			{
				static Call(funcobj [, params*]) => RealThread ; Runs `funcobj` on a new real thread, passing `params` to it.
				RealThread(funcobj [, params*])
				static Main => RealThread ; The script's main thread.
				Id => Integer             ; Managed id of the backing OS thread.
				Status => String          ; "Running" until it finishes, then "Done" or "Error".
				                          ; Work can be given to it exactly while it is "Running".
				Result => Any             ; The body's return value; "" while running, on error, or if it exited early.
				Threads => Array          ; The active ScriptThreads of this real thread, oldest first.
				Post(funcobj [, params*])              ; Queue work and return immediately.
				Send(funcobj [, params*]) => Any       ; Run work there, wait, return its value.
				Wait([timeout := -1]) => Boolean       ; True if it finished, false if the timeout elapsed first.
				ContinueWith(funcobj [, params*]) => RealThread ; Runs `funcobj` on a new real thread after this one finishes.
				Exit([exitCode := 0])                  ; Cooperative shutdown request.
			}

			ThreadFunc(obj)
			{
				; Long running operation to run on a real thread.
			}

			theThread := RealThread("ThreadFunc") ; Create and start the thread.
			theThread.Wait() ; Wait for the thread to complete before continuing.
			```
		+ `RealThread.Main.Post(fn)` is the supported way to move work back onto the main thread from a worker. `A_RealThread` is the calling thread's object; on the main thread it is literally the same object as `RealThread.Main`, so `A_RealThread == RealThread.Main` is the test for "am I on the main thread".
		+ `Wait` reports *completion*, not the body's value — that is `Result` — so a timeout is distinguishable from a body that returned nothing.
		+ An uncaught error in a body is reported on the thread where it happened, exactly like one in a timer or hotkey body, and sets `Status` to `"Error"`. It is never smuggled to a later `Wait`.
		+ A worker that registered a timer, hotkey or callback keeps serving them after its body returns; `Exit` is how it is shut down. `Wait`, `ContinueWith` and `Exit` throw `TargetError` on `RealThread.Main` and on adopted threads, which have no body of their own.
	+ `Lock`: Guards code shared between real threads where `LockRun` cannot — a timed acquire, or a lock held across several statements. `LockRun` remains the one-call form and is not duplicated on the class.
		```
		class Lock
		{
			static Call() => Lock
			Acquire([Timeout := -1]) => Boolean ; true once held, false if the timeout elapsed first
			Release()                           ; once per successful Acquire, from the acquiring real thread
		}
		```
		+ The lock belongs to a *real* thread and is reentrant. `Acquire` blocks the whole real thread, so acquiring on the main thread stalls the message loop — pass a timeout there.
	+ New class `Image` provides cross-platform image capture and manipulation. Capture with `Image.FromDesktop()`, `Image.FromMonitor(n)`, `Image.FromRect(x, y, w, h)`, `Image.FromWindow(winTitle [, options])`, load with `Image.FromFile(path)` / `Image.FromBitmap(handle)` / `Image.FromClipboard()` (an alias of the `Clipboard.Image` getter; returns `""` when the clipboard holds no image, and the write direction is `Clipboard.Image := img`), or create a blank ARGB canvas to draw on with `Image.Create(width, height [, background])` (omit `background` or pass `""` for fully transparent), or build one from raw pixel bytes with `Image.FromBuffer(data, width, height [, bytesPerPixel := 4])` — the inverse of `GetPixelData`, where `bytesPerPixel` 1 = 8-bit grayscale and 4 = RGBA. Paint shapes and text with `Clear([color])`, `DrawLine(x1, y1, x2, y2 [, color, thickness])`, `DrawRect`/`FillRect(x, y, w, h [, color, thickness])`, `DrawRoundRect`/`FillRoundRect(x, y, w, h, radius [, color, thickness])`, `DrawEllipse`/`FillEllipse(x, y, w, h [, color, thickness])`, `DrawText(text, x, y [, color, font])` (`font` is `"Name size"` with optional trailing style keywords `bold`, `italic`, `underline`, `strike`, e.g. `"Sans 16 bold italic"`), and `DrawImage(image [, x, y, w, h])` (stamp another image onto this one). A color is a name (`"Red"`), a `0xRRGGBB` value (opaque), or — for a non-opaque alpha — a `0xAARRGGBB` value given either as a number (e.g. `0x80FF0000`) or an 8-hex-digit string; a fully-transparent `0x00` alpha survives only as an 8-hex-digit string, since a numeric `0x00RRGGBB` collapses to a plain opaque `0xRRGGBB`. Queue chainable transforms — geometry (`Scale`, `Resize(width, height)` for an absolute resize where a single negative dimension keeps the aspect ratio, `Rotate`, `Flip`, `Crop`) and color (`Grayscale()`, `Opacity(factor)` with `factor` 0-1, `Brightness(amount)` and `Contrast(amount)` with `amount` -1 to 1); the draw ops and transforms all apply lazily and chain, then output via `Save(filename)` or `ToBitmap()`, show it in a window with `Show([title, wait])` (`wait` = block until the preview window closes), read/write pixels with `GetPixel(x, y)` (returns the full `0xAARRGGBB`, alpha included) and `SetPixel(x, y, color)` (a `0xRRGGBB` opaque or `0xAARRGGBB` value), or search it — the three search methods return a boolean found? and write the result(s) into a leading `&match` output variable, and matching is RGB-only (alpha is ignored, since capture alpha is unreliable): `Search(&match, needle [, variation, trans, direction])` locates a sub-image and on a hit sets `match := {x, y}` (the match's top-left as absolute image pixels) and returns `true`, else returns `false` and sets `match := ""` (`trans` = a needle color that matches anything, ImageSearch's `*TransN`; `direction` = ImageSearch's `*DirN` scan order 1-9 selecting which match wins); `SearchAll(&matches, needle [, variation, trans, direction])` sets `matches := [{x, y}, {x, y}, …]` (all matches, an empty array `[]` when none) and returns `true` when there is at least one; `SearchPixel(&match, color [, variation])` finds the first matching pixel — PixelSearch over a capture instead of the live screen — and on a hit sets `match := {x, y, color}` where `color` is the actual matched pixel's full `0xAARRGGBB` (the value `GetPixel` returns). Each also takes an optional `(x, y, w, h)` region right after `&match` (`Search(&match, x, y, w, h, needle [, …])`, likewise `SearchAll`/`SearchPixel`) to search only inside that rectangle (clamped to the image); returned coordinates stay absolute image pixels. The region form is selected by argument count — 5+ arguments after `&match` means a region; `SearchPixel` with 3 or 4 arguments (neither the plain nor the region form) raises a ValueError. Additional surface: `Copy()` duplicates the image; `MeasureText(text, font, &w, &h)` measures a string with the same font spec `DrawText` uses; `GetPixelData([bytesPerPixel := 1, buffer]) => Buffer` copies the pixels into a tightly packed `Buffer` (`bytesPerPixel` 1 = grayscale, 4 = RGBA) for `DllCall`/OCR interop — pass `buffer` (a `Buffer`, or any object exposing `Ptr` and `Size`) to write into storage you already own instead of allocating a new one, and that same object is returned; it must hold at *least* `Width * Height * bytesPerPixel` bytes (a ValueError otherwise), exactly that many are written from the start, and anything beyond is left alone, so one buffer sized for the largest capture can serve smaller ones too (`data := img.GetPixelData(4, data)` in a capture loop) — and `SetPixelData(data [, bytesPerPixel := 4])` overwrites the current image's pixels from such a buffer; and the read-only `X`/`Y`/`ScaleX`/`ScaleY` properties report the capture's screen origin and HiDPI pixel scale so coordinates found in the image map back to screen coordinates. `FromWindow` captures the whole window (title bar included; occluded windows capture correctly everywhere except foreign-toplevel-only compositors) and accepts an `options` object/mode (matching OCR.ahk): on Windows it selects the capture technique — `0`/`1` = GetDC + BitBlt, `2`/`3` = PrintWindow, `4` (default) = PrintWindow + PW_RENDERFULLCONTENT for hardware-accelerated windows (mode `5`, UWP capture, is not yet implemented) — and `{decorations: false}` requests a client-area-only grab where the platform can honor it (KWin); elsewhere the flag is ignored. `Image.FromRect` takes absolute screen coordinates and deliberately ignores the Pixel `CoordMode` (unlike `PixelGetColor`/`ImageSearch`), matching its sibling capture factories. Replaces the earlier `ImageCapture` function — e.g. `Image.FromRect(x, y, w, h).Save(filename)` or `.ToBitmap()`. Using a disposed `Image` now throws rather than silently returning `0`, and because `Rotate`/`Flip` invalidate the `X`/`Y` screen-origin mapping (`Rotate` also invalidates `ScaleX`/`ScaleY`), don't rely on those properties after rotating or flipping.
		* New KS class `Overlay` provides a click-through, always-on-top image surface. `Overlay(x, y [, w, h])` and all screen-facing geometry use the platform's native coordinates; the platform chooses the backing-pixel canvas automatically. Draw with the `Image` primitives, replace content with `SetImage(source)`, or atomically replace content and optional geometry with `Update(source [, x, y, w, h])`. `Redraw(callback [, x, y, w, h])` builds a target-sized frame off-screen and commits it once, while `BeginDraw()`/`EndDraw()` batches ordinary draw calls. `Show`, `Move`, `Hide`, and `Destroy` control the surface; `W`/`H` resize its display rectangle without discarding the canvas. `Opacity`, `ClickThrough`, `Visible`, and `Hwnd` expose presentation state. `Highlight` and, on Linux/macOS, `ToolTip` use this same primitive. Some compositor-drawn Wayland backings remain click-through even when `ClickThrough` is `false`.
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
	* Boolean property naming:
 		+ On a class, an `Is` prefix marks a read-only predicate (`Thread.IsActive`, `Thread.IsInterruptible`, `Func.IsBuiltIn`, `Func.IsVariadic`). 		+ A settable boolean is named for the state itself (`Thread.Critical`, `Thread.Paused`, `WinEvent.Paused`), so it reads as `obj.Paused := true` rather than as a question.
		+ This follows AutoHotkey, which never prefixes a settable boolean property — `InputHook.VisibleText`, `InputHook.CaseSensitive`, `GuiControl.Enabled`, `GuiControl.Visible` — while using `Is` on some read-only ones (`Func.IsBuiltIn`, `Func.IsVariadic`, alongside unprefixed `InputHook.InProgress`, `File.AtEOF`, `GuiControl.Focused`).
		+ The `A_Is*` variables are a separate namespace and keep their AHK spelling: `A_IsCritical`, `A_IsPaused`, `A_IsSuspended`.
	+ A compiled script can be reloaded.
		+ AutoHotkey does not support reloading a compiled script.
	+ When sending a string through `SendMessage()` using the `WM_COPYDATA` message type, the caller is no longer responsible for creating the special `COPYDATA` struct.
		+ Instead, just pass `WM_COPYDATA (0x4A)` as the message type and the string as the `lparam`, and `SendMessage()` will handle it internally.
		+ Note, this will send the string as UTF-16 Unicode. If you need to send to a program which expects ASCII, then you'll need to manually create the `COPYDATA` struct.
	+ New preprocessor directives:
		+ `#CSharp` embeds C# members in the script assembly for hot loops, buffer work and interop.
			+ Use `#CSharp` … `#EndCSharp` for an inline block, or `#CSharp "helper.cs"` for a file. Blocks may appear at module scope or directly in a class. Relative files use the module search path.
			+ At module scope, `public static` methods are callable locally and by an explicit `{ Name }` import. `[Export]` also exposes one to `{ * }`; `[Export(Default = true)]` makes it the bare-import default. These match `export` and `export default`.
			+ In class blocks, public methods and property accessors are script-visible; non-public members remain C# helpers, `init` is read-only, and fields are not exposed. `[Static]` selects the class-static side.
			+ Values use the same conversions as `Ks.Clr`. Unsupported public signatures are rejected, and CLR exceptions are mapped to catchable Keysharp errors where possible.
			+ Usings are shared within a module but isolated between modules. Script preprocessor symbols and C# `unsafe` are supported. Calls use normal script dispatch, so group substantial work across the boundary.
		+ `#Package [*i] id [version]` resolves a NuGet package for `Ks.Clr` and inline C# at compile time. It follows `NuGet.Config` and supports managed, resource and native assets, but not package build hooks. `*i` makes a missing package optional.
		+ `Clr.LoadPackage(id, version?, optional?)` loads a NuGet package at runtime. Prefer `#Package` for known dependencies.
		+ `#HookMutexName <name>` allows renaming the mutex objects created to detect keyboard and mouse hooks in other running scripts. The default name is "Keysharp".
		+ Assembly description attributes may be changed with the following directives, with the desired value as the only argument of the directive:
			+ `#AssemblyName`
			+ `#AssemblyDescription`
			+ `#AssemblyConfiguration`
			+ `#AssemblyCompany`
			+ `#AssemblyProduct`
			+ `#AssemblyCopyright`
			+ `#AssemblyTrademark`
			+ `#AssemblyVersion`
	+ Command line switches may start with `/`, `-` or `--`, and must appear before the script or assembly input. After the input is found, all remaining arguments are passed to the script or assembly entry point.
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
		- `--validate`, `/validate`
		  Compiles but does not run the script. Can be used to check for load-time errors.
		- `--validate-syntax`
		  Parses without lowering, compiling, or loading Roslyn. `--syntax-only` and `--parse-only` are aliases.
		- `--with-parser`, `--with-compiler`, `--with-component <parser|compiler>`
		  Includes the selected optional first-party deployment unit in a `.cks` or executable. Compiler use by `Ks.RunScript` or `Ks.ParseScript` is detected and included automatically. The generic form accepts unit IDs, not capability aliases.
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
				+ `TraySetIcon(A_KeysharpCorePath, "Keysharp_s.ico")`
			+ To set a menu item to the same:
				+ `parentMenu.SetIcon("Menu caption", A_KeysharpCorePath, "Keysharp_s.ico")`
		+ Rich text boxes are supported by passing `RichEdit` to `Gui.Add()`. The same options from `Edit` are supported with the following caveats:
			+ `Multiline` is `true` by default.
			+ `WantReturn` and `Password` are not supported.
			+ `Uppercase` and `Lowercase` are supported, but only for key presses, not for pasting.
			+ The `Gui.Control.Value` property will only get/set the displayed text of the control. To get/set the raw rich text, use the new property `Gui.Control.RichText`.
				+ Use `AltSubmit` with `Submit()` to get the raw rich text.
				+ Attempting to use `Gui.Control.RichText` on any control other than `RichEdit` will throw an exception.
	+ New methods and properties:
		+ `Gui`:
			+ `Visible`: Gets/sets whether the window is visible or not.
		+ `Menu`:
			+ `HideItem()`, `ShowItem()` and `ToggleItemVis()`: Shows, hides or toggles the visibility of a specific menu item.
			+ `MenuItemName()`: Gets the name of a menu item, rather than having to use `DllCall()`.
			+ `SetForeColor()`: Sets the fore (text) color of a menu item.
			+ `MenuItemCount`: Gets the number of sub items within a menu.
		+ `ListView`:
			+ `DeleteCol(col) => Boolean` Removes a column and returns `true` if the column was found and deleted, else `false`.
		+ `TabControl`:
			+ `SetTabIcon(tabIndex, imageIndex)`: Relieves the caller of having to use `SendMessage()`.
		+ `TreeView`:
			+ `GetNode(nodeIndex) => TreeNode`: Retrieves a raw Winforms `TreeNode` object based on the passed in ID.
	+ New classes:
		+ `WinEvent`: For subscribing to window events (active/foreground change, appearance, disappearance, move/resize, minimize, restore, title change, and caret movement) across platforms, modeled on the popular AutoHotkey `WinEvent` library.
			+ It is part of the `KS` module; import it with `#import KS { WinEvent }`.
			+ Each subscription is created by calling a `WinEvent()` static method, which returns a subscription object whose callback fires until it is stopped. The argument order mirrors the reference library: `count` (default `-1` = unlimited) comes right after `winTitle`, with the rarely-used `winText`/`excludeTitle`/`excludeText` criteria last. The criteria use standard `WinTitle` matching, and `count` limits how many times the callback fires.
			+ The callback receives `(hookObject, hwnd, dwmsEventTime)`. Event-specific extras arrive in `A_EventInfo` instead: `Move()` puts the window's new position and size there as an object with `x`, `y`, `w` and `h` (matching `WinGetPos()`), and `CaretMove()` the caret's rectangle in the same shape but in screen coordinates. Every other event type keeps the event time in `A_EventInfo`.
			+ The registration-time values of `DetectHiddenWindows`, `DetectHiddenText`, and the title-match mode are captured and used for matching (as in the reference library); `Exist()` additionally forces hidden detection on. `Active()` also fires when the active window's title changes, and `NotExist()` fires when a window is destroyed or — for a `DetectHiddenWindows`-off subscription — hidden or cloaked. `Exist()`/`NotExist()` replace the reference library's `Create()`/`Close()` (those were just `Exist()`/`NotExist()` with `DetectHiddenWindows` on).
			+ `CaretMove()` reports the caret owner's *top-level* window (not the focused edit control), suppresses events whose caret position is unchanged, and rides on the same accessibility plumbing as `CaretGetPos()` — so an application that draws its own caret without exposing it to accessibility reports nothing on any platform, and the Linux/macOS sources additionally need AT-SPI enabled / Accessibility permission granted.
			+ Subscriptions are auto-stopped on `__Delete()`, but because garbage collection is unpredictable, call `Stop()` (or let the owning thread tear down) when you are done with one.
				```
				class WinEvent
				{
					; Event subscriptions: (Callback, WinTitle, Count, WinText, ExcludeTitle, ExcludeText)
					static Active(callback [, winTitle, count := -1, winText, excludeTitle, excludeText]) => WinEvent  ; The foreground/active window changed (or the active window's title changed).
					static Exist(callback [, winTitle, count, ...]) => WinEvent        ; A matching window appeared (created, shown, or its title changed to match). Fires once per window; DetectHiddenWindows-aware.
					static NotExist(callback [, winTitle, count, ...]) => WinEvent     ; A matching window disappeared (destroyed, hidden/cloaked, or its title changed to no longer match).
					static Move(callback [, winTitle, count, ...]) => WinEvent         ; A window moved or resized (every event is delivered as-is, not coalesced).
					static Minimize(callback [, winTitle, count, ...]) => WinEvent     ; A window was minimized.
					static Restore(callback [, winTitle, count, ...]) => WinEvent      ; A window was restored from the minimized state.
					static TitleChange(callback [, winTitle, count, ...]) => WinEvent  ; A window's title changed.
					static CaretMove(callback [, winTitle, count, ...]) => WinEvent    ; The text caret moved inside a window; A_EventInfo holds its screen rectangle.

					; Global pause
					static Pause(newState := 1) => Boolean  ; Pause (1), unpause (0) or toggle (-1) all hooks; returns the new paused state.
					static Paused => Boolean                ; Get/set whether all hooks are paused.

					; Per-hook
					Stop()                       ; Cancel the subscription so the callback no longer fires.
					Pause(newState := 1) => Boolean ; Pause (1), unpause (0) or toggle (-1) this hook; returns the new paused state.
					Paused => Boolean            ; Get/set whether this hook is paused (paused hooks stay registered but don't fire).
					IsActive => Boolean          ; Whether the subscription is still receiving events.
					EventType => String          ; The event kind, e.g. "Active" or "Move".
					Count => Integer             ; Remaining number of times the callback will fire (-1 = unlimited).
				}
				```
			+ Platform support for `WinEvent`:
				+ Windows: Uses `SetWinEventHook()` and supports every event type.
				+ Linux: The events come from a GDK/X11 event filter on the UI thread (covering X11 and XWayland windows); native Wayland sources (GNOME/KWin/wlroots) are not yet wired, and `Restore`/`Close`-on-hide are not yet emitted.
				+ macOS: Only active-application changes are currently reported (window-granular events via the accessibility APIs are planned).
		+ `Monitor`: One display, carrying the metadata and device control the AHK-compatible `MonitorGet*` functions do not expose — model, manufacturer, serial, a stable id, refresh rate, physical size, orientation, connection kind, and brightness / raw DDC-CI control.
			+ It is part of the `KS` module; import it with `#import KS { Monitor }`. It does not replace the AHK `MonitorGet*` functions, which are unchanged apart from now raising a `ValueError` on an out-of-range monitor index instead of silently substituting the primary (matching AutoHotkey v2). `Image.FromMonitor()` inherits that same validation.
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
					static FromPoint(x, y) => Monitor    ; The monitor containing a native screen point, or the nearest one when it falls in a gap.
					static FromMouse() => Monitor
					static FromWindow([winTitle, winText, excludeTitle, excludeText]) => Monitor  ; The monitor a window overlaps most.
					static FromId(id) => Monitor   ; The monitor whose Id matches, or "" when it is not attached.
					static OnChange(callback [, count := -1]) => MonitorHook  ; Display-configuration changes; see below.

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
					X, Y, W, H => Integer
					Bounds, WorkArea => Object   ; { x, y, w, h }
					Scale => Float               ; Authored-size scale; 1.0 is 100%. Scales dimensions, never absolute positions.
					Dpi                          ; Derived from the physical size, in the same units as W/H; "" if unknown.
					PhysicalWidth, PhysicalHeight ; Millimetres, or "".
					RefreshRate => Float         ; Hz — 59.94, not 59 — or "".
					Orientation => Integer       ; Clockwise rotation of the desktop content: 0, 90, 180 or 270.
					Refresh() => Monitor         ; Re-read in place; returns this object, or "" if the monitor is gone.

					; Device control — each is a real hardware transaction, deliberately not cached
					Brightness => Integer        ; Get/set, 0-100. OSError naming the reason where unsupported.
					HasBrightness => Boolean     ; A real probe of the device, so it costs one brightness read.
					GetVCP(code) => Object       ; Raw DDC/CI (MCCS) feature => { current, max }.
					SetVCP(code, value)          ; Experimental; see the warning below.
				}
				```
			+ `Monitor.OnChange(callback)` returns a hook carrying the same `Stop`/`Pause`/`Paused`/`IsActive`/`Count` surface as a `WinEvent` hook, so the two subscription APIs are managed identically. The callback receives `(hook, kind)`, where `kind` is `"topology"` when the set of attached monitors changed (plug/unplug, dock/undock) and `"settings"` when the same monitors are attached but their resolution, position, scale or primary assignment changed; `A_EventInfo` holds the monitor count after the change. The kind is derived by diffing topology snapshots rather than by decoding native change flags, so it is identical on every platform and the redundant notifications each platform emits are dropped. A handler holding a `Monitor` should call its `Refresh()` — which returns falsy if that is the display that was just unplugged — or simply re-read `Monitor.All`.
			+ **`SetVCP()` is a foot-gun.** Writing an input-source or power code will switch the monitor away from this computer, and a few displays react badly to codes they document but mishandle; verify a code against the monitor's own MCCS documentation first. The feature is read back before it is written, so a code the monitor does not implement raises an `OSError` rather than reporting a success the display silently ignored.
			+ Platform support for `Monitor`:
				+ Windows: Metadata from the DisplayConfig API plus the EDID Windows caches under the monitor's registry key; brightness through WMI for the built-in panel and DDC/CI (dxva2) for external monitors.
				+ Linux: EDID from `/sys/class/drm/*/edid`, identical under X11 and Wayland; refresh rate and rotation from XRandR or `wl_output`. Brightness uses the kernel backlight class for the built-in panel (a direct sysfs write where permitted, else logind's `SetBrightness`) and DDC/CI over `/dev/i2c-*` for external monitors, which needs `i2c-dev` loaded and access to the bus — the packaged udev rule grants it, and the error message names the fix when it is missing.
				+ macOS: CoreGraphics answers identity, physical size, rotation and refresh rate directly, so no EDID parsing is involved; `Model` and `Adapter` are always `""` because CoreGraphics exposes no product-name or adapter API, and `Connection` only distinguishes the built-in panel. Brightness uses DisplayServices for the built-in panel and Apple's own displays, and DDC/CI for every other external monitor.
				+ Display-change events come from `SystemEvents.DisplaySettingsChanged` on Windows, GDK's monitor signals on Linux (covering X11 and Wayland alike), and `CGDisplayRegisterReconfigurationCallback` on macOS.

### Removals
* Removed/reduced functions:
	+ `Download()`: Supports only the `*0` option, and not any other numerical values.
	+ `ListLines()`: Non-functional because C# doesn't support it.
	+ `FormatTime()`: The `R`, `Dn` or `Tn` parameters in  are not supported, except for 0x80000000 to disallow user overrides.
		+ If you want to specify a particular format or order, do it in the format argument. There is no need or reason to have one argument alter the other.
		+ [Here](https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings) is a list of the C# style `DateTime` formatters which are supported.
	+ `ObjAddRef()` and `ObjPtrAddRef()` do not have an effect for non-COM objects. Instead, use the following:
		+ `newref := theobj ; adds 1 to the reference count`
		+ `newref := "" ; subtracts 1 from the reference count`
	+ When passing `"Interrupt"` as the first argument to `Thread()`, the third argument for `LineCount` is not supported because Keysharp does not support line level awareness.
* Syntax:
	+ The address of a variable cannot be taken using the reference operator.
		+ It returns a VarRef object as in AutoHotkey.
* Miscellaneous behavior:
	+ Pausing a script is not supported because a Keysharp script is actually a running program.
		+ The pause menu item and `Pause()` function have been removed.
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
		+ `IL_Create()` only takes one parameter: `largeIcons`. `initialCount` and `growCount` are no longer needed because memory is handled internally.
		+ `LoadPicture()` does not accept a `GDI+` argument as an option.
		+ `PixelGetColor()` ignores the `mode` parameter.
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
			+ Off Windows there is no native message queue to monitor, so the input messages are synthesized from the toolkit events the script's own GUI raises: `WM_MOUSEMOVE`, the left/right/middle button down, up and double-click messages, `WM_MOUSEWHEEL`, `WM_KEYDOWN`/`WM_KEYUP`, `WM_SYSKEYDOWN`/`WM_SYSKEYUP` and `WM_CHAR`. They carry the same payloads as on Windows — the control's handle, `MK_*` flags and packed coordinates in `wParam`/`lParam`, a virtual key code for the key messages — and the callback's last-found window is the GUI the control belongs to. Any other message number is never delivered, messages sent to windows the script does not own cannot be observed at all, and a click on a single-line `Edit` is missed because GTK's entry consumes the button press before the toolkit raises an event for it. `Gui.OnMessage()` and `GuiCtrl.OnMessage()` are fed from the same source after the global monitors, and are addressed the way Windows addresses them: a message that went to the GUI window reaches the former, one that went to a control reaches the latter. This is verified on X11 and Wayland; macOS runs the same code but is untested.
		
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
