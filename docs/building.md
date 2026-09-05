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

Windows packaging restores WiX from NuGet. Linux packaging also needs `rsync` and
`ripgrep`; generating a Debian package requires `dpkg-deb`.
See [the reference](reference.md) for platform-specific prerequisites and packaging
options. Use the individual scripts' `--help` (or PowerShell parameter help) for flags.

For tests, follow [Keysharp.Tests/TESTING.md](../Keysharp.Tests/TESTING.md).
Use the curated filter or a narrower noninteractive test; a full unfiltered test
run includes tests that require user input.
