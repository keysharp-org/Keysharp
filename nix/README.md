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

`flake.nix` pins nixpkgs and Eto to exact revisions. Every other packager builds the tip of Eto's
`Keysharp` branch, so move the Eto pin when that moves.

`.github/workflows/nixos.yml` builds the package, checks what it contains, and regenerates `deps.json` to
compare against the committed one.

`docs/linux-nixos.md` covers installing and running Keysharp on NixOS.
