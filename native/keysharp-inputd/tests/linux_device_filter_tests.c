#include "platform/linux_device_filter.h"

#include <stdbool.h>
#include <stdio.h>

static bool expect_seat(const char *id_seat, bool expected, const char *description)
{
    bool actual = ksi_linux_device_id_seat_is_seat0(id_seat);

    if (actual != expected) {
        fprintf(stderr,
            "FAIL: %s: ID_SEAT=%s was %s\n",
            description,
            id_seat == NULL ? "<absent>" : id_seat,
            actual ? "accepted" : "rejected");
        return false;
    }

    return true;
}

static bool expect_keyboard_key(unsigned int code, bool expected, const char *description)
{
    bool actual = ksi_linux_key_code_is_keyboard(code);

    if (actual != expected) {
        fprintf(stderr,
            "FAIL: %s: code=%u was %s\n",
            description,
            code,
            actual ? "accepted" : "rejected");
        return false;
    }

    return true;
}

static bool expect_pointer_button(unsigned int code, uint32_t expected, const char *description)
{
    uint32_t actual = ksi_linux_pointer_button_mask(code);

    if (actual != expected) {
        fprintf(stderr,
            "FAIL: %s: code=%u mask=0x%x expected=0x%x\n",
            description,
            code,
            actual,
            expected);
        return false;
    }

    return true;
}

static bool expect_admission(
    bool metadata_initialized,
    const char *id_seat,
    bool expected,
    const char *description)
{
    bool actual = ksi_linux_device_seat_metadata_is_admitted(
        metadata_initialized,
        id_seat);

    if (actual != expected) {
        fprintf(stderr,
            "FAIL: %s: initialized=%s ID_SEAT=%s was %s\n",
            description,
            metadata_initialized ? "true" : "false",
            id_seat == NULL ? "<absent>" : id_seat,
            actual ? "accepted" : "rejected");
        return false;
    }

    return true;
}

static bool expect_nearest_seat(
    const char *child_id_seat,
    const char *parent_id_seat,
    const char *expected,
    const char *description)
{
    const char *resolved = NULL;

    resolved = ksi_linux_device_prefer_nearest_id_seat(resolved, child_id_seat);
    resolved = ksi_linux_device_prefer_nearest_id_seat(resolved, parent_id_seat);

    if ((resolved == NULL) != (expected == NULL)
        || (resolved != NULL && strcmp(resolved, expected) != 0)) {
        fprintf(stderr,
            "FAIL: %s: child=%s parent=%s resolved=%s expected=%s\n",
            description,
            child_id_seat == NULL ? "<absent>" : child_id_seat,
            parent_id_seat == NULL ? "<absent>" : parent_id_seat,
            resolved == NULL ? "<absent>" : resolved,
            expected == NULL ? "<absent>" : expected);
        return false;
    }

    return true;
}

int main(void)
{
    bool passed = true;

    passed = expect_keyboard_key(KEY_A, true, "ordinary keys are accepted") && passed;
    passed = expect_keyboard_key(BTN_LEFT, false, "mouse buttons are rejected") && passed;
    passed = expect_keyboard_key(KEY_OK, true, "remote-control keys are accepted") && passed;
    passed = expect_keyboard_key(BTN_DPAD_UP, false, "controller D-pad buttons are rejected") && passed;
    passed = expect_keyboard_key(KEY_ALS_TOGGLE, true, "late hardware keys are accepted") && passed;
    passed = expect_keyboard_key(BTN_TRIGGER_HAPPY1, false, "controller trigger buttons are rejected") && passed;
    passed = expect_pointer_button(BTN_SIDE, 1u << 3, "side maps to X1") && passed;
    passed = expect_pointer_button(BTN_BACK, 1u << 3, "back aliases X1") && passed;
    passed = expect_pointer_button(BTN_EXTRA, 1u << 4, "extra maps to X2") && passed;
    passed = expect_pointer_button(BTN_FORWARD, 1u << 4, "forward aliases X2") && passed;
    passed = expect_pointer_button(BTN_TASK, 0u, "unrepresented buttons are rejected") && passed;

    passed = expect_seat(NULL, true, "an absent ID_SEAT defaults to seat0") && passed;
    passed = expect_seat("seat0", true, "seat0 is admitted") && passed;
    passed = expect_seat("seat1", false, "another seat is rejected") && passed;
    passed = expect_seat("", false, "an explicitly empty seat is not absent") && passed;
    passed = expect_seat("Seat0", false, "seat identifiers are case-sensitive") && passed;
    passed = expect_seat("seat0 ", false, "seat identifiers must match exactly") && passed;

    passed = expect_admission(true, NULL, true,
        "confirmed absent ID_SEAT defaults to seat0") && passed;
    passed = expect_admission(false, NULL, false,
        "uninitialized metadata with absent ID_SEAT fails closed") && passed;
    passed = expect_admission(false, "seat0", false,
        "even a provisional seat0 value is rejected until metadata is initialized") && passed;
    passed = expect_admission(true, "seat1", false,
        "an initialized non-seat0 device is rejected") && passed;

    passed = expect_nearest_seat("seat0", "seat1", "seat0",
        "an event-node property takes precedence over its parent") && passed;
    passed = expect_nearest_seat(NULL, "seat1", "seat1",
        "a missing event-node property falls back to its parent") && passed;
    passed = expect_nearest_seat(NULL, NULL, NULL,
        "absence throughout the parent chain stays absent for the seat0 default") && passed;

    /* These policy edges are the deterministic halves of hotplug transitions:
     * handle_device_add_or_change() untracks on rejection and tracks on
     * admission. Thus seat0 -> seat1 selects untrack, while seat1 -> seat0
     * selects admission/re-track without requiring live udev or evdev nodes. */
    passed = expect_admission(true, "seat1", false,
        "seat0-to-seat1 transition selects untrack") && passed;
    passed = expect_admission(true, "seat0", true,
        "seat1-to-seat0 transition selects admission") && passed;

    if (!passed) {
        return 1;
    }

    puts("linux device seat filter tests passed");
    return 0;
}
