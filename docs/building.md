# Build Keysharp

Install the .NET 10 SDK. On Linux and macOS, clone the Eto fork beside this checkout:

```sh
git clone -b Keysharp --recurse-submodules https://github.com/keysharp-org/Eto.git ../Eto
dotnet build Keysharp.sln -c Debug
```

Windows builds only need the `dotnet build` command. The output is under
`bin/Debug/net10.0-windows/` on Windows or `bin/Debug/net10.0/` on Linux.
The Linux brokers are runtime dependencies for their respective features; building
Keysharp does not build or install them. Install them using
[Linux setup](install-linux.md) or build them from their own repositories.

Architecture is selected with a runtime identifier, such as `-r linux-arm64` or
`-r win-arm64`. Keep the managed platform as AnyCPU.

## Create release packages

Run the packaging script on the target operating system and architecture:

| Platform | Command | Output |
| --- | --- | --- |
| Windows | `pwsh Keysharp.Install/package-windows.ps1` | MSI and ZIP in `dist/` |
| Linux | `bash Keysharp.Install/package-linux.sh` | Archive and Debian package in `dist/` |
| macOS | `bash Keysharp.Install/package-macos.sh` | PKG and DMG in `dist/` |

Windows packaging restores WiX from NuGet. On Linux, generating the Debian package
requires `dpkg-dev`; the Launchpad source uploads are described in
[Keysharp.Install/ppa/README.md](../Keysharp.Install/ppa/README.md).

macOS packaging uses Apple's `pkgbuild`, `productbuild` and `hdiutil`. The PKG
installs the apps for all users or only the current user, and its optional
terminal-command, VS Code and launch choices are payload-free packages whose
postinstall runs `Keysharp.Install/macos/install.sh`. The DMG holds the PKG and
`uninstall.sh` as **Uninstall Keysharp.command**; the same uninstaller is also
in `Keysharp.app/Contents/Resources` for the dashboard. See
[macOS installation](reference.md#installing-on-macos).

For local macOS builds, the script reuses a `Keysharp` signing identity when it
exists. Create one with `bash Keysharp.Install/macos/create-signing-cert.sh`;
`APP_CERT` selects another identity, `AUTO_SIGN=false` disables automatic selection,
`ADHOC_SIGN=true` requests ad-hoc signing, and `SKIP_SIGN=true` skips signing. A
stable self-signed identity helps keep the app's permission identity across local
updates. Local self-signed, ad-hoc-signed, and unsigned builds are not notarized and
can still be blocked by Gatekeeper.

Public notarized distribution requires a **Developer ID Application** certificate
for all apps and the DMG, a **Developer ID Installer** certificate for the PKG,
and Apple notarization credentials. `APP_CERT`, `INSTALLER_CERT`, and
`NOTARY_PROFILE` select these; successful notarization also requires valid
signatures, hardened runtime, and secure timestamps. See
[Apple's notarization requirements](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).
The existing Keysharp and Keyview bundle layout mixes managed files and resources
in `Contents/MacOS` and uses `codesign --deep` when signing. This route has not been
validated for Developer ID notarization; certificate settings alone do not prove
that a package is ready for public distribution. Apple recommends separating
code from resources and signing nested code individually, from the inside out:
[macOS Code Signing In Depth](https://developer.apple.com/library/archive/technotes/tn2206/_index.html).

See [the reference](reference.md) for platform-specific prerequisites and packaging
options. Use the individual scripts' `--help` (or PowerShell parameter help) for flags.

For tests, follow [Keysharp.Tests/TESTING.md](../Keysharp.Tests/TESTING.md).
Use the curated filter or a narrower noninteractive test; a full unfiltered test
run includes tests that require user input.
