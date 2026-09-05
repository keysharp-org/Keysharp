#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
CONFIG="${CONFIG:-Release}"
# The client ABI capabilities the Debian package recommends. Each component
# provides its own capability; Keysharp only names them.
INPUT_CLIENT_ABI_PACKAGE="keysharp-input-client-abi-0 (>= 0.2)"
DESKTOP_CLIENT_ABI_PACKAGE="keysharp-desktop-client-abi-0 (>= 0.8)"

usage() {
  cat <<'EOF'
Usage: package-linux.sh [options]

Options:
  -h, --help            Show this help.

KEYSHARP_DIST_DIR selects an alternate output/work directory.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

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

map_rid_to_deb_arch() {
  case "$1" in
    linux-x64) echo amd64 ;;
    linux-arm64) echo arm64 ;;
    *) return 1 ;;
  esac
}

RID="${RID:-$(detect_default_rid)}"
DEB_ARCH="$(map_rid_to_deb_arch "${RID}")" || {
  echo "Unsupported Linux RID: ${RID}" >&2
  exit 1
}
DIST_DIR="${KEYSHARP_DIST_DIR:-${ROOT}/dist}"
PUBLISH_DIR="${DIST_DIR}/publish/${RID}"
STAGING_DIR="${DIST_DIR}/staging/${RID}"
PACKAGE_ROOT_DIR="${DIST_DIR}/package-root"
PKG_NAME="keysharp-${RID}"
PKG_DIR="${STAGING_DIR}/${PKG_NAME}"
APP_DIR="${PKG_DIR}/app"
DEB_TMP_DIR="${PACKAGE_ROOT_DIR}/${PKG_NAME}-deb"
DEB_PKG_NAME="${DEB_PKG_NAME:-keysharp}"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${ROOT}/Keysharp/Keysharp.csproj" | head -n 1)}"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"

if [[ -z "${VERSION}" ]]; then
  echo "Unable to determine the Keysharp package version. Set VERSION explicitly." >&2
  exit 1
fi

ETO_DIR="$(if cd "${ROOT}/../Eto" 2>/dev/null; then pwd; fi)"
PATH_MAP="${ROOT}=/_/keysharp"
if [[ -n "${ETO_DIR}" ]]; then
  PATH_MAP="${PATH_MAP}%2c${ETO_DIR}=/_/Eto"
fi

relocate_library_scripts() {
  if [[ -f "${APP_DIR}/Scripts/AtSpi.ks" ]]; then
    mkdir -p "${APP_DIR}/Lib"
    mv "${APP_DIR}/Scripts/AtSpi.ks" "${APP_DIR}/Lib/AtSpi.ks"
  fi
}

verify_dash_present() {
  if [[ ! -f "${APP_DIR}/Keysharp.cks" && ! -f "${APP_DIR}/Keysharp.ks" ]]; then
    echo "Package payload has neither Keysharp.cks nor Keysharp.ks." >&2
    exit 1
  fi
  if [[ ! -f "${APP_DIR}/Keysharp.cks" ]]; then
    echo "Warning: Keysharp.cks was not produced; the Dash will ship as source." >&2
  fi
}

normalize_app_permissions() {
  find "${APP_DIR}" -type d -exec chmod 0755 {} +
  find "${APP_DIR}" -type f -exec chmod 0644 {} +
  for exe in Keysharp Keyview; do
    [[ -f "${APP_DIR}/${exe}" ]] && chmod 0755 "${APP_DIR}/${exe}"
  done
}

verify_no_local_paths() {
  local scan_dir="$1"
  local found=0
  local patterns=("${ROOT}")
  [[ -n "${HOME:-}" ]] && patterns+=("${HOME}")
  [[ -n "${ETO_DIR}" ]] && patterns+=("${ETO_DIR}")

  command -v rg >/dev/null 2>&1 || {
    echo "ripgrep is required to verify the release payload." >&2
    return 1
  }
  for pattern in "${patterns[@]}"; do
    if rg -a -F -n --max-count 20 "${pattern}" "${scan_dir}"; then
      found=1
    fi
  done
  [[ "${found}" -eq 0 ]] || {
    echo "Package payload contains local absolute paths." >&2
    return 1
  }
}

rewrite_desktop_exec() {
  sed -e 's|/usr/local/bin/|/usr/bin/|g' \
      -e 's|/usr/local/lib/keysharp/|/usr/lib/keysharp/|g' \
      "$1" > "$2"
}

write_deb_control() {
  cat > "$1" <<EOF
Package: ${DEB_PKG_NAME}
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${DEB_ARCH}
Maintainer: Descolada <16986957+Descolada@users.noreply.github.com>
Homepage: https://github.com/keysharp-org/Keysharp
Depends: dotnet-runtime-10.0, libx11-6, libxtst6, libxinerama1, libxt6, libx11-xcb1, libxkbcommon-x11-0, libxcb-xtest0, libgtk-3-0, libglib2.0-0, libnotify4, libatspi2.0-0, at-spi2-core, pulseaudio-utils
Recommends: ${INPUT_CLIENT_ABI_PACKAGE}, ${DESKTOP_CLIENT_ABI_PACKAGE}
Description: A cross-platform C# port and enhancement of the AutoHotkey program
EOF
}

write_deb_preinst() {
  install -m 0755 "${ASSETS_DIR}/debian/preinst" "$1"
}

write_deb_postinst() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f /usr/share/icons/hicolor || true
packaged_rule=/usr/lib/udev/rules.d/70-keysharp-i2c-uaccess.rules
override_rule=/etc/udev/rules.d/70-keysharp-i2c-uaccess.rules
if [ -f "${override_rule}" ] && [ ! -L "${override_rule}" ]; then
  if cmp -s "${override_rule}" "${packaged_rule}"; then
    rm -f -- "${override_rule}"
  else
    echo "Keeping modified ${override_rule}; it overrides the packaged Keysharp rule." >&2
  fi
fi
command -v udevadm >/dev/null 2>&1 && udevadm control --reload-rules || true
command -v udevadm >/dev/null 2>&1 && udevadm trigger --subsystem-match=i2c-dev || true
EOF
  chmod 0755 "$1"
}

write_deb_prerm() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e
# Standalone Linux services can have other clients and are not part of this
# package's lifecycle. Stop only Keysharp's optional compile daemon.
command -v pkill >/dev/null 2>&1 && pkill -f '[Kk]eysharp --daemon' 2>/dev/null || true
EOF
  chmod 0755 "$1"
}

write_deb_postrm() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f /usr/share/icons/hicolor || true
command -v udevadm >/dev/null 2>&1 && udevadm control --reload-rules || true
command -v udevadm >/dev/null 2>&1 && udevadm trigger --subsystem-match=i2c-dev || true
EOF
  chmod 0755 "$1"
}

build_tarball() {
  local tarball="${DIST_DIR}/${PKG_NAME}.tar.gz"
  tar -czf "${tarball}" -C "${STAGING_DIR}" "${PKG_NAME}"
  echo "Tarball ready at ${tarball}"
}

build_deb() {
  command -v dpkg-deb >/dev/null 2>&1 || {
    echo "Skipping Debian package creation because dpkg-deb is not installed."
    return 0
  }

  local deb_root="${DEB_TMP_DIR}"
  local debian_dir="${deb_root}/DEBIAN"
  local lib_dir="${deb_root}/usr/lib/keysharp"
  local bin_dir="${deb_root}/usr/bin"
  local applications_dir="${deb_root}/usr/share/applications"
  local mime_dir="${deb_root}/usr/share/mime/packages"
  local icon_dir="${deb_root}/usr/share/icons/hicolor/256x256/apps"
  local udev_dir="${deb_root}/usr/lib/udev/rules.d"
  local doc_dir="${deb_root}/usr/share/doc/${DEB_PKG_NAME}"
  local deb_out="${DIST_DIR}/${DEB_PKG_NAME}_${VERSION}_${DEB_ARCH}.deb"

  rm -rf -- "${deb_root}"
  mkdir -p "${debian_dir}" "${lib_dir}" "${bin_dir}" "${applications_dir}" \
    "${mime_dir}" "${icon_dir}" "${udev_dir}" "${doc_dir}"

  rsync -a "${APP_DIR}/" "${lib_dir}/"
  ln -s ../lib/keysharp/Keysharp "${bin_dir}/keysharp"
  ln -s ../lib/keysharp/Keyview "${bin_dir}/keyview"
  rewrite_desktop_exec "${ASSETS_DIR}/keysharp.desktop" "${applications_dir}/keysharp.desktop"
  rewrite_desktop_exec "${ASSETS_DIR}/keyview.desktop" "${applications_dir}/keyview.desktop"
	install -Dm644 "${ASSETS_DIR}/keysharp.xml" "${mime_dir}/keysharp.xml"
	install -Dm644 "${ROOT}/assets/Keysharp.png" "${icon_dir}/keysharp.png"
	install -Dm644 "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" \
		"${udev_dir}/70-keysharp-i2c-uaccess.rules"
	install -Dm644 "${ROOT}/license.txt" "${doc_dir}/copyright"

  write_deb_control "${debian_dir}/control"
  write_deb_preinst "${debian_dir}/preinst"
  write_deb_postinst "${debian_dir}/postinst"
  write_deb_prerm "${debian_dir}/prerm"
  write_deb_postrm "${debian_dir}/postrm"

  find "${deb_root}" -type d -exec chmod 0755 {} +
  find "${deb_root}" -type f -exec chmod 0644 {} +
  chmod 0755 "${lib_dir}/Keysharp" "${lib_dir}/Keyview" \
    "${debian_dir}/preinst" "${debian_dir}/postinst" \
    "${debian_dir}/prerm" "${debian_dir}/postrm"

  if dpkg-deb --help 2>/dev/null | grep -q -- '--root-owner-group'; then
    dpkg-deb --build --root-owner-group "${deb_root}" "${deb_out}"
  elif command -v fakeroot >/dev/null 2>&1; then
    fakeroot dpkg-deb --build "${deb_root}" "${deb_out}"
  else
    dpkg-deb --build "${deb_root}" "${deb_out}"
  fi
  echo "Debian package ready at ${deb_out}"
}

echo "Publishing Keysharp and Keyview (CONFIG=${CONFIG}, RID=${RID})..."
mkdir -p "${DIST_DIR}"
rm -rf -- "${PUBLISH_DIR}/Keysharp" "${PUBLISH_DIR}/Keyview"
for project in Keysharp Keyview; do
  dotnet publish "${ROOT}/${project}/${project}.csproj" -c "${CONFIG}" -r "${RID}" \
    -p:PublishDir="${PUBLISH_DIR}/${project}/" \
    -p:KeysharpVersion="${VERSION}" \
    -p:Deterministic=true \
    -p:ContinuousIntegrationBuild=true \
    -p:ShouldUnsetParentConfigurationAndPlatform=false \
    -p:PathMap="${PATH_MAP}"
done

dotnet msbuild "${ROOT}/Keysharp.Install/payload/Keysharp.Payload.proj" \
  -p:PayloadDir="${PUBLISH_DIR}/Keysharp" -p:KpmRid="${RID}" --nologo -v:minimal

rm -rf -- "${PKG_DIR}"
mkdir -p "${APP_DIR}"
rsync -a "${PUBLISH_DIR}/Keyview/" "${APP_DIR}/"
rsync -a "${PUBLISH_DIR}/Keysharp/" "${APP_DIR}/"
find "${APP_DIR}" -name '*.pdb' -delete
relocate_library_scripts
verify_dash_present
normalize_app_permissions
verify_no_local_paths "${APP_DIR}"

cp "${ASSETS_DIR}/install.sh" "${ASSETS_DIR}/uninstall.sh" "${PKG_DIR}/"
cp "${ASSETS_DIR}/keyview.desktop" "${ASSETS_DIR}/keysharp.desktop" \
	"${ASSETS_DIR}/keysharp.xml" "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" "${PKG_DIR}/"
cp "${ROOT}/assets/Keysharp.png" "${PKG_DIR}/"
chmod 0755 "${PKG_DIR}/install.sh" "${PKG_DIR}/uninstall.sh"
chmod 0644 "${PKG_DIR}/keyview.desktop" "${PKG_DIR}/keysharp.desktop" \
	"${PKG_DIR}/keysharp.xml" "${PKG_DIR}/70-keysharp-i2c-uaccess.rules" "${PKG_DIR}/Keysharp.png"

build_tarball
build_deb
echo "Done."
