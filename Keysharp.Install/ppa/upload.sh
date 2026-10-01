#!/usr/bin/env bash
# Usage: upload.sh OWNER/PPA CHANGES...
#
# Signs each source upload with PPA_GPG_PRIVATE_KEY (armored; PPA_GPG_PASSPHRASE if it has one) and
# sends it to the PPA. A version the PPA has already published is skipped, since Launchpad would only
# reject it, so a rerun uploads just what is missing.
set -euo pipefail

ppa="${1:?owner/ppa}"
shift
: "${PPA_GPG_PRIVATE_KEY:?PPA_GPG_PRIVATE_KEY must hold the armored secret key of the Launchpad signing identity}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

umask 077
GNUPGHOME="$(mktemp -d)"
export GNUPGHOME
trap 'gpgconf --kill all >/dev/null 2>&1 || true; rm -rf -- "${GNUPGHOME}"' EXIT
printf '%s\n' "${PPA_GPG_PRIVATE_KEY}" | gpg --batch --quiet --import
fingerprint="$(gpg --batch --with-colons --list-secret-keys | awk -F: '$1 == "fpr" { print $10; exit }')"
[[ -n "${fingerprint}" ]] || { echo "PPA_GPG_PRIVATE_KEY holds no secret key." >&2; exit 1; }
# dput checks the signatures it uploads, and reports an unknown-trust key as an error.
printf '%s:6:\n' "${fingerprint}" | gpg --batch --quiet --import-ownertrust
printf '%s' "${PPA_GPG_PASSPHRASE:-}" > "${GNUPGHOME}/passphrase"
cat > "${GNUPGHOME}/sign" <<EOF
#!/bin/sh
exec gpg --batch --yes --pinentry-mode loopback --passphrase-file "${GNUPGHOME}/passphrase" "\$@"
EOF
chmod 0700 "${GNUPGHOME}/sign"
echo "Signing with ${fingerprint}."

for changes in "$@"; do
  source="$(sed -n 's/^Source: //p' "${changes}")"
  version="$(sed -n 's/^Version: //p' "${changes}")"
  if python3 "${here}/launchpad.py" has-version "${ppa}" "${source}" "${version}"; then
    echo "ppa:${ppa} already has ${source} ${version}; skipping it."
    continue
  fi
  # debsign asks before replacing a signature and after a failed one; no stdin makes it decide at once.
  debsign --no-conf --re-sign -p "${GNUPGHOME}/sign" -k "${fingerprint}" "${changes}" </dev/null
  dput "ppa:${ppa}" "${changes}"
done
