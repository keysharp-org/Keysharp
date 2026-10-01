#!/usr/bin/env bash
# Usage: restore-feed.sh SOURCE_TREE FEED_DIR
#
# Adds to FEED_DIR every NuGet package an offline build of SOURCE_TREE restores, for both Linux
# architectures. Run it with the dotnet SDK of the Ubuntu series the feed is for: that SDK fixes the
# versions of the runtime, apphost and crossgen2 packs it will ask for.
set -euo pipefail

tree="$(cd "${1:?source tree}" && pwd)"
mkdir -p "${2:?feed directory}"
feed="$(cd "$2" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf -- "${work}"' EXIT

# Restore writes obj/ into the tree it restores, so it gets a copy.
cp -a "${tree}" "${work}/src"
export NUGET_PACKAGES="${work}/packages" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
for rid in linux-x64 linux-arm64; do
  RID="${rid}" EtoRoot="${work}/src/vendor/Eto" KEYSHARP_DIST_DIR="${work}/dist" \
    bash "${work}/src/Keysharp.Install/package-linux.sh" --restore
done
dotnet restore "${work}/src/vendor/KPM/src/KPM.Core/KPM.Core.csproj" -p:Configuration=Release --nologo

# crossgen2 is picked by the architecture of the machine that builds, so an x64 restore never fetches
# the arm64 one Launchpad's arm64 builder runs. Fetch the other architecture of every RID-named pack.
downloads=()
while IFS= read -r nupkg; do
  name="$(basename "$(dirname "$(dirname "${nupkg}")")")"
  version="$(basename "$(dirname "${nupkg}")")"
  for pair in linux-x64:linux-arm64 linux-arm64:linux-x64; do
    sibling="${name/.${pair%%:*}/.${pair##*:}}"
    if [[ "${sibling}" != "${name}" && ! -d "${NUGET_PACKAGES}/${sibling}/${version}" ]]; then
      downloads+=("<PackageDownload Include=\"${sibling}\" Version=\"[${version}]\" />")
    fi
  done
done < <(find "${NUGET_PACKAGES}" -mindepth 3 -maxdepth 3 -name '*.nupkg' -path '*linux-*')
if (( ${#downloads[@]} )); then
  mkdir -p "${work}/siblings"
  printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>%s</ItemGroup></Project>\n' \
    "$(printf '%s' "${downloads[@]}")" > "${work}/siblings/siblings.csproj"
  dotnet restore "${work}/siblings/siblings.csproj" --nologo
fi

while IFS= read -r nupkg; do
  [[ -e "${feed}/$(basename "${nupkg}")" ]] || cp "${nupkg}" "${feed}/"
done < <(find "${NUGET_PACKAGES}" -mindepth 3 -maxdepth 3 -name '*.nupkg')
echo "${feed} holds $(find "${feed}" -name '*.nupkg' | wc -l) packages."
