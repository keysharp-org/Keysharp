{ keysharpPackage }:
{
  config,
  lib,
  pkgs,
  ...
}:

let
  cfg = config.programs.keysharp;

  monitorUdevRules = builtins.readFile ../Keysharp.Install/linux/70-keysharp-i2c-uaccess.rules;
in
{
  options.programs.keysharp = {
    enable = lib.mkEnableOption "Keysharp automation runtime and editor";

    package = lib.mkOption {
      type = lib.types.package;
      default = keysharpPackage;
      defaultText = lib.literalExpression "inputs.keysharp.packages.${pkgs.stdenv.hostPlatform.system}.default";
      description = "Keysharp package to install.";
    };

    monitorControl.enable = lib.mkOption {
      type = lib.types.bool;
      default = true;
      description = ''
        Load i2c-dev and install Keysharp's display-controller-only uaccess rule
        for DDC/CI monitor brightness and VCP control.
      '';
    };
  };

  config = lib.mkIf cfg.enable {
    environment.systemPackages = [ cfg.package ];
    services.gnome.at-spi2-core.enable = lib.mkDefault true;

    boot.kernelModules = lib.optional cfg.monitorControl.enable "i2c-dev";

    services.udev.extraRules = lib.optionalString cfg.monitorControl.enable monitorUdevRules;
  };
}
