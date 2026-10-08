#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
DEBIAN_DIR="${ASSETS_DIR}/debian"

usage() {
  cat <<'EOF'
Usage: package-linux.sh [--restore | --stage | --install DESTDIR]

Publishes Keysharp and Keyview, then writes dist/keysharp-<rid>.tar.gz and
dist/keysharp_<version>_<arch>.deb.

  --restore          Restore the NuGet packages the publish needs, and stop.
  --stage            Publish and stage the application tree, and stop.
  --install DESTDIR  Install the staged tree into DESTDIR in the Debian layout.
  -h, --help         Show this help.

RID, VERSION and CONFIG select the build. KEYSHARP_DIST_DIR replaces dist/,
and EtoRoot names the Eto checkout when it is not ../Eto.
EOF
}

mode=package
destdir=
case "${1:-}" in
  "") ;;
  --restore) mode=restore ;;
  --stage) mode=stage ;;
  --install)
    mode=install
    destdir="${2:-}"
    [[ -n "${destdir}" ]] || { usage >&2; exit 2; }
    shift
    ;;
  -h|--help) usage; exit 0 ;;
  *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
esac
[[ $# -le 1 ]] || { usage >&2; exit 2; }

detect_default_rid() {
  case "$(uname -m)" in
    x86_64) echo linux-x64 ;;
    aarch64|arm64) echo linux-arm64 ;;
    *)
      echo "Unable to infer a supported Linux RID from $(uname -m). Set RID=linux-x64 or linux-arm64." >&2
      return 1
      ;;
  esac
}

RID="${RID:-$(detect_default_rid)}"
case "${RID}" in
  linux-x64) DEB_ARCH=amd64 ;;
  linux-arm64) DEB_ARCH=arm64 ;;
  *) echo "Unsupported Linux RID: ${RID}" >&2; exit 1 ;;
esac
CONFIG="${CONFIG:-Release}"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"
if [[ -z "${VERSION}" ]]; then
  echo "Unable to determine the Keysharp package version. Set VERSION explicitly." >&2
  exit 1
fi
# MSBuild reads the environment as properties, so an exported VERSION would become the Version of
# every project built below, including Eto and KPM.
export -n RID CONFIG VERSION

DIST_DIR="${KEYSHARP_DIST_DIR:-${ROOT}/dist}"
PUBLISH_DIR="${DIST_DIR}/publish/${RID}"
PKG_NAME="keysharp-${RID}"
STAGING_DIR="${DIST_DIR}/staging/${RID}"
PKG_DIR="${STAGING_DIR}/${PKG_NAME}"
APP_DIR="${PKG_DIR}/app"

ETO_DIR="$(if cd "${EtoRoot:-${ROOT}/../Eto}" 2>/dev/null; then pwd; fi)"
PATH_MAP="${ROOT}=/_/keysharp"
# One RID and one Eto target framework, so restore fetches nothing the package does not ship.
DOTNET_ARGS=(-p:Configuration="${CONFIG}" -p:RuntimeIdentifiers="${RID}" -p:TargetFrameworkOverride=net10.0 --nologo)
if [[ -n "${ETO_DIR}" ]]; then
  PATH_MAP="${PATH_MAP}%2c${ETO_DIR}=/_/Eto"
  DOTNET_ARGS+=(-p:EtoRoot="${ETO_DIR}")
fi

restore() {
  # Keysharp builds its scripting components through an MSBuild task rather than a project
  # reference, so its own restore never reaches them.
  dotnet restore "${ROOT}/Keysharp.Components/Scripting/Compiler/Keysharp.Components.Scripting.Compiler.csproj" \
    "${DOTNET_ARGS[@]}"
  for project in Keysharp Keyview; do
    dotnet restore "${ROOT}/${project}/${project}.csproj" -r "${RID}" "${DOTNET_ARGS[@]}"
  done
}

publish() {
  echo "Publishing Keysharp and Keyview (CONFIG=${CONFIG}, RID=${RID})..."
  restore
  rm -rf -- "${PUBLISH_DIR}"
  for project in Keysharp Keyview; do
    dotnet publish "${ROOT}/${project}/${project}.csproj" -c "${CONFIG}" -r "${RID}" --no-restore \
      "${DOTNET_ARGS[@]}" \
      -p:PublishDir="${PUBLISH_DIR}/${project}/" \
      -p:KeysharpVersion="${VERSION}" \
      -p:Deterministic=true \
      -p:ContinuousIntegrationBuild=true \
      -p:ShouldUnsetParentConfigurationAndPlatform=false \
      -p:PathMap="${PATH_MAP}"
  done

  dotnet msbuild "${ROOT}/Keysharp.Install/payload/Keysharp.Payload.proj" \
    -p:PayloadDir="${PUBLISH_DIR}/Keysharp" -p:KpmRid="${RID}" --nologo -v:minimal
}

relocate_library_scripts() {
  if [[ -f "${APP_DIR}/Scripts/AtSpi.ks" ]]; then
    mkdir -p "${APP_DIR}/Lib"
    mv "${APP_DIR}/Scripts/AtSpi.ks" "${APP_DIR}/Lib/AtSpi.ks"
  fi
}

# Only a cross-architecture build, whose host cannot run, may ship the Dash as source.
verify_dash_present() {
  [[ -f "${APP_DIR}/Keysharp.cks" || ( -f "${APP_DIR}/Keysharp.ks" && "${RID}" != "$(detect_default_rid)" ) ]] && return 0
  echo "The payload has no Keysharp.cks: the published host could not precompile the Dash (see above)." >&2
  exit 1
}

normalize_app_permissions() {
  find "${APP_DIR}" -type d -exec chmod 0755 {} +
  find "${APP_DIR}" -type f -exec chmod 0644 {} +
  chmod 0755 "${APP_DIR}/Keysharp" "${APP_DIR}/Keyview"
}

verify_no_local_paths() {
  local patterns=() pattern
  # Upstream DLLs may embed their own runner home; the checkout roots identify this build.
  for pattern in "${ROOT}" "${ETO_DIR}"; do
    [[ -n "${pattern}" && "${pattern}" != / ]] && patterns+=(-e "${pattern}")
  done
  if grep -rlaF "${patterns[@]}" -- "${APP_DIR}"; then
    echo "Package payload contains local absolute paths." >&2
    exit 1
  fi
}

stage() {
  publish
  rm -rf -- "${PKG_DIR}"
  mkdir -p "${APP_DIR}"
  cp -a "${PUBLISH_DIR}/Keyview/." "${APP_DIR}/"
  cp -a "${PUBLISH_DIR}/Keysharp/." "${APP_DIR}/"
  find "${APP_DIR}" -name '*.pdb' -delete
  relocate_library_scripts
  verify_dash_present
  normalize_app_permissions
  verify_no_local_paths
}

# The system layout both Debian builds share: this script's own .deb and debian/rules.
install_tree() {
  local dest="$1" entry
  install -d "${dest}/usr/lib/keysharp" "${dest}/usr/bin" "${dest}/usr/share/applications"
  cp -a "${APP_DIR}/." "${dest}/usr/lib/keysharp/"
  ln -sfn ../lib/keysharp/Keysharp "${dest}/usr/bin/keysharp"
  ln -sfn ../lib/keysharp/Keyview "${dest}/usr/bin/keyview"
  for entry in keysharp keyview; do
    sed -e 's|/usr/local/bin/|/usr/bin/|g' -e 's|/usr/local/lib/keysharp/|/usr/lib/keysharp/|g' \
      "${ASSETS_DIR}/${entry}.desktop" > "${dest}/usr/share/applications/${entry}.desktop"
    chmod 0644 "${dest}/usr/share/applications/${entry}.desktop"
  done
  install -Dm644 "${ASSETS_DIR}/keysharp.xml" "${dest}/usr/share/mime/packages/keysharp.xml"
  install -Dm644 "${ROOT}/assets/Keysharp.png" "${dest}/usr/share/icons/hicolor/256x256/apps/keysharp.png"
  install -Dm644 "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" \
    "${dest}/usr/lib/udev/rules.d/70-keysharp-i2c-uaccess.rules"
  # postinst sources the notice, so both install channels print the same text.
  install -Dm644 "${ASSETS_DIR}/component-notice.sh" "${dest}/usr/share/keysharp/component-notice.sh"
  install -Dm644 "${ROOT}/license.txt" "${dest}/usr/share/doc/keysharp/copyright"
}

build_tarball() {
  cp "${ASSETS_DIR}/install.sh" "${ASSETS_DIR}/uninstall.sh" "${ASSETS_DIR}/component-notice.sh" \
    "${ASSETS_DIR}/keyview.desktop" "${ASSETS_DIR}/keysharp.desktop" "${ASSETS_DIR}/keysharp.xml" \
    "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" "${ROOT}/assets/Keysharp.png" "${PKG_DIR}/"
  chmod 0644 "${PKG_DIR}"/*.desktop "${PKG_DIR}/keysharp.xml" "${PKG_DIR}/70-keysharp-i2c-uaccess.rules" \
    "${PKG_DIR}/Keysharp.png" "${PKG_DIR}/component-notice.sh"
  chmod 0755 "${PKG_DIR}/install.sh" "${PKG_DIR}/uninstall.sh"
  tar -czf "${DIST_DIR}/${PKG_NAME}.tar.gz" -C "${STAGING_DIR}" "${PKG_NAME}"
  echo "Tarball ready at ${DIST_DIR}/${PKG_NAME}.tar.gz"
}

build_deb() {
  if ! command -v dpkg-deb >/dev/null 2>&1 || ! command -v dpkg-gencontrol >/dev/null 2>&1; then
    echo "Skipping the Debian package: it needs dpkg-deb and dpkg-gencontrol (dpkg-dev)."
    return 0
  fi

  local root="${DIST_DIR}/package-root/${PKG_NAME}"
  local control_dir="${DIST_DIR}/package-root/control-${RID}"
  local deb="${DIST_DIR}/keysharp_${VERSION}_${DEB_ARCH}.deb"
  local script maintainer
  rm -rf -- "${root}" "${control_dir}"
  install_tree "${root}"
  install -d "${root}/DEBIAN" "${control_dir}/debian"
  for script in preinst postinst prerm postrm; do
    # The token marks where debhelper inserts its snippets; dpkg-deb inserts none.
    sed '/^#DEBHELPER#$/d' "${DEBIAN_DIR}/${script}" > "${root}/DEBIAN/${script}"
    chmod 0755 "${root}/DEBIAN/${script}"
  done
  find "${root}" -type d -exec chmod 0755 {} +
  (cd "${root}" && find usr -type f -exec md5sum {} + | LC_ALL=C sort -k 2) > "${root}/DEBIAN/md5sums"

  # dpkg-gencontrol takes the binary package's fields from the same debian/control the Launchpad
  # build uses, and needs a changelog beside it for the version.
  maintainer="$(sed -n 's/^Maintainer: //p' "${DEBIAN_DIR}/control")"
  cp "${DEBIAN_DIR}/control" "${control_dir}/debian/control"
  printf 'keysharp (%s) unstable; urgency=medium\n\n  * Release %s.\n\n -- %s  %s\n' \
    "${VERSION}" "${VERSION}" "${maintainer}" "$(date -R)" > "${control_dir}/debian/changelog"
  (cd "${control_dir}" && dpkg-gencontrol -pkeysharp -P"${root}" -DArchitecture="${DEB_ARCH}" \
    -Vmisc:Depends= -Vshlibs:Depends=)

  # xz rather than Ubuntu's zstd default, so dpkg on older Debian releases can unpack it.
  dpkg-deb --build --root-owner-group -Zxz "${root}" "${deb}"
  echo "Debian package ready at ${deb}"
}

case "${mode}" in
  restore) restore ;;
  stage) stage ;;
  install) install_tree "${destdir}" ;;
  package)
    stage
    build_tarball
    build_deb
    ;;
esac
echo "Done."
