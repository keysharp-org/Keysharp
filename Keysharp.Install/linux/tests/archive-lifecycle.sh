#!/usr/bin/env bash
# Packaging functions run against a fake publish tree; all system paths are redirected.
# shellcheck disable=SC2034,SC2154,SC2317
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PACKAGER="${REPOSITORY_ROOT}/Keysharp.Install/package-linux.sh"
temporary="$(mktemp -d)"
[[ "${temporary}" == /* && "${temporary}" != / && -d "${temporary}" ]] \
  || { echo 'Expected an absolute temporary directory below the filesystem root.' >&2; exit 1; }
trap 'rm -rf -- "${temporary}"' EXIT HUP INT TERM

fail() {
  echo "archive-lifecycle: $*" >&2
  exit 1
}

assert_absent() {
  [[ ! -e "$1" && ! -L "$1" ]] || fail "unexpected remaining path: $1"
}

assert_same() {
  cmp -s "$1" "$2" || fail "files differ: $1 and $2"
}

assert_license_link() {
  local path="$1" target="$2"
  [[ -L "${path}" && "$(readlink -- "${path}")" == "${target}" ]] \
    || fail "expected license symlink: ${path} -> ${target}"
  assert_same "${REPOSITORY_ROOT}/license.txt" "${path}"
}

# Exercise the real staging, tarball and Debian layout functions without a build.
sed -n '/^restore() {/,/^case "${mode}" in/p' "${PACKAGER}" | sed '$d' \
  > "${temporary}/package-functions.sh"
(
  ROOT="${REPOSITORY_ROOT}"
  ASSETS_DIR="${ROOT}/Keysharp.Install/linux"
  DIST_DIR="${temporary}/dist"
  PUBLISH_DIR="${DIST_DIR}/publish/linux-x64"
  STAGING_DIR="${DIST_DIR}/staging/linux-x64"
  PKG_NAME=keysharp-linux-x64
  PKG_DIR="${STAGING_DIR}/${PKG_NAME}"
  APP_DIR="${PKG_DIR}/app"
  RID=linux-x64
  ETO_DIR=
  # shellcheck source=/dev/null
  source "${temporary}/package-functions.sh"
  publish() {
    mkdir -p "${PUBLISH_DIR}/Keysharp" "${PUBLISH_DIR}/Keyview"
    printf 'fake host\n' > "${PUBLISH_DIR}/Keysharp/Keysharp"
    printf 'fake viewer\n' > "${PUBLISH_DIR}/Keyview/Keyview"
    printf 'fake compiled dash\n' > "${PUBLISH_DIR}/Keysharp/Keysharp.cks"
    cp "${ROOT}/license.txt" "${PUBLISH_DIR}/Keysharp/license.txt"
  }
  stage
  assert_license_link "${APP_DIR}/license.txt" ../doc/license.txt
  build_tarball
  assert_same "${ROOT}/README.md" "${PKG_DIR}/doc/README.md"
  assert_same "${ROOT}/license.txt" "${PKG_DIR}/doc/license.txt"
  install_tree "${temporary}/debian"
  assert_same "${ROOT}/README.md" "${temporary}/debian/usr/share/doc/keysharp/README.md"
  assert_same "${ROOT}/license.txt" "${temporary}/debian/usr/share/doc/keysharp/copyright"
  assert_license_link "${temporary}/debian/usr/lib/keysharp/license.txt" ../../share/doc/keysharp/copyright
)
mkdir -p "${temporary}/archive"
tar -xzf "${temporary}/dist/keysharp-linux-x64.tar.gz" -C "${temporary}/archive"
archive="${temporary}/archive/keysharp-linux-x64"
assert_same "${REPOSITORY_ROOT}/README.md" "${archive}/doc/README.md"
assert_same "${REPOSITORY_ROOT}/license.txt" "${archive}/doc/license.txt"
assert_license_link "${archive}/app/license.txt" ../doc/license.txt

mkdir -p "${temporary}/stubs"
cat > "${temporary}/stubs/dotnet" <<'EOF'
#!/usr/bin/env bash
[[ "$*" == --list-runtimes ]] || exit 1
echo 'Microsoft.NETCore.App 10.0.0 [fixture]'
EOF
for name in dpkg-query rpm pacman apt-get dnf yum zypper; do
  printf '#!/usr/bin/env bash\nexit 1\n' > "${temporary}/stubs/${name}"
done
for name in pkill chown udevadm update-desktop-database update-mime-database gtk-update-icon-cache; do
  cat > "${temporary}/stubs/${name}" <<'EOF'
#!/usr/bin/env bash
printf '%s %s\n' "${0##*/}" "$*" >> "${COMMAND_LOG}"
EOF
done
chmod 0755 "${temporary}/stubs/"*
TEST_PATH="${temporary}/stubs:${PATH}"
export TEST_PATH

prepare_case() {
  local name="$1" uid="$2" script
  case_dir="${temporary}/${name}"
  mkdir -p "${case_dir}/payload" "${case_dir}/system" "${case_dir}/home"
  cp -a "${archive}/." "${case_dir}/payload/"
  for script in install.sh uninstall.sh component-notice.sh; do
    sed \
      -e 's|${EUID}|${TEST_EUID}|g' \
      -e 's|^  PATH=.*|  PATH="${TEST_PATH}"|' \
      -e "s|/usr/|${case_dir}/system/usr/|g" \
      -e "s|/etc/|${case_dir}/system/etc/|g" \
      "${archive}/${script}" > "${case_dir}/payload/${script}"
  done
  PREFIX="${case_dir}/custom-prefix"
  HOME="${case_dir}/home"
  XDG_DATA_HOME="${case_dir}/custom-xdg"
  TEST_EUID="${uid}"
  COMMAND_LOG="${case_dir}/commands.log"
  export PREFIX HOME XDG_DATA_HOME TEST_EUID COMMAND_LOG
  docs="${PREFIX}/share/doc/keysharp"
  rule="${case_dir}/system/etc/udev/rules.d/70-keysharp-i2c-uaccess.rules"
  INSTALL_DEPS=false PATH="${TEST_PATH}" bash "${case_dir}/payload/install.sh" \
    > "${case_dir}/install.log" 2>&1 \
    || { cat "${case_dir}/install.log" >&2; fail "${name}: install failed"; }
  assert_same "${archive}/doc/README.md" "${docs}/README.md"
  assert_same "${archive}/doc/license.txt" "${docs}/license.txt"
  assert_same "${archive}/70-keysharp-i2c-uaccess.rules" "${docs}/70-keysharp-i2c-uaccess.rules"
  [[ -x "${docs}/uninstall.sh" ]] || fail "${name}: installed uninstaller is not executable"
  assert_license_link "${PREFIX}/lib/keysharp/license.txt" ../../share/doc/keysharp/license.txt
  assert_absent "${PREFIX}/README.md"
  assert_absent "${PREFIX}/license.txt"
}

uninstall_case() {
  local source="$1"
  PATH="${TEST_PATH}" bash "${source}" > "${case_dir}/uninstall.log" 2>&1 \
    || { cat "${case_dir}/uninstall.log" >&2; fail "uninstall failed: ${source}"; }
  assert_absent "${PREFIX}/lib/keysharp"
  assert_absent "${PREFIX}/bin/keysharp"
  assert_absent "${PREFIX}/bin/keyview"
  local name
  for name in uninstall.sh README.md license.txt 70-keysharp-i2c-uaccess.rules; do
    assert_absent "${docs}/${name}"
  done
}

prepare_case root-installed 0
assert_same "${archive}/70-keysharp-i2c-uaccess.rules" "${rule}"
uninstall_case "${docs}/uninstall.sh"
assert_absent "${rule}"
assert_absent "${docs}"
grep -Fq 'udevadm control --reload-rules' "${COMMAND_LOG}" \
  || fail "root lifecycle did not refresh the redirected udev rule"

prepare_case root-modified 0
printf '# local customization\n' >> "${rule}"
cp "${rule}" "${case_dir}/expected.rules"
printf 'unrelated documentation\n' > "${docs}/local-notes.txt"
uninstall_case "${docs}/uninstall.sh"
assert_same "${case_dir}/expected.rules" "${rule}"
[[ "$(cat "${docs}/local-notes.txt")" == 'unrelated documentation' ]] \
  || fail "unrelated documentation was changed"

prepare_case root-symlink 0
mv "${rule}" "${case_dir}/symlink-target.rules"
ln -s "${case_dir}/symlink-target.rules" "${rule}"
[[ -L "${rule}" ]] || fail "native symlinks are required; run this fixture on Linux"
uninstall_case "${docs}/uninstall.sh"
[[ -L "${rule}" ]] || fail "the custom udev symlink was removed"
assert_same "${archive}/70-keysharp-i2c-uaccess.rules" "${case_dir}/symlink-target.rules"
assert_absent "${docs}"

prepare_case root-archive 0
uninstall_case "${case_dir}/payload/uninstall.sh"
assert_absent "${rule}"
assert_absent "${docs}"

prepare_case user-installed 1000
[[ -f "${XDG_DATA_HOME}/applications/keysharp.desktop" ]] \
  || fail "user install ignored XDG_DATA_HOME"
[[ -f "${XDG_DATA_HOME}/mime/packages/keysharp.xml" ]] \
  || fail "user MIME registration ignored XDG_DATA_HOME"
assert_absent "${case_dir}/system/usr/share/applications/keysharp.desktop"
assert_absent "${rule}"
uninstall_case "${docs}/uninstall.sh"
assert_absent "${docs}"
assert_absent "${XDG_DATA_HOME}/applications/keysharp.desktop"
assert_absent "${XDG_DATA_HOME}/mime/packages/keysharp.xml"

echo 'Keysharp archive lifecycle checks passed.'
