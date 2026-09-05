# Keysharp on NixOS and COSMIC

NixOS packaging and COSMIC session support are experimental. For every other distribution see the "Installing on Linux" section of [reference.md](reference.md).

NixOS should use flakes rather than the tarball installer: systemd, polkit, udev and compositor integration belong in the system configuration. Keysharp, `keysharp-input`, and `keysharp-desktop` remain separate flake inputs and modules. Pin native revisions whose client ABIs satisfy Keysharp's current minimums (input 0.2+ and desktop 0.8+); until compatible release tags exist, use reviewed branch revisions and commit `flake.lock`. Product versions do not need to match across the three projects.

Add the input and module to the host flake:

```nix
{
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  inputs.keysharp.url = "github:keysharp-org/Keysharp";
  inputs.keysharp.inputs.nixpkgs.follows = "nixpkgs";
  inputs.keysharp-input.url = "github:keysharp-org/keysharp-input";
  inputs.keysharp-input.inputs.nixpkgs.follows = "nixpkgs";
  inputs.keysharp-desktop.url = "github:keysharp-org/keysharp-desktop";
  inputs.keysharp-desktop.inputs.nixpkgs.follows = "nixpkgs";

  outputs = { nixpkgs, keysharp, keysharp-input, keysharp-desktop, ... }: {
    nixosConfigurations.my-host = nixpkgs.lib.nixosSystem {
      system = "x86_64-linux";
      modules = [
        keysharp.nixosModules.default
        keysharp-input.nixosModules.default
        keysharp-desktop.nixosModules.default
        {
          services.desktopManager.cosmic.enable = true;
          programs.keysharp.enable = true;
          services.keysharp-input.enable = true;
          services.keysharp-desktop.enable = true;
        }
      ];
    };
  };
}
```

All three inputs follow the host's nixpkgs revision and are pinned by `flake.lock`.

`programs.keysharp.enable` installs the .NET application and its ordinary runtime libraries. The two service options separately install:

- `services.keysharp-input`, which provides `keysharp-input.service`, its client library, evdev/uinput setup, polkit action, and access to the shared permission namespace.
- `services.keysharp-desktop`, which provides the system `keysharp-desktop-authority.socket`, the supervised per-user `keysharp-desktop.service`, compositor providers, its polkit action, and access to the same shared permission namespace.
- The `i2c-dev` module and display-controller-only DDC/CI uaccess rule for external-monitor brightness and VCP control.
- AT-SPI support for accessibility-backed caret and control queries.

The service settings are independent of `programs.keysharp.enable`. Removing Keysharp does not remove a service that another module still enables.

Individual privileged facilities can be disabled when they are not needed:

```nix
services.keysharp-input.enable = false;
services.keysharp-desktop.enable = false;
programs.keysharp.monitorControl.enable = false;
```

Disabling `services.keysharp-desktop` removes its compositor providers and permission authority. Direct helper-backed desktop operations then fail closed. Screenshot-portal fallback remains available where the desktop portal permits it and follows the portal's own policy.

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

For a local source checkout:

```sh
nix build .#keysharp
nix run .#keysharp -- hello.ks
nix develop
```

The development shell supplies .NET 10 and the managed application's Linux development/runtime libraries, and puts a writable copy of the pinned Keysharp Eto fork in the user cache, exported through `EtoRoot`. This is necessary because MSBuild writes `obj` data beside Eto's project files while flake inputs themselves are immutable. Native component development uses the shells in the two standalone repositories.

Real-machine COSMIC smoke test:

1. Apply the host configuration, then run `keysharp-input probe` and confirm `systemctl status keysharp-input.socket keysharp-input.service keysharp-desktop-authority.socket` succeeds.
2. In the COSMIC session, confirm the portal services with `systemctl --user status xdg-desktop-portal.service xdg-desktop-portal-cosmic.service`, then run a script using `PixelGetColor` or `Image.FromDesktop`. After the first request, confirm `systemctl --user status keysharp-desktop.service`. With the direct protocol available, authenticate the first `keysharp-desktop` grant and check regions spanning outputs with different scales or rotations. On a session without the native protocol, confirm the portal fallback follows the portal's policy.
3. Run the Dash and Window Spy against a disposable application window. Check title, active state, geometry, activation, maximize/minimize, and close; protocol capability advertisements decide which actions COSMIC accepts.
4. Run a simple global `F12` hotkey, complete the first `keysharp-input` polkit authentication, then test `SendText` into a disposable editor. Inspect `journalctl -u keysharp-input.service` if either fails. Avoid testing `BlockInput` until the hook is stable; `Backspace+Escape+Enter` is the daemon's native panic chord.

Remaining COSMIC limitations are the absence of compositor protocols for authoritative global cursor position, window stacking order (overlapping background windows are ambiguous), foreign-process identity, reserved work-area bounds, and general foreign-window move/resize/always-on-top operations. Control discovery remains best-effort through AT-SPI. Mouse hooks intentionally avoid raw-grabbing touchpads, touchscreens, and tablets because replaying their evdev stream would bypass COSMIC/libinput gesture processing; `BlockInput` can still grab them when explicitly requested. The portal fallback is a whole-desktop round trip and is slower than direct image-copy capture. Multi-output composition and scale/rotation handling in the direct path are implemented but remain unverified on real COSMIC hardware. All COSMIC-specific behavior remains provisional until exercised on a real session.
