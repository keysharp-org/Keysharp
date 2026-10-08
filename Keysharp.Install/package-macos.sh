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
MACOS_DIR="${ROOT}/Keysharp.Install/macos"

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
  require_tool productbuild
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
  # A per-user install in ~/Applications cannot write the global .NET location, so its runtime goes
  # in ~/.dotnet (install.sh), which the apphost finds four levels above Contents/MacOS.
  local APPHOST_DOTNET=../../../../.dotnet
  [[ "${RID}" == osx-arm64 ]] || APPHOST_DOTNET="${APPHOST_DOTNET}/x64"
  # Keysharp builds its scripting components through an MSBuild task rather than a project reference,
  # so its own restore never reaches them.
  dotnet restore "${ROOT}/Keysharp.Components/Scripting/Compiler/Keysharp.Components.Scripting.Compiler.csproj" --nologo
  for proj in Keysharp Keyview; do
    rm -rf "${PUBLISH_DIR}/${proj}"
    dotnet publish "${ROOT}/${proj}/${proj}.csproj" -c "${CONFIG}" -r "${RID}" \
      -o "${PUBLISH_DIR}/${proj}" \
      -p:KeysharpVersion="${VERSION}" \
      -p:Deterministic=true \
      -p:ContinuousIntegrationBuild=true \
      '-p:AppHostDotNetSearch="AppRelative;EnvironmentVariable;Global"' \
      -p:AppHostRelativeDotNet="${APPHOST_DOTNET}" \
      -p:PathMap="${PATH_MAP}"
  done

  # The Dash, its template, the demos and every .cks: install payload, so Keysharp.csproj does not carry
  # it. Runs against the just-published host, which is what makes each .cks match this build. Inside the
  # .app because that is where macOS publishes; the other two packagers publish a plain tree.
  log "Staging install payload..."
  dotnet msbuild "${ROOT}/Keysharp.Install/payload/Keysharp.Payload.proj" \
    -p:PayloadDir="$(resolve_app_source Keysharp)/Contents/MacOS" -p:KpmRid="${RID}" --nologo -v:minimal
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

# Writes a package scripts folder whose preinstall/postinstall run the given install.sh actions.
write_package_scripts() {
  local dir="$1" phase action
  shift
  rm -rf "${dir}"
  mkdir -p "${dir}"
  install -m 0644 "${MACOS_DIR}/install.sh" "${dir}/"
  for phase in "$@"; do
    action="${phase#*=}"
    printf '#!/bin/bash\nexec /bin/bash "${0%%/*}/install.sh" %s %s "$2"\n' "${action}" "${RID#osx-}" > "${dir}/${phase%%=*}"
    chmod 0755 "${dir}/${phase%%=*}"
  done
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
  rm -rf "${PKG_ROOT}"
  mkdir -p "${PKG_ROOT}/Applications"

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

  install -m 0644 "${MACOS_DIR}/uninstall.sh" "${PKG_ROOT}/Applications/Keysharp.app/Contents/Resources/"
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
  # Apple discourages --deep for signing. Review the managed payload layout and inside-out signing
  # before relying on this path for Developer ID distribution.
  codesign --force --deep --timestamp --options runtime "${entitlements_arg[@]}" --sign "${sign_identity}" "${app}"
  codesign --verify --deep --strict "${app}"
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
  local distribution="${STAGING_DIR}/${PKG_NAME}-distribution.xml"
  local host_architecture=arm64
  [[ "${RID}" == osx-arm64 ]] || host_architecture=x86_64

  log "Creating package ${PKG_OUT}..."
  rm -f "${PKG_OUT}"
  write_package_scripts "${SCRIPTS_DIR}" preinstall=preinstall postinstall=postinstall
  pkgbuild --root "${PKG_ROOT}" --component-plist "${component_plist}" --identifier "${PKG_IDENTIFIER}" \
    --version "${VERSION}" --install-location / --scripts "${SCRIPTS_DIR}" "${STAGING_DIR}/${PKG_NAME}-app.pkg"
  # Installer can only make a choice optional by giving it its own package, so each option is a
  # payload-free package whose postinstall applies it.
  local option
  for option in terminal vscode launch; do
    write_package_scripts "${STAGING_DIR}/${PKG_NAME}-${option}-scripts" "postinstall=${option}"
    pkgbuild --nopayload --identifier "${PKG_IDENTIFIER}.${option}" --version "${VERSION}" \
      --scripts "${STAGING_DIR}/${PKG_NAME}-${option}-scripts" "${STAGING_DIR}/${PKG_NAME}-${option}.pkg"
  done

  cat > "${distribution}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<installer-gui-script minSpecVersion="2">
  <title>Keysharp</title>
  <conclusion file="Conclusion.html" mime-type="text/html"/>
  <options customize="always" require-scripts="true" hostArchitectures="${host_architecture}"/>
  <domains enable_anywhere="false" enable_currentUserHome="true" enable_localSystem="true"/>
  <volume-check><allowed-os-versions><os-version min="15.0"/></allowed-os-versions></volume-check>
  <choices-outline>
    <line choice="app"/><line choice="terminal"/><line choice="vscode"/><line choice="launch"/>
  </choices-outline>
  <choice id="app" title="Keysharp and Keyview" enabled="false">
    <pkg-ref id="${PKG_IDENTIFIER}"/>
  </choice>
  <choice id="terminal" title="Terminal commands" description="keysharp, keyview and keysharp-uninstall in /usr/local/bin, or in ~/.local/bin (added to your PATH) when installing only for you.">
    <pkg-ref id="${PKG_IDENTIFIER}.terminal"/>
  </choice>
  <choice id="vscode" title="VS Code AutoHotkey v2 support" description="~/.local/bin/AutoHotkey.exe, to set as the extension's interpreter path." start_selected="false">
    <pkg-ref id="${PKG_IDENTIFIER}.vscode"/>
  </choice>
  <choice id="launch" title="Open Keysharp when done">
    <pkg-ref id="${PKG_IDENTIFIER}.launch"/>
  </choice>
  <pkg-ref id="${PKG_IDENTIFIER}" version="${VERSION}" onConclusion="None">${PKG_NAME}-app.pkg</pkg-ref>
  <pkg-ref id="${PKG_IDENTIFIER}.terminal" version="${VERSION}" onConclusion="None">${PKG_NAME}-terminal.pkg</pkg-ref>
  <pkg-ref id="${PKG_IDENTIFIER}.vscode" version="${VERSION}" onConclusion="None">${PKG_NAME}-vscode.pkg</pkg-ref>
  <pkg-ref id="${PKG_IDENTIFIER}.launch" version="${VERSION}" onConclusion="None">${PKG_NAME}-launch.pkg</pkg-ref>
</installer-gui-script>
EOF
  local productbuild_args=(
    --distribution "${distribution}"
    --resources "${MACOS_DIR}/installer-resources"
    --package-path "${STAGING_DIR}"
  )
  if [[ -n "${INSTALLER_CERT}" ]]; then
    productbuild_args+=(--sign "${INSTALLER_CERT}" --timestamp)
  fi
  productbuild "${productbuild_args[@]}" "${PKG_OUT}"

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

  install -m 0644 "${PKG_OUT}" "${DMG_STAGING_DIR}/Install Keysharp.pkg"
  install -m 0755 "${MACOS_DIR}/uninstall.sh" "${DMG_STAGING_DIR}/Uninstall Keysharp.command"

  rm -f "${DMG_OUT}"
  hdiutil create \
    -volname "Keysharp ${VERSION}" \
    -srcfolder "${DMG_STAGING_DIR}" \
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
  local artifact="$1"
  if is_true "${SKIP_NOTARIZE}"; then
    log "Skipping notarization because SKIP_NOTARIZE=${SKIP_NOTARIZE}."
    return
  fi

  if [[ -z "${NOTARY_PROFILE}" ]]; then
    log "NOTARY_PROFILE not set; skipping notarization."
    return
  fi

  log "Submitting ${artifact} for notarization..."
  xcrun notarytool submit "${artifact}" --keychain-profile "${NOTARY_PROFILE}" --wait
  xcrun stapler staple "${artifact}"
  xcrun stapler validate "${artifact}"
}

run_step "selecting the signing identity" auto_select_app_cert
run_step "validating inputs and required tools" validate_inputs
run_step "publishing Keysharp and Keyview" publish_projects
run_step "staging the package payload" stage_payload
run_step "signing the app bundles" sign_apps_if_requested
run_step "building the .pkg" build_pkg
run_step "notarizing the .pkg" notarize_if_requested "${PKG_OUT}"
run_step "building the .dmg" build_dmg
run_step "notarizing the .dmg" notarize_if_requested "${DMG_OUT}"

log ""
log "macOS packages ready:"
log "  Installer package:     ${PKG_OUT}"
log "  Installer disk image:  ${DMG_OUT}"
