This document serves as a high level analysis of the various tools other than AHK/Keysharp for desktop automation.

As is shown here, there are many alternatives, hobby projects and even full fledged commercial software for hotkeys, text expansion and general automation.

It would serve no valuable purpose to create yet another experimental project. Instead, Keysharp aims to extend well known Windows-only functionality to linux.


# Alternative/example projects examined #


## espanso ##
	+ Description: A cross platform text expander.
	+ Platform(s): Windows, linux & Mac.
	+ Language: Rust
	+ Implementation methodology: Reads devices directly and uses some X11 xkb functions. Borrows code from https://github.com/xkbcommon/libxkbcommon/blob/master/tools/interactive-evdev.c
	+ Dependencies:
	+ Pros:
	+ Cons:
	+ Notes:
		espanso's linux keyboard detection code seems to be here: https://github.com/espanso/espanso/blob/dev/espanso-detect/src/x11/native.cpp
		Note they do Windows keyboard/mouse detection differently than AHK. They used raw WM_INPUT rather than installing a keyboard/mouse hook.
	+ Site: https://github.com/espanso, https://espanso.org
		
## pkeymacs ##
	+ Description: 
	+ Platform(s): linux
	+ Language: Python
	+ Implementation methodology:
	+ Dependencies: evdev
	+ Pros:
	+ Cons:
	+ Notes: xkeysnail copied the code for this library and extended it.
	+ Site: https://github.com/zhanghai/pykeymacs
	
## xkeysnail ##
	+ Description: Keyboard remapping and hotkey tool for linux/X11.
	+ Platform(s): linux
	+ Language: Python
	+ Implementation methodology: Reads devices directly and waits for an `EV_KEY` event.
	+ Dependencies: evdev
	+ Pros:
	+ Cons:
	+ Notes:
	+ Site: https://github.com/mooz/xkeysnail
	
## kinto ##
	+ Description: Wrapper around AutoHotkey and xkeysnail that provides Mac-style shortcut keys for linux and Windows.
	+ Platform(s): Windows, linux & Mac.
	+ Language: AHK script, Bash script, Python.
	+ Implementation methodology: Just wraps already implemented programs.
	+ Dependencies: AutoHotkey
	+ Pros:
	+ Cons:
	+ Notes:
	+ Site: https://github.com/rbreaves/kinto 
	
## evdev-sharp-unity ##
	+ Description: A C# wrapper library around evdev supplied as a nuget package.
	+ Platform(s): linux
	+ Language: C#
	+ Implementation methodology: Functions similar to evdev while only using C#.
	+ Dependencies: None
	+ Pros:
	+ Cons:
	+ Notes: This is a fork of https://github.com/afshin-parsa/evdev-sharp which was a fork of https://github.com/kekekeks/evdev-sharp
	+ Site: https://github.com/Draass/evdev-sharp-unity/
	
## actiona ##
	+ Description: Cross platform keyboard/mouse manipulator that provides additional functionality similar to AHK. Also allows executing Javascript code.
	+ Platform(s): Windows & linux.
	+ Language: C++
	+ Implementation methodology: Uses QHotkey from Qt which internally calls RegisterHotKey() on Windows and XGrabKey() on linux. See here: https://github.com/Jmgr/actiona/blob/main/actiona/src/QHotkey/qhotkey_x11.cpp
	+ Dependencies: Qt
	+ Pros:
	+ Cons:
	+ Notes:
	+ Site: https://github.com/Jmgr/actiona
	
## Alfred ##
	+ Description: Mac program that appears to do all thawt AHK does plus more.
	+ Platform(s): Mac
	+ Language: 
	+ Implementation methodology: 
	+ Dependencies:
	+ Pros:
	+ Cons: Closed source
	+ Notes:
	+ Site: https://www.alfredapp.com/
	
## Keyboard Maestro ##
	+ Description: Mac program that appears to do all thawt AHK does plus more.
	+ Platform(s): Mac
	+ Language: 
	+ Implementation methodology: 
	+ Dependencies:
	+ Pros:
	+ Cons: Closed source
	+ Notes:
	+ Site: https://www.keyboardmaestro.com/
	
## Simple Text Expander ##
	+ Description: Simple text expander for Windows.
	+ Platform(s): Windows
	+ Language: C#
	+ Implementation methodology: Uses the basic Windows keyboard hook. When the expansion is sent, it uses SendKeys.SendWait() and uses a bool to stop key interceptions, rather than unwiring and rewiring the hook.
	+ Dependencies:
	+ Pros:
	+ Cons:
	+ Notes:
	+ Site: https://github.com/natebean/SimpleTextExpander
	
## Beeftext ##
	+ Description: Simple text expander for Windows.
	+ Platform(s): Windows
	+ Language: C++
	+ Implementation methodology: Uses the basic Windows keyboard hook with Qt. Details here: https://github.com/xmichelo/Beeftext/blob/master/Beeftext/InputManager.cpp
	+ Dependencies: Qt
	+ Pros: Provides a nice GUI for editing text explansions.
	+ Cons:
	+ Notes: Well written and documented code.
	+ Site: https://beeftext.org/, https://github.com/xmichelo/Beeftext/tree/master
	
## Hypoinput ##
	+ Description: Simple text expander for Windows.
	+ Platform(s): Windows
	+ Language: C++
	+ Implementation methodology: Uses the basic Windows keyboard hook. Details here: https://github.com/giosali/hypoinput/blob/main/Hypoinput/keyboard.cpp
	+ Dependencies:
	+ Pros: Provides a nice GUI written in Powershell for editing text explansions.
	+ Cons:
	+ Notes:
	+ Site: https://github.com/giosali/hypoinput
		
## node-ahk ##
	+ Description: An attempt to make a desktop automation tool using the original IronAHK and NodeJS.
	+ Platform(s): Windows
	+ Language: C# & NodeJS
	+ Implementation methodology: Uses whatever IronAHK did.
	+ Dependencies:
	+ Pros: 
	+ Cons:
	+ Notes: Was ony worked on briefly and stopped being maintained many years ago.
	+ Site: https://github.com/1j01/node-ahk
		
## robotjs ##
	+ Description: An attempt to make a cross platform desktop automation tool using C and NodeJS.
	+ Platform(s): Windows, linux & Mac.
	+ Language: C & NodeJS
	+ Implementation methodology: Cross platform calls, uses SendInput() for Windows. Details with good examples of key code conversion here: https://github.com/octalmage/robotjs/blob/master/src/mouse.c and https://github.com/octalmage/robotjs/blob/master/src/keypress.c
	+ Dependencies:
	+ Pros: 
	+ Cons: Seems to not support hotkeys/strings.
	+ Notes: Was ony worked on briefly and stopped being maintained many years ago.
	+ Site: https://github.com/octalmage/robotjs
		
## LibreAutomate ##
	+ Description: A Windows keyboard/mouse manipulator that provides functionality similar to AHK plus more. The scripting language itself is C#.
	+ Platform(s): Windows
	+ Language: C#
	+ Implementation methodology: Uses the standard API functions such as RegisterHotKey(), SetWindowsHookEx() etc... functions. Details here: https://github.com/qgindi/LibreAutomate/tree/master/Au/Input which include good references for mouse and keyboard processing. Hook code is here: https://github.com/qgindi/LibreAutomate/blob/master/Au/Au.More/WindowsHook.cs which has good code for monitoring timeouts.
	+ Dependencies:
	+ Pros: The library can be used in other programs. Actively maintained.
	+ Cons: Appears to not use any Winforms code for the GUI. Everything is done using Windows API p/invoke.
	+ Notes: Well written and documented code. 
	+ Site: https://www.libreautomate.com/, https://github.com/qgindi/LibreAutomate

## Reginald ##
	+ Description: A general productivity tool for Windows that resembles the official Microsoft PowerToys and was inspired by Alfred. It also has support for text expansion.
	+ Platform(s): Windows
	+ Language: C#
	+ Implementation methodology: Uses the standard Windows calls for hooks such as SetWindowsHookEx(), UnhookWindowsHookEx(), SendInput() etc... Details here: https://github.com/giosali/reginald/tree/main/Reginald.Core/IO/Hooks, https://github.com/giosali/reginald/tree/main/Reginald.Core/IO/Injection and https://github.com/giosali/reginald/blob/main/Reginald/Services/DataModelService.cs
	+ Dependencies:
	+ Pros: 
	+ Cons: 
	+ Notes: Well written code.
	+ Site: https://github.com/giosali/reginald

## map2 ##
	+ Description: A more advanced text expander and key remapper for linux.
	+ Platform(s): linux
	+ Language: Python and Rust.
	+ Implementation methodology: Uses a Rust wrapper around evdev, with Python.
	+ Dependencies: evdev
	+ Pros: More advanced features such as chaining multiple mappers.
	+ Cons: 
	+ Notes: Well written code. Users can create Python style functions, which can be used as key event handlers.
	+ Site: https://github.com/shiro/map2

## Hammerspoon ##
	+ Description: A desktop automation tool for Mac.
	+ Platform(s): Mac
	+ Language: Objective-C
	+ Implementation methodology:
	+ Dependencies:
	+ Pros:
	+ Cons: 
	+ Notes:
	+ Site: https://www.hammerspoon.org/, https://github.com/Hammerspoon/hammerspoon

## AutoKey ##
	+ Description: A desktop automation tool for linux that supports hotkeys and text expansion. It also provides wrappers in Python for accessing system functionality.
	+ Platform(s): linux
	+ Language: Python
	+ Implementation methodology: Provides a scripting language that can call Python scripts in other files in response to user actions. The Python scripts can call Autokey's API to perform system processing. Window manipulation is done by calling the `wmctrl` command. Mouse and keyboard are done using various X11 calls.
	+ Dependencies: dbus-python, packaging, pyinotify, python-xlib, wmctrl, (GTK, GtkSourceView, libappindicator, libdbus-1-dev, libdbus-glib-1-dev, zenity) or (PyQt5, PyQt5 SVG, PyQt5-QScintilla2, kdialog, pyrcc5).
	+ Pros: Provides two installers, `autokey-gtk` for GTK-based DEs, and `autokey-qt` for Qt-based DEs. Undo support to go back in the keypress history, with some limitations.
	+ Cons: Doesn't natively support multi-byte characters. Instead, it suggests you copy such characters to the clipboard, then simulate paste with Ctrl+v.
	+ Notes: Supports global values that are saved to a JSON file which can be accessed by any script as well as local values that are saved to a script-specific JSON file. Uses either AtSpi or XRecord X11 extension for keyboard listening. Contains special processing for Mutter. Details here: https://github.com/autokey/autokey/blob/master/lib/autokey/interface.py and window manipulation details here: https://github.com/autokey/autokey/blob/master/lib/autokey/scripting/window.py
	+ Site: https://github.com/autokey/autokey
	
## Phrase Express ##
	+ Description: A commercial text expander and desktop automation tool for Windows and Mac.
	+ Platform(s): Windows and Mac.
	+ Language: Unknown
	+ Implementation methodology: Unknown
	+ Dependencies: Unknown
	+ Pros: Provides a pleasant and intuitive GUI for managing text templates as well as the ability to share them with others using the cloud.
	+ Cons: 
	+ Notes:
	+ Site: https://www.hammerspoon.org/, https://github.com/Hammerspoon/hammerspoon

## pywinauto ##
	+ Description: A desktop automation and text expander for Windows and linux.
	+ Platform(s): Windows and linux.
	+ Language: Python
	+ Implementation methodology: Provides a Python module that provides a simple interface for automation, so the scripts themselves are written in Python. Uses SetWindowsHookEx()/UnhookWindowsHookEx() on Windows and AtSpi on linux, details here: https://github.com/pywinauto/pywinauto/blob/atspi/pywinauto/linux/atspi_objects.py. Uses `xclip` and `xsel` for linux. Windows details here: https://github.com/pywinauto/pywinauto/blob/atspi/pywinauto/windows/keyboard.py
	+ Dependencies:
	+ Pros:
	+ Cons: Requires users to know Python.
	+ Notes:
	+ Site: https://github.com/pywinauto/pywinauto

## PyAutoGUI ##
	+ Description: A desktop automation tool for Windows, linux and Mac.
	+ Platform(s): Windows, linux and Mac.
	+ Language: Python
	+ Implementation methodology: Provides a Python module with a simple interface for automation, so the scripts themselves are written in Python. Uses `XFakeInput()` from `XTest` on linux.
	+ Dependencies: python3-xlib on linux and pyobjc-core on Mac.
	+ Pros: Full featured and well documented with a book. Minimal implementation with most functionality delegated to well-known imported Python modules.
	+ Cons: Does not support hotkeys or text expansion. Requires users to know Python.
	+ Notes:
	+ Site: https://automatetheboringstuff.com/, https://github.com/asweigart/pyautogui, https://pyautogui.readthedocs.io/en/latest/

## Desktop.Robot ##
	+ Description: A mouse clicker/mover and key sender library for Windows, linux and Mac.
	+ Platform(s): Windows, linux and Mac.
	+ Language: Python
	+ Implementation methodology: Provides a C# library with a simple interface for automation. This library is then used in C# programs. The cross platform functionality is implemented in a unique way. They are C programs that are compiled for each OS, then called into. It would seem much easier to just use pinvoke. Details here: https://github.com/lucassklp/Desktop.Robot/tree/main/Desktop.Robot/Source
	+ Dependencies: Clever way to do keycodes for different OSes: https://github.com/lucassklp/Desktop.Robot/blob/main/Desktop.Robot/Key.cs
	+ Pros: Well designed for good cross platform support.
	+ Cons: Does not support hotkeys or text expansion. Requires users to know C#.
	+ Notes:
	+ Site: https://github.com/lucassklp/Desktop.Robot
	
## AHK_X11 ##
	+ Description: A port of AHKv1 for linux.
	+ Platform(s): linux
	+ Language: Crystal
	+ Implementation methodology: Full rewrite of AHK in Crystal for linux
	+ Dependencies: libxdo, gi-crystal, x11-cr, c_do.cr
	+ Pros: The most advanced attempt at porting AHK to linux in existence.
	+ Cons: Only supports AHKv1.
	+ Notes:
	+ Site: https://github.com/phil294/AHK_X11
	
## rdev ##
	+ Description: An event listener library for Windows, linux and Mac.
	+ Platform(s): Windows, linux and Mac.
	+ Language: Rust
	+ Implementation methodology: Uses evdev to listen to a device on linux, and SetWindowsHookEx() on Windows. Details here: https://github.com/Narsil/rdev/tree/main/src/linux and https://github.com/Narsil/rdev/tree/main/src/windows.
	+ Dependencies: 
	+ Pros:
	+ Cons: Only a listener library for Rust.
	+ Notes:
	+ Site: https://github.com/Narsil/rdev
	
## Input Remapper ##
	+ Description: An input remapper for linux
	+ Platform(s): linux
	+ Language: 
	+ Implementation methodology: 
	+ Dependencies: 
	+ Pros: Supports both Wayland and X11.
	+ Cons: 
	+ Notes:
	+ Site: https://github.com/sezanzeb/input-remapper
	
	
https://nutjs.dev/	
https://sharphook.tolik.io/v5.3.6/ https://sharphook.tolik.io/articles/native.html (this is probably what we'll use)

https://pythonawesome.com/hook-and-simulate-global-keyboard-events-on-windows-and-linux/

https://github.com/boppreh/keyboard

https://github.com/Skycoder42/QHotkey/tree/master/QHotkey

https://github.com/rvaiya/keyd

	
