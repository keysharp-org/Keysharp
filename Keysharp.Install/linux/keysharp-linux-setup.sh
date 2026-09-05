#!/bin/sh
# Resolves and installs Keysharp and its two optional standalone components from
# their own GitHub releases. It carries no payload: every artifact is downloaded
# from the project that owns it, verified against that release's SHA256SUMS, and
# handed to that project's own installer or to apt.
set -eu

PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
unset CDPATH ENV BASH_ENV LD_LIBRARY_PATH LD_PRELOAD 2>/dev/null || true

KEYSHARP_REPOSITORY=keysharp-org/Keysharp
INPUT_REPOSITORY=keysharp-org/keysharp-input
DESKTOP_REPOSITORY=keysharp-org/keysharp-desktop

# A component needs this ABI major and at least this additive minor.
# Product versions select artifacts; the client ABI decides compatibility.
INPUT_CLIENT_ABI_MAJOR=0
INPUT_CLIENT_ABI_MINOR=2
DESKTOP_CLIENT_ABI_MAJOR=0
DESKTOP_CLIENT_ABI_MINOR=8

channel=auto
keysharp_version=latest
input_version=latest
desktop_version=latest
want_input=true
want_desktop=true
dry_run=false
diagnose=false
upgrade_components=false
input_explicit=false
desktop_explicit=false

usage() {
    cat <<'EOF'
Usage: sudo ./keysharp-linux-setup.sh [options]

Installs Keysharp, and the keysharp-input and keysharp-desktop components that
its privileged Linux features need. Each is downloaded from its own release and
installed by its own installer. A component already present at a compatible
client ABI is left alone.

Options:
  --channel deb|tar     Force a channel instead of detecting one.
  --keysharp-version V  Install this version instead of the latest.
  --input-version V     Install this keysharp-input version.
  --desktop-version V   Install this keysharp-desktop version.
  --upgrade-components Upgrade components through their existing install channel.
  --diagnose            Report installed versions, ABI and service health; no network.
  --skip-input          Do not install keysharp-input.
  --skip-desktop        Do not install keysharp-desktop.
  --dry-run             Resolve and report the plan; download nothing.
  -h, --help            Show this help.

Keysharp runs without either component; the features each one provides are
unavailable until it is installed.
EOF
}

need_cmd() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "$1 is required but was not found." >&2
        return 1
    }
}

# Release assets name x86_64 "x64" and aarch64 "arm64"; Debian names them
# "amd64" and "arm64". Set every spelling once.
detect_arch() {
    machine=$(uname -m)
    case "$machine" in
        x86_64|amd64) arch_tag=linux-x64; deb_arch=amd64 ;;
        aarch64|arm64) arch_tag=linux-arm64; deb_arch=arm64 ;;
        *)
            echo "Unsupported architecture: $machine" >&2
            return 1
            ;;
    esac
}

# apt owns the result where it is available, so a dpkg host takes the package
# channel and everything else takes the portable one.
detect_channel() {
    if [ "$channel" != auto ]; then
        return 0
    fi
    if [ -x /usr/local/lib/keysharp/Keysharp ] \
        && is_protected_path /usr/local/lib/keysharp/Keysharp; then
        channel=tar
        return 0
    fi
    if command -v apt-get >/dev/null 2>&1 && command -v dpkg >/dev/null 2>&1; then
        channel=deb
    else
        channel=tar
    fi
}

# GitHub redirects /releases/latest to the tag, so the tag is readable without
# an API token, a rate limit, or a JSON parser.
resolve_version() {
    resolve_repository=$1
    resolve_requested=${2#v}
    case "$resolve_requested" in
        latest) ;;
        *[!0-9.]*|'') echo "Invalid release version: $2" >&2; return 1 ;;
    esac
    if [ "$resolve_requested" != latest ]; then
        printf '%s\n' "$resolve_requested"
        return 0
    fi
    resolve_url=$(curl -fsSLI --connect-timeout 15 --max-time 60 -o /dev/null -w '%{url_effective}' \
        --proto '=https' --tlsv1.2 \
        "https://github.com/$resolve_repository/releases/latest") || {
        echo "Could not reach the $resolve_repository releases page." >&2
        return 1
    }
    case "$resolve_url" in
        */releases/tag/v*) ;;
        *)
            echo "No published release found for $resolve_repository." >&2
            return 1
            ;;
    esac
    printf '%s\n' "${resolve_url##*/releases/tag/v}"
}

# Separated from the download so the rule that an unlisted or mismatched asset
# is rejected can be exercised without a network.
verify_checksum() {
    verify_dir=$1
    verify_asset=$2
    verify_sums=$3
    verify_line=$(awk -v want="$verify_asset" \
        '{ name = $2; sub(/^\*/, "", name); if (name == want) print }' \
        "$verify_sums")
    if [ -z "$verify_line" ]; then
        echo "$verify_asset is not listed in $verify_sums." >&2
        return 1
    fi
    printf '%s\n' "$verify_line" \
        | (cd "$verify_dir" && sha256sum -c -) >/dev/null 2>&1
}

# SHA256SUMS detects transfer corruption. GitHub provenance, when available,
# additionally binds both downloads to the owning project's release workflow.
download_verified() {
    download_repository=$1
    download_tag=$2
    download_asset=$3
    download_dir=$4
    download_base="https://github.com/$download_repository/releases/download/$download_tag"
    curl -fsSL --connect-timeout 15 --max-time 600 --proto '=https' --tlsv1.2 \
        -o "$download_dir/$download_asset" "$download_base/$download_asset" || {
        echo "Could not download $download_asset from $download_repository $download_tag." >&2
        return 1
    }
    curl -fsSL --connect-timeout 15 --max-time 600 --proto '=https' --tlsv1.2 \
        -o "$download_dir/SHA256SUMS" "$download_base/SHA256SUMS" || {
        echo "Could not download SHA256SUMS from $download_repository $download_tag." >&2
        return 1
    }
    if command -v gh >/dev/null 2>&1; then
        gh attestation verify "$download_dir/SHA256SUMS" \
            --repo "$download_repository" >/dev/null || {
            echo "Provenance verification failed for SHA256SUMS from $download_repository $download_tag." >&2
            return 1
        }
        gh attestation verify "$download_dir/$download_asset" \
            --repo "$download_repository" >/dev/null || {
            echo "Provenance verification failed for $download_asset from $download_repository $download_tag." >&2
            return 1
        }
    fi
    verify_checksum "$download_dir" "$download_asset" "$download_dir/SHA256SUMS" || {
        echo "Checksum verification failed for $download_asset from $download_repository $download_tag." >&2
        return 1
    }
    rm -f -- "$download_dir/SHA256SUMS"
}

# Only run installed binaries whose path and resolved target are root protected.
is_protected_path() {
    protected_path=$1
    while :; do
        protected_metadata=$(stat -Lc '%u %a' -- "$protected_path" 2>/dev/null) || return 1
        # shellcheck disable=SC2086 # stat emits two numeric fields
        set -- $protected_metadata
        [ "$1" = 0 ] || return 1
        if [ $((0$2 & 022)) -ne 0 ]; then
            [ "$protected_path" = /nix/store ] \
                && [ $((0$2 & 002)) -eq 0 ] && [ $((0$2 & 01000)) -ne 0 ] || return 1
        fi
        [ "$protected_path" = / ] && return 0
        protected_path=${protected_path%/*}
        [ -n "$protected_path" ] || protected_path=/
    done
}

component_candidates() {
    printf '%s\n' "/usr/bin/$1" "/run/current-system/sw/bin/$1" "/usr/local/bin/$1"
}

info_field() {
    printf '%s\n' "$component_info" | awk -F= -v key="$1" '
        $1 == key { value = $2; count++ }
        END { if (count == 1) print value }'
}

inspect_component() {
    component_name=$1
    component_major=$2
    component_minor=$3
    component_path=
    component_version=unknown
    component_channel=$channel
    component_health=missing
    component_compatible=false
    component_abi=unknown
    for component_candidate in $(component_candidates "$component_name"); do
        [ -x "$component_candidate" ] || continue
        component_resolved=$(readlink -f -- "$component_candidate") || continue
        if ! is_protected_path "$component_candidate" \
            || ! is_protected_path "$component_resolved"; then continue; fi
        if [ -n "$component_path" ] && [ "$component_path" != "$component_resolved" ]; then
            component_health=conflicting-installations
            component_compatible=false
            return 0
        fi
        component_path=$component_resolved
    done
    [ -n "$component_path" ] || return 0
    component_prefix=${component_path%/bin/*}
    case "$component_path" in
        /usr/local/*) component_channel=tar ;;
        /nix/store/*) component_channel=nix ;;
        *)
            if command -v dpkg-query >/dev/null 2>&1 \
                && dpkg-query -S "$component_path" >/dev/null 2>&1; then
                component_channel=deb
            else
                component_channel=system
            fi ;;
    esac
    component_health=unloadable-client
    component_info=$("$component_path" info 2>/dev/null) || return 0
    component_version=$(info_field product_version)
    component_actual_major=$(info_field client_abi_major)
    component_actual_minor=$(info_field client_abi_minor)
    component_abi=$component_actual_major.$component_actual_minor
    case "$component_actual_minor" in ''|*[!0-9]*) return 0 ;; esac
    component_health=incompatible-abi
    [ "$component_actual_major" = "$component_major" ] \
        && [ "$component_actual_minor" -ge "$component_minor" ] || return 0
    component_compatible=true
    component_health=incomplete-installation
    case "$component_name" in
        keysharp-input) component_socket=keysharp-input.socket; component_policy=org.keysharp.input.policy ;;
        keysharp-desktop) component_socket=keysharp-desktop-authority.socket; component_policy=org.keysharp.desktop.policy
            for component_resource in libexec/keysharp-desktop-capture-worker \
                share/gnome-shell/extensions/keysharp@keysharp.io/metadata.json \
                share/cinnamon/extensions/keysharp@keysharp.io/metadata.json \
                share/kwin/scripts/io.github.keysharp.desktop.kwin/metadata.json; do
                is_protected_path "$component_prefix/$component_resource" || return 0
            done ;;
    esac
    component_policy_path=/usr/share/polkit-1/actions/$component_policy
    [ "$component_channel" != nix ] \
        || component_policy_path=/run/current-system/sw/share/polkit-1/actions/$component_policy
    is_protected_path "$component_policy_path" || return 0
    command -v systemctl >/dev/null 2>&1 || return 0
    [ "$(systemctl show --property=LoadState --value "$component_socket" 2>/dev/null)" = loaded ] || return 0
    component_health=inactive-socket
    systemctl is-active --quiet "$component_socket" || return 0
    component_health=failed-service
    if systemctl is-failed --quiet "${component_socket%.socket}.service"; then return 0; fi
    if [ "$component_name" = keysharp-desktop ]; then
        component_health=disabled-session-service
        [ "$(systemctl --global is-enabled keysharp-desktop.service 2>/dev/null)" = enabled ] || return 0
    fi
    component_health=ready
}

abi_compatible() {
    [ "$1" = "$3" ] || return 1
    case "$2" in ''|*[!0-9]*) return 1 ;; esac
    [ "$2" -ge "$4" ]
}

verify_deb_abi() {
    deb_abi=$(dpkg-deb -f "$1" Provides | tr ',' '\n' | awk -v name="$2-client-abi-$3" '
        $1 == name && $2 == "(=" && NF == 3 { sub(/\)$/, "", $3); print $3 }')
    if ! abi_compatible "${deb_abi%%.*}" "${deb_abi#*.}" "$3" "$4"; then
        echo "$1 does not provide the required $2 client ABI $3.$4 or newer minor." >&2
        return 1
    fi
}

verify_archive_abi() {
    archive_prefix=$1
    [ "$2" != keysharp-desktop ] || archive_prefix=$1/payload/usr/local
    component_info=$(LD_LIBRARY_PATH="$archive_prefix/lib" "$archive_prefix/bin/$2" info)
    if ! abi_compatible "$(info_field client_abi_major)" "$(info_field client_abi_minor)" "$3" "$4"; then
        echo "$1 does not provide the required $2 client ABI $3.$4 or newer minor." >&2
        return 1
    fi
}

component_needs_install() {
    [ "$component_health" != ready ] || [ "$component_compatible" != true ] \
        || [ "$upgrade_components" = true ] || [ "$1" = true ]
}

report_component() {
    printf '%s\n' "  $1: $2; version=$3 ABI=$4 channel=$5"
    case "$2" in
        ready|skipped) ;;
        missing) printf '%s\n' "    Run setup to install it." ;;
        inactive-socket|failed-service|disabled-session-service)
            printf '%s\n' "    Repair through the existing installer, or inspect systemctl status $1*." ;;
        *) printf '%s\n' "    Repair through the owning install channel before using this component." ;;
    esac
}

report_plan() {
    printf '%s\n' "Keysharp channel: $channel ($arch_tag)"
    [ "$diagnose" = true ] || printf '%s\n' "  Keysharp $keysharp_resolved"
    report_component keysharp-input "$input_health" "$input_installed_version" "$input_abi" "$input_channel"
    report_component keysharp-desktop "$desktop_health" "$desktop_installed_version" "$desktop_abi" "$desktop_channel"
    [ -z "$input_resolved" ] || printf '%s\n' "  Install keysharp-input $input_resolved ($input_channel)"
    [ -z "$desktop_resolved" ] || printf '%s\n' "  Install keysharp-desktop $desktop_resolved ($desktop_channel)"
}

release_asset() {
    if [ "$2" = deb ]; then
        printf '%s_%s_%s.deb\n' "$1" "$3" "$deb_arch"
    else
        printf '%s-%s-%s.tar.gz\n' "$1" "$3" "$arch_tag"
    fi
}

# Turns on the keysharp-desktop shell extension for the user running sudo.
#
# The tarball channel gets this from keysharp-desktop's own installer, but the
# deb channel ends at apt-get, and its maintainer scripts deliberately do not
# touch a user's dconf. Neither runs at all when the installed keysharp-desktop
# is already compatible -- which is exactly the state of someone rerunning this
# to fix a session whose backend reports None because the extension was never
# enabled. So this runs regardless of channel, and regardless of whether
# keysharp-desktop was installed just now.
#
# Advisory throughout. The extension is per-user and per-desktop: a KWin or X11
# machine has nothing to enable, and nothing here may fail the setup.
enable_desktop_extension() {
    extension_uid=${SUDO_UID:-}
    case "$extension_uid" in
        ''|*[!0-9]*|0) return 0 ;;
    esac
    [ -x /usr/bin/getent ] && [ -x /usr/bin/awk ] && [ -x /usr/bin/stat ] \
        && [ -x /usr/sbin/runuser ] && [ -x /usr/bin/env ] || return 0
    extension_passwd=$(/usr/bin/getent passwd "$extension_uid" 2>/dev/null) \
        || return 0
    extension_name=$(printf '%s\n' "$extension_passwd" | /usr/bin/awk -F: \
        -v expected="$extension_uid" '$3 == expected { print $1; exit }')
    extension_home=$(printf '%s\n' "$extension_passwd" | /usr/bin/awk -F: \
        -v expected="$extension_uid" '$3 == expected { print $6; exit }')
    case "$extension_name" in
        ''|-*|*[!A-Za-z0-9._-]*) return 0 ;;
    esac
    case "$extension_home" in
        /*) ;;
        *) return 0 ;;
    esac
    # The checks keysharp-desktop's own installer makes before trusting a
    # runtime directory: ours, not group- or world-writable, not a symlink, and
    # carrying a real session bus socket. Without them the write can land on a
    # bus nobody reads.
    extension_runtime=/run/user/$extension_uid
    extension_metadata=$(/usr/bin/stat -Lc '%u %a' -- "$extension_runtime" \
        2>/dev/null) || return 0
    # shellcheck disable=SC2086 # deliberate split into uid and mode
    set -- $extension_metadata
    [ "$1" = "$extension_uid" ] && [ $((0$2 & 022)) -eq 0 ] \
        && [ ! -L "$extension_runtime" ] \
        && [ -S "$extension_runtime/bus" ] \
        && [ ! -L "$extension_runtime/bus" ] || return 0

    for extension_binary in /usr/bin/keysharp-desktop \
        /run/current-system/sw/bin/keysharp-desktop \
        /usr/local/bin/keysharp-desktop; do
        [ -x "$extension_binary" ] || continue
        extension_status=0
        # env -i leaves PATH as /usr/bin:/bin, which does not contain
        # /usr/local/bin, so the binary is named absolutely.
        /usr/sbin/runuser -u "$extension_name" -- /usr/bin/env -i \
            HOME="$extension_home" USER="$extension_name" \
            LOGNAME="$extension_name" LANG=C PATH=/usr/bin:/bin \
            XDG_RUNTIME_DIR="$extension_runtime" \
            DBUS_SESSION_BUS_ADDRESS="unix:path=$extension_runtime/bus" \
            "$extension_binary" enable-extension >/dev/null 2>&1 \
            || extension_status=$?
        case "$extension_status" in
            0) return 0 ;;
            3) printf '%s\n' \
                "keysharp-desktop shell extension enabled. Log out and back in to load it." ;;
            *) printf '%s\n' \
                "Could not enable the keysharp-desktop shell extension. On GNOME or Cinnamon, run this as yourself:" \
                "  keysharp-desktop enable-extension" ;;
        esac
        return 0
    done
    return 0
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --channel)
            [ "$#" -ge 2 ] || { echo "--channel requires deb or tar" >&2; exit 2; }
            case "$2" in
                deb|tar) channel=$2 ;;
                *) echo "--channel accepts deb or tar" >&2; exit 2 ;;
            esac
            shift 2
            ;;
        --keysharp-version)
            [ "$#" -ge 2 ] || { echo "--keysharp-version requires a version" >&2; exit 2; }
            keysharp_version=$2
            shift 2
            ;;
        --input-version)
            [ "$#" -ge 2 ] || { echo "--input-version requires a version" >&2; exit 2; }
            input_version=$2
            input_explicit=true
            shift 2
            ;;
        --desktop-version)
            [ "$#" -ge 2 ] || { echo "--desktop-version requires a version" >&2; exit 2; }
            desktop_version=$2
            desktop_explicit=true
            shift 2
            ;;
        --skip-input) want_input=false; shift ;;
        --skip-desktop) want_desktop=false; shift ;;
        --dry-run) dry_run=true; shift ;;
        --diagnose) diagnose=true; shift ;;
        --upgrade-components) upgrade_components=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [ "$dry_run" != true ] && [ "$diagnose" != true ] && [ "$(id -u)" -ne 0 ]; then
    echo "keysharp-linux-setup.sh must be run as root" >&2
    exit 1
fi

detect_arch
detect_channel
input_resolved=
desktop_resolved=
keysharp_resolved=
inspect_component keysharp-input "$INPUT_CLIENT_ABI_MAJOR" "$INPUT_CLIENT_ABI_MINOR"
input_channel=$component_channel
input_health=$component_health
input_installed_version=$component_version
input_abi=$component_abi
if [ "$want_input" = false ]; then input_health=skipped
elif ! component_needs_install "$input_explicit"; then want_input=false
fi
inspect_component keysharp-desktop "$DESKTOP_CLIENT_ABI_MAJOR" "$DESKTOP_CLIENT_ABI_MINOR"
desktop_channel=$component_channel
desktop_health=$component_health
desktop_installed_version=$component_version
desktop_abi=$component_abi
enable_desktop=$want_desktop
if [ "$want_desktop" = false ]; then desktop_health=skipped
elif ! component_needs_install "$desktop_explicit"; then want_desktop=false
fi
if [ "$diagnose" = true ]; then
    report_plan
    printf '%s\n' "Run each component's probe as your graphical user to inspect session capabilities."
    exit 0
fi

for selected_channel in "$channel" \
    "$(if [ "$want_input" = true ]; then printf '%s' "$input_channel"; fi)" \
    "$(if [ "$want_desktop" = true ]; then printf '%s' "$desktop_channel"; fi)"; do
    case "$selected_channel" in
        deb) need_cmd apt-get; need_cmd dpkg-deb ;;
        tar) need_cmd tar ;;
        '') ;;
        *) echo "Update or repair the $selected_channel component through its package manager; setup will not replace it." >&2; exit 1 ;;
    esac
done
[ "$channel" != tar ] || need_cmd bash
if [ "$input_health" = conflicting-installations ] || [ "$desktop_health" = conflicting-installations ]; then
    report_plan
    echo "Remove the conflicting component installation through its owner before rerunning setup." >&2
    exit 1
fi
need_cmd curl
need_cmd sha256sum
keysharp_resolved=$(resolve_version "$KEYSHARP_REPOSITORY" "$keysharp_version")
[ "$want_input" != true ] || input_resolved=$(resolve_version "$INPUT_REPOSITORY" "$input_version")
[ "$want_desktop" != true ] || desktop_resolved=$(resolve_version "$DESKTOP_REPOSITORY" "$desktop_version")
report_plan
[ "$dry_run" != true ] || exit 0

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT HUP INT TERM
set --
input_tree=
desktop_tree=
keysharp_tree=
if [ "$want_input" = true ]; then
    asset=$(release_asset keysharp-input "$input_channel" "$input_resolved")
    download_verified "$INPUT_REPOSITORY" "v$input_resolved" "$asset" "$work"
    if [ "$input_channel" = deb ]; then
        verify_deb_abi "$work/$asset" keysharp-input "$INPUT_CLIENT_ABI_MAJOR" "$INPUT_CLIENT_ABI_MINOR"
        set -- "$@" "$work/$asset"
    else input_tree=$work/keysharp-input-$input_resolved-$arch_tag
        tar -xzf "$work/$asset" -C "$work"
    fi
fi
if [ "$want_desktop" = true ]; then
    asset=$(release_asset keysharp-desktop "$desktop_channel" "$desktop_resolved")
    download_verified "$DESKTOP_REPOSITORY" "v$desktop_resolved" "$asset" "$work"
    if [ "$desktop_channel" = deb ]; then
        verify_deb_abi "$work/$asset" keysharp-desktop "$DESKTOP_CLIENT_ABI_MAJOR" "$DESKTOP_CLIENT_ABI_MINOR"
        set -- "$@" "$work/$asset"
    else desktop_tree=$work/keysharp-desktop-$desktop_resolved-$arch_tag
        tar -xzf "$work/$asset" -C "$work"
    fi
fi
asset=$(release_asset keysharp "$channel" "$keysharp_resolved")
download_verified "$KEYSHARP_REPOSITORY" "v$keysharp_resolved" "$asset" "$work"
if [ "$channel" = deb ]; then set -- "$@" "$work/$asset"
else keysharp_tree=$work/keysharp-$arch_tag
    tar -xzf "$work/$asset" -C "$work"
fi

# No installed files are changed until every requested artifact is verified.
for component_tree in "$input_tree" "$desktop_tree"; do
    [ -n "$component_tree" ] || continue
    if [ ! -f "$component_tree/check-runtime.sh" ]; then
        echo "This component release has no dependency preflight. Install its documented runtime dependencies and use its own installer." >&2
        exit 1
    fi
done
for component_tree in "$input_tree" "$desktop_tree"; do
    [ -z "$component_tree" ] || sh "$component_tree/check-runtime.sh" --install
done
if [ -n "$keysharp_tree" ]; then
    bash "$keysharp_tree/install.sh" --install-dependencies
fi
for component_tree in "$input_tree" "$desktop_tree"; do
    [ -z "$component_tree" ] || sh "$component_tree/check-runtime.sh"
done
[ -z "$input_tree" ] || verify_archive_abi "$input_tree" keysharp-input "$INPUT_CLIENT_ABI_MAJOR" "$INPUT_CLIENT_ABI_MINOR"
[ -z "$desktop_tree" ] || verify_archive_abi "$desktop_tree" keysharp-desktop "$DESKTOP_CLIENT_ABI_MAJOR" "$DESKTOP_CLIENT_ABI_MINOR"
# Components supplied as local archives must not be replaced by apt Recommends.
if [ "$#" -gt 0 ]; then apt-get install -y --reinstall --no-install-recommends "$@"; fi
for component_tree in "$input_tree" "$desktop_tree"; do
    [ -z "$component_tree" ] || sh "$component_tree/install.sh"
done
[ -z "$keysharp_tree" ] || INSTALL_DEPS=false bash "$keysharp_tree/install.sh"
[ "$enable_desktop" != true ] || enable_desktop_extension
printf '%s\n' "Keysharp $keysharp_resolved is installed. Run 'keysharp --version' to confirm."
