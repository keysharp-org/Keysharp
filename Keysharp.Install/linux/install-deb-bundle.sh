#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERIFY_ONLY=false
SNAPSHOT=""
SELECTED_DEBS=()
APT_AUTO_PACKAGES=()
APT_MANUAL_PACKAGES=()

usage() {
  cat <<'EOF'
Usage: install.sh [--verify-only]

Verifies and installs the Keysharp Debian bundle. Compatible installed helper
packages and complete standalone helper installations are left untouched.

  --verify-only  Verify checksums, architecture, and package metadata only.
  -h, --help     Show this help.
EOF
}

fail() {
  echo "keysharp Debian bundle: $*" >&2
  return 1
}

cleanup() {
  [[ -z "${SNAPSHOT}" ]] || rm -rf -- "${SNAPSHOT}"
}

is_safe_filename() {
  [[ "$1" =~ ^[A-Za-z0-9][A-Za-z0-9._+~-]*\.deb$ ]]
}

load_bundle_manifest() {
  local manifest="$1"
  local role package version architecture capability filename extra prefix
  local input_count=0 desktop_count=0 keysharp_count=0

  unset KEYSHARP_PACKAGE KEYSHARP_VERSION KEYSHARP_ARCH KEYSHARP_CAPABILITY
  unset KEYSHARP_DEB INPUT_PACKAGE INPUT_VERSION INPUT_ARCH INPUT_CAPABILITY
  unset INPUT_DEB
  unset DESKTOP_PACKAGE DESKTOP_VERSION DESKTOP_ARCH DESKTOP_CAPABILITY
  unset DESKTOP_DEB

  awk -F '\t' '
    /^[[:space:]]*#/ { next }
    /^[[:space:]]*$/ { next }
    NF != 6 { exit 1 }
  ' "${manifest}" || fail "bundle.tsv must contain exactly six fields per record."

  while IFS=$'\t' read -r role package version architecture capability filename extra; do
    [[ -n "${role}" && "${role}" != \#* ]] || continue
    [[ -z "${extra:-}" ]] || fail "bundle.tsv contains an extra field."
    [[ "${package}" =~ ^[a-z0-9][a-z0-9+.-]+$ ]] \
      || fail "invalid package name in bundle.tsv: ${package}"
    [[ "${version}" =~ ^[0-9A-Za-z][0-9A-Za-z.+:~_-]*$ ]] \
      || fail "invalid package version in bundle.tsv: ${version}"
    [[ "${architecture}" == amd64 || "${architecture}" == arm64 ]] \
      || fail "unsupported bundle architecture: ${architecture}"
    is_safe_filename "${filename}" \
      || fail "unsafe Debian filename in bundle.tsv: ${filename}"

    case "${role}" in
      keysharp)
        prefix=KEYSHARP
        keysharp_count=$((keysharp_count + 1))
        [[ "${package}" == keysharp && "${capability}" == - ]] \
          || fail "invalid Keysharp record in bundle.tsv"
        ;;
      input)
        prefix=INPUT
        input_count=$((input_count + 1))
        [[ "${package}" == keysharp-input \
          && "${capability}" =~ ^keysharp-input-client-abi-[0-9]+$ ]] \
          || fail "invalid input-component record in bundle.tsv"
        ;;
      desktop)
        prefix=DESKTOP
        desktop_count=$((desktop_count + 1))
        [[ "${package}" == keysharp-desktop \
          && "${capability}" =~ ^keysharp-desktop-client-abi-[0-9]+$ ]] \
          || fail "invalid desktop-component record in bundle.tsv"
        ;;
      *) fail "unknown role in bundle.tsv: ${role}" ;;
    esac

    printf -v "${prefix}_PACKAGE" '%s' "${package}"
    printf -v "${prefix}_VERSION" '%s' "${version}"
    printf -v "${prefix}_ARCH" '%s' "${architecture}"
    printf -v "${prefix}_CAPABILITY" '%s' "${capability}"
    printf -v "${prefix}_DEB" '%s' "${filename}"
  done < "${manifest}"

  [[ "${keysharp_count}" -eq 1 && "${input_count}" -eq 1 \
    && "${desktop_count}" -eq 1 ]] \
    || fail "bundle.tsv must contain one Keysharp, input, and desktop record."
  [[ "${KEYSHARP_ARCH}" == "${INPUT_ARCH}" \
    && "${KEYSHARP_ARCH}" == "${DESKTOP_ARCH}" ]] \
    || fail "bundle.tsv mixes package architectures."
  BUNDLE_ARCH="${KEYSHARP_ARCH}"
}

copy_regular_file() {
  local name="$1"
  local mode="${2:-0644}"
  local source="${SCRIPT_DIR}/${name}"
  [[ -f "${source}" && ! -L "${source}" ]] \
    || fail "missing or unsafe bundle file: ${name}"
  install -m "${mode}" -- "${source}" "${SNAPSHOT}/${name}"
  [[ -f "${SNAPSHOT}/${name}" && ! -L "${SNAPSHOT}/${name}" ]] \
    || fail "could not snapshot bundle file: ${name}"
}

prepare_snapshot() {
  SNAPSHOT="$(mktemp -d /tmp/keysharp-deb-bundle.XXXXXXXXXX)" \
    || fail "could not create a private temporary directory."
  copy_regular_file bundle.tsv
  load_bundle_manifest "${SNAPSHOT}/bundle.tsv"
  copy_regular_file SHA256SUMS
  copy_regular_file component-probe.sh 0755
  copy_regular_file "${KEYSHARP_DEB}"
  copy_regular_file "${INPUT_DEB}"
  copy_regular_file "${DESKTOP_DEB}"
}

verify_checksums() {
  local sums="${SNAPSHOT}/SHA256SUMS"
  local checked="${SNAPSHOT}/checked-sums"
  local line hash separator filename
  local count=0
  local -A expected=() seen=()

  expected[bundle.tsv]=1
  expected[component-probe.sh]=1
  expected["${KEYSHARP_DEB}"]=1
  expected["${INPUT_DEB}"]=1
  expected["${DESKTOP_DEB}"]=1
  : > "${checked}"

  while IFS= read -r line || [[ -n "${line}" ]]; do
    hash="${line:0:64}"
    separator="${line:64:2}"
    filename="${line:66}"
    [[ "${filename}" == bundle.tsv || "${filename}" == component-probe.sh \
      || "${filename}" =~ ^[A-Za-z0-9][A-Za-z0-9._+~-]*\.deb$ ]] \
      || fail "SHA256SUMS contains an unsafe filename."
    [[ "${hash}" =~ ^[0-9a-f]{64}$ && "${separator}" == "  " \
      && -n "${expected[${filename}]:-}" && -z "${seen[${filename}]:-}" ]] \
      || fail "SHA256SUMS has an unexpected or duplicate record."
    seen["${filename}"]=1
    count=$((count + 1))
    printf '%s  %s\n' "${hash}" "${filename}" >> "${checked}"
  done < "${sums}"

  [[ "${count}" -eq "${#expected[@]}" ]] \
    || fail "SHA256SUMS does not cover every bundle payload."
  for filename in "${!expected[@]}"; do
    [[ -n "${seen[${filename}]:-}" ]] \
      || fail "SHA256SUMS is missing ${filename}."
  done
  (cd "${SNAPSHOT}" && sha256sum --check --strict checked-sums >/dev/null) \
    || fail "a bundle payload failed checksum verification."
  bash -n "${SNAPSHOT}/component-probe.sh" \
    || fail "the bundled component probe is not valid Bash."
}

deb_field() {
  dpkg-deb -f "$1" "$2" 2>/dev/null
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

validate_deb_metadata() {
  local file="$1"
  local expected_package="$2"
  local expected_version="$3"
  local expected_arch="$4"
  local expected_capability="$5"
  local package version architecture provides

  dpkg-deb --info "${file}" >/dev/null 2>&1 \
    || fail "not a valid Debian package: $(basename -- "${file}")"
  package="$(deb_field "${file}" Package)"
  version="$(deb_field "${file}" Version)"
  architecture="$(deb_field "${file}" Architecture)"
  [[ "${package}" == "${expected_package}" ]] \
    || fail "$(basename -- "${file}") contains package ${package}, expected ${expected_package}."
  [[ "${version}" == "${expected_version}" ]] \
    || fail "${expected_package} has version ${version}, expected ${expected_version}."
  [[ "${architecture}" == "${expected_arch}" ]] \
    || fail "${expected_package} has architecture ${architecture}, expected ${expected_arch}."
  if [[ "${expected_capability}" != - ]]; then
    provides="$(deb_field "${file}" Provides)"
    provides_capability "${provides}" "${expected_capability}" \
      || fail "${expected_package} does not provide ${expected_capability}."
  fi
}

validate_bundle() {
  local host_arch recommends normalized_recommends
  host_arch="$(dpkg --print-architecture)"
  [[ "${host_arch}" == "${BUNDLE_ARCH}" ]] \
    || fail "this ${BUNDLE_ARCH} bundle cannot be installed on ${host_arch}."

  validate_deb_metadata "${SNAPSHOT}/${KEYSHARP_DEB}" \
    "${KEYSHARP_PACKAGE}" "${KEYSHARP_VERSION}" "${KEYSHARP_ARCH}" -
  validate_deb_metadata "${SNAPSHOT}/${INPUT_DEB}" \
    "${INPUT_PACKAGE}" "${INPUT_VERSION}" "${INPUT_ARCH}" "${INPUT_CAPABILITY}"
  validate_deb_metadata "${SNAPSHOT}/${DESKTOP_DEB}" \
    "${DESKTOP_PACKAGE}" "${DESKTOP_VERSION}" "${DESKTOP_ARCH}" "${DESKTOP_CAPABILITY}"

  recommends="$(deb_field "${SNAPSHOT}/${KEYSHARP_DEB}" Recommends)"
  normalized_recommends="$(tr -d '[:space:]' <<< "${recommends}")"
  [[ "${normalized_recommends}" \
    == "${INPUT_CAPABILITY},${DESKTOP_CAPABILITY}" ]] \
    || fail "Keysharp's Recommends are not the two exact bundled client ABIs."
}

query_provider_records() {
  dpkg-query -W -f='${db:Status-Abbrev}\t${Provides}\n' 2>/dev/null
}

installed_debian_provider_satisfies() {
  local capability="$1"
  local status provides token
  local -a tokens=()

  while IFS=$'\t' read -r status provides; do
    [[ "${status}" == "ii " ]] || continue
    IFS=, read -r -a tokens <<< "${provides}"
    for token in "${tokens[@]}"; do
      token="${token#"${token%%[![:space:]]*}"}"
      token="${token%%[[:space:]]*}"
      [[ "${token}" == "${capability}" ]] && return 0
    done
  done < <(query_provider_records || true)
  return 1
}

standalone_provider_satisfies() {
  local package="$1"
  local capability="$2"
  local abi="${capability##*-}"
  bash "${SNAPSHOT}/component-probe.sh" --probe-compatible \
    "${package}" "${abi}" \
    >/dev/null 2>&1
}

debian_package_installed() {
  local package="$1"
  [[ "$(dpkg-query -W -f='${db:Status-Abbrev}' "${package}" 2>/dev/null || true)" \
    == "ii " ]]
}

debian_package_is_auto() {
  local package="$1"
  /usr/bin/apt-mark showauto "${package}" 2>/dev/null \
    | grep -Fxq "${package}"
}

remember_selected_package_state() {
  local package="$1"
  if ! debian_package_installed "${package}"; then
    APT_AUTO_PACKAGES+=("${package}")
  elif debian_package_is_auto "${package}"; then
    APT_AUTO_PACKAGES+=("${package}")
  else
    APT_MANUAL_PACKAGES+=("${package}")
  fi
}

portable_layer_present() {
  local package="$1"
  bash "${SNAPSHOT}/component-probe.sh" --probe-portable-layer "${package}" \
    >/dev/null 2>&1
}

apt_mark() {
  /usr/bin/apt-mark "$@"
}

restore_helper_apt_states() {
  if (( ${#APT_AUTO_PACKAGES[@]} > 0 )); then
    apt_mark auto "${APT_AUTO_PACKAGES[@]}" >/dev/null \
      || fail "helper packages were installed, but their automatic-install state could not be restored. Run: sudo apt-mark auto ${APT_AUTO_PACKAGES[*]}"
  fi
  if (( ${#APT_MANUAL_PACKAGES[@]} > 0 )); then
    apt_mark manual "${APT_MANUAL_PACKAGES[@]}" >/dev/null \
      || fail "helper packages were installed, but their manual-install state could not be restored. Run: sudo apt-mark manual ${APT_MANUAL_PACKAGES[*]}"
  fi
}

select_install_debs() {
  local prefix package capability filename variable
  SELECTED_DEBS=()
  APT_AUTO_PACKAGES=()
  APT_MANUAL_PACKAGES=()

  for prefix in INPUT DESKTOP; do
    variable="${prefix}_PACKAGE"; package="${!variable}"
    variable="${prefix}_CAPABILITY"; capability="${!variable}"
    variable="${prefix}_DEB"; filename="${!variable}"

    if installed_debian_provider_satisfies "${capability}"; then
      if portable_layer_present "${package}"; then
        fail "a conflicting portable ${package} layer is present beside the installed Debian provider. Remove the portable layer before installing this bundle."
        return 1
      fi
      echo "Using an installed Debian provider for ${capability}; leaving it untouched."
    elif debian_package_installed "${package}"; then
      if portable_layer_present "${package}"; then
        fail "a conflicting portable ${package} layer is present. Remove it before updating the Debian package."
        return 1
      fi
      remember_selected_package_state "${package}"
      SELECTED_DEBS+=("${SNAPSHOT}/${filename}")
    elif standalone_provider_satisfies "${package}" "${capability}"; then
      echo "Using a compatible standalone ${package}; leaving it untouched."
    elif portable_layer_present "${package}"; then
      fail "an incompatible or incomplete portable ${package} layer is present. Run its independent uninstaller before installing this bundle."
      return 1
    else
      remember_selected_package_state "${package}"
      SELECTED_DEBS+=("${SNAPSHOT}/${filename}")
    fi
  done
  SELECTED_DEBS+=("${SNAPSHOT}/${KEYSHARP_DEB}")
}

main() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --verify-only) VERIFY_ONLY=true; shift ;;
      -h|--help) usage; return 0 ;;
      *) echo "Unknown option: $1" >&2; usage >&2; return 2 ;;
    esac
  done

  if [[ "${VERIFY_ONLY}" != true && "${EUID}" -ne 0 ]]; then
    echo "Run this installer as root, for example: sudo bash ./install.sh" >&2
    return 2
  fi
  for command in dpkg dpkg-deb dpkg-query sha256sum; do
    command -v "${command}" >/dev/null 2>&1 \
      || fail "${command} is required."
  done

  trap cleanup EXIT HUP INT TERM
  prepare_snapshot
  verify_checksums
  validate_bundle
  if [[ "${VERIFY_ONLY}" == true ]]; then
    echo "Bundle checksums and Debian metadata are valid for ${BUNDLE_ARCH}."
    return 0
  fi

  [[ -x /usr/bin/apt-get && -x /usr/bin/apt-mark ]] \
    || fail "apt-get and apt-mark are required to install this bundle."
  select_install_debs
  /usr/bin/apt-get install --no-install-recommends "${SELECTED_DEBS[@]}"
  restore_helper_apt_states
  echo "Keysharp installation complete. Helper packages and permission grants have independent lifecycles."
  echo "Removing Keysharp later does not remove either helper or /var/lib/keysharp-permissions/v1."
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
