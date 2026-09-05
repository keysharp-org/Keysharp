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
    libgdiplus
    libxkbcommon
    wayland
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
    pulseaudio
    systemd
    util-linux
    xdg-utils
    xinput
  ];

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

  # Eto's T4 outputs are committed beside their templates, and its Transform target is incremental. The
  # copy gives every file the same fresh timestamp, so whether the target reruns comes down to which file
  # cp happened to touch last - and rerunning it means `dotnet tool restore`, which the sandbox has no
  # network for. Making each output newer than its template keeps the committed source, deterministically.
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
  # Restored and built but not published: the scripting components are loaded at runtime, so nothing
  # references them at compile time and restoring the two apps never reaches them. Publishing Keysharp
  # still builds them (Directory.Build.targets stages their payload), which fails offline without this.
  testProjectFile = [
    "Keysharp.Components/Scripting/Compiler/Keysharp.Components.Scripting.Compiler.csproj"
    "Keysharp.Components/Scripting/Parser/Keysharp.Components.Scripting.Parser.csproj"
  ];
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnet-sdk.runtime;

  nativeBuildInputs = [ wrapGAppsHook3 ];
  runtimeDeps = runtimeLibraries;
  # buildDotnetModule's own wrapper already applies gappsWrapperArgs.
  dontWrapGApps = true;

  # The payload step runs the host just published here, so it needs both the GTK libraries and a writable
  # HOME. Its kpm download has no network in the sandbox; that is a warning by design and costs the
  # bundled package manager, not the build.
  postInstall = ''
    export LD_LIBRARY_PATH="${lib.makeLibraryPath runtimeLibraries}:''${LD_LIBRARY_PATH:-}"
    HOME=$(mktemp -d) dotnet msbuild Keysharp.Install/payload/Keysharp.Payload.proj \
      -p:PayloadDir="$out/lib/keysharp" --nologo -v:minimal

    if [[ ! -f "$out/lib/keysharp/Keysharp.cks" && ! -f "$out/lib/keysharp/Keysharp.ks" ]]; then
      echo "Payload staging produced neither Keysharp.cks nor Keysharp.ks; the Dash would be missing." >&2
      exit 1
    fi

    if [[ -f "$out/lib/keysharp/Scripts/AtSpi.ks" ]]; then
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
    inherit runtimeLibraries runtimePrograms;
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
