#!/usr/bin/env bash
if [ -z "${BASH_VERSION:-}" ]; then exec /usr/bin/env bash "$0" "$@"; fi
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_INSTALL=false
if [[ "${EUID}" -eq 0 ]]; then
  ROOT_INSTALL=true
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
INSTALL_DEPS="${INSTALL_DEPS:-true}"
DOTNET_PACKAGE="${DOTNET_PACKAGE:-dotnet-runtime-10.0}"
GNOME_EXT_UUID="keysharp@keysharp.io"
GNOME_EXT_SOURCE="${SCRIPT_DIR}/gnome-shell-extension"
CINNAMON_EXT_UUID="keysharp@keysharp.io"
CINNAMON_EXT_SOURCE="${SCRIPT_DIR}/cinnamon-extension"
maybe_run() { command -v "$1" >/dev/null 2>&1 && "$@"; }
have_pkg() { command -v "$1" >/dev/null 2>&1; }
# Resolve the target desktop user for a root install. Prefer SUDO_USER (set by
# `sudo ./install.sh`). If it is unset — e.g. when run from a plain root shell —
# fall back to the active graphical login session via loginctl, then to the owner
# of any live /run/user/<uid>/bus socket. Echoes the username (empty if none).
resolve_target_user() {
  if [[ -n "${SUDO_USER:-}" && "${SUDO_USER}" != "root" ]]; then
    echo "${SUDO_USER}"
    return 0
  fi

  if command -v loginctl >/dev/null 2>&1; then
    # Pick a seat0 session that is active and graphical (or at least active).
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

  # Last resort: exactly one live user bus socket → treat its owner as the target.
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
    return
  fi

  if grep -q "^${mime}=" "${mimeapps}"; then
    sed -i "s|^${mime}=.*|${mime}=${app}|" "${mimeapps}"
  elif grep -q '^\[Default Applications\]' "${mimeapps}"; then
    sed -i "/^\[Default Applications\]/a ${mime}=${app}" "${mimeapps}"
  else
    printf '\n[Default Applications]\n%s=%s\n' "${mime}" "${app}" >> "${mimeapps}"
  fi
}
rewrite_systemd_service() {
  local src="$1"
  local dest="$2"
  sed -e "s|@CMAKE_INSTALL_FULL_BINDIR@|${APP_DIR_TARGET}|g" \
      "${src}" > "${dest}"
}
normalize_root_app_permissions() {
  find "${APP_DIR_TARGET}" -type d -exec chmod 0755 {} +
  find "${APP_DIR_TARGET}" -type f -exec chmod 0644 {} +

  for exe in Keysharp Keyview keysharp-inputd keysharp-helper; do
    if [[ -f "${APP_DIR_TARGET}/${exe}" ]]; then
      chmod 0755 "${APP_DIR_TARGET}/${exe}"
    fi
  done
}
# Install the GNOME Shell extension. This is always a per-user operation
# because GNOME extensions live in ~/.local/share/gnome-shell/extensions/.
# When running as root via sudo, we target the invoking user ($SUDO_USER).
install_gnome_extension() {
  if [[ ! -d "${GNOME_EXT_SOURCE}" ]]; then
    echo "Warning: GNOME Shell extension source not found at ${GNOME_EXT_SOURCE}; skipping." >&2
    return 0
  fi

  local target_user=""
  local target_home="${HOME}"

  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    target_user="$(resolve_target_user)"
    if [[ -n "${target_user}" ]]; then
      target_home="$(getent passwd "${target_user}" | cut -d: -f6 2>/dev/null || echo "")"
      if [[ -z "${target_home}" ]]; then
        echo "Warning: could not determine home directory for ${target_user}; skipping GNOME extension install." >&2
        return 0
      fi
    else
      # Running as root and no desktop user could be resolved (no SUDO_USER and
      # no active graphical session). The GNOME extension must be installed as
      # the desktop user, so print instructions instead.
      cat >&2 <<EOF
Warning: running as root and could not determine the desktop user.
To install the GNOME Shell extension for your desktop user, run as that user:
  cp -r "${GNOME_EXT_SOURCE}/." "\${HOME}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID}/"
  gnome-extensions enable "${GNOME_EXT_UUID}"
  (then log out and back in)
EOF
      return 0
    fi
  fi

  local ext_dir="${target_home}/.local/share/gnome-shell/extensions/${GNOME_EXT_UUID}"
  echo "Installing GNOME Shell extension to ${ext_dir}..."

  local ext_registered=false
  if [[ -n "${target_user}" ]]; then
    sudo -u "${target_user}" mkdir -p "${ext_dir}"
    sudo -u "${target_user}" cp -r "${GNOME_EXT_SOURCE}/." "${ext_dir}/"
    if gnome_preregister_extension "${target_user}"; then
      ext_registered=true
    fi
  else
    mkdir -p "${ext_dir}"
    cp -r "${GNOME_EXT_SOURCE}/." "${ext_dir}/"
    if gnome_preregister_extension ""; then
      ext_registered=true
    fi
  fi

  echo "GNOME Shell extension installed (uuid: ${GNOME_EXT_UUID})."
  if [[ "${ext_registered}" == "true" ]]; then
    echo "Log out and back in to activate it (Wayland requires a session restart for new extensions)."
  else
    cat >&2 <<EOF
Could not automatically enable the GNOME Shell extension (no active D-Bus
session was detected for the desktop user). To enable it, run as your
desktop user:
  gnome-extensions enable "${GNOME_EXT_UUID}"
or open the GNOME Extensions app and enable 'Keysharp Integration'.
Then log out and back in.
EOF
  fi
}

# KS_RUN is the command prefix (as an array) used to run gsettings/gdbus as the
# target desktop user with their session bus, or empty to run in the current
# environment. ks_set_runner populates it; the ks_ext_* helpers consume it.
KS_RUN=()
ks_set_runner() {
  local as_user="${1}"
  KS_RUN=()
  if [[ -n "${as_user}" ]]; then
    local uid
    uid=$(id -u "${as_user}" 2>/dev/null) || return 1
    # /run/user/<uid>/bus is the standard sd-bus socket (systemd/logind).
    # If it doesn't exist the user has no live D-Bus session and gsettings
    # won't work — return 1 so the caller shows manual instructions.
    [[ -S "/run/user/${uid}/bus" ]] || return 1
    KS_RUN=(sudo -u "${as_user}" env "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/${uid}/bus")
  fi
  return 0
}

# Read the current enabled-extensions list for a schema. Echoes the raw GVariant
# (e.g. "['a@b', 'c@d']" or "@as []"), empty on timeout/error. gsettings is wrapped
# in `timeout` so a wedged session bus can't hang on the 25s D-Bus default.
ks_gsettings_get() {
  local schema="${1}"
  timeout 5 "${KS_RUN[@]}" gsettings get "${schema}" enabled-extensions 2>/dev/null || true
}

ks_gsettings_set() {
  local schema="${1}"
  local value="${2}"
  timeout 5 "${KS_RUN[@]}" gsettings set "${schema}" enabled-extensions "${value}" 2>/dev/null
}

# Add uuid to the schema's enabled-extensions if not already present.
# Note: uuid contains '.' and '@'; in the grep/sed patterns below the '.' is a BRE
# "any char", but the surrounding single quotes make a false match effectively
# impossible, so we do not escape it.
ks_ext_add() {
  local uuid="${1}"
  local schema="${2}"
  local cur
  cur="$(ks_gsettings_get "${schema}")"
  [[ -z "${cur}" ]] && return 1
  if printf '%s' "${cur}" | grep -q "'${uuid}'"; then
    return 0
  fi
  local new
  if printf '%s' "${cur}" | grep -q "]$" && ! printf '%s' "${cur}" | grep -Eq '\[\s*\]|@as \[\s*\]'; then
    # Non-empty list: insert before the trailing ']'.
    new="$(printf '%s' "${cur}" | sed "s/]$/, '${uuid}']/")"
  else
    new="['${uuid}']"
  fi
  ks_gsettings_set "${schema}" "${new}"
}

# Remove uuid from the schema's enabled-extensions. Absent uuid is a no-op.
ks_ext_remove() {
  local uuid="${1}"
  local schema="${2}"
  local cur
  cur="$(ks_gsettings_get "${schema}")"
  [[ -z "${cur}" ]] && return 0
  printf '%s' "${cur}" | grep -q "'${uuid}'" || return 0
  local new
  # Remove the entry with either its trailing or leading separator, then a lone
  # entry; collapse an emptied list to the canonical "@as []".
  new="$(printf '%s' "${cur}" \
    | sed "s/'${uuid}', //g" \
    | sed "s/, '${uuid}'//g" \
    | sed "s/'${uuid}'//g")"
  if printf '%s' "${new}" | grep -Eq '^\[\s*\]$'; then
    new="@as []"
  fi
  ks_gsettings_set "${schema}" "${new}"
}

# Return 0/1/2 for a D-Bus name's ownership: 0 owned, 1 not owned, 2 unknown
# (gdbus missing/timeout/error). gdbus is wrapped in `timeout`.
ks_dbus_name_owned() {
  local name="${1}"
  command -v gdbus >/dev/null 2>&1 || return 2
  local out
  out="$(timeout 5 "${KS_RUN[@]}" gdbus call --session --timeout 3 \
    --dest org.freedesktop.DBus \
    --object-path /org/freedesktop/DBus \
    --method org.freedesktop.DBus.NameHasOwner "${name}" 2>/dev/null)" || return 2
  case "${out}" in
    *true*) return 0 ;;
    *false*) return 1 ;;
    *) return 2 ;;
  esac
}

# Add the extension UUID to org.gnome.shell enabled-extensions. Returns 0 if the
# UUID was successfully registered (or was already present), 1 if enabling could
# not be attempted (gsettings missing, or no active D-Bus session for the target
# user).
gnome_preregister_extension() {
  local as_user="${1}"
  command -v gsettings >/dev/null 2>&1 || return 1
  ks_set_runner "${as_user}" || return 1
  ks_ext_add "${GNOME_EXT_UUID}" org.gnome.shell
}

# Install the Cinnamon extension. Like the GNOME extension, this is a per-user
# operation: root installs target the resolved desktop user's
# ~/.local/share/cinnamon/extensions via `sudo -u`, mirroring the GNOME path, so
# there is no root-owned system copy to shadow. When no desktop user can be
# resolved, print manual instructions (do not mutate root's dconf).
install_cinnamon_extension() {
  if [[ ! -d "${CINNAMON_EXT_SOURCE}" ]]; then
    echo "Warning: Cinnamon extension source not found at ${CINNAMON_EXT_SOURCE}; skipping." >&2
    return 0
  fi

  local target_user=""
  local target_home="${HOME}"

  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    target_user="$(resolve_target_user)"
    if [[ -n "${target_user}" ]]; then
      target_home="$(getent passwd "${target_user}" | cut -d: -f6 2>/dev/null || echo "")"
      if [[ -z "${target_home}" ]]; then
        echo "Warning: could not determine home directory for ${target_user}; skipping Cinnamon extension install." >&2
        return 0
      fi
    else
      # Running as root and no desktop user could be resolved. The Cinnamon
      # extension is per-user, so print instructions rather than mutating root's
      # dconf.
      cat >&2 <<EOF
Warning: running as root and could not determine the desktop user.
To install the Cinnamon extension for your desktop user, run as that user:
  cp -r "${CINNAMON_EXT_SOURCE}/." "\${HOME}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}/"
  Open Cinnamon's Extensions app and enable 'Keysharp Integration'.
  (then restart Cinnamon or log out and back in)
EOF
      return 0
    fi
  fi

  local ext_dir="${target_home}/.local/share/cinnamon/extensions/${CINNAMON_EXT_UUID}"
  echo "Installing Cinnamon extension to ${ext_dir}..."

  local ext_registered=false
  if [[ -n "${target_user}" ]]; then
    sudo -u "${target_user}" rm -rf "${ext_dir}"
    sudo -u "${target_user}" mkdir -p "${ext_dir}"
    sudo -u "${target_user}" cp -r "${CINNAMON_EXT_SOURCE}/." "${ext_dir}/"
    if cinnamon_preregister_extension "${target_user}"; then
      ext_registered=true
    fi
  else
    rm -rf "${ext_dir}"
    mkdir -p "${ext_dir}"
    cp -r "${CINNAMON_EXT_SOURCE}/." "${ext_dir}/"
    if cinnamon_preregister_extension ""; then
      ext_registered=true
    fi
  fi

  echo "Cinnamon extension installed (uuid: ${CINNAMON_EXT_UUID})."
  if [[ "${ext_registered}" == "true" ]]; then
    echo "Cinnamon was asked to load/reload it if a live Cinnamon session was available."
  else
    cat >&2 <<EOF
Could not automatically enable or load the Cinnamon extension. Either no active
Cinnamon D-Bus session was detected for the desktop user, or Cinnamon did not
expose the Keysharp D-Bus service after reload. To enable it manually, run as
your desktop user:
  Open Cinnamon's Extensions app and enable 'Keysharp Integration'.
Then restart Cinnamon or log out and back in.
EOF
  fi
}

# Add the extension UUID to org.cinnamon enabled-extensions. If Cinnamon is the
# active session, force a live reload by toggling the UUID off (sleep 1) and back
# on, then poll the Keysharp Cinnamon D-Bus name to confirm the extension loaded.
cinnamon_preregister_extension() {
  local as_user="${1}"
  command -v gsettings >/dev/null 2>&1 || return 1
  ks_set_runner "${as_user}" || return 1

  # Decide whether Cinnamon is live: prefer the org.Cinnamon D-Bus name, then the
  # desktop env vars (in the target user's environment when running via sudo).
  local active_cinnamon=false
  if ks_dbus_name_owned org.Cinnamon; then
    active_cinnamon=true
  else
    local desk=""
    if [[ -n "${as_user}" ]]; then
      desk="$("${KS_RUN[@]}" sh -c 'printf "%s %s %s" "${XDG_CURRENT_DESKTOP:-}" "${XDG_SESSION_DESKTOP:-}" "${DESKTOP_SESSION:-}"' 2>/dev/null || true)"
    else
      desk="${XDG_CURRENT_DESKTOP:-} ${XDG_SESSION_DESKTOP:-} ${DESKTOP_SESSION:-}"
    fi
    if printf '%s' "${desk}" | grep -qi cinnamon; then
      active_cinnamon=true
    fi
  fi

  # Toggle off/on to force a live reload when Cinnamon is running and the UUID is
  # already enabled.
  if [[ "${active_cinnamon}" == "true" ]]; then
    local cur
    cur="$(ks_gsettings_get org.cinnamon)"
    if printf '%s' "${cur}" | grep -q "'${CINNAMON_EXT_UUID}'"; then
      ks_ext_remove "${CINNAMON_EXT_UUID}" org.cinnamon
      sleep 1
    fi
  fi

  ks_ext_add "${CINNAMON_EXT_UUID}" org.cinnamon || return 1

  if [[ "${active_cinnamon}" == "true" ]]; then
    local i rc
    for i in $(seq 1 10); do
      ks_dbus_name_owned io.github.keysharp.CinnamonShell
      rc=$?
      if [[ "${rc}" -eq 0 ]]; then
        return 0
      fi
      # rc=2 means gdbus is unavailable/errored; stop polling and assume success.
      if [[ "${rc}" -eq 2 ]]; then
        break
      fi
      sleep 0.5
    done
  fi
  return 0
}

# Reconcile a prior install living under the OTHER prefix. Install mode is chosen
# purely by EUID (root -> /usr/local, user -> ${HOME}/.local), so a mode-switch
# reinstall (root<->user) would otherwise drop a SECOND parallel copy and leave the
# other prefix's binaries, its enabled root inputd daemon / setuid helper, and its
# XDG desktop+mime entries behind: the stale root daemon keeps socket-activating the
# old binary, and a leftover .desktop can still launch the old app on double-click.
#   - Root install: actively remove a prior per-user install for the resolved
#     desktop user (we have the privilege; a user install created no systemd units
#     or setuid binaries, so removing its files is sufficient).
#   - User install: we cannot touch the root-owned system copy or stop its daemon,
#     so warn and point at the root uninstaller.
# Skipped when PREFIX was set explicitly (a custom prefix has no well-defined
# "other" install to reconcile).
reconcile_other_prefix() {
  [[ "${PREFIX_EXPLICIT}" == "true" ]] && return 0

  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    local u home other_app other_bin other_apps other_mime other_icon other_mimeapps
    u="$(resolve_target_user)"
    [[ -z "${u}" ]] && return 0
    home="$(getent passwd "${u}" | cut -d: -f6 2>/dev/null || true)"
    [[ -z "${home}" ]] && return 0
    other_app="${home}/.local/lib/keysharp"
    [[ -d "${other_app}" ]] || return 0

    other_bin="${home}/.local/bin"
    other_apps="${home}/.local/share/applications"
    other_mime="${home}/.local/share/mime/packages"
    other_icon="${home}/.local/share/icons/hicolor/256x256/apps"
    other_mimeapps="${other_apps}/mimeapps.list"
    echo "Found a prior per-user Keysharp install at ${other_app}; removing it so this system-wide install is authoritative."
    rm -rf "${other_app}"
    rm -f "${other_bin}/keysharp" "${other_bin}/keyview" "${other_bin}/keysharp-inputd"
    rm -f "${other_apps}/keysharp.desktop" "${other_apps}/keyview.desktop" "${other_apps}/keysharp-helper.desktop"
    rm -f "${other_mime}/keysharp.xml" "${other_icon}/keysharp.png"
    if [[ -f "${other_mimeapps}" ]]; then
      sed -i '/^application\/x-keysharp=keysharp\.desktop$/d' "${other_mimeapps}" || true
      sed -i '/^application\/x-keysharp-compiled=keysharp\.desktop$/d' "${other_mimeapps}" || true
      sed -i '/^application\/x-autohotkey=keysharp\.desktop$/d' "${other_mimeapps}" || true
    fi
  else
    if [[ -d /usr/local/lib/keysharp ]]; then
      cat >&2 <<EOF
Warning: a system-wide Keysharp install exists at /usr/local/lib/keysharp.
This user-mode install will sit alongside it, and the system-wide install's
privileged input daemon (keysharp-inputd) will keep serving its OLD binary,
which can break input hooks/synthesis for this new install. To remove the
system-wide install first, run the uninstaller shipped with it as root:
  sudo ./uninstall.sh
EOF
    fi
  fi
}

show_install_mode() {
  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    cat <<EOF
Installing with root privileges.
Optional Linux helpers will be enabled when present:
  - keysharp-inputd: systemd socket service for input hooks, synthesis, BlockInput, and input permission management (keysharp-inputd trust list/reset).
  - keysharp-helper: Wayland screen capture helper (KWin ScreenShot2; trust gate for GNOME; screen-capture permission management).
  - DDC/CI udev rule: lets the logged-in user drive external monitors (Monitor.Brightness, Monitor.GetVCP/SetVCP). Grants access to display-controller i2c buses only, not to the motherboard SMBus, and only for the local seat.

This install may add systemd units, enable the keysharp-inputd service, load uinput, install a udev rule, and mark the KDE helper root-owned setuid.
EOF
  else
    cat <<EOF
Installing without root privileges.
Keysharp will be installed under ${PREFIX}. Optional privileged Linux helpers will be skipped, so Linux input hooks/synthesis, Wayland screen capture and DDC/CI monitor control may be unavailable until a root install is performed.
EOF
  fi
}

has_dotnet10() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'
}

install_deps() {
  # Eto.Forms Gtk backend requires GTK3; libnotify is used for notifications; AT-SPI2 supports accessibility hooks;
  # zenity provides the keysharp-inputd input-access trust prompt (a GTK dialog, so lightweight given GTK3 is
  # already required, and it runs on any desktop including KDE -- avoiding kdialog's heavy KDE Frameworks pull).
  local packages_apt=(libx11-6 libxtst6 libxinerama1 libxt6 libx11-xcb1 libxkbcommon-x11-0 libxcb-xtest0 libgtk-3-0 libglib2.0-0 libnotify4 zenity libatspi2.0-0 at-spi2-core pulseaudio-utils libudev1 libevdev2 systemd kmod)
  local packages_dnf=(libX11 libXtst libXinerama libXt libxkbcommon-x11 libxcb libX11-xcb gtk3 glib2 libnotify zenity at-spi2-core systemd-libs libevdev systemd kmod)
  local packages_yum=(libX11 libXtst libXinerama libXt libxcb xorg-x11-xkb-utils gtk3 glib2 libnotify zenity at-spi2-core systemd-libs libevdev systemd kmod)
  local packages_zypper=(libX11-6 libXtst6 libXinerama1 libXt6 libxkbcommon-x11-0 libxcb1 gtk3 glib2 libnotify4 zenity at-spi2-core libudev1 libevdev2 systemd kmod)
  local packages_pacman=(libx11 libxtst libxinerama libxt libxkbcommon-x11 libxcb gtk3 glib2 libnotify zenity at-spi2-core systemd libevdev kmod)

  if ! has_dotnet10; then
    packages_apt+=("${DOTNET_PACKAGE}")
    packages_dnf+=("${DOTNET_PACKAGE}")
    packages_yum+=("${DOTNET_PACKAGE}")
    packages_zypper+=("${DOTNET_PACKAGE}")
    packages_pacman+=(dotnet-runtime)
  fi

  if have_pkg apt-get; then
    echo "Installing runtime deps via apt-get..."
    if ! apt-get update; then
      echo "Warning: apt-get update failed; trying dependency install with the existing package indexes." >&2
    fi
    if ! DEBIAN_FRONTEND=noninteractive apt-get install -y "${packages_apt[@]}"; then
      echo "Warning: apt-get install exited non-zero (an unrelated package may have failed to configure)." >&2
      echo "Continuing — check_dotnet will verify the critical .NET 10 runtime is present." >&2
    fi
    return
  fi

  if have_pkg dnf; then
    echo "Installing runtime deps via dnf..."
    dnf install -y "${packages_dnf[@]}"
    return
  fi

  if have_pkg yum; then
    echo "Installing runtime deps via yum..."
    yum install -y "${packages_yum[@]}"
    return
  fi

  if have_pkg zypper; then
    echo "Installing runtime deps via zypper..."
    zypper install -y "${packages_zypper[@]}"
    return
  fi

  if have_pkg pacman; then
    echo "Installing runtime deps via pacman..."
    pacman -Sy --noconfirm "${packages_pacman[@]}"
    return
  fi

  echo "Package manager not detected; please ensure .NET 10, X11 libs, GTK3, libnotify, and optionally AT-SPI2 are installed." >&2
}

check_dotnet() {
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet runtime not found. Install ${DOTNET_PACKAGE} or rebuild self-contained." >&2
    exit 1
  fi

  if ! dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'; then
    echo ".NET 10 runtime missing. Install ${DOTNET_PACKAGE} or rebuild self-contained." >&2
    exit 1
  fi
}

if [[ ! -d "${APP_DIR_SOURCE}" ]]; then
  echo "Expected app payload at ${APP_DIR_SOURCE}; aborting." >&2
  exit 1
fi

show_install_mode

if [[ "${INSTALL_DEPS}" == "true" ]]; then
  if [[ "${ROOT_INSTALL}" == "true" ]]; then
    install_deps
  else
    echo "Skipping dependency install because this is a user install. Ensure .NET 10 and runtime libraries are present."
  fi
else
  echo "Skipping dependency install (INSTALL_DEPS=false). Ensure X11 libs are present."
fi

check_dotnet

# Stop ALL Keysharp/Keyview instances (the compile daemon AND any running scripts) before replacing the
# binaries, not just the daemon: a lingering old-build instance keeps holding the input hook and its granted
# permissions, so newly-launched scripts misbehave until it exits. -x matches the process name exactly, so
# it won't touch this installer or keysharp-inputd (different names). Run as root, pkill reaches the desktop
# user's processes too. (Replacing the ELF on disk while it runs is harmless on Linux.)
maybe_run pkill -x '[Kk]eysharp' || true
maybe_run pkill -x '[Kk]eyview' || true

# On a root<->user mode-switch reinstall, clean up (or warn about) the install
# under the other prefix so we don't leave a stale parallel copy / daemon behind.
reconcile_other_prefix

echo "Installing to ${APP_DIR_TARGET} (prefix=${PREFIX})"
# Replace the payload wholesale instead of overlaying it. A plain `cp -a` merge
# never deletes files removed or renamed between versions (e.g. the lib/ -> Lib/
# library relocation), so they pile up as orphans the uninstaller would have wiped;
# worse, a binary the new version dropped (e.g. an old keysharp-helper) would be
# re-blessed setuid-root by the root block below, which keys off what is on disk.
# Clearing first makes the installed tree an exact mirror of the new payload.
# (APP_DIR_TARGET is ${PREFIX}/lib/keysharp — application files only, no user data —
# so it is safe to remove; the running daemon's binary is replaced afterwards and
# the socket is restarted below, and deleting a running ELF is harmless on Linux.)
rm -rf "${APP_DIR_TARGET}"
mkdir -p "${APP_DIR_TARGET}" "${BINDIR}"
cp -a "${APP_DIR_SOURCE}/." "${APP_DIR_TARGET}/"

# Legacy (pre-rename) capture-helper artifacts that live OUTSIDE the app dir and so
# are not covered by the wipe above.
rm -f "${BINDIR}/keysharp-screencap" \
      "${DESKTOP_DIR}/keysharp-screencap.desktop"

if [[ "${ROOT_INSTALL}" == "true" ]]; then
  chown -R root:root "${APP_DIR_TARGET}"
  normalize_root_app_permissions
fi

ln -sf "${APP_DIR_TARGET}/Keysharp" "${BINDIR}/keysharp"
ln -sf "${APP_DIR_TARGET}/Keyview" "${BINDIR}/keyview"

if [[ "${ROOT_INSTALL}" == "true" ]]; then
  if [[ -f "${APP_DIR_TARGET}/keysharp-inputd" ]]; then
    ln -sf "${APP_DIR_TARGET}/keysharp-inputd" "${BINDIR}/keysharp-inputd"

    if [[ -f "${SCRIPT_DIR}/keysharp-inputd.service.in" && -f "${SCRIPT_DIR}/keysharp-inputd.socket" ]]; then
      install -d "${SYSTEMD_DIR}"
      rewrite_systemd_service "${SCRIPT_DIR}/keysharp-inputd.service.in" "${SYSTEMD_DIR}/keysharp-inputd.service"
      install -m 0644 "${SCRIPT_DIR}/keysharp-inputd.socket" "${SYSTEMD_DIR}/keysharp-inputd.socket"
      maybe_run systemctl daemon-reload || true

      # On an upgrade an older keysharp-inputd may still be running the previous
      # binary. Stop it before --install-input-access reloads the units and starts
      # the just-installed service (which brings up its required socket).
      maybe_run systemctl stop keysharp-inputd.service || true

      if ! "${APP_DIR_TARGET}/keysharp-inputd" --install-input-access; then
        echo "Warning: keysharp-inputd service setup did not complete. Input automation helper may be unavailable." >&2
        # Keep the same service entry point on the recovery path. Its Requires=
        # dependency starts the socket; daemon-reload already ran above.
        maybe_run systemctl start keysharp-inputd.service || true
      fi
    else
      echo "Warning: keysharp-inputd systemd unit files were not found in the installer payload." >&2
    fi
  fi

  if [[ -f "${APP_DIR_TARGET}/keysharp-helper" ]]; then
    chown root:root "${APP_DIR_TARGET}/keysharp-helper"
    chmod 4755 "${APP_DIR_TARGET}/keysharp-helper"
  fi

  # DDC/CI (Monitor.Brightness, Monitor.GetVCP/SetVCP on external monitors) needs
  # read/write on the connector's /dev/i2c-* node. The rule grants that to the
  # local-seat user via logind's uaccess ACL, scoped to display-controller buses
  # only -- see the rule file for why this is not the 'i2c' group. Deliberately
  # unconditional: it is independent of keysharp-helper (DDC does not go through
  # it) and harmless alongside ddcutil's equivalent 60-ddcutil-i2c.rules.
  if [[ -f "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" ]]; then
    install -Dm0644 "${SCRIPT_DIR}/70-keysharp-i2c-uaccess.rules" \
      /etc/udev/rules.d/70-keysharp-i2c-uaccess.rules
    # Reload, then re-run the rules against the buses that already exist, so DDC
    # works in the current session without a replug or reboot. (uaccess ACLs are
    # applied by udev at add/change time; trigger's default action is "change".)
    maybe_run udevadm control --reload-rules || true
    maybe_run udevadm trigger --subsystem-match=i2c-dev || true
  fi
else
  rm -f "${APP_DIR_TARGET}/keysharp-inputd" \
        "${APP_DIR_TARGET}/keysharp-helper"
  echo "Installed in user mode; privileged Linux helpers were skipped."
fi

install -d "${DESKTOP_DIR}"
rewrite_desktop_exec "${SCRIPT_DIR}/keyview.desktop" "${DESKTOP_DIR}/keyview.desktop"
rewrite_desktop_exec "${SCRIPT_DIR}/keysharp.desktop" "${DESKTOP_DIR}/keysharp.desktop"
if [[ "${ROOT_INSTALL}" == "true" && -f "${APP_DIR_TARGET}/keysharp-helper" ]]; then
  rewrite_desktop_exec "${SCRIPT_DIR}/keysharp-helper.desktop" "${DESKTOP_DIR}/keysharp-helper.desktop"
fi
install -Dm644 "${SCRIPT_DIR}/keysharp.xml" "${MIME_DIR}/keysharp.xml"
install -Dm644 "${SCRIPT_DIR}/Keysharp.png" "${ICON_DIR}/keysharp.png"

set_mime_default application/x-keysharp keysharp.desktop
set_mime_default application/x-keysharp-compiled keysharp.desktop
set_mime_default application/x-autohotkey keysharp.desktop

maybe_run update-desktop-database "${DESKTOP_DIR}" || true
maybe_run update-mime-database "${MIME_ROOT}" || true
maybe_run gtk-update-icon-cache -f "${ICON_ROOT}" || true

install_gnome_extension
install_cinnamon_extension

echo "Install complete."
