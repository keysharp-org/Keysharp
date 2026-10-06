# Install Keysharp on Linux

## Install from the Ubuntu PPA

On Ubuntu 24.04 (noble), Ubuntu 26.04 (resolute), and derivatives using those bases
(including Pop!_OS 24.04), prefer the
[Launchpad PPA](https://launchpad.net/~descolada/+archive/ubuntu/keysharp) for amd64
and arm64. Apt keeps installed packages updated:

```sh
sudo apt install software-properties-common
sudo add-apt-repository ppa:descolada/keysharp
sudo apt update
sudo apt install keysharp
```

Keysharp recommends both components, so apt installs available compatible providers
unless told not to install recommended packages. On GNOME or Cinnamon, then run
`keysharp-desktop enable-extension` as yourself, not with `sudo`, and log out and
back in if it asks you to.

The PPA targets Ubuntu releases, not Debian or Ubuntu 22.04-based distributions
such as Pop!_OS 22.04. Use the release installer below on those systems.
Check the Ubuntu base in `/etc/os-release` (`UBUNTU_CODENAME`) on a derivative;
do not substitute a different Ubuntu codename to make an unsupported base use the PPA.

When switching from portable components to the PPA, run their portable uninstallers
first. The portable `keysharp-input` v1.0.0 uninstaller leaves its ABI 1 library
behind; see [portable component removal](#uninstall) for the extra cleanup.

## Install from a release

Download `keysharp-linux-setup.sh` from a
[release](https://github.com/keysharp-org/Keysharp/releases), then run:

```sh
sudo sh ./keysharp-linux-setup.sh
keysharp --version
```

Setup installs Keysharp system-wide, together with the optional `keysharp-input`
and `keysharp-desktop` components by default. It verifies every download before
installing anything and chooses one of these channels:

- On systems with both `apt-get` and `dpkg`, it installs `.deb` packages through apt.
- On other supported systems, it runs the projects' archive installers under
  `/usr/local`.

If Keysharp is already installed under `/usr/local`, setup keeps using the archive
channel. A healthy compatible component also keeps its existing channel, so its
channel can differ from Keysharp's. Setup prints the selected Keysharp channel and
the detected channel for each component.

## Update, repair or customize

For PPA installations, update through apt:

```sh
sudo apt update
sudo apt install --only-upgrade keysharp keysharp-input keysharp-desktop
```

For release installations, rerun setup to update or reinstall Keysharp and to
install or repair missing or unhealthy components:

```sh
sudo sh ./keysharp-linux-setup.sh
```

A healthy component with a compatible client ABI stays at its installed version.
Preview the plan without changing the machine, or request component updates too:

```sh
sh ./keysharp-linux-setup.sh --dry-run
sudo sh ./keysharp-linux-setup.sh --upgrade-components
```

- `--skip-input` or `--skip-desktop` skips that component for this run. It does not
  uninstall an existing component.
- `--keysharp-version`, `--input-version` and `--desktop-version` select a release
  for this run. A version may include its `v` prefix.
- `--channel deb|tar` forces Keysharp's channel and the default for missing
  components. Existing components remain on their detected channels.

Downloaded packages do not add an update repository, so rerun setup when you want
a newer release, or install from the PPA instead.

## Diagnose

If apt cannot find a PPA package, refresh its index with `sudo apt update`, then
check `apt-cache policy keysharp keysharp-input keysharp-desktop`. Confirm that
the configured PPA series matches your Ubuntu base. A source upload listed on
Launchpad becomes installable only after its binary builds succeed and are published.

Run setup's offline diagnosis without `sudo` to see the Keysharp channel and each
component's channel, version, client ABI and service health:

```sh
sh ./keysharp-linux-setup.sh --diagnose
```

Run the component probes as your graphical user to inspect live device and desktop
capabilities:

```sh
keysharp-input probe
keysharp-desktop probe
```

Rerun setup to repair a `deb` or `tar` installation. Components reported as `nix`
or `system` must be repaired or upgraded through their owning configuration or
package manager. Avoid mixing package and archive copies of the same project.
GNOME or Cinnamon extensions may need a logout after installation or upgrade.

## Requirements and alternatives

System-wide installation needs root; `--diagnose` and `--dry-run` do not. Setup
supports x64 and ARM64 Linux and requires `curl` and `sha256sum`. The archive
channel also needs `bash` and `tar`.
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

On NixOS, use the [NixOS and COSMIC guide](linux-nixos.md) instead of the setup
script. Arch users can alternatively install the
[`keysharp-git` AUR package](https://aur.archlinux.org/packages/keysharp-git).

## Uninstall

For a system-wide installation created by setup, check its channels before removing
anything:

```sh
sh ./keysharp-linux-setup.sh --diagnose
```

If it reports `Keysharp channel: deb`, remove the package with:

```sh
sudo apt remove keysharp
```

If it reports `Keysharp channel: tar`, download and extract the Keysharp Linux
archive matching the installed version and architecture from the
[Releases](https://github.com/keysharp-org/Keysharp/releases) page. Setup removes
its temporary copy after installation, so you need to download the archive again
unless you kept one. From the extracted `keysharp-linux-*` directory, run:

```sh
sudo bash ./uninstall.sh
```

For any other archive installation, run the same uninstaller with the same privilege
level and `PREFIX` used to install it. A per-user installation does not need `sudo`.
Remove NixOS, AUR and other package-manager installations through their owner
instead.

Removing Keysharp does not remove the independently installed components because
other applications may use them. If you also want to remove a component, follow
the `channel=` value reported for that component:

- For `deb`, remove it through apt. Components installed by setup use
  `sudo apt remove keysharp-input keysharp-desktop`; a retained compatible provider
  may have a different package name.
- For `tar`, run `sudo /usr/local/share/doc/keysharp-input/uninstall.sh` and/or
  `sudo /usr/local/share/doc/keysharp-desktop/uninstall.sh`.
- For `nix` or `system`, remove it through the configuration or package manager
  that owns it.

After running the portable `keysharp-input` v1.0.0 uninstaller, remove its remaining
ABI 1 library files before installing the Debian or PPA package:

```sh
sudo rm -f -- /usr/local/lib/libkeysharp-input.so.1 /usr/local/lib/libkeysharp-input.so.1.*
sudo ldconfig
```

The uninstallers retain shared permission grants under
`/var/lib/keysharp-permissions/v1`.

## VS Code

For thqby's AutoHotkey v2 extension, create the interpreter shim it expects:

```sh
mkdir -p ~/.local/bin
ln -sf "$(command -v keysharp)" ~/.local/bin/AutoHotkey.exe
```

Set its interpreter path to `~/.local/bin/AutoHotkey.exe`. Windows-specific debugging,
help and compiler integration are unavailable. [Build instructions](building.md)
are separate from installation.
