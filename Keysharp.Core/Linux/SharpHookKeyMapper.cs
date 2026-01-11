
#if !WINDOWS
using SharpHook.Data;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Linux 
{
    internal static class SharpHookKeyMapper
    {
        public static KeyCode VkToKeyCode(uint vk)
        {
            switch (vk)
            {
                case VK_F1:  return KeyCode.VcF1;
                case VK_F2:  return KeyCode.VcF2;
                case VK_F3:  return KeyCode.VcF3;
                case VK_F4:  return KeyCode.VcF4;
                case VK_F5:  return KeyCode.VcF5;
                case VK_F6:  return KeyCode.VcF6;
                case VK_F7:  return KeyCode.VcF7;
                case VK_F8:  return KeyCode.VcF8;
                case VK_F9:  return KeyCode.VcF9;
                case VK_F10: return KeyCode.VcF10;
                case VK_F11: return KeyCode.VcF11;
                case VK_F12: return KeyCode.VcF12;
                case VK_F13: return KeyCode.VcF13;
                case VK_F14: return KeyCode.VcF14;
                case VK_F15: return KeyCode.VcF15;
                case VK_F16: return KeyCode.VcF16;
                case VK_F17: return KeyCode.VcF17;
                case VK_F18: return KeyCode.VcF18;
                case VK_F19: return KeyCode.VcF19;
                case VK_F20: return KeyCode.VcF20;
                case VK_F21: return KeyCode.VcF21;
                case VK_F22: return KeyCode.VcF22;
                case VK_F23: return KeyCode.VcF23;
                case VK_F24: return KeyCode.VcF24;
            }

            return vk switch
            {
                // Letters
                >= (uint)'A' and <= (uint)'Z'
                    => (KeyCode)((int)KeyCode.VcA + (int)(vk - 'A')),

                // Digits
                >= (uint)'0' and <= (uint)'9'
                    => (KeyCode)((int)KeyCode.Vc0 + (int)(vk - '0')),

                // Main editing / navigation
                VK_RETURN      => KeyCode.VcEnter,
                VK_ESCAPE      => KeyCode.VcEscape,
                VK_TAB         => KeyCode.VcTab,
                VK_SPACE       => KeyCode.VcSpace,
                VK_BACK        => KeyCode.VcBackspace,
                VK_DELETE      => KeyCode.VcDelete,
                VK_INSERT      => KeyCode.VcInsert,
                VK_HOME        => KeyCode.VcHome,
                VK_END         => KeyCode.VcEnd,
                VK_PRIOR       => KeyCode.VcPageUp,
                VK_NEXT        => KeyCode.VcPageDown,
                VK_LEFT        => KeyCode.VcLeft,
                VK_RIGHT       => KeyCode.VcRight,
                VK_UP          => KeyCode.VcUp,
                VK_DOWN        => KeyCode.VcDown,

                // Lock keys and print screen
                VK_CAPITAL     => KeyCode.VcCapsLock,
                VK_SCROLL      => KeyCode.VcScrollLock,
                VK_PAUSE       => KeyCode.VcPause,
                VK_NUMLOCK     => KeyCode.VcNumLock,
                VK_SNAPSHOT    => KeyCode.VcPrintScreen,

                // Modifiers
                VK_LSHIFT      => KeyCode.VcLeftShift,
                VK_RSHIFT      => KeyCode.VcRightShift,
                VK_SHIFT       => KeyCode.VcLeftShift,
                VK_LCONTROL    => KeyCode.VcLeftControl,
                VK_RCONTROL    => KeyCode.VcRightControl,
                VK_CONTROL     => KeyCode.VcLeftControl,
                VK_LMENU       => KeyCode.VcLeftAlt,
                VK_RMENU       => KeyCode.VcRightAlt,
                VK_MENU        => KeyCode.VcLeftAlt,
                VK_LWIN        => KeyCode.VcLeftMeta,
                VK_RWIN        => KeyCode.VcRightMeta,

                
                // OEM / punctuation keys (all those from SetupMapping)
                VK_OEM_3       => KeyCode.VcBackQuote,     // `~
                VK_OEM_MINUS   => KeyCode.VcMinus,         // -_
                VK_OEM_PLUS    => KeyCode.VcEquals,        // =+
                VK_OEM_4       => KeyCode.VcOpenBracket,   // [{
                VK_OEM_6       => KeyCode.VcCloseBracket,  // ]}
                VK_OEM_5       => KeyCode.VcBackslash,     // \|
                VK_OEM_1       => KeyCode.VcSemicolon,     // ;:
                VK_OEM_7       => KeyCode.VcQuote,         // '"
                VK_OEM_COMMA   => KeyCode.VcComma,         // ,<
                VK_OEM_PERIOD  => KeyCode.VcPeriod,        // .>
                VK_OEM_2       => KeyCode.VcSlash,         // /?

                // Numpad
                VK_NUMPAD0     => KeyCode.VcNumPad0,
                VK_NUMPAD1     => KeyCode.VcNumPad1,
                VK_NUMPAD2     => KeyCode.VcNumPad2,
                VK_NUMPAD3     => KeyCode.VcNumPad3,
                VK_NUMPAD4     => KeyCode.VcNumPad4,
                VK_NUMPAD5     => KeyCode.VcNumPad5,
                VK_NUMPAD6     => KeyCode.VcNumPad6,
                VK_NUMPAD7     => KeyCode.VcNumPad7,
                VK_NUMPAD8     => KeyCode.VcNumPad8,
                VK_NUMPAD9     => KeyCode.VcNumPad9,
                VK_DIVIDE      => KeyCode.VcNumPadDivide,
                VK_MULTIPLY    => KeyCode.VcNumPadMultiply,
                VK_SUBTRACT    => KeyCode.VcNumPadSubtract,
                VK_ADD         => KeyCode.VcNumPadAdd,
                VK_DECIMAL     => KeyCode.VcNumPadDecimal,
                VK_CLEAR       => KeyCode.VcNumPadClear,
                VK_SEPARATOR   => KeyCode.VcNumPadSeparator,

                // Function keys (F1–F24)
                >= VK_F1 and <= VK_F24
                    => (KeyCode)((int)KeyCode.VcF1 + (int)(vk - VK_F1)),

                // Context menu
                VK_APPS        => KeyCode.VcContextMenu,

                _              => KeyCode.VcUndefined
            };
        }

        public static uint KeyCodeToVk(KeyCode code)
        {
            // Symmetric reverse mapping of VkToKeyCode
            if (code is >= KeyCode.VcA and <= KeyCode.VcZ)
                return (uint)('A' + (code - KeyCode.VcA));

            if (code is >= KeyCode.Vc0 and <= KeyCode.Vc9)
                return (uint)('0' + (code - KeyCode.Vc0));

            switch (code)
            {
                // Main editing / navigation
                case KeyCode.VcEnter:       return VK_RETURN;
                case KeyCode.VcEscape:      return VK_ESCAPE;
                case KeyCode.VcTab:         return VK_TAB;
                case KeyCode.VcSpace:       return VK_SPACE;
                case KeyCode.VcBackspace:   return VK_BACK;
                case KeyCode.VcDelete:      return VK_DELETE;
                case KeyCode.VcInsert:      return VK_INSERT;
                case KeyCode.VcHome:        return VK_HOME;
                case KeyCode.VcEnd:         return VK_END;
                case KeyCode.VcPageUp:      return VK_PRIOR;
                case KeyCode.VcPageDown:    return VK_NEXT;
                case KeyCode.VcLeft:        return VK_LEFT;
                case KeyCode.VcRight:       return VK_RIGHT;
                case KeyCode.VcUp:          return VK_UP;
                case KeyCode.VcDown:        return VK_DOWN;

                // Lock keys and print screen
                case KeyCode.VcCapsLock:    return VK_CAPITAL;
                case KeyCode.VcScrollLock:  return VK_SCROLL;
                case KeyCode.VcPause:       return VK_PAUSE;
                case KeyCode.VcNumLock:     return VK_NUMLOCK;
                case KeyCode.VcPrintScreen: return VK_SNAPSHOT;

                // Modifiers
                case KeyCode.VcLeftShift:   return VK_LSHIFT;
                case KeyCode.VcRightShift:  return VK_RSHIFT;
                case KeyCode.VcLeftControl: return VK_LCONTROL;
                case KeyCode.VcRightControl:return VK_RCONTROL;
                case KeyCode.VcLeftAlt:     return VK_LMENU;
                case KeyCode.VcRightAlt:    return VK_RMENU;
                case KeyCode.VcLeftMeta:    return VK_LWIN;
                case KeyCode.VcRightMeta:   return VK_RWIN;

                // OEM / punctuation
                case KeyCode.VcBackQuote:   return VK_OEM_3;      // `~
                case KeyCode.VcMinus:       return VK_OEM_MINUS;  // -_
                case KeyCode.VcEquals:      return VK_OEM_PLUS;   // =+
                case KeyCode.VcOpenBracket: return VK_OEM_4;      // [{
                case KeyCode.VcCloseBracket:return VK_OEM_6;      // ]}
                case KeyCode.VcBackslash:   return VK_OEM_5;      // \|
                case KeyCode.VcSemicolon:   return VK_OEM_1;      // ;:
                case KeyCode.VcQuote:       return VK_OEM_7;      // '"
                case KeyCode.VcComma:       return VK_OEM_COMMA;  // ,<
                case KeyCode.VcPeriod:      return VK_OEM_PERIOD; // .>
                case KeyCode.VcSlash:       return VK_OEM_2;      // /?

                // Numpad
                case KeyCode.VcNumPad0:        return VK_NUMPAD0;
                case KeyCode.VcNumPad1:        return VK_NUMPAD1;
                case KeyCode.VcNumPad2:        return VK_NUMPAD2;
                case KeyCode.VcNumPad3:        return VK_NUMPAD3;
                case KeyCode.VcNumPad4:        return VK_NUMPAD4;
                case KeyCode.VcNumPad5:        return VK_NUMPAD5;
                case KeyCode.VcNumPad6:        return VK_NUMPAD6;
                case KeyCode.VcNumPad7:        return VK_NUMPAD7;
                case KeyCode.VcNumPad8:        return VK_NUMPAD8;
                case KeyCode.VcNumPad9:        return VK_NUMPAD9;
                case KeyCode.VcNumPadDivide:   return VK_DIVIDE;
                case KeyCode.VcNumPadMultiply: return VK_MULTIPLY;
                case KeyCode.VcNumPadSubtract: return VK_SUBTRACT;
                case KeyCode.VcNumPadAdd:      return VK_ADD;
                case KeyCode.VcNumPadDecimal:  return VK_DECIMAL;
                case KeyCode.VcNumPadClear:    return VK_CLEAR;
                case KeyCode.VcNumPadSeparator:return VK_SEPARATOR;

                // Context menu
                case KeyCode.VcContextMenu:    return VK_APPS;

                // Function keys
                case KeyCode.VcF1:  return VK_F1;
                case KeyCode.VcF2:  return VK_F2;
                case KeyCode.VcF3:  return VK_F3;
                case KeyCode.VcF4:  return VK_F4;
                case KeyCode.VcF5:  return VK_F5;
                case KeyCode.VcF6:  return VK_F6;
                case KeyCode.VcF7:  return VK_F7;
                case KeyCode.VcF8:  return VK_F8;
                case KeyCode.VcF9:  return VK_F9;
                case KeyCode.VcF10: return VK_F10;
                case KeyCode.VcF11: return VK_F11;
                case KeyCode.VcF12: return VK_F12;
                case KeyCode.VcF13: return VK_F13;
                case KeyCode.VcF14: return VK_F14;
                case KeyCode.VcF15: return VK_F15;
                case KeyCode.VcF16: return VK_F16;
                case KeyCode.VcF17: return VK_F17;
                case KeyCode.VcF18: return VK_F18;
                case KeyCode.VcF19: return VK_F19;
                case KeyCode.VcF20: return VK_F20;
                case KeyCode.VcF21: return VK_F21;
                case KeyCode.VcF22: return VK_F22;
                case KeyCode.VcF23: return VK_F23;
                case KeyCode.VcF24: return VK_F24;

                default: return 0; // VK undefined
            }
        }

        public static MouseButton VkToMouseButton(uint vk)
        {
            return vk switch
            {
                VK_LBUTTON  => MouseButton.Button1,
                VK_RBUTTON  => MouseButton.Button2,
                VK_MBUTTON  => MouseButton.Button3,
                VK_XBUTTON1 => MouseButton.Button4,
                VK_XBUTTON2 => MouseButton.Button5,
                _           => MouseButton.NoButton
            };
        }
    }

    internal interface IKeySimulationBackend
    {
        // “Event” mode: send immediately
        void KeyDown(uint vk);
        void KeyUp(uint vk);
        void KeyStroke(uint vk);

        // “Input” mode: batching via SharpHook sequence
        IKeySimulationSequence BeginSequence();
    }

    internal interface IKeySimulationSequence : IDisposable
    {
        void AddKeyDown(uint vk);
        void AddKeyUp(uint vk);
        void AddKeyStroke(uint vk);
        void Commit(long extraInfo);
    }
}
#endif