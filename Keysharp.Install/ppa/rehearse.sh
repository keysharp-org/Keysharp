#!/usr/bin/env bash
# Usage: rehearse.sh DSC SERIES [OUT_DIR]
#
# Builds a source package the way a Launchpad builder does - in a clean ubuntu:SERIES container with
# the build dependencies from the archive, without network, as an unprivileged user and with no
# usable HOME - then asks apt whether the result would install. Launchpad accepts each version once,
# so this is where a broken upload has to be caught. The binary packages land in OUT_DIR.
set -euo pipefail

dsc="$(realpath "${1:?source package .dsc}")"
series="${2:?Ubuntu series}"
out="$(mkdir -p "${3:-rehearsal-${series}}" && cd "${3:-rehearsal-${series}}" && pwd)"
source_dir="$(dirname "${dsc}")"
image="ppa-rehearsal-${series}-$$"
build="$(mktemp -d)"
cleanup() {
  docker image rm -f "${image}" >/dev/null 2>&1 || true
  rm -rf -- "${build}"
}
trap cleanup EXIT

# The builder is a real account, as on Launchpad, but with this machine's uid so it can write the build tree.
container="$(docker create -e BUILD_UID="$(id -u)" -v "${source_dir}:/src:ro" "ubuntu:${series}" bash -euc '
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -qq -y --no-install-recommends dpkg-dev >/dev/null
  DEBIAN_FRONTEND=noninteractive apt-get build-dep -qq -y "/src/$1" >/dev/null
  getent passwd "${BUILD_UID}" >/dev/null || useradd --uid "${BUILD_UID}" --no-create-home builder' \
  rehearse "$(basename "${dsc}")")"
docker start -a "${container}"
docker commit "${container}" "${image}" >/dev/null
docker rm "${container}" >/dev/null

docker run --rm --network none --user "$(id -u):$(id -g)" -e HOME=/nonexistent -e LC_ALL=C.UTF-8 \
  -e DEB_BUILD_OPTIONS="parallel=$(nproc)" -v "${source_dir}:/src:ro" -v "${build}:/build" -w /build \
  "${image}" bash -euc 'dpkg-source -x "/src/$1" source && cd source && dpkg-buildpackage -b -us -uc' \
  rehearse "$(basename "${dsc}")"

cp "${build}"/*.deb "${out}/"
for deb in "${out}"/*.deb; do
  dpkg-deb --info "${deb}"
done
docker run --rm --network none -v "${out}:/debs:ro" "${image}" \
  bash -euc 'apt-get install --simulate /debs/*.deb >/dev/null && echo "apt can install the packages on '"${series}"'."'
