# Keysharp on NixOS and COSMIC

NixOS packaging and COSMIC support are experimental. Use the NixOS modules for Keysharp and its two optional helpers. The Linux release installer is for other distributions; see [Installing on Linux](reference.md#installing-on-linux).

## Enable flakes

If flakes are not enabled, add this to `/etc/nixos/configuration.nix`:

```nix
nix.settings.experimental-features = [ "nix-command" "flakes" ];
```

Apply it with `sudo nixos-rebuild switch` before using the flake below.

## Install on a host that has `configuration.nix`

Keep the existing `/etc/nixos/configuration.nix`. Create `/etc/nixos/flake.nix` below. The output name is fixed, and NixOS reads the architecture from `hardware-configuration.nix`. The example uses NixOS 26.05. For another release, run `nixos-version` and use its first two numbers in the Nixpkgs URL (for example, `nixos-25.11`). If you already have a host flake, add these inputs and modules to it.

```nix
{
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-26.05";
  inputs.keysharp.url = "github:keysharp-org/Keysharp";
  inputs.keysharp-input.url = "github:keysharp-org/keysharp-input";
  inputs.keysharp-desktop.url = "github:keysharp-org/keysharp-desktop";

  outputs = { nixpkgs, keysharp, keysharp-input, keysharp-desktop, ... }: {
    nixosConfigurations.default = nixpkgs.lib.nixosSystem {
      modules = [
        ./configuration.nix
        keysharp.nixosModules.default
        keysharp-input.nixosModules.default
        keysharp-desktop.nixosModules.default
        ({ pkgs, ... }: {
          programs.keysharp.enable = true;
          services.keysharp-input.enable = true;
          services.keysharp-input.package =
            keysharp-input.packages.${pkgs.stdenv.hostPlatform.system}.default;
          services.keysharp-desktop.enable = true;
        })
      ];
    };
  };
}
```

Keep the `services.keysharp-input.package` line; the input module currently requires it. Keysharp needs keysharp-input client ABI 0.4 or newer and keysharp-desktop client ABI 0.8 or newer. Keep the generated `/etc/nixos/flake.lock`; it pins the exact revisions used for rebuilds.

If updating an existing host flake, refresh the application inputs before switching:

```sh
sudo nix flake update keysharp keysharp-input keysharp-desktop --flake /etc/nixos
```

Apply the configuration:

```sh
sudo nixos-rebuild switch --flake /etc/nixos#default
```

Log out and back in after switching so GNOME refreshes its app search and shows Keysharp and Keyview. The desktop module enables the GNOME or Cinnamon extension once for each user on their first graphical login. Check all three components as your desktop user:

```sh
env -u DISPLAY -u WAYLAND_DISPLAY keysharp --version
keysharp-input info
keysharp-input probe
keysharp-desktop probe
```

The `env` command makes the version print in the terminal instead of an informational dialog. `keysharp-input info` should report `client_abi_minor=4` or higher. A first permission-scoped action may prompt for polkit authorization. Enable `services.desktopManager.cosmic.enable = true` only on a COSMIC host.

## What the modules install

`programs.keysharp.enable` installs the .NET application and its ordinary runtime libraries. It also enables AT-SPI support and, by default, the `i2c-dev` module and display-controller-only DDC/CI uaccess rule for external monitor brightness and VCP control. The two service options separately install:

- `services.keysharp-input`, which provides `keysharp-input.service`, its client library, evdev/uinput setup, polkit action, and access to the shared permission namespace.
- `services.keysharp-desktop`, which provides the system `keysharp-desktop-authority.socket`, the supervised per-user `keysharp-desktop.service`, compositor providers, one-time extension setup on GNOME and Cinnamon, its polkit action, and access to the same shared permission namespace.

The service settings are independent of `programs.keysharp.enable`. Removing Keysharp does not remove a service that another module still enables.

Individual privileged facilities can be disabled when they are not needed:

```nix
services.keysharp-input.enable = false;
services.keysharp-desktop.enable = false;
programs.keysharp.monitorControl.enable = false;
```

Disabling `services.keysharp-desktop` removes its compositor providers and permission authority. Direct helper-backed desktop operations then fail closed. Screenshot-portal fallback remains available where the desktop portal permits it and follows the portal's own policy.

## COSMIC-specific setup

The standard NixOS COSMIC desktop module already enables the desktop portal and supplies both `xdg-desktop-portal-cosmic` and `xdg-desktop-portal-gtk`. A custom COSMIC setup which does not use that module needs the equivalent configuration:

```nix
xdg.portal = {
  enable = true;
  extraPortals = with pkgs; [
    xdg-desktop-portal-cosmic
    xdg-desktop-portal-gtk
  ];
  configPackages = [ pkgs.xdg-desktop-portal-cosmic ];
};
```

On COSMIC, Keysharp first probes the staging `ext-image-copy-capture` and output-source protocols. When available, it requests the screen-capture capability from `keysharp-desktop`, captures each intersecting output, and composes the requested region while accounting for output scale and rotation. An explicit denial is authoritative and is not bypassed through the portal. If the native protocol is absent or cannot be opened, Keysharp falls back to the portal's Screenshot interface; that request follows the portal's policy rather than the `keysharp-desktop` grant.

The currently supported COSMIC portal has no RemoteDesktop path for Keysharp's global input work, so installing the portal packages does not replace `keysharp-input`. XWayland can help X11 applications run inside the session, but does not turn the COSMIC session into X11 or bypass its Wayland restrictions.

## Build from a local source checkout

For a local source checkout:

```sh
nix build .#keysharp
nix run .#keysharp -- hello.ks
nix develop
```

The checkout's committed `flake.lock` pins Eto and Nixpkgs. Run `nix flake update --refresh eto` only when deliberately updating the Eto revision, then commit the revised lock file. The development shell supplies .NET 10 and the managed application's Linux development/runtime libraries, and puts a writable copy of the resolved Keysharp Eto fork in the user cache, exported through `EtoRoot`. MSBuild writes `obj` data beside Eto's project files while flake inputs themselves are immutable. Native component development uses the shells in the two standalone repositories.

### Launch the GUI test from VS Code

The workspace's **C#: Keysharp** F5 configuration builds Keysharp and runs `Keysharp.Tests/Code/Gui/guitest.ks`. The Microsoft C# extension's debugger uses a generic Linux executable, so enable NixOS's compatibility loader in your host configuration:

```nix
programs.nix-ld.enable = true;
```

Apply the host configuration and log out and back in so VS Code receives the loader environment. Quit all existing VS Code windows, then start it from the development shell so Keysharp can find GTK and X11 libraries:

```sh
nix develop --command code .
```

Select **C#: Keysharp** in Run and Debug, then press F5. Starting `code .` while another VS Code process is running can reuse that process without the development shell environment.

Real-machine COSMIC smoke test:

1. Apply the host configuration, then run `keysharp-input probe` and confirm `systemctl status keysharp-input.socket keysharp-input.service keysharp-desktop-authority.socket` succeeds.
2. In the COSMIC session, confirm the portal services with `systemctl --user status xdg-desktop-portal.service xdg-desktop-portal-cosmic.service`, then run a script using `PixelGetColor` or `Image.FromDesktop`. After the first request, confirm `systemctl --user status keysharp-desktop.service`. With the direct protocol available, authenticate the first `keysharp-desktop` grant and check regions spanning outputs with different scales or rotations. On a session without the native protocol, confirm the portal fallback follows the portal's policy.
3. Run the Dash and Window Spy against a disposable application window. Check title, active state, geometry, activation, maximize/minimize, and close; protocol capability advertisements decide which actions COSMIC accepts.
4. Run a simple global `F12` hotkey, complete the first `keysharp-input` polkit authentication, then test `SendText` into a disposable editor. Inspect `journalctl -u keysharp-input.service` if either fails. Avoid testing `BlockInput` until the hook is stable; `Backspace+Escape+Enter` is the daemon's native panic chord.

Remaining COSMIC limitations are the absence of compositor protocols for authoritative global cursor position, window stacking order (overlapping background windows are ambiguous), foreign-process identity, reserved work-area bounds, and general foreign-window move/resize/always-on-top operations. Control discovery remains best-effort through AT-SPI. Mouse hooks intentionally avoid raw-grabbing touchpads, touchscreens, and tablets because replaying their evdev stream would bypass COSMIC/libinput gesture processing; `BlockInput` can still grab them when explicitly requested. The portal fallback is a whole-desktop round trip and is slower than direct image-copy capture. Multi-output composition and scale/rotation handling in the direct path are implemented but remain unverified on real COSMIC hardware. All COSMIC-specific behavior remains provisional until exercised on a real session.
