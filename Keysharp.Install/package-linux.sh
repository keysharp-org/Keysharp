#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
CONFIG="${CONFIG:-Release}"
RID="${RID:-linux-x64}"
TFM="${TFM:-net10.0}"
DIST_DIR="${ROOT}/dist"
PKG_NAME="keysharp-${RID}"
PKG_DIR="${DIST_DIR}/${PKG_NAME}"
APP_DIR="${PKG_DIR}/app"

echo "Publishing Keysharp and Keyview (CONFIG=${CONFIG}, RID=${RID})..."
dotnet publish "${ROOT}/Keysharp/Keysharp.csproj" -c "${CONFIG}" -r "${RID}"
dotnet publish "${ROOT}/Keyview/Keyview.csproj" -c "${CONFIG}" -r "${RID}"

echo "Staging package at ${PKG_DIR}..."
rm -rf "${PKG_DIR}"
mkdir -p "${APP_DIR}"

rsync -a "${ROOT}/bin/${CONFIG}/${TFM}/${RID}/publish/" "${APP_DIR}/"

# Copy installer assets
cp "${ASSETS_DIR}/install.sh" "${ASSETS_DIR}/uninstall.sh" "${PKG_DIR}/"
cp "${ASSETS_DIR}/keyview.desktop" "${ASSETS_DIR}/keysharp.desktop" "${ASSETS_DIR}/keysharp.xml" "${PKG_DIR}/"
cp "${ROOT}/Keysharp.png" "${PKG_DIR}/"

mkdir -p "${DIST_DIR}"
tarball="${DIST_DIR}/${PKG_NAME}.tar.gz"
echo "Creating tarball ${tarball}..."
tar -czf "${tarball}" -C "${DIST_DIR}" "${PKG_NAME}"

echo "Done. Tarball ready at ${tarball}"
