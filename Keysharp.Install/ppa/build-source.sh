#!/usr/bin/env bash
# Builds the unsigned Launchpad source uploads of the committed HEAD, one per Ubuntu series, into
# OUTPUT_DIR. See README.md.
#
# Environment: VERSION, SERIES, PPA_REVISION (the N in <version>-1~<series>N), PPA (owner/name, whose
# upstream tarball for VERSION is reused when it has one), RESTORE_IN (docker restores the NuGet feed
# with each series' SDK in an ubuntu:<series> container; otherwise the local dotnet must be that SDK),
# ETO_CHECKOUT, KPM_CHECKOUT, OUTPUT_DIR.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="${ROOT}/Keysharp.Install/ppa"
VERSION="${VERSION:-$(sed -n 's:.*<KeysharpVersion[^>]*>\(.*\)</KeysharpVersion>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)}"
SERIES="${SERIES:-noble resolute}"
PPA_REVISION="${PPA_REVISION:-1}"
PPA="${PPA:-}"
RESTORE_IN="${RESTORE_IN:-host}"
ETO_CHECKOUT="${ETO_CHECKOUT:-${ROOT}/../Eto}"
KPM_CHECKOUT="${KPM_CHECKOUT:-${ROOT}/../KPM}"
OUTPUT_DIR="${OUTPUT_DIR:-${ROOT}/dist/ppa}"

[[ "${PPA_REVISION}" =~ ^[1-9][0-9]*$ ]] || { echo "PPA_REVISION must be a positive number." >&2; exit 1; }

eto_rev="$(git -C "${ROOT}" show HEAD:flake.lock \
  | python3 -c 'import json, sys; print(json.load(sys.stdin)["nodes"]["eto"]["locked"]["rev"])')"
kpm_tag="$(git -C "${ROOT}" show HEAD:Keysharp.Install/payload/Keysharp.Payload.proj | sed -n 's:.*<KpmVersion[^>]*>\(.*\)</KpmVersion>.*:\1:p' | head -n 1)"
kpm_rev="$(git -C "${KPM_CHECKOUT}" rev-parse --verify --quiet "refs/tags/${kpm_tag}^{commit}")" \
  || { echo "${KPM_CHECKOUT} has no tag ${kpm_tag}, the KPM release Keysharp.Payload.proj pins." >&2; exit 1; }
git -C "${ETO_CHECKOUT}" cat-file -e "${eto_rev}^{commit}" \
  || { echo "${ETO_CHECKOUT} lacks Eto ${eto_rev}, the revision flake.lock pins." >&2; exit 1; }
epoch="$(git -C "${ROOT}" log -1 --format=%ct HEAD)"
maintainer="$(sed -n 's/^Maintainer: //p' "${ROOT}/Keysharp.Install/linux/debian/control")"

work="$(mktemp -d)"
trap 'rm -rf -- "${work}"' EXIT
orig_name="keysharp_${VERSION}.orig.tar.xz"
orig="${OUTPUT_DIR}/${orig_name}"
mkdir -p "${OUTPUT_DIR}"
rm -f -- "${OUTPUT_DIR}"/keysharp_"${VERSION}"[-.]*

restore_feed() {
  local tree="$1" feed="$2" series
  if [[ "${RESTORE_IN}" != docker ]]; then
    bash "${HERE}/restore-feed.sh" "${tree}" "${feed}"
    return
  fi
  for series in ${SERIES}; do
    echo "Restoring the NuGet feed with the dotnet SDK of ${series}..."
    docker run --rm -e OWNER="$(id -u):$(id -g)" -v "${tree}:/src:ro" -v "${feed}:/feed" "ubuntu:${series}" \
      bash -euc 'apt-get update -qq
        DEBIAN_FRONTEND=noninteractive apt-get install -qq -y --no-install-recommends dotnet-sdk-10.0 ca-certificates >/dev/null
        bash /src/Keysharp.Install/ppa/restore-feed.sh /src /feed
        chown -R "${OWNER}" /feed'
  done
}

reused_orig=false
if [[ -n "${PPA}" ]] && url="$(python3 "${HERE}/launchpad.py" orig-url "${PPA}" keysharp "${VERSION}")"; then
  echo "Reusing ${orig_name} from ppa:${PPA}."
  curl -fsSL --retry 3 -o "${orig}" "${url}"
  reused_orig=true
else
  tree="${work}/keysharp-${VERSION}"
  mkdir -p "${tree}/vendor/Eto" "${tree}/vendor/KPM"
  git -C "${ROOT}" archive HEAD | tar -x -C "${tree}"
  git -C "${ETO_CHECKOUT}" archive "${eto_rev}" | tar -x -C "${tree}/vendor/Eto"
  git -C "${KPM_CHECKOUT}" archive "${kpm_rev}" | tar -x -C "${tree}/vendor/KPM"
  mkdir -p "${work}/feed"
  restore_feed "${tree}" "${work}/feed"
  mv "${work}/feed" "${tree}/vendor/nuget"
  tar --sort=name --mtime="@${epoch}" --owner=0 --group=0 --numeric-owner -C "${work}" \
    -cf - "keysharp-${VERSION}" | xz -T0 -6 > "${orig}"
fi

for series in ${SERIES}; do
  debian_version="${VERSION}-1~${series}${PPA_REVISION}"
  dir="${work}/${series}"
  source_dir="${dir}/keysharp-${VERSION}"
  mkdir -p "${dir}"
  ln -s "${orig}" "${dir}/${orig_name}"
  tar -xJf "${orig}" -C "${dir}"
  cp -a "${ROOT}/Keysharp.Install/linux/debian" "${source_dir}/debian"
  chmod 0755 "${source_dir}/debian/rules"
  cat > "${source_dir}/debian/changelog" <<EOF
keysharp (${debian_version}) ${series}; urgency=medium

  * Keysharp ${VERSION} with Eto ${eto_rev:0:12} and KPM ${kpm_tag}.

 -- ${maintainer}  $(date -R -u -d "@${epoch}")
EOF
  # Every upload of a new tarball carries it, since Launchpad may process the series in any order.
  (cd "${source_dir}" && dpkg-buildpackage -S -d -us -uc "$([[ "${reused_orig}" == true ]] && echo -sd || echo -sa)")
  mv "${dir}/keysharp_${debian_version}"* "${OUTPUT_DIR}/"
done
echo "Unsigned source uploads are in ${OUTPUT_DIR}."
