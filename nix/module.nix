{
  config,
  lib,
  pkgs,
  keysharpPackage,
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
      default = keysharpPackage.override { audioSupport = cfg.audio.enable; };
      defaultText = lib.literalExpression "keysharpPackage.override { audioSupport = config.programs.keysharp.audio.enable; }";
      description = "Keysharp package to install.";
    };

    audio.enable = lib.mkOption {
      type = lib.types.bool;
      default = config.services.pulseaudio.enable
        || (config.services.pipewire.enable && config.services.pipewire.pulse.enable);
      defaultText = lib.literalExpression "config.services.pulseaudio.enable || (config.services.pipewire.enable && config.services.pipewire.pulse.enable)";
      description = "Include audio client libraries and tools in the default Keysharp package.";
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
