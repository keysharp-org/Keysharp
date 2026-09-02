#!/usr/bin/env bash
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
downloads="$(grep -c 'curl -fsSL --proto' "${SETUP}")"
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

echo "Keysharp setup resolution checks passed."
