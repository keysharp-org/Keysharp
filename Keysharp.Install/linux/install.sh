#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${PREFIX:-/usr/local}"
APP_DIR_SOURCE="${SCRIPT_DIR}/app"
APP_DIR_TARGET="${PREFIX}/lib/keysharp"
BINDIR="${PREFIX}/bin"
DESKTOP_DIR="/usr/share/applications"
MIME_DIR="/usr/share/mime/packages"
ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
INSTALL_DEPS="${INSTALL_DEPS:-true}"
maybe_run() { command -v "$1" >/dev/null 2>&1 && "$@"; }
have_pkg() { command -v "$1" >/dev/null 2>&1; }

install_deps() {
  # Eto.Forms Gtk backend requires GTK3; libnotify is used for notifications; AT-SPI2 supports accessibility hooks.
  local packages_apt=(libx11-6 libxtst6 libxinerama1 libxt6 libx11-xcb1 libxkbcommon-x11-0 libxcb-xtest0 libgtk-3-0 libnotify4 libatspi2.0-0 at-spi2-core)
  if have_pkg apt-get; then
    echo "Installing runtime deps via apt-get..."
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y "${packages_apt[@]}"
    return
  fi

  if have_pkg dnf; then
    echo "Installing runtime deps via dnf..."
    dnf install -y libX11 libXtst libXinerama libXt libxkbcommon-x11 libxcb libX11-xcb gtk3 libnotify at-spi2-core
    return
  fi

  if have_pkg yum; then
    echo "Installing runtime deps via yum..."
    yum install -y libX11 libXtst libXinerama libXt libxcb xorg-x11-xkb-utils gtk3 libnotify at-spi2-core
    return
  fi

  if have_pkg zypper; then
    echo "Installing runtime deps via zypper..."
    zypper install -y libX11-6 libXtst6 libXinerama1 libXt6 libxkbcommon-x11-0 libxcb1 gtk3 libnotify4 at-spi2-core
    return
  fi

  if have_pkg pacman; then
    echo "Installing runtime deps via pacman..."
    pacman -Sy --noconfirm libx11 libxtst libxinerama libxt libxkbcommon-x11 libxcb gtk3 libnotify at-spi2-core
    return
  fi

  echo "Package manager not detected; please ensure X11 libs, GTK3, libnotify, and optionally AT-SPI2 are installed." >&2
}

check_dotnet() {
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet runtime not found. Install .NET 10.0 runtime (framework-dependent publish) or rebuild self-contained." >&2
    return
  fi

  if ! dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'; then
    echo ".NET 10 runtime missing. Install it or rebuild self-contained." >&2
  fi
}

if [[ ! -d "${APP_DIR_SOURCE}" ]]; then
  echo "Expected app payload at ${APP_DIR_SOURCE}; aborting." >&2
  exit 1
fi

if [[ "${INSTALL_DEPS}" == "true" ]]; then
  install_deps
else
  echo "Skipping dependency install (INSTALL_DEPS=false). Ensure X11 libs are present."
fi

check_dotnet

echo "Installing to ${APP_DIR_TARGET} (prefix=${PREFIX})"
mkdir -p "${APP_DIR_TARGET}" "${BINDIR}"
cp -a "${APP_DIR_SOURCE}/." "${APP_DIR_TARGET}/"

ln -sf "${APP_DIR_TARGET}/Keysharp" "${BINDIR}/keysharp"
ln -sf "${APP_DIR_TARGET}/Keyview" "${BINDIR}/keyview"

install -Dm644 "${SCRIPT_DIR}/keyview.desktop" "${DESKTOP_DIR}/keyview.desktop"
install -Dm644 "${SCRIPT_DIR}/keysharp.desktop" "${DESKTOP_DIR}/keysharp.desktop"
install -Dm644 "${SCRIPT_DIR}/keysharp.xml" "${MIME_DIR}/keysharp.xml"
install -Dm644 "${SCRIPT_DIR}/Keysharp.png" "${ICON_DIR}/keysharp.png"

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database /usr/share/mime || true
maybe_run gtk-update-icon-cache -f /usr/share/icons/hicolor || true

echo "Install complete."
