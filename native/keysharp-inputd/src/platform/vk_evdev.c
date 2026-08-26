#include "vk_evdev.h"

#include <linux/input-event-codes.h>
#include <stddef.h>

/*
 * Single source of truth for the daemon's VK ↔ evdev KEY_* mappings.
 *
 * Each row maps one Windows virtual-key code to one evdev KEY_* code.
 * ksi_vk_to_evdev  returns the first match for a given VK  (canonical synthesis key).
 * ksi_evdev_to_vk  returns the first match for a given evdev code.
 *
 * Where one VK maps to multiple evdev codes (e.g. KEY_ENTER and KEY_KPENTER
 * both satisfy VK_RETURN), the canonical synthesis key comes first; subsequent
 * rows let ksi_evdev_to_vk recognise the alternate evdev code.
 *
 * Note: VK_CLEAR (0x0C) maps to KEY_CLEAR, not KEY_KP5.
 *       KEY_KP5 is VK_NUMPAD5 (0x65).
 *
 * The Keysharp client keeps an equivalent table in
 * Keysharp.Core/Internals/Input/Keyboard/KeyCodes.Evdev.cs, because it has to answer the same
 * questions in-process (key names, synthesis, key-state bitmaps) without a round trip here.
 * The two must agree, or a key reports one VK from a hook event and another from a state
 * query — add every new mapping to both, and to both sides' tests.
 */

typedef struct {
    uint16_t vk;
    uint16_t evdev;
} ksi_vk_evdev_entry;

static const ksi_vk_evdev_entry ksi_vk_evdev_table[] = {
    /* Letters */
    { 0x41u, KEY_A },
    { 0x42u, KEY_B },
    { 0x43u, KEY_C },
    { 0x44u, KEY_D },
    { 0x45u, KEY_E },
    { 0x46u, KEY_F },
    { 0x47u, KEY_G },
    { 0x48u, KEY_H },
    { 0x49u, KEY_I },
    { 0x4Au, KEY_J },
    { 0x4Bu, KEY_K },
    { 0x4Cu, KEY_L },
    { 0x4Du, KEY_M },
    { 0x4Eu, KEY_N },
    { 0x4Fu, KEY_O },
    { 0x50u, KEY_P },
    { 0x51u, KEY_Q },
    { 0x52u, KEY_R },
    { 0x53u, KEY_S },
    { 0x54u, KEY_T },
    { 0x55u, KEY_U },
    { 0x56u, KEY_V },
    { 0x57u, KEY_W },
    { 0x58u, KEY_X },
    { 0x59u, KEY_Y },
    { 0x5Au, KEY_Z },

    /* Digits */
    { 0x30u, KEY_0 },
    { 0x31u, KEY_1 },
    { 0x32u, KEY_2 },
    { 0x33u, KEY_3 },
    { 0x34u, KEY_4 },
    { 0x35u, KEY_5 },
    { 0x36u, KEY_6 },
    { 0x37u, KEY_7 },
    { 0x38u, KEY_8 },
    { 0x39u, KEY_9 },

    /* Control keys */
    { 0x03u, KEY_CANCEL },
    { 0x08u, KEY_BACKSPACE },
    { 0x09u, KEY_TAB },
    { 0x0Cu, KEY_CLEAR },       /* VK_CLEAR — not KEY_KP5 */
    { 0x0Du, KEY_ENTER },       /* VK_RETURN — KEY_ENTER is canonical for synthesis */
    { 0x0Du, KEY_KPENTER },     /* alternate evdev code recognised on input */
    { 0x1Bu, KEY_ESC },
    { 0x20u, KEY_SPACE },

    /* Navigation */
    { 0x21u, KEY_PAGEUP },
    { 0x22u, KEY_PAGEDOWN },
    { 0x23u, KEY_END },
    { 0x24u, KEY_HOME },
    { 0x25u, KEY_LEFT },
    { 0x26u, KEY_UP },
    { 0x27u, KEY_RIGHT },
    { 0x28u, KEY_DOWN },
    { 0x2Du, KEY_INSERT },
    { 0x2Eu, KEY_DELETE },

    /* Misc / OEM control */
    { 0x29u, KEY_SELECT },
    { 0x2Au, KEY_PRINT },
    { 0x2Bu, KEY_OPEN },
    { 0x2Cu, KEY_SYSRQ },       /* VK_SNAPSHOT */
    { 0x2Fu, KEY_HELP },
    { 0x13u, KEY_PAUSE },
    { 0x14u, KEY_CAPSLOCK },
    { 0x90u, KEY_NUMLOCK },
    { 0x91u, KEY_SCROLLLOCK },

    /* IME */
    { 0x15u, KEY_KATAKANAHIRAGANA },
    { 0x15u, KEY_KATAKANA },
    { 0x15u, KEY_HIRAGANA },
    { 0x15u, KEY_HANGEUL },
    { 0x19u, KEY_HANJA },
    { 0x1Cu, KEY_HENKAN },
    { 0x1Du, KEY_MUHENKAN },
    { 0x1Fu, KEY_MODE },

    /* Modifier keys */
    { 0x10u, KEY_LEFTSHIFT },   /* VK_SHIFT   → left as canonical */
    { 0x11u, KEY_LEFTCTRL },    /* VK_CONTROL → left as canonical */
    { 0x12u, KEY_LEFTALT },     /* VK_MENU    → left as canonical */
    { 0xA0u, KEY_LEFTSHIFT },
    { 0xA1u, KEY_RIGHTSHIFT },
    { 0xA2u, KEY_LEFTCTRL },
    { 0xA3u, KEY_RIGHTCTRL },
    { 0xA4u, KEY_LEFTALT },
    { 0xA5u, KEY_RIGHTALT },

    /* Windows/Meta keys */
    { 0x5Bu, KEY_LEFTMETA },
    { 0x5Cu, KEY_RIGHTMETA },
    { 0x5Du, KEY_COMPOSE },
    { 0x5Du, KEY_MENU },
    { 0x5Fu, KEY_SLEEP },

    /* Function keys */
    { 0x70u, KEY_F1 },
    { 0x71u, KEY_F2 },
    { 0x72u, KEY_F3 },
    { 0x73u, KEY_F4 },
    { 0x74u, KEY_F5 },
    { 0x75u, KEY_F6 },
    { 0x76u, KEY_F7 },
    { 0x77u, KEY_F8 },
    { 0x78u, KEY_F9 },
    { 0x79u, KEY_F10 },
    { 0x7Au, KEY_F11 },
    { 0x7Bu, KEY_F12 },
    { 0x7Cu, KEY_F13 },
    { 0x7Du, KEY_F14 },
    { 0x7Eu, KEY_F15 },
    { 0x7Fu, KEY_F16 },
    { 0x80u, KEY_F17 },
    { 0x81u, KEY_F18 },
    { 0x82u, KEY_F19 },
    { 0x83u, KEY_F20 },
    { 0x84u, KEY_F21 },
    { 0x85u, KEY_F22 },
    { 0x86u, KEY_F23 },
    { 0x87u, KEY_F24 },

    /* Numpad */
    { 0x60u, KEY_KP0 },
    { 0x61u, KEY_KP1 },
    { 0x62u, KEY_KP2 },
    { 0x63u, KEY_KP3 },
    { 0x64u, KEY_KP4 },
    { 0x65u, KEY_KP5 },
    { 0x66u, KEY_KP6 },
    { 0x67u, KEY_KP7 },
    { 0x68u, KEY_KP8 },
    { 0x69u, KEY_KP9 },
    { 0x6Au, KEY_KPASTERISK },
    { 0x6Bu, KEY_KPPLUS },
    { 0x6Cu, KEY_KPCOMMA },
    { 0x6Cu, KEY_KPJPCOMMA },
    { 0x6Du, KEY_KPMINUS },
    { 0x6Eu, KEY_KPDOT },
    { 0x6Fu, KEY_KPSLASH },

    /* Media / browser keys */
    { 0xA6u, KEY_BACK },
    { 0xA7u, KEY_FORWARD },
    { 0xA8u, KEY_REFRESH },
    { 0xA9u, KEY_STOP },
    { 0xAAu, KEY_SEARCH },
    { 0xABu, KEY_BOOKMARKS },
    { 0xABu, KEY_FAVORITES },
    { 0xACu, KEY_HOMEPAGE },
    { 0xACu, KEY_WWW },
    { 0xADu, KEY_MUTE },
    { 0xAEu, KEY_VOLUMEDOWN },
    { 0xAFu, KEY_VOLUMEUP },
    { 0xB0u, KEY_NEXTSONG },
    { 0xB1u, KEY_PREVIOUSSONG },
    { 0xB2u, KEY_STOPCD },
    { 0xB3u, KEY_PLAYPAUSE },
    { 0xB3u, KEY_PLAYCD },
    { 0xB3u, KEY_PAUSECD },
    { 0xB3u, KEY_PLAY },
    { 0xB4u, KEY_MAIL },
    { 0xB4u, KEY_EMAIL },
    { 0xB5u, KEY_MEDIA },
    { 0xB6u, KEY_PROG1 },
    { 0xB7u, KEY_PROG2 },

    /* Punctuation / OEM keys */
    { 0xBAu, KEY_SEMICOLON },
    { 0xBBu, KEY_EQUAL },
    { 0xBBu, KEY_KPEQUAL },
    { 0xBCu, KEY_COMMA },
    { 0xBDu, KEY_MINUS },
    { 0xBEu, KEY_DOT },
    { 0xBFu, KEY_SLASH },
    { 0xC0u, KEY_GRAVE },
    { 0xDBu, KEY_LEFTBRACE },
    { 0xDCu, KEY_BACKSLASH },
    { 0xDCu, KEY_YEN },
    { 0xDDu, KEY_RIGHTBRACE },
    { 0xDEu, KEY_APOSTROPHE },
    { 0xE2u, KEY_102ND },
    { 0xE2u, KEY_RO },
};

#define KSI_VK_EVDEV_TABLE_SIZE \
    (sizeof(ksi_vk_evdev_table) / sizeof(ksi_vk_evdev_table[0]))

int ksi_vk_to_evdev(uint16_t vk)
{
    for (size_t i = 0; i < KSI_VK_EVDEV_TABLE_SIZE; i++) {
        if (ksi_vk_evdev_table[i].vk == vk) {
            return (int)ksi_vk_evdev_table[i].evdev;
        }
    }

    return -1;
}

uint32_t ksi_evdev_to_vk(unsigned int evdev_code)
{
    for (size_t i = 0; i < KSI_VK_EVDEV_TABLE_SIZE; i++) {
        if (ksi_vk_evdev_table[i].evdev == (uint16_t)evdev_code) {
            return ksi_vk_evdev_table[i].vk;
        }
    }

    return 0u;
}

size_t ksi_vk_evdev_key_count(void)
{
    return KSI_VK_EVDEV_TABLE_SIZE;
}

unsigned int ksi_vk_evdev_key_at(size_t index)
{
    return index < KSI_VK_EVDEV_TABLE_SIZE
        ? ksi_vk_evdev_table[index].evdev
        : 0u;
}
