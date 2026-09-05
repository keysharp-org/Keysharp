# Install Keysharp on Linux

Download `keysharp-linux-setup.sh` and `SHA256SUMS` from a
[release](https://github.com/keysharp-org/Keysharp/releases). In that directory,
verify the script with either command before running it:

```sh
sha256sum --check --ignore-missing SHA256SUMS
gh attestation verify keysharp-linux-setup.sh --repo keysharp-org/Keysharp
```

```sh
sudo sh ./keysharp-linux-setup.sh
keysharp hello.ks
```

Setup is a system-wide installer and requires root when it changes the machine;
`--diagnose` and `--dry-run` remain unprivileged. It installs Keysharp and its
optional input and desktop components from each project's own release. It verifies
every download before installing anything.
Debian and Ubuntu use packages; other supported systemd distributions use archives.
Archive dependency checks run before application files are changed. GNOME or
Cinnamon extensions may need a logout after installation or upgrade.

## Select or update components

A healthy component with a compatible client ABI is kept. ABI compatibility,
installation health and release version are separate checks. Keysharp needs
input ABI 0.2+ and desktop ABI 0.8+ within ABI major 0;
downloaded artifacts are checked too, including explicitly selected versions.
Setup discovers root-protected installations under `/usr`, `/usr/local` and the Nix system profile.
It preserves each component's install channel; Nix and other system packages must
be repaired or upgraded through their owner.

```sh
sh ./keysharp-linux-setup.sh --diagnose
sh ./keysharp-linux-setup.sh --dry-run
sudo sh ./keysharp-linux-setup.sh --upgrade-components
```

`--diagnose` reads local metadata and service state without network access,
permission dialogs or starting services. Run each component's `probe` command as
your graphical user for live compositor/device capabilities.
`--dry-run` resolves releases and prints the plan without downloading artifacts.
`--input-version` and `--desktop-version` pin component releases even when an
installed ABI is compatible.
The version may include its `v` prefix. `--keysharp-version` selects Keysharp;
`--channel deb|tar` selects its channel and the default for missing components.

Use `--skip-input` or `--skip-desktop` to omit a component. Keysharp runs without
them; their operations are unavailable. Each project's own `.deb` and archive
installs just that project. Downloaded packages do not configure an update
repository. Rerun setup with `--upgrade-components` to receive broker fixes and
new capabilities; ordinary setup deliberately retains healthy existing brokers.

## Requirements and alternatives

Setup needs `curl`, `sha256sum`, root privileges and an x64 or ARM64 Linux system.
The archive channel also needs `bash` and `tar`.
The broker services need systemd and polkit. The archive installers support apt,
dnf, zypper and pacman for runtime dependencies. On another distribution, install
the documented dependencies through its package manager first.
Prebuilt native brokers target glibc 2.35 or newer.

For an unprivileged or portable Keysharp installation, extract its archive and
run `app/Keysharp`, or run its `install.sh` without sudo to install under `~/.local`.
Install the .NET 10 runtime and the runtime libraries listed in
[the platform reference](reference.md#linux-platform-support) first. Broker services have their own
installation requirements; they are not installed by Keysharp's individual archive.

If an explicitly selected older broker archive has no `check-runtime.sh`, the
combined setup stops before installing files. Install that version through its own
documented installer after satisfying its dependencies.

## Diagnose and repair

```sh
sh ./keysharp-linux-setup.sh --diagnose
keysharp-input probe
keysharp-desktop probe
```

The diagnosis reports the install channel, product version, client ABI and service health
separately. A compatible library does not prove the service is installed correctly.
Repair missing files through their owning installer; repair disabled services
using the [input instructions](https://github.com/keysharp-org/keysharp-input/blob/main/docs/install.md)
or [desktop instructions](https://github.com/keysharp-org/keysharp-desktop/blob/main/docs/install.md).
Avoid mixing package and archive copies of the same project.

## Remove

Remove a package with `sudo apt remove keysharp`. For an archive installation,
run the archive's `uninstall.sh` using the same privilege level and `PREFIX` as
the installation. Neither removes the independently installed brokers or shared
permission grants. Use each component's package manager or its own uninstaller
when you want to remove it too.

## VS Code

For thqby's AutoHotkey v2 extension, create the interpreter shim it expects:

```sh
mkdir -p ~/.local/bin
ln -sf "$(command -v keysharp)" ~/.local/bin/AutoHotkey.exe
```

Set its interpreter path to `~/.local/bin/AutoHotkey.exe`. Windows-specific debugging,
help and compiler integration are unavailable. [Build instructions](building.md)
are separate from installation.
