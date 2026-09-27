{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  glib,
  gtk3,
  gdk-pixbuf,
  cairo,
  pango,
  at-spi2-core,
  libnotify,
  libayatana-appindicator,
  libgdiplus,
  libxkbcommon,
  libx11,
  libxcb,
  libxcomposite,
  libxext,
  libxfixes,
  libxinerama,
  libxrandr,
  libxt,
  libxtst,
  wayland,
  systemd,
  bash,
  coreutils,
  eject,
  gawk,
  gnugrep,
  iproute2,
  pulseaudio,
  util-linux,
  wrapGAppsHook3,
  xdg-utils,
  xinput,
  src,
  etoSrc,
  audioSupport ? false,
  x11Support ? true,
  waylandSupport ? true,
}:

let
  runtimeLibraries = [
    glib
    gtk3
    gdk-pixbuf
    cairo
    pango
    at-spi2-core
    libnotify
    libayatana-appindicator
    libgdiplus
    libxkbcommon
  ]
  ++ lib.optional audioSupport pulseaudio
  ++ lib.optional waylandSupport wayland
  ++ lib.optionals x11Support [
    libx11
    libxtst
    libxinerama
    libxrandr
    libxfixes
    libxcomposite
    libxext
    libxt
    libxcb
  ];
  # Everything the runtime shells out to: gio (FileRecycle), eject (Drive), findmnt (Drive labels),
  # pactl/paplay (Sound), xinput with awk/grep (device enumeration), ip (net queries), xdg-open/xdg-mime,
  # systemctl, and the bash those command strings are run through.
  runtimePrograms = [
    bash
    coreutils
    eject
    gawk
    glib
    gnugrep
    iproute2
    systemd
    util-linux
    xdg-utils
  ]
  ++ lib.optional audioSupport pulseaudio
  ++ lib.optional x11Support xinput;

  versionMatch = builtins.match ".*<KeysharpVersion[^>]*>([0-9.]+)</KeysharpVersion>.*" (
    builtins.readFile (src + "/Directory.Build.props")
  );
in
buildDotnetModule rec {
  pname = "keysharp";
  # Taken from the property every other packager builds against, so the two cannot drift.
  version =
    if versionMatch == null then
      throw "nix/package.nix: Directory.Build.props declares no <KeysharpVersion>."
    else
      lib.head versionMatch;

  inherit src;

  # Keep Eto's committed T4 outputs newer than their templates to avoid an offline tool restore.
  postPatch = ''
    cp -R ${etoSrc} ../Eto
    chmod -R u+w ../Eto
    for template in $(find ../Eto -name '*.tt'); do
      generated="$(dirname "$template")/$(basename "$template" .tt).cs"
      if [ -f "$generated" ]; then
        touch "$generated"
      fi
    done
  '';

  projectFile = [
    "Keysharp/Keysharp.csproj"
    "Keyview/Keyview.csproj"
  ];
  # Runtime-loaded components need an explicit restore before the offline publish builds them.
  testProjectFile = [
    "Keysharp.Components/Scripting/Compiler/Keysharp.Components.Scripting.Compiler.csproj"
    "Keysharp.Components/Scripting/Parser/Keysharp.Components.Scripting.Parser.csproj"
  ];
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnet-sdk.runtime;

  # buildDotnetModule otherwise limits MSBuild to one worker.
  enableParallelBuilding = true;

  nativeBuildInputs = [ wrapGAppsHook3 ];
  runtimeDeps = runtimeLibraries;
  # buildDotnetModule's own wrapper already applies gappsWrapperArgs.
  dontWrapGApps = true;

  # Payload staging runs Keysharp and needs GTK and a writable home. Its optional kpm download is offline.
  postInstall = ''
    export LD_LIBRARY_PATH="${lib.makeLibraryPath runtimeLibraries}:''${LD_LIBRARY_PATH:-}"
    export DOTNET_ROOT="${dotnet-sdk}/share/dotnet"
    HOME=$(mktemp -d) dotnet msbuild Keysharp.Install/payload/Keysharp.Payload.proj \
      -p:PayloadDir="$out/lib/keysharp" --nologo -v:minimal

    if [[ ! -f "$out/lib/keysharp/Keysharp.cks" && ! -f "$out/lib/keysharp/Keysharp.ks" ]]; then
      echo "Payload staging produced neither Keysharp.cks nor Keysharp.ks; the Dash would be missing." >&2
      exit 1
    fi

    if [[ -f "$out/lib/keysharp/Scripts/AtSpi.ks" ]]; then
      if [[ ! -f "$out/lib/keysharp/Scripts/AtSpi.cks" ]]; then
        echo "AT-SPI inspector precompilation failed." >&2
        exit 1
      fi
      mkdir -p "$out/lib/keysharp/Lib"
      mv "$out/lib/keysharp/Scripts/AtSpi.ks" "$out/lib/keysharp/Lib/AtSpi.ks"
    fi

    ln -s Keysharp "$out/lib/keysharp/keysharp"
    ln -s Keyview "$out/lib/keysharp/keyview"
    mkdir -p "$out/bin"

    install -Dm644 Keysharp.Install/linux/keysharp.desktop \
      "$out/share/applications/keysharp.desktop"
    install -Dm644 Keysharp.Install/linux/keyview.desktop \
      "$out/share/applications/keyview.desktop"
    substituteInPlace "$out/share/applications/keysharp.desktop" \
      --replace-fail "/usr/local/bin/keysharp" "keysharp"
    substituteInPlace "$out/share/applications/keyview.desktop" \
      --replace-fail "/usr/local/bin/keyview" "keyview"

    install -Dm644 Keysharp.Install/linux/keysharp.xml \
      "$out/share/mime/packages/keysharp.xml"
    install -Dm644 assets/Keysharp.png \
      "$out/share/icons/hicolor/256x256/apps/keysharp.png"
    install -Dm644 license.txt "$out/share/licenses/keysharp/license.txt"
  '';

  executables = [
    "keysharp"
    "keyview"
  ];
  makeWrapperArgs = [
    "--prefix"
    "PATH"
    ":"
    (lib.makeBinPath runtimePrograms)
  ];

  doCheck = false;

  passthru = {
    inherit audioSupport x11Support waylandSupport runtimeLibraries runtimePrograms;
  };

  meta = {
    description = "Cross-platform C# implementation of AutoHotkey v2";
    homepage = "https://github.com/keysharp-org/Keysharp";
    license = lib.licenses.bsd2;
    mainProgram = "keysharp";
    platforms = [
      "x86_64-linux"
      "aarch64-linux"
    ];
  };
}
