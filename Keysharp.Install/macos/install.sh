#!/bin/bash
# Package scripts: install.sh preinstall|postinstall|terminal|vscode|launch ARCH LOCATION
# Apple's Installer runs them as root for /Applications (LOCATION /) and as the user
# for ~/Applications (LOCATION is the home directory).
set -euo pipefail
export PATH=/usr/bin:/bin:/usr/sbin:/sbin

action="$1" arch="$2" location="${3:-/}"
apps="${location%/}/Applications"
if [[ ${EUID} -eq 0 ]]; then
  scope=system
  user="$(stat -f %Su /dev/console)"
else
  scope=user
  user="$(id -un)"
fi
uid="$(id -u "${user}" 2>/dev/null || echo 0)"
home="$(dscl /Search -read "/Users/${user}" NFSHomeDirectory 2>/dev/null | sed 's/^NFSHomeDirectory: //' || true)"
# An unattended system install (MDM, ssh) has no one at the console to configure.
has_user() { [[ ${uid} -ge 501 ]] && launchctl print "gui/${uid}" >/dev/null 2>&1; }

as_user() {
  if [[ ${EUID} -eq 0 ]]; then launchctl asuser "${uid}" sudo -u "${user}" "$@"; else "$@"; fi
}

# Writes stdin to an executable file. Prefix the command with as_user for files in the home.
write_exec() {
  local destination="$1" temporary; shift
  "$@" mkdir -p "${destination%/*}"
  temporary="$("$@" mktemp "${destination}.XXXXXX")"
  if ! "$@" tee "${temporary}" >/dev/null \
      || ! "$@" chmod 0755 "${temporary}" \
      || ! "$@" mv -fh "${temporary}" "${destination}"; then
    "$@" rm -f "${temporary}"
    return 1
  fi
}

shim() { printf '#!/bin/sh\nexec "%s" "$@"\n' "${apps}/$1.app/Contents/MacOS/$1"; }

case "${action}" in
  preinstall)
    # The apphost looks for .NET beside the Applications folder (~/.dotnet) before the global
    # location; see AppHostRelativeDotNet in package-macos.sh.
    if [[ ${scope} == user ]]; then
      runtime="${location%/}/.dotnet"
      [[ ${arch} == arm64 ]] || runtime="${runtime}/x64"
    else
      runtime=/usr/local/share/dotnet
      [[ ${arch} == arm64 || $(uname -m) == x86_64 ]] || runtime="${runtime}/x64"
    fi
    if ! "${runtime}/dotnet" --list-runtimes 2>/dev/null | grep -q '^Microsoft.NETCore.App 10\.'; then
      echo "Installing the .NET 10 runtime in ${runtime}..."
      curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
        --channel 10.0 --runtime dotnet --architecture "${arch}" --install-dir "${runtime}" --no-path
    fi
    pkill -f "^${apps}/Key(sharp|view)\.app/Contents/MacOS/" || true
    rm -rf "${apps}/Keysharp.app" "${apps}/Keyview.app"
    ;;

  postinstall)
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
      -f "${apps}/Keysharp.app" "${apps}/Keyview.app" || true
    # Setting the preferred handler through Launch Services avoids duplicate preference entries.
    if has_user; then
      as_user osascript -l JavaScript <<'JXA' || true
ObjC.import("CoreServices");
var status = $.LSSetDefaultRoleHandlerForContentType("org.keysharp.script", $.kLSRolesAll, "org.keysharp.keysharp");
if (status !== 0) throw new Error("Could not set Keysharp as the default script handler: " + status);
JXA
    fi
    ;;

  terminal)
    if [[ ${scope} == system ]]; then
      shim Keysharp | write_exec /usr/local/bin/keysharp
      shim Keyview | write_exec /usr/local/bin/keyview
      write_exec /usr/local/bin/keysharp-uninstall < "${apps}/Keysharp.app/Contents/Resources/uninstall.sh"
    else
      shim Keysharp | write_exec "${home}/.local/bin/keysharp"
      shim Keyview | write_exec "${home}/.local/bin/keyview"
      write_exec "${home}/.local/bin/keysharp-uninstall" < "${apps}/Keysharp.app/Contents/Resources/uninstall.sh"
      case "$(dscl /Search -read "/Users/${user}" UserShell)" in
        *zsh) profile=.zprofile ;;
        *bash) profile=.bash_profile ;;
        *) profile=.profile ;;
      esac
      line='export PATH="$HOME/.local/bin:$PATH" # Added by Keysharp'
      grep -qsxF "${line}" "${home}/${profile}" || printf '\n%s\n' "${line}" >> "${home}/${profile}"
    fi
    ;;

  vscode)
    has_user || exit 0
    shim Keysharp | write_exec "${home}/.local/bin/AutoHotkey.exe" as_user
    ;;

  launch)
    has_user || exit 0
    as_user open -a "${apps}/Keysharp.app" || true
    ;;
esac
