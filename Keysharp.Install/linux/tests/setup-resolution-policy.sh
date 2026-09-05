#!/usr/bin/env bash
# Functions and their globals are sourced from the installer for isolated fixtures.
# shellcheck disable=SC2034,SC2154,SC2317
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SETUP="${REPOSITORY_ROOT}/Keysharp.Install/linux/keysharp-linux-setup.sh"

fail() {
  echo "setup-resolution-policy: $*" >&2
  exit 1
}

temporary="$(mktemp -d)"
trap 'rm -rf -- "${temporary}"' EXIT HUP INT TERM

# The setup script must stay a resolver. Anything that reads a bundled payload or
# a checksum recorded in this repository would put component release cadence back
# inside Keysharp's.
for forbidden in \
    'components/manifest.tsv' \
    'component-versions.conf' \
    'SHA256_LINUX' \
    '--dependency-dir'; do
  if grep -Fq -- "${forbidden}" "${SETUP}"; then
    fail "the setup script references a bundled component payload: ${forbidden}"
  fi
done

for repository in \
    'keysharp-org/Keysharp' \
    'keysharp-org/keysharp-input' \
    'keysharp-org/keysharp-desktop'; do
  grep -Fq -- "${repository}" "${SETUP}" \
    || fail "the setup script does not resolve ${repository}"
done

# Every download must be verified, so the only curl that writes an artifact is
# the one download_verified performs.
downloads="$(grep -c 'curl -fsSL --connect-timeout' "${SETUP}")"
[[ "${downloads}" -eq 2 ]] \
  || fail "expected exactly the asset and SHA256SUMS downloads, found ${downloads}"

functions="${temporary}/functions.sh"
sed '/^while \[ "$#" -gt 0 \]; do/,$d' "${SETUP}" > "${functions}"
# shellcheck source=/dev/null
source "${functions}"

payload="${temporary}/keysharp-0.0.0.17-linux-x64.tar.gz"
printf 'payload\n' > "${payload}"
asset="$(basename "${payload}")"
sums="${temporary}/SHA256SUMS"
(cd "${temporary}" && sha256sum "${asset}") > "${sums}"

verify_checksum "${temporary}" "${asset}" "${sums}" \
  || fail "a correct checksum was rejected"

printf 'tampered\n' > "${payload}"
if verify_checksum "${temporary}" "${asset}" "${sums}" 2>/dev/null; then
  fail "a tampered asset passed verification"
fi

(cd "${temporary}" && sha256sum "${asset}") > "${sums}"
if verify_checksum "${temporary}" "keysharp-0.0.0.17-linux-arm64.tar.gz" \
    "${sums}" 2>/dev/null; then
  fail "an asset absent from SHA256SUMS passed verification"
fi

# A release line for a different asset must not satisfy a lookup by prefix.
printf '%s  other-%s\n' "$(sha256sum "${payload}" | cut -d' ' -f1)" "${asset}" \
  > "${sums}"
if verify_checksum "${temporary}" "${asset}" "${sums}" 2>/dev/null; then
  fail "a checksum line for a different asset was accepted"
fi

machine_arch() {
  # detect_arch reads uname, so exercise it through a stub on PATH. The stub goes
  # on PATH after sourcing, because the script hardens PATH at the top.
  local machine="$1"
  local stub="${temporary}/stub-${machine}"
  mkdir -p "${stub}"
  printf '#!/bin/sh\nprintf "%%s\\n" %s\n' "${machine}" > "${stub}/uname"
  chmod 0755 "${stub}/uname"
  bash -c "
    source '${functions}'
    PATH='${stub}':\"\${PATH}\"
    detect_arch
    printf '%s %s\n' \"\${arch_tag}\" \"\${deb_arch}\"
  "
}

[[ "$(machine_arch x86_64)" == "linux-x64 amd64" ]] \
  || fail "x86_64 did not map to the x64 release assets"
[[ "$(machine_arch aarch64)" == "linux-arm64 arm64" ]] \
  || fail "aarch64 did not map to the arm64 release assets"
if machine_arch riscv64 >/dev/null 2>&1; then
  fail "an unsupported architecture was accepted"
fi

channel=auto
detect_channel
[[ "${channel}" == deb || "${channel}" == tar ]] \
  || fail "channel detection produced ${channel}"
channel=tar
detect_channel
[[ "${channel}" == tar ]] || fail "an explicit channel was overridden"

# A pinned version must not reach the network.
[[ "$(resolve_version keysharp-org/Keysharp 0.0.0.17)" == "0.0.0.17" ]] \
  || fail "a pinned version was not returned verbatim"

arch_tag=linux-x64
deb_arch=amd64
[[ "$(release_asset keysharp deb 0.0.0.17)" == keysharp-0.0.0.17-linux-x64.deb ]] \
  || fail "Keysharp Debian asset does not match release.yml"
[[ "$(release_asset keysharp-input deb 0.2.0)" == keysharp-input_0.2.0_amd64.deb ]] \
  || fail "input Debian asset name is incorrect"
[[ "$(release_asset keysharp-desktop tar 0.2.0)" == keysharp-desktop-0.2.0-linux-x64.tar.gz ]] \
  || fail "desktop archive asset name is incorrect"
# shellcheck disable=SC2016 # Match the workflow's literal variable expressions.
grep -Fq 'dist/release-assets/keysharp-${VERSION}-${RID}.deb' \
  "${REPOSITORY_ROOT}/.github/workflows/release.yml" \
  || fail "Keysharp release asset convention changed; update the setup contract"

component_health=ready
component_compatible=true
upgrade_components=false
if component_needs_install false; then fail "healthy compatible component should be reused"; fi
component_needs_install true || fail "an explicit component version must be honored"
upgrade_components=true
component_needs_install false || fail "component upgrades must not be skipped"
upgrade_components=false
component_health=inactive-socket
component_needs_install false || fail "ABI compatibility must not hide a broken service"

# The installed path is independent of the caller's PATH. All system interaction
# is stubbed: this fixture neither connects to a broker nor changes a service.
fixture="${temporary}/component"
mkdir -p "${fixture}/bin"
cat > "${fixture}/bin/keysharp-input" <<'EOF'
#!/bin/sh
printf '%s\n' product_version=0.2.0 client_abi_major=0 client_abi_minor=1
EOF
chmod 0755 "${fixture}/bin/keysharp-input"
(
  is_protected_path() { return 0; }
  component_candidates() { printf '%s\n' "${fixture}/bin/keysharp-input"; }
  systemctl() {
    case "$1" in
      show) echo loaded ;;
      is-active) return 0 ;;
      is-failed) return 1 ;;
    esac
  }
  inspect_component keysharp-input 0 1
  [[ "$component_path" == "${fixture}/bin/keysharp-input" && "$component_health" == ready ]] \
    || fail "an explicit installed path outside PATH was not discovered"
  inspect_component keysharp-input 0 2
  [[ "$component_compatible" == false && "$component_health" == incompatible-abi ]] \
    || fail "a client below the minimum ABI minor was accepted"
  component_info=$'client_abi_minor=1\nclient_abi_minor=99'
  [[ -z "$(info_field client_abi_minor)" ]] || fail "duplicate ABI fields were accepted"
)
[[ "$(component_candidates keysharp-input)" == *'/usr/local/bin/keysharp-input'* ]] \
  || fail "portable component discovery is missing"

abi_compatible 0 8 0 8 || fail "matching ABI was rejected"
abi_compatible 0 9 0 8 || fail "newer additive ABI was rejected"
if abi_compatible 0 7 0 8 || abi_compatible 1 8 0 8; then
  fail "incompatible downloaded ABI was accepted"
fi
(
  dpkg-deb() { echo 'other-package, keysharp-desktop-client-abi-0 (= 0.8)'; }
  verify_deb_abi fixture.deb keysharp-desktop 0 8 || fail "versioned Debian ABI was rejected"
  dpkg-deb() { echo 'keysharp-desktop-client-abi-0'; }
  if verify_deb_abi fixture.deb keysharp-desktop 0 8 2>/dev/null; then
    fail "unversioned Debian ABI cannot promise the required minor"
  fi
  if verify_archive_abi "$fixture" keysharp-input 0 2 2>/dev/null; then
    fail "archive with an older ABI was accepted"
  fi
)

echo "Keysharp setup resolution checks passed."
