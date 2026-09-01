#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
INSTALLER="${REPOSITORY_ROOT}/Keysharp.Install/linux/install-deb-bundle.sh"
PACKAGER="${REPOSITORY_ROOT}/Keysharp.Install/package-linux-deb-bundle.sh"
LOCK="${REPOSITORY_ROOT}/Keysharp.Install/linux/component-versions.conf"

fail() {
  echo "deb-bundle-policy: $*" >&2
  exit 1
}

for command in dpkg dpkg-deb sha256sum; do
  command -v "${command}" >/dev/null 2>&1 \
    || fail "${command} is required"
done

# shellcheck source=/dev/null
source "${LOCK}"
temporary="$(mktemp -d)"
trap 'rm -rf -- "${temporary}"' EXIT HUP INT TERM
bundle="${temporary}/bundle"
mkdir -p "${bundle}"
install -m 0755 "${INSTALLER}" "${bundle}/install.sh"
cat > "${bundle}/component-probe.sh" <<'EOF'
#!/usr/bin/env bash
exit 1
EOF
chmod 0755 "${bundle}/component-probe.sh"

host_arch="$(dpkg --print-architecture)"
case "${host_arch}" in
  amd64) other_arch=arm64 ;;
  arm64) other_arch=amd64 ;;
  *) fail "unsupported test architecture: ${host_arch}" ;;
esac
keysharp_version=9.9.9
keysharp_deb="keysharp_${keysharp_version}_${host_arch}.deb"
input_deb="keysharp-input_${KEYSHARP_INPUT_VERSION}_${host_arch}.deb"
desktop_deb="keysharp-desktop_${KEYSHARP_DESKTOP_VERSION}_${host_arch}.deb"

make_deb() {
  local output="$1"
  local package="$2"
  local version="$3"
  local architecture="$4"
  local provides="$5"
  local recommends="$6"
  local root
  root="$(mktemp -d "${temporary}/package.XXXXXXXXXX")"
  mkdir -p "${root}/DEBIAN"
  {
    printf 'Package: %s\n' "${package}"
    printf 'Version: %s\n' "${version}"
    printf 'Architecture: %s\n' "${architecture}"
    printf 'Maintainer: Bundle Test <test@example.invalid>\n'
    [[ -z "${provides}" ]] || printf 'Provides: %s\n' "${provides}"
    [[ -z "${recommends}" ]] || printf 'Recommends: %s\n' "${recommends}"
    printf 'Description: bundle policy fixture\n'
  } > "${root}/DEBIAN/control"
  dpkg-deb --build --root-owner-group "${root}" "${output}" >/dev/null
}

write_manifest() {
  local architecture="$1"
  local main_filename="$2"
  local input_filename="$3"
  local desktop_filename="$4"
  cat > "${bundle}/bundle.tsv" <<EOF
# role	package	version	architecture	client-abi-package	filename
keysharp	keysharp	${keysharp_version}	${architecture}	-	${main_filename}
input	keysharp-input	${KEYSHARP_INPUT_VERSION}	${architecture}	${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}	${input_filename}
desktop	keysharp-desktop	${KEYSHARP_DESKTOP_VERSION}	${architecture}	${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}	${desktop_filename}
EOF
}

write_sums() {
  (
    cd "${bundle}"
    sha256sum bundle.tsv component-probe.sh \
      "$(basename -- "$1")" "$(basename -- "$2")" "$(basename -- "$3")" \
      > SHA256SUMS
  )
}

recommends="${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}, ${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}"
make_deb "${bundle}/${keysharp_deb}" keysharp "${keysharp_version}" \
  "${host_arch}" "" "${recommends}"
make_deb "${bundle}/${input_deb}" keysharp-input "${KEYSHARP_INPUT_VERSION}" \
  "${host_arch}" "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}" ""
make_deb "${bundle}/${desktop_deb}" keysharp-desktop "${KEYSHARP_DESKTOP_VERSION}" \
  "${host_arch}" "${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}" ""
write_manifest "${host_arch}" "${keysharp_deb}" "${input_deb}" "${desktop_deb}"
write_sums "${keysharp_deb}" "${input_deb}" "${desktop_deb}"

bash "${bundle}/install.sh" --verify-only >/dev/null \
  || fail "installer rejected a valid bundle"

case "${host_arch}" in
  amd64) rid=linux-x64 ;;
  arm64) rid=linux-arm64 ;;
esac
packaged_dist="${temporary}/packaged-dist"
VERSION="${keysharp_version}" RID="${rid}" KEYSHARP_DIST_DIR="${packaged_dist}" \
  bash "${PACKAGER}" --keysharp-deb "${bundle}/${keysharp_deb}" \
    --dependency-dir "${bundle}" >/dev/null \
  || fail "bundle packager rejected valid local Debian packages"
packaged_archive="${packaged_dist}/keysharp-${keysharp_version}-${rid}-deb-bundle.tar.gz"
[[ -f "${packaged_archive}" ]] || fail "bundle packager did not produce its archive"
mkdir -p "${temporary}/packaged"
tar -xzf "${packaged_archive}" -C "${temporary}/packaged"
bash "${temporary}/packaged/keysharp-${keysharp_version}-${rid}-deb-bundle/install.sh" \
  --verify-only >/dev/null \
  || fail "generated bundle did not pass its own verification"

cp "${bundle}/${input_deb}" "${temporary}/valid-input.deb"
printf 'tampered' >> "${bundle}/${input_deb}"
if bash "${bundle}/install.sh" --verify-only >/dev/null 2>&1; then
  fail "installer accepted a checksum-tampered helper package"
fi
cp "${temporary}/valid-input.deb" "${bundle}/${input_deb}"

make_deb "${bundle}/${input_deb}" keysharp-input 99.0.0 \
  "${host_arch}" "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}" ""
write_sums "${keysharp_deb}" "${input_deb}" "${desktop_deb}"
if bash "${bundle}/install.sh" --verify-only >/dev/null 2>&1; then
  fail "installer accepted helper package metadata that disagrees with bundle.tsv"
fi
cp "${temporary}/valid-input.deb" "${bundle}/${input_deb}"
write_sums "${keysharp_deb}" "${input_deb}" "${desktop_deb}"

wrong_keysharp="keysharp_${keysharp_version}_${other_arch}.deb"
wrong_input="keysharp-input_${KEYSHARP_INPUT_VERSION}_${other_arch}.deb"
wrong_desktop="keysharp-desktop_${KEYSHARP_DESKTOP_VERSION}_${other_arch}.deb"
make_deb "${bundle}/${wrong_keysharp}" keysharp "${keysharp_version}" \
  "${other_arch}" "" "${recommends}"
make_deb "${bundle}/${wrong_input}" keysharp-input "${KEYSHARP_INPUT_VERSION}" \
  "${other_arch}" "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}" ""
make_deb "${bundle}/${wrong_desktop}" keysharp-desktop "${KEYSHARP_DESKTOP_VERSION}" \
  "${other_arch}" "${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}" ""
write_manifest "${other_arch}" "${wrong_keysharp}" "${wrong_input}" "${wrong_desktop}"
write_sums "${wrong_keysharp}" "${wrong_input}" "${wrong_desktop}"
if bash "${bundle}/install.sh" --verify-only >/dev/null 2>&1; then
  fail "installer accepted a bundle for the wrong architecture"
fi

# Source the installer to exercise selection without invoking apt or probing the
# real machine. Product name/version never satisfies compatibility; only an
# installed client-ABI Provides token or the protected standalone probe may omit a .deb.
# shellcheck source=/dev/null
source "${INSTALLER}"
SNAPSHOT=/snapshot
INPUT_PACKAGE=keysharp-input
INPUT_VERSION="${KEYSHARP_INPUT_VERSION}"
INPUT_CAPABILITY="${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}"
INPUT_DEB=input.deb
DESKTOP_PACKAGE=keysharp-desktop
DESKTOP_VERSION="${KEYSHARP_DESKTOP_VERSION}"
DESKTOP_CAPABILITY="${KEYSHARP_DESKTOP_DEBIAN_CLIENT_PACKAGE}"
DESKTOP_DEB=desktop.deb
KEYSHARP_DEB=keysharp.deb

query_provider_records() {
  printf 'ii \t%s\n' "${KEYSHARP_INPUT_DEBIAN_CLIENT_PACKAGE}"
}
standalone_provider_satisfies() { return 1; }
debian_package_installed() { return 1; }
debian_package_is_auto() { return 1; }
portable_layer_present() { return 1; }
select_install_debs > "${temporary}/selection.out"
[[ "${#SELECTED_DEBS[@]}" -eq 2 \
  && "${SELECTED_DEBS[0]}" == /snapshot/desktop.deb \
  && "${SELECTED_DEBS[1]}" == /snapshot/keysharp.deb \
  && "${#APT_AUTO_PACKAGES[@]}" -eq 1 \
  && "${APT_AUTO_PACKAGES[0]}" == keysharp-desktop \
  && "${#APT_MANUAL_PACKAGES[@]}" -eq 0 ]] \
  || fail "an installed exact input provider was not omitted from the plan"

query_provider_records() {
  # An installed product without the exact ABI Provides token is incompatible,
  # regardless of its independently versioned product release.
  printf 'ii \tother-capability\n'
}
standalone_provider_satisfies() {
  [[ "$1" == keysharp-desktop ]]
}
debian_package_installed() { return 1; }
portable_layer_present() { return 1; }
select_install_debs > "${temporary}/selection-standalone.out"
[[ "${#SELECTED_DEBS[@]}" -eq 2 \
  && "${SELECTED_DEBS[0]}" == /snapshot/input.deb \
  && "${SELECTED_DEBS[1]}" == /snapshot/keysharp.deb ]] \
  || fail "the plan did not require the exact ABI or preserve a compatible standalone helper"

query_provider_records() { return 0; }
standalone_provider_satisfies() { return 1; }
portable_layer_present() { return 1; }
debian_package_installed() { return 0; }
debian_package_is_auto() { [[ "$1" == keysharp-input ]]; }
select_install_debs > "${temporary}/selection-existing.out"
[[ "${#SELECTED_DEBS[@]}" -eq 3 \
  && "${APT_AUTO_PACKAGES[*]}" == keysharp-input \
  && "${APT_MANUAL_PACKAGES[*]}" == keysharp-desktop ]] \
  || fail "the plan did not preserve pre-existing helper apt states"

debian_package_installed() { return 1; }
select_install_debs > "${temporary}/selection-new.out"
[[ "${APT_AUTO_PACKAGES[*]}" == "keysharp-input keysharp-desktop" \
  && "${#APT_MANUAL_PACKAGES[@]}" -eq 0 ]] \
  || fail "new bundled helpers were not scheduled for automatic state"

portable_layer_present() { [[ "$1" == keysharp-desktop ]]; }
if select_install_debs > "${temporary}/selection-conflict.out" 2>&1; then
  fail "the plan accepted an incompatible portable layer"
fi

apt_mark_log="${temporary}/apt-mark.log"
apt_mark() { printf '%s\n' "$*" >> "${apt_mark_log}"; }
APT_AUTO_PACKAGES=(keysharp-input)
APT_MANUAL_PACKAGES=(keysharp-desktop)
restore_helper_apt_states
grep -Fxq 'auto keysharp-input' "${apt_mark_log}" \
  || fail "automatic helper state was not restored"
grep -Fxq 'manual keysharp-desktop' "${apt_mark_log}" \
  || fail "manual helper state was not restored"

echo "Keysharp Debian bundle checks passed."
