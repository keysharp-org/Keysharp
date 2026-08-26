#ifndef KEYSHARP_INPUTD_LINUX_DEVICE_FILTER_H
#define KEYSHARP_INPUTD_LINUX_DEVICE_FILTER_H

#include <stdbool.h>
#include <stdint.h>
#include <linux/input-event-codes.h>
#include <string.h>

/* Match the key ranges udev's input_id classifies as keyboard keys. The gaps
 * are BTN_* controller/tablet ranges even though later KEY_* values follow. */
static inline bool ksi_linux_key_code_is_keyboard(unsigned int code)
{
    return (code > KEY_RESERVED && code < BTN_MISC)
        || (code >= KEY_OK && code < BTN_DPAD_UP)
        || (code >= KEY_ALS_TOGGLE && code < BTN_TRIGGER_HAPPY);
}

/* Map Linux's two common names for each auxiliary mouse button to the same
 * Windows-style five-button state mask used by hook events and state queries. */
static inline uint32_t ksi_linux_pointer_button_mask(unsigned int code)
{
    switch (code) {
    case BTN_LEFT:    return 1u << 0;
    case BTN_RIGHT:   return 1u << 1;
    case BTN_MIDDLE:  return 1u << 2;
    case BTN_SIDE:
    case BTN_BACK:    return 1u << 3;
    case BTN_EXTRA:
    case BTN_FORWARD: return 1u << 4;
    default:          return 0u;
    }
}

/* Select the first (nearest) ID_SEAT found while walking from an event node to
 * its parents.  A property on the event node must win over a conflicting
 * parent property; a missing child property falls back to the parent. */
static inline const char *ksi_linux_device_prefer_nearest_id_seat(
    const char *nearest_id_seat,
    const char *candidate_id_seat)
{
    return nearest_id_seat != NULL ? nearest_id_seat : candidate_id_seat;
}

/* systemd-logind assigns initialized devices without ID_SEAT to the default
 * seat.  Absence is meaningful only after udev has finished processing the
 * device: an uninitialized object may merely be missing ID_SEAT because the
 * lookup raced udev, so it must fail closed. Keep these policy predicates
 * independent of libudev so both discovery paths use identical semantics and
 * the boundary values remain directly testable. */
static inline bool ksi_linux_device_id_seat_is_seat0(const char *id_seat)
{
    return id_seat == NULL || strcmp(id_seat, "seat0") == 0;
}

static inline bool ksi_linux_device_seat_metadata_is_admitted(
    bool metadata_initialized,
    const char *id_seat)
{
    return metadata_initialized && ksi_linux_device_id_seat_is_seat0(id_seat);
}

#endif
