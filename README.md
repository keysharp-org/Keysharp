<img src="assets/Keysharp.png" Alt="Project logo" width="128" height="128">

# Keysharp

Keysharp is a cross-platform C# implementation of [AutoHotkey v2](https://www.autohotkey.com/docs/v2/). It parses AutoHotkey-style scripts, compiles them through .NET, and runs them on Windows, Linux, and macOS.

> **Status:** under active development and not yet recommended for production. Windows has the broadest compatibility; Linux and macOS support continues to improve.

Most scripts run unmodified on Windows. Across platforms, anything using **DllCall, COM, or the registry will not work** on Linux and macOS.

- [Download a release](https://github.com/keysharp-org/Keysharp/releases)
- [Platform compatibility](#platform-compatibility)
- [Full setup & build guide](docs/reference.md)
- [AutoHotkey v2 differences](docs/reference.md#differences)

## Demos

<a href="https://github.com/user-attachments/assets/c362ccf2-f096-4d0e-960a-aef2d9ce46b5" target="_blank">▶ Watch the Keysharp demos</a> *(8 MB GIF)*

See the [demos folder](./demos) for example scripts showcasing Keysharp's
cross-platform image, overlay, OCR, input and window-management APIs.

## Quick Start

1. Install Keysharp for your platform (see [Install](#install)).
2. Create a script named `hello.ks`:
   ```ahk
   MsgBox("Hello from Keysharp!")
   ```
3. Double-click it, or run it from a terminal:
   ```sh
   keysharp hello.ks
   ```

Keysharp runs `.ahk`/`.ks` source scripts and `.cks` compiled scripts. It also ships **Keyview**, a lightweight editor with live validation, running, and `.cks` compilation — open it without a file for an autosaved scratchpad.

For per-platform setup, permissions, portable/standalone runs, command-line switches, and building from source, see the [detailed reference](docs/reference.md).

## Install

Prebuilt packages are on the [Releases](https://github.com/keysharp-org/Keysharp/releases) page. All platforms require the [.NET 10 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (the Linux and macOS installers help set this up).

### Windows

- **Install:** download and run the MSI. It associates `.ks`/`.cks` files, adds **Edit** (open in Keyview) and **Compile** right-click actions, and can add Keysharp to `PATH`.
- **Run:** double-click a script, or `Keysharp.exe hello.ks`.
- **Uninstall:** Settings → Apps → Installed apps.
- **VS Code:** with thqby's *AutoHotkey v2 Language Support* extension, set the interpreter path to your `Keysharp.exe` (usually `C:\Program Files\Keysharp\Keysharp.exe`).

### Linux

- **Install on Debian/Ubuntu:** download the single `keysharp-*-deb-bundle.tar.gz`, extract it, and run `sudo bash ./install.sh`. It contains Keysharp plus the independent [`keysharp-input`](https://github.com/keysharp-org/keysharp-input) and [`keysharp-desktop`](https://github.com/keysharp-org/keysharp-desktop) packages, so there are no separate downloads. On other distributions, use the regular Linux tarball. Compatible system components are reused instead of reinstalled.
- **Run:** `keysharp hello.ks`.
- **Uninstall:** use `sudo apt remove keysharp` for the Debian bundle, or `sudo bash ./uninstall.sh` for the tar channel. Both remove only Keysharp. They never remove `keysharp-input`, `keysharp-desktop`, or shared permission grants; the package manager or each component's separate uninstaller owns that lifecycle.
- **VS Code:** the extension needs an `.exe` name, so create a shim:
  ```sh
  mkdir -p ~/.local/bin && ln -sf "$(command -v keysharp)" ~/.local/bin/AutoHotkey.exe
  ```
  Then use `~/.local/bin/AutoHotkey.exe` as the interpreter path.

### macOS

- **Requirements:** macOS 15 or later. Download `osx-arm64` for Apple Silicon or `osx-x64` for an Intel Mac.
- **Install:** Keysharp is not signed or notarized, so macOS Gatekeeper blocks the download — the DMG may refuse to open and the apps may report that the file **"is damaged and can't be opened"** (it isn't). Clear the download's quarantine flag first, then install:

  ```bash
  # Use the actual file you downloaded (the .pkg works the same way).
  xattr -dr com.apple.quarantine ~/Downloads/keysharp-*-osx-*.dmg
  ```

  Then open the **DMG** and double-click `Install.command` (copies the apps and optionally adds the `keysharp`/`keyview` terminal commands and VS Code shim), or run the **PKG** for a system-wide install. If an installed app still won't launch, clear it there too:

  ```bash
  xattr -dr com.apple.quarantine /Applications/Keysharp.app /Applications/Keyview.app
  ```
- **Run:** `keysharp hello.ks`, or use `Keyview.app`. Keysharp requests **Input Monitoring**, **Accessibility**, and **Screen Recording** permissions as features need them — grant them in System Settings → Privacy & Security.
- **Uninstall:** double-click `Uninstall.command` in the DMG, or run `sudo keysharp-uninstall` for a PKG install.
- **VS Code:** answer "Yes" to the shim prompt during install, then use `~/.local/bin/AutoHotkey.exe` as the interpreter path.
- **GUI editing shortcuts:** GUIs are automatically given a minimal macOS menu bar (an App menu plus the standard Edit menu), so ⌘C/⌘V/⌘X/⌘A/⌘Z work in their text controls. Add the `-AppMenu` option (e.g. `MyGui := Gui("-AppMenu")`) to opt out; it has no effect on Windows/Linux.

> thqby's VS Code extension targets AutoHotkey on Windows: editing and running scripts work cross-platform, but Windows-specific debugging, help, and compiler integration do not. See the [full setup guide](docs/reference.md) for portable runs, permissions, and platform details.

## Platform Compatibility

`Full` means the feature is generally usable; `Partial` means known gaps remain. See the [full capability matrix](docs/capabilities.md) and [per-platform details](docs/reference.md).

| Capability | Windows | Linux | macOS |
|---|---|---|---|
| Parser and script execution | Full | Full | Full |
| File and directory operations | Full | Partial | Partial |
| Hotkeys, hotstrings, and input | Full | Partial | Partial |
| GUI windows and window automation | Full | Partial | Partial |
| Screen capture and pixel functions | Full | Partial | Partial |
| Registry and COM APIs | Full | Unsupported | Unsupported |

## Building From Source

Keysharp targets .NET 10. Linux and macOS builds also need the sibling [Keysharp Eto fork](https://github.com/keysharp-org/Eto/tree/Keysharp) cloned next to this repository.

| Platform | Command |
|---|---|
| Windows | Open `Keysharp.sln` in Visual Studio 2022 (with the .NET 10 SDK) and build. `Keysharp.Install\package-windows.ps1` produces the MSI and portable ZIP in `dist\`; add `-Msix` for the Microsoft Store package. |
| Linux | `bash ./Keysharp.Install/package-linux.sh` → output in `dist/` |
| macOS | `bash ./Keysharp.Install/package-macos.sh` → output in `dist/` |

The Windows MSI is built with [WiX v5](https://wixtoolset.org/), whose toolset restores from NuGet, so the
.NET SDK is the only prerequisite — Visual Studio is not needed to package. Pass
`-RuntimeIdentifier win-arm64` for an ARM64 build. Full step-by-step instructions for each platform are in
the [detailed reference](docs/reference.md#building-from-source-on-windows).

## Documentation

- [Detailed reference](docs/reference.md) — full setup, permissions, command-line switches, building from source, and AutoHotkey v2 differences
- [Capability matrix](docs/capabilities.md) — per-feature, per-platform status
- [AutoHotkey v2 docs](https://www.autohotkey.com/docs/v2/) — the language Keysharp implements

## Acknowledgements

Keysharp builds on work from IronAHK, AutoHotkey, Eto.Forms, Scintilla, NAudio, and many other open-source projects and community contributors. See the [full acknowledgements](docs/reference.md#code-acknowledgements).

## Contributing and Support

Please use the [issue tracker](https://github.com/keysharp-org/Keysharp/issues) for bug reports, compatibility gaps, and feature requests.
