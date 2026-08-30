#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
COMPONENT_LOCK="${ASSETS_DIR}/component-versions.conf"
CONFIG="${CONFIG:-Release}"
COMPONENT_DIR="${KEYSHARP_COMPONENT_DIR:-}"
DOWNLOAD_COMPONENTS="${KEYSHARP_DOWNLOAD_COMPONENTS:-false}"
PACKAGE_COMPONENTS=true

usage() {
  cat <<'EOF'
Usage: package-linux.sh [options]

Options:
  --dependency-dir DIR  Use exact standalone-component archives from DIR.
  --download-components
                        Download versions pinned in component-versions.conf.
  --without-components  Build a development tarball without component installers.
  -h, --help            Show this help.

KEYSHARP_COMPONENT_DIR and KEYSHARP_DOWNLOAD_COMPONENTS are equivalent
environment settings. KEYSHARP_DIST_DIR selects an alternate output/work
directory. Release builds should always include components.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dependency-dir)
      [[ $# -ge 2 ]] || { echo "--dependency-dir requires a path" >&2; exit 2; }
      COMPONENT_DIR="$2"
      shift 2
      ;;
    --download-components)
      DOWNLOAD_COMPONENTS=true
      shift
      ;;
    --without-components)
      PACKAGE_COMPONENTS=false
      shift
      ;;
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

if [[ ! -f "${COMPONENT_LOCK}" ]]; then
  echo "Standalone-component lock file is missing: ${COMPONENT_LOCK}" >&2
  exit 1
fi
# shellcheck source=linux/component-versions.conf
source "${COMPONENT_LOCK}"

validate_component_lock() {
  local package
  [[ "${KEYSHARP_INPUT_DEBIAN_PROTOCOL_PACKAGE}" \
      == "keysharp-input-protocol-${KEYSHARP_INPUT_PROTOCOL_MAJOR}.${KEYSHARP_INPUT_PROTOCOL_MINOR}" ]] \
    || { echo "The input Debian protocol package does not match the locked protocol." >&2; return 1; }
  [[ "${KEYSHARP_DESKTOP_DEBIAN_PROTOCOL_PACKAGE}" \
      == "keysharp-desktop-protocol-${KEYSHARP_DESKTOP_PROTOCOL_MAJOR}.${KEYSHARP_DESKTOP_PROTOCOL_MINOR}" ]] \
    || { echo "The desktop Debian protocol package does not match the locked protocol." >&2; return 1; }
  for package in "${KEYSHARP_INPUT_DEBIAN_PROTOCOL_PACKAGE}" \
      "${KEYSHARP_DESKTOP_DEBIAN_PROTOCOL_PACKAGE}"; do
    [[ "${package}" =~ ^[a-z0-9][a-z0-9+.-]+$ ]] \
      || { echo "Invalid Debian protocol package name: ${package}" >&2; return 1; }
  done
}

validate_component_lock

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

ETO_DIR="$(cd "${ROOT}/../Eto" 2>/dev/null && pwd || true)"
PATH_MAP="${ROOT}=/_/keysharp"
if [[ -n "${ETO_DIR}" ]]; then
  PATH_MAP="${PATH_MAP}%2c${ETO_DIR}=/_/Eto"
fi

component_archive_name() {
  printf '%s-%s-%s.tar.gz\n' "$1" "$2" "${RID}"
}

component_expected_sha() {
  local component="$1"
  local arch_key value_name
  arch_key="$(printf '%s' "${RID}" | tr '[:lower:]-' '[:upper:]_')"
  case "${component}" in
    keysharp-input) value_name="KEYSHARP_INPUT_SHA256_${arch_key}" ;;
    keysharp-desktop) value_name="KEYSHARP_DESKTOP_SHA256_${arch_key}" ;;
    *) return 1 ;;
  esac
  printf '%s\n' "${!value_name:-}"
}

validate_download_pins() {
  local component expected_sha
  [[ "${PACKAGE_COMPONENTS}" == true && "${DOWNLOAD_COMPONENTS}" == true \
      && -z "${COMPONENT_DIR}" ]] || return 0
  for component in keysharp-input keysharp-desktop; do
    expected_sha="$(component_expected_sha "${component}")"
    if [[ ! "${expected_sha}" =~ ^[0-9a-f]{64}$ ]]; then
      echo "Cannot download ${component}: its ${RID} SHA-256 is not pinned in ${COMPONENT_LOCK}." >&2
      return 1
    fi
  done
}

validate_download_pins

find_local_component_archive() {
  local component="$1"
  local archive_name="$2"
  local repository_dir candidate
  local candidates=()

  if [[ -n "${COMPONENT_DIR}" ]]; then
    candidates+=("${COMPONENT_DIR}/${archive_name}")
  else
    repository_dir="${ROOT}/../${component}"
    candidates+=(
      "${repository_dir}/dist/release-assets/${archive_name}"
      "${repository_dir}/dist/${archive_name}"
      "${repository_dir}/build/release/${archive_name}"
    )
  fi

  for candidate in "${candidates[@]}"; do
    if [[ -f "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done
  return 1
}

component_archive_info_value() {
  local info="$1"
  shift
  local key value
  for key in "$@"; do
    value="$(printf '%s\n' "${info}" | sed -n "s/^${key}=//p" | head -n 1)"
    if [[ -n "${value}" ]]; then
      printf '%s\n' "${value}"
      return 0
    fi
  done
  return 1
}

validate_component_archive() {
  local component="$1"
  local version="$2"
  local protocol_name="$3"
  local protocol_major="$4"
  local protocol_minor="$5"
  local archive="$6"
  local archive_name archive_root listing detail type entry temporary binary info
  local actual_version actual_protocol actual_major actual_minor runner=()

  archive_name="$(basename -- "${archive}")"
  archive_root="${archive_name%.tar.gz}"
  listing="$(tar -tzf "${archive}")" || {
    echo "Unable to list standalone archive: ${archive_name}" >&2
    return 1
  }
  while IFS= read -r entry; do
    entry="${entry#./}"
    case "${entry}" in
      "${archive_root}"|"${archive_root}/"|"${archive_root}/"*) ;;
      *)
        echo "Standalone archive has a path outside ${archive_root}: ${entry}" >&2
        return 1
        ;;
    esac
    case "${entry}" in
      /|/*|..|../*|*/../*|*/..) return 1 ;;
    esac
  done <<< "${listing}"

  listing="$(tar -tvzf "${archive}")" || return 1
  while IFS= read -r detail; do
    type="${detail:0:1}"
    case "${type}" in
      -|d) ;;
      *)
        echo "Standalone archives may contain only regular files and directories: ${archive_name}" >&2
        return 1
        ;;
    esac
  done <<< "${listing}"

  mkdir -p "${STAGING_DIR}"
  temporary="$(mktemp -d "${STAGING_DIR}/component-check.XXXXXXXXXX")" || return 1
  if ! tar -xzf "${archive}" -C "${temporary}"; then
    rm -rf -- "${temporary}"
    return 1
  fi
  case "${component}" in
    keysharp-input)
      binary="${temporary}/${archive_root}/bin/keysharp-inputd"
      ;;
    keysharp-desktop)
      binary="${temporary}/${archive_root}/payload/usr/local/bin/keysharp-desktop"
      ;;
    *)
      rm -rf -- "${temporary}"
      return 1
      ;;
  esac
  if [[ ! -x "${binary}" \
      || ! -x "${temporary}/${archive_root}/install.sh" \
      || ! -x "${temporary}/${archive_root}/uninstall.sh" ]]; then
    echo "Standalone archive has an incomplete executable layout: ${archive_name}" >&2
    rm -rf -- "${temporary}"
    return 1
  fi
  if ! bash -n "${temporary}/${archive_root}/install.sh" \
      "${temporary}/${archive_root}/uninstall.sh"; then
    echo "Standalone archive has an invalid lifecycle script: ${archive_name}" >&2
    rm -rf -- "${temporary}"
    return 1
  fi
  command -v timeout >/dev/null 2>&1 && runner=(timeout 5)
  case "${component}" in
    keysharp-input) info="$("${runner[@]}" "${binary}" --info 2>/dev/null || true)" ;;
    keysharp-desktop) info="$("${runner[@]}" "${binary}" version 2>/dev/null || true)" ;;
  esac
  rm -rf -- "${temporary}"

  actual_version="$(component_archive_info_value "${info}" version product_version 2>/dev/null || true)"
  actual_protocol="$(component_archive_info_value "${info}" protocol-name protocol_name 2>/dev/null || true)"
  actual_major="$(component_archive_info_value "${info}" protocol-major protocol_major 2>/dev/null || true)"
  actual_minor="$(component_archive_info_value "${info}" protocol-minor protocol_minor 2>/dev/null || true)"
  if [[ "${actual_version}" != "${version}" \
      || "${actual_protocol}" != "${protocol_name}" \
      || "${actual_major}" != "${protocol_major}" \
      || "${actual_minor}" != "${protocol_minor}" ]]; then
    echo "Standalone archive metadata mismatch for ${archive_name}." >&2
    echo "Expected product ${version}, protocol ${protocol_name} ${protocol_major}.${protocol_minor}." >&2
    return 1
  fi
}

download_component_archive() {
  local repository="$1"
  local version="$2"
  local archive_name="$3"
  local expected_sha="$4"
  local destination="$5"
  local partial="${destination}.partial.$$"

  if [[ -z "${expected_sha}" ]]; then
    echo "Refusing to download ${archive_name}: its SHA-256 is not pinned in ${COMPONENT_LOCK}." >&2
    echo "Publish the standalone release and update the lock file first." >&2
    return 1
  fi
  command -v curl >/dev/null 2>&1 || {
    echo "curl is required for --download-components." >&2
    return 1
  }

  rm -f -- "${partial}"
  if ! curl --fail --location --proto '=https' --tlsv1.2 \
      --output "${partial}" \
      "https://github.com/${repository}/releases/download/v${version}/${archive_name}"; then
    rm -f -- "${partial}"
    return 1
  fi
  mv -f -- "${partial}" "${destination}"
}

stage_component_archive() {
  local component="$1"
  local repository="$2"
  local version="$3"
  local protocol_name="$4"
  local protocol_major="$5"
  local protocol_minor="$6"
  local archive_name source_archive destination expected_sha actual_sha

  archive_name="$(component_archive_name "${component}" "${version}")"
  destination="${PKG_DIR}/components/${archive_name}"
  expected_sha="$(component_expected_sha "${component}")"
  source_archive=""
  if [[ -n "${COMPONENT_DIR}" || "${DOWNLOAD_COMPONENTS}" != true ]]; then
    source_archive="$(find_local_component_archive "${component}" "${archive_name}" || true)"
  fi

  if [[ -z "${source_archive}" ]]; then
    if [[ "${DOWNLOAD_COMPONENTS}" != true ]]; then
      echo "Required standalone archive not found: ${archive_name}" >&2
      echo "Use --dependency-dir, KEYSHARP_COMPONENT_DIR, or --download-components." >&2
      return 1
    fi
    source_archive="${STAGING_DIR}/downloaded-components/${archive_name}"
    mkdir -p "$(dirname "${source_archive}")"
    if [[ ! -f "${source_archive}" ]]; then
      download_component_archive \
        "${repository}" "${version}" "${archive_name}" "${expected_sha}" "${source_archive}"
    fi
  fi

  actual_sha="$(sha256sum "${source_archive}" | awk '{print $1}')"
  if [[ "${DOWNLOAD_COMPONENTS}" == true && -z "${COMPONENT_DIR}" \
      && -n "${expected_sha}" && "${actual_sha}" != "${expected_sha}" ]]; then
    echo "SHA-256 mismatch for ${archive_name}: expected ${expected_sha}, got ${actual_sha}." >&2
    return 1
  fi
  validate_component_archive "${component}" "${version}" "${protocol_name}" \
    "${protocol_major}" "${protocol_minor}" "${source_archive}"

  cp "${source_archive}" "${destination}"
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "${component}" "${version}" "${protocol_name}" "${protocol_major}" \
    "${protocol_minor}" "${archive_name}" "${actual_sha}" \
    >> "${PKG_DIR}/components/manifest.tsv"
}

stage_components() {
  if [[ "${PACKAGE_COMPONENTS}" != true ]]; then
    echo "Building without standalone components (development-only)."
    return 0
  fi

  mkdir -p "${PKG_DIR}/components"
  printf '# component\tproduct-version\tprotocol-name\tprotocol-major\tprotocol-minor\tarchive\tsha256\n' \
    > "${PKG_DIR}/components/manifest.tsv"

  stage_component_archive \
    keysharp-input "${KEYSHARP_INPUT_REPOSITORY}" "${KEYSHARP_INPUT_VERSION}" \
    "${KEYSHARP_INPUT_PROTOCOL_NAME}" "${KEYSHARP_INPUT_PROTOCOL_MAJOR}" \
    "${KEYSHARP_INPUT_PROTOCOL_MINOR}"
  stage_component_archive \
    keysharp-desktop "${KEYSHARP_DESKTOP_REPOSITORY}" "${KEYSHARP_DESKTOP_VERSION}" \
    "${KEYSHARP_DESKTOP_PROTOCOL_NAME}" "${KEYSHARP_DESKTOP_PROTOCOL_MAJOR}" \
    "${KEYSHARP_DESKTOP_PROTOCOL_MINOR}"
}

preflight_component_archives() {
  local component repository version protocol_name protocol_major protocol_minor
  local archive_name source_archive expected_sha actual_sha

  [[ "${PACKAGE_COMPONENTS}" == true ]] || return 0

  while read -r component repository version protocol_name protocol_major protocol_minor; do
    archive_name="$(component_archive_name "${component}" "${version}")"
    source_archive="$(find_local_component_archive \
      "${component}" "${archive_name}" || true)"
    if [[ -z "${source_archive}" ]]; then
      if [[ "${DOWNLOAD_COMPONENTS}" == true ]]; then
        expected_sha="$(component_expected_sha "${component}")"
        if [[ ! "${expected_sha}" =~ ^[0-9a-f]{64}$ ]]; then
          echo "Cannot download ${component}: its ${RID} SHA-256 is not pinned in ${COMPONENT_LOCK}." >&2
          return 1
        fi
        source_archive="${STAGING_DIR}/downloaded-components/${archive_name}"
        mkdir -p "$(dirname "${source_archive}")"
        if [[ ! -f "${source_archive}" ]]; then
          download_component_archive "${repository}" "${version}" \
            "${archive_name}" "${expected_sha}" "${source_archive}"
        fi
      else
        echo "Required standalone archive not found: ${archive_name}" >&2
        echo "Use --dependency-dir, KEYSHARP_COMPONENT_DIR, or --download-components." >&2
        return 1
      fi
    fi

    actual_sha="$(sha256sum "${source_archive}" | awk '{print $1}')" \
      || return 1
    [[ "${actual_sha}" =~ ^[0-9a-f]{64}$ ]] || return 1
    expected_sha="$(component_expected_sha "${component}")"
    if [[ "${DOWNLOAD_COMPONENTS}" == true && -z "${COMPONENT_DIR}" \
        && -n "${expected_sha}" && "${actual_sha}" != "${expected_sha}" ]]; then
      echo "SHA-256 mismatch for ${archive_name}: expected ${expected_sha}, got ${actual_sha}." >&2
      return 1
    fi
    validate_component_archive "${component}" "${version}" "${protocol_name}" \
      "${protocol_major}" "${protocol_minor}" "${source_archive}" \
      || return 1
  done <<EOF
keysharp-input ${KEYSHARP_INPUT_REPOSITORY} ${KEYSHARP_INPUT_VERSION} ${KEYSHARP_INPUT_PROTOCOL_NAME} ${KEYSHARP_INPUT_PROTOCOL_MAJOR} ${KEYSHARP_INPUT_PROTOCOL_MINOR}
keysharp-desktop ${KEYSHARP_DESKTOP_REPOSITORY} ${KEYSHARP_DESKTOP_VERSION} ${KEYSHARP_DESKTOP_PROTOCOL_NAME} ${KEYSHARP_DESKTOP_PROTOCOL_MAJOR} ${KEYSHARP_DESKTOP_PROTOCOL_MINOR}
EOF
}

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
Recommends: keysharp-input (>= ${KEYSHARP_INPUT_VERSION}), ${KEYSHARP_INPUT_DEBIAN_PROTOCOL_PACKAGE}, keysharp-desktop (>= ${KEYSHARP_DESKTOP_VERSION}), ${KEYSHARP_DESKTOP_DEBIAN_PROTOCOL_PACKAGE}
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

preflight_component_archives

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

cp "${ASSETS_DIR}/install.sh" "${ASSETS_DIR}/uninstall.sh" \
  "${ASSETS_DIR}/install-components.sh" "${PKG_DIR}/"
cp "${ASSETS_DIR}/keyview.desktop" "${ASSETS_DIR}/keysharp.desktop" \
	"${ASSETS_DIR}/keysharp.xml" "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" "${PKG_DIR}/"
cp "${ROOT}/assets/Keysharp.png" "${PKG_DIR}/"
stage_components

chmod 0755 "${PKG_DIR}/install.sh" "${PKG_DIR}/uninstall.sh" \
  "${PKG_DIR}/install-components.sh"
chmod 0644 "${PKG_DIR}/keyview.desktop" "${PKG_DIR}/keysharp.desktop" \
	"${PKG_DIR}/keysharp.xml" "${PKG_DIR}/70-keysharp-i2c-uaccess.rules" "${PKG_DIR}/Keysharp.png"
if [[ -d "${PKG_DIR}/components" ]]; then
  find "${PKG_DIR}/components" -type d -exec chmod 0755 {} +
  find "${PKG_DIR}/components" -type f -exec chmod 0644 {} +
fi

build_tarball
build_deb
echo "Done."
