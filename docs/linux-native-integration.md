# Linux native integration

Keysharp delegates foreign desktop automation to `keysharp-desktop` on X11 and
Wayland, and global input to `keysharp-input`. GTK/Eto still owns the script's GUI,
overlays and local GUI events. Core retains a small Xlib helper for waiting until
its own GTK window maps; foreign window, capture, pointer and keyboard queries
use the component libraries.

| Component | Responsibility |
| --- | --- |
| `keysharp-input` (client ABI 0.2+) | Suppressible hooks, passive observers, synthesis, state/idle queries, device metadata and raw device observation |
| `keysharp-desktop` (client ABI 0.8+) | Window queries/actions/events, capture, pointer positioning, display topology, keyboard keymaps and compositor integration |
| `keysharp-permissions` | Shared source library for identity and durable grants; bundled into the two services |

System grants persist per user, executable identity and scope. Scripts and DLLs
share their host executable's identity. Protected executable paths retain grants
across upgrades; changed user-writable executables require renewed authorization.
Denied and cancelled prompts are not saved. A user-installed desktop service uses
an explicit Allow Always/Deny dialog and a private store; its grants do not
authorize the system input service.

X11 uses the same consent scopes as Wayland. These scopes govern broker access;
the X server itself still accepts calls from other applications in that session.
Executable identity checks on ongoing input streams run periodically, with a
bounded timeout when validation cannot complete.

The desktop ABI adds single-window queries, child enumeration, hit-testing,
display topology, keyboard state/keymaps, title/visibility/redraw operations,
individual window button events and child focus. X11 preserves native XIDs,
frame/client geometry, legacy titles, window stacking and XComposite capture.
Native X11 event streams avoid periodic window enumeration during idle periods.
`validFields` distinguishes supplied facts from unavailable provider metadata.

For a SharpHook-style consumer, passive observers provide a bounded event stream
without grabbing devices or waiting for callback decisions. The separate hook
stream supports suppression and replacement. Device records include capabilities,
hotplug generations and absolute-axis ranges; raw observation covers touchpads,
pens and touchscreens. Consumers interpret raw coordinates, assemble click/drag
semantics, and translate keymaps locally. See the native projects' integration
guides and observer/keyboard examples.

Keyboard queries support a map revision: an unchanged reply omits the large
keymap. Keysharp retains it across state refreshes. Native executable validation
caches file identity, permission checks use generation invalidation, desktop
connections persist, and an interrupted mutation is reported without automatic
replay. The managed nested-hook adapter retains replacement storage until native
serialization has finished and defers callback-time disposal.

Input permission-store reads and writes run on a bounded worker pool, keeping
filesystem stalls out of the device event loop. Authorization completions are
checked against the connection and active-seat generation. Revocation and grant
refresh fence queued output and release synthesized keys before dispatch resumes.

Important platform limits remain explicit:

- Generic Wayland can publish a keymap without exposing the global active layout
  group. Keysharp retains its first-layout fallback in that case.
- Key translation does not provide IME-committed text. Raw touch data requires
  gesture interpretation, and relative deltas are not accelerated cursor positions.
- Portal screenshot capture still produces an image per request. PipeWire frame
  streaming is a separate capture backend. KWin's restricted capture path retains
  its dedicated one-shot worker.
- X11 and fake-compositor tests do not establish behavior on real GNOME, KDE,
  COSMIC, touch hardware or high-rate input devices.

The native desktop performance guide records isolated Xvfb measurements and
reproduction commands. These measure native operations on a persistent connection;
they exclude authority IPC, managed parsing, polkit and real hardware latency.

Both native projects pin the same published `keysharp-permissions` commit as a
source submodule. Their releases bundle this shared implementation; no separate
runtime permission package is required. Matching setup and platform notes are
maintained in the separate `KeysharpDocs` reference-site checkout.

Validation on 2026-09-05 passed 24 targeted managed tests, 15 desktop native test
targets, seven input native test targets, the shared permissions suite, and the
reference site's 474-file HTML check. The capability parity report has no name
mismatches; its 23 missing and 292 extra inventory entries are unchanged by this
work. The capability names, categories and reference-site index match their
respective repository baselines.
