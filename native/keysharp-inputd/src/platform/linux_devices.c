#include "keysharp_inputd/linux_devices.h"

#include "keysharp_inputd/globals.h"
#include "keysharp_inputd/linux_synth.h"
#include "keysharp_inputd/protocol.h"
#include "linux_device_filter.h"
#include "vk_evdev.h"

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <linux/input.h>
#include <libevdev/libevdev.h>
#include <libudev.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/ioctl.h>
#include <time.h>
#include <unistd.h>

#define KSI_INPUT_DIR "/dev/input"
#define KSI_EVENT_PREFIX "event"
#define KSI_DEVICE_NAME_LENGTH 256
#define KSI_MAX_TRACKED_DEVICES 128
#define KSI_MAX_DEVICE_EVENTS_PER_PASS 256
#define KSI_MAX_UDEV_EVENTS_PER_PASS 64

#define KSI_BITS_PER_LONG (sizeof(unsigned long) * CHAR_BIT)
#define KSI_BIT_WORD(bit) ((bit) / KSI_BITS_PER_LONG)
#define KSI_BIT_MASK(bit) (1UL << ((bit) % KSI_BITS_PER_LONG))
#define KSI_BIT_ARRAY_LENGTH(max_bit) (((max_bit) / KSI_BITS_PER_LONG) + 1)

typedef struct ksi_linux_device_info {
    char path[PATH_MAX];
    char name[KSI_DEVICE_NAME_LENGTH];
    bool has_keys;
    bool has_relative;
    bool has_absolute;
    bool has_keyboard_keys;
    bool has_mouse_buttons;
    bool has_pointer_axes;
    bool requires_compositor_processing;
    uint16_t bustype;
    uint16_t vendor;
    uint16_t product;
    uint16_t version;
    bool is_synth_device;
} ksi_linux_device_info;

typedef struct ksi_linux_tracked_device {
    char path[PATH_MAX];
    char name[KSI_DEVICE_NAME_LENGTH];
    int fd;
    struct libevdev *evdev;
    uint32_t device_id;
    bool grabbed;
    bool keyboard_candidate;
    bool mouse_candidate;
    bool mouse_hook_candidate;
    bool mouse_block_candidate;
    bool injected_source;
    bool event_clock_monotonic;
    bool grab_deferred;
    bool has_buffered_event;
    struct input_event buffered_event;
    unsigned long physical_down_keys[KSI_BIT_ARRAY_LENGTH(KEY_MAX)];
    unsigned long deferred_down_keys[KSI_BIT_ARRAY_LENGTH(KEY_MAX)];
    bool has_pending_rel;
    int32_t pending_rel_x;
    int32_t pending_rel_y;
    uint64_t pending_rel_time_ms;
    uint64_t pending_rel_extra_info;
    bool pending_rel_injected;
    bool has_pending_abs;
    int32_t pending_abs_start_x;
    int32_t pending_abs_start_y;
    int32_t current_abs_x;
    int32_t current_abs_y;
    int32_t current_abs_x_raw;
    int32_t current_abs_y_raw;
    int32_t abs_x_min;
    int32_t abs_x_max;
    int32_t abs_y_min;
    int32_t abs_y_max;
    uint64_t pending_abs_time_ms;
    uint64_t pending_abs_extra_info;
    bool pending_abs_injected;
} ksi_linux_tracked_device;

static ksi_linux_tracked_device tracked_devices[KSI_MAX_TRACKED_DEVICES];
static size_t tracked_device_count;
static struct udev *udev_context;
static struct udev_monitor *udev_monitor;
static uint32_t next_device_id = 1;
static ksi_hook_event_callback hook_event_callback;
static void *hook_event_context;
static ksi_physical_key_event_callback physical_key_event_callback;
static void *physical_key_event_context;
static uint32_t grab_hook_mask;
static uint32_t block_input_mask;
/* Set when the most recent set_grab_masks() could not apply the requested grab
 * state to every device. Forces the next call to re-evaluate (rather than
 * short-circuit on an unchanged mask) so a transient grab failure is retried. */
static bool grab_state_incomplete;

/* Current LED state, updated from EV_LED events on grabbed keyboards.
 * Included in every keyboard hook event so the C# side can maintain an
 * accurate indicator snapshot without a separate IPC round-trip. */
static bool current_caps_lock;
static bool current_num_lock;
static bool current_scroll_lock;
static ksi_pointer_position_payload current_pointer_position;
/* Device ingestion and protocol queries run on the daemon's main poll thread,
 * so this field is read and written serially. Zero means no observed input. */
static uint64_t last_input_activity_ms;

static void refresh_indicator_state_from_device(const ksi_linux_tracked_device *device);
static int set_grab_masks(uint32_t hook_mask, uint32_t block_mask);

static bool test_bit(const unsigned long *bits, int bit)
{
    return (bits[KSI_BIT_WORD((size_t)bit)] & KSI_BIT_MASK((size_t)bit)) != 0;
}

static void clear_bit(unsigned long *bits, int bit)
{
    bits[KSI_BIT_WORD((size_t)bit)] &= ~KSI_BIT_MASK((size_t)bit);
}

static void set_bit(unsigned long *bits, int bit)
{
    bits[KSI_BIT_WORD((size_t)bit)] |= KSI_BIT_MASK((size_t)bit);
}

static void set_payload_key_bit(uint8_t *keys, unsigned int code)
{
    size_t byte_index;

    if (keys == NULL || code >= KSI_KEY_STATE_BITMAP_BITS) {
        return;
    }

    byte_index = (size_t)code >> 3;
    keys[byte_index] |= (uint8_t)(1u << (code & 7u));
}

static bool payload_key_bit_is_set(const uint8_t *keys, unsigned int code)
{
    size_t byte_index;

    if (keys == NULL || code >= KSI_KEY_STATE_BITMAP_BITS) {
        return false;
    }

    byte_index = (size_t)code >> 3;
    return (keys[byte_index] & (uint8_t)(1u << (code & 7u))) != 0u;
}

static bool any_bit_set(const unsigned long *bits, size_t length)
{
    for (size_t i = 0; i < length; i++) {
        if (bits[i] != 0) {
            return true;
        }
    }

    return false;
}

static bool is_keyboard_key_code(unsigned int code)
{
    return ksi_linux_key_code_is_keyboard(code);
}

static bool is_mouse_button_code(unsigned int code)
{
    return ksi_linux_pointer_button_mask(code) != 0u;
}

static bool looks_like_keyboard(const unsigned long *key_bits)
{
    for (unsigned int code = KEY_RESERVED + 1u; code <= KEY_MAX; code++) {
        if (is_keyboard_key_code(code) && test_bit(key_bits, (int)code)) {
            return true;
        }
    }

    return false;
}

static bool looks_like_mouse_buttons(const unsigned long *key_bits)
{
    return test_bit(key_bits, BTN_LEFT)
        || test_bit(key_bits, BTN_RIGHT)
        || test_bit(key_bits, BTN_MIDDLE)
        || test_bit(key_bits, BTN_SIDE)
        || test_bit(key_bits, BTN_EXTRA)
        || test_bit(key_bits, BTN_FORWARD)
        || test_bit(key_bits, BTN_BACK);
}

static bool looks_like_compositor_processed_pointer(const unsigned long *key_bits)
{
    return test_bit(key_bits, BTN_TOOL_FINGER)
        || test_bit(key_bits, BTN_TOOL_PEN)
        || test_bit(key_bits, BTN_TOOL_RUBBER);
}

static bool looks_like_pointer_axes(const unsigned long *rel_bits, const unsigned long *abs_bits)
{
    return (test_bit(rel_bits, REL_X) && test_bit(rel_bits, REL_Y))
        || (test_bit(abs_bits, ABS_X) && test_bit(abs_bits, ABS_Y));
}

static bool is_keysharp_synth_device_identity(
    const char *name,
    uint16_t bustype,
    uint16_t vendor,
    uint16_t product)
{
    bool name_matches = name != NULL
        && (strcmp(name, KSI_SYNTH_DEVICE_NAME) == 0
            || strcmp(name, KSI_SYNTH_ABS_DEVICE_NAME) == 0);
    bool id_matches = bustype == KSI_SYNTH_DEVICE_BUSTYPE
        && vendor == KSI_SYNTH_DEVICE_VENDOR
        && (product == KSI_SYNTH_DEVICE_PRODUCT
            || product == KSI_SYNTH_ABS_DEVICE_PRODUCT);

    return name_matches && id_matches;
}

static int open_event_fd(const char *path)
{
    /* Every evdev open file description owns an independent event queue: these
     * reads cannot consume or suppress events for libinput, keyd, games, or
     * another observer. Interception is a separate EVIOCGRAB operation and is
     * issued only from set_grab_masks() for a non-zero hook/BlockInput mask.
     * EVIOCSCLOCKID below changes timestamps for this fd only. */
    /* O_CLOEXEC atomically at open (vs a post-open fcntl, which races a
     * concurrent fork+exec): a spawned helper (systemctl/udevadm/prompt worker)
     * must never inherit an evdev grab fd, or the grab would outlive this daemon
     * and strand the user's input even after the daemon is killed. */
    int fd = open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);

    if (fd < 0) {
        fprintf(stderr, "inputd: cannot open %s: %s\n", path, strerror(errno));
        return -1;
    }

    return fd;
}

static int read_device_info(const char *path, ksi_linux_device_info *info)
{
    unsigned long event_bits[KSI_BIT_ARRAY_LENGTH(EV_MAX)];
    unsigned long key_bits[KSI_BIT_ARRAY_LENGTH(KEY_MAX)];
    unsigned long rel_bits[KSI_BIT_ARRAY_LENGTH(REL_MAX)];
    unsigned long abs_bits[KSI_BIT_ARRAY_LENGTH(ABS_MAX)];
    struct input_id input_id;
    int fd;

    memset(info, 0, sizeof(*info));
    memset(event_bits, 0, sizeof(event_bits));
    memset(key_bits, 0, sizeof(key_bits));
    memset(rel_bits, 0, sizeof(rel_bits));
    memset(abs_bits, 0, sizeof(abs_bits));

    (void)snprintf(info->path, sizeof(info->path), "%s", path);

    fd = open_event_fd(path);

    if (fd < 0) {
        return -1;
    }

    if (ioctl(fd, EVIOCGNAME(sizeof(info->name)), info->name) < 0) {
        (void)snprintf(info->name, sizeof(info->name), "unknown");
    }

    memset(&input_id, 0, sizeof(input_id));

    if (ioctl(fd, EVIOCGID, &input_id) == 0) {
        info->bustype = input_id.bustype;
        info->vendor = input_id.vendor;
        info->product = input_id.product;
        info->version = input_id.version;
    }

    if (ioctl(fd, EVIOCGBIT(0, sizeof(event_bits)), event_bits) < 0) {
        fprintf(stderr, "inputd: cannot read capabilities for %s: %s\n", path, strerror(errno));
        close(fd);
        return -1;
    }

    info->has_keys = test_bit(event_bits, EV_KEY);
    info->has_relative = test_bit(event_bits, EV_REL);
    info->has_absolute = test_bit(event_bits, EV_ABS);

    if (info->has_keys && ioctl(fd, EVIOCGBIT(EV_KEY, sizeof(key_bits)), key_bits) < 0) {
        fprintf(stderr, "inputd: cannot read key capabilities for %s: %s\n", path, strerror(errno));
        close(fd);
        return -1;
    }

    if (info->has_relative && ioctl(fd, EVIOCGBIT(EV_REL, sizeof(rel_bits)), rel_bits) < 0) {
        fprintf(stderr, "inputd: cannot read relative-axis capabilities for %s: %s\n", path, strerror(errno));
        close(fd);
        return -1;
    }

    if (info->has_absolute && ioctl(fd, EVIOCGBIT(EV_ABS, sizeof(abs_bits)), abs_bits) < 0) {
        fprintf(stderr, "inputd: cannot read absolute-axis capabilities for %s: %s\n", path, strerror(errno));
        close(fd);
        return -1;
    }

    info->has_keyboard_keys = info->has_keys && looks_like_keyboard(key_bits);
    info->has_mouse_buttons = info->has_keys && looks_like_mouse_buttons(key_bits);
    info->has_pointer_axes = looks_like_pointer_axes(rel_bits, abs_bits);
    info->requires_compositor_processing = info->has_absolute
        && looks_like_compositor_processed_pointer(key_bits);
    info->is_synth_device = is_keysharp_synth_device_identity(
        info->name,
        info->bustype,
        info->vendor,
        info->product);

    close(fd);
    return 0;
}

static bool is_event_device_name(const char *name)
{
    return strncmp(name, KSI_EVENT_PREFIX, strlen(KSI_EVENT_PREFIX)) == 0;
}

static bool is_event_device_path(const char *path)
{
    const char *last_slash;

    if (path == NULL) {
        return false;
    }

    last_slash = strrchr(path, '/');
    return last_slash != NULL && is_event_device_name(last_slash + 1);
}

/* ID_SEAT is normally attached to the event node itself, but custom udev
 * rules may put it on an input parent. Walk the chain before applying the
 * documented default (no ID_SEAT anywhere means seat0). A missing or not-yet-
 * initialized udev object is different from a confirmed missing property:
 * metadata lookup races fail closed so a non-seat0 device is never opened
 * merely because its udev database record is incomplete. */
static bool udev_device_is_on_seat0(
    struct udev_device *device,
    const char **out_id_seat,
    bool *out_metadata_initialized)
{
    struct udev_device *current;
    const char *id_seat = NULL;
    bool metadata_initialized = false;

    if (device != NULL) {
        metadata_initialized = udev_device_get_is_initialized(device) > 0;
    }

    if (out_metadata_initialized != NULL) {
        *out_metadata_initialized = metadata_initialized;
    }

    if (metadata_initialized) {
        for (current = device; current != NULL; current = udev_device_get_parent(current)) {
            id_seat = ksi_linux_device_prefer_nearest_id_seat(
                id_seat,
                udev_device_get_property_value(current, "ID_SEAT"));

            if (id_seat != NULL) {
                break;
            }
        }
    }

    if (out_id_seat != NULL) {
        *out_id_seat = id_seat;
    }

    return ksi_linux_device_seat_metadata_is_admitted(
        metadata_initialized,
        id_seat);
}

static bool admit_udev_event_device(struct udev_device *device, const char *path, const char *reason)
{
    const char *id_seat = NULL;
    bool metadata_initialized = false;

    if (udev_device_is_on_seat0(device, &id_seat, &metadata_initialized)) {
        return true;
    }

    if (device == NULL || !metadata_initialized) {
        fprintf(stderr,
            "inputd: %s %s ignored: udev metadata unavailable or not initialized "
            "(seat lookup failed closed)\n",
            reason,
            path);
    } else {
        fprintf(stderr,
            "inputd: %s %s ignored: ID_SEAT=\"%s\" is outside seat0\n",
            reason,
            path,
            id_seat != NULL ? id_seat : "");
    }

    return false;
}

static bool udev_property_is_true(const char *value)
{
    return value != NULL && strcmp(value, "1") == 0;
}

static bool udev_hierarchy_requires_compositor_processing(struct udev_device *device)
{
    for (struct udev_device *current = device;
         current != NULL;
         current = udev_device_get_parent(current)) {
        if (udev_property_is_true(udev_device_get_property_value(current, "ID_INPUT_TOUCHPAD"))
            || udev_property_is_true(udev_device_get_property_value(current, "ID_INPUT_TOUCHSCREEN"))
            || udev_property_is_true(udev_device_get_property_value(current, "ID_INPUT_TABLET"))
            || udev_property_is_true(udev_device_get_property_value(current, "ID_INPUT_TABLET_PAD"))) {
            return true;
        }
    }

    return false;
}

static void apply_udev_device_metadata(
    ksi_linux_device_info *info,
    struct udev_device *device)
{
    if (info == NULL || device == NULL) {
        return;
    }

    info->requires_compositor_processing = info->requires_compositor_processing
        || udev_hierarchy_requires_compositor_processing(device);
}

static bool is_candidate(const ksi_linux_device_info *info)
{
    return info->has_keyboard_keys || (info->has_mouse_buttons && info->has_pointer_axes);
}

/* Devices opened solely for idle tracking are never grabbed or hook-dispatched.
 * EV_KEY covers touchscreens/pens/gamepads/media keys as well as keyboards;
 * EV_REL covers pointer-like devices which may not advertise mouse buttons.
 * Deliberately exclude absolute-only sensors (accelerometers, light sensors):
 * their continuous EV_ABS reports are not evidence of user activity. */
static bool is_idle_candidate(const ksi_linux_device_info *info)
{
    return is_candidate(info) || info->has_keys || info->has_relative;
}

static bool is_keyboard_candidate(const ksi_linux_device_info *info)
{
    return info->has_keyboard_keys;
}

static bool is_mouse_candidate(const ksi_linux_device_info *info)
{
    return info->has_mouse_buttons && info->has_pointer_axes;
}

static bool is_mouse_hook_candidate(const ksi_linux_device_info *info)
{
    return is_mouse_candidate(info) && !info->requires_compositor_processing;
}

static bool is_mouse_block_candidate(const ksi_linux_device_info *info)
{
    return (info->has_mouse_buttons || info->requires_compositor_processing)
        && info->has_pointer_axes;
}

static void log_device(const ksi_linux_device_info *info, const char *prefix)
{
    fprintf(stderr, "inputd: %s %s: \"%s\" candidate=%s%s%s%s%s%s%s\n",
        prefix,
        info->path,
        info->name,
        is_keyboard_candidate(info) ? "keyboard" : "",
        is_keyboard_candidate(info) && is_mouse_candidate(info) ? "," : "",
        is_mouse_candidate(info) ? "mouse" : "",
        info->has_relative ? " rel" : "",
        info->has_absolute ? " abs" : "",
        info->requires_compositor_processing ? " compositor-processed" : "",
        info->is_synth_device ? " injected" : "");
}

static ssize_t find_tracked_device(const char *path)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        if (strcmp(tracked_devices[i].path, path) == 0) {
            return (ssize_t)i;
        }
    }

    return -1;
}

static ssize_t find_tracked_device_by_fd(int fd)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        if (tracked_devices[i].fd == fd) {
            return (ssize_t)i;
        }
    }

    return -1;
}

static void close_tracked_device(ksi_linux_tracked_device *device)
{
    if (device->grabbed && device->fd >= 0) {
        (void)ioctl(device->fd, EVIOCGRAB, 0);
        device->grabbed = false;
    }

    if (device->evdev != NULL) {
        libevdev_free(device->evdev);
        device->evdev = NULL;
    }

    if (device->fd >= 0) {
        close(device->fd);
        device->fd = -1;
    }

    device->grab_deferred = false;
    device->event_clock_monotonic = false;
    device->has_buffered_event = false;
    memset(&device->buffered_event, 0, sizeof(device->buffered_event));
    memset(device->physical_down_keys, 0, sizeof(device->physical_down_keys));
    memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));
}

static int open_tracked_device(ksi_linux_tracked_device *device)
{
    int fd;
    int result;
    int clock_id = CLOCK_MONOTONIC;

    close_tracked_device(device);

    fd = open_event_fd(device->path);

    if (fd < 0) {
        return -1;
    }

    /* Make evdev input_event timestamps comparable with daemon monotonic time.
     * Older kernels may reject this; in that case event ordering still works
     * best-effort with the device's default clock. */
    device->event_clock_monotonic = ioctl(fd, EVIOCSCLOCKID, &clock_id) == 0;

    result = libevdev_new_from_fd(fd, &device->evdev);

    if (result < 0) {
        fprintf(stderr, "inputd: cannot initialize libevdev for %s: %s\n",
            device->path,
            strerror(-result));
        close(fd);
        device->evdev = NULL;
        return -1;
    }

    device->fd = fd;
    return 0;
}

static int scale_abs_axis(int32_t value, int32_t minimum, int32_t maximum)
{
    int64_t numerator;
    int64_t denominator;

    if (maximum <= minimum) {
        return value;
    }

    if (value < minimum) {
        value = minimum;
    } else if (value > maximum) {
        value = maximum;
    }

    numerator = (int64_t)(value - minimum) * 65535;
    denominator = (int64_t)maximum - minimum;
    return (int32_t)(numerator / denominator);
}

static void update_absolute_axis_ranges(ksi_linux_tracked_device *device)
{
    const struct input_absinfo *abs_x;
    const struct input_absinfo *abs_y;

    device->abs_x_min = 0;
    device->abs_x_max = 65535;
    device->abs_y_min = 0;
    device->abs_y_max = 65535;
    device->current_abs_x = 0;
    device->current_abs_y = 0;
    device->current_abs_x_raw = 0;
    device->current_abs_y_raw = 0;

    if (device->evdev == NULL) {
        return;
    }

    abs_x = libevdev_get_abs_info(device->evdev, ABS_X);
    abs_y = libevdev_get_abs_info(device->evdev, ABS_Y);

    if (abs_x != NULL) {
        device->abs_x_min = abs_x->minimum;
        device->abs_x_max = abs_x->maximum;
        device->current_abs_x_raw = abs_x->value;
        device->current_abs_x = scale_abs_axis(abs_x->value, device->abs_x_min, device->abs_x_max);
    }

    if (abs_y != NULL) {
        device->abs_y_min = abs_y->minimum;
        device->abs_y_max = abs_y->maximum;
        device->current_abs_y_raw = abs_y->value;
        device->current_abs_y = scale_abs_axis(abs_y->value, device->abs_y_min, device->abs_y_max);
    }
}

static int set_device_grab(ksi_linux_tracked_device *device, bool enabled)
{
    int value = enabled ? 1 : 0;

    if (device->fd < 0) {
        device->grab_deferred = false;
        memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));
        return 0;
    }

    if (device->injected_source) {
        device->grab_deferred = false;
        memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));

        if (device->grabbed) {
            if (ioctl(device->fd, EVIOCGRAB, 0) != 0) {
                fprintf(stderr,
                    "inputd: EVIOCGRAB(off) failed for injected source %s: %s; closing device\n",
                    device->path,
                    strerror(errno));
                close_tracked_device(device);
                return -1;
            }

            device->grabbed = false;
            fprintf(stderr, "inputd: ungrabbed injected source %s\n", device->path);
        }

        return 0;
    }

    if (!enabled) {
        device->grab_deferred = false;
        memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));

        if (!device->grabbed) {
            return 0;
        }
    } else if (device->grabbed) {
        return 0;
    }

    if (enabled && device->keyboard_candidate) {
        memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));
        memcpy(device->deferred_down_keys, device->physical_down_keys, sizeof(device->deferred_down_keys));

        if (any_bit_set(device->deferred_down_keys, sizeof(device->deferred_down_keys) / sizeof(device->deferred_down_keys[0]))) {
            device->grab_deferred = true;
            fprintf(stderr, "inputd: deferred grab %s until active keys are released\n", device->path);
            return 0;
        }
    }

    if (ioctl(device->fd, EVIOCGRAB, value) != 0) {
        fprintf(stderr,
            "inputd: EVIOCGRAB(%s) failed for %s: %s\n",
            enabled ? "on" : "off",
            device->path,
            strerror(errno));

        if (!enabled) {
            /* Closing the evdev fd is the kernel-level fail-open fallback when
             * EVIOCGRAB(off) itself fails. */
            close_tracked_device(device);
        }

        return -1;
    }

    device->grabbed = enabled;
    device->grab_deferred = false;
    memset(device->deferred_down_keys, 0, sizeof(device->deferred_down_keys));

    if (enabled && device->keyboard_candidate) {
        /* Seed the LED state from the device so the first hook events carry
         * the correct indicator flags before any EV_LED events arrive. */
        refresh_indicator_state_from_device(device);
    }

    /* NOTE: releasing keys replayed "down" on the uinput device when the keyboard is
     * ungrabbed (so a held key can't be stranded on the virtual device) is handled at
     * the daemon layer -- update_grab_state enqueues KSI_OUTPUT_ACTION_RELEASE_ALL on
     * the keyboard-grab held->released edge, which runs on the output sequencer thread.
     * Doing it here would race the sequencer's writes to uinput / synthesized_keys_down. */

    fprintf(stderr, "inputd: %s %s\n", enabled ? "grabbed" : "ungrabbed", device->path);
    return 0;
}

static bool process_deferred_grab_key_event(ksi_linux_tracked_device *device, const struct input_event *event)
{
    if (device == NULL
        || event == NULL
        || !device->grab_deferred
        || event->type != EV_KEY
        || event->code > KEY_MAX
        || !is_keyboard_key_code(event->code)) {
        return false;
    }

    if (event->value == 0) {
        clear_bit(device->deferred_down_keys, event->code);
    } else {
        set_bit(device->deferred_down_keys, event->code);
    }

    if (!any_bit_set(device->deferred_down_keys, sizeof(device->deferred_down_keys) / sizeof(device->deferred_down_keys[0]))) {
        device->grab_deferred = false;
        (void)set_device_grab(device, true);
    }

    return true;
}

static void update_physical_key_state(ksi_linux_tracked_device *device, const struct input_event *event)
{
    if (device == NULL
        || event == NULL
        || event->type != EV_KEY
        || event->code > KEY_MAX
        || !is_keyboard_key_code(event->code)) {
        return;
    }

    if (event->value == 0) {
        clear_bit(device->physical_down_keys, event->code);
    } else {
        set_bit(device->physical_down_keys, event->code);
    }
}

static bool should_grab_device_for_masks(
    const ksi_linux_tracked_device *device,
    uint32_t hook_mask,
    uint32_t block_mask)
{
    if (device == NULL || device->injected_source) {
        return false;
    }

    return (((hook_mask & KSI_CAP_HOOK_KEYBOARD) != 0
             || (block_mask & KSI_BLOCK_INPUT_KEYBOARD) != 0)
            && device->keyboard_candidate)
        || ((hook_mask & KSI_CAP_HOOK_MOUSE) != 0
            && device->mouse_hook_candidate)
        || ((block_mask & KSI_BLOCK_INPUT_MOUSE) != 0
            && device->mouse_block_candidate);
}

/* Best-effort, kernel-truth check: is any of our OWN re-emission device (the
 * uinput nodes we create, tracked with injected_source) currently EVIOCGRAB'd by
 * another process? evdev exposes no "is this grabbed / by whom" query, so we
 * learn it the only way possible: open the node fresh and try to grab it --
 * EBUSY means another open file description already holds it; success means
 * nobody does, so we release immediately. This is the "am I still the tail of
 * the chain?" test.
 *
 * Called only event-driven (hotplug), never on a timer: cooperating interceptors
 * are assumed (so no event flood can occur), and the momentary self-grab
 * therefore cannot meaningfully divert consumer input. The probe fd is O_CLOEXEC
 * so it can never leak a grab to a spawned child. */
static bool our_output_is_grabbed(void)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        int fd;

        if (!tracked_devices[i].injected_source) {
            continue;
        }

        fd = open(tracked_devices[i].path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);

        if (fd < 0) {
            continue; /* transient (device gone); treat as not grabbed */
        }

        if (ioctl(fd, EVIOCGRAB, 1) == 0) {
            (void)ioctl(fd, EVIOCGRAB, 0); /* nobody else had it; release */
            close(fd);
            continue;
        }

        if (errno == EBUSY) {
            close(fd);
            return true; /* a foreign process holds our output */
        }

        /* Any other errno (ENODEV, etc.): device is gone/unusable; not grabbed. */
        close(fd);
    }

    return false;
}

static void track_device(const ksi_linux_device_info *info, const char *reason)
{
    ssize_t existing_index = find_tracked_device(info->path);
    ksi_linux_tracked_device *target;
    bool is_new = existing_index < 0;

    if (!is_idle_candidate(info)) {
        return;
    }

    if (existing_index >= 0) {
        target = &tracked_devices[existing_index];
    } else {
        if (tracked_device_count >= KSI_MAX_TRACKED_DEVICES) {
            fprintf(stderr, "inputd: cannot track %s: device table is full\n", info->path);
            return;
        }

        target = &tracked_devices[tracked_device_count++];
        memset(target, 0, sizeof(*target));
        target->fd = -1;
        target->device_id = next_device_id++;
    }

    (void)snprintf(target->path, sizeof(target->path), "%s", info->path);
    (void)snprintf(target->name, sizeof(target->name), "%s", info->name);
    target->keyboard_candidate = is_keyboard_candidate(info);
    target->mouse_candidate = is_mouse_candidate(info);
    target->mouse_hook_candidate = is_mouse_hook_candidate(info);
    target->mouse_block_candidate = is_mouse_block_candidate(info);
    target->injected_source = info->is_synth_device;

    if (is_new || target->fd < 0) {
        if (open_tracked_device(target) != 0) {
            return;
        }

        update_absolute_axis_ranges(target);
    }

    {
        bool want_grab = should_grab_device_for_masks(target, grab_hook_mask, block_input_mask);

        /* Only-the-tail-extends: if a downstream cooperating interceptor has
         * grabbed our OWN output, we are no longer the tail of the chain, so we
         * must not grab a newly-appeared device -- that would put two grabbers on
         * one stream and risk a mutual-grab lockout. Defer to the tail instead.
         * Applies only to NEW devices; grabs already implied by the active mask
         * (e.g. our interception target at hook install) are unaffected. */
        if (want_grab && is_new && our_output_is_grabbed()) {
            fprintf(stderr,
                "inputd: not grabbing new device %s (\"%s\"): our output is grabbed "
                "downstream; deferring to the tail interceptor\n",
                target->path, target->name);
            want_grab = false;
        }

        if (set_device_grab(target, want_grab) != 0) {
            /* Mirror set_grab_masks()'s own bookkeeping: a device that fails to
             * grab right now (e.g. EBUSY racing keyd, or a stray second daemon
             * instance) must not be silently forgotten -- this device was never
             * counted by set_grab_masks()'s own failure loop (this call bypasses
             * it entirely), so without this it would never be retried. Setting
             * this flag makes ksi_linux_devices_retry_incomplete_grabs() (called
             * periodically from the main loop) re-attempt it. */
            grab_state_incomplete = true;
        }
    }

    log_device(info, reason);
}

static void untrack_device(const char *path)
{
    ssize_t existing_index = find_tracked_device(path);
    size_t index;

    if (existing_index < 0) {
        return;
    }

    index = (size_t)existing_index;

    fprintf(stderr, "inputd: remove %s: \"%s\"\n",
        tracked_devices[index].path,
        tracked_devices[index].name);

    close_tracked_device(&tracked_devices[index]);

    for (size_t i = index; i + 1 < tracked_device_count; i++) {
        tracked_devices[i] = tracked_devices[i + 1];
    }

    tracked_device_count--;
}

static void scan_existing_devices(void)
{
    DIR *dir;
    struct dirent *entry;
    struct udev *scan_udev;
    int devices_seen = 0;
    int candidates_seen = 0;

    dir = opendir(KSI_INPUT_DIR);

    if (dir == NULL) {
        fprintf(stderr, "inputd: cannot open %s: %s\n", KSI_INPUT_DIR, strerror(errno));
        return;
    }

    /* The monitor owns the long-lived context when available. In degraded
     * no-monitor mode, create a temporary context solely for admission. */
    scan_udev = udev_context != NULL ? udev_ref(udev_context) : udev_new();

    if (scan_udev == NULL) {
        fprintf(stderr,
            "inputd: cannot create udev context for seat0 filtering; skipping input devices\n");
        closedir(dir);
        return;
    }

    while ((entry = readdir(dir)) != NULL) {
        char path[PATH_MAX];
        ksi_linux_device_info info;
        struct udev_device *udev_device;
        bool idle_candidate;

        if (!is_event_device_name(entry->d_name)) {
            continue;
        }

        if (snprintf(path, sizeof(path), "%s/%s", KSI_INPUT_DIR, entry->d_name) >= (int)sizeof(path)) {
            fprintf(stderr, "inputd: skipping too-long input path for %s\n", entry->d_name);
            continue;
        }

        devices_seen++;

        udev_device = udev_device_new_from_subsystem_sysname(
            scan_udev,
            "input",
            entry->d_name);

        if (!admit_udev_event_device(udev_device, path, "existing")) {
            if (udev_device != NULL) {
                udev_device_unref(udev_device);
            }

            continue;
        }

        if (read_device_info(path, &info) != 0) {
            udev_device_unref(udev_device);
            continue;
        }

        apply_udev_device_metadata(&info, udev_device);
        udev_device_unref(udev_device);

        idle_candidate = is_idle_candidate(&info);

        if (idle_candidate) {
            candidates_seen++;
            track_device(&info, "existing");
        }
    }

    udev_unref(scan_udev);
    closedir(dir);

    fprintf(stderr, "inputd: scanned %d event devices, found %d user-input candidates\n",
        devices_seen,
        candidates_seen);
}

void ksi_linux_devices_rescan(void)
{
    scan_existing_devices();
}

static void stop_udev_monitor(void)
{
    if (udev_monitor != NULL) {
        udev_monitor_unref(udev_monitor);
        udev_monitor = NULL;
    }

    if (udev_context != NULL) {
        udev_unref(udev_context);
        udev_context = NULL;
    }
}

static int start_udev_monitor(void)
{
    udev_context = udev_new();

    if (udev_context == NULL) {
        fprintf(stderr, "inputd: failed to create udev context\n");
        return -1;
    }

    udev_monitor = udev_monitor_new_from_netlink(udev_context, "udev");

    if (udev_monitor == NULL) {
        fprintf(stderr, "inputd: failed to create udev monitor\n");
        stop_udev_monitor();
        return -1;
    }

    if (udev_monitor_filter_add_match_subsystem_devtype(
            udev_monitor,
            "input",
            NULL) < 0) {
        fprintf(stderr, "inputd: failed to install udev input filter\n");
        stop_udev_monitor();
        return -1;
    }

    if (udev_monitor_enable_receiving(udev_monitor) < 0) {
        fprintf(stderr, "inputd: failed to enable udev monitor\n");
        stop_udev_monitor();
        return -1;
    }

    fprintf(stderr, "inputd: udev hotplug monitor enabled\n");
    return 0;
}

static void handle_device_add_or_change(
    struct udev_device *udev_device,
    const char *path,
    const char *action)
{
    ksi_linux_device_info info;

    if (!admit_udev_event_device(udev_device, path, action)) {
        /* A change event can move an already-tracked device to another seat.
         * Release its grab and fd before any further event processing. */
        untrack_device(path);
        return;
    }

    if (read_device_info(path, &info) != 0) {
        return;
    }

    apply_udev_device_metadata(&info, udev_device);

    if (!is_idle_candidate(&info)) {
        fprintf(stderr, "inputd: %s %s ignored: not a user-input candidate\n", action, path);
        return;
    }

    track_device(&info, action);
}

int ksi_linux_devices_start(void)
{
    tracked_device_count = 0;
    grab_hook_mask = 0;
    block_input_mask = 0;
    grab_state_incomplete = false;
    memset(&current_pointer_position, 0, sizeof(current_pointer_position));
    last_input_activity_ms = 0u;

    /* Start the udev monitor BEFORE enumerating existing devices -- the same
     * order keyd itself uses (evloop.c: devmon_create() before device_scan()).
     * A device that appears between udev_monitor_enable_receiving() and the
     * enumeration below queues on the (now-open) netlink socket and is picked
     * up later by process_udev_events(); track_device() dedups by path, so a
     * device that both paths independently see is a harmless no-op on the
     * second sighting. The reverse order (enumerate-then-watch) leaves a gap
     * where a device created after the enumeration finishes but before the
     * monitor goes live is invisible to both paths, permanently -- notably
     * including keyd's own persistent virtual keyboard, which (unlike a
     * physical device) is never recreated on a later replug to give the
     * monitor a second chance. */
    if (start_udev_monitor() != 0) {
        fprintf(stderr, "inputd: warning: udev monitor unavailable; hotplug disabled\n");
        /* Continue in degraded mode: existing devices are tracked but newly
         * plugged devices will not be detected at runtime. */
    }

    scan_existing_devices();

    return 0;
}

/* Rate limit for ksi_linux_devices_retry_incomplete_grabs(): a device that is
 * genuinely, permanently ungrabbable (e.g. a composite wireless dongle's
 * extra "consumer control" node -- see the comment in set_grab_masks()) would
 * otherwise retry, fail, and log on every single main-loop tick. */
#define KSI_GRAB_RETRY_INTERVAL_MS 3000u
static uint64_t last_grab_retry_ms;

static uint64_t monotonic_ms(void)
{
    struct timespec ts;

    if (clock_gettime(CLOCK_MONOTONIC, &ts) != 0) {
        return 0;
    }

    return ((uint64_t)ts.tv_sec * 1000u) + ((uint64_t)ts.tv_nsec / 1000000u);
}

/* Called periodically (event-driven callers only ever retry on their own
 * event; nothing previously re-drove a grab attempt once time had simply
 * passed). Re-runs set_grab_masks() with the CURRENT masks whenever
 * grab_state_incomplete is set -- set_grab_masks()'s own short-circuit only
 * skips the per-device loop when the flag is clear, so this safely becomes a
 * cheap no-op once every device is resolved (grabbed, or intentionally
 * deferred-and-still-deferred). This is what lets a device that lost an
 * EVIOCGRAB race (against keyd, a stray second daemon instance, or any other
 * transient contention) get picked back up once the contention clears,
 * instead of being silently abandoned for the rest of the process's life. */
void ksi_linux_devices_retry_incomplete_grabs(void)
{
    uint64_t now;

    if (!grab_state_incomplete) {
        return;
    }

    now = monotonic_ms();

    if (now != 0 && last_grab_retry_ms != 0 && now - last_grab_retry_ms < KSI_GRAB_RETRY_INTERVAL_MS) {
        return;
    }

    last_grab_retry_ms = now;
    (void)set_grab_masks(grab_hook_mask, block_input_mask);
}

void ksi_linux_devices_stop(void)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        close_tracked_device(&tracked_devices[i]);
    }

    stop_udev_monitor();

    tracked_device_count = 0;
    memset(&current_pointer_position, 0, sizeof(current_pointer_position));
    last_input_activity_ms = 0u;
}

bool ksi_linux_devices_has_candidates(void)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        if (!tracked_devices[i].injected_source
            && (tracked_devices[i].keyboard_candidate
                || tracked_devices[i].mouse_candidate
                || tracked_devices[i].mouse_block_candidate)) {
            return true;
        }
    }

    return false;
}

static int set_grab_masks(uint32_t hook_mask, uint32_t block_mask)
{
    size_t grab_failures = 0;
    bool deferred_any = false;
    bool defer_new_grabs = false;

    if (!grab_state_incomplete
        && grab_hook_mask == hook_mask
        && block_input_mask == block_mask) {
        return 0;
    }

    /* Mirror track_device()'s "only-the-tail-extends" guard here too. That
     * guard previously applied only to a brand-new device appearing via
     * hotplug; without it here, a hook/BlockInput mask change (or a retry of
     * a device track_device() correctly deferred on) could still grab a
     * device out from under a downstream cooperating interceptor (e.g. keyd),
     * recreating the exact double-grab this mechanism exists to prevent.
     * Only gates NEW grabs (see the !grabbed check below) -- a device already
     * flowing is left alone, since un-grabbing it here would itself be a
     * worse disruption than leaving it as-is. */
    if (hook_mask != 0 || block_mask != 0) {
        defer_new_grabs = our_output_is_grabbed();
    }

    /* Best-effort, per-device: apply the requested grab state to each tracked
     * device independently. A failure on one device must not disable
     * hooking/blocking on the devices that did grab. This matters for
     * composite wireless receivers (one dongle for keyboard+mouse), which
     * commonly expose an extra "consumer/system control" evdev node that is a
     * grab candidate but cannot be EVIOCGRAB'd; rolling everything back on that
     * one node's failure was what made BlockInput silently do nothing. */
    for (size_t i = 0; i < tracked_device_count; i++) {
        bool should_grab = should_grab_device_for_masks(&tracked_devices[i], hook_mask, block_mask);

        if (should_grab && defer_new_grabs && !tracked_devices[i].grabbed) {
            fprintf(stderr,
                "inputd: not grabbing %s (\"%s\"): our output is grabbed downstream; "
                "deferring to the tail interceptor\n",
                tracked_devices[i].path, tracked_devices[i].name);
            should_grab = false;
            deferred_any = true;
        }

        if (set_device_grab(&tracked_devices[i], should_grab) != 0) {
            grab_failures++;
        }
    }

    /* Commit the masks even on partial failure: the devices that grabbed are
     * now enforcing this state, and devices hotplugged later must match it.
     * A deferred-to-tail-interceptor device counts as incomplete too (even
     * though set_device_grab() itself reported success/no-op for it): this is
     * what makes the periodic retry pass (see
     * ksi_linux_devices_retry_incomplete_grabs) keep re-checking
     * our_output_is_grabbed() and pick the device up as soon as the tail
     * interceptor releases our output, instead of deferring it forever. */
    grab_hook_mask = hook_mask;
    block_input_mask = block_mask;
    grab_state_incomplete = grab_failures != 0 || deferred_any;

    if (grab_failures != 0) {
        fprintf(stderr,
            "inputd: %zu device(s) could not be grabbed for hook_mask=0x%x block_mask=0x%x; "
            "continuing with the devices that grabbed successfully\n",
            grab_failures, hook_mask, block_mask);
    }

    return 0;
}

int ksi_linux_devices_set_grab_hook_mask(uint32_t hook_mask)
{
    return set_grab_masks(hook_mask, block_input_mask);
}

int ksi_linux_devices_set_block_input_mask(uint32_t block_mask)
{
    return set_grab_masks(grab_hook_mask, block_mask);
}

static nfds_t add_poll_fd(struct pollfd *fds, nfds_t max_fds, nfds_t count, int fd)
{
    if (fd < 0 || count >= max_fds) {
        return count;
    }

    fds[count].fd = fd;
    fds[count].events = POLLIN;
    return count + 1;
}

nfds_t ksi_linux_devices_poll_fds(struct pollfd *fds, nfds_t max_fds)
{
    nfds_t count = 0;

    if (udev_monitor != NULL) {
        count = add_poll_fd(fds, max_fds, count, udev_monitor_get_fd(udev_monitor));
    }

    for (size_t i = 0; i < tracked_device_count; i++) {
        count = add_poll_fd(fds, max_fds, count, tracked_devices[i].fd);
    }

    return count;
}

static uint64_t event_time_ms(const struct input_event *event)
{
    uint64_t seconds = (uint64_t)event->input_event_sec;
    uint64_t useconds = (uint64_t)event->input_event_usec;
    return (seconds * 1000u) + (useconds / 1000u);
}

static uint64_t event_time_ns(const struct input_event *event)
{
    uint64_t seconds = (uint64_t)event->input_event_sec;
    uint64_t useconds = (uint64_t)event->input_event_usec;
    return (seconds * 1000000000u) + (useconds * 1000u);
}

static uint32_t evdev_key_to_vk(unsigned int code)
{
    return ksi_evdev_to_vk(code);
}

static uint32_t evdev_key_to_message(const struct input_event *event)
{
    if (event->value == 0) {
        return KSI_WM_KEYUP;
    }

    return KSI_WM_KEYDOWN;
}

static uint32_t evdev_key_to_flags(const struct input_event *event)
{
    uint32_t flags = 0;

    switch (event->code) {
        case KEY_RIGHTCTRL:
        case KEY_RIGHTALT:
        case KEY_INSERT:
        case KEY_DELETE:
        case KEY_HOME:
        case KEY_END:
        case KEY_PAGEUP:
        case KEY_PAGEDOWN:
        case KEY_UP:
        case KEY_DOWN:
        case KEY_LEFT:
        case KEY_RIGHT:
        case KEY_KPSLASH:
        case KEY_KPENTER:
            flags |= KSI_LLKHF_EXTENDED;
            break;
        default:
            break;
    }

    if (event->value == 0) {
        flags |= KSI_LLKHF_UP;
    }

    return flags;
}

static uint32_t keyboard_injected_flags(bool is_injected)
{
    return is_injected ? KSI_LLKHF_INJECTED : 0u;
}

static uint32_t keyboard_indicator_flags(void)
{
    uint32_t flags = 0;
    if (current_caps_lock)   flags |= KSI_LLKHF_CAPS_LOCK_ON;
    if (current_num_lock)    flags |= KSI_LLKHF_NUM_LOCK_ON;
    if (current_scroll_lock) flags |= KSI_LLKHF_SCROLL_LOCK_ON;
    return flags;
}

static uint32_t mouse_injected_flags(bool is_injected)
{
    return is_injected ? KSI_LLMHF_INJECTED : 0u;
}

static bool should_dispatch_hook_input(const ksi_linux_tracked_device *device)
{
    return device != NULL && device->grabbed && !device->injected_source;
}

static bool evdev_button_to_mouse_message(unsigned int code, int value, uint32_t *message)
{
    if (value != 0 && value != 1) {
        return false;
    }

    switch (code) {
        case BTN_LEFT:
            *message = value != 0 ? KSI_WM_LBUTTONDOWN : KSI_WM_LBUTTONUP;
            return true;
        case BTN_RIGHT:
            *message = value != 0 ? KSI_WM_RBUTTONDOWN : KSI_WM_RBUTTONUP;
            return true;
        case BTN_MIDDLE:
            *message = value != 0 ? KSI_WM_MBUTTONDOWN : KSI_WM_MBUTTONUP;
            return true;
        case BTN_SIDE:
        case BTN_BACK:
        case BTN_EXTRA:
        case BTN_FORWARD:
            *message = value != 0 ? KSI_WM_XBUTTONDOWN : KSI_WM_XBUTTONUP;
            return true;
        default:
            return false;
    }
}

static uint32_t evdev_button_mouse_data(unsigned int code)
{
    if (code == BTN_SIDE || code == BTN_BACK) {
        return KSI_XBUTTON1 << 16;
    }

    if (code == BTN_EXTRA || code == BTN_FORWARD) {
        return KSI_XBUTTON2 << 16;
    }

    return 0u;
}

static void dispatch_keyboard_event(
    const ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    if (!should_dispatch_hook_input(device)) {
        return;
    }

    ksi_keyboard_hook_event hook_event = {
        .message = evdev_key_to_message(event),
        .vk_code = evdev_key_to_vk((unsigned int)event->code),
        .scan_code = (uint32_t)event->code,
        .flags = evdev_key_to_flags(event) | keyboard_injected_flags(is_injected) | keyboard_indicator_flags(),
        .time_ms = event_time_ms(event),
        .extra_info = extra_info,
        .device_id = device->device_id,
    };

    if (g_verbose) {
        printf("inputd: key %s vk=0x%02x scan=%u value=%d flags=0x%x time=%llu device=\"%s\"\n",
            hook_event.message == KSI_WM_KEYUP ? "up" : "down",
            hook_event.vk_code,
            hook_event.scan_code,
            event->value,
            hook_event.flags,
            (unsigned long long)hook_event.time_ms,
            device->name);
    }

    if (hook_event_callback != NULL) {
        hook_event_callback(
            hook_event_context,
            KSI_HOOK_KEYBOARD_LL,
            &hook_event,
            sizeof(hook_event));
    }
}

static void dispatch_mouse_button_event(
    const ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    uint32_t message;

    if (!should_dispatch_hook_input(device)) {
        return;
    }

    if (!evdev_button_to_mouse_message((unsigned int)event->code, event->value, &message)) {
        return;
    }

    ksi_mouse_hook_event hook_event = {
        .message = message,
        /* Button events carry no cursor position; report the sentinel, not a bogus (0,0). */
        .x = KSI_MOUSE_COORD_UNSPECIFIED,
        .y = KSI_MOUSE_COORD_UNSPECIFIED,
        .mouse_data = evdev_button_mouse_data((unsigned int)event->code),
        .flags = mouse_injected_flags(is_injected),
        .time_ms = event_time_ms(event),
        .extra_info = extra_info,
        .device_id = device->device_id,
    };

    if (g_verbose) {
        printf("inputd: mouse button message=0x%x data=0x%x time=%llu device=\"%s\"\n",
            hook_event.message,
            hook_event.mouse_data,
            (unsigned long long)hook_event.time_ms,
            device->name);
    }

    if (hook_event_callback != NULL) {
        hook_event_callback(
            hook_event_context,
            KSI_HOOK_MOUSE_LL,
            &hook_event,
            sizeof(hook_event));
    }
}

static void dispatch_pending_mouse_move(ksi_linux_tracked_device *device)
{
    ksi_mouse_hook_event hook_event;

    if (device->has_pending_rel) {
        memset(&hook_event, 0, sizeof(hook_event));
        hook_event.message = KSI_WM_MOUSEMOVE;
        hook_event.x = device->pending_rel_x;
        hook_event.y = device->pending_rel_y;
        hook_event.delta_x = hook_event.x;
        hook_event.delta_y = hook_event.y;
        hook_event.flags = mouse_injected_flags(device->pending_rel_injected);
        hook_event.time_ms = device->pending_rel_time_ms;
        hook_event.extra_info = device->pending_rel_extra_info;
        hook_event.device_id = device->device_id;

        if (g_verbose && should_dispatch_hook_input(device)) {
            printf("inputd: mouse move dx=%d dy=%d time=%llu device=\"%s\"\n",
                hook_event.x,
                hook_event.y,
                (unsigned long long)hook_event.time_ms,
                device->name);
        }

        device->has_pending_rel = false;
        device->pending_rel_x = 0;
        device->pending_rel_y = 0;
        device->pending_rel_time_ms = 0;
        device->pending_rel_extra_info = 0;
        device->pending_rel_injected = false;

        if (should_dispatch_hook_input(device) && hook_event_callback != NULL) {
            hook_event_callback(
                hook_event_context,
                KSI_HOOK_MOUSE_LL,
                &hook_event,
                sizeof(hook_event));
        }
    }

    if (device->has_pending_abs) {
        memset(&hook_event, 0, sizeof(hook_event));
        hook_event.message = KSI_WM_MOUSEMOVE;
        hook_event.x = device->current_abs_x;
        hook_event.y = device->current_abs_y;
        hook_event.delta_x = hook_event.x - device->pending_abs_start_x;
        hook_event.delta_y = hook_event.y - device->pending_abs_start_y;
        hook_event.mouse_data = KSI_MOUSEEVENTF_ABSOLUTE;
        hook_event.flags = mouse_injected_flags(device->pending_abs_injected);
        hook_event.time_ms = device->pending_abs_time_ms;
        hook_event.extra_info = device->pending_abs_extra_info;
        hook_event.device_id = device->device_id;

        if (g_verbose && should_dispatch_hook_input(device)) {
            printf("inputd: mouse move abs x=%d y=%d dx=%d dy=%d time=%llu device=\"%s\"\n",
                hook_event.x, hook_event.y, hook_event.delta_x, hook_event.delta_y,
                (unsigned long long)hook_event.time_ms,
                device->name);
        }

        device->has_pending_abs = false;
        device->pending_abs_time_ms = 0;
        device->pending_abs_extra_info = 0;
        device->pending_abs_injected = false;

        if (should_dispatch_hook_input(device) && hook_event_callback != NULL) {
            hook_event_callback(
                hook_event_context,
                KSI_HOOK_MOUSE_LL,
                &hook_event,
                sizeof(hook_event));
        }
    }
}

static void queue_relative_motion(
    ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    if (device->has_pending_rel && device->pending_rel_injected != is_injected) {
        dispatch_pending_mouse_move(device);
    }

    if (event->code == REL_X) {
        device->pending_rel_x += event->value;
    } else {
        device->pending_rel_y += event->value;
    }

    device->pending_rel_time_ms = event_time_ms(event);
    device->pending_rel_extra_info = extra_info;
    device->pending_rel_injected = is_injected;
    device->has_pending_rel = true;

    /* A relative movement makes any cached absolute position stale: the cursor has moved but we
     * have no absolute coordinate for its new location. Invalidate so GET_POINTER_POSITION reports
     * "unknown" (valid = 0) rather than a stale absolute value -- callers must only trust the
     * absolute position when the most recent movement was itself absolute. */
    current_pointer_position.valid = 0u;
}

static void dispatch_relative_event(
    ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    if (event->code == REL_X || event->code == REL_Y) {
        queue_relative_motion(device, event, extra_info, is_injected);
        return;
    }

    if (event->code == REL_WHEEL || event->code == REL_HWHEEL) {
        ksi_mouse_hook_event hook_event = {
            .message = event->code == REL_WHEEL ? KSI_WM_MOUSEWHEEL : KSI_WM_MOUSEHWHEEL,
            /* Wheel events carry no cursor position; report the sentinel, not a bogus (0,0). */
            .x = KSI_MOUSE_COORD_UNSPECIFIED,
            .y = KSI_MOUSE_COORD_UNSPECIFIED,
            .mouse_data = (uint32_t)((int32_t)event->value * 120) << 16,
            .flags = mouse_injected_flags(is_injected),
            .time_ms = event_time_ms(event),
            .extra_info = extra_info,
            .device_id = device->device_id,
        };

        dispatch_pending_mouse_move(device);

        if (g_verbose && should_dispatch_hook_input(device)) {
            printf("inputd: mouse wheel message=0x%x delta=%d time=%llu device=\"%s\"\n",
                hook_event.message,
                event->value * 120,
                (unsigned long long)hook_event.time_ms,
                device->name);
        }

        if (should_dispatch_hook_input(device) && hook_event_callback != NULL) {
            hook_event_callback(
                hook_event_context,
                KSI_HOOK_MOUSE_LL,
                &hook_event,
                sizeof(hook_event));
        }
    }
}

static void queue_absolute_motion(
    ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    int32_t previous_abs_x = device->current_abs_x;
    int32_t previous_abs_y = device->current_abs_y;

    if (device->has_pending_abs && device->pending_abs_injected != is_injected) {
        dispatch_pending_mouse_move(device);
    }

    if (event->code == ABS_X) {
        device->current_abs_x_raw = event->value;
        device->current_abs_x = scale_abs_axis(event->value, device->abs_x_min, device->abs_x_max);
    } else {
        device->current_abs_y_raw = event->value;
        device->current_abs_y = scale_abs_axis(event->value, device->abs_y_min, device->abs_y_max);
    }

    /* Keep the report's starting point; dispatch emits both its delta and the final
     * [0,65535] position needed to replay it through the absolute uinput device. */
    if (!device->has_pending_abs) {
        device->pending_abs_start_x = previous_abs_x;
        device->pending_abs_start_y = previous_abs_y;
    }
    device->pending_abs_time_ms = event_time_ms(event);
    device->pending_abs_extra_info = extra_info;
    device->pending_abs_injected = is_injected;
    device->has_pending_abs = true;

    current_pointer_position.valid = 1u;
    current_pointer_position.x = device->current_abs_x_raw;
    current_pointer_position.y = device->current_abs_y_raw;
    current_pointer_position.x_min = device->abs_x_min;
    current_pointer_position.x_max = device->abs_x_max;
    current_pointer_position.y_min = device->abs_y_min;
    current_pointer_position.y_max = device->abs_y_max;
}

static void dispatch_absolute_event(
    ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t extra_info,
    bool is_injected)
{
    if (event->code == ABS_X || event->code == ABS_Y) {
        queue_absolute_motion(device, event, extra_info, is_injected);
    }
}

static void handle_input_event(
    ksi_linux_tracked_device *device,
    const struct input_event *event,
    uint64_t *fallback_activity_time_ms)
{
    uint64_t extra_info = 0;
    bool is_injected = false;

    /* Keysharp's replay/synthesis device is a downstream copy, not new
     * physical activity. Reject it at first ingress so it cannot reset idle
     * time or re-enter hook dispatch. */
    if (device == NULL || event == NULL || device->injected_source) {
        return;
    }

    /* Count activity at raw evdev ingress, before hook callbacks or deferred
     * processing can create re-entrant events. Synchronization packets, LED
     * output, and zero relative deltas can occur without user-visible input and
     * do not reset the clock. */
    if (event->type == EV_KEY
        || (event->type == EV_REL && event->value != 0)
        || event->type == EV_ABS) {
        uint64_t activity_time_ms;

        /* EVIOCSCLOCKID makes this kernel timestamp CLOCK_MONOTONIC, so the hot
         * path is only integer arithmetic. Fall back to clock_gettime only on
         * old kernels which rejected that ioctl. Using the event time rather
         * than processing time also preserves accuracy through a queue backlog. */
        if (device->event_clock_monotonic) {
            activity_time_ms = event_time_ms(event);
        } else {
            /* Query once, on the first real activity event in this readable
             * batch. This remains before hook dispatch while avoiding a clock
             * syscall for every event on kernels without EVIOCSCLOCKID. */
            if (*fallback_activity_time_ms == 0u) {
                *fallback_activity_time_ms = monotonic_ms();
            }

            activity_time_ms = *fallback_activity_time_ms;
        }

        if (activity_time_ms > last_input_activity_ms) {
            last_input_activity_ms = activity_time_ms;
        }
    }

    if (!device->keyboard_candidate && !device->mouse_candidate) {
        return;
    }

    if (event->type == EV_SYN) {
        if (event->code == SYN_REPORT) {
            dispatch_pending_mouse_move(device);
        }
    } else if (event->type == EV_KEY) {
        dispatch_pending_mouse_move(device);
        update_physical_key_state(device, event);

        if (device->keyboard_candidate && !device->injected_source
            && is_keyboard_key_code(event->code)
            && physical_key_event_callback != NULL) {
            physical_key_event_callback(physical_key_event_context, event->code);
        }

        if (process_deferred_grab_key_event(device, event)) {
            return;
        }

        if (device->mouse_hook_candidate && is_mouse_button_code(event->code)) {
            dispatch_mouse_button_event(device, event, extra_info, is_injected);
        } else if (device->keyboard_candidate && is_keyboard_key_code(event->code)) {
            dispatch_keyboard_event(device, event, extra_info, is_injected);
        }
    } else if (event->type == EV_REL && device->mouse_hook_candidate) {
        dispatch_relative_event(device, event, extra_info, is_injected);
    } else if (event->type == EV_ABS && device->mouse_hook_candidate) {
        dispatch_absolute_event(device, event, extra_info, is_injected);
    } else if (event->type == EV_LED) {
        /* Track indicator LED state so we can include it in keyboard hook events. */
        if (event->code == LED_CAPSL)
            current_caps_lock = event->value != 0;
        else if (event->code == LED_NUML)
            current_num_lock = event->value != 0;
        else if (event->code == LED_SCROLLL)
            current_scroll_lock = event->value != 0;
    }
}

void ksi_linux_devices_get_indicator_state(bool *caps_lock, bool *num_lock, bool *scroll_lock)
{
    if (caps_lock)   *caps_lock   = current_caps_lock;
    if (num_lock)    *num_lock    = current_num_lock;
    if (scroll_lock) *scroll_lock = current_scroll_lock;
}

bool ksi_linux_devices_get_pointer_position(ksi_pointer_position_payload *position)
{
    if (position == NULL) {
        return false;
    }

    *position = current_pointer_position;
    return position->valid != 0u;
}

bool ksi_linux_devices_get_idle_time(ksi_idle_time_payload *result)
{
    uint64_t now;

    if (result == NULL) {
        return false;
    }

    memset(result, 0, sizeof(*result));

    if (last_input_activity_ms == 0u) {
        return false;
    }

    now = monotonic_ms();
    result->valid = 1u;
    result->idle_time_ms = now >= last_input_activity_ms
        ? now - last_input_activity_ms
        : 0u;
    return true;
}

/* Snapshot the current mouse-button state. physical_buttons is read across all real pointer
 * devices via EVIOCGKEY. logical_buttons starts from that physical mask and adds Keysharp's
 * enqueue-time synthetic button state. Result bits: 0=left, 1=right, 2=middle, 3=X1(side),
 * 4=X2(extra). */
bool ksi_linux_devices_get_pointer_buttons(ksi_pointer_buttons_payload *result)
{
    unsigned long key_bits[KSI_BIT_ARRAY_LENGTH(KEY_MAX)];
    bool any = false;
    uint32_t physical_buttons = 0u;
    uint32_t logical_buttons = 0u;

    if (result == NULL) {
        return false;
    }

    memset(result, 0, sizeof(*result));

    for (size_t i = 0; i < tracked_device_count; i++) {
        const ksi_linux_tracked_device *dev = &tracked_devices[i];

        /* Skip our own virtual injection device: its EVIOCGKEY would report replayed state. */
        if (!dev->mouse_candidate || dev->injected_source || dev->fd < 0) {
            continue;
        }

        memset(key_bits, 0, sizeof(key_bits));

        if (ioctl(dev->fd, EVIOCGKEY(sizeof(key_bits)), key_bits) < 0) {
            continue;
        }

        any = true;

        for (unsigned int code = BTN_LEFT; code <= BTN_BACK; code++) {
            if (test_bit(key_bits, (int)code)) {
                physical_buttons |= ksi_linux_pointer_button_mask(code);
            }
        }
    }

    logical_buttons = physical_buttons;
    ksi_linux_synth_add_logical_pointer_button_state(&logical_buttons);

    result->valid = any ? 1u : 0u;
    result->buttons = physical_buttons;
    result->logical_buttons = logical_buttons;
    result->physical_buttons = physical_buttons;
    return any;
}

static void refresh_indicator_state_from_device(const ksi_linux_tracked_device *device)
{
    unsigned char leds = 0;

    if (device == NULL || !device->keyboard_candidate || device->injected_source || device->fd < 0) {
        return;
    }

    if (ioctl(device->fd, EVIOCGLED(sizeof(leds)), &leds) < 0) {
        return;
    }

    current_caps_lock   = (leds & (1u << LED_CAPSL))   != 0;
    current_num_lock    = (leds & (1u << LED_NUML))    != 0;
    current_scroll_lock = (leds & (1u << LED_SCROLLL)) != 0;
}

/* Re-read indicator LED state from all grabbed keyboard devices via EVIOCGLED.
 * Unlike current_caps_lock (which is updated lazily from EV_LED events), this
 * always reflects the hardware LED state at call time. */
void ksi_linux_devices_refresh_indicator_state(void)
{
    for (size_t i = 0; i < tracked_device_count; i++) {
        if (tracked_devices[i].grabbed && tracked_devices[i].keyboard_candidate)
            refresh_indicator_state_from_device(&tracked_devices[i]);
    }
}


static uint32_t modifiers_from_logical_key_bitmap(const uint8_t *keys)
{
    uint32_t mods = 0;

    if (payload_key_bit_is_set(keys, KEY_LEFTCTRL))   mods |= 0x01u; /* MOD_LCONTROL */
    if (payload_key_bit_is_set(keys, KEY_RIGHTCTRL))  mods |= 0x02u; /* MOD_RCONTROL */
    if (payload_key_bit_is_set(keys, KEY_LEFTALT))    mods |= 0x04u; /* MOD_LALT     */
    if (payload_key_bit_is_set(keys, KEY_RIGHTALT))   mods |= 0x08u; /* MOD_RALT     */
    if (payload_key_bit_is_set(keys, KEY_LEFTSHIFT))  mods |= 0x10u; /* MOD_LSHIFT   */
    if (payload_key_bit_is_set(keys, KEY_RIGHTSHIFT)) mods |= 0x20u; /* MOD_RSHIFT   */
    if (payload_key_bit_is_set(keys, KEY_LEFTMETA))   mods |= 0x40u; /* MOD_LWIN     */
    if (payload_key_bit_is_set(keys, KEY_RIGHTMETA))  mods |= 0x80u; /* MOD_RWIN     */

    return mods;
}

bool ksi_linux_devices_get_key_state(ksi_key_state_payload *result)
{
    unsigned long key_bits[KSI_BIT_ARRAY_LENGTH(KEY_MAX)];
    bool any = false;

    if (result == NULL) {
        return false;
    }

    memset(result->logical_keys, 0, sizeof(result->logical_keys));
    memset(result->physical_keys, 0, sizeof(result->physical_keys));

    for (size_t i = 0; i < tracked_device_count; i++) {
        const ksi_linux_tracked_device *dev = &tracked_devices[i];

        if (!dev->keyboard_candidate || dev->injected_source || dev->fd < 0) {
            continue;
        }

        memset(key_bits, 0, sizeof(key_bits));

        if (ioctl(dev->fd, EVIOCGKEY(sizeof(key_bits)), key_bits) < 0) {
            if (!dev->grabbed) {
                continue;
            }

            for (unsigned int code = 0; code <= KEY_MAX; code++) {
                if (test_bit(dev->physical_down_keys, (int)code)) {
                    set_payload_key_bit(result->logical_keys, code);
                    set_payload_key_bit(result->physical_keys, code);
                }
            }

            any = true;
            continue;
        }

        any = true;

        for (unsigned int code = 0; code <= KEY_MAX; code++) {
            if (test_bit(key_bits, (int)code)) {
                set_payload_key_bit(result->logical_keys, code);
                set_payload_key_bit(result->physical_keys, code);
            }
        }
    }

    ksi_linux_synth_add_logical_key_state(result->logical_keys, sizeof(result->logical_keys));
    result->modifiers_lr = modifiers_from_logical_key_bitmap(result->logical_keys);
    return any;
}

void ksi_linux_devices_set_hook_event_callback(ksi_hook_event_callback callback, void *context)
{
    hook_event_callback = callback;
    hook_event_context = context;
}

void ksi_linux_devices_set_physical_key_event_callback(
    ksi_physical_key_event_callback callback,
    void *context)
{
    physical_key_event_callback = callback;
    physical_key_event_context = context;
}

static void process_device_events(ksi_linux_tracked_device *device)
{
    uint64_t fallback_activity_time_ms = 0u;

    for (size_t processed = 0; processed < KSI_MAX_DEVICE_EVENTS_PER_PASS; processed++) {
        struct input_event event;
        int result;

        if (device->evdev == NULL) {
            return;
        }

        if (device->has_buffered_event) {
            event = device->buffered_event;
            device->has_buffered_event = false;
            memset(&device->buffered_event, 0, sizeof(device->buffered_event));
            result = 0;
        } else {
            result = libevdev_next_event(device->evdev, LIBEVDEV_READ_FLAG_NORMAL, &event);
        }

        if (result == 0) {
            handle_input_event(device, &event, &fallback_activity_time_ms);
            continue;
        }

        if (result == -EAGAIN) {
            dispatch_pending_mouse_move(device);
            return;
        }

        if (result == LIBEVDEV_READ_STATUS_SYNC) {
            continue;
        }

        fprintf(stderr, "inputd: failed reading %s: %s\n",
            device->path,
            strerror(-result));
        close_tracked_device(device);
        return;
    }

    /* Leave remaining events readable for another poll iteration. This keeps a
     * continuously busy device from starving other devices and cleanup work. */
    dispatch_pending_mouse_move(device);
}

static bool buffer_next_device_event(ksi_linux_tracked_device *device)
{
    if (device == NULL || device->evdev == NULL) {
        return false;
    }

    if (device->has_buffered_event) {
        return true;
    }

    for (;;) {
        struct input_event event;
        int result = libevdev_next_event(device->evdev, LIBEVDEV_READ_FLAG_NORMAL, &event);

        if (result == 0) {
            device->buffered_event = event;
            device->has_buffered_event = true;
            return true;
        }

        if (result == -EAGAIN) {
            return false;
        }

        if (result == LIBEVDEV_READ_STATUS_SYNC) {
            continue;
        }

        fprintf(stderr, "inputd: failed peeking %s: %s\n",
            device->path,
            strerror(-result));
        close_tracked_device(device);
        return false;
    }
}

bool ksi_linux_devices_peek_oldest_pending_event(int *out_fd, uint64_t *out_time_ns)
{
    bool found = false;
    uint64_t oldest_time = 0;
    int oldest_fd = -1;

    for (size_t i = 0; i < tracked_device_count; i++) {
        ksi_linux_tracked_device *device = &tracked_devices[i];
        uint64_t time_ms;

        if (!device->grabbed || device->injected_source || device->fd < 0) {
            continue;
        }

        if (!buffer_next_device_event(device)) {
            continue;
        }

        time_ms = event_time_ns(&device->buffered_event);

        if (!found || time_ms < oldest_time) {
            found = true;
            oldest_time = time_ms;
            oldest_fd = device->fd;
        }
    }

    if (!found) {
        return false;
    }

    if (out_fd != NULL) {
        *out_fd = oldest_fd;
    }

    if (out_time_ns != NULL) {
        *out_time_ns = oldest_time;
    }

    return true;
}

static void process_udev_events(void)
{
    for (size_t processed = 0; processed < KSI_MAX_UDEV_EVENTS_PER_PASS; processed++) {
        struct udev_device *device;
        const char *action;
        const char *devnode;

        if (udev_monitor == NULL) {
            return;
        }

        device = udev_monitor_receive_device(udev_monitor);

        if (device == NULL) {
            return;
        }

        action = udev_device_get_action(device);
        devnode = udev_device_get_devnode(device);

        if (devnode != NULL && is_event_device_path(devnode)) {
            if (action != NULL && strcmp(action, "remove") == 0) {
                untrack_device(devnode);
            } else if (action != NULL && (strcmp(action, "add") == 0 || strcmp(action, "change") == 0)) {
                handle_device_add_or_change(device, devnode, action);
            }
        }

        udev_device_unref(device);
    }
}

void ksi_linux_devices_process_fd(int fd)
{
    ssize_t device_index;

    if (udev_monitor != NULL && fd == udev_monitor_get_fd(udev_monitor)) {
        process_udev_events();
        return;
    }

    device_index = find_tracked_device_by_fd(fd);

    if (device_index >= 0) {
        process_device_events(&tracked_devices[device_index]);
    }
}

/* An idle-time request can share one poll wakeup with device input. Client RPC
 * is normally serviced first to keep hook decisions responsive, so explicitly
 * drain every currently-readable upstream device queue before calculating
 * idle duration.
 * The returned idle time therefore comes from the newest ingested kernel event
 * timestamp; it is never replaced with a fabricated zero merely because data
 * was queued. process_device_events retains its normal 256-event inner budget,
 * and this outer loop repeats until every device reaches EAGAIN. */
void ksi_linux_devices_drain_pending_input(void)
{
    for (;;) {
        bool found_pending = false;

        for (size_t i = 0u; i < tracked_device_count; i++) {
            ksi_linux_tracked_device *device = &tracked_devices[i];

            if (device->injected_source || device->fd < 0 || device->evdev == NULL
                || (!device->has_buffered_event
                    && libevdev_has_event_pending(device->evdev) != 1)) {
                continue;
            }

            found_pending = true;
            ksi_linux_devices_process_fd(device->fd);
        }

        if (!found_pending) {
            break;
        }
    }
}
