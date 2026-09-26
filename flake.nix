{
  description = "Keysharp packages, NixOS module, and development shell";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/e5bdc4a41d4c072fe1e3787eaa0320a384741d44";
    eto = {
      url = "github:keysharp-org/Eto/Keysharp";
      flake = false;
    };
  };

  outputs =
    { self, nixpkgs, eto, ... }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
      pkgsFor = system: import nixpkgs { inherit system; };
    in
    {
      packages = forAllSystems (
        system:
        rec {
          keysharp = (pkgsFor system).callPackage ./nix/package.nix {
            src = self;
            etoSrc = eto;
          };
          default = keysharp;
        }
      );

      # Evaluate a complete NixOS configuration without building its system closure.
      checks = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
          machine = nixpkgs.lib.nixosSystem {
            modules = [
              self.nixosModules.default
              {
                nixpkgs.hostPlatform = system;
                programs.keysharp.enable = true;
                boot.loader.grub.enable = false;
                fileSystems."/" = {
                  device = "/dev/disk/by-label/nixos";
                  fsType = "ext4";
                };
                system.stateVersion = nixpkgs.lib.trivial.release;
              }
            ];
          };
        in
        {
          nixos-module = pkgs.runCommand "keysharp-nixos-module-eval" { } ''
            echo ${builtins.unsafeDiscardStringContext machine.config.system.build.toplevel.drvPath} > $out
          '';
        }
      );

      apps = forAllSystems (system: {
        default = {
          type = "app";
          program = "${self.packages.${system}.default}/bin/keysharp";
        };
        keyview = {
          type = "app";
          program = "${self.packages.${system}.default}/bin/keyview";
        };
      });

      devShells = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
          keysharp = self.packages.${system}.default.override { audioSupport = true; };
          etoCacheKey = builtins.baseNameOf (toString eto);
        in
        {
          default = pkgs.mkShell {
            packages =
              (with pkgs; [
                dotnetCorePackages.sdk_10_0
                glib
                gtk3
              ])
              ++ keysharp.passthru.runtimePrograms;
            LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath keysharp.passthru.runtimeLibraries;
            shellHook = ''
              if [[ -z "''${EtoRoot:-}" ]]; then
                export EtoRoot="''${XDG_CACHE_HOME:-$HOME/.cache}/keysharp/${etoCacheKey}"
                if [[ ! -f "$EtoRoot/.keysharp-source-complete" ]]; then
                  mkdir -p "$EtoRoot"
                  chmod -R u+w "$EtoRoot"
                  cp -R ${eto}/. "$EtoRoot/"
                  chmod -R u+w "$EtoRoot"
                  touch "$EtoRoot/.keysharp-source-complete"
                fi
              fi
            '';
          };
        }
      );

      nixosModules = rec {
        default =
          { config, lib, pkgs, ... }:
          import ./nix/module.nix {
            inherit config lib pkgs;
            keysharpPackage = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
          };
        keysharp = default;
      };
    };
}
