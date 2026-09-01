#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
INSTALLER="${REPOSITORY_ROOT}/Keysharp.Install/linux/install-components.sh"

fail() {
  echo "component-resource-policy: $*" >&2
  exit 1
}

bash -n "${INSTALLER}" || fail "component installer has invalid shell syntax"

if grep -Eq 'keysharp-inputd|keysharp-desktop serve|installation_resources_match' "${INSTALLER}"; then
  fail "component compatibility must use only the public client ABI"
fi
for literal in \
    'lib${component}.so.${abi}' \
    '${component}-client-abi-${abi}' \
    '--probe-portable-layer' \
    'portable_layer_present_under' \
    'component_resources_present' \
    'client_abi_matches' \
    'leaving it untouched' \
    'package-managed but does not provide client ABI'; do
  grep -Fq -- "${literal}" "${INSTALLER}" \
    || fail "component installer is missing: ${literal}"
done

temporary="$(mktemp -d)"
trap 'rm -rf -- "${temporary}"' EXIT HUP INT TERM
functions_file="${temporary}/functions.sh"
sed -n '/^component_valid() {$/,/^if \[\[ -n "${PROBE_COMPONENT}" \]\]; then$/p' \
  "${INSTALLER}" | sed '$d' > "${functions_file}"
MINIMUM_CLIENT_ABI_MINOR=1
# shellcheck source=/dev/null
source "${functions_file}"

for function_name in trusted_file validate_archive_paths component_compatible \
    component_resources_present client_abi_matches; do
  declare -F "${function_name}" >/dev/null \
    || fail "could not load ${function_name} from the component installer"
done

cat > "${temporary}/compatible-info" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' client_abi_major=0 client_abi_minor=1
EOF
chmod 0755 "${temporary}/compatible-info"
client_abi_matches "${temporary}/compatible-info" 0 \
  || fail "a compatible linked client ABI was rejected"
if client_abi_matches "${temporary}/compatible-info" 1; then
  fail "an incompatible linked client ABI major was accepted"
fi

portable_root="${temporary}/portable-root"
mkdir -p "${portable_root}/usr/local/lib" "${portable_root}/usr/lib"
printf 'portable\n' > \
  "${portable_root}/usr/local/lib/libkeysharp-input.so.0"
portable_layer_present_under keysharp-input "${portable_root}" \
  || fail "a stale portable input client library was not detected"
rm -f -- "${portable_root}/usr/local/lib/libkeysharp-input.so.0"
printf 'packaged\n' > "${portable_root}/usr/lib/libkeysharp-input.so.0"
ln -s ../../lib/libkeysharp-input.so.0 \
  "${portable_root}/usr/local/lib/libkeysharp-input.so.0"
if portable_layer_present_under keysharp-input "${portable_root}"; then
  fail "a benign portable alias to the packaged input library was rejected"
fi

trusted_file /etc/os-release \
  || fail "a protected system file was rejected"
printf 'ordinary\n' > "${temporary}/ordinary-file"
if trusted_file "${temporary}/ordinary-file"; then
  fail "a file below a user-writable directory was accepted as protected"
fi

safe_root="${temporary}/safe/root/lib"
mkdir -p "${safe_root}"
printf 'library\n' > "${safe_root}/libkeysharp-input.so.0.2.0"
ln -s libkeysharp-input.so.0.2.0 "${safe_root}/libkeysharp-input.so.0"
tar -C "${temporary}/safe" -czf "${temporary}/safe.tar.gz" root
validate_archive_paths "${temporary}/safe.tar.gz" \
  || fail "a relative client-library SONAME link was rejected"

unsafe_root="${temporary}/unsafe/root/lib"
mkdir -p "${unsafe_root}"
ln -s /etc/passwd "${unsafe_root}/libkeysharp-input.so.0"
tar -C "${temporary}/unsafe" -czf "${temporary}/unsafe.tar.gz" root
if validate_archive_paths "${temporary}/unsafe.tar.gz"; then
  fail "an absolute archive link target was accepted"
fi

echo "Standalone component ABI compatibility checks passed."
