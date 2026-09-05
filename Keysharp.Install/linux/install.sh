#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_INSTALL=false
if [[ "${EUID}" -eq 0 ]]; then
  ROOT_INSTALL=true
  PATH=/usr/sbin:/usr/bin:/sbin:/bin:/usr/local/sbin:/usr/local/bin
  export PATH
fi

if [[ -z "${PREFIX:-}" ]]; then
  PREFIX_EXPLICIT=false
  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    PREFIX="/usr/local"
  else
    PREFIX="${HOME}/.local"
  fi
else
  PREFIX_EXPLICIT=true
fi

XDG_DATA_HOME="${XDG_DATA_HOME:-${HOME}/.local/share}"
APP_DIR_SOURCE="${SCRIPT_DIR}/app"
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
INSTALL_DEPS="${INSTALL_DEPS:-true}"
DOTNET_PACKAGE="${DOTNET_PACKAGE:-dotnet-runtime-10.0}"
DEPENDENCIES_ONLY=false
case "${1:-}" in
  "") ;;
  --install-dependencies) DEPENDENCIES_ONLY=true ;;
  -h|--help) echo "Usage: install.sh [--install-dependencies]"; exit 0 ;;
  *) echo "Usage: install.sh [--install-dependencies]" >&2; exit 2 ;;
esac
[[ $# -le 1 ]] || exit 2

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

check_system_channel_conflict() {
  [[ "${ROOT_INSTALL}" == "true" ]] || return 0

  local conflicts=()
  package_keysharp_is_installed && conflicts+=("the installed 'keysharp' package")

  if path_is_protected_system_artifact /usr/lib/keysharp \
      && ! path_is_exact_alias /usr/lib/keysharp "${APP_DIR_TARGET}"; then
    conflicts+=("/usr/lib/keysharp")
  fi
  if path_is_protected_system_artifact /usr/bin/keysharp \
      && ! path_is_exact_alias /usr/bin/keysharp "${APP_DIR_TARGET}/Keysharp"; then
    conflicts+=("/usr/bin/keysharp")
  fi
  if path_is_protected_system_artifact /usr/bin/keyview \
      && ! path_is_exact_alias /usr/bin/keyview "${APP_DIR_TARGET}/Keyview"; then
    conflicts+=("/usr/bin/keyview")
  fi

  if (( ${#conflicts[@]} == 0 )); then
    return 0
  fi

  echo "Refusing to layer the root tar installation over an existing system Keysharp installation:" >&2
  printf '  %s\n' "${conflicts[@]}" >&2
  echo "Remove the system package with its package manager, then rerun this installer." >&2
  echo "Running the extracted application in place or installing without sudo remains supported." >&2
  return 1
}

rewrite_desktop_exec() {
  local src="$1"
  local dest="$2"
  sed -e "s|/usr/local/bin/|${BINDIR}/|g" \
      -e "s|/usr/local/lib/keysharp/|${APP_DIR_TARGET}/|g" \
      "${src}" > "${dest}"
}

set_mime_default() {
  local mimeapps="${DESKTOP_DIR}/mimeapps.list"
  local mime="$1"
  local app="$2"

  if [[ ! -f "${mimeapps}" ]]; then
    printf '[Default Applications]\n%s=%s\n' "${mime}" "${app}" > "${mimeapps}"
  elif grep -q "^${mime}=" "${mimeapps}"; then
    sed -i "s|^${mime}=.*|${mime}=${app}|" "${mimeapps}"
  elif grep -q '^\[Default Applications\]' "${mimeapps}"; then
    sed -i "/^\[Default Applications\]/a ${mime}=${app}" "${mimeapps}"
  else
    printf '\n[Default Applications]\n%s=%s\n' "${mime}" "${app}" >> "${mimeapps}"
  fi
}

normalize_root_app_permissions() {
  find "${APP_DIR_TARGET}" -type d -exec chmod 0755 {} +
  find "${APP_DIR_TARGET}" -type f -exec chmod 0644 {} +
  for exe in Keysharp Keyview; do
    if [[ -f "${APP_DIR_TARGET}/${exe}" ]]; then
      chmod 0755 "${APP_DIR_TARGET}/${exe}"
    fi
  done
}

has_dotnet10() {
  command -v dotnet >/dev/null 2>&1 \
    && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'
}

install_deps() {
  # Keysharp's own runtime dependencies only. Each standalone component brings its
  # own, through its package or its installer.
  local packages_apt=(libx11-6 libxtst6 libxinerama1 libxt6 libx11-xcb1 libxkbcommon-x11-0 libxcb-xtest0 libgtk-3-0 libglib2.0-0 libnotify4 libatspi2.0-0 at-spi2-core pulseaudio-utils)
  local packages_dnf=(libX11 libXtst libXinerama libXt libxkbcommon-x11 libxcb libX11-xcb gtk3 glib2 libnotify at-spi2-core)
  local packages_yum=(libX11 libXtst libXinerama libXt libxcb xorg-x11-xkb-utils gtk3 glib2 libnotify at-spi2-core)
  local packages_zypper=(libX11-6 libXtst6 libXinerama1 libXt6 libxkbcommon-x11-0 libxcb1 gtk3 glib2 libnotify4 at-spi2-core)
  local packages_pacman=(libx11 libxtst libxinerama libxt libxkbcommon-x11 libxcb gtk3 glib2 libnotify at-spi2-core)

  if ! has_dotnet10; then
    packages_apt+=("${DOTNET_PACKAGE}")
    packages_dnf+=("${DOTNET_PACKAGE}")
    packages_yum+=("${DOTNET_PACKAGE}")
    packages_zypper+=("${DOTNET_PACKAGE}")
    packages_pacman+=(dotnet-runtime)
  fi

  if have_pkg apt-get; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y "${packages_apt[@]}"
  elif have_pkg dnf; then
    dnf install -y "${packages_dnf[@]}"
  elif have_pkg yum; then
    yum install -y "${packages_yum[@]}"
  elif have_pkg zypper; then
    zypper install -y "${packages_zypper[@]}"
  elif have_pkg pacman; then
    pacman -S --needed --noconfirm "${packages_pacman[@]}"
  else
    echo "Package manager not detected; ensure .NET 10, X11, GTK3, libnotify and AT-SPI2 are installed." >&2
  fi
}

check_dotnet() {
  if ! has_dotnet10; then
    echo ".NET 10 runtime missing. Install ${DOTNET_PACKAGE} or rebuild self-contained." >&2
    exit 1
  fi
}

check_parallel_install() {
  [[ "${PREFIX_EXPLICIT}" == "true" ]] && return 0
  if [[ "${ROOT_INSTALL}" == "true" && -n "${SUDO_USER:-}" && "${SUDO_USER}" != root ]]; then
    local user_home
    user_home="$(getent passwd "${SUDO_USER}" 2>/dev/null | cut -d: -f6 || true)"
    if [[ -n "${user_home}" && -d "${user_home}/.local/lib/keysharp" ]]; then
      echo "Warning: a per-user Keysharp install also exists at ${user_home}/.local/lib/keysharp." >&2
    fi
  elif [[ "${ROOT_INSTALL}" != "true" && -d /usr/local/lib/keysharp ]]; then
    echo "Warning: a system-wide Keysharp install also exists at /usr/local/lib/keysharp." >&2
  fi
}

validate_app_target() {
  local parent resolved_parent
  [[ "$(basename -- "${APP_DIR_TARGET}")" == keysharp ]] || return 1
  parent="$(dirname -- "${APP_DIR_TARGET}")"
  mkdir -p "${parent}"
  resolved_parent="$(cd "${parent}" && pwd -P)"
  [[ -n "${resolved_parent}" && "${resolved_parent}" != / ]]
}

if [[ ! -d "${APP_DIR_SOURCE}" ]]; then
  echo "Expected app payload at ${APP_DIR_SOURCE}; aborting." >&2
  exit 1
fi

# The root tar and distribution-package application channels share /usr/share
# integration files, so establish the channel before dependencies
# can be changed.
check_system_channel_conflict

if [[ "${ROOT_INSTALL}" == "true" ]]; then
  echo "Installing Keysharp system-wide."
else
  echo "Installing Keysharp for the current user."
fi

if [[ "${INSTALL_DEPS}" == "true" && "${ROOT_INSTALL}" == "true" ]]; then
  install_deps
elif [[ "${INSTALL_DEPS}" == "true" ]]; then
  echo "Skipping dependency installation in user mode."
else
  echo "Skipping dependency installation (INSTALL_DEPS=false)."
fi
check_dotnet
if [[ "${DEPENDENCIES_ONLY}" == "true" ]]; then
  echo "Keysharp runtime dependencies are ready."
  exit 0
fi

maybe_run pkill -x '[Kk]eysharp' || true
maybe_run pkill -x '[Kk]eyview' || true
check_parallel_install

if ! validate_app_target; then
  echo "Refusing unsafe application target: ${APP_DIR_TARGET}" >&2
  exit 1
fi

echo "Installing to ${APP_DIR_TARGET} (prefix=${PREFIX})"
rm -rf -- "${APP_DIR_TARGET}"
mkdir -p "${APP_DIR_TARGET}" "${BINDIR}"
cp -a "${APP_DIR_SOURCE}/." "${APP_DIR_TARGET}/"

if [[ "${ROOT_INSTALL}" == "true" ]]; then
	chown -R root:root "${APP_DIR_TARGET}"
	normalize_root_app_permissions

	# Monitor DDC/CI is implemented in Keysharp itself, not either standalone
	# broker. Keep its display-controller-scoped uaccess rule with Keysharp.
	if [[ -f "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" ]]; then
		udev_rule=/etc/udev/rules.d/70-keysharp-i2c-uaccess.rules
		if { [[ ! -e "${udev_rule}" && ! -L "${udev_rule}" ]] \
				|| { [[ -f "${udev_rule}" && ! -L "${udev_rule}" ]] \
					&& cmp -s "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" "${udev_rule}"; }; }; then
			install -Dm0644 "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" "${udev_rule}"
		else
			echo "Keeping modified ${udev_rule}; it overrides the bundled Keysharp rule." >&2
		fi
		maybe_run udevadm control --reload-rules || true
		maybe_run udevadm trigger --subsystem-match=i2c-dev || true
	fi
fi

ln -sf "${APP_DIR_TARGET}/Keysharp" "${BINDIR}/keysharp"
ln -sf "${APP_DIR_TARGET}/Keyview" "${BINDIR}/keyview"

install -d "${DESKTOP_DIR}"
rewrite_desktop_exec "${SCRIPT_DIR}/keyview.desktop" "${DESKTOP_DIR}/keyview.desktop"
rewrite_desktop_exec "${SCRIPT_DIR}/keysharp.desktop" "${DESKTOP_DIR}/keysharp.desktop"
install -Dm644 "${SCRIPT_DIR}/keysharp.xml" "${MIME_DIR}/keysharp.xml"
install -Dm644 "${SCRIPT_DIR}/Keysharp.png" "${ICON_DIR}/keysharp.png"

set_mime_default application/x-keysharp keysharp.desktop
set_mime_default application/x-keysharp-compiled keysharp.desktop
set_mime_default application/x-autohotkey keysharp.desktop

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database "${MIME_ROOT}" || true
maybe_run gtk-update-icon-cache -f "${ICON_ROOT}" || true

echo "Install complete. Run 'keysharp' with no arguments, or pick Keysharp from the applications menu."
