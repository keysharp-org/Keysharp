# Keysharp on NixOS

Use the NixOS modules for Keysharp and its optional input and desktop helpers. NixOS packaging is experimental.

## Install

If flakes are not enabled, add this to `/etc/nixos/configuration.nix` and run `sudo nixos-rebuild switch`:

```nix
nix.settings.experimental-features = [ "nix-command" "flakes" ];
```

Keep your existing `configuration.nix` and `hardware-configuration.nix`. Create `/etc/nixos/flake.nix`:

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
        {
          programs.keysharp.enable = true;
          services.keysharp-input.enable = true;
          services.keysharp-desktop.enable = true;
        }
      ];
    };
  };
}
```

Use the NixOS release reported by `nixos-version` in the Nixpkgs URL; the example uses 26.05. The architecture comes from your hardware configuration, and `default` is a fixed configuration name.

For an existing host flake, add the three Keysharp inputs, module imports and enable settings, keeping its Nixpkgs input and configuration name. If `/etc/nixos` is tracked by Git, add the new `flake.nix` before rebuilding.

```sh
sudo nixos-rebuild switch --flake /etc/nixos#default
```

Log out and back in. The desktop module automatically enables its GNOME or Cinnamon extension on the first graphical login; later user changes are preserved. Keysharp and Keyview should appear in app search.

Check the installation as your desktop user:

```sh
env -u DISPLAY -u WAYLAND_DISPLAY keysharp --version
keysharp-input info
keysharp-input probe
keysharp-desktop probe
```

The first command prints the version without a dialog. Keysharp requires client ABI 1.x for both helpers, with desktop protocol 3. The first permission-scoped action may prompt for authorization.

## Update

Keep `/etc/nixos/flake.lock`; it records the exact revisions installed. To update the applications:

```sh
sudo nix flake update keysharp keysharp-input keysharp-desktop --flake /etc/nixos
sudo nixos-rebuild switch --flake /etc/nixos#default
```

Use your existing configuration name instead of `default` if you already had a host flake.

## Optional features

| Setting | Default |
| --- | --- |
| `programs.keysharp.audio.enable` | Includes audio clients when the host enables PulseAudio or PipeWire's PulseAudio support |
| `programs.keysharp.monitorControl.enable` | `true`; loads `i2c-dev` and installs the DDC/CI access rule |
| `services.keysharp-desktop.autoEnableExtension` | `true`; enables the GNOME or Cinnamon extension once |

Set either helper's `enable` option to `false` to omit it. Input hooks and synthesis need keysharp-input; supported desktop capture, window and clipboard operations use keysharp-desktop. See the [Linux support matrix](reference.md#linux-platform-support).

Both X11 and Wayland clients are included; you do not select a session type. Keysharp does not enable a compositor or audio server. For custom packages, see [package overrides](../nix/README.md#package-overrides).

For COSMIC, use NixOS's standard `services.desktopManager.cosmic.enable` module, which configures its portals. See [COSMIC setup and limitations](linux-cosmic.md).

## Build and develop

From a Keysharp checkout:

```sh
nix build .#keysharp
nix run .#keysharp -- hello.ks
nix develop
dotnet restore Keysharp.sln
dotnet build Keysharp/Keysharp.csproj -c Debug --no-restore
```

The development shell supplies .NET, native libraries (including audio clients), and a writable copy of the pinned Eto source. The standalone package omits audio clients by default; [package overrides](../nix/README.md#package-overrides) can enable them.

### VS Code / F5

The Microsoft C# debugger needs NixOS's compatibility loader and ICU. Add this to your host configuration and rebuild:

```nix
programs.nix-ld = {
  enable = true;
  libraries = [ pkgs.icu ];
};
```

Log out and back in after enabling the loader. Quit all VS Code windows, then launch from the checkout:

```sh
nix develop --command dotnet restore Keysharp.sln
nix develop --command code .
```

Select **C#: Keysharp** and press F5 to run `guitest.ks`. After changing development dependencies, quit all VS Code windows and launch this way again. Local Nix edits need no flake update; updating an upstream input does.
