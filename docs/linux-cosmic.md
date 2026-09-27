# Keysharp on COSMIC

For NixOS installation, see the [NixOS guide](linux-nixos.md). For other distributions, see [Installing on Linux](reference.md#installing-on-linux).

## Portals and capture

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

When the compositor also exposes the foreign-toplevel capture-source protocol, `Image.FromWindow` captures the window directly, including pixels covered by another window. The compositor chooses the capture extent; `Decorations` does not change it on this backend.

For Keysharp-owned GTK windows, screen coordinates use the compositor's frame geometry and GTK's shadow inset when the surface rectangle is unavailable.

The currently supported COSMIC portal has no RemoteDesktop path for Keysharp's global input work, so installing the portal packages does not replace `keysharp-input`. XWayland can help X11 applications run inside the session, but does not turn the COSMIC session into X11 or bypass its Wayland restrictions.

## Tray icons

Keysharp publishes its tray image and menu through AppIndicator. The image, menu callbacks, menu-triggered exit, and native item disposal have been verified on COSMIC 1.2.0.

COSMIC 1.2.0 can retain stale entries when a process with several tray items exits: its [watcher removes only the first matching entry](https://github.com/pop-os/cosmic-applets/blob/epoch-1.2.0/cosmic-applet-status-area/src/subscriptions/status_notifier_watcher/server.rs). Dead entries can appear as gear icons with no working menu. Keysharp's test host therefore builds menus without publishing desktop icons. To clear entries already retained by the desktop, restart the watcher:

```sh
systemctl --user restart com.system76.CosmicStatusNotifierWatcher.service
```

## Real-machine smoke test

1. Apply the host configuration, then run `keysharp-input probe` and confirm `systemctl status keysharp-input.socket keysharp-input.service keysharp-desktop-authority.socket` succeeds.
2. In the COSMIC session, confirm the portal services with `systemctl --user status xdg-desktop-portal.service xdg-desktop-portal-cosmic.service`, then run a script using `PixelGetColor` or `Image.FromDesktop`. After the first request, confirm `systemctl --user status keysharp-desktop.service`. With the direct protocol available, authenticate the first `keysharp-desktop` grant and check regions spanning outputs with different scales or rotations. On a session without the native protocol, confirm the portal fallback follows the portal's policy.
3. Run the Dash and Window Spy against a disposable application window. Check title, active state, geometry, activation, maximize/minimize, and close; protocol capability advertisements decide which actions COSMIC accepts.
4. Run a simple global `F12` hotkey, complete the first `keysharp-input` polkit authentication, then test `SendText` into a disposable editor. Inspect `journalctl -u keysharp-input.service` if either fails. Avoid testing `BlockInput` until the hook is stable; `Backspace+Escape+Enter` is the daemon's native panic chord.

Remaining COSMIC limitations are the absence of compositor protocols for authoritative global cursor position, window stacking order (overlapping background windows are ambiguous), foreign-process identity, reserved work-area bounds, and general foreign-window move/resize/always-on-top operations. Control discovery remains best-effort through AT-SPI. Mouse hooks intentionally avoid raw-grabbing touchpads, touchscreens, and tablets because replaying their evdev stream would bypass COSMIC/libinput gesture processing; `BlockInput` can still grab them when explicitly requested. The portal fallback is a whole-desktop round trip and is slower than direct image-copy capture. Multi-output composition and scale/rotation handling in the direct path are implemented but remain unverified on real COSMIC hardware. All COSMIC-specific behavior remains provisional until exercised on a real session.
