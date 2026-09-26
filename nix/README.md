# Nix packaging notes

`deps.json` lists the NuGet packages the build downloads ahead of time, since the Nix sandbox has no
network. Regenerate it whenever a project reference changes or the nixpkgs pin moves:

```sh
nix build '.#keysharp.fetch-deps'
./result nix/deps.json
nix build .#keysharp
```

Use that command rather than editing the file by hand. It knows which packages the .NET SDK already
provides, and listing one of those here fails the build.

`flake.nix` pins nixpkgs and uses a reviewed Eto revision in the committed `flake.lock`.
Update Eto deliberately when validating a new revision:

```sh
nix flake update --refresh eto
```

Commit the resulting `flake.lock` with the change. Keeping the lock file in the repository makes
local builds and fresh installations resolve the same inputs. CI updates Eto for its compatibility
check without changing the committed lock file.

CI has a NixOS leg that builds the package and checks what it contains, but it is not part of the push
gate: run the CI workflow manually and pick `nixos` (or `all`) after touching these expressions, a project
reference, or the install payload. The same thing locally is:

```sh
nix flake check -L --all-systems
nix build .#keysharp
```

`docs/linux-nixos.md` covers installing and running Keysharp on NixOS.

## Package overrides

The package accepts these `.override` arguments:

| Argument | Default | Includes |
| --- | --- | --- |
| `audioSupport` | `false` | PulseAudio client library and tools, also usable with PipeWire |
| `x11Support` | `true` | X11 client libraries and `xinput` |
| `waylandSupport` | `true` | Wayland client library |

The NixOS module selects audio support from the host's PulseAudio/PipeWire settings;
`programs.keysharp.audio.enable` overrides that choice. The development shell includes
audio support. Display support is selected at runtime. These flags do not enable a server
or compositor. Disabling a flag removes Keysharp's direct dependency; GTK may retain it
transitively.

For a custom package, add a module like this to your host flake's `modules` list:

```nix
({ pkgs, ... }: {
  programs.keysharp.package =
    keysharp.packages.${pkgs.stdenv.hostPlatform.system}.default.override {
      audioSupport = true;
    };
})
```

An explicit `programs.keysharp.package` takes precedence over the module's audio selection.
The helpers have equivalent `services.keysharp-input.package` and
`services.keysharp-desktop.package` options.

## Local Eto development

The development shell uses the locked Eto revision by default. To build against an editable
sibling checkout instead:

```sh
EtoRoot="$PWD/../Eto" nix develop
```

For a Nix package build against that checkout without updating the committed lock:

```sh
nix build .#keysharp --override-input eto path:../Eto --no-write-lock-file
```
