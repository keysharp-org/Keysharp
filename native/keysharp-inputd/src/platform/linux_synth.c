#include "keysharp_inputd/linux_synth.h"

#include "keysharp_inputd/globals.h"
#include "keysharp_inputd/linux_devices.h"
#include "linux_device_filter.h"
#include "vk_evdev.h"

#include <errno.h>
#include <fcntl.h>
#include <linux/input.h>
#include <linux/uinput.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/ioctl.h>
#include <time.h>
#include <unistd.h>

#define KSI_UINPUT_PATH "/dev/uinput"

/* Synthetic-output pacing.
 *
 * uinput gives the producer no backpressure: write() always succeeds, and when
 * a consumer's per-fd evdev ring fills (≈64 events on a typical keyboard) the
 * kernel discards queued events and raises SYN_DROPPED on *that consumer*. A
 * back-to-back burst (e.g. a long Send) therefore silently loses keystrokes —
 * the compositor never gets a chance to drain between our writes.
 *
 * Since the overflow cannot be observed from here, the practical mitigation is
 * to keep the number of in-flight (un-drained) events safely below the ring and
 * briefly yield so the consumer's event loop runs. We count emitted events and
 * clock_nanosleep() once a chunk's worth has been written. The check runs after
 * every event (not per high-level input), so the chunk size IS the per-cycle
 * footprint — there is no extra overshoot from a single input expanding into a
 * burst (e.g. a Unicode char ≈32 events).
 *
 * Smaller, more frequent pauses tolerate delayed consumer scheduling better
 * while preserving the same nominal throughput. Short sends never reach the
 * threshold and pay nothing, so the common case runs at full speed.
 *
 * The counter is SHARED across every output path that runs on the sequencer
 * thread -- bulk client synthesis AND single-event passthrough replay -- and
 * PERSISTS across back-to-back emits rather than resetting per batch. This is
 * what lets replay be paced: a passthrough replay is one event per call, so a
 * per-batch reset would keep it forever below the threshold, but a *sustained*
 * burst (mouse REL motion floods, key autorepeat, several grabbed devices
 * interleaving) is a stream of such calls arriving back-to-back on the one
 * sequencer thread and can overflow the consumer ring exactly like a long Send.
 * By carrying the counter across calls, that stream accumulates and paces the
 * same way synthesis does.
 *
 * Isolated input must stay free (no per-keystroke latency), so the counter is
 * reset whenever the gap since the previous emit exceeds KSI_SYNTH_PACE_IDLE_
 * RESET_NS: an idle gap that long means the consumer has had ample time to
 * drain, so nothing we sent is still in flight and the next event starts a
 * fresh chunk. A single passed keystroke (or one every few hundred ms) therefore
 * never accumulates toward the threshold and never sleeps, while a genuine
 * sub-millisecond flood does. The gap-based reset also spaces successive client
 * Sends: two Sends separated by human-scale time each start fresh, exactly as
 * the old per-batch reset did. */
#define KSI_SYNTH_PACE_EVENTS 16
#define KSI_SYNTH_PACE_SLEEP_NS (350L * 1000L) /* 0.35 ms */
/* Idle gap after which the consumer is assumed fully drained (nothing of ours
 * in flight) and the pacing counter is reset. Comfortably above the pace sleep
 * (0.35 ms) so a paced burst is never mistaken for idle, comfortably below any
 * human-perceptible key/mouse cadence so isolated input always resets. */
#define KSI_SYNTH_PACE_IDLE_RESET_NS (8L * 1000L * 1000L) /* 8 ms */
#define KSI_VK_PACKET 0xE7u

/* Relative mouse: keyboard keys + BTN_* + REL_X/Y/WHEEL. */
static int uinput_fd = -1;
/* Absolute pointer: ABS_X/Y with INPUT_PROP_POINTER for absolute MouseMove. */
static int uinput_abs_fd = -1;
/* Latched by emit_event_to() on a genuine uinput write failure (see there).
 * Cleared/re-latched by ksi_linux_synth_recreate() (on the sequencer thread).
 * Read by ksi_linux_synth_is_available() and ksi_linux_synth_needs_recovery()
 * on the main thread -- a benign read of a simple latch, the same cross-thread
 * read is_available() has always done. */
static bool synth_write_failed;

/* All uinput writes and the ACTUAL-device synthesis state they touch (uinput_fd,
 * uinput_abs_fd, synthesized_keys_down and the pacing counters) are accessed on a
 * single thread -- the output sequencer, which drains replay, client synthesis,
 * the release-all action (KSI_OUTPUT_ACTION_RELEASE_ALL) AND the recreate action
 * (KSI_OUTPUT_ACTION_RECREATE_SYNTH) in arrival order. ksi_linux_synth_stop() /
 * ksi_linux_synth_start() therefore run on the sequencer thread in exactly two
 * situations, both of which keep this state single-threaded: (1) at shutdown,
 * after the sequencer thread has been joined (daemon.c); (2) mid-run via
 * ksi_linux_synth_recreate(), which the sequencer itself invokes when it drains a
 * RECREATE_SYNTH action -- NEVER from the main thread, whose only role is to
 * ENQUEUE that action. So there is no concurrent access and no lock is needed here.
 * (If a caller is ever added that touches this state off the sequencer thread while
 * it is running, reintroduce serialization.) synthesized_keys_down tracks what is
 * actually held on the uinput device so release_all can drop any key left "down"
 * when the grab is dropped. */
static bool synthesized_keys_down[KEY_MAX + 1];

/* enqueued_synth_* is the exception to the single-thread rule above: it tracks
 * the LOGICAL synthetic input state at the hook / output-queue boundary, updated when a
 * synth batch is ENQUEUED (see ksi_linux_synth_note_enqueued_synth, called from the
 * daemon's output-queue push path on the main/lane threads) rather than when it drains
 * to uinput, and read by GET_KEY_STATE/GET_POINTER_BUTTONS — so it needs its own lock. A query therefore
 * reports the state that WILL exist once the queue empties, race-free regardless of how
 * far the paced drain has progressed. This mirrors Windows, where SendInput is
 * synchronous and the modifier state is already in effect the instant SendInput returns
 * — here the logical state is likewise settled the instant the batch is accepted,
 * without waiting for the physical drain. */
static bool enqueued_synth_keys_down[KEY_MAX + 1];
static uint32_t enqueued_synth_pointer_buttons;
static pthread_mutex_t enqueued_synth_keys_mutex = PTHREAD_MUTEX_INITIALIZER;

static int resolve_synth_key_code(const ksi_keybdinput *input, int *out_value);
static uint16_t mouse_data_to_xbutton(uint32_t mouse_data);
static uint16_t pending_high_surrogate;
/* Pacing state (see pacing note). synth_pacing_active gates the actual sleep and
 * is held while ANY output that funnels through ksi_linux_synth_send_input is
 * being written -- that is both bulk client synthesis and single-event
 * passthrough replay, since replay_*_hook_event route through send_input too.
 * synth_pace_events is the shared, persistent chunk counter; last_emit_ns is the
 * monotonic timestamp of the previous emit, used to reset the counter after an
 * idle gap. All three are touched only on the sequencer thread (see note above),
 * so no lock is needed. */
static bool synth_pacing_active;
static unsigned synth_pace_events; /* events emitted since the last yield */
static uint64_t last_emit_ns;      /* monotonic time of the previous emit, for idle reset */

static void pace_synthetic_output(void)
{
    struct timespec pause = { 0, KSI_SYNTH_PACE_SLEEP_NS };
    struct timespec remaining;
    int result;

    do {
        result = clock_nanosleep(CLOCK_MONOTONIC, 0, &pause, &remaining);

        if (result == EINTR) {
            pause = remaining;
        }
    } while (result == EINTR);
}

static uint64_t monotonic_ms(void)
{
    struct timespec time_value;

    if (clock_gettime(CLOCK_MONOTONIC, &time_value) != 0) {
        return 0;
    }

    return ((uint64_t)time_value.tv_sec * 1000u) + ((uint64_t)time_value.tv_nsec / 1000000u);
}

/* Monotonic nanoseconds, for the sub-millisecond idle-gap check in the pacer.
 * Returns 0 on failure, which the caller treats as "no measurable gap" (it never
 * triggers an idle reset), so a clock hiccup fails safe toward more pacing. */
static uint64_t monotonic_ns(void)
{
    struct timespec time_value;

    if (clock_gettime(CLOCK_MONOTONIC, &time_value) != 0) {
        return 0;
    }

    return ((uint64_t)time_value.tv_sec * 1000000000ull) + (uint64_t)time_value.tv_nsec;
}

static uint64_t synth_hook_time_ms(uint32_t input_time)
{
    return input_time != 0u ? (uint64_t)input_time : monotonic_ms();
}

static uint32_t keyboard_indicator_flags_for_hook(void)
{
    bool caps_lock = false;
    bool num_lock = false;
    bool scroll_lock = false;
    uint32_t flags = 0;

    ksi_linux_devices_get_indicator_state(&caps_lock, &num_lock, &scroll_lock);

    if (caps_lock) {
        flags |= KSI_LLKHF_CAPS_LOCK_ON;
    }

    if (num_lock) {
        flags |= KSI_LLKHF_NUM_LOCK_ON;
    }

    if (scroll_lock) {
        flags |= KSI_LLKHF_SCROLL_LOCK_ON;
    }

    return flags;
}

static int emit_event_to(int fd, uint16_t type, uint16_t code, int32_t value)
{
    struct input_event event;
    ssize_t nwritten;

    if (fd < 0) {
        return -1;
    }

    memset(&event, 0, sizeof(event));
    event.type = type;
    event.code = code;
    event.value = value;

    /* Retry on EINTR: a signal delivered during the write syscall returns -1
     * with errno=EINTR.  Without the retry the rest of a synthesis batch is
     * silently dropped, which manifests as only a few characters being typed.
     *
     * We do NOT retry EAGAIN even though the fd is O_NONBLOCK: a write() to
     * /dev/uinput never blocks and never returns EAGAIN. uinput's write handler
     * copies the events into the kernel input core and returns immediately -- it
     * has no bounded producer-side buffer to fill (the ring that CAN overflow is
     * the *consumer's* evdev fd, which write() here cannot see, hence the pacing
     * below). O_NONBLOCK is set only so the open()/setup path can't stall. A
     * poll-retry would therefore never fire and is not warranted. */
    do {
        nwritten = write(fd, &event, sizeof(event));
    } while (nwritten < 0 && errno == EINTR);

    if (nwritten != (ssize_t)sizeof(event)) {
        /* A write() to /dev/uinput does not block and does not return EAGAIN
         * (see the comment above), so landing here means something is
         * actually wrong with the device -- e.g. the kernel tore it down out
         * from under us. Latch it so ksi_linux_synth_is_available() stops
         * silently advertising a broken output, and so the periodic recovery
         * check (ksi_linux_synth_needs_recovery -> a RECREATE_SYNTH action drained
         * by ksi_linux_synth_recreate on the sequencer thread) rebuilds the uinput
         * devices instead of leaving them dead for the rest of the process's life. */
        synth_write_failed = true;
        return -1;
    }

    /* Pace synthesis AND passthrough replay against the consumer's finite evdev
     * ring. The counter is shared and persists across calls (see pacing note),
     * so a sustained back-to-back burst -- whether one long Send or a flood of
     * single-event replays -- is throttled the same way. Checked after every
     * emitted event so the chunk size is the exact per-cycle footprint. Yielding
     * mid-report (between a key and its SYN) is harmless: the consumer applies
     * nothing until the SYN, and we drop nothing. */
    if (synth_pacing_active) {
        uint64_t now_ns = monotonic_ns();

        /* An idle gap this long means the consumer has drained everything we
         * sent, so nothing is in flight: start a fresh chunk. This keeps
         * isolated keystrokes free (they never accumulate to the threshold) and
         * reproduces the old per-batch reset for Sends spaced by human time,
         * while still letting a genuine sub-millisecond burst accumulate. */
        if (last_emit_ns != 0 && now_ns != 0
            && now_ns - last_emit_ns >= KSI_SYNTH_PACE_IDLE_RESET_NS) {
            synth_pace_events = 0;
        }

        last_emit_ns = now_ns;

        if (++synth_pace_events >= KSI_SYNTH_PACE_EVENTS) {
            pace_synthetic_output();
            synth_pace_events = 0;
        }
    }

    return 0;
}

static int emit_event(uint16_t type, uint16_t code, int32_t value)
{
    return emit_event_to(uinput_fd, type, code, value);
}

static int emit_abs_event(uint16_t code, int32_t value)
{
    return emit_event_to(uinput_abs_fd, EV_ABS, code, value);
}

static int emit_abs_sync(void)
{
    return emit_event_to(uinput_abs_fd, EV_SYN, SYN_REPORT, 0);
}

static int emit_sync(void)
{
    return emit_event(EV_SYN, SYN_REPORT, 0);
}

static int send_key_code(int key_code, int value)
{
    if (emit_event(EV_KEY, (uint16_t)key_code, value) != 0) {
        return -1;
    }

    /* Update bookkeeping as soon as the EV_KEY write lands, not only on full
     * success: the kernel's input core applies a key event to its internal
     * per-device state table immediately, independent of whether a following
     * SYN_REPORT event is ever written. Doing this before (rather than only
     * after) the SYN write means a SYN write failure can never leave
     * synthesized_keys_down understating a key the kernel already considers
     * held, which would otherwise let it survive a release-all sweep and
     * strand it stuck down on the virtual device. */
    if (key_code >= 0 && key_code <= KEY_MAX) {
        synthesized_keys_down[key_code] = value != 0;
    }

    return emit_sync();
}

void ksi_linux_synth_add_logical_key_state(uint8_t *keys, size_t key_bytes)
{
    if (keys == NULL) {
        return;
    }

    /* Report the enqueue-time logical state (what will be held once the queue
     * drains), not the drain-time synthesized_keys_down (which lags behind the
     * paced output and would momentarily show a transient modifier — e.g. the
     * Shift held to type capital letters — as still down, sticking it in the
     * caller's logical modifier state until the next send). */
    pthread_mutex_lock(&enqueued_synth_keys_mutex);

    for (int key_code = 0; key_code <= KEY_MAX; key_code++) {
        if (!enqueued_synth_keys_down[key_code]) {
            continue;
        }

        size_t byte_index = (size_t)key_code >> 3;

        if (byte_index >= key_bytes) {
            continue;
        }

        keys[byte_index] |= (uint8_t)(1u << (key_code & 7));
    }

    pthread_mutex_unlock(&enqueued_synth_keys_mutex);
}

void ksi_linux_synth_add_logical_pointer_button_state(uint32_t *buttons)
{
    if (buttons == NULL) {
        return;
    }

    pthread_mutex_lock(&enqueued_synth_keys_mutex);
    *buttons |= enqueued_synth_pointer_buttons;
    pthread_mutex_unlock(&enqueued_synth_keys_mutex);
}

static uint32_t mouse_button_to_mask(uint16_t button)
{
    switch (button) {
        case BTN_LEFT:   return 1u << 0;
        case BTN_RIGHT:  return 1u << 1;
        case BTN_MIDDLE: return 1u << 2;
        case BTN_SIDE:   return 1u << 3;
        case BTN_EXTRA:  return 1u << 4;
        default:         return 0u;
    }
}

static void note_enqueued_synth_mouse_button(uint16_t button, bool down)
{
    uint32_t mask = mouse_button_to_mask(button);

    if (mask == 0) {
        return;
    }

    if (down) {
        enqueued_synth_pointer_buttons |= mask;
    } else {
        enqueued_synth_pointer_buttons &= ~mask;
    }
}

static void note_enqueued_synth_mouse(const ksi_mouseinput *input)
{
    uint16_t xbutton;

    if (input == NULL) {
        return;
    }

    if ((input->flags & KSI_MOUSEEVENTF_LEFTDOWN) != 0)   note_enqueued_synth_mouse_button(BTN_LEFT, true);
    if ((input->flags & KSI_MOUSEEVENTF_LEFTUP) != 0)     note_enqueued_synth_mouse_button(BTN_LEFT, false);
    if ((input->flags & KSI_MOUSEEVENTF_RIGHTDOWN) != 0)  note_enqueued_synth_mouse_button(BTN_RIGHT, true);
    if ((input->flags & KSI_MOUSEEVENTF_RIGHTUP) != 0)    note_enqueued_synth_mouse_button(BTN_RIGHT, false);
    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEDOWN) != 0) note_enqueued_synth_mouse_button(BTN_MIDDLE, true);
    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEUP) != 0)   note_enqueued_synth_mouse_button(BTN_MIDDLE, false);

    xbutton = mouse_data_to_xbutton(input->mouse_data);

    if ((input->flags & KSI_MOUSEEVENTF_XDOWN) != 0) note_enqueued_synth_mouse_button(xbutton, true);
    if ((input->flags & KSI_MOUSEEVENTF_XUP) != 0)   note_enqueued_synth_mouse_button(xbutton, false);
}

/* Update the enqueue-time logical synthetic key state for a batch that was just
 * accepted into the output queue. Called from the daemon's output-queue push path
 * (under the queue lock, so these updates are ordered identically to the queue
 * insertions). Only persistent keyboard/mouse button transitions affect the state;
 * unicode units and pointer movement are ignored. */
void ksi_linux_synth_note_enqueued_synth(const ksi_input *inputs, size_t count)
{
    if (inputs == NULL) {
        return;
    }

    pthread_mutex_lock(&enqueued_synth_keys_mutex);

    for (size_t i = 0; i < count; i++) {
        int value;
        int key_code;

        if (inputs[i].type == KSI_INPUT_MOUSE) {
            note_enqueued_synth_mouse(&inputs[i].data.mouse);
            continue;
        }

        if (inputs[i].type != KSI_INPUT_KEYBOARD) {
            continue;
        }

        key_code = resolve_synth_key_code(&inputs[i].data.keyboard, &value);
        if (key_code >= 0 && key_code <= KEY_MAX) {
            enqueued_synth_keys_down[key_code] = value != 0;
        }
    }

    pthread_mutex_unlock(&enqueued_synth_keys_mutex);
}

/* Clear the enqueue-time logical state. Called when a RELEASE_ALL action is
 * enqueued (the grab is being dropped, so every synthetic key will be released),
 * keeping the logical state consistent with what release_all will do on drain. */
void ksi_linux_synth_reset_enqueued_synth(void)
{
    pthread_mutex_lock(&enqueued_synth_keys_mutex);
    memset(enqueued_synth_keys_down, 0, sizeof(enqueued_synth_keys_down));
    enqueued_synth_pointer_buttons = 0u;
    pthread_mutex_unlock(&enqueued_synth_keys_mutex);
}

static int send_key_stroke(int key_code)
{
    if (send_key_code(key_code, 1) != 0) {
        return -1;
    }

    return send_key_code(key_code, 0);
}


static int hex_digit_to_key(char digit)
{
    if (digit >= '0' && digit <= '9') {
        return digit == '0' ? KEY_0 : KEY_1 + (digit - '1');
    }

    if (digit >= 'a' && digit <= 'f') {
        switch (digit) {
            case 'a':
                return KEY_A;
            case 'b':
                return KEY_B;
            case 'c':
                return KEY_C;
            case 'd':
                return KEY_D;
            case 'e':
                return KEY_E;
            case 'f':
                return KEY_F;
        }
    }

    if (digit >= 'A' && digit <= 'F') {
        switch (digit) {
            case 'A':
                return KEY_A;
            case 'B':
                return KEY_B;
            case 'C':
                return KEY_C;
            case 'D':
                return KEY_D;
            case 'E':
                return KEY_E;
            case 'F':
                return KEY_F;
        }
    }

    return -1;
}

static int send_unicode_input(uint32_t codepoint)
{
    char hex[9];
    int length;
    int result = 0;

    if (codepoint == 0 || codepoint > 0x10FFFFu) {
        fprintf(stderr, "inputd: unsupported unicode codepoint U+%x\n", codepoint);
        return -1;
    }

    length = snprintf(hex, sizeof(hex), codepoint <= 0xFFFFu ? "%04x" : "%x", codepoint);

    if (length <= 0 || length >= (int)sizeof(hex)) {
        fprintf(stderr, "inputd: failed to format unicode codepoint U+%x\n", codepoint);
        return -1;
    }

    if (send_key_code(KEY_LEFTCTRL, 1) != 0
        || send_key_code(KEY_LEFTSHIFT, 1) != 0
        || send_key_code(KEY_U, 1) != 0) {
        result = -1;
    }

    if (send_key_code(KEY_U, 0) != 0) {
        result = -1;
    }

    if (send_key_code(KEY_LEFTSHIFT, 0) != 0) {
        result = -1;
    }

    if (send_key_code(KEY_LEFTCTRL, 0) != 0) {
        result = -1;
    }

    if (result != 0) {
        fprintf(stderr, "inputd: failed to start unicode input sequence U+%x: %s\n", codepoint, strerror(errno));
        return -1;
    }

    for (int i = 0; i < length; i++) {
        int key_code = hex_digit_to_key(hex[i]);

        if (key_code < 0 || send_key_stroke(key_code) != 0) {
            fprintf(stderr, "inputd: failed to emit unicode hex digit '%c' for U+%x: %s\n", hex[i], codepoint, strerror(errno));
            return -1;
        }
    }

    if (send_key_stroke(KEY_SPACE) != 0) {
        fprintf(stderr, "inputd: failed to commit unicode input U+%x: %s\n", codepoint, strerror(errno));
        return -1;
    }

    if (g_verbose) printf("inputd: synth unicode U+%x via ctrl+shift+u\n", codepoint);
    return 0;
}

static int send_unicode_utf16_unit(uint16_t unit)
{
    if (unit >= 0xD800u && unit <= 0xDBFFu) {
        pending_high_surrogate = unit;
        return 0;
    }

    if (unit >= 0xDC00u && unit <= 0xDFFFu) {
        uint16_t high = pending_high_surrogate;
        uint32_t codepoint;

        pending_high_surrogate = 0;

        if (high < 0xD800u || high > 0xDBFFu) {
            fprintf(stderr, "inputd: low unicode surrogate 0x%x without preceding high surrogate\n", unit);
            return -1;
        }

        codepoint = 0x10000u + ((((uint32_t)high - 0xD800u) << 10) | ((uint32_t)unit - 0xDC00u));
        return send_unicode_input(codepoint);
    }

    pending_high_surrogate = 0;
    return send_unicode_input(unit);
}

static int enable_event(int event_type)
{
    if (ioctl(uinput_fd, UI_SET_EVBIT, event_type) < 0) {
        fprintf(stderr, "inputd: uinput UI_SET_EVBIT(%d) failed: %s\n", event_type, strerror(errno));
        return -1;
    }

    return 0;
}

static int enable_key(int key_code)
{
    if (ioctl(uinput_fd, UI_SET_KEYBIT, key_code) < 0) {
        fprintf(stderr, "inputd: uinput UI_SET_KEYBIT(%d) failed: %s\n", key_code, strerror(errno));
        return -1;
    }

    return 0;
}

static int enable_relative(int relative_code)
{
    if (ioctl(uinput_fd, UI_SET_RELBIT, relative_code) < 0) {
        fprintf(stderr, "inputd: uinput UI_SET_RELBIT(%d) failed: %s\n", relative_code, strerror(errno));
        return -1;
    }

    return 0;
}

static int vk_to_evdev_key(uint16_t vk)
{
    return ksi_vk_to_evdev(vk);
}

static int scan_to_evdev_key(uint16_t scan, bool extended)
{
    if (scan == 0) {
        return -1;
    }

    if (extended) {
        /* Map Windows AT set-1 E0-prefixed scan codes to evdev keycodes.
         * These differ from the base (non-extended) numbering used by Linux. */
        switch (scan) {
            case 0x1Cu: return KEY_KPENTER;
            case 0x1Du: return KEY_RIGHTCTRL;
            case 0x35u: return KEY_KPSLASH;
            case 0x37u: return KEY_SYSRQ;
            case 0x38u: return KEY_RIGHTALT;
            case 0x47u: return KEY_HOME;
            case 0x48u: return KEY_UP;
            case 0x49u: return KEY_PAGEUP;
            case 0x4Bu: return KEY_LEFT;
            case 0x4Du: return KEY_RIGHT;
            case 0x4Fu: return KEY_END;
            case 0x50u: return KEY_DOWN;
            case 0x51u: return KEY_PAGEDOWN;
            case 0x52u: return KEY_INSERT;
            case 0x53u: return KEY_DELETE;
            case 0x5Bu: return KEY_LEFTMETA;
            case 0x5Cu: return KEY_RIGHTMETA;
            case 0x5Du: return KEY_COMPOSE;
            default: return -1;
        }
    }

    /* For non-extended PS/2 AT scan codes the numbering is identical to
     * Linux evdev keycodes.  This path also handles replayed hook events
     * where the scan field already contains a raw evdev keycode. */
    if (scan <= KEY_MAX) {
        return scan;
    }

    return -1;
}

static int enable_keyboard_keys(void)
{
    /* A grabbed keyboard is replayed through this device. Enable the complete
     * Linux keyboard-key ranges so keys without a Windows VK counterpart
     * (brightness, privacy, macro and similar hardware controls) pass through
     * instead of disappearing while a Keysharp hook is installed. Keep BTN_*
     * ranges off the keyboard device so libinput does not classify it as a
     * mouse, tablet, joystick or gamepad. */
    for (int key = KEY_RESERVED + 1; key <= KEY_MAX; key++) {
        if (ksi_linux_key_code_is_keyboard((unsigned int)key)
            && enable_key(key) != 0) {
            return -1;
        }
    }

    return 0;
}

static int configure_uinput_device(void)
{
    struct uinput_setup setup;

    /* Intentionally do not enable EV_REP. Keyboard synthesis emits only the
     * explicit down/up transitions requested by the caller. */
    if (enable_event(EV_KEY) != 0
        || enable_event(EV_REL) != 0) {
        return -1;
    }

    if (enable_keyboard_keys() != 0) {
        return -1;
    }

    if (enable_key(BTN_LEFT) != 0
        || enable_key(BTN_RIGHT) != 0
        || enable_key(BTN_MIDDLE) != 0
        || enable_key(BTN_SIDE) != 0
        || enable_key(BTN_EXTRA) != 0) {
        return -1;
    }

    if (enable_relative(REL_X) != 0
        || enable_relative(REL_Y) != 0
        || enable_relative(REL_WHEEL) != 0
        || enable_relative(REL_HWHEEL) != 0) {
        return -1;
    }

    memset(&setup, 0, sizeof(setup));
    (void)snprintf(setup.name, sizeof(setup.name), "%s", KSI_SYNTH_DEVICE_NAME);
    setup.id.bustype = KSI_SYNTH_DEVICE_BUSTYPE;
    setup.id.vendor = KSI_SYNTH_DEVICE_VENDOR;
    setup.id.product = KSI_SYNTH_DEVICE_PRODUCT;
    setup.id.version = KSI_SYNTH_DEVICE_VERSION;

    if (ioctl(uinput_fd, UI_DEV_SETUP, &setup) < 0) {
        fprintf(stderr, "inputd: uinput UI_DEV_SETUP failed: %s\n", strerror(errno));
        return -1;
    }

    if (ioctl(uinput_fd, UI_DEV_CREATE) < 0) {
        fprintf(stderr, "inputd: uinput UI_DEV_CREATE failed: %s\n", strerror(errno));
        return -1;
    }

    return 0;
}

static int configure_abs_uinput_device(void)
{
    struct uinput_setup setup;
    struct uinput_abs_setup abs_setup;

    /* INPUT_PROP_POINTER: tells libinput this is an absolute pointer device
     * (like a drawing tablet in mouse mode), not a touchscreen or trackpad.
     * The compositor maps the ABS range [0, 65535] directly to screen area. */
    if (ioctl(uinput_abs_fd, UI_SET_PROPBIT, INPUT_PROP_POINTER) < 0) {
        fprintf(stderr, "inputd: UI_SET_PROPBIT(INPUT_PROP_POINTER) failed: %s\n", strerror(errno));
        /* Non-fatal: proceed without the property; the device may still work
         * under xf86-input-evdev even if libinput classifies it differently. */
    }

    if (ioctl(uinput_abs_fd, UI_SET_EVBIT, EV_ABS) < 0) {
        fprintf(stderr, "inputd: abs device UI_SET_EVBIT(EV_ABS) failed: %s\n", strerror(errno));
        return -1;
    }

    if (ioctl(uinput_abs_fd, UI_SET_EVBIT, EV_KEY) < 0) {
        fprintf(stderr, "inputd: abs device UI_SET_EVBIT(EV_KEY) failed: %s\n", strerror(errno));
        return -1;
    }

    /* Buttons live on the absolute device too so button+absolute-move events
     * arrive from a single device and can be correctly ordered. The full set
     * (including the side/extra X-buttons) is enabled so any button routed here
     * after an absolute move is actually emitted rather than silently dropped
     * for lack of a keybit. */
    if (ioctl(uinput_abs_fd, UI_SET_KEYBIT, BTN_LEFT) < 0
        || ioctl(uinput_abs_fd, UI_SET_KEYBIT, BTN_RIGHT) < 0
        || ioctl(uinput_abs_fd, UI_SET_KEYBIT, BTN_MIDDLE) < 0
        || ioctl(uinput_abs_fd, UI_SET_KEYBIT, BTN_SIDE) < 0
        || ioctl(uinput_abs_fd, UI_SET_KEYBIT, BTN_EXTRA) < 0) {
        fprintf(stderr, "inputd: abs device UI_SET_KEYBIT failed: %s\n", strerror(errno));
        return -1;
    }

    if (ioctl(uinput_abs_fd, UI_SET_ABSBIT, ABS_X) < 0
        || ioctl(uinput_abs_fd, UI_SET_ABSBIT, ABS_Y) < 0) {
        fprintf(stderr, "inputd: abs device UI_SET_ABSBIT failed: %s\n", strerror(errno));
        return -1;
    }

    memset(&setup, 0, sizeof(setup));
    (void)snprintf(setup.name, sizeof(setup.name), "%s", KSI_SYNTH_ABS_DEVICE_NAME);
    setup.id.bustype = KSI_SYNTH_DEVICE_BUSTYPE;
    setup.id.vendor  = KSI_SYNTH_DEVICE_VENDOR;
    setup.id.product = KSI_SYNTH_ABS_DEVICE_PRODUCT;
    setup.id.version = KSI_SYNTH_DEVICE_VERSION;

    if (ioctl(uinput_abs_fd, UI_DEV_SETUP, &setup) < 0) {
        fprintf(stderr, "inputd: abs device UI_DEV_SETUP failed: %s\n", strerror(errno));
        return -1;
    }

    memset(&abs_setup, 0, sizeof(abs_setup));
    abs_setup.absinfo.minimum = 0;
    abs_setup.absinfo.maximum = 65535;
    abs_setup.absinfo.resolution = 1;

    abs_setup.code = ABS_X;
    if (ioctl(uinput_abs_fd, UI_ABS_SETUP, &abs_setup) < 0) {
        fprintf(stderr, "inputd: abs device UI_ABS_SETUP(ABS_X) failed: %s\n", strerror(errno));
        return -1;
    }

    abs_setup.code = ABS_Y;
    if (ioctl(uinput_abs_fd, UI_ABS_SETUP, &abs_setup) < 0) {
        fprintf(stderr, "inputd: abs device UI_ABS_SETUP(ABS_Y) failed: %s\n", strerror(errno));
        return -1;
    }

    if (ioctl(uinput_abs_fd, UI_DEV_CREATE) < 0) {
        fprintf(stderr, "inputd: abs device UI_DEV_CREATE failed: %s\n", strerror(errno));
        return -1;
    }

    return 0;
}

int ksi_linux_synth_start(void)
{
    uinput_fd = open(KSI_UINPUT_PATH, O_WRONLY | O_NONBLOCK | O_CLOEXEC);

    if (uinput_fd < 0) {
        fprintf(stderr, "inputd: cannot open %s: %s\n", KSI_UINPUT_PATH, strerror(errno));
        return 0;
    }

    if (configure_uinput_device() != 0) {
        close(uinput_fd);
        uinput_fd = -1;
        return 0;
    }

    puts("inputd: uinput relative mouse device created");

    uinput_abs_fd = open(KSI_UINPUT_PATH, O_WRONLY | O_NONBLOCK | O_CLOEXEC);

    if (uinput_abs_fd < 0) {
        fprintf(stderr, "inputd: cannot open %s for abs device: %s\n", KSI_UINPUT_PATH, strerror(errno));
        /* Non-fatal: absolute mouse moves won't work but everything else will. */
        return 0;
    }

    if (configure_abs_uinput_device() != 0) {
        close(uinput_abs_fd);
        uinput_abs_fd = -1;
        fprintf(stderr, "inputd: failed to create absolute pointer device\n");
        return 0;
    }

    puts("inputd: uinput absolute pointer device created");
    return 0;
}

void ksi_linux_synth_stop(void)
{
    ksi_linux_synth_release_all();

    if (uinput_fd >= 0) {
        (void)ioctl(uinput_fd, UI_DEV_DESTROY);
        close(uinput_fd);
        uinput_fd = -1;
        puts("inputd: uinput relative mouse device destroyed");
    }

    if (uinput_abs_fd >= 0) {
        (void)ioctl(uinput_abs_fd, UI_DEV_DESTROY);
        close(uinput_abs_fd);
        uinput_abs_fd = -1;
        puts("inputd: uinput absolute pointer device destroyed");
    }
}

bool ksi_linux_synth_is_started(void)
{
    return uinput_fd >= 0;
}

bool ksi_linux_synth_is_available(void)
{
    return ksi_linux_synth_is_started() && !synth_write_failed;
}

/* Rate limit for synth recovery: recreating the uinput devices is real
 * ioctl/UI_DEV_DESTROY/UI_DEV_CREATE work, not something to repeat on every
 * single main-loop tick if the underlying problem persists. */
#define KSI_SYNTH_RETRY_INTERVAL_MS 3000u
/* Main-thread-only (written and read only in ksi_linux_synth_needs_recovery). */
static uint64_t last_synth_retry_ms;

/* MAIN THREAD. Polled from the daemon's periodic maintenance. Notices the latch
 * set by emit_event_to() and reports (at most once per KSI_SYNTH_RETRY_INTERVAL_MS)
 * that a recreation should be requested. It deliberately does NOT touch the device:
 * the actual stop()+start() must happen on the output sequencer thread, so the
 * daemon reacts to a true return by enqueuing a KSI_OUTPUT_ACTION_RECREATE_SYNTH
 * action (which drains into ksi_linux_synth_recreate below). Without this split,
 * a uinput write failure had no consequence beyond a log line and a dropped
 * event -- the daemon kept advertising synth/hook capability and kept writing to
 * the same broken fd for the rest of its life. */
bool ksi_linux_synth_needs_recovery(void)
{
    uint64_t now;

    if (!synth_write_failed) {
        return false;
    }

    now = monotonic_ms();

    if (now != 0 && last_synth_retry_ms != 0
        && now - last_synth_retry_ms < KSI_SYNTH_RETRY_INTERVAL_MS) {
        return false;
    }

    last_synth_retry_ms = now;
    return true;
}

/* OUTPUT SEQUENCER THREAD ONLY. Drains a KSI_OUTPUT_ACTION_RECREATE_SYNTH action.
 * Runs on the sequencer so stop() (release_all + close(uinput_fd)/uinput_fd=-1)
 * and start() (reopen) cannot race the sequencer's own writes to the very fds /
 * key-down table they mutate. Preserves the write-failure re-latch so a failed
 * reopen keeps needs_recovery() returning true on the next pass instead of the
 * daemon silently sitting on a dead synth output. */
void ksi_linux_synth_recreate(void)
{
    fprintf(stderr, "inputd: synthetic output device write failed; recreating uinput devices\n");
    ksi_linux_synth_stop();
    synth_write_failed = false;
    (void)ksi_linux_synth_start();

    if (uinput_fd < 0) {
        /* ksi_linux_synth_start() already logged the specific reason. Mark
         * broken again so the next needs_recovery() poll re-requests recovery. */
        synth_write_failed = true;
    }
}

/* Resolve a keyboard synth input to the evdev key code it toggles and its
 * up/down value (*out_value: 1 = down, 0 = up). Returns -1 for inputs that do
 * not map to a single persistent key transition — unicode units (emitted as a
 * self-contained sequence that leaves nothing held) and unsupported vk/scan.
 * Shared by the drain path (send_keyboard_input) and the enqueue-time logical
 * tracker (ksi_linux_synth_note_enqueued_synth) so both agree on exactly which
 * key each input toggles. */
static int resolve_synth_key_code(const ksi_keybdinput *input, int *out_value)
{
    int key_code = -1;

    if (out_value != NULL) {
        *out_value = (input->flags & KSI_KEYEVENTF_KEYUP) != 0 ? 0 : 1;
    }

    if ((input->flags & KSI_KEYEVENTF_UNICODE) != 0) {
        return -1;
    }

    if ((input->flags & KSI_KEYEVENTF_SCANCODE) != 0) {
        bool extended = (input->flags & KSI_KEYEVENTF_EXTENDEDKEY) != 0;
        key_code = scan_to_evdev_key(input->scan, extended);
    }

    if (key_code < 0
        && input->vk == 0x0Du
        && (input->flags & KSI_KEYEVENTF_EXTENDEDKEY) != 0) {
        key_code = KEY_KPENTER;
    }

    if (key_code < 0) {
        key_code = vk_to_evdev_key(input->vk);
    }

    return key_code;
}

static bool keyboard_input_to_hook_event(
    const ksi_keybdinput *input,
    ksi_keyboard_hook_event *event)
{
    bool key_up;
    uint32_t flags;

    if (input == NULL || event == NULL) {
        return false;
    }

    memset(event, 0, sizeof(*event));
    key_up = (input->flags & KSI_KEYEVENTF_KEYUP) != 0;
    flags = KSI_LLKHF_INJECTED | keyboard_indicator_flags_for_hook();

    if (key_up) {
        flags |= KSI_LLKHF_UP;
    }

    if ((input->flags & KSI_KEYEVENTF_EXTENDEDKEY) != 0) {
        flags |= KSI_LLKHF_EXTENDED;
    }

    event->message = key_up ? KSI_WM_KEYUP : KSI_WM_KEYDOWN;
    event->flags = flags;
    event->time_ms = synth_hook_time_ms(input->time);
    event->extra_info = input->extra_info;

    if ((input->flags & KSI_KEYEVENTF_UNICODE) != 0) {
        event->vk_code = KSI_VK_PACKET;
        event->scan_code = input->scan;
        return true;
    }

    {
        int value;
        int key_code = resolve_synth_key_code(input, &value);

        (void)value;

        if (key_code >= 0) {
            event->scan_code = (uint32_t)key_code;
            event->vk_code = ksi_evdev_to_vk((unsigned int)key_code);
        }
    }

    if (event->vk_code == 0u) {
        event->vk_code = input->vk;
    }

    if (event->scan_code == 0u) {
        event->scan_code = input->scan;
    }

    return event->vk_code != 0u || event->scan_code != 0u;
}

static bool mouse_input_to_hook_event(
    const ksi_mouseinput *input,
    ksi_mouse_hook_event *event)
{
    if (input == NULL || event == NULL) {
        return false;
    }

    memset(event, 0, sizeof(*event));
    event->flags = KSI_LLMHF_INJECTED;
    event->time_ms = synth_hook_time_ms(input->time);
    event->extra_info = input->extra_info;

    if ((input->flags & KSI_MOUSEEVENTF_MOVE) != 0) {
        event->message = KSI_WM_MOUSEMOVE;
        event->x = input->dx;
        event->y = input->dy;

        if ((input->flags & KSI_MOUSEEVENTF_ABSOLUTE) != 0) {
            event->mouse_data = KSI_MOUSEEVENTF_ABSOLUTE;
        } else {
            event->delta_x = input->dx;
            event->delta_y = input->dy;
        }

        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_WHEEL) != 0) {
        event->message = KSI_WM_MOUSEWHEEL;
        event->mouse_data = input->mouse_data << 16;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_HWHEEL) != 0) {
        event->message = KSI_WM_MOUSEHWHEEL;
        event->mouse_data = input->mouse_data << 16;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_LEFTDOWN) != 0) {
        event->message = KSI_WM_LBUTTONDOWN;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_LEFTUP) != 0) {
        event->message = KSI_WM_LBUTTONUP;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_RIGHTDOWN) != 0) {
        event->message = KSI_WM_RBUTTONDOWN;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_RIGHTUP) != 0) {
        event->message = KSI_WM_RBUTTONUP;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEDOWN) != 0) {
        event->message = KSI_WM_MBUTTONDOWN;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEUP) != 0) {
        event->message = KSI_WM_MBUTTONUP;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_XDOWN) != 0) {
        event->message = KSI_WM_XBUTTONDOWN;
        event->mouse_data = input->mouse_data;
        return true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_XUP) != 0) {
        event->message = KSI_WM_XBUTTONUP;
        event->mouse_data = input->mouse_data;
        return true;
    }

    return false;
}

bool ksi_linux_synth_input_to_hook_event(
    const ksi_input *input,
    uint32_t *hook_type,
    ksi_hook_event_payload *event,
    size_t *event_size)
{
    if (input == NULL || hook_type == NULL || event == NULL || event_size == NULL) {
        return false;
    }

    memset(event, 0, sizeof(*event));

    if (input->type == KSI_INPUT_KEYBOARD) {
        if (!keyboard_input_to_hook_event(&input->data.keyboard, &event->event.keyboard)) {
            return false;
        }

        *hook_type = KSI_HOOK_KEYBOARD_LL;
        event->hook_type = *hook_type;
        *event_size = sizeof(event->event.keyboard);
        return true;
    }

    if (input->type == KSI_INPUT_MOUSE) {
        if (!mouse_input_to_hook_event(&input->data.mouse, &event->event.mouse)) {
            return false;
        }

        *hook_type = KSI_HOOK_MOUSE_LL;
        event->hook_type = *hook_type;
        *event_size = sizeof(event->event.mouse);
        return true;
    }

    return false;
}

static int send_keyboard_input(const ksi_keybdinput *input)
{
    int key_code;
    int value;

    if ((input->flags & KSI_KEYEVENTF_UNICODE) != 0) {
        if ((input->flags & KSI_KEYEVENTF_KEYUP) != 0) {
            return 0;
        }

        return send_unicode_utf16_unit(input->scan);
    }

    key_code = resolve_synth_key_code(input, &value);

    if (key_code < 0) {
        fprintf(stderr, "inputd: unsupported keyboard input vk=0x%x scan=%u flags=0x%x\n",
            input->vk,
            input->scan,
            input->flags);
        return -1;
    }

    /* SendInput/keybd_event semantics are explicit transitions only. Do not
     * emit EV_KEY value 2: that marks autorepeat from a held physical key. */
    if (send_key_code(key_code, value) != 0) {
        fprintf(stderr, "inputd: failed to emit keyboard input: %s\n", strerror(errno));
        return -1;
    }

    if (g_verbose) {
        printf("inputd: synth key vk=0x%x scan=%u evdev=%d %s\n",
            input->vk,
            input->scan,
            key_code,
            value == 0 ? "up" : "down");
    }

    return 0;
}

void ksi_linux_synth_release_all(void)
{
    bool emitted = false;

    if (uinput_fd < 0) {
        memset(synthesized_keys_down, 0, sizeof(synthesized_keys_down));
        return;
    }

    for (int key_code = 0; key_code <= KEY_MAX; key_code++) {
        if (!synthesized_keys_down[key_code]) {
            continue;
        }

        if (emit_event(EV_KEY, (uint16_t)key_code, 0) == 0) {
            if (g_verbose) printf("inputd: release synthetic key evdev=%d\n", key_code);
            emitted = true;
        }

        synthesized_keys_down[key_code] = false;
    }

    if (emitted && emit_sync() != 0) {
        fprintf(stderr, "inputd: failed to sync synthetic key release: %s\n", strerror(errno));
    }
}

static int send_mouse_button(int fd, uint16_t button, bool down)
{
    if (emit_event_to(fd, EV_KEY, button, down ? 1 : 0) != 0) {
        return -1;
    }

    return 0;
}

static uint16_t mouse_data_to_xbutton(uint32_t mouse_data)
{
    uint32_t xbutton = mouse_data >> 16;

    if (xbutton == KSI_XBUTTON1) {
        return BTN_SIDE;
    }

    if (xbutton == KSI_XBUTTON2) {
        return BTN_EXTRA;
    }

    return 0;
}

/* Which synthetic device (absolute vs relative) currently holds each pressed
 * mouse button, indexed by (button - BTN_LEFT). Lets a button-UP release on the
 * same device its DOWN went to even across batches, so a `Click x,y Down` (down
 * routed to the abs device) followed by a move-less `Click Up` cannot strand a
 * button held down on the absolute device — release_all only sweeps keyboard
 * keys, never mouse buttons. Touched only on the single output-sequencer thread,
 * so it needs no locking. */
#define KSI_TRACKED_BTN_BASE BTN_LEFT
#define KSI_TRACKED_BTN_COUNT 8
static bool button_held_on_abs[KSI_TRACKED_BTN_COUNT];

static bool *tracked_button_slot(uint16_t btn)
{
    int idx = (int)btn - KSI_TRACKED_BTN_BASE;

    if (idx < 0 || idx >= KSI_TRACKED_BTN_COUNT) {
        return NULL;
    }

    return &button_held_on_abs[idx];
}

/* Route one button transition to the correct device and flag that device's SYN.
 * A DOWN uses this batch's device (the abs device iff this batch positioned and
 * that device exists) and records where it went; an UP releases on the recorded
 * device, falling back to the relative device when there is no record or the abs
 * device has since disappeared. */
static int route_mouse_button(uint16_t btn, bool down, bool abs_active,
                              bool *abs_pending, bool *rel_pending)
{
    bool *slot = tracked_button_slot(btn);
    bool on_abs = down ? abs_active : (slot != NULL ? *slot : abs_active);
    int fd;

    if (on_abs && uinput_abs_fd >= 0) {
        fd = uinput_abs_fd;
    } else {
        fd = uinput_fd;
        on_abs = false;
    }

    if (send_mouse_button(fd, btn, down) != 0) {
        return -1;
    }

    if (slot != NULL) {
        *slot = down ? on_abs : false;
    }

    if (on_abs) {
        *abs_pending = true;
    } else {
        *rel_pending = true;
    }

    return 0;
}

static int send_mouse_input(const ksi_mouseinput *input, bool *batch_used_abs_move)
{
    uint16_t xbutton;
    bool abs_move_active;
    bool rel_pending = false; /* relative-device events awaiting a SYN */
    bool abs_pending = false; /* absolute-device button events awaiting a SYN */

    if ((input->flags & KSI_MOUSEEVENTF_MOVE) != 0) {
        if ((input->flags & KSI_MOUSEEVENTF_ABSOLUTE) != 0) {
            if (uinput_abs_fd < 0) {
                fprintf(stderr, "inputd: absolute mouse move dropped: abs device unavailable\n");
            } else if (emit_abs_event(ABS_X, input->dx) != 0
                       || emit_abs_event(ABS_Y, input->dy) != 0
                       || emit_abs_sync() != 0) {
                return -1;
            } else if (batch_used_abs_move != NULL) {
                /* Remember, for this batch, that an absolute position was set:
                 * buttons that follow (in this input or a later one) must be
                 * emitted on the SAME (absolute) device so libinput cannot
                 * process the click before the reposition and land it at the
                 * old location. */
                *batch_used_abs_move = true;
            }
        } else {
            if (input->dx != 0 && emit_event(EV_REL, REL_X, input->dx) != 0) {
                return -1;
            }

            if (input->dy != 0 && emit_event(EV_REL, REL_Y, input->dy) != 0) {
                return -1;
            }

            if (input->dx != 0 || input->dy != 0) {
                rel_pending = true;
            }
        }
    }

    if ((input->flags & KSI_MOUSEEVENTF_WHEEL) != 0) {
        int32_t delta = (int32_t)input->mouse_data / 120;

        if (delta == 0 && input->mouse_data != 0) {
            delta = input->mouse_data > 0 ? 1 : -1;
        }

        if (emit_event(EV_REL, REL_WHEEL, delta) != 0) {
            return -1;
        }

        rel_pending = true;
    }

    if ((input->flags & KSI_MOUSEEVENTF_HWHEEL) != 0) {
        int32_t delta = (int32_t)input->mouse_data / 120;

        if (delta == 0 && input->mouse_data != 0) {
            delta = input->mouse_data > 0 ? 1 : -1;
        }

        if (emit_event(EV_REL, REL_HWHEEL, delta) != 0) {
            return -1;
        }

        rel_pending = true;
    }

    /* Route each button transition to the correct device. A DOWN goes to this
     * batch's device (the absolute device iff this batch established a position
     * and that device exists); an UP is released on the device its DOWN went to
     * — tracked across batches by route_mouse_button — so a `Click x,y Down`
     * then a later move-less `Click Up` cannot strand a button held on the
     * absolute device. Each call marks the device it actually used SYN-pending. */
    abs_move_active = batch_used_abs_move != NULL && *batch_used_abs_move && uinput_abs_fd >= 0;

    if ((input->flags & KSI_MOUSEEVENTF_LEFTDOWN) != 0 && route_mouse_button(BTN_LEFT, true, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    if ((input->flags & KSI_MOUSEEVENTF_LEFTUP) != 0 && route_mouse_button(BTN_LEFT, false, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    if ((input->flags & KSI_MOUSEEVENTF_RIGHTDOWN) != 0 && route_mouse_button(BTN_RIGHT, true, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    if ((input->flags & KSI_MOUSEEVENTF_RIGHTUP) != 0 && route_mouse_button(BTN_RIGHT, false, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEDOWN) != 0 && route_mouse_button(BTN_MIDDLE, true, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    if ((input->flags & KSI_MOUSEEVENTF_MIDDLEUP) != 0 && route_mouse_button(BTN_MIDDLE, false, abs_move_active, &abs_pending, &rel_pending) != 0) {
        return -1;
    }

    xbutton = mouse_data_to_xbutton(input->mouse_data);

    if ((input->flags & KSI_MOUSEEVENTF_XDOWN) != 0) {
        if (xbutton == 0 || route_mouse_button(xbutton, true, abs_move_active, &abs_pending, &rel_pending) != 0) {
            return -1;
        }
    }

    if ((input->flags & KSI_MOUSEEVENTF_XUP) != 0) {
        if (xbutton == 0 || route_mouse_button(xbutton, false, abs_move_active, &abs_pending, &rel_pending) != 0) {
            return -1;
        }
    }

    /* Commit each device that actually received events with its own SYN. The
     * absolute move above already emitted its own abs SYN. */
    if (rel_pending && emit_sync() != 0) {
        fprintf(stderr, "inputd: failed to emit mouse input: %s\n", strerror(errno));
        return -1;
    }

    if (abs_pending && emit_abs_sync() != 0) {
        fprintf(stderr, "inputd: failed to emit absolute mouse button: %s\n", strerror(errno));
        return -1;
    }

    if (g_verbose) {
        printf("inputd: synth mouse dx=%d dy=%d data=%u flags=0x%x\n",
            input->dx,
            input->dy,
            input->mouse_data,
            input->flags);
    }

    return 0;
}

int ksi_linux_synth_send_input(const ksi_input *inputs, size_t count, uint32_t flags)
{
    int result = 0;

    if (uinput_fd < 0) {
        fprintf(stderr, "inputd: synthesis unavailable; %s could not be opened\n", KSI_UINPUT_PATH);
        return -1;
    }

    /* pending_high_surrogate is a single process-global (see its declaration):
     * it must never carry an unpaired high surrogate left over from a
     * previous, unrelated batch (e.g. one client's malformed/truncated
     * SendText ending mid-surrogate) into THIS batch, where it could splice
     * with an unrelated leading low surrogate and emit the wrong codepoint.
     * Each top-level CLIENT batch is expected to be well-formed UTF-16 on its
     * own, so starting every client batch with a clean slate is correct.
     *
     * A BATCH_FRAGMENT push is NOT a client batch: it is one event of an
     * already-received batch that the synthetic-hook routing re-emits
     * individually (hook_ingress.inc), so a surrogate pair legitimately spans
     * two fragment calls. Resetting between fragments discarded the high half
     * and silently dropped every astral character (emoji) whenever a keyboard
     * hook was installed — so fragments keep the pending state. */
    /* Reset the pending high surrogate at genuine client-batch boundaries only.
     * Skip the reset for: a physical replay (REPLAY — carries no surrogate state
     * and may be interleaved between a pair's two fragments), and a non-first
     * fragment of a batch (BATCH_FRAGMENT without BATCH_START — the pair spans
     * fragments). DO reset for a plain top-level client batch (no flags) and for
     * the FIRST fragment of a batch (BATCH_START), which re-arms the boundary. */
    if ((flags & KSI_SYNTH_FLAG_REPLAY) == 0u
            && ((flags & KSI_SYNTH_FLAG_BATCH_FRAGMENT) == 0u
                || (flags & KSI_SYNTH_FLAG_BATCH_START) != 0u)) {
        pending_high_surrogate = 0;
    }

    /* Enable per-event pacing for this batch (see emit_event_to). The chunk
     * counter is deliberately NOT reset here: it is shared and persists across
     * calls so a burst of single-event passthrough replays paces like one long
     * synthesis batch. emit_event_to resets it after an idle gap instead, which
     * keeps isolated Sends/keystrokes free without a per-batch reset. */
    synth_pacing_active = true;

    /* Per-batch: once any input performs an absolute MouseMove, subsequent
     * button presses in this same batch are emitted on the absolute device so
     * position+click stay atomic on one device (see send_mouse_input). */
    bool used_abs_move = false;
    int had_error = 0;

    for (size_t i = 0; i < count; i++) {
        if (inputs[i].type == KSI_INPUT_KEYBOARD) {
            result = send_keyboard_input(&inputs[i].data.keyboard);
        } else if (inputs[i].type == KSI_INPUT_MOUSE) {
            result = send_mouse_input(&inputs[i].data.mouse, &used_abs_move);
        } else {
            fprintf(stderr, "inputd: unsupported input type %u\n", inputs[i].type);
            result = -1;
        }

        if (result != 0) {
            had_error = 1;

            /* A data-validation failure (unmappable codepoint/vk, lone surrogate)
             * must NOT drop the rest of the batch — skip just this input and keep
             * going, matching Windows SendInput (which never rejects individual
             * KEYEVENTF_UNICODE events). Only a genuine device write failure
             * (synth_write_failed, latched in emit_event_to) aborts, since
             * continuing would only spew failed writes until the recovery poll
             * rebuilds the device. */
            if (synth_write_failed) {
                break;
            }
        }
    }

    synth_pacing_active = false;
    return had_error ? -1 : 0;
}

static int replay_keyboard_hook_event(const ksi_keyboard_hook_event *event)
{
    ksi_input input;

    memset(&input, 0, sizeof(input));
    input.type = KSI_INPUT_KEYBOARD;
    input.data.keyboard.vk = (uint16_t)event->vk_code;
    input.data.keyboard.scan = (uint16_t)event->scan_code;
    input.data.keyboard.flags = KSI_KEYEVENTF_SCANCODE;

    if ((event->flags & KSI_LLKHF_UP) != 0) {
        input.data.keyboard.flags |= KSI_KEYEVENTF_KEYUP;
    }

    return ksi_linux_synth_send_input(&input, 1,
        KSI_SYNTH_FLAG_BYPASS_HOOK | KSI_SYNTH_FLAG_REPLAY);
}

static int replay_mouse_hook_event(const ksi_mouse_hook_event *event)
{
    ksi_input input;

    memset(&input, 0, sizeof(input));
    input.type = KSI_INPUT_MOUSE;

    switch (event->message) {
        case KSI_WM_MOUSEMOVE:
            input.data.mouse.flags = KSI_MOUSEEVENTF_MOVE;

            if ((event->mouse_data & KSI_MOUSEEVENTF_ABSOLUTE) != 0) {
                input.data.mouse.flags |= KSI_MOUSEEVENTF_ABSOLUTE;
            }

            input.data.mouse.dx = event->x;
            input.data.mouse.dy = event->y;
            break;
        case KSI_WM_LBUTTONDOWN:
            input.data.mouse.flags = KSI_MOUSEEVENTF_LEFTDOWN;
            break;
        case KSI_WM_LBUTTONUP:
            input.data.mouse.flags = KSI_MOUSEEVENTF_LEFTUP;
            break;
        case KSI_WM_RBUTTONDOWN:
            input.data.mouse.flags = KSI_MOUSEEVENTF_RIGHTDOWN;
            break;
        case KSI_WM_RBUTTONUP:
            input.data.mouse.flags = KSI_MOUSEEVENTF_RIGHTUP;
            break;
        case KSI_WM_MBUTTONDOWN:
            input.data.mouse.flags = KSI_MOUSEEVENTF_MIDDLEDOWN;
            break;
        case KSI_WM_MBUTTONUP:
            input.data.mouse.flags = KSI_MOUSEEVENTF_MIDDLEUP;
            break;
        case KSI_WM_MOUSEWHEEL:
            input.data.mouse.flags = KSI_MOUSEEVENTF_WHEEL;
            input.data.mouse.mouse_data = (uint32_t)((int32_t)event->mouse_data >> 16);
            break;
        case KSI_WM_MOUSEHWHEEL:
            input.data.mouse.flags = KSI_MOUSEEVENTF_HWHEEL;
            input.data.mouse.mouse_data = (uint32_t)((int32_t)event->mouse_data >> 16);
            break;
        case KSI_WM_XBUTTONDOWN:
            input.data.mouse.flags = KSI_MOUSEEVENTF_XDOWN;
            input.data.mouse.mouse_data = event->mouse_data;
            break;
        case KSI_WM_XBUTTONUP:
            input.data.mouse.flags = KSI_MOUSEEVENTF_XUP;
            input.data.mouse.mouse_data = event->mouse_data;
            break;
        default:
            fprintf(stderr, "inputd: unsupported mouse hook replay message=0x%x\n", event->message);
            return -1;
    }

    return ksi_linux_synth_send_input(&input, 1,
        KSI_SYNTH_FLAG_BYPASS_HOOK | KSI_SYNTH_FLAG_REPLAY);
}

int ksi_linux_synth_replay_hook_event(uint32_t hook_type, const ksi_hook_event_payload *event)
{
    int result;

    if (event == NULL) {
        return -1;
    }

    if (hook_type == KSI_HOOK_KEYBOARD_LL) {
        result = replay_keyboard_hook_event(&event->event.keyboard);
    } else if (hook_type == KSI_HOOK_MOUSE_LL) {
        result = replay_mouse_hook_event(&event->event.mouse);
    } else {
        result = -1;
    }

    return result;
}
