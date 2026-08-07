#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

ROOT_INSTALL=false
if [[ "${EUID}" -eq 0 ]]; then
  ROOT_INSTALL=true
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
GNOME_EXT_UUID="keysharp@keysharp.io"
CINNAMON_EXT_UUID="keysharp@keysharp.io"
SYSTEMD_DIR="/etc/systemd/system"
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
# Resolve the target desktop user for a root uninstall. Prefer SUDO_USER (set by
# `sudo ./uninstall.sh`). If it is unset — e.g. when run from a plain root shell —
# fall back to the active graphical login session via loginctl, then to the owner
# of any live /run/user/<uid>/bus socket. Echoes the username (empty if none).
resolve_target_user() {
  if [[ -n "${SUDO_USER:-}" && "${SUDO_USER}" != "root" ]]; then
    echo "${SUDO_USER}"
    return 0
  fi

  if command -v loginctl >/dev/null 2>&1; then
    local sid uid seat state type user
    while read -r sid uid user seat _; do
      [[ -z "${sid}" ]] && continue
      seat="$(loginctl show-session "${sid}" -p Seat --value 2>/dev/null || true)"
      state="$(loginctl show-session "${sid}" -p State --value 2>/dev/null || true)"
      type="$(loginctl show-session "${sid}" -p Type --value 2>/dev/null || true)"
      if [[ "${seat}" == "seat0" && "${state}" == "active" && ( "${type}" == "wayland" || "${type}" == "x11" ) ]]; then
        if [[ -n "${user}" && "${user}" != "root" && -S "/run/user/${uid}/bus" ]]; then
          echo "${user}"
          return 0
        fi
      fi
    done < <(loginctl list-sessions --no-legend 2>/dev/null || true)
  fi

  local bus uid count found=""
  count=0
  for bus in /run/user/*/bus; do
    [[ -S "${bus}" ]] || continue
    uid="${bus#/run/user/}"
    uid="${uid%/bus}"
    [[ "${uid}" == "0" ]] && continue
    found="$(getent passwd "${uid}" | cut -d: -f1 2>/dev/null || true)"
    count=$((count + 1))
  done
  if [[ "${count}" -eq 1 && -n "${found}" ]]; then
    echo "${found}"
    return 0
  fi

  echo ""
}

# KS_RUN is the command prefix (as an array) used to run gsettings as the target
# desktop user with their session bus, or empty for the current environment.
KS_RUN=()
ks_set_runner() {
  local as_user="${1}"
  KS_RUN=()
  if [[ -n "${as_user}" ]]; then
    local uid
    uid=$(id -u "${as_user}" 2>/dev/null) || return 1
    [[ -S "/run/user/${uid}/bus" ]] || return 1
    KS_RUN=(sudo -u "${as_user}" env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${uid}/bus")
  fi
  return 0
}

# gsettings wrapped in `timeout` so a wedged session bus can't hang.
ks_gsettings_get() {
  timeout 5 "${KS_RUN[@]}" gsettings get "${1}" enabled-extensions 2>/dev/null || true
}
ks_gsettings_set() {
  timeout 5 "${KS_RUN[@]}" gsettings set "${1}" enabled-extensions "${2}" 2>/dev/null
}

# Remove uuid from the schema's enabled-extensions. Absent uuid is a no-op.
# Note: uuid contains '.' and '@'; the '.' is a BRE "any char" in the sed patterns
# below, but the surrounding single quotes make a false match effectively
# impossible, so we do not escape it.
ks_ext_remove() {
  local uuid="${1}"
  local schema="${2}"
  local cur
  cur="$(ks_gsettings_get "${schema}")"
  [[ -z "${cur}" ]] && return 0
  printf '%s' "${cur}" | grep -q "'${uuid}'" || return 0
  local new
  new="$(printf '%s' "${cur}" \
    | sed "s/'${uuid}', //g" \
    | sed "s/, '${uuid}'//g" \
    | sed "s/'${uuid}'//g")"
  if printf '%s' "${new}" | grep -Eq '^\[\s*\]$'; then
    new="@as []"
  fi
  ks_gsettings_set "${schema}" "${new}"
}

echo "Uninstalling from ${APP_DIR_TARGET} (prefix=${PREFIX})"

# Stop ALL Keysharp/Keyview instances (the compile daemon AND any running scripts), not just the daemon:
# a lingering old instance keeps holding the input hook / its permissions. -x matches the process name
# exactly, so it won't touch this uninstaller or keysharp-inputd (different names). Run as root, pkill also
# reaches the desktop user's processes.
maybe_run pkill -x '[Kk]eysharp' || true
maybe_run pkill -x '[Kk]eyview' || true

if [[ "${ROOT_INSTALL}" == "true" ]]; then
  maybe_run systemctl disable --now keysharp-inputd.service || true
  maybe_run systemctl disable --now keysharp-inputd.socket || true

  # Remove the uaccess udev rule that grants the desktop user (via systemd-logind
  # uaccess) read access to the virtual input device the daemon creates. Prefer the
  # daemon's own removal path so it stays the single source of truth (and reloads
  # udev); otherwise delete the rule file and reload udev directly. Do this while
  # the binary still exists — the APP_DIR_TARGET removal below deletes it.
  removed_udev_rule=false
  if [[ -x "${APP_DIR_TARGET}/keysharp-inputd" ]]; then
    "${APP_DIR_TARGET}/keysharp-inputd" --remove-input-access || true
  else
    # Binary already gone (manual deletion, a corrupted install, or a prior
    # packaging run that skipped the native helpers) -- --remove-input-access
    # can't run, so manually remove BOTH files it would have removed: the
    # udev rule AND the uinput modules-load config. Leaving the latter behind
    # was a real gap (kept the kernel auto-loading uinput at every boot after
    # a complete uninstall) since only the rule was deleted here before.
    rm -f /etc/udev/rules.d/70-keysharp-inputd-uaccess.rules
    rm -f /etc/modules-load.d/uinput.conf
    removed_udev_rule=true
  fi
  # Legacy rule from installs predating the uaccess switch (harmless if absent).
  if [[ -e /etc/udev/rules.d/99-keysharp-inputd.rules ]]; then
    rm -f /etc/udev/rules.d/99-keysharp-inputd.rules
    removed_udev_rule=true
  fi
  # The DDC/CI i2c uaccess rule. No binary owns it (it is a plain data file
  # installed by install.sh), so it is always removed directly here.
  if [[ -e /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules ]]; then
    rm -f /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules
    removed_udev_rule=true
  fi
  if [[ "${removed_udev_rule}" == "true" ]]; then
    maybe_run udevadm control --reload-rules || true
  fi

  rm -f "${SYSTEMD_DIR}/keysharp-inputd.service" "${SYSTEMD_DIR}/keysharp-inputd.socket"
  # Also purge units left in /usr/local/lib/systemd/system by an old `cmake
  # --install` (default /usr/local prefix). That directory outranks the dpkg
  # target /usr/lib/systemd/system in systemd's search path, so a leftover copy
  # there would keep shadowing a later package install (and, if it predates the
  # [Install] section, make `systemctl enable` warn about a static unit). It is
  # never dpkg-owned, so removing it here is safe. We deliberately do not touch
  # /usr/lib/systemd/system: those units belong to the .deb and are removed by
  # dpkg's own prerm, not by this tarball uninstaller.
  rm -f /usr/local/lib/systemd/system/keysharp-inputd.service \
        /usr/local/lib/systemd/system/keysharp-inputd.socket \
        /usr/local/lib/systemd/system/sockets.target.wants/keysharp-inputd.socket \
        /usr/local/lib/systemd/system/multi-user.target.wants/keysharp-inputd.service || true
  maybe_run systemctl daemon-reload || true

  # Remove the root-owned input-permission trust store (the daemon's systemd
  # StateDirectory). systemd never auto-deletes a StateDirectory on stop/disable,
  # so without this a full uninstall leaves the "allow always" grants behind and a
  # later fresh reinstall silently inherits them. This runs only on uninstall, so
  # upgrades (which re-run install.sh, not this) still keep the user's decisions.
  rm -rf /var/lib/keysharp-trust
fi

rm -f "${BINDIR}/keysharp" "${BINDIR}/keyview" "${BINDIR}/keysharp-inputd"
rm -f "${DESKTOP_DIR}/keyview.desktop" "${DESKTOP_DIR}/keysharp.desktop" "${DESKTOP_DIR}/keysharp-helper.desktop" "${MIME_DIR}/keysharp.xml" "${ICON_DIR}/keysharp.png"
# Legacy (pre-rename) capture-helper artifacts from older installs.
rm -f "${DESKTOP_DIR}/keysharp-screencap.desktop" "${BINDIR}/keysharp-screencap"
rm -rf "${APP_DIR_TARGET}"

MIMEAPPS="${DESKTOP_DIR}/mimeapps.list"
if [[ -f "${MIMEAPPS}" ]]; then
  sed -i '/^application\/x-keysharp=keysharp\.desktop$/d' "${MIMEAPPS}" || true
  sed -i '/^application\/x-keysharp-compiled=keysharp\.desktop$/d' "${MIMEAPPS}" || true
  sed -i '/^application\/x-autohotkey=keysharp\.desktop$/d' "${MIMEAPPS}" || true
fi

# Remove the GNOME Shell extension. When uninstalling as root we resolve the
# desktop user so the extension is disabled for them, then remove both the
# per-user and system extension directories when possible.
remove_gnome_extension() {
  local target_user=""
  local target_home="${HOME}"

  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    target_user="$(resolve_target_user)"
    if [[ -n "${target_user}" ]]; then
      target_home="$(getent passwd "${target_user}" | cut -d: -f6 2>/dev/null || echo "${HOME}")"
    fi
  fi

  local local_ext_dir="${target_home}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID}"
  local global_ext_dir="/usr/share/gnome-shell/extensions/${GNOME_EXT_UUID}"
  local removed=false

  # Strip the UUID from org.gnome.shell enabled-extensions via gsettings, then also
  # call `gnome-extensions disable` (the canonical GNOME API) as the desktop user.
  if [[ -n "${target_user}" ]]; then
    if command -v gsettings >/dev/null 2>&1 && ks_set_runner "${target_user}"; then
      ks_ext_remove "${GNOME_EXT_UUID}" org.gnome.shell || true
    fi
    sudo -u "${target_user}" gnome-extensions disable "${GNOME_EXT_UUID}" 2>/dev/null || true
  else
    if command -v gsettings >/dev/null 2>&1 && ks_set_runner ""; then
      ks_ext_remove "${GNOME_EXT_UUID}" org.gnome.shell || true
    fi
    maybe_run gnome-extensions disable "${GNOME_EXT_UUID}" 2>/dev/null || true
  fi

  if [[ -e "${local_ext_dir}" ]]; then
    rm -rf "${local_ext_dir}"
    removed=true
  fi

  if [[ -e "${global_ext_dir}" ]]; then
    if rm -rf "${global_ext_dir}" 2>/dev/null; then
      removed=true
    else
      echo "Warning: could not remove global GNOME Shell extension at ${global_ext_dir}; rerun uninstall as root." >&2
    fi
  fi

  if [[ "${removed}" == "true" ]]; then
    echo "GNOME Shell extension removed."
  fi
}

remove_gnome_extension

remove_cinnamon_extension() {
  local target_user=""
  local target_home="${HOME}"

  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    target_user="$(resolve_target_user)"
    if [[ -n "${target_user}" ]]; then
      target_home="$(getent passwd "${target_user}" | cut -d: -f6 2>/dev/null || echo "${HOME}")"
    fi
  fi

  local local_ext_dir="${target_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}"
  local global_ext_dir="/usr/share/cinnamon/extensions/${CINNAMON_EXT_UUID}"
  local removed=false

  # Strip the UUID from org.cinnamon enabled-extensions via gsettings (run as the
  # desktop user when installed as root).
  if [[ -n "${target_user}" ]]; then
    if command -v gsettings >/dev/null 2>&1 && ks_set_runner "${target_user}"; then
      ks_ext_remove "${CINNAMON_EXT_UUID}" org.cinnamon || true
    fi
  else
    if command -v gsettings >/dev/null 2>&1 && ks_set_runner ""; then
      ks_ext_remove "${CINNAMON_EXT_UUID}" org.cinnamon || true
    fi
  fi

  if [[ -e "${local_ext_dir}" ]]; then
    rm -rf "${local_ext_dir}"
    removed=true
  fi

  if [[ -e "${global_ext_dir}" ]]; then
    if rm -rf "${global_ext_dir}" 2>/dev/null; then
      removed=true
    else
      echo "Warning: could not remove global Cinnamon extension at ${global_ext_dir}; rerun uninstall as root." >&2
    fi
  fi

  if [[ "${removed}" == "true" ]]; then
    echo "Cinnamon extension removed."
  fi
}

remove_cinnamon_extension

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database "${MIME_ROOT}" || true
maybe_run gtk-update-icon-cache -f "${ICON_ROOT}" || true

echo "Uninstall complete."
