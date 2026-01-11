#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

PREFIX="${PREFIX:-/usr/local}"
APP_DIR_TARGET="${PREFIX}/lib/keysharp"
BINDIR="${PREFIX}/bin"
DESKTOP_DIR="/usr/share/applications"
MIME_DIR="/usr/share/mime/packages"
ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
maybe_run() { command -v "$1" >/dev/null 2>&1 && "$@"; }

echo "Uninstalling from ${APP_DIR_TARGET} (prefix=${PREFIX})"

rm -f "${BINDIR}/keysharp" "${BINDIR}/keyview"
rm -f "${DESKTOP_DIR}/keyview.desktop" "${DESKTOP_DIR}/keysharp.desktop" "${MIME_DIR}/keysharp.xml" "${ICON_DIR}/keysharp.png"
rm -rf "${APP_DIR_TARGET}"

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database /usr/share/mime || true
maybe_run gtk-update-icon-cache -f /usr/share/icons/hicolor || true

echo "Uninstall complete."
