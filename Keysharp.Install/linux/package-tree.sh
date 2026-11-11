#!/usr/bin/env bash

install_package_tree() {
  local app="$1" assets="$2" docs="$3" icon="$4" dest entry
  [[ -n "$5" ]] || { echo 'Expected a nonempty package destination.' >&2; return 2; }
  dest="$(readlink -m -- "$5")" || return 2
  [[ "${dest}" != / ]] || { echo 'Package destination must not be the filesystem root.' >&2; return 2; }
  [[ -d "${app}" ]] || { echo "Expected app payload at ${app}; aborting." >&2; return 1; }

  install -d "${dest}/usr/lib/keysharp" "${dest}/usr/bin" "${dest}/usr/share/applications"
  cp -a "${app}/." "${dest}/usr/lib/keysharp/"
  ln -sfn ../../share/doc/keysharp/copyright "${dest}/usr/lib/keysharp/license.txt"
  ln -sfn ../lib/keysharp/Keysharp "${dest}/usr/bin/keysharp"
  ln -sfn ../lib/keysharp/Keyview "${dest}/usr/bin/keyview"
  for entry in keysharp keyview; do
    sed -e 's|/usr/local/bin/|/usr/bin/|g' -e 's|/usr/local/lib/keysharp/|/usr/lib/keysharp/|g' \
      "${assets}/${entry}.desktop" > "${dest}/usr/share/applications/${entry}.desktop"
    chmod 0644 "${dest}/usr/share/applications/${entry}.desktop"
  done
  install -Dm644 "${assets}/keysharp.xml" "${dest}/usr/share/mime/packages/keysharp.xml"
  install -Dm644 "${icon}" "${dest}/usr/share/icons/hicolor/256x256/apps/keysharp.png"
  install -Dm644 "${assets}/70-keysharp-i2c-uaccess.rules" \
    "${dest}/usr/lib/udev/rules.d/70-keysharp-i2c-uaccess.rules"
  # Debian's postinst sources this notice after the package is installed.
  install -Dm644 "${assets}/component-notice.sh" "${dest}/usr/share/keysharp/component-notice.sh"
  install -Dm644 "${docs}/license.txt" "${dest}/usr/share/doc/keysharp/copyright"
  install -Dm644 "${docs}/README.md" "${dest}/usr/share/doc/keysharp/README.md"
}
