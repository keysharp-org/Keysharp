# Linux native integration

Keysharp delegates foreign desktop automation to `keysharp-desktop` on X11 and
Wayland, and global input to `keysharp-input`. GTK/Eto owns the script's GUI and
local GUI events. Overlay presentation prefers a layer-shell surface when available,
then a GNOME/Cinnamon shell actor for click-through overlays, and otherwise an Eto
window. Core uses a small Xlib helper to wait until its own GTK window maps;
foreign window, capture, pointer and keyboard queries use the component libraries.

| Component | Responsibility |
| --- | --- |
| `keysharp-input` (client ABI 0.2+) | Suppressible hooks, passive observers, synthesis, state/idle queries, device metadata and raw device observation |
| `keysharp-desktop` (client ABI 0.8+) | Window queries/actions/events, capture, pointer positioning, display topology, keyboard keymaps and compositor integration |
| `keysharp-permissions` | Shared source library for identity and durable grants; bundled into the two services |

On GNOME, the desktop extension also consumes the standard Unity
`LauncherEntry` signal and maps its application-wide count, progress and urgent
state onto the stock overview dash. The Cinnamon extension maps the same state
onto the grouped-window-list. Plasma's task manager and compatible third-party
docks consume the protocol themselves.

System grants persist per user, executable identity and scope. Scripts and DLLs
share their host executable's identity. Grants for protected executable paths survive
upgrades; changed user-writable executables require renewed authorization.
Denied and cancelled prompts are not saved. A user-installed desktop service uses
an explicit Allow Always/Deny dialog and a private store; its grants do not
authorize the system input service.

X11 uses the same consent scopes as Wayland. These scopes govern broker access;
the X server accepts calls from other applications in that session.
Executable identity checks on ongoing input streams run periodically, with a
bounded timeout when validation cannot complete.

The desktop ABI adds single-window queries, child enumeration, hit-testing,
display topology, keyboard state/keymaps, title/visibility/redraw operations,
individual window button events and child focus. X11 uses native XIDs,
frame/client geometry, legacy titles, window stacking and XComposite capture.
Native X11 event streams avoid periodic window enumeration during idle periods.
Providers list supplied window facts in `validFields`; the managed parser treats
unlisted values as unavailable metadata.

The input library exposes passive observers for consumers that need a bounded event
stream without grabs or callback decisions. Keysharp uses the separate hook stream,
which supports suppression and replacement. Device records include capabilities,
hotplug generations and absolute-axis ranges; raw observation covers touchpads,
pens and touchscreens. Consumers interpret raw coordinates, assemble click/drag
semantics, and translate keymaps locally. See the native projects' integration
guides and observer/keyboard examples.

Keyboard queries support a map revision: a request carrying the current revision
receives an unchanged reply without the large keymap, which Keysharp reuses. Native
executable validation caches file identity, permission generation changes invalidate
cached grants, desktop connections persist, and an interrupted mutation is reported
without automatic replay. The managed nested-hook adapter keeps replacement storage until native
serialization has finished and defers callback-time disposal.

Input permission-store reads and writes run on a bounded worker pool, keeping
filesystem stalls out of the device event loop. Authorization completions are
checked against the connection and active-seat generation. Revocation and grant
refresh fence queued output and release synthesized keys before dispatch resumes.

Important platform limits remain explicit:

- Generic Wayland can publish a keymap without exposing the global active layout
  group. Keysharp uses its first-layout fallback in that case.
- Key translation does not provide IME-committed text. Raw touch data requires
  gesture interpretation, and relative deltas are not accelerated cursor positions.
- Joystick discovery and state reads access evdev directly and can require membership
  in the `input` group; they do not use `keysharp-input` yet.
- Portal screenshot capture produces an image per request. PipeWire frame
  streaming is a separate capture backend. KWin's restricted capture path uses
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
