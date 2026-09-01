#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
COMPONENT_LOCK="${ASSETS_DIR}/component-versions.conf"
DIST_DIR="${KEYSHARP_DIST_DIR:-${ROOT}/dist}"
COMPONENT_DIR="${KEYSHARP_COMPONENT_DEB_DIR:-}"
DOWNLOAD_COMPONENTS=false
KEYSHARP_DEB=""

usage() {
  cat <<'EOF'
Usage: package-linux-deb-bundle.sh [options]

Builds a one-download Debian bundle containing Keysharp and its two independent
helper packages. package-linux.sh must build the Keysharp .deb first.

Options:
  --keysharp-deb FILE    Use this Keysharp Debian package.
  --dependency-dir DIR  Use helper Debian packages from DIR.
  --download-components Download the locked helper releases from GitHub.
  -h, --help            Show this help.

KEYSHARP_COMPONENT_DEB_DIR selects a local dependency directory;
KEYSHARP_DIST_DIR selects the output directory. Release builds use
--download-components after the release hashes have been pinned.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --keysharp-deb)
      [[ $# -ge 2 ]] || { echo "--keysharp-deb requires a file" >&2; exit 2; }
      KEYSHARP_DEB="$2"
      shift 2
      ;;
    --dependency-dir)
      [[ $# -ge 2 ]] || { echo "--dependency-dir requires a directory" >&2; exit 2; }
      COMPONENT_DIR="$2"
      shift 2
      ;;
    --download-components)
      DOWNLOAD_COMPONENTS=true
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

[[ -f "${COMPONENT_LOCK}" ]] || {
  echo "Standalone-component lock file is missing: ${COMPONENT_LOCK}" >&2
  exit 1
}
# shellcheck source=linux/component-versions.conf
source "${COMPONENT_LOCK}"

detect_default_rid() {
  case "$(uname -m)" in
    x86_64) echo linux-x64 ;;
    aarch64|arm64) echo linux-arm64 ;;
    *)
      echo "Unable to infer a supported Linux RID from $(uname -m)." >&2
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
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${ROOT}/Keysharp/Keysharp.csproj" | head -n 1)}"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"
[[ -n "${VERSION}" ]] || {
  echo "Unable to determine the Keysharp version. Set VERSION explicitly." >&2
  exit 1
}

if [[ -z "${KEYSHARP_DEB}" ]]; then
  KEYSHARP_DEB="${DIST_DIR}/keysharp_${VERSION}_${DEB_ARCH}.deb"
fi
[[ -f "${KEYSHARP_DEB}" ]] || {
  echo "Keysharp Debian package not found: ${KEYSHARP_DEB}" >&2
  exit 1
}
if [[ -n "${COMPONENT_DIR}" && "${DOWNLOAD_COMPONENTS}" == true ]]; then
  echo "Use either --dependency-dir or --download-components, not both." >&2
  exit 2
fi

component_deb_name() {
  printf '%s_%s_%s.deb\n' "$1" "$2" "${DEB_ARCH}"
}

component_deb_expected_sha() {
  local component="$1"
  local arch_key value_name
  arch_key="$(printf '%s' "${DEB_ARCH}" | tr '[:lower:]-' '[:upper:]_')"
  case "${component}" in
    keysharp-input) value_name="KEYSHARP_INPUT_DEB_SHA256_${arch_key}" ;;
    keysharp-desktop) value_name="KEYSHARP_DESKTOP_DEB_SHA256_${arch_key}" ;;
    *) return 1 ;;
  esac
  printf '%s\n' "${!value_name:-}"
}

find_local_component_deb() {
  local component="$1"
  local filename="$2"
  local repository_dir candidate
  local -a candidates=()
  if [[ -n "${COMPONENT_DIR}" ]]; then
    candidates+=("${COMPONENT_DIR}/${filename}")
  else
    repository_dir="${ROOT}/../${component}"
    candidates+=(
      "${repository_dir}/dist/release-assets/${filename}"
      "${repository_dir}/dist/${filename}"
      "${repository_dir}/build/release/${filename}"
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

download_component_deb() {
  local repository="$1"
  local version="$2"
  local filename="$3"
  local expected_sha="$4"
  local destination="$5"
  local partial="${destination}.partial.$$"
  [[ "${expected_sha}" =~ ^[0-9a-f]{64}$ ]] || {
    echo "Refusing to download ${filename}: its SHA-256 is not pinned." >&2
    return 1
  }
  command -v curl >/dev/null 2>&1 || {
    echo "curl is required for --download-components." >&2
    return 1
  }
  rm -f -- "${partial}"
  curl --fail --location --proto '=https' --tlsv1.2 \
    --output "${partial}" \
    "https://github.com/${repository}/releases/download/v${version}/${filename}" \
    || { rm -f -- "${partial}"; return 1; }
  if [[ "$(sha256sum "${partial}" | awk '{print $1}')" != "${expected_sha}" ]]; then
    echo "SHA-256 mismatch for downloaded ${filename}." >&2
    rm -f -- "${partial}"
    return 1
  fi
  mv -f -- "${partial}" "${destination}"
}

provides_capability() {
  local provides="$1"
  local capability="$2"
  local token
  local -a tokens=()
  IFS=, read -r -a tokens <<< "${provides}"
  for token in "${tokens[@]}"; do
    token="${token#"${token%%[![:space:]]*}"}"
    token="${token%%[[:space:]]*}"
    [[ "${token}" == "${capability}" ]] && return 0
  done
  return 1
}

validate_deb() {
  local file="$1"
  local expected_package="$2"
  local expected_version="$3"
  local expected_capability="$4"
  local package version architecture provides
  dpkg-deb --info "${file}" >/dev/null 2>&1 || {
    echo "Not a valid Debian package: ${file}" >&2
    return 1
  }
  package="$(dpkg-deb -f "${file}" Package)"
  version="$(dpkg-deb -f "${file}" Version)"
  architecture="$(dpkg-deb -f "${file}" Architecture)"
  [[ "${package}" == "${expected_package}" \
    && "${version}" == "${expected_version}" \
    && "${architecture}" == "${DEB_ARCH}" ]] || {
    echo "Debian metadata mismatch for $(basename -- "${file}")." >&2
    echo "Expected ${expected_package} ${expected_version} ${DEB_ARCH}." >&2
    return 1
  }
  if [[ "${expected_capability}" != - ]]; then
    provides="$(dpkg-deb -f "${file}" Provides)"
    provides_capability "${provides}" "${expected_capability}" || {
      echo "${expected_package} does not provide ${expected_capability}." >&2
      return 1
    }
  fi
}

validate_keysharp_relationships() {
  local recommends normalized_recommends
  recommends="$(dpkg-deb -f "${KEYSHARP_DEB}" Recommends)"
  normalized_recommends="$(tr -d '[:space:]' <<< "${recommends}")"
  [[ "${normalized_recommends}" \
    == "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE},${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}" ]] || {
    echo "Keysharp's Recommends do not match the locked client ABIs." >&2
    return 1
  }
}

mkdir -p "${DIST_DIR}"
WORK_DIR="$(mktemp -d "${DIST_DIR%/}/deb-bundle-work.XXXXXXXXXX")"
cleanup() {
  rm -rf -- "${WORK_DIR}"
}
trap cleanup EXIT HUP INT TERM

resolve_component_deb() {
  local component="$1"
  local repository="$2"
  local version="$3"
  local filename source expected_sha actual_sha
  filename="$(component_deb_name "${component}" "${version}")"
  source="$(find_local_component_deb "${component}" "${filename}" || true)"
  expected_sha="$(component_deb_expected_sha "${component}")"
  if [[ -z "${source}" ]]; then
    if [[ "${DOWNLOAD_COMPONENTS}" != true ]]; then
      echo "Required helper package not found: ${filename}" >&2
      echo "Use --dependency-dir or --download-components." >&2
      return 1
    fi
    source="${WORK_DIR}/${filename}"
    download_component_deb \
      "${repository}" "${version}" "${filename}" "${expected_sha}" "${source}"
  fi
  actual_sha="$(sha256sum "${source}" | awk '{print $1}')"
  if [[ "${DOWNLOAD_COMPONENTS}" == true && -z "${COMPONENT_DIR}" \
      && "${actual_sha}" != "${expected_sha}" ]]; then
    echo "SHA-256 mismatch for ${filename}: expected ${expected_sha}, got ${actual_sha}." >&2
    return 1
  fi
  printf '%s\n' "${source}"
}

INPUT_DEB_SOURCE="$(resolve_component_deb keysharp-input \
  "${KEYSHARP_INPUT_REPOSITORY}" "${KEYSHARP_INPUT_VERSION}")"
DESKTOP_DEB_SOURCE="$(resolve_component_deb keysharp-desktop \
  "${KEYSHARP_DESKTOP_REPOSITORY}" "${KEYSHARP_DESKTOP_VERSION}")"

validate_deb "${KEYSHARP_DEB}" keysharp "${VERSION}" -
validate_deb "${INPUT_DEB_SOURCE}" keysharp-input "${KEYSHARP_INPUT_VERSION}" \
  "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}"
validate_deb "${DESKTOP_DEB_SOURCE}" keysharp-desktop "${KEYSHARP_DESKTOP_VERSION}" \
  "${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}"
validate_keysharp_relationships

BUNDLE_NAME="keysharp-${VERSION}-${RID}-deb-bundle"
BUNDLE_ROOT="${WORK_DIR}/${BUNDLE_NAME}"
BUNDLE_OUT="${DIST_DIR}/${BUNDLE_NAME}.tar.gz"
INPUT_DEB_NAME="$(component_deb_name keysharp-input "${KEYSHARP_INPUT_VERSION}")"
DESKTOP_DEB_NAME="$(component_deb_name keysharp-desktop "${KEYSHARP_DESKTOP_VERSION}")"
KEYSHARP_DEB_NAME="keysharp_${VERSION}_${DEB_ARCH}.deb"
mkdir -p "${BUNDLE_ROOT}"
install -m 0755 "${ASSETS_DIR}/install-deb-bundle.sh" "${BUNDLE_ROOT}/install.sh"
install -m 0755 "${ASSETS_DIR}/install-components.sh" "${BUNDLE_ROOT}/component-probe.sh"
install -m 0644 "${KEYSHARP_DEB}" "${BUNDLE_ROOT}/${KEYSHARP_DEB_NAME}"
install -m 0644 "${INPUT_DEB_SOURCE}" "${BUNDLE_ROOT}/${INPUT_DEB_NAME}"
install -m 0644 "${DESKTOP_DEB_SOURCE}" "${BUNDLE_ROOT}/${DESKTOP_DEB_NAME}"

cat > "${BUNDLE_ROOT}/bundle.tsv" <<EOF
# role	package	version	architecture	client-abi-package	filename
keysharp	keysharp	${VERSION}	${DEB_ARCH}	-	${KEYSHARP_DEB_NAME}
input	keysharp-input	${KEYSHARP_INPUT_VERSION}	${DEB_ARCH}	${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}	${INPUT_DEB_NAME}
desktop	keysharp-desktop	${KEYSHARP_DESKTOP_VERSION}	${DEB_ARCH}	${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}	${DESKTOP_DEB_NAME}
EOF

(
  cd "${BUNDLE_ROOT}"
  sha256sum bundle.tsv component-probe.sh \
    "${KEYSHARP_DEB_NAME}" "${INPUT_DEB_NAME}" "${DESKTOP_DEB_NAME}" \
    > SHA256SUMS
)
tar -czf "${BUNDLE_OUT}" -C "${WORK_DIR}" "${BUNDLE_NAME}"
echo "Debian bundle ready at ${BUNDLE_OUT}"
