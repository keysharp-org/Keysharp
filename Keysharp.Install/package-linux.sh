#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
CONFIG="${CONFIG:-Release}"

# Default to this machine's architecture; set RID explicitly to cross-target (e.g. RID=linux-arm64).
detect_default_rid() {
  case "$(uname -m)" in
    x86_64) echo "linux-x64" ;;
    aarch64|arm64) echo "linux-arm64" ;;
    armv7l|armv7|armhf) echo "linux-arm" ;;
    *)
      echo "Unable to infer Linux RID from architecture $(uname -m). Set RID=linux-x64, linux-arm64 or linux-arm." >&2
      return 1
      ;;
  esac
}

RID="${RID:-$(detect_default_rid)}"
DIST_DIR="${ROOT}/dist"
ETO_DIR="$(cd "${ROOT}/../Eto" 2>/dev/null && pwd || true)"
PATH_MAP="${ROOT}=/_/keysharp"
if [[ -n "${ETO_DIR}" ]]; then
  PATH_MAP="${PATH_MAP}%2c${ETO_DIR}=/_/Eto"
fi
PUBLISH_DIR="${DIST_DIR}/publish/${RID}"
STAGING_DIR="${DIST_DIR}/staging/${RID}"
PACKAGE_ROOT_DIR="${DIST_DIR}/package-root"
PKG_NAME="keysharp-${RID}"
PKG_DIR="${STAGING_DIR}/${PKG_NAME}"
APP_DIR="${PKG_DIR}/app"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${ROOT}/Keysharp/Keysharp.csproj" | head -n 1)}"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"
DEB_PKG_NAME="${DEB_PKG_NAME:-keysharp}"
DEB_TMP_DIR="${PACKAGE_ROOT_DIR}/${PKG_NAME}-deb"
DEB_OUT=""
DEB_ARCH=""
DEB_DEPENDS="dotnet-runtime-10.0, libx11-6, libxtst6, libxinerama1, libxt6, libx11-xcb1, libxkbcommon-x11-0, libxcb-xtest0, libgtk-3-0, libglib2.0-0, libnotify4, zenity, libatspi2.0-0, at-spi2-core, pulseaudio-utils, libudev1, libevdev2, systemd, kmod"
DEB_DESCRIPTION="A cross-platform C# port and enhancement of the AutoHotkey program"
INPUTD_SERVICE_TEMPLATE="${ROOT}/native/keysharp-inputd/systemd/keysharp-inputd.service.in"
INPUTD_SOCKET="${ROOT}/native/keysharp-inputd/systemd/keysharp-inputd.socket"
GNOME_EXT_UUID="keysharp@keysharp.io"
GNOME_EXT_SOURCE="${ASSETS_DIR}/gnome-shell-extension"
CINNAMON_EXT_UUID="keysharp@keysharp.io"
CINNAMON_EXT_SOURCE="${ASSETS_DIR}/cinnamon-extension"

if [[ -z "${VERSION}" ]]; then
  echo "Unable to determine package version from Keysharp.csproj. Set VERSION explicitly." >&2
  exit 1
fi

map_rid_to_deb_arch() {
  case "$1" in
    linux-x64) echo "amd64" ;;
    linux-arm64) echo "arm64" ;;
    linux-arm) echo "armhf" ;;
    *)
      echo "Unsupported RID for Debian packaging: $1" >&2
      return 1
      ;;
  esac
}

rewrite_desktop_exec() {
  local src="$1"
  local dest="$2"
  sed -e 's|/usr/local/bin/|/usr/bin/|g' \
      -e 's|/usr/local/lib/keysharp/|/usr/lib/keysharp/|g' \
      "${src}" > "${dest}"
}

build_native_helpers() {
  local inputd_build_dir="${STAGING_DIR}/native-keysharp-inputd-${RID}"
  local kwin_build_dir="${STAGING_DIR}/native-keysharp-helper-${RID}"

  if [[ "${RID}" != linux-* ]]; then
    return
  fi

  if ! command -v cmake >/dev/null 2>&1; then
    echo "Skipping native helper builds because cmake is not installed." >&2
    return
  fi

  if ! command -v pkg-config >/dev/null 2>&1; then
    echo "Skipping native helper builds because pkg-config is not installed." >&2
    return
  fi

  if pkg-config --exists libudev libevdev; then
    # Build only the daemon: disable the hooktest diagnostic tool and the CTest
    # executables (BUILD_TESTING defaults ON via the CMakeLists' include(CTest)).
    # None of these are packaged, so compiling them just wastes build time.
    cmake -S "${ROOT}/native/keysharp-inputd" -B "${inputd_build_dir}" \
      -DCMAKE_BUILD_TYPE="${CONFIG}" \
      -DKEYSHARP_INPUTD_BUILD_HOOKTEST=OFF \
      -DBUILD_TESTING=OFF
    cmake --build "${inputd_build_dir}" --clean-first
    cp "${inputd_build_dir}/keysharp-inputd" "${APP_DIR}/"
  else
    echo "Skipping keysharp-inputd build because libudev or libevdev development files are missing." >&2
  fi

  if ! pkg-config --exists gio-2.0 gio-unix-2.0; then
    echo "Skipping keysharp-helper build because gio-2.0 development files are missing." >&2
    return
  fi

  cmake -S "${ROOT}/native/keysharp-helper" -B "${kwin_build_dir}" -DCMAKE_BUILD_TYPE="${CONFIG}"
  cmake --build "${kwin_build_dir}" --clean-first
  cp "${kwin_build_dir}/keysharp-helper" "${APP_DIR}/"
}

relocate_library_scripts() {
  # The .cks (compiled) form stays in Scripts so the tray menu can launch it as
  # an inspector, while the .ks (source) form moves to Lib/ so #include <AtSpi>
  # resolves it as the standard library copy. The folder is capital "Lib" to match
  # the include resolver (it searches "<exeDir>/Lib"); the case matters on Linux's
  # case-sensitive filesystem.
  if [[ -f "${APP_DIR}/Scripts/AtSpi.ks" ]]; then
    mkdir -p "${APP_DIR}/Lib"
    mv "${APP_DIR}/Scripts/AtSpi.ks" "${APP_DIR}/Lib/AtSpi.ks"
  fi
}

verify_dash_present() {
  # A bare launch runs Keysharp.cks - the Dash - through the ordinary <exe-name> probe. Neither it nor
  # the Keysharp.ks fallback present means `keysharp` with no script errors instead of opening it.
  if [[ ! -f "${APP_DIR}/Keysharp.cks" && ! -f "${APP_DIR}/Keysharp.ks" ]]; then
    echo "Package payload has neither Keysharp.cks nor Keysharp.ks. Running keysharp with no script would error instead of opening the Dash." >&2
    exit 1
  fi

  if [[ ! -f "${APP_DIR}/Keysharp.cks" ]]; then
    echo "Warning: Keysharp.cks was not produced, so the Dash ships as source and is compiled in memory on every launch. Expected on a cross-RID publish; otherwise check the publish output for 'Could not precompile'." >&2
  fi
}

normalize_app_permissions() {
  find "${APP_DIR}" -type d -exec chmod 0755 {} +
  find "${APP_DIR}" -type f -exec chmod 0644 {} +

  for exe in Keysharp Keyview keysharp-inputd keysharp-helper; do
    if [[ -f "${APP_DIR}/${exe}" ]]; then
      chmod 0755 "${APP_DIR}/${exe}"
    fi
  done
}

verify_no_local_paths() {
  local scan_dir="$1"
  local found=0
  local patterns=("${ROOT}")

  if [[ -n "${HOME:-}" ]]; then
    patterns+=("${HOME}")
  fi

  if [[ -n "${ETO_DIR}" ]]; then
    patterns+=("${ETO_DIR}")
  fi

  echo "Checking packaged files for local absolute paths..."
  for pattern in "${patterns[@]}"; do
    if rg -a -F -n --max-count 20 "${pattern}" "${scan_dir}"; then
      found=1
    fi
  done

  if [[ "${found}" -ne 0 ]]; then
    echo "Package payload contains local absolute paths. Rebuild with path mapping before packaging." >&2
    exit 1
  fi
}

write_deb_control() {
  cat > "$1" <<EOF
Package: ${DEB_PKG_NAME}
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${DEB_ARCH}
Maintainer: Keysharp Team
Homepage: https://github.com/keysharp-org/Keysharp
Depends: ${DEB_DEPENDS}
Description: ${DEB_DESCRIPTION}
EOF
}

write_deb_copyright() {
  cat > "$1" <<'EOF'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: keysharp
Upstream-Contact: Keysharp Team
Source: https://github.com/keysharp-org/Keysharp

Files: *
Copyright: 2020-Present Matt Feemster <matt.feemster@gmail.com>
           2010-2015 A. <inspiration3@gmail.com>, Tobias Kappé <tobias@ntlabs.org>
           And other contributors.
License: BSD-2-Clause

Files: PCRE.NET.dll PCRE.NET.Native.so
Copyright: 2014-2022 Lucas Trzesniewski <lucas.trzesniewski@gmail.com>
           1997-2022 University of Cambridge
           2010-2022 Zoltan Herczeg <hzmester@freemail.hu>
           2009-2022 Zoltan Herczeg <hzmester@freemail.hu>
License: BSD-3-Clause

License: BSD-2-Clause
 Redistribution and use in source and binary forms, with or without
 modification, are permitted provided that the following conditions are
 met:
 .
  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.
 .
 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
 OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

License: BSD-3-Clause
 Redistribution and use in source and binary forms, with or without
 modification, are permitted provided that the following conditions are
 met:
 .
  * Redistributions of source code must retain the above copyright
    notices, this list of conditions and the following disclaimer.
  * Redistributions in binary form must reproduce the above copyright
    notices, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.
  * Neither the name of the University of Cambridge nor the names of
    any contributors may be used to endorse or promote products derived
    from this software without specific prior written permission.
 .
 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
 OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
EOF
}

write_deb_postinst() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e

# Resolve the target desktop user for enabling the shell extensions. `sudo dpkg -i`
# DOES propagate SUDO_USER, so prefer it. The gap is PackageKit / pkexec /
# unattended-upgrades, which run maintainer scripts as root WITHOUT SUDO_USER; in
# that case fall back to the active graphical login session (loginctl), then to the
# owner of the sole live /run/user/<uid>/bus socket. Prints "<uid> <user>" or "".
_resolve_target_user() {
  if [ -n "${SUDO_USER:-}" ] && [ "${SUDO_USER}" != "root" ]; then
    _u=$(id -u "${SUDO_USER}" 2>/dev/null || true)
    if [ -n "${_u}" ]; then
      printf '%s %s\n' "${_u}" "${SUDO_USER}"
      return 0
    fi
  fi

  if command -v loginctl >/dev/null 2>&1; then
    for _sid in $(loginctl list-sessions --no-legend 2>/dev/null | awk '{print $1}'); do
      [ -n "${_sid}" ] || continue
      _seat=$(loginctl show-session "${_sid}" -p Seat --value 2>/dev/null || true)
      _state=$(loginctl show-session "${_sid}" -p State --value 2>/dev/null || true)
      _type=$(loginctl show-session "${_sid}" -p Type --value 2>/dev/null || true)
      _name=$(loginctl show-session "${_sid}" -p Name --value 2>/dev/null || true)
      if [ "${_seat}" = "seat0" ] && [ "${_state}" = "active" ] && \
         { [ "${_type}" = "wayland" ] || [ "${_type}" = "x11" ]; } && \
         [ -n "${_name}" ] && [ "${_name}" != "root" ]; then
        _uid=$(id -u "${_name}" 2>/dev/null || true)
        if [ -n "${_uid}" ] && [ -S "/run/user/${_uid}/bus" ]; then
          printf '%s %s\n' "${_uid}" "${_name}"
          return 0
        fi
      fi
    done
  fi

  _count=0
  _fuid=""
  _fname=""
  for _bus in /run/user/*/bus; do
    [ -S "${_bus}" ] || continue
    _uid=${_bus#/run/user/}
    _uid=${_uid%/bus}
    [ "${_uid}" = "0" ] && continue
    _fuid="${_uid}"
    _fname=$(getent passwd "${_uid}" 2>/dev/null | cut -d: -f1 || true)
    _count=$((_count + 1))
  done
  if [ "${_count}" -eq 1 ] && [ -n "${_fname}" ]; then
    printf '%s %s\n' "${_fuid}" "${_fname}"
    return 0
  fi

  printf '\n'
}

# Run gsettings/gdbus as the resolved desktop user (globals _KS_UID/_KS_USER) with
# their session bus. All calls are wrapped in `timeout` so a wedged session bus
# cannot hang on the 25s D-Bus default; a timeout is treated as "no-op / not owned".
_ks_gsettings_get() {
  timeout 5 sudo -u "${_KS_USER}" \
    env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
    gsettings get "$1" enabled-extensions 2>/dev/null || true
}
_ks_gsettings_set() {
  timeout 5 sudo -u "${_KS_USER}" \
    env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
    gsettings set "$1" enabled-extensions "$2" 2>/dev/null
}

# Add uuid ($1) to schema ($2) enabled-extensions if not already present.
# Note: uuid contains '.' and '@'; the '.' is a BRE "any char" in the grep/sed
# patterns, but the surrounding single quotes make a false match effectively
# impossible, so we do not escape it. Returns 0 on success/already-present.
_ks_ext_add() {
  _uuid="$1"; _schema="$2"
  _cur=$(_ks_gsettings_get "${_schema}")
  [ -z "${_cur}" ] && return 1
  if printf '%s' "${_cur}" | grep -q "'${_uuid}'"; then
    return 0
  fi
  if printf '%s' "${_cur}" | grep -q "]$" && \
     ! printf '%s' "${_cur}" | grep -Eq '\[[[:space:]]*\]'; then
    _new=$(printf '%s' "${_cur}" | sed "s/]$/, '${_uuid}']/")
  else
    _new="['${_uuid}']"
  fi
  _ks_gsettings_set "${_schema}" "${_new}"
}

# Remove uuid ($1) from schema ($2) enabled-extensions. Absent uuid is a no-op.
_ks_ext_remove() {
  _uuid="$1"; _schema="$2"
  _cur=$(_ks_gsettings_get "${_schema}")
  [ -z "${_cur}" ] && return 0
  printf '%s' "${_cur}" | grep -q "'${_uuid}'" || return 0
  _new=$(printf '%s' "${_cur}" \
    | sed "s/'${_uuid}', //g" \
    | sed "s/, '${_uuid}'//g" \
    | sed "s/'${_uuid}'//g")
  if printf '%s' "${_new}" | grep -Eq '^\[[[:space:]]*\]$'; then
    _new="@as []"
  fi
  _ks_gsettings_set "${_schema}" "${_new}"
}

# Return 0 owned / 1 not owned / 2 unknown for a D-Bus name ($1). gdbus wrapped in
# `timeout`; missing gdbus, timeout, or error -> 2.
_ks_dbus_owned() {
  command -v gdbus >/dev/null 2>&1 || return 2
  _out=$(timeout 5 sudo -u "${_KS_USER}" \
    env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
    gdbus call --session --timeout 3 \
      --dest org.freedesktop.DBus \
      --object-path /org/freedesktop/DBus \
      --method org.freedesktop.DBus.NameHasOwner "$1" 2>/dev/null) || return 2
  case "${_out}" in
    *true*) return 0 ;;
    *false*) return 1 ;;
    *) return 2 ;;
  esac
}

if command -v update-mime-database >/dev/null 2>&1; then
  update-mime-database /usr/share/mime || true
fi

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f /usr/share/icons/hicolor || true
fi

# Set keysharp.desktop as the system-wide default handler for .ks, .cks and .ahk files.
# Writes to /usr/share/applications/mimeapps.list (freedesktop vendor defaults).
_set_mime_default() {
  local mimeapps="/usr/share/applications/mimeapps.list"
  local mime="$1"
  local app="$2"
  if [ ! -f "$mimeapps" ]; then
    printf '[Default Applications]\n%s=%s\n' "$mime" "$app" > "$mimeapps"
    return
  fi
  if grep -q "^${mime}=" "$mimeapps"; then
    sed -i "s|^${mime}=.*|${mime}=${app}|" "$mimeapps"
  elif grep -q '^\[Default Applications\]' "$mimeapps"; then
    sed -i "/^\[Default Applications\]/a ${mime}=${app}" "$mimeapps"
  else
    printf '\n[Default Applications]\n%s=%s\n' "$mime" "$app" >> "$mimeapps"
  fi
}
_set_mime_default application/x-keysharp  keysharp.desktop
_set_mime_default application/x-keysharp-compiled keysharp.desktop
_set_mime_default application/x-autohotkey keysharp.desktop

if [ -f /usr/lib/keysharp/keysharp-helper ]; then
  echo "Configuring keysharp-helper for Wayland screen capture (KWin ScreenShot2 serve mode; trust gate for GNOME)."
  chown root:root /usr/lib/keysharp/keysharp-helper || true
  chmod 4755 /usr/lib/keysharp/keysharp-helper || true
fi

if [ -f /usr/lib/keysharp/keysharp-inputd ]; then
  echo "Configuring keysharp-inputd for reliable Linux input hooks, input synthesis, and BlockInput."
  # Purge any keysharp-inputd unit that outranks this package's units in
  # /usr/lib/systemd/system. systemd's unit search path gives strict precedence to
  # both /etc/systemd/system (a prior root *tarball* install target) and
  # /usr/local/lib/systemd/system (an old `cmake --install` with the default
  # /usr/local prefix). A stale copy in either place shadows the dpkg unit: it keeps
  # socket-activating the old /usr/local/lib/keysharp/keysharp-inputd binary, and if
  # it predates the [Install] section that older release shipped, `systemctl enable`
  # resolves the stale *static* file and prints the "no installation config" warning.
  # Remove the shadowing units (and any leftover enable symlink) so the dpkg units
  # are the ones resolved; --install-input-access below runs daemon-reload + enable.
  rm -f /etc/systemd/system/keysharp-inputd.service \
        /etc/systemd/system/keysharp-inputd.socket \
        /etc/systemd/system/sockets.target.wants/keysharp-inputd.socket \
        /etc/systemd/system/multi-user.target.wants/keysharp-inputd.service \
        /usr/local/lib/systemd/system/keysharp-inputd.service \
        /usr/local/lib/systemd/system/keysharp-inputd.socket \
        /usr/local/lib/systemd/system/sockets.target.wants/keysharp-inputd.socket \
        /usr/local/lib/systemd/system/multi-user.target.wants/keysharp-inputd.service || true
  if ! /usr/lib/keysharp/keysharp-inputd --install-input-access; then
    cat >&2 <<'WARN'
Warning: keysharp-inputd --install-input-access did not complete successfully.
Linux input hooks, input synthesis, and BlockInput
prompt may be unavailable until this is resolved. Re-run manually as root:
  sudo /usr/lib/keysharp/keysharp-inputd --install-input-access
and check the output for the failing step (modprobe uinput, udevadm, or
systemctl enable --now keysharp-inputd.service).
WARN
    # --install-input-access normally reloads and starts the service. When it
    # fails, prerm has already stopped the old daemon on an upgrade, so retry the
    # same entry point; its Requires= dependency starts the socket.
    if command -v systemctl >/dev/null 2>&1; then
      systemctl daemon-reload || true
      systemctl start keysharp-inputd.service || true
    fi
  fi
fi

# DDC/CI (Monitor.Brightness, Monitor.GetVCP/SetVCP on external monitors) needs
# read/write on the connector's /dev/i2c-* node. The rule grants that to the
# local-seat user via logind's uaccess ACL, scoped to display-controller buses
# only -- see the rule file for why this is not the 'i2c' group. Installed into
# /etc rather than shipped in /usr/lib/udev/rules.d so both install channels
# (this package and the tarball's install.sh) own the exact same path: a
# deb-over-tarball install then updates that file instead of leaving a stale
# higher-precedence copy in /etc shadowing the packaged one.
if [ -f /usr/lib/keysharp/udev/70-keysharp-i2c-uaccess.rules ]; then
  install -Dm0644 /usr/lib/keysharp/udev/70-keysharp-i2c-uaccess.rules \
    /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules || true
  # Reload, then re-run the rules against the buses that already exist, so DDC
  # works in the current session without a replug or reboot. (uaccess ACLs are
  # applied by udev at add/change time; trigger's default action is "change".)
  if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules || true
    udevadm trigger --subsystem-match=i2c-dev || true
  fi
fi

# GNOME Shell extension: register in the desktop user's enabled-extensions. The
# extension files land system-wide at /usr/share/gnome-shell/extensions/ via dpkg;
# enabling is always per-user via gsettings, run as the desktop user resolved above.
# /run/user/<uid>/bus is the standard sd-bus socket (systemd/logind).
GNOME_EXT_UUID="keysharp@keysharp.io"
_gnome_ext_dir="/usr/share/gnome-shell/extensions/${GNOME_EXT_UUID}"
if [ "$1" = "configure" ] && [ -d "${_gnome_ext_dir}" ]; then
  echo "GNOME Shell extension installed at ${_gnome_ext_dir}"
  _gnome_enabled=false

  _tu=$(_resolve_target_user)
  _KS_UID=${_tu%% *}
  _KS_USER=${_tu#* }
  [ "${_tu}" = "${_KS_USER}" ] && _KS_USER=""

  # A tarball install places the extension per-user; GNOME Shell loads that copy in preference to
  # the dpkg one, which would silently pin a stale protocol (same skew failure once hit on Cinnamon).
  if [ -n "${_KS_USER}" ]; then
    _user_home=$(getent passwd "${_KS_USER}" 2>/dev/null | cut -d: -f6 || true)
    if [ -n "${_user_home}" ] && [ -d "${_user_home}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID}" ]; then
      echo "Removing older per-user GNOME extension at ${_user_home}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID} so the global install is used."
      rm -rf "${_user_home}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID}" || true
    fi
  fi

  if [ -n "${_KS_UID}" ] && [ -n "${_KS_USER}" ] && \
     [ -S "/run/user/${_KS_UID}/bus" ] && command -v gsettings >/dev/null 2>&1; then
    if _ks_ext_add "${GNOME_EXT_UUID}" org.gnome.shell; then
      _gnome_enabled=true
    fi
  fi

  if [ "${_gnome_enabled}" = "true" ]; then
    echo "GNOME Shell extension enabled for ${_KS_USER}. Log out and back in to activate it."
  else
    cat <<GNOMEMSG
Could not automatically enable the GNOME Shell extension (no active D-Bus
session was detected for the desktop user).  To enable it, run as your
desktop user:
  gnome-extensions enable ${GNOME_EXT_UUID}
or open the GNOME Extensions app and enable 'Keysharp Integration'.
Then log out and back in to activate it.
GNOMEMSG
  fi
fi

# Cinnamon extension: register in the desktop user's enabled-extensions. The
# extension files land system-wide via dpkg; enabling is per-user. If Cinnamon is
# the active session, force a live reload by toggling the UUID off (sleep 1) and
# back on, then poll the Keysharp Cinnamon D-Bus name to confirm it loaded.
CINNAMON_EXT_UUID="keysharp@keysharp.io"
_cinnamon_ext_dir="/usr/share/cinnamon/extensions/${CINNAMON_EXT_UUID}"
if [ "$1" = "configure" ] && [ -d "${_cinnamon_ext_dir}" ]; then
  echo "Cinnamon extension installed at ${_cinnamon_ext_dir}"
  _cinnamon_enabled=false

  _tu=$(_resolve_target_user)
  _KS_UID=${_tu%% *}
  _KS_USER=${_tu#* }
  [ "${_tu}" = "${_KS_USER}" ] && _KS_USER=""

  if [ -n "${_KS_USER}" ]; then
    _user_home=$(getent passwd "${_KS_USER}" 2>/dev/null | cut -d: -f6 || true)
    if [ -n "${_user_home}" ] && [ -d "${_user_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}" ]; then
      echo "Removing older per-user Cinnamon extension at ${_user_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID} so the global install is used."
      rm -rf "${_user_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}" || true
    fi
  fi

  if [ -n "${_KS_UID}" ] && [ -n "${_KS_USER}" ] && \
     [ -S "/run/user/${_KS_UID}/bus" ] && command -v gsettings >/dev/null 2>&1; then
    # Detect a live Cinnamon session (D-Bus name, else the user's desktop env).
    _active_cinnamon=false
    if _ks_dbus_owned org.Cinnamon; then
      _active_cinnamon=true
    else
      _desk=$(sudo -u "${_KS_USER}" \
        env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
        sh -c 'printf "%s %s %s" "${XDG_CURRENT_DESKTOP:-}" "${XDG_SESSION_DESKTOP:-}" "${DESKTOP_SESSION:-}"' 2>/dev/null || true)
      if printf '%s' "${_desk}" | grep -qi cinnamon; then
        _active_cinnamon=true
      fi
    fi

    # Toggle off/on to force a live reload when already enabled.
    if [ "${_active_cinnamon}" = "true" ]; then
      _cur=$(_ks_gsettings_get org.cinnamon)
      if printf '%s' "${_cur}" | grep -q "'${CINNAMON_EXT_UUID}'"; then
        _ks_ext_remove "${CINNAMON_EXT_UUID}" org.cinnamon || true
        sleep 1
      fi
    fi

    if _ks_ext_add "${CINNAMON_EXT_UUID}" org.cinnamon; then
      _cinnamon_enabled=true
    fi

    if [ "${_cinnamon_enabled}" = "true" ] && [ "${_active_cinnamon}" = "true" ]; then
      _i=0
      while [ "${_i}" -lt 10 ]; do
        _ks_dbus_owned io.github.keysharp.CinnamonShell && _rc=0 || _rc=$?
        # rc=0 owned (success); rc=2 gdbus missing/errored -> stop and assume ok.
        { [ "${_rc}" -eq 0 ] || [ "${_rc}" -eq 2 ]; } && break
        sleep 0.5
        _i=$((_i + 1))
      done
    fi
  fi

  if [ "${_cinnamon_enabled}" = "true" ]; then
    echo "Cinnamon extension enabled for ${_KS_USER}; running Cinnamon was asked to load/reload it when detected."
  else
    cat <<CINNAMONMSG
Could not automatically enable or load the Cinnamon extension. Either no active
Cinnamon D-Bus session was detected for the desktop user, or Cinnamon did not
expose the Keysharp D-Bus service after reload. To enable it manually, run as
your desktop user:
  Open Cinnamon's Extensions app and enable 'Keysharp Integration'.
Then restart Cinnamon or log out and back in to activate it.
CINNAMONMSG
  fi
fi
EOF
  chmod 0755 "$1"
}

write_deb_prerm() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e

# Resolve the target desktop user for disabling the shell extensions. `sudo dpkg -r`
# DOES propagate SUDO_USER, so prefer it. The gap is PackageKit / pkexec /
# unattended-upgrades, which run maintainer scripts as root WITHOUT SUDO_USER; in
# that case fall back to the active graphical login session (loginctl), then to the
# owner of the sole live /run/user/<uid>/bus socket. Prints "<uid> <user>" or "".
_resolve_target_user() {
  if [ -n "${SUDO_USER:-}" ] && [ "${SUDO_USER}" != "root" ]; then
    _u=$(id -u "${SUDO_USER}" 2>/dev/null || true)
    if [ -n "${_u}" ]; then
      printf '%s %s\n' "${_u}" "${SUDO_USER}"
      return 0
    fi
  fi

  if command -v loginctl >/dev/null 2>&1; then
    for _sid in $(loginctl list-sessions --no-legend 2>/dev/null | awk '{print $1}'); do
      [ -n "${_sid}" ] || continue
      _seat=$(loginctl show-session "${_sid}" -p Seat --value 2>/dev/null || true)
      _state=$(loginctl show-session "${_sid}" -p State --value 2>/dev/null || true)
      _type=$(loginctl show-session "${_sid}" -p Type --value 2>/dev/null || true)
      _name=$(loginctl show-session "${_sid}" -p Name --value 2>/dev/null || true)
      if [ "${_seat}" = "seat0" ] && [ "${_state}" = "active" ] && \
         { [ "${_type}" = "wayland" ] || [ "${_type}" = "x11" ]; } && \
         [ -n "${_name}" ] && [ "${_name}" != "root" ]; then
        _uid=$(id -u "${_name}" 2>/dev/null || true)
        if [ -n "${_uid}" ] && [ -S "/run/user/${_uid}/bus" ]; then
          printf '%s %s\n' "${_uid}" "${_name}"
          return 0
        fi
      fi
    done
  fi

  _count=0
  _fuid=""
  _fname=""
  for _bus in /run/user/*/bus; do
    [ -S "${_bus}" ] || continue
    _uid=${_bus#/run/user/}
    _uid=${_uid%/bus}
    [ "${_uid}" = "0" ] && continue
    _fuid="${_uid}"
    _fname=$(getent passwd "${_uid}" 2>/dev/null | cut -d: -f1 || true)
    _count=$((_count + 1))
  done
  if [ "${_count}" -eq 1 ] && [ -n "${_fname}" ]; then
    printf '%s %s\n' "${_fuid}" "${_fname}"
    return 0
  fi

  printf '\n'
}

# Run gsettings as the resolved desktop user (globals _KS_UID/_KS_USER) with their
# session bus, wrapped in `timeout` so a wedged bus can't hang; timeout -> no-op.
_ks_gsettings_get() {
  timeout 5 sudo -u "${_KS_USER}" \
    env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
    gsettings get "$1" enabled-extensions 2>/dev/null || true
}
_ks_gsettings_set() {
  timeout 5 sudo -u "${_KS_USER}" \
    env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${_KS_UID}/bus" \
    gsettings set "$1" enabled-extensions "$2" 2>/dev/null
}

# Remove uuid ($1) from schema ($2) enabled-extensions. Absent uuid is a no-op.
# Note: uuid contains '.' and '@'; the '.' is a BRE "any char" in the sed patterns,
# but the surrounding single quotes make a false match effectively impossible.
_ks_ext_remove() {
  _uuid="$1"; _schema="$2"
  _cur=$(_ks_gsettings_get "${_schema}")
  [ -z "${_cur}" ] && return 0
  printf '%s' "${_cur}" | grep -q "'${_uuid}'" || return 0
  _new=$(printf '%s' "${_cur}" \
    | sed "s/'${_uuid}', //g" \
    | sed "s/, '${_uuid}'//g" \
    | sed "s/'${_uuid}'//g")
  if printf '%s' "${_new}" | grep -Eq '^\[[[:space:]]*\]$'; then
    _new="@as []"
  fi
  _ks_gsettings_set "${_schema}" "${_new}"
}

# Runs on both removal ("remove"/"deconfigure") and upgrade ("upgrade"). In every
# case stop the per-user compile daemon ("Keysharp --daemon") and the running
# input broker so the old binaries are no longer in use; on an upgrade postinst
# re-enables the updated inputd service, which starts its required socket.
if command -v pkill >/dev/null 2>&1; then
  pkill -f '[Kk]eysharp --daemon' 2>/dev/null || true
fi

if command -v systemctl >/dev/null 2>&1; then
  systemctl stop keysharp-inputd.service || true
fi

if [ "$1" = "remove" ] || [ "$1" = "deconfigure" ]; then
  if command -v systemctl >/dev/null 2>&1; then
    systemctl disable --now keysharp-inputd.service || true
    systemctl disable --now keysharp-inputd.socket || true
  fi

  # Remove the uaccess udev rule (and any legacy rule) so removal does not orphan
  # device-access grants. The daemon binary still exists during prerm, so prefer its
  # own removal path (keeps it the single source of truth and reloads udev);
  # otherwise delete the rule file ourselves. dpkg removes the payload after prerm.
  _ks_removed_udev_rule=false
  if [ -x /usr/lib/keysharp/keysharp-inputd ]; then
    /usr/lib/keysharp/keysharp-inputd --remove-input-access || true
  else
    # Binary already gone -- --remove-input-access can't run, so manually
    # remove BOTH files it would have removed: the udev rule AND the uinput
    # modules-load config. Leaving the latter behind was a real gap (kept the
    # kernel auto-loading uinput at every boot after a complete uninstall)
    # since only the rule was deleted here before.
    rm -f /etc/udev/rules.d/70-keysharp-inputd-uaccess.rules || true
    rm -f /etc/modules-load.d/uinput.conf || true
    _ks_removed_udev_rule=true
  fi
  # Legacy rule from installs predating the uaccess switch (harmless if absent).
  if [ -e /etc/udev/rules.d/99-keysharp-inputd.rules ]; then
    rm -f /etc/udev/rules.d/99-keysharp-inputd.rules || true
    _ks_removed_udev_rule=true
  fi
  # The DDC/CI i2c uaccess rule. postinst installs it into /etc, which dpkg does
  # not own, so it has to be removed explicitly here (the payload copy under
  # /usr/lib/keysharp is dpkg's and goes away on its own after prerm).
  if [ -e /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules ]; then
    rm -f /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules || true
    _ks_removed_udev_rule=true
  fi
  if [ "${_ks_removed_udev_rule}" = "true" ] && command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules || true
  fi

  # Remove the root-owned input-permission trust store (the daemon's systemd
  # StateDirectory). systemd never auto-deletes a StateDirectory, so without this a
  # remove/purge leaves the "allow always" grants behind for a later reinstall to
  # inherit. This branch runs only on remove/deconfigure, not on upgrade ("$1" =
  # "upgrade"), so upgrades keep the user's decisions.
  rm -rf /var/lib/keysharp-trust || true

  # Remove system-wide default MIME associations added by postinst.
  _mimeapps="/usr/share/applications/mimeapps.list"
  if [ -f "$_mimeapps" ]; then
    sed -i '/^application\/x-keysharp=keysharp\.desktop$/d'  "$_mimeapps" || true
    sed -i '/^application\/x-keysharp-compiled=keysharp\.desktop$/d' "$_mimeapps" || true
    sed -i '/^application\/x-autohotkey=keysharp\.desktop$/d' "$_mimeapps" || true
  fi

  # Resolve the desktop user once for both extensions and strip each UUID from the
  # user's enabled-extensions with the pure-shell _ks_ext_remove helper (timeouts).
  _tu=$(_resolve_target_user)
  _KS_UID=${_tu%% *}
  _KS_USER=${_tu#* }
  [ "${_tu}" = "${_KS_USER}" ] && _KS_USER=""

  # GNOME Shell extension: remove the UUID from org.gnome.shell enabled-extensions.
  GNOME_EXT_UUID="keysharp@keysharp.io"
  if [ -n "${_KS_UID}" ] && [ -n "${_KS_USER}" ] && \
     [ -S "/run/user/${_KS_UID}/bus" ] && command -v gsettings >/dev/null 2>&1; then
    _ks_ext_remove "${GNOME_EXT_UUID}" org.gnome.shell || true
  fi

  # Cinnamon extension: remove any per-user shadow copy, then strip the UUID from
  # org.cinnamon enabled-extensions.
  CINNAMON_EXT_UUID="keysharp@keysharp.io"
  if [ -n "${_KS_USER}" ]; then
    _user_home=$(getent passwd "${_KS_USER}" 2>/dev/null | cut -d: -f6 || true)
    if [ -n "${_user_home}" ]; then
      rm -rf "${_user_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}" || true
    fi
  fi
  if [ -n "${_KS_UID}" ] && [ -n "${_KS_USER}" ] && \
     [ -S "/run/user/${_KS_UID}/bus" ] && command -v gsettings >/dev/null 2>&1; then
    _ks_ext_remove "${CINNAMON_EXT_UUID}" org.cinnamon || true
  fi
fi

exit 0
EOF
  chmod 0755 "$1"
}

write_deb_postrm() {
  cat > "$1" <<'EOF'
#!/bin/sh
set -e

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi

if command -v update-mime-database >/dev/null 2>&1; then
  update-mime-database /usr/share/mime || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f /usr/share/icons/hicolor || true
fi

if command -v systemctl >/dev/null 2>&1; then
  systemctl daemon-reload || true
fi
EOF
  chmod 0755 "$1"
}

build_tarball() {
  local tarball="${DIST_DIR}/${PKG_NAME}.tar.gz"
  echo "Creating tarball ${tarball}..."
  tar -czf "${tarball}" -C "${STAGING_DIR}" "${PKG_NAME}"
  echo "Tarball ready at ${tarball}"
}

build_deb() {
  local deb_root="${DEB_TMP_DIR}"
  local debian_dir="${deb_root}/DEBIAN"
  local lib_dir="${deb_root}/usr/lib/keysharp"
  local bin_dir="${deb_root}/usr/bin"
  local applications_dir="${deb_root}/usr/share/applications"
  local mime_dir="${deb_root}/usr/share/mime/packages"
  local icon_dir="${deb_root}/usr/share/icons/hicolor/256x256/apps"
  local systemd_dir="${deb_root}/usr/lib/systemd/system"
  local gnome_ext_dir="${deb_root}/usr/share/gnome-shell/extensions/${GNOME_EXT_UUID}"
  local cinnamon_ext_dir="${deb_root}/usr/share/cinnamon/extensions/${CINNAMON_EXT_UUID}"
  local doc_dir="${deb_root}/usr/share/doc/${DEB_PKG_NAME}"
  local build_cmd=()

  if ! command -v dpkg-deb >/dev/null 2>&1; then
    echo "Skipping Debian package creation because dpkg-deb is not installed."
    return
  fi

  if ! DEB_ARCH="$(map_rid_to_deb_arch "${RID}")"; then
    echo "Skipping Debian package creation for unsupported RID ${RID}."
    return
  fi

  DEB_OUT="${DIST_DIR}/${DEB_PKG_NAME}_${VERSION}_${DEB_ARCH}.deb"
  echo "Creating Debian package ${DEB_OUT}..."
  rm -rf "${deb_root}"
  mkdir -p "${debian_dir}" "${lib_dir}" "${bin_dir}" "${applications_dir}" "${mime_dir}" "${icon_dir}" "${systemd_dir}" "${gnome_ext_dir}" "${cinnamon_ext_dir}" "${doc_dir}"

  rsync -a "${APP_DIR}/" "${lib_dir}/"
  ln -s "../lib/keysharp/Keysharp" "${bin_dir}/keysharp"
  ln -s "../lib/keysharp/Keyview" "${bin_dir}/keyview"
  if [[ -f "${lib_dir}/keysharp-inputd" ]]; then
    ln -s "../lib/keysharp/keysharp-inputd" "${bin_dir}/keysharp-inputd"
  fi
  rewrite_desktop_exec "${ASSETS_DIR}/keysharp.desktop" "${applications_dir}/keysharp.desktop"
  rewrite_desktop_exec "${ASSETS_DIR}/keyview.desktop" "${applications_dir}/keyview.desktop"
  if [[ -f "${lib_dir}/keysharp-helper" ]]; then
    rewrite_desktop_exec "${ASSETS_DIR}/keysharp-helper.desktop" "${applications_dir}/keysharp-helper.desktop"
  fi
  install -Dm644 "${ASSETS_DIR}/keysharp.xml" "${mime_dir}/keysharp.xml"
  install -Dm644 "${ROOT}/assets/Keysharp.png" "${icon_dir}/keysharp.png"
  # Source copy of the DDC/CI uaccess rule; postinst installs it into
  # /etc/udev/rules.d (see the comment there for why it is not shipped straight
  # into /usr/lib/udev/rules.d).
  install -Dm644 "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" \
    "${lib_dir}/udev/70-keysharp-i2c-uaccess.rules"

  if [[ -d "${GNOME_EXT_SOURCE}" ]]; then
    cp -r "${GNOME_EXT_SOURCE}/." "${gnome_ext_dir}/"
  fi

  if [[ -d "${CINNAMON_EXT_SOURCE}" ]]; then
    cp -r "${CINNAMON_EXT_SOURCE}/." "${cinnamon_ext_dir}/"
  fi

  if [[ -f "${lib_dir}/keysharp-inputd" ]]; then
    sed -e 's|@CMAKE_INSTALL_FULL_BINDIR@|/usr/lib/keysharp|g' \
      "${INPUTD_SERVICE_TEMPLATE}" > "${systemd_dir}/keysharp-inputd.service"
    install -m 0644 "${INPUTD_SOCKET}" "${systemd_dir}/keysharp-inputd.socket"
  fi

  if [[ -f "${lib_dir}/keysharp-helper" ]]; then
    chown 0:0 "${lib_dir}/keysharp-helper" 2>/dev/null || true
    chmod 4755 "${lib_dir}/keysharp-helper"
  fi

  write_deb_control "${debian_dir}/control"
  write_deb_postinst "${debian_dir}/postinst"
  write_deb_prerm "${debian_dir}/prerm"
  write_deb_postrm "${debian_dir}/postrm"
  write_deb_copyright "${doc_dir}/copyright"

  find "${deb_root}" -type d -exec chmod 0755 {} +
  find "${deb_root}" -type f -exec chmod 0644 {} +

  for exe in Keysharp Keyview keysharp-inputd; do
    if [[ -f "${lib_dir}/${exe}" ]]; then
      chmod 0755 "${lib_dir}/${exe}"
    fi
  done

  if [[ -f "${lib_dir}/keysharp-helper" ]]; then
    chmod 4755 "${lib_dir}/keysharp-helper"
  fi

  chmod 0755 "${debian_dir}/postinst" "${debian_dir}/prerm" "${debian_dir}/postrm"

  if dpkg-deb --help 2>/dev/null | grep -q -- '--root-owner-group'; then
    build_cmd=(dpkg-deb --build --root-owner-group "${deb_root}" "${DEB_OUT}")
  elif command -v fakeroot >/dev/null 2>&1; then
    build_cmd=(fakeroot dpkg-deb --build "${deb_root}" "${DEB_OUT}")
  else
    build_cmd=(dpkg-deb --build "${deb_root}" "${DEB_OUT}")
  fi

  "${build_cmd[@]}"
  echo "Debian package ready at ${DEB_OUT}"
}

echo "Publishing Keysharp and Keyview (CONFIG=${CONFIG}, RID=${RID})..."
mkdir -p "${DIST_DIR}"
rm -rf "${PUBLISH_DIR}/Keysharp" "${PUBLISH_DIR}/Keyview"
# Publish only the two shipping projects rather than the whole solution. Keysharp and
# Keyview pull in everything that actually ships via <ProjectReference>
# (Keysharp -> Keysharp.Core -> Eto; Keyview -> Keysharp/Keysharp.Core/Eto), so this
# builds all required code without compiling Keysharp.Tests, Keysharp.OutputTest, or
# Keysharp.Benchmark — none of which are packaged.
#
# Eto is referenced via <ProjectReference> but is not a member of Keysharp.sln. A
# solution build unsets the parent Configuration/Platform when building project
# references, and an out-of-solution reference has no solution mapping, so Eto would
# fall back to its default (Debug). A Debug Eto keeps its SourceLink "documents" key
# on the raw local checkout path (csc /pathmap does not rewrite the SourceLink blob),
# which trips verify_no_local_paths. Building the projects directly (plus keeping
# ShouldUnsetParentConfigurationAndPlatform=false) lets references inherit the parent
# config and build Eto in Release, where DeterministicSourcePaths scrubs the path to /_/.
for proj in Keysharp Keyview; do
  dotnet publish "${ROOT}/${proj}/${proj}.csproj" -c "${CONFIG}" -r "${RID}" \
    -p:KeysharpVersion="${VERSION}" \
    -p:Deterministic=true \
    -p:ContinuousIntegrationBuild=true \
    -p:ShouldUnsetParentConfigurationAndPlatform=false \
    -p:PathMap="${PATH_MAP}"
done

# The Dash, its template, the demos and every .cks: install payload, so Keysharp.csproj does not carry
# it. Runs against the just-published host, which is what makes each .cks match this build.
echo "Staging install payload..."
dotnet msbuild "${ROOT}/Keysharp.Install/payload/Keysharp.Payload.proj" \
  -p:PayloadDir="${PUBLISH_DIR}/Keysharp" --nologo -v:minimal

echo "Staging package at ${PKG_DIR}..."
rm -rf "${PKG_DIR}"
mkdir -p "${APP_DIR}"

rsync -a "${PUBLISH_DIR}/Keyview/" "${APP_DIR}/"
rsync -a "${PUBLISH_DIR}/Keysharp/" "${APP_DIR}/"
# Keysharp, Keysharp.Core and Keyview emit no PDBs in Release (see Directory.Build.props), but Eto is a
# project reference on this platform and is not covered by that, so its symbols would still ship. macOS
# strips PDBs the same way in clean_app_bundle; do it here too so all three platforms agree.
find "${APP_DIR}" -name '*.pdb' -delete
relocate_library_scripts
verify_dash_present
build_native_helpers
normalize_app_permissions
verify_no_local_paths "${APP_DIR}"

# Copy installer assets
cp "${ASSETS_DIR}/install.sh" "${ASSETS_DIR}/uninstall.sh" "${PKG_DIR}/"
cp "${ASSETS_DIR}/keyview.desktop" "${ASSETS_DIR}/keysharp.desktop" "${ASSETS_DIR}/keysharp-helper.desktop" "${ASSETS_DIR}/keysharp.xml" "${PKG_DIR}/"
cp "${ASSETS_DIR}/70-keysharp-i2c-uaccess.rules" "${PKG_DIR}/"
cp "${ROOT}/assets/Keysharp.png" "${PKG_DIR}/"
cp "${INPUTD_SERVICE_TEMPLATE}" "${PKG_DIR}/keysharp-inputd.service.in"
cp "${INPUTD_SOCKET}" "${PKG_DIR}/keysharp-inputd.socket"
cp -r "${ASSETS_DIR}/gnome-shell-extension" "${PKG_DIR}/gnome-shell-extension"
cp -r "${ASSETS_DIR}/cinnamon-extension" "${PKG_DIR}/cinnamon-extension"
chmod 0755 "${PKG_DIR}/install.sh" "${PKG_DIR}/uninstall.sh"
chmod 0644 "${PKG_DIR}/keyview.desktop" \
  "${PKG_DIR}/keysharp.desktop" \
  "${PKG_DIR}/keysharp-helper.desktop" \
  "${PKG_DIR}/keysharp.xml" \
  "${PKG_DIR}/Keysharp.png" \
  "${PKG_DIR}/keysharp-inputd.service.in" \
  "${PKG_DIR}/keysharp-inputd.socket" \
  "${PKG_DIR}/70-keysharp-i2c-uaccess.rules"
find "${PKG_DIR}/gnome-shell-extension" -type d -exec chmod 0755 {} +
find "${PKG_DIR}/gnome-shell-extension" -type f -exec chmod 0644 {} +
find "${PKG_DIR}/cinnamon-extension" -type d -exec chmod 0755 {} +
find "${PKG_DIR}/cinnamon-extension" -type f -exec chmod 0644 {} +

build_tarball
build_deb

echo "Done."
