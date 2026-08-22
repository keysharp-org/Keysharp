#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
# -E (errtrace) makes the ERR trap below fire for failures inside functions too, not just top-level commands.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Release}"
DIST_DIR="${ROOT}/dist"
ETO_DIR="$(cd "${ROOT}/../Eto" 2>/dev/null && pwd || true)"
PATH_MAP="${ROOT}=/_/keysharp"
if [[ -n "${ETO_DIR}" ]]; then
  PATH_MAP="${PATH_MAP}%2c${ETO_DIR}=/_/Eto"
fi

detect_default_rid() {
  case "$(uname -m)" in
    arm64) echo "osx-arm64" ;;
    x86_64) echo "osx-x64" ;;
    *)
      echo "Unable to infer macOS RID from architecture $(uname -m). Set RID=osx-arm64 or RID=osx-x64." >&2
      return 1
      ;;
  esac
}

RID="${RID:-$(detect_default_rid)}"
PUBLISH_DIR="${DIST_DIR}/publish/${RID}"
STAGING_DIR="${DIST_DIR}/staging/${RID}"
PACKAGE_ROOT_DIR="${DIST_DIR}/package-root"
PKG_NAME="Keysharp-${RID}"
PKG_ROOT="${PACKAGE_ROOT_DIR}/${PKG_NAME}"
SCRIPTS_DIR="${STAGING_DIR}/${PKG_NAME}-scripts"
PKG_OUT="${DIST_DIR}/${PKG_NAME}.pkg"
DMG_STAGING_DIR="${DIST_DIR}/dmg-staging/${RID}"
DMG_OUT="${DIST_DIR}/${PKG_NAME}.dmg"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${ROOT}/Keysharp/Keysharp.csproj" | head -n 1)}"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"
APP_CERT="${APP_CERT:-}"
INSTALLER_CERT="${INSTALLER_CERT:-}"
NOTARY_PROFILE="${NOTARY_PROFILE:-}"
ENTITLEMENTS="${ENTITLEMENTS:-${ROOT}/Keysharp.Install/macos/keysharp.entitlements}"
SKIP_PUBLISH="${SKIP_PUBLISH:-false}"
SKIP_SIGN="${SKIP_SIGN:-false}"
SKIP_NOTARIZE="${SKIP_NOTARIZE:-false}"
ADHOC_SIGN="${ADHOC_SIGN:-false}"
# When APP_CERT is unset, auto-use a stable local self-signed identity (created by
# Keysharp.Install/macos/create-signing-cert.sh) if one exists, instead of ad-hoc/unsigned. A stable code
# signature is what lets granted TCC permissions (Accessibility, Input Monitoring) survive rebuilds/updates.
AUTO_SIGN="${AUTO_SIGN:-true}"
AUTO_SIGN_IDENTITY="${AUTO_SIGN_IDENTITY:-Keysharp}"
PKG_IDENTIFIER="${PKG_IDENTIFIER:-org.keysharp.pkg}"
UNINSTALL_SCRIPT="${ROOT}/Keysharp.Install/macos/uninstall.sh"
INSTALL_SCRIPT="${ROOT}/Keysharp.Install/macos/install.command"

log() {
  printf '%s\n' "$*"
}

die() {
  printf '%s\n' "$*" >&2
  exit 1
}

# Name of the high-level stage currently running, so an unexpected failure (e.g. codesign rejecting a
# bundle) reports exactly where it broke instead of aborting with only the failing tool's own message.
CURRENT_STEP="initializing"

# Fires (via set -E) on any unhandled non-zero command, including failures deep inside functions. Prints a
# loud banner naming the failing stage, the exact command, and its location so the break is impossible to miss.
on_error() {
  local rc="$1" cmd="$2" src="$3" line="$4"
  trap - ERR   # disarm so this reports once, not once per unwinding frame
  local red='' rst=''
  if [[ -t 2 ]]; then red=$'\033[1;31m'; rst=$'\033[0m'; fi
  {
    printf '\n%s======================================================================%s\n' "${red}" "${rst}"
    printf '%sERROR: package-macos.sh failed while: %s%s\n' "${red}" "${CURRENT_STEP}" "${rst}"
    printf '  exit code : %s\n' "${rc}"
    printf '  command   : %s\n' "${cmd}"
    printf '  location  : %s:%s\n' "${src##*/}" "${line}"
    printf '  No .pkg / .dmg were produced — fix the above and re-run.\n'
    printf '%s======================================================================%s\n' "${red}" "${rst}"
  } >&2
}
# Pass $? / $BASH_COMMAND / line into the handler at the instant the trap fires — they change as soon as
# the handler starts running its own commands, so they can't be read reliably inside on_error itself.
trap 'on_error "$?" "$BASH_COMMAND" "${BASH_SOURCE[0]}" "${LINENO}"' ERR

# Runs one pipeline stage: records its name (for on_error) and logs a progress header, so the last
# "==> ..." line printed is always the stage that failed.
run_step() {
  CURRENT_STEP="$1"
  shift
  log ""
  log "==> ${CURRENT_STEP}..."
  "$@"
}

is_true() {
  case "$1" in
    1|[Tt][Rr][Uu][Ee]|[Yy][Ee][Ss]|[Oo][Nn]) return 0 ;;
    *) return 1 ;;
  esac
}

require_tool() {
  command -v "$1" >/dev/null 2>&1 || die "Required tool not found: $1"
}

validate_inputs() {
  [[ "${RID}" == "osx-arm64" || "${RID}" == "osx-x64" ]] || die "Unsupported RID '${RID}'. Use osx-arm64 or osx-x64."
  [[ -n "${VERSION}" ]] || die "Unable to determine package version. Set VERSION explicitly."
  require_tool dotnet
  require_tool pkgbuild
  require_tool plutil
  require_tool rsync
  require_tool file
  require_tool hdiutil
  if ! is_true "${SKIP_SIGN}" || [[ -n "${INSTALLER_CERT}" ]]; then
    require_tool codesign
  fi
  if [[ -n "${INSTALLER_CERT}" ]]; then
    require_tool pkgutil
  fi
  if ! is_true "${SKIP_NOTARIZE}" && [[ -n "${NOTARY_PROFILE}" ]]; then
    require_tool xcrun
  fi

  if ! is_true "${SKIP_SIGN}" && [[ -n "${APP_CERT}" && ! -f "${ENTITLEMENTS}" ]]; then
    die "Entitlements file not found: ${ENTITLEMENTS}"
  fi

  [[ -f "${UNINSTALL_SCRIPT}" ]] || die "Uninstall script not found: ${UNINSTALL_SCRIPT}"
  [[ -f "${INSTALL_SCRIPT}" ]] || die "Install script not found: ${INSTALL_SCRIPT}"

  if ! is_true "${SKIP_NOTARIZE}" && [[ -n "${NOTARY_PROFILE}" && -z "${INSTALLER_CERT}" ]]; then
    die "NOTARY_PROFILE requires INSTALLER_CERT so the .pkg can be signed before notarization."
  fi

  if ! is_true "${SKIP_NOTARIZE}" && [[ -n "${NOTARY_PROFILE}" && -z "${APP_CERT}" ]]; then
    die "NOTARY_PROFILE requires APP_CERT so the app bundles can be signed before notarization."
  fi
}

publish_projects() {
  if is_true "${SKIP_PUBLISH}"; then
    log "Skipping publish because SKIP_PUBLISH=${SKIP_PUBLISH}."
    return
  fi

  log "Publishing Keysharp and Keyview (CONFIG=${CONFIG}, RID=${RID})..."
  for proj in Keysharp Keyview; do
    rm -rf "${PUBLISH_DIR}/${proj}"
    dotnet publish "${ROOT}/${proj}/${proj}.csproj" -c "${CONFIG}" -r "${RID}" \
      -o "${PUBLISH_DIR}/${proj}" \
      -p:KeysharpVersion="${VERSION}" \
      -p:Deterministic=true \
      -p:ContinuousIntegrationBuild=true \
      -p:PathMap="${PATH_MAP}"
  done

  # The Dash, its template, the demos and every .cks: install payload, so Keysharp.csproj does not carry
  # it. Runs against the just-published host, which is what makes each .cks match this build. Inside the
  # .app because that is where macOS publishes; the other two packagers publish a plain tree.
  log "Staging install payload..."
  dotnet msbuild "${ROOT}/Keysharp.Install/payload/Keysharp.Payload.proj" \
    -p:PayloadDir="$(resolve_app_source Keysharp)/Contents/MacOS" --nologo -v:minimal
}

resolve_app_source() {
  local name="$1"
  # The publish tree is what this script builds and ships; the build tree is only a fallback for a
  # hand-built bundle. A plain build puts that in bin/<config>/<tfm>/ on every platform, and only adds
  # a <rid>/ level when one was passed explicitly (-r), so accept both.
  local candidates=(
    "${PUBLISH_DIR}/${name}/${name}.app"
    "${ROOT}/bin/${CONFIG}/net10.0/${name}.app"
    "${ROOT}/bin/${CONFIG}/net10.0/${RID}/${name}.app"
  )

  for candidate in "${candidates[@]}"; do
    if [[ -d "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  die "Could not find ${name}.app. Expected one of: ${candidates[*]}"
}

plistbuddy() {
  /usr/libexec/PlistBuddy "$@"
}

set_plist_value() {
  local plist="$1"
  local key="$2"
  local type="$3"
  local value="$4"

  plistbuddy -c "Set :${key} ${value}" "${plist}" 2>/dev/null ||
    plistbuddy -c "Add :${key} ${type} ${value}" "${plist}"
}

set_bundle_metadata() {
  local app="$1"
  local plist="${app}/Contents/Info.plist"

  [[ -f "${plist}" ]] || die "Missing Info.plist: ${plist}"

  set_plist_value "${plist}" "CFBundleShortVersionString" string "${VERSION}"
  set_plist_value "${plist}" "CFBundleVersion" string "${VERSION}"
  set_plist_value "${plist}" "LSMinimumSystemVersion" string "15.0"
  set_plist_value "${plist}" "NSHumanReadableCopyright" string "Copyright 2020-Present Keysharp contributors"
  plutil -lint "${plist}" >/dev/null
}

add_document_types() {
  local app="$1"
  local plist="${app}/Contents/Info.plist"

  [[ -f "${plist}" ]] || die "Missing Info.plist: ${plist}"

  plistbuddy -c "Delete :CFBundleDocumentTypes" "${plist}" 2>/dev/null || true
  plistbuddy -c "Delete :UTExportedTypeDeclarations" "${plist}" 2>/dev/null || true

  plistbuddy -c "Add :CFBundleDocumentTypes array" "${plist}"

  plistbuddy -c "Add :CFBundleDocumentTypes:0 dict" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeName string 'Keysharp Script'" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeRole string Shell" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSHandlerRank string Owner" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0 string ahk" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions:1 string ks" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSItemContentTypes array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSItemContentTypes:0 string org.keysharp.script" "${plist}"

  plistbuddy -c "Add :CFBundleDocumentTypes:1 dict" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:CFBundleTypeName string 'Compiled Keysharp Script'" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:CFBundleTypeRole string Shell" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:LSHandlerRank string Owner" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:CFBundleTypeExtensions array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0 string cks" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:LSItemContentTypes array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:1:LSItemContentTypes:0 string org.keysharp.compiled-script" "${plist}"

  plistbuddy -c "Add :UTExportedTypeDeclarations array" "${plist}"

  plistbuddy -c "Add :UTExportedTypeDeclarations:0 dict" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeIdentifier string org.keysharp.script" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeDescription string 'Keysharp Script'" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeConformsTo array" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeConformsTo:0 string public.source-code" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeTagSpecification dict" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeTagSpecification:public.filename-extension array" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeTagSpecification:public.filename-extension:0 string ahk" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeTagSpecification:public.filename-extension:1 string ks" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:0:UTTypeTagSpecification:public.mime-type string application/x-keysharp" "${plist}"

  plistbuddy -c "Add :UTExportedTypeDeclarations:1 dict" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeIdentifier string org.keysharp.compiled-script" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeDescription string 'Compiled Keysharp Script'" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeConformsTo array" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeConformsTo:0 string public.data" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeTagSpecification dict" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeTagSpecification:public.filename-extension array" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeTagSpecification:public.filename-extension:0 string cks" "${plist}"
  plistbuddy -c "Add :UTExportedTypeDeclarations:1:UTTypeTagSpecification:public.mime-type string application/x-keysharp-compiled" "${plist}"

  plutil -lint "${plist}" >/dev/null
}

add_editor_document_types() {
  local app="$1"
  local plist="${app}/Contents/Info.plist"

  [[ -f "${plist}" ]] || die "Missing Info.plist: ${plist}"

  plistbuddy -c "Delete :CFBundleDocumentTypes" "${plist}" 2>/dev/null || true
  plistbuddy -c "Add :CFBundleDocumentTypes array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0 dict" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeName string 'Keysharp Script'" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeRole string Editor" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSHandlerRank string Alternate" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0 string ahk" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:CFBundleTypeExtensions:1 string ks" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSItemContentTypes array" "${plist}"
  plistbuddy -c "Add :CFBundleDocumentTypes:0:LSItemContentTypes:0 string org.keysharp.script" "${plist}"

  plutil -lint "${plist}" >/dev/null
}

clean_app_bundle() {
  local app="$1"
  local macos_dir="${app}/Contents/MacOS"

  # Strip debug symbols and per-assembly XML documentation — neither is used at runtime and a release
  # shouldn't carry them (Eto.xml alone is ~2 MB). A .xml is only removed when a same-named .dll sits
  # beside it (i.e. it's that assembly's doc file), so any genuine standalone .xml resource is preserved.
  find "${app}" -name '*.pdb' -delete
  find "${app}" -name '*.xml' -type f | while IFS= read -r xml; do
    [[ -e "${xml%.xml}.dll" ]] && rm -f "${xml}"
  done
  find "${macos_dir}" -type f \( \
    -name 'Keysharp.OutputTest' -o \
    -name 'Keysharp.OutputTest.dll' -o \
    -name 'Keysharp.OutputTest.deps.json' -o \
    -name 'Keysharp.OutputTest.runtimeconfig.json' \
  \) -delete

  find "${app}" -type d -exec chmod 0755 {} +
  find "${app}" -type f -exec chmod 0644 {} +

  find "${macos_dir}" -type f \( -name 'Keysharp' -o -name 'Keyview' -o -name '*.dylib' \) -exec chmod 0755 {} +
}

write_install_scripts() {
  mkdir -p "${SCRIPTS_DIR}"

  cat > "${SCRIPTS_DIR}/preinstall" <<'EOF'
#!/bin/sh

# Stop ALL running Keysharp/Keyview instances (the compile daemon AND any running scripts) before the
# bundle is replaced, not just the daemon: a lingering old-build instance keeps holding the global input
# hook and its granted permissions, so newly-launched scripts misbehave until it is killed. preinstall runs
# as root, so pkill -f reaches the desktop user's processes too (killall is a name-based fallback).
pkill -f 'Keysharp.app/Contents/MacOS/Keysharp' 2>/dev/null || true
pkill -f 'Keyview.app/Contents/MacOS/Keyview' 2>/dev/null || true
killall Keysharp Keyview 2>/dev/null || true

# Unregister any stale LaunchServices entries (dev builds, old installs) so that
# the installer's bundle-relocation search cannot redirect files into non-standard
# locations such as a developer's build output directory.
LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [ -x "${LSREGISTER}" ]; then
  for bundle in \
    /Applications/Keysharp.app \
    /Applications/Keyview.app \
    /usr/local/lib/Keysharp.app \
    /usr/local/lib/Keyview.app; do
    "${LSREGISTER}" -u "${bundle}" 2>/dev/null || true
  done
  # Purge any remaining registrations by bundle ID so dev-build paths are cleared.
  "${LSREGISTER}" -kill -seed 2>/dev/null || true
fi

rm -rf /Applications/Keysharp.app /Applications/Keyview.app
exit 0
EOF
  chmod 0755 "${SCRIPTS_DIR}/preinstall"

  cat > "${SCRIPTS_DIR}/postinstall" <<'EOF'
#!/bin/sh
set -e

mkdir -p /usr/local/bin

has_dotnet10() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft.NETCore.App 10\.'
}

install_dotnet10() {
  echo "Keysharp requires the .NET 10 runtime; installing it now..."

  local arch
  case "$(uname -m)" in
    arm64) arch="arm64" ;;
    x86_64) arch="x64" ;;
    *)
      echo "Warning: unrecognized architecture $(uname -m); cannot auto-install the .NET 10 runtime." >&2
      return 1
      ;;
  esac

  local script="/tmp/dotnet-install-$$.sh"
  if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${script}"; then
    return 1
  fi
  chmod +x "${script}"
  "${script}" --channel 10.0 --runtime dotnet --architecture "${arch}" --install-dir /usr/local/share/dotnet
  local result=$?
  rm -f "${script}"
  [ ${result} -eq 0 ] || return 1

  ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
}

if ! has_dotnet10; then
  install_dotnet10 || echo "Warning: could not auto-install the .NET 10 runtime. Install it manually from https://dotnet.microsoft.com/en-us/download/dotnet/10.0" >&2
fi

LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [ -x "${LSREGISTER}" ]; then
  "${LSREGISTER}" -f /Applications/Keysharp.app /Applications/Keyview.app >/dev/null 2>&1 || true
fi

# Offer optional extras via GUI prompts shown to the logged-in user, since
# this script runs as root with no terminal attached.
CONSOLE_USER="$(stat -f%Su /dev/console 2>/dev/null || true)"
if [ -n "${CONSOLE_USER}" ] && [ "${CONSOLE_USER}" != "root" ]; then
  CONSOLE_UID="$(id -u "${CONSOLE_USER}" 2>/dev/null || echo 0)"
  CONSOLE_HOME="$(dscl . -read "/Users/${CONSOLE_USER}" NFSHomeDirectory 2>/dev/null | awk '{print $2}')"

  # Launch Services registrations describe which applications can open a document, but registering
  # Keysharp and Keyview together does not reliably select the Owner over the Alternate after an upgrade.
  # Record an explicit per-user default after registration so reinstall order cannot make Keyview the
  # default for Keysharp scripts. Keep Keyview registered above so it remains available in Open With.
  launchctl asuser "${CONSOLE_UID}" sudo -u "${CONSOLE_USER}" \
    defaults write com.apple.LaunchServices/com.apple.launchservices.secure LSHandlers -array-add \
    '{ LSHandlerContentType = "org.keysharp.script"; LSHandlerRoleAll = "org.keysharp.keysharp"; }' \
    >/dev/null 2>&1 || echo "Warning: could not set Keysharp as the default script handler for ${CONSOLE_USER}." >&2

  # Remove TCC permission entries created under an incorrectly-cased bundle id (org.keysharp.Keysharp /
  # org.keysharp.Keyview) by earlier or ad-hoc-signed builds. The canonical ids are all-lowercase; the
  # mis-cased duplicates otherwise split the app's permissions across two identities (e.g. Input Monitoring
  # granted to one but read from the other). TCC is per-user, so reset as the console user. Harmless if the
  # entries don't exist.
  for badid in org.keysharp.Keysharp org.keysharp.Keyview; do
    launchctl asuser "${CONSOLE_UID}" sudo -u "${CONSOLE_USER}" tccutil reset All "${badid}" >/dev/null 2>&1 || true
  done

  ask_yes_no() {
    local prompt="$1"
    local result
    result="$(launchctl asuser "${CONSOLE_UID}" sudo -u "${CONSOLE_USER}" osascript -e "display dialog \"${prompt}\" buttons {\"No\", \"Yes\"} default button \"Yes\" with title \"Keysharp\"" 2>/dev/null || echo "button returned:No")"
    case "${result}" in *"Yes"*) return 0 ;; *) return 1 ;; esac
  }

  if ask_yes_no "Install the keysharp and keyview terminal commands in /usr/local/bin?"; then
    mkdir -p /usr/local/bin
    printf '#!/bin/sh\nexec "/Applications/Keysharp.app/Contents/MacOS/Keysharp" "$@"\n' > /usr/local/bin/keysharp
    printf '#!/bin/sh\nexec "/Applications/Keyview.app/Contents/MacOS/Keyview" "$@"\n' > /usr/local/bin/keyview
    chmod 0755 /usr/local/bin/keysharp /usr/local/bin/keyview
  fi

  if [ -n "${CONSOLE_HOME}" ] && ask_yes_no "Install the VS Code AutoHotkey v2 extension compatibility shim (~/.local/bin/AutoHotkey.exe)?"; then
    DEST="${CONSOLE_HOME}/.local/bin/AutoHotkey.exe"
    launchctl asuser "${CONSOLE_UID}" sudo -u "${CONSOLE_USER}" mkdir -p "$(dirname "${DEST}")"
    printf '#!/bin/sh\nexec "/Applications/Keysharp.app/Contents/MacOS/Keysharp" "$@"\n' > "${DEST}"
    chown "${CONSOLE_USER}" "${DEST}"
    chmod 0755 "${DEST}"
  fi
fi

exit 0
EOF
  chmod 0755 "${SCRIPTS_DIR}/postinstall"
}

relocate_library_scripts() {
  local app="$1"

  # The .cks (compiled) form stays in Scripts so the tray menu can launch it as
  # an inspector, while the .ks (source) form moves to Lib/ so #include <Ax>
  # resolves it as the standard library copy. The folder is capital "Lib" to match
  # the include resolver (it searches "<exeDir>/Lib").
  for dir in "${app}/Contents/MacOS" "${app}/Contents/Resources"; do
    if [[ -f "${dir}/Scripts/Ax.ks" ]]; then
      mkdir -p "${dir}/Lib"
      mv "${dir}/Scripts/Ax.ks" "${dir}/Lib/Ax.ks"
    fi
  done
}

verify_dash_present() {
  local macos_dir="$1/Contents/MacOS"

  # Opened with no document, Keysharp.app runs Keysharp.cks - the Dash - through the ordinary
  # <exe-name> probe. Neither it nor the Keysharp.ks fallback present means the icon opens an error.
  if [[ ! -f "${macos_dir}/Keysharp.cks" && ! -f "${macos_dir}/Keysharp.ks" ]]; then
    echo "Keysharp.app has neither Keysharp.cks nor Keysharp.ks. Opening it with no document would error instead of showing the Dash." >&2
    exit 1
  fi

  if [[ ! -f "${macos_dir}/Keysharp.cks" ]]; then
    echo "Warning: Keysharp.cks was not produced, so the Dash ships as source and is compiled in memory on every launch. Expected on a cross-RID publish; otherwise check the publish output for 'Could not precompile'." >&2
  fi
}

stage_payload() {
  local keysharp_app_source
  local keyview_app_source

  keysharp_app_source="$(resolve_app_source Keysharp)"
  keyview_app_source="$(resolve_app_source Keyview)"

  log "Staging package payload at ${PKG_ROOT}..."
  rm -rf "${PKG_ROOT}" "${SCRIPTS_DIR}"
  mkdir -p "${PKG_ROOT}/Applications" "${PKG_ROOT}/usr/local/bin"

  rsync -a "${keysharp_app_source}" "${PKG_ROOT}/Applications/"
  rsync -a "${keyview_app_source}" "${PKG_ROOT}/Applications/"

  relocate_library_scripts "${PKG_ROOT}/Applications/Keysharp.app"
  relocate_library_scripts "${PKG_ROOT}/Applications/Keyview.app"
  verify_dash_present "${PKG_ROOT}/Applications/Keysharp.app"

  set_bundle_metadata "${PKG_ROOT}/Applications/Keysharp.app"
  set_bundle_metadata "${PKG_ROOT}/Applications/Keyview.app"
  add_document_types "${PKG_ROOT}/Applications/Keysharp.app"
  add_editor_document_types "${PKG_ROOT}/Applications/Keyview.app"
  clean_app_bundle "${PKG_ROOT}/Applications/Keysharp.app"
  clean_app_bundle "${PKG_ROOT}/Applications/Keyview.app"

  install -m 0755 "${UNINSTALL_SCRIPT}" "${PKG_ROOT}/usr/local/bin/keysharp-uninstall"
  write_install_scripts
}

sign_macho_files() {
  local app="$1"
  local sign_identity="$2"
  local main_exe="${app}/Contents/MacOS/$(basename "${app}" .app)"
  local entitlements_arg=()

  if [[ -f "${ENTITLEMENTS}" ]]; then
    entitlements_arg=(--entitlements "${ENTITLEMENTS}")
  fi

  find "${app}/Contents/MacOS" -type f | while IFS= read -r file; do
    # Skip the bundle's main executable: pointing codesign at it signs the WHOLE bundle, which — before the
    # --deep seal in sign_app_bundle runs — fails on the loose non-code files in Contents/MacOS. The main
    # executable (with entitlements + hardened runtime) is signed by the bundle-level codesign there.
    [[ "${file}" == "${main_exe}" ]] && continue
    if file "${file}" | grep -q 'Mach-O'; then
      codesign --force --timestamp --options runtime "${entitlements_arg[@]}" --sign "${sign_identity}" "${file}"
    fi
  done
}

sign_app_bundle() {
  local app="$1"
  local sign_identity="$2"
  local entitlements_arg=()

  if [[ -f "${ENTITLEMENTS}" ]]; then
    entitlements_arg=(--entitlements "${ENTITLEMENTS}")
  fi

  log "Signing ${app}..."
  sign_macho_files "${app}" "${sign_identity}"
  # --deep is required for the bundle seal: .NET lays the whole app out flat in Contents/MacOS (managed
  # DLLs, *.json, Eto.xml, Icon.icns, plus Lib/Scripts/refs). Since Command Line Tools 26.5, codesign
  # treats every loose non-Mach-O file in Contents/MacOS as an unsigned nested "subcomponent" and refuses
  # the whole bundle ("code object is not signed at all / In subcomponent: ..."); --deep seals them instead.
  # NB: Apple's notary service rejects --deep, so a future Developer ID + notarization path must relocate
  # the payload out of Contents/MacOS (or sign each nested item individually) rather than rely on this.
  codesign --force --deep --timestamp --options runtime "${entitlements_arg[@]}" --sign "${sign_identity}" "${app}"
  codesign --verify --deep --strict --verbose=2 "${app}"
}

# Prefer a stable local self-signed identity over ad-hoc/unsigned when no cert was requested, so a plain
# `./package-macos.sh` produces a permission-stable build. Runs before validate_inputs so the selected
# identity flows through validation, entitlements and signing exactly like an explicit APP_CERT.
auto_select_app_cert() {
  [[ -n "${APP_CERT}" ]] && return 0            # an explicit cert always wins
  is_true "${SKIP_SIGN}" && return 0
  is_true "${ADHOC_SIGN}" && return 0           # honor an explicit ad-hoc request
  is_true "${AUTO_SIGN}" || return 0
  command -v security >/dev/null 2>&1 || return 0

  if security find-identity -v -p codesigning 2>/dev/null | grep -qF "\"${AUTO_SIGN_IDENTITY}\""; then
    APP_CERT="${AUTO_SIGN_IDENTITY}"
    log "APP_CERT not set; auto-using local self-signed identity \"${AUTO_SIGN_IDENTITY}\" (stable signature keeps TCC permissions across updates)."
  else
    log "APP_CERT not set and no \"${AUTO_SIGN_IDENTITY}\" signing identity found."
    log "  Tip: run ./Keysharp.Install/macos/create-signing-cert.sh once so granted permissions persist across updates."
  fi
}

sign_apps_if_requested() {
  local sign_identity="${APP_CERT}"

  if is_true "${SKIP_SIGN}"; then
    log "Skipping app signing because SKIP_SIGN=${SKIP_SIGN}."
    return
  fi

  if [[ -z "${sign_identity}" ]]; then
    if is_true "${ADHOC_SIGN}"; then
      sign_identity="-"
      log "APP_CERT not set; using ad-hoc app signing because ADHOC_SIGN=${ADHOC_SIGN}."
    else
      log "APP_CERT not set; leaving app bundles unsigned."
      return
    fi
  fi

  sign_app_bundle "${PKG_ROOT}/Applications/Keysharp.app" "${sign_identity}"
  sign_app_bundle "${PKG_ROOT}/Applications/Keyview.app" "${sign_identity}"
}

write_component_plist() {
  local plist="${STAGING_DIR}/${PKG_NAME}-components.plist"
  mkdir -p "${STAGING_DIR}"
  cat > "${plist}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<array>
  <dict>
    <key>BundleHasStrictIdentifier</key><false/>
    <key>BundleIsRelocatable</key><false/>
    <key>BundleIsVersionChecked</key><false/>
    <key>BundleOverwriteAction</key><string>upgrade</string>
    <key>RootRelativeBundlePath</key><string>Applications/Keysharp.app</string>
  </dict>
  <dict>
    <key>BundleHasStrictIdentifier</key><false/>
    <key>BundleIsRelocatable</key><false/>
    <key>BundleIsVersionChecked</key><false/>
    <key>BundleOverwriteAction</key><string>upgrade</string>
    <key>RootRelativeBundlePath</key><string>Applications/Keyview.app</string>
  </dict>
</array>
</plist>
EOF
  printf '%s\n' "${plist}"
}

build_pkg() {
  local component_plist
  component_plist="$(write_component_plist)"

  local pkgbuild_args=(
    --root "${PKG_ROOT}"
    --component-plist "${component_plist}"
    --identifier "${PKG_IDENTIFIER}"
    --version "${VERSION}"
    --install-location /
    --scripts "${SCRIPTS_DIR}"
  )

  if [[ -n "${INSTALLER_CERT}" ]]; then
    pkgbuild_args+=(--sign "${INSTALLER_CERT}" --timestamp)
  fi

  log "Creating package ${PKG_OUT}..."
  rm -f "${PKG_OUT}"
  pkgbuild "${pkgbuild_args[@]}" "${PKG_OUT}"

  if [[ -n "${INSTALLER_CERT}" ]]; then
    pkgutil --check-signature "${PKG_OUT}"
  else
    log "INSTALLER_CERT not set; package is unsigned."
  fi
}

build_dmg() {
  log "Creating DMG ${DMG_OUT}..."
  rm -rf "${DMG_STAGING_DIR}"
  mkdir -p "${DMG_STAGING_DIR}"

  # Reuse the already-staged (and signed) app bundles from the .pkg payload.
  rsync -a "${PKG_ROOT}/Applications/Keysharp.app" "${DMG_STAGING_DIR}/"
  rsync -a "${PKG_ROOT}/Applications/Keyview.app" "${DMG_STAGING_DIR}/"

  # Standard "drag to Applications folder" symlink shown in every Mac DMG.
  ln -s /Applications "${DMG_STAGING_DIR}/Applications"

  # Double-clickable installer/uninstaller for users who install via drag-and-drop (no
  # terminal commands available to them otherwise).
  install -m 0755 "${INSTALL_SCRIPT}" "${DMG_STAGING_DIR}/Install.command"
  install -m 0755 "${UNINSTALL_SCRIPT}" "${DMG_STAGING_DIR}/Uninstall.command"

  rm -f "${DMG_OUT}"
  hdiutil create \
    -volname "Keysharp ${VERSION}" \
    -srcfolder "${DMG_STAGING_DIR}" \
    -ov \
    -format UDZO \
    "${DMG_OUT}"

  # Sign the DMG with the app cert so it can be notarized.
  if ! is_true "${SKIP_SIGN}" && [[ -n "${APP_CERT}" ]]; then
    log "Signing DMG..."
    codesign --force --timestamp --sign "${APP_CERT}" "${DMG_OUT}"
  elif ! is_true "${SKIP_SIGN}" && is_true "${ADHOC_SIGN}"; then
    log "Ad-hoc signing DMG..."
    codesign --force --sign - "${DMG_OUT}"
  fi

  log "DMG ready at ${DMG_OUT}"
}

notarize_if_requested() {
  if is_true "${SKIP_NOTARIZE}"; then
    log "Skipping notarization because SKIP_NOTARIZE=${SKIP_NOTARIZE}."
    return
  fi

  if [[ -z "${NOTARY_PROFILE}" ]]; then
    log "NOTARY_PROFILE not set; skipping notarization."
    return
  fi

  for artifact in "${PKG_OUT}" "${DMG_OUT}"; do
    log "Submitting ${artifact} for notarization..."
    xcrun notarytool submit "${artifact}" --keychain-profile "${NOTARY_PROFILE}" --wait
    xcrun stapler staple "${artifact}"
    xcrun stapler validate "${artifact}"
  done
}

run_step "selecting the signing identity" auto_select_app_cert
run_step "validating inputs and required tools" validate_inputs
run_step "publishing Keysharp and Keyview" publish_projects
run_step "staging the package payload" stage_payload
run_step "signing the app bundles" sign_apps_if_requested
run_step "building the .pkg" build_pkg
run_step "building the .dmg" build_dmg
run_step "notarizing" notarize_if_requested

log ""
log "macOS packages ready:"
log "  System install (root):  ${PKG_OUT}"
log "  User install  (no root): ${DMG_OUT}"
