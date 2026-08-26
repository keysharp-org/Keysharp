# keysharp-inputd IPC protocol

`keysharp-inputd` exposes a Windows-shaped logical input protocol over a Unix
socket. The daemon uses evdev, udev, and uinput internally, but Keysharp sees
the same concepts it already uses for Windows hooks and `SendInput`.

C constants and structs: `include/keysharp_inputd/protocol.h`

## Framing

Every message has a `ksi_message_header` carrying:

- protocol major/minor version
- message type
- client id
- correlation id (for request/response pairs)
- payload byte size

The current wire version is `1.1`; major and minor must both match. Version 1.1
adds explicit mouse-move deltas, so Keysharp and an installed daemon must be
updated together.

Frames arrive over a stream socket and may be split or coalesced. The daemon
buffers per-client input and disconnects clients that send invalid versions,
invalid sizes, or oversized frames.

Supported message types:

```
CLIENT_HELLO          HEARTBEAT
SUBSCRIBE_HOOK        UNSUBSCRIBE_HOOK
HOOK_EVENT            HOOK_DECISION
HOOK_QUARANTINED
SYNTHESIZE_INPUT      SYNTHESIS_RESULT
EMERGENCY_PASSTHROUGH SET_BLOCK_INPUT
GET_INDICATOR_STATE   INDICATOR_STATE_RESULT
GET_POINTER_POSITION  POINTER_POSITION_RESULT
GET_KEY_STATE         KEY_STATE_RESULT
GET_POINTER_BUTTONS   POINTER_BUTTONS_RESULT
IDLE_TIME
LIST_PERMISSIONS      RESET_PERMISSIONS
```

`GET_KEY_STATE` / `KEY_STATE_RESULT` report current logical modifiers, lock-key
state, a logical evdev key bitmap, and an appended physical evdev key bitmap. It
requires `KSI_CAP_HOOK_KEYBOARD`.

`GET_POINTER_BUTTONS` / `POINTER_BUTTONS_RESULT` report mouse button masks. The
legacy `buttons` field remains the physical mask; newer clients read appended
`logical_buttons` and `physical_buttons`. Logical buttons combine the evdev
physical snapshot with Keysharp's queued synthetic button state. It requires
`KSI_CAP_HOOK_MOUSE`.

`IDLE_TIME` is an empty request and a same-type response carrying a validity flag
and milliseconds since the daemon last observed upstream user activity. It
requires an authenticated `CLIENT_HELLO` but no privileged input capability, so
reading `A_TimeIdle` never causes a permission prompt. Until the daemon observes
its first activity event, the validity flag is false. The response uses the same
message type and carries a 16-byte idle payload.

`LIST_PERMISSIONS` and `RESET_PERMISSIONS` back the `keysharp-inputd trust`
subcommand (see below).

The protocol permits a `HEARTBEAT` with correlation id `0` as a one-way grab
lease renewal. The daemon sends no response for that form, so hook-reader
connections can renew without introducing an unexpected receive frame. Other
heartbeat correlation ids retain request/response behavior.

## Authorization

Clients connect through the systemd socket at
`/run/keysharp-inputd/keysharp-inputd.sock`. The daemon authenticates via
`SO_PEERCRED` plus a hash of the peer's executable and argument vector.

Protocol 1.x is intentionally incompatible with 0.2. `CLIENT_HELLO` carries
requested capabilities, optional flags, and one authenticated connection role:

- `HOOK_STREAM` receives hook events, returns decisions, and carries synchronous
  requests made while one of those callbacks is active.
- `GENERAL_RPC` carries ordinary requests from unrelated script threads.

Callback synthesis stays on the same `HOOK_STREAM` and uses its current parent
event id as the request correlation id. The daemon accepts that ancestry only while
the same authenticated stream is the active responder for that event. A general
connection cannot claim a parent, and stale callback output is emitted only through
the bypassed fail-open path.

The system socket is machine-wide, but input ownership is not: only the uid of
logind's active session on `seat0` may acquire capabilities, subscribe, block,
synthesize, query input state, or enter a hook snapshot. A seat-owner transition
fails old work closed by generation and releases its grabs before the new uid is
published. Evdev discovery likewise admits only `seat0` devices.

Capabilities and flags are:

```
KSI_CAP_HOOK_KEYBOARD   KSI_CAP_HOOK_MOUSE
KSI_CAP_SYNTH_KEYBOARD  KSI_CAP_SYNTH_MOUSE
KSI_CAP_BLOCK_INPUT

KSI_CLIENT_HELLO_FLAG_FORCE_PROMPT
```

The daemon replies with granted capabilities. Unknown identities trigger a
permission prompt; `Allow always` decisions are persisted per uid in the
shared root-owned keysharp-trust store and pruned after 60 days.
Keysharp requests both keyboard and mouse bits for its user-facing input
monitoring permission, and both synthesis bits for its user-facing input
synthesis permission. The protocol still keeps the device bits separate.

`Deny` is also persisted — once denied, the daemon will not prompt for that
capability again until the user explicitly opts back in. There are two ways
to opt back in:

* From inside a script, call `Ks.RequestCapabilities(...)`, which sends
  `KSI_CLIENT_HELLO_FLAG_FORCE_PROMPT` and bypasses the persistent deny.
* Out of band, run `keysharp-inputd trust reset <hash>` (or `--pid <pid>`). The
  subcommand speaks `KSI_MESSAGE_LIST_PERMISSIONS` and `KSI_MESSAGE_RESET_PERMISSIONS`
  to the daemon, which clears the allow/deny bits for the matched record.

The list/reset messages are scoped to the caller's uid. Only root (when the
daemon is running as root) can target another user's records.

## Hook model

The daemon owns hook transport and native fail-open safety; Keysharp owns script
hook policy. Root keyboard and mouse events use independent lane threads. Each
script has one `HOOK_STREAM`
call stack shared by both hook types, so its own keyboard/mouse callbacks serialize;
different scripts can execute in parallel. All uinput-bound writes use one output
sequencer thread.

```
evdev / main thread       lane threads             sequencer thread
─────────────────────     ────────────────         ────────────────
classify hook event  ──►  (kbd lane)
                            │  send HOOK_EVENT
                            │  await decision/timeout
                            └─►  action          ──►  uinput
classify hook event  ──►  (mouse lane)
                            │  …
                            └─►  action          ──►
SYNTHESIZE_INPUT     ──────────────────────────────►
```

An ordinary `SYNTHESIZE_INPUT` result acknowledges atomic queue admission; its
low-level callbacks run later and never hold the sender RPC open. A request on an
active `HOOK_STREAM` instead forms one synchronous child transaction on the exact
parent lane, irrespective of child input type. The stream marks its parent frame as
pumping; recursive callbacks then enter the same per-script call stack ahead of
queued root callbacks. Every child runs the complete newest-to-oldest hook chain,
and the synthesis result is sent only after all child frames unwind. The managed
reader pumps those nested `HOOK_EVENT` frames synchronously, so Send returns
child-before-parent just as it does inside a Windows low-level hook. Before the
daemon publishes a synthesis result, it closes that parent pump and lets any
recursive frames which already entered from the other lane unwind. Consequently,
two separate back-to-back Sends cannot overlap stale native pump state.

Recursion is limited to 32 callback transactions and synthesis requests are
limited to 1024 `ksi_input` entries and 4096 expanded low-level hook events. Either
limit rejects the child before hook delivery or uinput output. Recursion-limit and
expanded-size failures use synthesis result details 32 and 33 respectively.

Hook subscriptions require the matching hook access. `MODIFY` decisions and direct
`SYNTHESIZE_INPUT` additionally require the matching synthesis access. Once
`EVIOCGRAB` is active, passed events must be replayable through `uinput`; if
`uinput` is unavailable, subscriptions are denied.

Every entered subscriber callback has its own one-second deadline. Time spent in
recursive child callbacks is charged to those children, not again to the suspended
parent. A busy stream which cannot start another root turn simply passes that event;
the active callback owns timeout accounting. An entered timeout passes the event and
quarantines only that hook type. `HOOK_QUARANTINED` reports the event, strike and
cooldown; the daemon retries automatically after 1, 2, 4, 8 or 16 seconds. Sixty
seconds without another quarantine resets the strike history. The fifth consecutive
timeout invalidates only that HookStream so managed recovery reconnects it.
Keyboard and mouse quarantine state is independent.

`EMERGENCY_PASSTHROUGH` from a client already granted hook access clears all hook
subscriptions, discards pending hook events, and releases all grabs.

Hook subscriptions and non-zero `BlockInput` masks hold a 15-second lease.
Keysharp renews active leases every five seconds using one-way heartbeats.
Expiry clears the client's input state, releases grabs, and disconnects the
client so managed recovery can reconnect or fall back.

Physically holding `Backspace+Escape+Enter`, in any press order, performs the same
fail-open action directly in the daemon. It clears hook subscriptions and
`BlockInput` masks and releases grabs without involving managed hook logic. The
final chord event is consumed while a keyboard grab is active.

`SET_BLOCK_INPUT` requires `KSI_CAP_BLOCK_INPUT` and sets the calling client's
physical input block mask: keyboard, mouse, both, or neither. The daemon drops
blocked physical events while allowing virtual input to continue. Block masks are
client-scoped and are removed when that client disconnects.

The daemon bounds its queues and admits client input atomically. Each hook lane
has a single pending decision slot, so a decision from any client other than the
lane's current responder — or a late decision after the one-second timeout — is
rejected. A `SYNTHESIZE_INPUT` request or `MODIFY` decision that would exceed the
output bounds is rejected with a failure result rather than partially queued, and
capacity is reserved for key/button releases so saturation cannot split cleanup
after its press.

## Ordering and timestamps

Synthetic batches receive a trusted daemon monotonic admission timestamp in
nanoseconds. Before starting a batch, the daemon peeks every grabbed physical
device; an earlier kernel `CLOCK_MONOTONIC` event runs first (ties favor the
already-observed physical event). Synthesis also waits for every earlier physical
hook callback to reach output admission. After a synthetic batch starts, its
remaining members retain priority over later physical and unrelated synthetic input.
Nested callback synthesis is the normal exception and preempts its parent.
Emergency passthrough and fail-open recovery may preempt everything.

The caller's `INPUT.time` is never used for scheduling. A nonzero value is copied
to the generated hook payload, while zero is replaced with the current monotonic
millisecond value. Past and future values are therefore visible as metadata but
are delivered immediately. Synthetic events retain `device_id = 0`; physical
events retain their daemon-assigned device id.

### Keyboard hook event fields

Modelled after `KBDLLHOOKSTRUCT` + low-level hook `wParam`:

| Field | Description |
|---|---|
| `message` | `WM_KEYDOWN`, `WM_KEYUP`, `WM_SYSKEYDOWN`, `WM_SYSKEYUP` |
| `vk_code` | Windows virtual-key code |
| `scan_code` | Backend low-level key code (e.g. Linux evdev `KEY_*`) |
| `flags` | `LLKHF_EXTENDED`, `LLKHF_INJECTED`, `LLKHF_ALTDOWN`, `LLKHF_UP` |
| `time_ms` | Event timestamp |
| `extra_info` | Equivalent of `dwExtraInfo` |
| `device_id` | Daemon-assigned physical device id |

### Mouse hook event fields

Modelled after `MSLLHOOKSTRUCT` + low-level hook `wParam`:

| Field | Description |
|---|---|
| `message` | `WM_MOUSEMOVE`, `WM_LBUTTONDOWN`, `WM_MOUSEWHEEL`, etc. |
| `x`, `y` | Original replay values: relative counts or a normalized absolute target |
| `mouse_data` | Wheel delta or X-button value |
| `flags` | `LLMHF_INJECTED`-style flags |
| `time_ms` | Event timestamp |
| `extra_info` | Equivalent of `dwExtraInfo` |
| `device_id` | Zero for synthetic input; daemon-assigned positive id for physical input |
| `delta_x`, `delta_y` | Relative counts, or a physical absolute report's change in normalized `[0,65535]` axes; synthetic absolute reports use zero |

### evdev translation

```
KEY_A        → VK_A + scan code KEY_A
BTN_LEFT     → WM_LBUTTONDOWN/UP
REL_WHEEL    → WM_MOUSEWHEEL + WHEEL_DELTA-compatible mouse_data
BTN_SIDE     → WM_XBUTTONDOWN/UP + XBUTTON1-compatible mouse_data
BTN_EXTRA    → WM_XBUTTONDOWN/UP + XBUTTON2-compatible mouse_data
```

Axis records are grouped only to the evdev `SYN_REPORT` boundary; each resulting
relative or absolute movement report is forwarded once.

## Synthesis model

Input synthesis is modelled after Windows `SendInput` (`ksi_input`).

Keyboard synthesis fields: `vk`, `scan`, `flags` (`KEYEVENTF_*`), `time`, `extra_info`.

Mouse synthesis fields: `dx`, `dy`, `mouse_data`, `flags` (`MOUSEEVENTF_*`), `time`, `extra_info`.

The daemon translates requests into `uinput` events. `extra_info` is present in
the protocol for cross-platform shape compatibility but is not preserved by Linux
`uinput`.

`MODIFY` hook decisions append replacement `ksi_input` entries after the decision
header and require the same synthesis capabilities as direct `SYNTHESIZE_INPUT`.

## Device state queries

`GET_POINTER_POSITION` returns the last absolute pointer sample (raw `ABS_X`,
`ABS_Y`, and each axis's min/max). Keysharp maps this range to screen coordinates
before exposing the value to scripts. Requires `KSI_CAP_HOOK_MOUSE`.

`GET_INDICATOR_STATE` / `INDICATOR_STATE_RESULT` report the current Caps Lock,
Num Lock, and Scroll Lock LED state.

`IDLE_TIME` is fed by keyboard, button, touch, pen, gamepad/media-key,
relative-motion, wheel, and pointer-axis events from tracked evdev devices.
Devices tracked only for idle time are opened read-only and are never grabbed or
hook-dispatched. Keysharp's own uinput devices are excluded before the idle
timestamp is updated or hooks are dispatched, so replayed and synthetic input
cannot masquerade as new physical activity. Event timestamps use
`CLOCK_MONOTONIC`; if an IPC query races queued device data, the daemon ingests
that data first and computes the elapsed duration from the newest processed
kernel event timestamp. A capless idle query does not create Keysharp's uinput
devices or elevate the observer thread to real-time scheduling; those resources
are activated only for explicitly requested privileged input functionality.

## Injected-event tagging

The daemon's `uinput` device is identified by:

```
name     Keysharp Virtual Input
bustype  BUS_VIRTUAL
vendor   0x0FAC   (masquerades as keyd's vendor so keyd won't re-grab our output; see protocol.h)
product  0x0001
```

The device is identified as *ours* by name **and** vendor (`0x0FAC` is shared with
keyd, so name is the discriminator); keyd's own `keyd virtual keyboard` therefore
does not match and remains a valid interception target for us to grab.

Events from this device set `LLKHF_INJECTED` / `LLMHF_INJECTED` in hook callback
flags. Pass-through replay events are suppressed from re-entering the callback
path; direct `SYNTHESIZE_INPUT` events are not, so Keysharp can apply
`SendLevel`-style policy to its own synthetic input.

## Multiple scripts

Multiple Keysharp processes are modelled as ordered hook clients. Each
subscription carries a client id, hook type, and subscription order. The daemon
owns delivery order, timeout enforcement, device ownership, replay, suppression,
and synthesis. Individual Keysharp processes do not open or grab physical devices
themselves.
