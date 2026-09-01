#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

PATH=/usr/sbin:/usr/bin:/sbin:/bin:/usr/local/sbin:/usr/local/bin
export PATH

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="${SCRIPT_DIR}/components/manifest.tsv"
PROBE_COMPONENT=""
PROBE_ABI=""
PROBE_PORTABLE_COMPONENT=""
MINIMUM_CLIENT_ABI_MINOR=1

usage() {
  cat <<'EOF'
Usage: install-components.sh
       install-components.sh --probe-compatible COMPONENT ABI
       install-components.sh --probe-portable-layer COMPONENT

Installs missing standalone Linux components from this bundle. Compatibility is
defined by the public libkeysharp-*.so ABI. Existing compatible installations
are left untouched, and package-managed installations are never overwritten.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --probe-compatible)
      [[ $# -ge 3 && -z "${PROBE_COMPONENT}" && -z "${PROBE_PORTABLE_COMPONENT}" ]] \
        || { echo "--probe-compatible requires one COMPONENT ABI request" >&2; exit 2; }
      PROBE_COMPONENT="$2"
      PROBE_ABI="$3"
      shift 3
      ;;
    --probe-portable-layer)
      [[ $# -ge 2 && -z "${PROBE_COMPONENT}" && -z "${PROBE_PORTABLE_COMPONENT}" ]] \
        || { echo "--probe-portable-layer requires one COMPONENT request" >&2; exit 2; }
      PROBE_PORTABLE_COMPONENT="$2"
      shift 2
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

component_valid() {
  [[ "$1" == keysharp-input || "$1" == keysharp-desktop ]]
}

abi_valid() {
  [[ "$1" =~ ^[0-9]+$ ]]
}

canonical_path() {
  readlink -f -- "$1" 2>/dev/null
}

root_protected_chain() {
  local path="$1"
  local directory owner mode
  directory="$(dirname -- "$path")"
  while :; do
    read -r owner mode < <(stat -Lc '%u %a' -- "${directory}" 2>/dev/null) || return 1
    [[ "${owner}" == 0 && "${mode}" =~ ^[0-7]{3,4}$ ]] || return 1
    if (( (8#${mode} & 8#0022) != 0 )); then
      [[ "${directory}" == /nix/store ]] \
        && (( (8#${mode} & 8#0002) == 0 )) \
        && (( (8#${mode} & 8#1000) != 0 )) \
        || return 1
    fi
    [[ "${directory}" == / ]] && return 0
    directory="$(dirname -- "${directory}")"
  done
}

trusted_file() {
  local path="$1"
  local executable="${2:-false}"
  local resolved owner mode
  [[ "${path}" == /* && ( -e "${path}" || -L "${path}" ) ]] || return 1
  resolved="$(canonical_path "${path}")" || return 1
  [[ -f "${resolved}" && -s "${resolved}" ]] || return 1
  [[ "${executable}" != true || -x "${resolved}" ]] || return 1
  read -r owner mode < <(stat -Lc '%u %a' -- "${resolved}" 2>/dev/null) || return 1
  [[ "${owner}" == 0 && "${mode}" =~ ^[0-7]{3,4}$ ]] || return 1
  (( (8#${mode} & 8#0022) == 0 )) || return 1
  root_protected_chain "${path}" && root_protected_chain "${resolved}"
}

trusted_private_executable() {
  local resolved mode
  trusted_file "$1" true || return 1
  resolved="$(canonical_path "$1")" || return 1
  mode="$(stat -Lc '%a' -- "${resolved}" 2>/dev/null)" || return 1
  [[ "${mode}" =~ ^[0-7]{3,4}$ ]] && (( (8#${mode} & 8#0077) == 0 ))
}

trusted_any() {
  local executable="$1"
  shift
  local candidate
  for candidate in "$@"; do
    trusted_file "${candidate}" "${executable}" && return 0
  done
  return 1
}

binary_candidates() {
  local component="$1"
  printf '%s\n' \
    "/usr/bin/${component}" \
    "/run/current-system/sw/bin/${component}" \
    "/usr/local/bin/${component}"
}

library_candidates() {
  local component="$1"
  local abi="$2"
  local soname="lib${component}.so.${abi}"
  printf '%s\n' \
    "/usr/lib/${soname}" \
    "/usr/lib64/${soname}" \
    "/usr/lib/x86_64-linux-gnu/${soname}" \
    "/usr/lib/aarch64-linux-gnu/${soname}" \
    "/run/current-system/sw/lib/${soname}" \
    "/usr/local/lib/${soname}" \
    "/usr/local/lib64/${soname}" \
    "/usr/local/lib/x86_64-linux-gnu/${soname}" \
    "/usr/local/lib/aarch64-linux-gnu/${soname}"
}

resolve_trusted_binary() {
  local component="$1"
  local candidate
  while IFS= read -r candidate; do
    trusted_file "${candidate}" true || continue
    printf '%s\n' "${candidate}"
    return 0
  done < <(binary_candidates "${component}")
  return 1
}

resolve_trusted_library() {
  local component="$1"
  local abi="$2"
  local expected="lib${component}.so.${abi}"
  local candidate dynamic
  while IFS= read -r candidate; do
    trusted_file "${candidate}" || continue
    if command -v readelf >/dev/null 2>&1; then
      dynamic="$(readelf -d "$(canonical_path "${candidate}")" 2>/dev/null || true)"
      grep -Eq "\(SONAME\).*\[${expected//./\\.}\]" <<< "${dynamic}" || continue
    fi
    printf '%s\n' "${candidate}"
    return 0
  done < <(library_candidates "${component}" "${abi}")
  return 1
}

debian_provider_satisfies() {
  local capability="$1"
  local status provides token
  local -a tokens=()
  command -v dpkg-query >/dev/null 2>&1 || return 1
  while IFS=$'\t' read -r status provides; do
    [[ "${status}" == "ii " ]] || continue
    IFS=, read -r -a tokens <<< "${provides}"
    for token in "${tokens[@]}"; do
      token="${token#"${token%%[![:space:]]*}"}"
      token="${token%%[[:space:]]*}"
      [[ "${token}" == "${capability}" ]] && return 0
    done
  done < <(dpkg-query -W -f='${db:Status-Abbrev}\t${Provides}\n' 2>/dev/null || true)
  return 1
}

client_abi_matches() {
  local binary="$1"
  local expected_major="$2"
  "${binary}" info 2>/dev/null | awk -F= \
    -v expected_major="${expected_major}" \
    -v minimum_minor="${MINIMUM_CLIENT_ABI_MINOR}" '
    $1 == "client_abi_major" { major_count++; major = $2; next }
    $1 == "client_abi_minor" { minor_count++; minor = $2; next }
    END {
      if (major_count != 1 || minor_count != 1 ||
          major !~ /^[0-9]+$/ || minor !~ /^[0-9]+$/ ||
          major + 0 != expected_major || minor + 0 < minimum_minor)
        exit 1
    }
  '
}

trusted_relative_files() {
  local root="$1"
  shift
  local relative
  for relative in "$@"; do
    trusted_file "${root}/${relative}" || return 1
  done
}

component_resources_present() {
  local component="$1"
  local binary="$2"
  local prefix policy
  local -a system_units=() user_units=()

  case "${binary}" in
    /usr/bin/*)
      prefix=/usr
      system_units=(/usr/lib/systemd/system /lib/systemd/system)
      user_units=(/usr/lib/systemd/user /usr/share/systemd/user)
      ;;
    /usr/local/bin/*)
      prefix=/usr/local
      system_units=(/etc/systemd/system /usr/local/lib/systemd/system)
      user_units=(/usr/local/lib/systemd/user /usr/local/share/systemd/user)
      ;;
    *) return 1 ;;
  esac

  policy=/usr/share/polkit-1/actions/org.keysharp."${component#keysharp-}".policy
  trusted_file "${policy}" || return 1

  case "${component}" in
    keysharp-input)
      trusted_any false \
        "${system_units[0]}/keysharp-input.service" \
        "${system_units[1]}/keysharp-input.service" \
        && trusted_any false \
          "${system_units[0]}/keysharp-input.socket" \
          "${system_units[1]}/keysharp-input.socket" \
        || return 1
      if [[ "${prefix}" == /usr ]]; then
        trusted_file /usr/lib/tmpfiles.d/keysharp-input-permissions.conf \
          && trusted_any false \
            /usr/lib/udev/rules.d/70-keysharp-input-uaccess.rules \
            /lib/udev/rules.d/70-keysharp-input-uaccess.rules
      else
        trusted_file /usr/local/lib/tmpfiles.d/keysharp-input-permissions.conf \
          && trusted_file /etc/udev/rules.d/70-keysharp-input-uaccess.rules
      fi
      ;;
    keysharp-desktop)
      trusted_any false \
        "${system_units[0]}/keysharp-desktop-authority.service" \
        "${system_units[1]}/keysharp-desktop-authority.service" \
        && trusted_any false \
          "${system_units[0]}/keysharp-desktop-authority.socket" \
          "${system_units[1]}/keysharp-desktop-authority.socket" \
        && trusted_any false \
          "${user_units[0]}/keysharp-desktop.service" \
          "${user_units[1]}/keysharp-desktop.service" \
        || return 1
      trusted_private_executable \
        "${prefix}/libexec/keysharp-desktop-capture-worker" \
        && trusted_relative_files "${prefix}" \
          lib/tmpfiles.d/keysharp-desktop-permissions.conf \
          share/applications/org.keysharp.DesktopCapture.desktop \
          share/gnome-shell/extensions/keysharp@keysharp.io/extension.js \
          share/gnome-shell/extensions/keysharp@keysharp.io/metadata.json \
          share/cinnamon/extensions/keysharp@keysharp.io/extension.js \
          share/cinnamon/extensions/keysharp@keysharp.io/metadata.json
      ;;
    *) return 1 ;;
  esac
}

component_package_installed() {
  local component="$1"
  if command -v dpkg-query >/dev/null 2>&1 \
      && dpkg-query -W -f='${db:Status-Abbrev}' "${component}" 2>/dev/null \
        | grep -q '^ii '; then
    return 0
  fi
  command -v rpm >/dev/null 2>&1 && rpm -q "${component}" >/dev/null 2>&1 && return 0
  command -v pacman >/dev/null 2>&1 && pacman -Q "${component}" >/dev/null 2>&1 && return 0
  trusted_file "/run/current-system/sw/bin/${component}" true && return 0
  return 1
}

path_is_distinct_from() {
  local candidate="$1"
  shift
  local actual expected
  [[ -e "${candidate}" || -L "${candidate}" ]] || return 1
  if [[ -L "${candidate}" ]]; then
    actual="$(readlink -m -- "${candidate}" 2>/dev/null)" || return 0
    for expected in "$@"; do
      expected="$(readlink -m -- "${expected}" 2>/dev/null)" || continue
      [[ "${actual}" == "${expected}" ]] && return 1
    done
  else
    for expected in "$@"; do
      [[ -e "${expected}" && "${candidate}" -ef "${expected}" ]] && return 1
    done
  fi
  return 0
}

portable_layer_present_under() {
  local component="$1"
  local root="${2%/}"
  local local_library
  local -a package_libraries=(
    "${root}/usr/lib/lib${component}.so.0"
    "${root}/usr/lib64/lib${component}.so.0"
    "${root}/usr/lib/x86_64-linux-gnu/lib${component}.so.0"
    "${root}/usr/lib/aarch64-linux-gnu/lib${component}.so.0"
  )

  path_is_distinct_from "${root}/usr/local/bin/${component}" \
    "${root}/usr/bin/${component}" && return 0
  for local_library in \
      "${root}/usr/local/lib/lib${component}.so.0" \
      "${root}/usr/local/lib64/lib${component}.so.0" \
      "${root}/usr/local/lib/x86_64-linux-gnu/lib${component}.so.0" \
      "${root}/usr/local/lib/aarch64-linux-gnu/lib${component}.so.0"; do
    path_is_distinct_from "${local_library}" "${package_libraries[@]}" \
      && return 0
  done

  case "${component}" in
    keysharp-input)
      path_is_distinct_from "${root}/etc/systemd/system/keysharp-input.service" \
        "${root}/usr/lib/systemd/system/keysharp-input.service" \
        "${root}/lib/systemd/system/keysharp-input.service" && return 0
      path_is_distinct_from "${root}/etc/systemd/system/keysharp-input.socket" \
        "${root}/usr/lib/systemd/system/keysharp-input.socket" \
        "${root}/lib/systemd/system/keysharp-input.socket" && return 0
      path_is_distinct_from \
        "${root}/usr/local/lib/tmpfiles.d/keysharp-input-permissions.conf" \
        "${root}/usr/lib/tmpfiles.d/keysharp-input-permissions.conf" && return 0
      ;;
    keysharp-desktop)
      path_is_distinct_from \
        "${root}/usr/local/lib/systemd/user/keysharp-desktop.service" \
        "${root}/usr/lib/systemd/user/keysharp-desktop.service" && return 0
      path_is_distinct_from \
        "${root}/usr/local/share/systemd/user/keysharp-desktop.service" \
        "${root}/usr/lib/systemd/user/keysharp-desktop.service" && return 0
      path_is_distinct_from \
        "${root}/usr/local/lib/systemd/system/keysharp-desktop-authority.service" \
        "${root}/usr/lib/systemd/system/keysharp-desktop-authority.service" && return 0
      path_is_distinct_from \
        "${root}/usr/local/lib/systemd/system/keysharp-desktop-authority.socket" \
        "${root}/usr/lib/systemd/system/keysharp-desktop-authority.socket" && return 0
      path_is_distinct_from \
        "${root}/usr/local/lib/tmpfiles.d/keysharp-desktop-permissions.conf" \
        "${root}/usr/lib/tmpfiles.d/keysharp-desktop-permissions.conf" && return 0
      ;;
  esac
  return 1
}

component_compatible() {
  local component="$1"
  local abi="$2"
  local capability="${component}-client-abi-${abi}"
  local binary
  debian_provider_satisfies "${capability}" && return 0
  binary="$(resolve_trusted_binary "${component}")" || return 1
  resolve_trusted_library "${component}" "${abi}" >/dev/null || return 1
  client_abi_matches "${binary}" "${abi}" || return 1
  [[ "${binary}" == /run/current-system/sw/bin/* ]] \
    || component_resources_present "${component}" "${binary}"
}

verify_archive() {
  local archive="$1"
  local expected_sha="$2"
  local actual_sha
  [[ "${expected_sha}" =~ ^[0-9a-f]{64}$ && -f "${archive}" ]] || return 1
  actual_sha="$(sha256sum "${archive}" | awk '{print $1}')"
  [[ "${actual_sha}" == "${expected_sha}" ]]
}

validate_archive_paths() {
  local archive="$1"
  local entry detail type target listing
  listing="$(tar -tzf "${archive}")" || return 1
  while IFS= read -r entry; do
    entry="${entry#./}"
    [[ -n "${entry}" && "${entry}" != /* \
      && "${entry}" != .. && "${entry}" != ../* \
      && "${entry}" != */../* && "${entry}" != */.. ]] || return 1
  done <<< "${listing}"
  listing="$(tar -tvzf "${archive}")" || return 1
  while IFS= read -r detail; do
    type="${detail:0:1}"
    case "${type}" in
      -|d) ;;
      l)
        target="${detail##* -> }"
        [[ "${target}" =~ ^[A-Za-z0-9._+-]+$ ]] || return 1
        ;;
      *) return 1 ;;
    esac
  done <<< "${listing}"
}

install_archive() {
  local archive="$1"
  local expected_sha="$2"
  local temporary snapshot installer status=0
  temporary="$(mktemp -d /tmp/keysharp-component.XXXXXXXXXX)" || return 1
  snapshot="${temporary}/component.tar.gz"
  install -m 0600 -- "${archive}" "${snapshot}" || status=$?
  if [[ "${status}" -eq 0 ]]; then
    verify_archive "${snapshot}" "${expected_sha}" || status=1
  fi
  if [[ "${status}" -eq 0 ]]; then
    validate_archive_paths "${snapshot}" || status=1
  fi
  if [[ "${status}" -eq 0 ]]; then
    tar -xzf "${snapshot}" -C "${temporary}" || status=1
  fi
  if [[ "${status}" -eq 0 ]]; then
    installer="$(find "${temporary}" -mindepth 2 -maxdepth 2 -type f -name install.sh -print -quit)"
    [[ -n "${installer}" ]] || status=1
  fi
  if [[ "${status}" -eq 0 ]]; then
    chmod 0755 "${installer}"
    bash "${installer}" --skip-if-compatible || status=$?
  fi
  rm -rf -- "${temporary}"
  return "${status}"
}

if [[ -n "${PROBE_COMPONENT}" ]]; then
  component_valid "${PROBE_COMPONENT}" && abi_valid "${PROBE_ABI}" || {
    echo "Invalid component compatibility request." >&2
    exit 2
  }
  if component_compatible "${PROBE_COMPONENT}" "${PROBE_ABI}"; then
    echo "Compatible ${PROBE_COMPONENT} client ABI ${PROBE_ABI} is installed."
    exit 0
  fi
  exit 1
fi

if [[ -n "${PROBE_PORTABLE_COMPONENT}" ]]; then
  component_valid "${PROBE_PORTABLE_COMPONENT}" || {
    echo "Invalid component portable-layer request." >&2
    exit 2
  }
  portable_layer_present_under "${PROBE_PORTABLE_COMPONENT}" / \
    && exit 0
  exit 1
fi

[[ "${EUID}" -eq 0 ]] || {
  echo "Standalone Linux components require a root installation." >&2
  exit 2
}

if [[ ! -f "${MANIFEST}" ]]; then
  echo "No bundled standalone-component manifest was found; leaving the system unchanged."
  exit 0
fi

manifest_snapshot="$(mktemp /tmp/keysharp-components-manifest.XXXXXXXXXX)"
trap 'rm -f -- "${manifest_snapshot}"' EXIT HUP INT TERM
install -m 0600 -- "${MANIFEST}" "${manifest_snapshot}"
awk -F '\t' '
  /^[[:space:]]*#/ || /^[[:space:]]*$/ { next }
  NF != 5 { exit 1 }
  $1 == "keysharp-input" { input++ ; next }
  $1 == "keysharp-desktop" { desktop++ ; next }
  { exit 1 }
  END { if (input != 1 || desktop != 1) exit 1 }
' "${manifest_snapshot}" || {
  echo "The component manifest must contain one input and one desktop record." >&2
  exit 1
}

failures=0
while IFS=$'\t' read -r component product_version client_abi archive_name archive_sha extra; do
  component="${component%$'\r'}"
  [[ -n "${component}" && "${component}" != \#* ]] || continue
  archive_sha="${archive_sha%$'\r'}"
  if [[ -n "${extra:-}" ]] || ! component_valid "${component}" \
      || ! abi_valid "${client_abi}" \
      || [[ ! "${product_version}" =~ ^[0-9A-Za-z][0-9A-Za-z.+:~_-]*$ ]] \
      || [[ ! "${archive_name}" =~ ^[A-Za-z0-9][A-Za-z0-9._+~-]*\.tar\.gz$ ]] \
      || [[ ! "${archive_sha}" =~ ^[0-9a-f]{64}$ ]]; then
    echo "Invalid standalone-component manifest record." >&2
    exit 1
  fi
  if component_compatible "${component}" "${client_abi}"; then
    echo "Using installed ${component} client ABI ${client_abi}; leaving it untouched."
    continue
  fi
  if component_package_installed "${component}"; then
    echo "${component} is package-managed but does not provide client ABI ${client_abi}." >&2
    echo "Upgrade it with the package manager; it will not be overwritten." >&2
    failures=$((failures + 1))
    continue
  fi

  archive="${SCRIPT_DIR}/components/${archive_name}"
  echo "Installing ${component} ${product_version} from the bundled archive."
  if ! install_archive "${archive}" "${archive_sha}" \
      || ! component_compatible "${component}" "${client_abi}"; then
    echo "Failed to install ${component} client ABI ${client_abi}." >&2
    failures=$((failures + 1))
  fi
done < "${manifest_snapshot}"
exit "${failures}"
