{
  description = "Keysharp packages, NixOS module, and development shell";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/e5bdc4a41d4c072fe1e3787eaa0320a384741d44";
    eto = {
      url = "github:keysharp-org/Eto/d2f0dbe939972550f3ae865c07aa0c305ee7eb24";
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
      packageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.callPackage ./nix/package.nix {
          src = self;
          etoSrc = eto;
        };
    in
    {
      packages = forAllSystems (
        system:
        rec {
          keysharp = packageFor system;
          default = keysharp;
        }
      );

      # `nix flake check` evaluates every output above and builds this one. It composes the module into a
      # real NixOS system and stops at the derivation, so a broken option or udev rule fails here without
      # anyone waiting for a system closure to build.
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
          keysharp = self.packages.${system}.default;
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
              keysharpEtoParent="''${XDG_CACHE_HOME:-$HOME/.cache}/keysharp"
              keysharpEtoCache="$keysharpEtoParent/${etoCacheKey}"
              if [[ ! -f "$keysharpEtoCache/.keysharp-source-complete" ]]; then
                mkdir -p "$keysharpEtoCache"
                chmod -R u+w "$keysharpEtoCache"
                cp -R ${eto}/. "$keysharpEtoCache/"
                chmod -R u+w "$keysharpEtoCache"
                touch "$keysharpEtoCache/.keysharp-source-complete"
              fi
              export EtoRoot="$keysharpEtoCache"
            '';
          };
        }
      );

      nixosModules = rec {
        default =
          { pkgs, ... }:
          {
            imports = [
              (import ./nix/module.nix {
                keysharpPackage = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
              })
            ];
          };
        keysharp = default;
      };
    };
}
