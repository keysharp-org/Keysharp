#!/bin/bash
# Keysharp uninstaller: installed as keysharp-uninstall, in Keysharp.app for the Dash, and on the DMG.
if [ -z "${BASH_VERSION:-}" ]; then exec /bin/bash "$0" "$@"; fi
set -euo pipefail
export PATH=/usr/bin:/bin:/usr/sbin:/sbin

usage() {
  cat <<'EOF'
Usage: keysharp-uninstall [--scope system|user] [--remove-settings|--keep-settings] [--yes] [--gui]

Removes Keysharp and Keyview with their terminal commands, VS Code shim and package
receipts. Installed copies remove their own installation, in /Applications or
~/Applications. The copy on the disk image removes every installation found.
Use --scope to select an installation explicitly.
  --remove-settings  delete your Keysharp settings and cached data (default)
  --keep-settings    preserve your Keysharp settings and cached data
  --yes              do not ask for confirmation
  --gui              ask with dialogs instead of in the terminal
The .NET runtime and macOS privacy permissions are left in place.
EOF
}

scope="" remove_settings="" yes=false gui=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope) scope="${2:-}"; shift ;;
    --remove-settings) remove_settings=true ;;
    --keep-settings) remove_settings=false ;;
    --yes) yes=true ;;
    --gui) gui=true ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; exit 1 ;;
  esac
  shift
done
[[ -z ${scope} || ${scope} == system || ${scope} == user ]] || { usage >&2; exit 1; }

say() {
  if ${gui}; then
    osascript -e 'on run argv' -e 'display dialog (item 1 of argv) with title "Uninstall Keysharp" buttons {"OK"} default button 1' -e 'end run' "$1" >/dev/null
  else
    printf '%s\n' "$1"
  fi
}
trap 'status=$?; if [[ ${status} -ne 0 ]] && ${gui}; then say "Keysharp was not uninstalled."; fi' EXIT

self="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
user="${SUDO_USER:-$(id -un)}"
home="$(dscl /Search -read "/Users/${user}" NFSHomeDirectory | sed 's/^NFSHomeDirectory: //')"

# A copy installed with Keysharp removes only its own installation.
if [[ -z ${scope} ]]; then
  case "${self}" in
    /Applications/*|/usr/local/bin/*) scope=system ;;
    "${home}"/Applications/*|"${home}"/.local/bin/*) scope=user ;;
    */Keysharp.app/*) say "Move Keysharp back to an Applications folder to uninstall it."; trap - EXIT; exit 1 ;;
  esac
fi

locate() {
  if [[ $1 == system ]]; then
    apps=/Applications bin=/usr/local/bin volume=/ where="/Applications"
  else
    apps="${home}/Applications" bin="${home}/.local/bin" volume="${home}" where="~/Applications"
  fi
}

receipts() { pkgutil --volume "${volume}" --pkgs='org\.keysharp\.pkg.*' 2>/dev/null || true; }

installed() {
  locate "$1"
  [[ -e ${apps}/Keysharp.app || -e ${apps}/Keyview.app || -n $(receipts) ]] && return 0
  local file
  for file in "${bin}/keysharp" "${bin}/keyview" "${bin}/keysharp-uninstall" "${home}/.local/bin/AutoHotkey.exe"; do
    ours "${file}" && return 0
  done
  return 1
}

# Shims written by the installer, symbolic links into the apps, and copies of this script.
ours() {
  if [[ -L $1 ]]; then
    [[ $(readlink "$1") == "${apps}"/Key*.app/* ]]
  else
    grep -qsF -e "exec \"${apps}/Keysharp.app/" -e "exec \"${apps}/Keyview.app/" -e '# Keysharp uninstaller' "$1"
  fi
}

remove() {
  locate "$1"
  pkill -f "^${apps}/Key(sharp|view)\.app/Contents/MacOS/" || true
  rm -rf "${apps}/Keysharp.app" "${apps}/Keyview.app"
  local file id
  for file in "${bin}/keysharp" "${bin}/keyview" "${bin}/keysharp-uninstall" "${home}/.local/bin/AutoHotkey.exe"; do
    if ours "${file}"; then rm -f "${file}"; fi
  done
  for id in $(receipts); do pkgutil --volume "${volume}" --forget "${id}" >/dev/null; done
  if [[ $1 == user ]]; then
    for file in "${home}/.zprofile" "${home}/.bash_profile" "${home}/.profile"; do
      if grep -qsF '# Added by Keysharp' "${file}"; then
        # Rewriting in place keeps the profile's owner when running under sudo.
        local contents
        contents="$(grep -vF '# Added by Keysharp' "${file}" || true)"
        printf '%s\n' "${contents}" > "${file}"
      fi
    done
  fi
}

scopes="" places=""
for s in ${scope:-user system}; do
  if installed "${s}"; then
    scopes="${scopes} ${s}"
    places="${places:+${places} and }${where}"
  fi
done
if [[ -z ${scopes} && ${remove_settings} != true ]]; then
  say "Keysharp is not installed${scope:+ in ${where}}."
  exit 0
fi

if ! ${yes} && [[ -n ${scopes} ]]; then
  question="Uninstall Keysharp and Keyview from ${places}? Running scripts will be stopped."
  if ${gui}; then
    default_button="Uninstall and Delete Settings"
    if [[ ${remove_settings} == false ]]; then default_button=Uninstall; fi
    choice="$(osascript -e 'on run argv' \
      -e 'button returned of (display dialog (item 1 of argv) with title "Uninstall Keysharp" buttons {"Cancel", "Uninstall and Delete Settings", "Uninstall"} default button (item 2 of argv) cancel button 1 with icon caution)' \
      -e 'end run' "${question} Choose Uninstall to keep your settings, or Uninstall and Delete Settings to delete them." "${default_button}")" || exit 0
    if [[ ${choice} == Uninstall ]]; then remove_settings=false; else remove_settings=true; fi
  elif [[ -t 0 ]]; then
    read -r -p "${question} [Y/n] " answer
    [[ -z ${answer} || ${answer} == [Yy]* ]] || exit 0
    if [[ -z ${remove_settings} ]]; then
      read -r -p "Also delete your Keysharp settings? [Y/n] " answer
      if [[ -z ${answer} || ${answer} == [Yy]* ]]; then remove_settings=true; else remove_settings=false; fi
    fi
  fi
fi
remove_settings="${remove_settings:-true}"

for s in ${scopes}; do
  locate "${s}"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -u "${apps}/Keysharp.app" "${apps}/Keyview.app" || true
  if [[ ${s} == system && ${EUID} -ne 0 ]]; then
    elevated=(/bin/bash "${self}" --scope system --keep-settings --yes)
    if ${gui}; then
      osascript -e 'on run argv' -e 'do shell script (item 1 of argv) with administrator privileges' -e 'end run' \
        "$(printf '%q ' "${elevated[@]}")" >/dev/null
    else
      sudo "${elevated[@]}" >/dev/null
    fi
    # Without sudo, the elevated copy runs as root and cannot find this user's VS Code shim.
    locate system
    if ours "${home}/.local/bin/AutoHotkey.exe"; then rm -f "${home}/.local/bin/AutoHotkey.exe"; fi
  else
    remove "${s}"
  fi
done

if [[ ${remove_settings} == true ]]; then
  rm -rf "${home}/Library/Application Support/Keysharp" "${home}"/Library/Preferences/org.keysharp.* \
    "${home}"/Library/Caches/org.keysharp.*
fi

say "Keysharp has been uninstalled. The .NET runtime is kept, and you can remove Keysharp's entries in System Settings > Privacy & Security."
