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

# A component is compatible when its public client library reports this major.
# Product versions select artifacts; the client ABI decides compatibility.
INPUT_CLIENT_ABI_MAJOR=0
DESKTOP_CLIENT_ABI_MAJOR=0

channel=auto
keysharp_version=latest
input_version=latest
desktop_version=latest
want_input=true
want_desktop=true
dry_run=false

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
    resolve_requested=$2
    if [ "$resolve_requested" != latest ]; then
        printf '%s\n' "$resolve_requested"
        return 0
    fi
    resolve_url=$(curl -fsSLI -o /dev/null -w '%{url_effective}' \
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

# The release's own SHA256SUMS is the reference, so no checksum has to be
# recorded here and go stale.
download_verified() {
    download_repository=$1
    download_tag=$2
    download_asset=$3
    download_dir=$4
    download_base="https://github.com/$download_repository/releases/download/$download_tag"
    curl -fsSL --proto '=https' --tlsv1.2 \
        -o "$download_dir/$download_asset" "$download_base/$download_asset" || {
        echo "Could not download $download_asset from $download_repository $download_tag." >&2
        return 1
    }
    curl -fsSL --proto '=https' --tlsv1.2 \
        -o "$download_dir/SHA256SUMS" "$download_base/SHA256SUMS" || {
        echo "Could not download SHA256SUMS from $download_repository $download_tag." >&2
        return 1
    }
    verify_checksum "$download_dir" "$download_asset" "$download_dir/SHA256SUMS" || {
        echo "Checksum verification failed for $download_asset from $download_repository $download_tag." >&2
        return 1
    }
    rm -f -- "$download_dir/SHA256SUMS"
}

# The component's own CLI answers what it provides, which is the same contract
# its installer and its Debian capability express.
component_is_compatible() {
    component_command=$1
    component_major=$2
    command -v "$component_command" >/dev/null 2>&1 || return 1
    "$component_command" info 2>/dev/null \
        | grep -q "^client_abi_major=$component_major$"
}

report_plan() {
    printf '%s\n' "Channel: $channel ($arch_tag)"
    printf '%s\n' "  Keysharp $keysharp_resolved"
    if [ "$want_input" = true ]; then
        printf '%s\n' "  keysharp-input $input_resolved"
    else
        printf '%s\n' "  keysharp-input: already compatible or skipped"
    fi
    if [ "$want_desktop" = true ]; then
        printf '%s\n' "  keysharp-desktop $desktop_resolved"
    else
        printf '%s\n' "  keysharp-desktop: already compatible or skipped"
    fi
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
            shift 2
            ;;
        --desktop-version)
            [ "$#" -ge 2 ] || { echo "--desktop-version requires a version" >&2; exit 2; }
            desktop_version=$2
            shift 2
            ;;
        --skip-input) want_input=false; shift ;;
        --skip-desktop) want_desktop=false; shift ;;
        --dry-run) dry_run=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [ "$dry_run" != true ] && [ "$(id -u)" -ne 0 ]; then
    echo "keysharp-linux-setup.sh must be run as root" >&2
    exit 1
fi

need_cmd curl
need_cmd sha256sum
detect_arch
detect_channel
if [ "$channel" = tar ]; then
    need_cmd tar
fi

# Skip what is already usable, so a rerun installs only what is missing.
if [ "$want_input" = true ] \
    && component_is_compatible keysharp-input "$INPUT_CLIENT_ABI_MAJOR"; then
    want_input=false
fi
if [ "$want_desktop" = true ] \
    && component_is_compatible keysharp-desktop "$DESKTOP_CLIENT_ABI_MAJOR"; then
    want_desktop=false
fi

keysharp_resolved=$(resolve_version "$KEYSHARP_REPOSITORY" "$keysharp_version")
input_resolved=
desktop_resolved=
if [ "$want_input" = true ]; then
    input_resolved=$(resolve_version "$INPUT_REPOSITORY" "$input_version")
fi
if [ "$want_desktop" = true ]; then
    desktop_resolved=$(resolve_version "$DESKTOP_REPOSITORY" "$desktop_version")
fi

report_plan
if [ "$dry_run" = true ]; then
    exit 0
fi

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT HUP INT TERM

if [ "$channel" = deb ]; then
    need_cmd apt-get
    set --
    if [ "$want_input" = true ]; then
        asset="keysharp-input_${input_resolved}_${deb_arch}.deb"
        download_verified "$INPUT_REPOSITORY" "v$input_resolved" "$asset" "$work"
        set -- "$@" "$work/$asset"
    fi
    if [ "$want_desktop" = true ]; then
        asset="keysharp-desktop_${desktop_resolved}_${deb_arch}.deb"
        download_verified "$DESKTOP_REPOSITORY" "v$desktop_resolved" "$asset" "$work"
        set -- "$@" "$work/$asset"
    fi
    asset="keysharp-${keysharp_resolved}-${arch_tag}.deb"
    download_verified "$KEYSHARP_REPOSITORY" "v$keysharp_resolved" "$asset" "$work"
    set -- "$@" "$work/$asset"
    # One transaction, so the components satisfy Keysharp's Recommends and the
    # order of the packages on the command line does not matter.
    apt-get install -y "$@"
else
    # Components first, so Keysharp's post-install probe sees what is present.
    if [ "$want_input" = true ]; then
        asset="keysharp-input-${input_resolved}-${arch_tag}.tar.gz"
        download_verified "$INPUT_REPOSITORY" "v$input_resolved" "$asset" "$work"
        tar -xzf "$work/$asset" -C "$work"
        (cd "$work/keysharp-input-${input_resolved}-${arch_tag}" \
            && sh ./install.sh --skip-if-compatible)
    fi
    if [ "$want_desktop" = true ]; then
        asset="keysharp-desktop-${desktop_resolved}-${arch_tag}.tar.gz"
        download_verified "$DESKTOP_REPOSITORY" "v$desktop_resolved" "$asset" "$work"
        tar -xzf "$work/$asset" -C "$work"
        (cd "$work/keysharp-desktop-${desktop_resolved}-${arch_tag}" \
            && sh ./install.sh --skip-if-compatible)
    fi
    asset="keysharp-${keysharp_resolved}-${arch_tag}.tar.gz"
    download_verified "$KEYSHARP_REPOSITORY" "v$keysharp_resolved" "$asset" "$work"
    tar -xzf "$work/$asset" -C "$work"
    (cd "$work/keysharp-${arch_tag}" && sh ./install.sh)
fi

printf '%s\n' "Keysharp $keysharp_resolved is installed. Run 'keysharp --version' to confirm."
