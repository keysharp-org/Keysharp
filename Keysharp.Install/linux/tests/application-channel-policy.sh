#!/usr/bin/env bash
# Source fixtures intentionally inspect literal expressions and consume globals.
# shellcheck disable=SC2016,SC2034
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
INSTALLER="${REPOSITORY_ROOT}/Keysharp.Install/linux/install.sh"
UNINSTALLER="${REPOSITORY_ROOT}/Keysharp.Install/linux/uninstall.sh"
PACKAGER="${REPOSITORY_ROOT}/Keysharp.Install/package-linux.sh"
PREINST="${REPOSITORY_ROOT}/Keysharp.Install/linux/debian/preinst"

fail() {
  echo "application-channel-policy: $*" >&2
  exit 1
}

require_literal() {
  local file="$1"
  local literal="$2"
  grep -Fq -- "${literal}" "${file}" \
    || fail "${file} is missing: ${literal}"
}

preflight_line="$(grep -n '^check_system_channel_conflict$' "${INSTALLER}" | cut -d: -f1)"
dependency_line="$(grep -n '^  install_deps$' "${INSTALLER}" | cut -d: -f1)"
[[ -n "${preflight_line}" && -n "${dependency_line}" ]] \
  || fail "could not locate the tar install lifecycle boundaries"
(( preflight_line < dependency_line )) \
  || fail "the tar channel preflight runs after a dependency/component mutation"

for literal in \
    "dpkg-query -W" \
    "rpm -q keysharp" \
    "pacman -Q keysharp" \
    "/usr/lib/keysharp" \
    "/usr/bin/keysharp" \
    "/usr/bin/keyview"; do
  require_literal "${INSTALLER}" "${literal}"
done

require_literal "${PACKAGER}" 'install -m 0755 "${ASSETS_DIR}/debian/preinst" "$1"'
require_literal "${PACKAGER}" 'write_deb_preinst "${debian_dir}/preinst"'
require_literal "${PACKAGER}" '"${debian_dir}/preinst" "${debian_dir}/postinst"'
require_literal "${PACKAGER}" '-p:PublishDir="${PUBLISH_DIR}/${project}/"'
require_literal "${PACKAGER}" '${INPUT_CLIENT_ABI_PACKAGE}'
require_literal "${PACKAGER}" '${DESKTOP_CLIENT_ABI_PACKAGE}'

for literal in \
    'remove_shared_integration_file "${DESKTOP_DIR}/keyview.desktop"' \
    'remove_shared_integration_file "${DESKTOP_DIR}/keysharp.desktop"' \
    'remove_shared_integration_file "${MIME_DIR}/keysharp.xml"' \
    'remove_shared_integration_file "${ICON_DIR}/keysharp.png"' \
    'path_is_package_managed "${path}"' \
    'sudo apt-get install --reinstall keysharp'; do
  require_literal "${UNINSTALLER}" "${literal}"
done

temporary="$(mktemp -d)"
trap 'rm -rf -- "${temporary}"' EXIT HUP INT TERM

control_function="${temporary}/write-deb-control.sh"
sed -n '/^write_deb_control() {$/,/^}$/p' "${PACKAGER}" \
  > "${control_function}"
# shellcheck source=/dev/null
source "${control_function}"
# The packager declares the two capabilities; read them from it so a rename there
# is compared against the literals below rather than silently agreeing with them.
eval "$(grep -E '^(INPUT|DESKTOP)_CLIENT_ABI_PACKAGE=' "${PACKAGER}")"
DEB_PKG_NAME=keysharp
VERSION=0.0.0.17
DEB_ARCH=amd64
control_root="${temporary}/control-package"
mkdir -p "${control_root}/DEBIAN"
write_deb_control "${control_root}/DEBIAN/control"
dpkg-deb --build --root-owner-group "${control_root}" \
  "${temporary}/control-package.deb" >/dev/null
recommends="$(dpkg-deb -f "${temporary}/control-package.deb" Recommends)"
expected_recommends="keysharp-input-client-abi-0 (>= 0.2), keysharp-desktop-client-abi-0 (>= 0.8)"
[[ "${recommends}" == "${expected_recommends}" ]] \
  || fail "generated Debian Recommends is not the two exact client ABIs: ${recommends}"

portable_app="${temporary}/usr/local/lib/keysharp"
portable_keysharp="${temporary}/usr/local/bin/keysharp"
portable_keyview="${temporary}/usr/local/bin/keyview"
package_app="${temporary}/usr/lib/keysharp"
package_keysharp="${temporary}/usr/bin/keysharp"
package_keyview="${temporary}/usr/bin/keyview"
test_preinst="${temporary}/preinst"
staged_preinst="${temporary}/package/DEBIAN/preinst"

mkdir -p "$(dirname "${portable_app}")" "$(dirname "${portable_keysharp}")" \
  "$(dirname "${package_app}")" "$(dirname "${package_keysharp}")"
sed \
  -e "s|/usr/local/lib/keysharp|${portable_app}|g" \
  -e "s|/usr/local/bin/keysharp|${portable_keysharp}|g" \
  -e "s|/usr/local/bin/keyview|${portable_keyview}|g" \
  -e "s|/usr/lib/keysharp|${package_app}|g" \
  -e "s|/usr/bin/keysharp|${package_keysharp}|g" \
  -e "s|/usr/bin/keyview|${package_keyview}|g" \
  "${PREINST}" > "${test_preinst}"
chmod 0755 "${test_preinst}"

"${test_preinst}" install

# Exact package aliases are benign even before the package targets exist.
ln -s "${package_app}" "${portable_app}"
ln -s "${package_keysharp}" "${portable_keysharp}"
ln -s "${package_app}/Keyview" "${portable_keyview}"
"${test_preinst}" install
rm -f -- "${portable_app}" "${portable_keysharp}" "${portable_keyview}"

# An unresolved link is not benign merely because it is a link.
ln -s "${temporary}/unrelated/keysharp" "${portable_keysharp}"
if "${test_preinst}" install >/dev/null 2>&1; then
  fail "preinst accepted an unsafe dangling /usr/local alias"
fi
rm -f -- "${portable_keysharp}"

# Existing aliases may resolve through the package's normal bin links.
mkdir -p "${package_app}"
: > "${package_app}/Keysharp"
: > "${package_app}/Keyview"
ln -s ../lib/keysharp/Keysharp "${package_keysharp}"
ln -s ../lib/keysharp/Keyview "${package_keyview}"
ln -s "${package_app}" "${portable_app}"
ln -s "${package_keysharp}" "${portable_keysharp}"
ln -s "${package_keyview}" "${portable_keyview}"
"${test_preinst}" install
rm -f -- "${portable_app}" "${portable_keysharp}" "${portable_keyview}"

mkdir -p "${portable_app}"
if "${test_preinst}" install >/dev/null 2>&1; then
  fail "preinst accepted a distinct portable application on fresh install"
fi
upgrade_output="$("${test_preinst}" upgrade 1.0.0 2>&1)" \
  || fail "preinst blocked an upgrade over a pre-existing layered state"
grep -Fq 'upgrade will continue' <<< "${upgrade_output}" \
  || fail "preinst upgrade did not print recovery guidance"
"${test_preinst}" abort-upgrade 1.0.0

mkdir -p "$(dirname "${staged_preinst}")"
install -m 0755 "${PREINST}" "${staged_preinst}"
cmp -s "${PREINST}" "${staged_preinst}" \
  || fail "staged Debian preinst differs from its source template"
[[ "$(stat -c '%a' "${staged_preinst}")" == 755 ]] \
  || fail "staged Debian preinst is not executable"

echo "Keysharp application-channel lifecycle checks passed."
