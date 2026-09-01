#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: uninstall.sh

Removes Keysharp only. Independently installed keysharp-input and
keysharp-desktop components are always retained.
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

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_INSTALL=false
if [[ "${EUID}" -eq 0 ]]; then
  ROOT_INSTALL=true
  PATH=/usr/sbin:/usr/bin:/sbin:/bin:/usr/local/sbin:/usr/local/bin
  export PATH
fi

if [[ -z "${PREFIX:-}" ]]; then
  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    PREFIX="/usr/local"
  else
    PREFIX="${HOME}/.local"
  fi
fi

XDG_DATA_HOME="${XDG_DATA_HOME:-${HOME}/.local/share}"
APP_DIR_TARGET="${PREFIX}/lib/keysharp"
BINDIR="${PREFIX}/bin"
if [[ "${ROOT_INSTALL}" == "true" ]]; then
  DESKTOP_DIR="/usr/share/applications"
  MIME_ROOT="/usr/share/mime"
  ICON_ROOT="/usr/share/icons/hicolor"
else
  DESKTOP_DIR="${XDG_DATA_HOME}/applications"
  MIME_ROOT="${XDG_DATA_HOME}/mime"
  ICON_ROOT="${XDG_DATA_HOME}/icons/hicolor"
fi
MIME_DIR="${MIME_ROOT}/packages"
ICON_DIR="${ICON_ROOT}/256x256/apps"

maybe_run() { command -v "$1" >/dev/null 2>&1 && "$@"; }
have_pkg() { command -v "$1" >/dev/null 2>&1; }

package_keysharp_is_installed() {
  if have_pkg dpkg-query \
      && dpkg-query -W -f='${db:Status-Abbrev}\n' keysharp 2>/dev/null | grep -q '^ii'; then
    return 0
  fi
  if have_pkg rpm && rpm -q keysharp >/dev/null 2>&1; then
    return 0
  fi
  if have_pkg pacman \
      && { pacman -Q keysharp >/dev/null 2>&1 \
        || pacman -Q keysharp-git >/dev/null 2>&1; }; then
    return 0
  fi
  return 1
}

path_is_exact_alias() {
  local path="$1"
  shift
  local actual expected

  [[ -L "${path}" ]] || return 1
  actual="$(readlink -m -- "${path}" 2>/dev/null)" || return 1
  for expected in "$@"; do
    expected="$(readlink -m -- "${expected}" 2>/dev/null)" || continue
    [[ "${actual}" == "${expected}" ]] && return 0
  done
  return 1
}

path_is_protected_system_artifact() {
  local path="$1"
  local owner mode

  [[ -e "${path}" || -L "${path}" ]] || return 1
  if [[ -L "${path}" ]]; then
    owner="$(stat -c '%u' -- "${path}" 2>/dev/null)" || return 1
    [[ "${owner}" == 0 ]]
    return
  fi
  read -r owner mode < <(stat -Lc '%u %a' -- "${path}" 2>/dev/null) || return 1
  [[ "${owner}" == 0 && "${mode}" =~ ^[0-7]{3,4}$ ]] || return 1
  (( (8#${mode} & 022) == 0 ))
}

system_channel_is_present() {
  package_keysharp_is_installed && return 0
  if path_is_protected_system_artifact /usr/lib/keysharp \
      && ! path_is_exact_alias /usr/lib/keysharp "${APP_DIR_TARGET}"; then
    return 0
  fi
  if path_is_protected_system_artifact /usr/bin/keysharp \
      && ! path_is_exact_alias /usr/bin/keysharp "${APP_DIR_TARGET}/Keysharp"; then
    return 0
  fi
  if path_is_protected_system_artifact /usr/bin/keyview \
      && ! path_is_exact_alias /usr/bin/keyview "${APP_DIR_TARGET}/Keyview"; then
    return 0
  fi
  return 1
}

path_is_package_managed() {
  local path="$1"

  have_pkg dpkg-query && dpkg-query -S "${path}" >/dev/null 2>&1 && return 0
  have_pkg rpm && rpm -qf -- "${path}" >/dev/null 2>&1 && return 0
  have_pkg pacman && pacman -Qo -- "${path}" >/dev/null 2>&1 && return 0
  return 1
}

SYSTEM_CHANNEL_PRESENT=false
PRESERVED_SHARED_INTEGRATION=false

remove_shared_integration_file() {
  local path="$1"

  [[ -e "${path}" || -L "${path}" ]] || return 0
  if [[ "${ROOT_INSTALL}" == "true" ]] \
      && { [[ "${SYSTEM_CHANNEL_PRESENT}" == "true" ]] || path_is_package_managed "${path}"; }; then
    echo "Keeping system/package-managed integration file ${path}."
    PRESERVED_SHARED_INTEGRATION=true
    return 0
  fi
  rm -f -- "${path}"
}

validate_app_target() {
  local parent resolved_parent
  [[ "$(basename -- "${APP_DIR_TARGET}")" == keysharp ]] || return 1
  parent="$(dirname -- "${APP_DIR_TARGET}")"
  [[ -d "${parent}" ]] || return 1
  resolved_parent="$(cd "${parent}" && pwd -P)"
  [[ -n "${resolved_parent}" && "${resolved_parent}" != / ]]
}

echo "Uninstalling Keysharp from ${APP_DIR_TARGET} (prefix=${PREFIX})"

if ! validate_app_target; then
  echo "Refusing unsafe application target: ${APP_DIR_TARGET}" >&2
  exit 1
fi

if [[ "${ROOT_INSTALL}" == "true" ]] && system_channel_is_present; then
  SYSTEM_CHANNEL_PRESENT=true
fi

maybe_run pkill -x '[Kk]eysharp' || true
maybe_run pkill -x '[Kk]eyview' || true

if [[ "${ROOT_INSTALL}" == "true" && -e /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules ]]; then
	udev_rule=/etc/udev/rules.d/70-keysharp-i2c-uaccess.rules
	if [[ ! -L "${udev_rule}" && -f "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" ]] \
			&& cmp -s "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" "${udev_rule}"; then
		rm -f -- "${udev_rule}"
	else
		echo "Keeping modified ${udev_rule}." >&2
	fi
	maybe_run udevadm control --reload-rules || true
	maybe_run udevadm trigger --subsystem-match=i2c-dev || true
fi

rm -f -- "${BINDIR}/keysharp" "${BINDIR}/keyview"
remove_shared_integration_file "${DESKTOP_DIR}/keyview.desktop"
remove_shared_integration_file "${DESKTOP_DIR}/keysharp.desktop"
remove_shared_integration_file "${MIME_DIR}/keysharp.xml"
remove_shared_integration_file "${ICON_DIR}/keysharp.png"
rm -rf -- "${APP_DIR_TARGET}"

MIMEAPPS="${DESKTOP_DIR}/mimeapps.list"
if [[ -f "${MIMEAPPS}" && "${SYSTEM_CHANNEL_PRESENT}" != "true" \
    && "${PRESERVED_SHARED_INTEGRATION}" != "true" ]]; then
  sed -i '/^application\/x-keysharp=keysharp\.desktop$/d' "${MIMEAPPS}" || true
  sed -i '/^application\/x-keysharp-compiled=keysharp\.desktop$/d' "${MIMEAPPS}" || true
  sed -i '/^application\/x-autohotkey=keysharp\.desktop$/d' "${MIMEAPPS}" || true
fi

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database "${MIME_ROOT}" || true
maybe_run gtk-update-icon-cache -f "${ICON_ROOT}" || true

echo "Keysharp uninstall complete. Standalone Linux components and their grants were kept."
if [[ "${ROOT_INSTALL}" == "true" \
    && ( "${SYSTEM_CHANNEL_PRESENT}" == "true" \
      || "${PRESERVED_SHARED_INTEGRATION}" == "true" ) ]]; then
  echo "A system/package Keysharp installation remains; its shared desktop, MIME, and icon files were kept."
  if package_keysharp_is_installed && have_pkg dpkg-query; then
    echo "If a launcher still points below ${PREFIX}, repair it with: sudo apt-get install --reinstall keysharp"
  else
    echo "If a launcher still points below ${PREFIX}, reinstall the system Keysharp package with its package manager."
  fi
fi
if [[ "${ROOT_INSTALL}" == "true" ]]; then
  cleanup_commands=()
  [[ -x /usr/local/share/doc/keysharp-input/uninstall.sh ]] \
    && cleanup_commands+=("sudo /usr/local/share/doc/keysharp-input/uninstall.sh")
  [[ -x /usr/local/share/doc/keysharp-desktop/uninstall.sh ]] \
    && cleanup_commands+=("sudo /usr/local/share/doc/keysharp-desktop/uninstall.sh")
  if (( ${#cleanup_commands[@]} > 0 )); then
    echo "If no other application uses a component, remove it separately:"
    printf '  %s\n' "${cleanup_commands[@]}"
  fi
fi
