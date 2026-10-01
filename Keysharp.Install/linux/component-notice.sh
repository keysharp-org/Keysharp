# Names the standalone components that are missing. Keysharp recommends their
# client ABI virtual packages, which stays silent when no repository offers them.
component_installed() {
  for component_dir in /usr/bin /usr/local/bin /run/current-system/sw/bin; do
    if [ -x "${component_dir}/$1" ]; then return 0; fi
  done
  return 1
}

missing_components=""
component_installed keysharp-input || missing_components="${missing_components}
  keysharp-input: global hotkeys and hotstrings, input hooks and synthesis, BlockInput"
component_installed keysharp-desktop || missing_components="${missing_components}
  keysharp-desktop: authorized screen capture, foreign-window queries and control, clipboard integration"

if [ -n "${missing_components}" ]; then
  printf '%s%s\n%s\n%s\n' \
    "Keysharp runs without these standalone components, which supply its privileged Linux features:" \
    "${missing_components}" \
    "keysharp-linux-setup.sh, from https://github.com/keysharp-org/Keysharp/releases, installs them." \
    "With ppa:descolada/keysharp added, apt installs them: sudo apt install keysharp-input keysharp-desktop"
fi
