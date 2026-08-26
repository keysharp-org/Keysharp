# Capability Matrix

Generated from docs/capabilities.json via scripts/generate-capabilities.ps1.

Status legend:
- 🟢 Full: Implemented and generally usable
- 🟡 Partial: Implemented with known limitations or gaps
- 🟠 Planned: Not implemented yet, but intended
- 🔴 Unsupported: Not supported
- ⚪ Unknown: Not yet verified
- `Partial*` on non-Windows `Control*()` functions means script-owned Keysharp controls are supported, but controls in foreign applications are not.

| Capability | Windows | Linux (X11) | Linux (Wayland) | macOS | Notes |
|---|---|---|---|---|---|
| - | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subtraction / unary minus operator |
| -- | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Decrement operator |
| ! | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical NOT operator |
| !~= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Regular-expression not-match operator. |
| != | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Inequality operator |
| !== | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Strict inequality operator. |
| #AssemblyCompany | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly company metadata for compiled scripts. |
| #AssemblyConfiguration | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly configuration metadata for compiled scripts. |
| #AssemblyCopyright | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly copyright metadata for compiled scripts. |
| #AssemblyDescription | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly description metadata for compiled scripts. |
| #AssemblyName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the compiled assembly's identity, which is what Assembly.GetName().Name reports. Unlike the other #Assembly* directives this is not an attribute (.NET has no AssemblyNameAttribute), so it is passed to the compiler rather than emitted as one; A_AssemblyName reads it back. |
| #AssemblyProduct | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly product metadata for compiled scripts. |
| #AssemblyTrademark | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly trademark metadata for compiled scripts. |
| #AssemblyVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets assembly version metadata for compiled scripts. |
| #ClipboardTimeout | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets how long clipboard operations should wait before timing out. |
| #ConsoleApp | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-only, and the AutoHotkey equivalent of Ahk2Exe's ;@Ahk2Exe-ConsoleApp. Builds a --compile exe executable as a console (CUI) application, so a shell waits for it and its stdin/stdout are the terminal's. Windows fixes this in the PE subsystem before the process starts, so it cannot be a runtime setting; GUI remains the default so a double-clicked script never flashes a console window. Ignored when the script is interpreted or compiled to a .cks, since neither writes an executable, and inert on Linux and macOS, where executables have no subsystem and a shell always waits. |
| #CSharp | 🟢 Full | ⚪ Unknown | ⚪ Unknown | ⚪ Unknown | Keysharp-only. Embeds C# members into the script assembly at module or class scope; `#CSharp <Library>` uses #Include's Lib-folder search order, `.cs` extension and underscore fallback. |
| #Define | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Defines a conditional compilation symbol. |
| #DllLoad | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #DllLoad directive loads a DLL or EXE file before the script starts executing. |
| #ElIf | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds an alternate conditional compilation branch. |
| #Else | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds a fallback branch in a conditional compilation block. |
| #EndIf | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Ends a conditional compilation block. |
| #EndRegion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Accepted; a source-folding marker with no runtime semantics. |
| #Error | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Emits the given message as a compile-time diagnostic. |
| #ErrorStdOut | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #ErrorStdOut directive sends any syntax error that prevents a script from launching to the standard error stream (stderr) rather than displaying a dialog. |
| #HookMutexName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the mutex name used for global hook synchronization. |
| #HotIf | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #HotIf directive creates context-sensitive hotkeys and hotstrings. They perform a different action (or none at all) depending on any condition (an expression). |
| #HotIfTimeout | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #HotIfTimeout directive sets the maximum time that may be spent evaluating a single #HotIf expression. |
| #Hotstring | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #Hotstring directive changes hotstring options or ending characters. |
| #If | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Begins a conditional compilation block. |
| #Import | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Imports exported names from another module/script file. Keysharp extension: an #import inside a function/method/property body or a class body binds the imported names lexically to that scope (in AutoHotkey v2.1 it binds module-wide); control-flow-nested and top-level imports match AutoHotkey and bind at module scope. Module loading and execution order stay eager regardless of nesting. |
| #Include | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #Include and #IncludeAgain directives cause the script to behave as though the specified file's contents are present at this exact position. Path separators are platform-independent: a backslash in an #Include, #Import or #CSharp path separates directories on Linux and macOS too, so `#Include Lib\Thing.ahk` resolves everywhere. |
| #IncludeAgain | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #Include and #IncludeAgain directives cause the script to behave as though the specified file's contents are present at this exact position. Path separators are platform-independent: a backslash in an #Include, #Import or #CSharp path separates directories on Linux and macOS too, so `#Include Lib\Thing.ahk` resolves everywhere. |
| #InputLevel | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #InputLevel directive controls which artificial keyboard and mouse events are ignored by hotkeys and hotstrings. |
| #Line | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Accepted; line/file override for diagnostics is a no-op. |
| #MaxThreads | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #MaxThreads directive sets the maximum number of simultaneous threads. |
| #MaxThreadsBuffer | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #MaxThreadsBuffer directive causes some or all hotkeys to buffer rather than ignore keypresses when their #MaxThreadsPerHotkey limit has been reached. |
| #MaxThreadsPerHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #MaxThreadsPerHotkey directive sets the maximum number of simultaneous threads per hotkey or hotstring. |
| #Module | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #Module directive starts a new module or reopens an existing module. |
| #NoTrayIcon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #NoTrayIcon directive disables the showing of a tray icon. |
| #Nullable | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Accepted; nullable-context state is a no-op in Keysharp. |
| #Package | 🟢 Full | ⚪ Unknown | ⚪ Unknown | ⚪ Unknown | Keysharp-only. Resolves NuGet packages at compile time for Clr and inline C#. Supports managed, resource and native assets, but not package build hooks. Windows verified. |
| #Pragma | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Accepted; preprocessor pragma options are a no-op. |
| #Region | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Accepted; a source-folding marker with no runtime semantics. |
| #Requires | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #Requires directive displays an error and quits if a version requirement is not met. |
| #SingleInstance | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #SingleInstance directive determines whether a script is allowed to run again when it is already running. |
| #StructPack | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the maximum alignment for subsequent typed struct fields. |
| #SuspendExempt | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #SuspendExempt directive exempts subsequent hotkeys and hotstrings from suspension. |
| #Undef | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Undefines a conditional compilation symbol. |
| #UseHook | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #UseHook directive forces the use of the hook to implement all or some keyboard hotkeys. |
| #Warn | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enables warnings (VarUnset, Unreachable, LocalSameAsGlobal, NamedArg); VarUnset, Unreachable and NamedArg are on by default. |
| #Warn NamedArg | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp extension. Checks a named argument against the callee's signature at build time when the callee is a bare name. On by default; dispatch is by value at run time so it is a warning, and the binder re-checks and throws. |
| #Warning | 🟠 Planned | 🟠 Planned | 🟠 Planned | 🟠 Planned | Intended to emit a compile-time warning message. No handler exists, so using it is a load-time error. Distinct from #Warn, which is implemented. |
| #WinActivateForce | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The #WinActivateForce directive skips the gentle method of activating a window and goes straight to the forceful method. |
| %...% / Dereference | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Performs dynamic dereferencing (double-deref) to resolve a variable name stored in another variable. |
| & | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Bitwise AND operator |
| & (VarRef) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | VarRef/address-of operator |
| && | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical AND operator |
| &= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| * | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Multiplication operator |
| ** | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Power operator |
| **= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| *= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| , | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comma/sequence operator |
| . | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Concatenation operator |
| .= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| / | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Division operator |
| // | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Integer division operator |
| //= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| /= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| := | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assignment operator |
| ?: | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Ternary operator |
| ?? | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Null coalescing operator |
| ??= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Null-coalescing assignment operator |
| [ ... ] / Array | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates an Array literal. |
| [ ... ] / Map | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates an Map literal. |
| ^ | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Bitwise XOR operator |
| ^= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| __Call | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Meta-function invoked when calling a missing method or property. |
| __Delete | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Meta-function invoked when an object is being deleted. |
| __Enum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns an enumerator for the object. |
| __Get | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Meta-function invoked when getting a missing property. |
| __Init() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Class initialization method executed once before first use. |
| __Item | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Indexer meta-property for bracket access. |
| __New | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Meta-function invoked when constructing a new object. |
| __Set | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Meta-function invoked when setting a missing property. |
| { ... } (Block) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates a block scope for one or more statements. |
| { ... } / Object | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates an Object literal. |
| {Blind} | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Send option which preserves modifier state while sending keys; parsed and applied by the shared cross-platform sender. |
| \\| | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Bitwise OR operator |
| \\|\\| | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical OR operator |
| \\|= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| ~ | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Bitwise NOT operator |
| ~= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Regex match operator |
| + | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Addition / unary plus operator |
| ++ | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Increment operator |
| += | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| < | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comparison operator |
| << | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Left shift operator |
| <<= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| <= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comparison operator |
| <> | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Inequality alias operator |
| = | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Case-insensitive equality operator |
| -= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| == | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Case-sensitive equality operator |
| => | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Fat-arrow function operator. |
| > | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comparison operator |
| >= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comparison operator |
| >> | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Right shift operator |
| >>= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| >>> | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical right shift operator |
| >>>= | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compound assignment operator |
| 1, 2, 3 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Comma operator evaluates expressions left-to-right and returns the last value. |
| A_AhkBuildVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Build version stamped onto the running script assembly. |
| A_AhkPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The full path to the executable compiling the script. For compiled scripts, it's the path to the compiled executable. |
| A_AhkVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The version of the program used to compile the script. |
| A_AllowMainWindow | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_AllowTimers | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets whether timers are allowed to run. |
| A_AppData | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. %APPDATA% on Windows; $XDG_CONFIG_HOME else ~/.config on Linux; ~/Library/Application Support on macOS. |
| A_AppDataCommon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. %ProgramData% on Windows; the first entry of $XDG_CONFIG_DIRS else /etc/xdg on Linux; /Library/Application Support on macOS. Writable only by an administrator, like %ProgramData%. |
| A_Args | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable containing command-line arguments passed to the script. |
| A_AssemblyCompany | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly company metadata set by #AssemblyCompany. |
| A_AssemblyConfiguration | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly configuration metadata set by #AssemblyConfiguration. |
| A_AssemblyCopyright | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly copyright metadata set by #AssemblyCopyright. |
| A_AssemblyDescription | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly description metadata set by #AssemblyDescription. |
| A_AssemblyName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The executing assembly's identity, set by #AssemblyName. Unlike the other A_Assembly* variables this reads the assembly name rather than an attribute, so it always has a value: without the directive it is the name the script was compiled under. |
| A_AssemblyProduct | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly product metadata set by #AssemblyProduct. |
| A_AssemblyTitle | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly title metadata set by #AssemblyTitle. |
| A_AssemblyTrademark | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly trademark metadata set by #AssemblyTrademark. |
| A_AssemblyVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assembly version metadata set by #AssemblyVersion. |
| A_Clipboard | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | A_Clipboard is a built-in variable that reflects the current contents of the Windows clipboard. |
| A_ClipboardTimeout | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets clipboard operation timeout used by Keysharp. |
| A_CommandLine | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the current script command line. |
| A_ComputerName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ComSpec | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ControlDelay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay in milliseconds that will occur after each control-modifying command. |
| A_CoordModeCaret | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the coordinate mode for caret operations to be relative to either the active window, the client area of the active window, or the screen. |
| A_CoordModeMenu | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the coordinate mode for menus to be relative to either the active window, the client area of the active window, or the screen. |
| A_CoordModeMouse | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the coordinate mode for mouse operations to be relative to either the active window, the client area of the active window, or the screen. |
| A_CoordModePixel | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the coordinate mode for pixel operations to be relative to either the active window, the client area of the active window, or the screen. |
| A_CoordModeToolTip | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the coordinate mode for tool tips to be relative to either the active window, the client area of the active window, or the screen. |
| A_Cursor | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_DD | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The two digit day of month from 1 - 31. Same as A_Mday. |
| A_DDD | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current day of the week's abbreviated name in the user's current culture language. |
| A_DDDD | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current day of the week's full name in the user's current culture language. |
| A_DefaultHotstringCaseSensitive | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringConformToCase | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringDetectWhenInsideWord | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringDoBackspace | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringDoReset | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringEndCharRequired | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringEndChars | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringKeyDelay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringNoMouse | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringOmitEndChar | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringPriority | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringSendMode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultHotstringSendRaw | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets default option used for newly created hotstrings. |
| A_DefaultMouseSpeed | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the mouse speed that will be used if unspecified in Click and MouseMove/Click/Drag. |
| A_Desktop | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_DesktopCommon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_DetectHiddenText | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles whether window text searchign includes hidden text. |
| A_DetectHiddenWindows | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles whether window searching includes hidden windows. |
| A_DirSeparator | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the current platform directory separator character. |
| A_EndChar | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_EventInfo | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ExitReason | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Always null until the main form is closing, in which case the value will be "OnExit()". |
| A_FileEncoding | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the encoding used for reading and writing files. This differs from AHK in that it only supports ASCII (ascii), UTF-8 (utf-8/utf8-raw) or Unicode (utf-16/utf16-raw or unicode). ASCII will always return us-ascii because that is the name of the encoding in C#. |
| A_GuiTheme | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets the application-wide GUI theme. Accepted values: Classic, System, Dark. System selects the operating-system theme; Eto follows later system-theme changes, while WinForms resolves the setting when it is applied. Classic selects the Eto light theme on Linux and macOS. |
| A_HasExited | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether script termination has been requested/completed. |
| A_HotIf | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_HotIfTimeout | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the timeout for evaluating #HotIf criteria. |
| A_HotkeyInterval | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_HotkeyModifierTimeout | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Hour | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current 2 digit hour 00 - 23. |
| A_IconFile | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Blank unless a custom tray icon has been specified via Menu, tray, icon, in which case it's the full path and name of the icon's file. |
| A_IconHidden | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets whether the system tray icon is hidden. 1 for hidden, 0 for visible. |
| A_IconNumber | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | If A_IconFile has been specified, gets the number of the icon of the icon file used for the system tray icon. Otherwise blank. |
| A_IconTip | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the tool tip text of the system tray icon. |
| A_Index | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_InitialWorkingDir | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_InputLevel | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the current thread input level. |
| A_Is64bitOS | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_IsAdmin | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_IsCompiled | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | True if the program is running as a compiled executable, else false if it's running as a script passed to Keysharp.exe. |
| A_IsCritical | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns 1 if the script is in critical priority mode, else 0. |
| A_IsPaused | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable indicating whether the current script/thread is paused. |
| A_IsPersistent | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Reports whether the script is persistent. |
| A_IsSuspended | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns 1 if the script is suspended, else 0. |
| A_IsUnicode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Whether the program uses unicode strings. Always returns true because C# programs are always unicode. |
| A_KeybdHookInstalled | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_KeyDelay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur after each keystroke sent by Send and ControlSend. |
| A_KeyDelayPlay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur after each keystroke sent by Send and ControlSend in SendPlay mode. |
| A_KeyDuration | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur between the key down and key up events of each keystroke sent by Send and ControlSend. |
| A_KeyDurationPlay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur between the key down and key up events of each keystroke sent by Send and ControlSend in SendPlay mode. |
| A_KeysharpCorePath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the path to Keysharp.Core. |
| A_KeysharpPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Alias of A_AhkPath containing the running executable path. |
| A_KsVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the Keysharp runtime version. |
| A_Language | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LastError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LineFile | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The full path and name of the file to which A_LineNumber belongs, which will be the same as A_ScriptFullPath unless the line belongs to one of a non-compiled script's #Include files. |
| A_LineNumber | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The exact line number in the script, including comment lines. |
| A_ListLines | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable controlling/listing script line logging behavior. |
| A_LoopField | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopFileAttrib | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The attributes of the file currently retrieved as a string with one character for each attribute present. |
| A_LoopFileDir | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The path of the directory in which A_LoopFileName resides. If FilePattern contains a relative path rather than an absolute path, the path here will also be relative. A root directory will not contain a trailing backslash. For example: C: |
| A_LoopFileExt | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The file's extension (e.g. TXT, DOC, or EXE). The period (.) is not included. |
| A_LoopFileFullPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | This is different than A_LoopFilePath in the following ways: 1) It always contains the absolute/complete path of the file even if FilePattern contains a relative path; 2) Any short (8.3) folder names in FilePattern itself are converted to their long names; 3) Characters in FilePattern are converted to uppercase or lowercase to match the case stored in the file system. This is useful for converting file names -- such as those passed into a script as command line parameters -- to their exact path names as shown by Explorer. |
| A_LoopFileLongPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | A synonym for A_LoopFileFullPath. |
| A_LoopFileName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The name of the file or folder currently retrieved (without the path). |
| A_LoopFilePath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The path and name of the file/folder currently retrieved. If FilePattern contains a relative path rather than an absolute path, the path here will also be relative. Short file names are not used. |
| A_LoopFileShortName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The 8.3 short name, or alternate name of the file. If the file doesn't have one, A_LoopFileName will be retrieved instead. |
| A_LoopFileShortPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The 8.3 short path and name of the file/folder currently retrieved. |
| A_LoopFileSize | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The size in bytes of the file currently retrieved. Files larger than 4 gigabytes are also supported. |
| A_LoopFileSizeKB | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The size in Kbytes of the file currently retrieved, rounded down to the nearest integer. |
| A_LoopFileSizeMB | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The size in Mbytes of the file currently retrieved, rounded down to the nearest integer. |
| A_LoopFileTimeAccessed | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The time the file was last accessed. Format YYYYMMDDHH24MISS. |
| A_LoopFileTimeCreated | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The time the file was created. Format YYYYMMDDHH24MISS. |
| A_LoopFileTimeModified | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The time the file was last modified. Format YYYYMMDDHH24MISS. |
| A_LoopKey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Current key exposed by Keysharp loop helpers. |
| A_LoopReadLine | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopRegKey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopRegName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopRegTimeModified | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopRegType | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_LoopRegValue | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | A new property to get the value of a registry item when using Loop Reg, which is more succinct than typing Value:= RegRead(). |
| A_MaxHotkeysPerInterval | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_MaxThreads | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets the script-wide max thread count setting. |
| A_MaxThreadsBuffer | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets whether new threads are buffered at the thread limit. |
| A_MaxThreadsPerHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the default thread limit per hotkey or hotstring. |
| A_MDay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_MenuMaskKey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Min | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current 2 digit minute 00- 23. |
| A_MM | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The two digit month from 1 - 12. Same as A_Month. |
| A_MMM | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current month's abbreviated text name in the user's current culture language. |
| A_MMMM | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current month's full text name in the user's current culture language. |
| A_Mon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_MouseDelay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur after each mouse movement or click. |
| A_MouseDelayPlay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur after each mouse movement or click in SendPlay mode. |
| A_MouseHookInstalled | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_MSec | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Current 3 digit millisecond 000 - 999. |
| A_MyDocuments | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_NewLine | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform-native newline sequence. |
| A_NoTrayIcon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets whether the tray icon is hidden. |
| A_Now | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current local time in YYYYMMDDHH24MISS format. |
| A_NowMs | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets current local timestamp including milliseconds. |
| A_NowUTC | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current Coordinated Universal Time (UTC) in YYYYMMDDHH24MISS format. |
| A_NowUtcMs | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets current UTC timestamp including milliseconds. |
| A_OSArch | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | CPU architecture of the OS, same names as A_ProcessArch. Differs from it only when the process is emulated, e.g. an X64 build on ARM64 Windows. |
| A_OSType | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Current platform symbol: WINDOWS, LINUX or OSX. |
| A_OSVersion | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_PeekFrequency | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the current thread's message-check interval in milliseconds. |
| A_PriorHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Priority | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the running thread's priority; equivalent to `Thread "Priority", n`. Every thread starts at 0; there is no settable process-wide default. |
| A_PriorKey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ProcessArch | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | CPU architecture of the running process: X64, ARM64, X86 or ARM, matching the preprocessor symbol of the same name. Branch on this rather than A_PtrSize for interop, since A_PtrSize is 8 for both X64 and ARM64. |
| A_ProgramFiles | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Programs | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ProgramsCommon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_PtrSize | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_RealThread | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The real OS thread the current pseudo-thread runs on, as a RealThread object. On the main thread this is the same object as RealThread.Main. |
| A_RegView | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets whether the registry is in 32 or 64 bit mode. |
| A_ScreenDPI | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ScreenHeight | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ScreenScale | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The primary monitor's authored-size scale in native screen units. 1.0 is 100%; Windows and X11 can return a larger value, while macOS and Wayland return 1.0 because their native coordinate spaces already account for UI scaling. Apply it to deliberately scalable dimensions, never absolute positions; use Monitor.Scale for mixed-monitor layouts. |
| A_ScreenWidth | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ScriptDir | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The full path of the script being compiled and ran, without a trailing backslash. Evaluates to a constant string in the C# code output. |
| A_ScriptFullPath | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The full path of the script being compiled and ran. Evaluates to a constant string in the C# code output. |
| A_ScriptHwnd | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The handle to the main window, as an int64, if it exists, else 0. |
| A_ScriptName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The name with extension, but without the path, of the script being compiled and ran. Evaluates to a constant string in the C# code output. |
| A_Sec | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current 2 digit second 00 - 23. |
| A_SendLevel | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Controls which artificial keyboard and mouse events are ignored by hotkeys and hotstrings. |
| A_SendMode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Unsure at the moment because send modes aren't quite clear. |
| A_Space | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | String containing a single space. |
| A_StartMenu | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_StartMenuCommon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Startup | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_StartupCommon | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_StoreCapsLockMode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles whether the state of CapsLock is restored after a Send. |
| A_SuspendExempt | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets whether current thread is exempt from Suspend. |
| A_Tab | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | String containing a single tab. |
| A_Temp | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_ThisFunc | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The name of the function. If called outside of a function, empty string is returned. |
| A_ThisHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Thread | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current pseudo-thread as a Thread object. Id keeps the former A_ThreadId layout: a 48-bit creation sequence and a 16-bit zero-based stack position. |
| A_TickCount | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The number of milliseconds since the system started. Note this is not limited to 49.7 days like AHK because it uses a long integer. |
| A_TimeIdle | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_TimeIdleKeyboard | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_TimeIdleMouse | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_TimeIdlePhysical | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_Timers | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets active timers as a map of callback -> enabled state. |
| A_TimeSincePriorHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_TimeSinceThisHotkey | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_TitleMatchMode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns 1 for matching the start of a title, 2 for matching anywhere in a title, 3 for matching exactly a title, or "regex" for matching using a regular expression. |
| A_TitleMatchModeSpeed | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns "fast" for fast window title matching, or "slow" for slow window title matching. |
| A_TotalScreenHeight | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets total virtual screen height across monitors. |
| A_TotalScreenWidth | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets total virtual screen width across monitors. |
| A_TrayMenu | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_UseHook | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets whether keyboard hook usage is forced. |
| A_UserName | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_WDay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current 1 digit day of the week. |
| A_WinActivateForce | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets whether window activation is forced. |
| A_WinDelay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets or returns the delay that will occur after each windowing command, such as WinActivate. |
| A_WinDir | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_WorkAreaHeight | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets primary work area height. |
| A_WorkAreaWidth | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets primary work area width. |
| A_WorkingDir | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The full path of the working folder of the executable compiling and running the script. |
| A_YDay | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current 1-366 day of the year. |
| A_Year | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in variable. |
| A_YWeek | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The current year and week number expressed as a string containing a 4 digit year and 2 digit week. |
| A_YYYY | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The four digit year. Same as A_Year. |
| Abs() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the absolute value. |
| Acos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the arc cosine. Throws an exception if the argument value is not between -1 and 1. |
| and | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical AND operator. |
| Any | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Special type value that can match any type. |
| Array | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Ordered collection object. |
| Array.__Enum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerates array elements. |
| Array.__Item | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Indexer property for getting or setting array elements. |
| Array.__New() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Constructs a new Array object. |
| Array.Capacity | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the reserved element capacity. |
| Array.Clone() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a shallow copy of an array. |
| Array.Contains() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-specific Array method. Omitting the value searches for an element which has no value. |
| Array.Default | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default value returned for missing indexes. |
| Array.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the element at the specified index to null, returns the element at that index before it was cleared. |
| Array.Filter() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a new array containing elements accepted by a callback predicate. |
| Array.FindIndex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the index of the first element matching a callback predicate. |
| Array.Get() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the value at an index with optional fallback default. |
| Array.Has() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether an array contains a non-empty value at the given index. |
| Array.IndexOf() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the index of the first occurrence of a value. Omitting the value searches for an element which has no value. An out of bounds StartIndex (0, or an absolute value exceeding the length) throws an IndexError, like the sibling FindIndex; an empty array returns 0. |
| Array.InsertAt() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Inserts an element or range of elements at a given index. |
| Array.Join() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Joins array elements into a string with a separator. |
| Array.Length | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets the logical length of the array. |
| Array.MapTo() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a new array transformed by a callback. |
| Array.MaxIndex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the largest integer contained in the array. Returns empty string if no integers are present. |
| Array.MinIndex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the smallest integer contained in the array. Returns empty string if no integers are present. |
| Array.Pop() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Removes and returns the last element of an array. An exception is thrown if the array was empty. |
| Array.Push() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Appends values to the end of an array. |
| Array.Remove() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-specific Array method. Removes the first occurrence of the value and returns true if one was found and removed, else false. Omitting the value removes the first element which has no value. |
| Array.RemoveAt() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Removes the element at a given index, plus optionally a length. Returns the removed item if no length was specified. Returns the null if a length was specified. |
| Array.Sort() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sorts array elements, optionally using a custom comparer callback. |
| Asin() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the arc sine. Throws an exception if the argument value is not between -1 and 1. |
| Atan() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the arc tangent. |
| ATan2() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the arc tangent by using two numbers. |
| Await() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Waits for a Task, a RealThread, or a CLR task and returns its value. Blocks the calling pseudo-thread while pumping everything else, like Sleep, so it is an interruption point rather than a suspension. |
| Base | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the value's base object. |
| Base64Decode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Decodes a Base64 string to binary data. |
| Base64Encode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Encodes binary data to a Base64 string. |
| BlockInput() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Disables or enables physical keyboard and mouse input while allowing synthetic input. Linux suppresses movement-only mode through its existing keysharp-inputd mouse hook and uses the daemon block mask for all-input mode; the X11 xinput fallback is device-wide and cannot preserve buttons while blocking only movement. macOS uses event taps and requires Input Monitoring permission. |
| Boolean | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The type of a truth value, extending Integer, so it reads as 1 or 0 everywhere except a type test. Boolean(Value) converts a value the way `if` decides it. |
| Break | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Exits the current loop. |
| Buffer() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Buffer object encapsulates a block of memory for use with advanced techniques such as DllCall, structures, StrPut and raw file I/O. |
| Buffer.__Item[] | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Indexer for reading/writing bytes in Buffer by offset. |
| Buffer.__New() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Constructs a new Buffer object. |
| Buffer.ToBase64() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns Buffer contents as a Base64 string. |
| Buffer.ToByteArray() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns Buffer contents as a byte array. |
| Buffer.ToHex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns Buffer contents as a hexadecimal string. |
| CallbackCreate() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates a native callback address which redirects to a script function. Supports both parameter-count callbacks and typed parameter/return signatures on every platform. |
| CallbackFree() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Releases a callback created by CallbackCreate on every platform. |
| CaretGetPos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the caret position. Linux uses native GTK geometry for script-owned controls and AT-SPI for foreign applications, normalizing Wayland-local coordinates through compositor window geometry when needed. macOS uses the Accessibility API and requires Accessibility permission. |
| Case | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Case branch label used by switch. |
| Catch | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Handles an exception thrown by try/throw. |
| Ceil() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the ceiling value of a number, rounding away from zero for positive numbers, and toward zero for negative numbers. |
| Chr | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the string (usually a single character) corresponding to the character code indicated by the specified number. |
| Click() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Clicks, holds, releases, wheels, or moves the mouse through the platform input backend. Wayland requires a supported compositor input backend or keysharp-inputd; macOS requires Accessibility permission. |
| Clipboard | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Text, image, URI, custom MIME, wait, and change-notification operations use the native platform clipboard backends. See ClipboardAll() for the Wayland multi-format restore limitation. |
| Clipboard.All | 🟢 Full | 🟢 Full | 🟡 Partial | ⚪ Unknown | The ClipboardAll() save/restore pair as one get/set property. Inherits ClipboardAll()'s Wayland single-representation limitation. |
| Clipboard.Files | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The copied files as an Array of paths, or "". Windows uses CF_HDROP, everything else text/uri-list. Copy semantics only; cut-vs-copy is desktop-specific and not implemented. |
| Clipboard.Formats / Clipboard.Has() / Clipboard.GetData() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The platform-native escape hatch. Format names are NOT normalized ("HTML Format"/"FileDrop" on Windows, "text/html"/"text/uri-list" elsewhere), so a script using them is platform-specific by construction. Has() also accepts the portable kind names Text/Image/Files/Html/Rtf. |
| Clipboard.Html / Clipboard.Rtf | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | HTML is exchanged as the fragment: the Windows CF_HTML header is built on write and stripped on read, so the raw envelope is only visible through GetData("HTML Format"). RTF is the format's source text. |
| Clipboard.Image | 🟢 Full | 🟡 Partial | 🟡 Partial | ⚪ Unknown | Gets an Image or ""; the setter takes an Image, file path, bitmap handle or "HBITMAP:n". Replaces CopyImageToClipboard(), which only accepted a filename. Linux is partial: writing works, but reading an image back through Eto/GTK produced no pixbuf under Xvfb and needs a real desktop session to confirm. |
| Clipboard.OnChange() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Returns a ClipboardHook with Stop/Pause/Paused/IsActive/Count, matching Ks.WinEvent. Registers into the same handler list as OnClipboardChange, so there is one native clipboard monitor either way. The callback is called as callback(hook, type). No callback has been observed firing from a real clipboard change on Linux or macOS. |
| Clipboard.Set() | 🟢 Full | 🟢 Full | 🟡 Partial | ⚪ Unknown | Publishes several formats in one transaction. Windows uses a single raw Win32 transaction, so one change notification; the Eto backends accumulate formats into one offer but publish per entry, so each raises a notification (GTK debounces them into one on Wayland). The Wayland shell-extension fallback can advertise only one MIME type and degrades to the most useful single representation, exactly as ClipboardAll() does there. |
| Clipboard.Text / Clipboard.IsEmpty / Clipboard.Clear() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Text get/set (identical to A_Clipboard), emptiness and clearing. IsEmpty is derived from the platform format enumeration, so private and registered formats count. macOS is unverified: the Eto/Cocoa backend has never been compiled from the development host. |
| Clipboard.Wait() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | ClipWait with the kind vocabulary added: 0/1 behave exactly as before, and "Text", "Any", "Image", "Files", "Html", "Rtf" are also accepted (by ClipWait too). |
| ClipboardAll() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | Captures and restores all advertised clipboard formats on Windows, X11, and macOS. The Wayland extension fallback can restore only one selected MIME representation at a time. |
| ClipCursor() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Confines physical cursor movement to a screen-coordinate rectangle with exclusive right/bottom edges; call without arguments to release. Coordinates ignore CoordMode Mouse. Artificial cursor movement is allowed. Linux requires the keysharp-inputd mouse hook and uses suppress-and-warp-back enforcement, so the cursor may briefly cross the boundary. Wayland also requires a compositor backend that can query and move the global cursor. macOS suppresses out-of-bounds movement and requires Input Monitoring and Accessibility permissions. |
| ClipWait() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Waits until the native platform clipboard contains data. |
| Clr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates the platform-independent CLR interop facade for loading and invoking .NET types. |
| Clr.GetNamespaceName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the namespace name for a managed wrapper or type; implemented by the shared managed runtime. |
| Clr.GetTypeName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the type name for a managed wrapper or object; implemented by the shared managed runtime. |
| Clr.Load() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Loads a managed assembly for CLR interop through the shared .NET runtime. |
| Clr.LoadPackage() | 🟢 Full | ⚪ Unknown | ⚪ Unknown | ⚪ Unknown | Keysharp-only runtime NuGet package loading. Calls accumulate and report version conflicts. Prefer #Package when the dependency is known at compile time. Windows verified. |
| Clr.ManagedAssembly | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform-independent wrapper for a loaded managed assembly. |
| Clr.ManagedInstance | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform-independent managed object instance wrapper. |
| Clr.ManagedInstance.__Enum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerates members exposed by the platform-independent managed instance wrapper. |
| Clr.ManagedNamespace | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform-independent managed namespace wrapper used for type resolution. |
| Clr.ManagedType | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform-independent managed type wrapper for reflection and invocation. |
| Clr.Type() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Resolves managed types through the platform-independent CLR interop surface. |
| Collect() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Forces garbage collection and finalizer processing. |
| COM APIs | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | COM is available on Windows only. |
| ComCall() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComCall function calls a native COM interface method by index. A return type of 'void' means the call returns no value. |
| ComObjActive() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjActive function retrieves a registered COM object. |
| ComObjArray() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjArray function creates a SafeArray for use with COM. |
| ComObjArray.__Enum() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Enumerates a COM SAFEARRAY. COM is available only on Windows. |
| ComObjArray.__Item | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Gets or sets a COM SAFEARRAY element. COM is available only on Windows. |
| ComObjConnect() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjConnect function connects a COM object's event source to the script, enabling events to be handled. |
| ComObject() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObject function creates a COM object. |
| ComObjFlags() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjFlags function retrieves or changes flags which control a COM wrapper object's behaviour. |
| ComObjFromPtr() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjFromPtr function wraps a raw IDispatch pointer (COM object) for use by the script. |
| ComObjGet() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjGet function returns a reference to an object provided by a COM component. |
| ComObjQuery() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjQuery function queries a COM object for an interface or service. |
| ComObjType() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjType function retrieves type information from a COM object. |
| ComObjValue() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComObjValue function retrieves the value or pointer stored in a COM wrapper object. |
| ComponentAvailable() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Reports whether the fixed first-party parser or compiler deployment unit is installed or embedded, compatible, and loadable. The check loads the requested unit; checking compiler can load Roslyn. Parsing/compilation are accepted aliases. |
| ComValue() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ComValue class wraps a value, SafeArray or COM object for use by the script or for passing to a COM method. |
| ComValueRef | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Reference wrapper type for COM values. |
| contains | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Substring containment operator. |
| Continue | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Skips to the next loop iteration. |
| ControlAddItem() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlAddItem function adds a new entry at the bottom of a list box, combo box, or drop-down list. |
| ControlChooseIndex() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlChooseIndex function selects an entry in a list box, combo box, or drop-down list, or a tab control page, by index. |
| ControlChooseString() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlChooseString function selects an entry in a list box, combo box, or drop-down list, or a tab control page, by string. |
| ControlClick() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlClick function sends a mouse button or mouse wheel event to a window or control. |
| ControlDeleteItem() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlDeleteItem function deletes an entry from a list box, combo box, or drop-down list by index. |
| ControlFindItem() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlFindItem function searches for an entry in a list box, combo box, or drop-down list by string, and returns its index. |
| ControlFocus() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlFocus function sets keyboard focus to a control. |
| ControlGetChecked() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetChecked function returns 1 if a check box or radio button is checked, or 0 if unchecked. |
| ControlGetChoice() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetChoice function returns the text of the currently selected entry in a list box, combo box, or drop-down list. |
| ControlGetClassNN() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetClassNN function returns the class ClassNN (class name and sequence number) of a control. |
| ControlGetEnabled() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetEnabled function returns 1 if a control is enabled, or 0 if disabled. |
| ControlGetExStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ControlGetStyle and ControlGetExStyle functions return an integer representing the style or extended style of a control. Eto controls have no Win32 style word, so the non-Windows backend returns the placeholder value 1 for every control rather than a real style. |
| ControlGetFocus() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetFocus function retrieves which control of the target window has keyboard focus, if any. |
| ControlGetHwnd() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetHwnd function returns the window handle (HWND) of a control. |
| ControlGetIndex() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetIndex function returns the index of the currently selected entry in a list box, combo box, or drop-down list, or the index of the active page in a tab control. |
| ControlGetItems() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetItems function returns an array of entries from a list box, combo box, or drop-down list. |
| ControlGetPos() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetPos function retrieves the position and size of a control. |
| ControlGetStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ControlGetStyle and ControlGetExStyle functions return an integer representing the style or extended style of a control. Eto controls have no Win32 style word, so the non-Windows backend returns the placeholder value 1 for every control rather than a real style. (Unlike WinGetStyle, no WS_* projection is done for controls.) |
| ControlGetText() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetText function retrieves text from a control. |
| ControlGetVisible() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlGetVisible function returns 1 if a control is visible, or 0 if hidden. |
| ControlHide() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlHide function hides a control. |
| ControlHideDropDown() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlHideDropDown function hides the popup list of a combo box or drop-down list. |
| ControlMove() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlMove function moves and/or resizes a control. |
| ControlSend() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlSend and ControlSendText functions send simulated keystrokes or text to a window or control. |
| ControlSendText() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlSend and ControlSendText functions send simulated keystrokes or text to a window or control. |
| ControlSetChecked() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlSetChecked function checks or unchecks a check box or radio button. |
| ControlSetEnabled() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlSetEnabled function enables or disables a control. |
| ControlSetExStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ControlSetStyle and ControlSetExStyle functions change the style or extended style of a control. Eto controls have no Win32 style word, so the value is parsed and accepted on non-Windows platforms but has no effect. |
| ControlSetStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ControlSetStyle and ControlSetExStyle functions change the style or extended style of a control. Eto controls have no Win32 style word, so the value is parsed and accepted on non-Windows platforms but has no effect. |
| ControlSetText() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlSetText function changes the text of a control. |
| ControlShow() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlShow function shows a control if it was previously hidden. |
| ControlShowDropDown() | 🟢 Full | 🟡 Partial* | 🟡 Partial* | 🟡 Partial* | The ControlShowDropDown function shows the popup list of a combo box or drop-down list. |
| CoordMode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The CoordMode function sets coordinate mode for various built-in functions to be relative to either the active window or the screen. |
| Copilot declaration/remap alias | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Keysharp extension: in static hotkey and remap declarations only, `Copilot` lowers to the generic `<#<+F23` chord. When used as a remap source, the firmware-generated LWin and LShift modifiers are released but not restored. It is intentionally not a runtime key name for Hotkey(), Send, KeyWait, GetKeyState, InputHook or related APIs. There is no Office alias. |
| Cos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the cosine of a number. |
| Cosh() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the hyperbolic cosine of a number. |
| Critical() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Critical statement prevents the current thread from being interrupted by other threads, or enables it to be interrupted. |
| Crypt.CRC32() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the CRC32 checksum of the input as an integer. Crypt.Hash returns the same checksum as hexadecimal. |
| Crypt.Decrypt() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Decrypts what Crypt.Encrypt produced, returning a Buffer. The key, algorithm, mode and encoding must match the ones it was encrypted under. The initialization vector is read from the front of the data unless IV supplies the one Encrypt was given. |
| Crypt.Encrypt() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Encrypts data with a named symmetric cipher, returning a Buffer. Algorithm is AES (the only one so far, named as a parameter so another is a value rather than a new method) and Mode is CBC, ECB, CFB or GCM. GCM authenticates as well as encrypts, so an altered message is detected on decryption instead of decrypting to rubbish; its nonce is 12 bytes and its tag is appended to the result. The chaining modes and GCM are measured byte-identical on Windows and Linux; GCM is not yet run on macOS, where an unsupported platform reports a ValueError rather than failing obscurely. A string Value or Key is taken as its UTF-8 bytes unless another encoding is named. With IV omitted a random one is drawn per call and written in front of the ciphertext, where Crypt.Decrypt reads it back, so the same text does not encrypt alike twice; supply IV only to match a format defined elsewhere, in which case it is not written to the result. The key is used as it stands rather than stretched by a key-derivation function, so derive one with Crypt.PBKDF2 rather than passing a passphrase; under a chaining mode the result carries no authentication tag, where GCM does. |
| Crypt.Hash() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Hashes a String, Buffer, Array of bytes or open File with MD5, SHA1, SHA256, SHA384, SHA512 or CRC32 (spelled with or without a hyphen), returning uppercase hexadecimal. A File is read as a stream and left at the position it was on. |
| Crypt.HashFile() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Hashes a file, reading it as a stream so that its size does not matter. Takes the same algorithm names as Crypt.Hash. |
| Crypt.MD5() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the MD5 hash of the input. A string is taken as its UTF-8 bytes, so the digest matches the one other tools print for the same text. |
| Crypt.PBKDF2() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Derives key material from a password with PBKDF2, returning a Buffer. This is what makes a passphrase usable as an encryption key, since Crypt.Encrypt otherwise takes the key exactly as given. Algorithm is SHA1, SHA256, SHA384 or SHA512; .NET rejects MD5 for derivation on every platform, so it is not offered. Verified against RFC 6070 on Windows and Linux; not yet run on macOS. |
| Crypt.RandomBytes() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns cryptographically secure random bytes as a Buffer, for an initialization vector, a salt or a key. |
| Crypt.SecureRandom() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Generates a cryptographically secure random number. Returns an integer unless either bound is a float. |
| Crypt.SHA1() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the SHA-1 hash of the input. A string is taken as its UTF-8 bytes, so the digest matches the one other tools print for the same text. |
| Crypt.SHA256() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the SHA-256 hash of the input. A string is taken as its UTF-8 bytes, so the digest matches the one other tools print for the same text. |
| Crypt.SHA384() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the SHA-384 hash of the input. A string is taken as its UTF-8 bytes, so the digest matches the one other tools print for the same text. |
| Crypt.SHA512() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the SHA-512 hash of the input. A string is taken as its UTF-8 bytes, so the digest matches the one other tools print for the same text. |
| Date Time Built-in Variables | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Language/runtime capability. |
| DateAdd() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The DateAdd function adds or subtracts time from a date-time value. |
| DateDiff() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The DateDiff function compares two date-time values and returns the difference. |
| Default | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default branch label used by switch. |
| DefineProp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Defines or modifies an own property without invoking an overridden method. |
| DelegateHolder | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Callback object type returned by CallbackCreate; exposes the native address through Ptr. |
| DetectHiddenText() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The DetectHiddenText function determines whether invisible text in a window is "seen" for the purpose of finding the window. |
| DetectHiddenWindows() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The DetectHiddenWindows function determines whether invisible windows are "seen" by the script. |
| DirCopy() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Copies a folder along with all its sub-folders and files (similar to xcopy). A .zip, .tar, .tar.gz or .tgz source is extracted into the destination folder; a plain .gz is decompressed to the destination as a single file. |
| DirCreate() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates a folder, and all of its parent folders if needed. |
| DirDelete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes a folder, optionally recursive. |
| Directives and preprocessing | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | OS-specific directives supported via compile constants. |
| DirExist() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Checks for the existence of a folder and returns its attributes. |
| DirMove() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Moves a folder along with all its sub-folders and files. It can also rename a folder. |
| DirSelect() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Displays the native folder-selection dialog on every platform. Some legacy Windows folder-dialog option flags have no portable equivalent. |
| DllCall() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Calls a native shared-library function on every platform. Numeric type classes can replace built-in type names, and a 'void' return type yields no value. |
| Download() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Download function downloads a file from the Internet. |
| DriveEject() | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Ejects or retracts the tray of the specified CD/DVD drive. |
| DriveGetCapacity() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the total capacity of the drive which contains the specified path, in megabytes. |
| DriveGetFileSystem() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the type of the specified drive's file system. |
| DriveGetLabel() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the volume label of the specified drive. |
| DriveGetList() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a string of letters, one character for each drive letter in the system. |
| DriveGetSerial() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the volume serial number of the specified drive. |
| DriveGetSpaceFree() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The DriveGetSpaceFree function returns the free disk space of the drive which contains the specified path, in megabytes. |
| DriveGetStatus() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the status of the drive which contains the specified path. |
| DriveGetStatusCD() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Retrieves CD/DVD media playback status through Windows MCI. Linux has no media-playback-status backend, and the feature is permanently unsupported on macOS. |
| DriveGetType() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the type of the drive which contains the specified path. |
| DriveLock() | 🟢 Full | 🟢 Full | 🟢 Full | 🔴 Unsupported | Prevents media ejection. Linux uses the eject utility; eject locking is permanently unsupported on macOS. |
| DriveRetract() | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | The DriveEject and DriveRetract functions eject or retract the tray of the specified CD/DVD drive. DriveEject can also eject a removable drive. |
| DriveSetLabel() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Changes a volume label. Windows uses DriveInfo, Linux dispatches to the installed filesystem-specific label utility, and macOS uses diskutil renameVolume. Unix operations can require elevated privileges; filesystem/backend coverage needs manual verification. |
| DriveUnlock() | 🟢 Full | 🟢 Full | 🟢 Full | 🔴 Unsupported | Restores media ejection. Linux uses the eject utility; eject locking is permanently unsupported on macOS. |
| Edit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Opens the script, or the file named by the optional argument, in a text editor rather than the extension's handler. |
| EditGetCurrentCol() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditGetCurrentCol function returns the column number in an edit control where the caret resides. |
| EditGetCurrentLine() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditGetCurrentLine function returns the line number in an edit control where the caret resides. |
| EditGetLine() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditGetLine function returns the text of a line in an edit control by line number. |
| EditGetLineCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditGetLineCount function returns the number of lines in an edit control. |
| EditGetSelectedText() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditGetSelectedText function returns the selected text in an edit control. |
| EditPaste() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The EditPaste function pastes a string at the caret in an edit control. |
| Else | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Alternate branch executed when if condition is false. |
| Enumerator | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerator object used for iteration. |
| EnvGet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the value of the specified environment variable if it exists, else it returns an empty string. |
| EnvSet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the specified environment variable to the specified value. Using a value of null deletes the variable. |
| EnvUpdate() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Windows broadcasts WM_SETTINGCHANGE. Linux publishes pending EnvSet changes to the D-Bus activation environment and systemd user manager; D-Bus deletions become empty values because its update API cannot unset them. macOS publishes pending changes to the current launchd session. Existing processes are unchanged and these updates are not persistent. |
| Error | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| Exit | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Exits the current pseudo-thread immediately. Terminating a different pseudo-thread is Thread.Exit(ExitCode?) on a Thread object, reached through KS.A_Thread.Underlying or KS.A_RealThread.Threads[i] (1-based, oldest first). An underlying target exits at its next cooperative event/message check; later requests overwrite its pending exit code. |
| ExitApp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ExitApp function terminates the script. |
| Exp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes e raised to the nth power. |
| Export | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | An Export declaration marks a function, class or variable for wildcard import, and optionally marks it as the default export. |
| extends | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keyword used to derive a class from a base class. |
| False | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Boolean false constant. |
| File | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | File object type. |
| File and directory operations | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | macOS recycle/trash and privacy-scoped file access still evolving. |
| FileAppend() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Writes text or binary data to the end of a file (first creating the file, if necessary). |
| FileCopy() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Copies one or more files. |
| FileCreateShortcut() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Windows creates .lnk files. Linux and macOS create symbolic links or freedesktop .desktop launchers; Windows-only icon-number, hotkey, and run-state metadata is not represented, and macOS aliases are not created. |
| FileCreateTemp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates an empty temporary file and returns its full path. |
| FileDelete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes one or more files. |
| FileDirName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns directory portion of a file path. |
| FileEncoding() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the default encoding for FileRead, Loop Read, FileAppend, and FileOpen. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| FileExist() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Checks for the existence of a file or folder and returns its attributes. |
| FileFullPath() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns absolute normalized full path. |
| FileGetAttrib() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Reports whether a file or folder is read-only, hidden, etc. |
| FileGetShortcut() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Windows reads .lnk metadata. Linux and macOS read symbolic links or freedesktop .desktop launchers; Windows-only icon-number and run-state metadata is unavailable, and argument extraction from .desktop Exec fields is approximate. |
| FileGetSize() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the size of a file. Also allows for passing "t" to return the size in terms of terrabytes. |
| FileGetTime() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the datetime stamp of a file or folder. |
| FileGetVersion() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Retrieves the version of a file. Version metadata availability differs by platform file formats. |
| FileInstall() | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | All scripts are converted into compiled executables, so this doesn't apply. |
| FileMove() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Moves or renames one or more files. |
| FileOpen() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Platform statuses inherited from curated 'File and directory operations'; per-function validation pending. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| FileRead() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the contents of a file. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| FileRecycle() | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Windows uses the recycle bin and Linux uses the freedesktop Trash through GIO. macOS currently moves only to the user ~/.Trash folder, without per-volume trash or Finder Put Back metadata. |
| FileRecycleEmpty() | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Windows empties the recycle bin and Linux empties the freedesktop Trash through GIO. macOS currently empties only the user ~/.Trash folder, not per-volume trash folders. |
| FileSelect() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Displays the native open/save file dialog, including cancellation and multiple selection, on every platform. |
| FileSetAttrib() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes the attributes of one or more files or folders. Wildcards are supported. |
| FileSetTime() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes the datetime stamp of one or more files or folders. Wildcards are supported. |
| Finally | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Runs after try/catch regardless of whether an exception occurred. |
| Float() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Float function converts a numeric string or integer value to a floating-point number. |
| Float32 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Float64 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Floor() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes a number rounded down to the nearest integer. Rounds toward zero for positive numbers and away from zero for negative numbers. |
| Font | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | A font as a value object carrying what Gui.SetFont takes, each property optional, plus the platform's Ui/Emoji/GuiDefault/Monospace families. |
| For | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Iterates over enumerable values or key/value pairs. |
| Foreign window management (non-Keysharp apps) | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | On Linux, Control* functions are not supported for foreign apps; use the included AtSpi library for cross-process window/control interaction. COSMIC supplies native listing, active state, geometry, and window events. Coordinate hit-testing is limited to the active window because the protocol exposes neither workspace membership nor stacking order; window actions and general move/resize are not supported. macOS currently relies on Accessibility APIs with permission requirements. |
| Format() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Formats text by substituting placeholders with argument values. |
| FormatCs() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Formats text using C#-style format placeholders (1-based indexing adaptation). |
| FormatTime | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Formats a datetime string according to the parameters. All C# formatting options are supported. Supports all V2 functionality except for the Dn and Tn options. If you want to specify a specific format, do it in the format parameter. |
| Func | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Function object type. |
| Func.Params | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp extension. An Array describing the function's parameters (Name, Index, Optional, ByRef, Variadic, and Default when there is a real one). Reports exactly the names named-argument binding uses, excluding the receiver, internal parameters, and a bound function's already-bound ones. Unset when the parameters cannot be known (ObjBindMethod). |
| FuncObj | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Managed function-object type name accepted by compatibility type checks alongside Func. |
| GetKeyboardLayout() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a readable platform-native identifier for the active keyboard layout. |
| GetKeyInfo() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns layout-aware portable VK, platform-native SC, key name, and modifier information on every platform. |
| GetKeyName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the portable name or text of a key from Windows scan codes, Linux evdev codes, or macOS kVK codes. |
| GetKeySC() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the platform-native physical key code: a Windows scan code, Linux evdev KEY_* code, or macOS kVK code. Returns 0 for a portable key which has no native physical code on that platform. |
| GetKeyState() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns logical, physical, and toggle state. Linux queries keysharp-inputd first for keyboard keys and all five mouse buttons without installing a hook (input-monitoring permission is required), with X11 core queries as a basic fallback and no XInput2 path. macOS uses combined-session and HID-system state APIs. |
| GetKeyVK() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns Keysharp's portable Windows-compatible VK for a named key or platform-native physical key code. |
| GetMethod() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The GetMethod function retrieves the implementation function of a method. |
| Global keyboard hooks | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Linux uses evdev/uinput, macOS uses CGEventTap. |
| Global mouse hooks | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Suppression/injection semantics differ by platform. |
| Goto | 🟡 Partial | 🟡 Partial | 🟡 Partial | 🟡 Partial | Goto doesn't support expressions in Keysharp. |
| GroupActivate() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The GroupActivate function activates the next window in a window group that was defined with the GroupAdd function. |
| GroupAdd() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The GroupAdd function adds a window specification to a window group, creating the group if necessary. |
| GroupClose() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The GroupClose function closes the active window if it was just activated by the GroupActivate or GroupDeactivate function. |
| GroupDeactivate() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The GroupDeactivate function is similar to the GroupActivate function but activates the next window not in the group. |
| Gui control types | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | GUI control types are elements of interaction which can be added to a GUI window using the Gui object's Add method. ActiveX and Custom are Win32-only and raise a ValueError on Linux and macOS; every other type is available on all platforms. ListView additionally supports only the Report view there (see Gui.Add). |
| Gui() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Gui object provides an interface to create a window, add controls, modify the window, and retrieve information about the window. Such windows can be used as data entry forms or custom user interfaces. |
| Gui.__Enum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns an enumerator for GUI controls. |
| Gui.__Item | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Indexer property for retrieving controls by name or key. |
| Gui.__New() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Constructs a new GUI window object. |
| Gui.Add() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Adds a control to the GUI. The ActiveX and Custom control types are Win32-only and raise a ValueError on Linux and macOS. A ListView created with a view option other than +Report (+Icon, +IconSmall, +Tile, +List) is not implemented there either. |
| Gui.BackColor | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the GUI background color. |
| Gui.Call() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Shows the GUI when the object is called like a function. |
| Gui.Control.Add() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds an item to controls that support item lists. |
| Gui.Control.Choose() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Selects an item in the control. |
| Gui.Control.ClassNN | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | ClassNN identifier of the control. Off Windows only a top-level window reports a ClassNN; a child control returns an empty string, because Eto controls are drawn by the toolkit and have no per-control native window class. |
| Gui.Control.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes items from controls that support item lists. |
| Gui.Control.Enabled | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets whether the control is enabled. |
| Gui.Control.Focus() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets keyboard focus to the control. |
| Gui.Control.Focused | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Whether the control currently has focus. |
| Gui.Control.Font | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Gets or sets the control's font as a detached Ks.Font snapshot; colour round-trips through ForeColor. |
| Gui.Control.GetPos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the control position and size. |
| Gui.Control.Gui | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Parent GUI object for the control. |
| Gui.Control.Hwnd | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Native window handle of the control. |
| Gui.Control.Move() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Moves or resizes the control. |
| Gui.Control.Name | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Associated control name. |
| Gui.Control.OnCommand() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Registers a WM_COMMAND callback for the control. WM_COMMAND is a Win32 concept; the non-Windows implementation accepts the call, registers nothing and never fires. |
| Gui.Control.OnEvent() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Registers a control event callback. |
| Gui.Control.OnMessage() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Registers Callback(GuiCtrlObj, wParam, lParam, Msg) to run when the control receives the given window message, ahead of the WM_COMMAND/WM_NOTIFY reflection and default processing. Matches AHK: a non-empty return claims the message (it becomes the reply, the remaining handlers are skipped and default processing is suppressed), while an empty string or no return at all lets the next handler and then default handling run. An explicit 0 therefore claims the message and replies 0. AddRemove is 1 (append, default), -1 (prepend) or 0 (unregister); any other value is a ValueError. Window messages are a Win32 concept, so on Linux and macOS the registration is accepted but no handler is ever invoked. |
| Gui.Control.OnNotify() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Registers a WM_NOTIFY callback for the control. WM_NOTIFY is a Win32 concept; the non-Windows implementation accepts the call, registers nothing and never fires. |
| Gui.Control.Opt() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets options for the control. Changing a ListView to a view other than Report (+Icon, +IconSmall, +Tile, +List) is not implemented on Linux or macOS and raises. Raw Win32 style options (+0x..., +E0x...) have no portable equivalent and are ignored. |
| Gui.Control.Redraw() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Redraws the control. |
| Gui.Control.SetCue() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets cue banner (placeholder text) for the control. |
| Gui.Control.SetFont() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the control font. |
| Gui.Control.Text | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the control text. |
| Gui.Control.Type | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Control type name. |
| Gui.Control.Value | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the control value. |
| Gui.Control.Visible | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets whether the control is visible. |
| Gui.Destroy() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Destroys the GUI window and releases associated resources. |
| Gui.Flash() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Flashes the GUI window to attract attention. Backed by the Win32 FlashWindow API; the call is accepted and does nothing on Linux and macOS. |
| Gui.FocusedCtrl | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Currently focused control in the GUI. |
| Gui.Font | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Gets or sets the font later controls inherit, as a detached Ks.Font snapshot. |
| Gui.GetClientPos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the GUI client-area position and size. |
| Gui.GetPos() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the GUI window position and size. |
| Gui.Hide() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Hides the GUI window. |
| Gui.Hwnd | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Native window handle of the GUI. |
| Gui.Icon | 🟢 Full | 🟡 Partial | 🔴 Unsupported | 🔴 Unsupported | Gets or sets this window's own icon, in place of the tray icon it was created with. Reading returns an Image holding the frame the window shows at its preferred size, or "" when it has none, so an icon can be copied from one window to another. Property form of Gui.SetIcon(). Partial on X11 for the same reasons as the setter. Wayland has no per-window icon protocol and resolves the icon from the window's app id instead; macOS windows have no icon. |
| Gui.MarginX | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default horizontal margin for layout. |
| Gui.MarginY | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default vertical margin for layout. |
| Gui.Maximize() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Maximizes the GUI window. |
| Gui.MenuBar | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the menu bar attached to the GUI. |
| Gui.Minimize() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Minimizes the GUI window. |
| Gui.Move() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | Moves and/or resizes the window. On Wayland a client cannot set its own position, so the move is routed through the compositor backend and needs KWin, GNOME or Cinnamon with the Keysharp extension; resizing works everywhere. |
| Gui.Name | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Associated GUI name. |
| Gui.OnEvent() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Registers a GUI event callback. |
| Gui.OnMessage() | 🟢 Full | 🟡 Partial | 🟡 Partial | ⚪ Unknown | Registers Callback(GuiObj, wParam, lParam, Msg) to run when the GUI window receives the given window message, ahead of the default window procedure. Matches AHK: a non-empty return claims the message (it becomes the reply, the remaining handlers are skipped and default processing is suppressed), while an empty string or no return at all lets the next handler and then the default window procedure run. An explicit 0 therefore claims the message and replies 0; a non-numeric value claims it and replies 0. AddRemove is 1 (append, default), -1 (prepend) or 0 (unregister); any other value is a ValueError. Off Windows the same synthesized input messages OnMessage() receives are delivered here, after the global monitors have had their turn; see that entry for which messages exist and what is missing. |
| Gui.Opt() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets options for the window. Raw Win32 style and extended-style options (+0x..., +E0x...) have no portable equivalent and are ignored on Linux and macOS; the named options are honoured through the toolkit's own properties. The Theme option is unsupported on every platform. +Round rounds the window's outer corners, on Windows 11 only. |
| Gui.Restore() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Restores the GUI window from minimized or maximized state. |
| Gui.SetFont() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the default font for subsequent controls. |
| Gui.SetIcon() | 🟢 Full | 🟡 Partial | 🔴 Unsupported | 🔴 Unsupported | Gives one window an icon of its own, from a file, an icon resource in a module, an "HICON:"/"HBITMAP:" handle or an Image, applying to a window which is already open. Every size the source carries is kept, so each place the icon appears takes the one it wants; the Options "Wn" chooses the large (alt-tab and taskbar) size; a source carrying fixed sizes supplies the nearest it holds, one that is resampled anyway lands exactly. Unsupported on Wayland and macOS for the reason given under Gui.Icon. Partial on X11: icon resources inside a module are addressable by name but not by index, and the size option selects the nearest frame the source already carries rather than resampling. |
| Gui.Show() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | Displays the window. Explicit X/Y placement on Wayland goes through the compositor backend (KWin, GNOME or Cinnamon with the Keysharp extension) because a client cannot position its own top-level; size, state and centring work everywhere. |
| Gui.Submit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Submits control values and returns them to script variables. |
| Gui.Tab.UseTab() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Selects the active tab page for subsequent control additions. |
| Gui.Title | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the GUI window title. |
| Gui.Visible | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets/sets GUI visibility state. |
| GuiCtrlFromHwnd() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The GuiCtrlFromHwnd function retrieves the GuiControl object of a GUI control associated with the specified window handle. |
| GuiFromHwnd() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The GuiFromHwnd function retrieves the Gui object of a GUI window associated with the specified window handle. |
| HasBase() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The HasBase function returns a non-zero number if the specified value is derived from the specified base object. |
| HashMap | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-specific map class extending Map without sorted enumeration. |
| HashMap.__New() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-specific HashMap constructor (inherits Map methods/properties). |
| HasMethod() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The HasMethod function returns a non-zero number if the specified value has a method by the specified name. |
| HasProp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The HasProp function returns a non-zero number if the specified value has a property by the specified name. |
| Highlight | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Cross-platform reusable screen-region highlighter built on Overlay. |
| HotIf() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The HotIf and HotIfWin functions specify the criteria for subsequently created or modified hotkey variants and hotstring variants. |
| HotIfWinActive() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets hotkey context for active windows. |
| HotIfWinExist() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets hotkey context for existing windows. |
| HotIfWinNotActive() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets hotkey context for windows that are not active. |
| HotIfWinNotExist() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets hotkey context for windows that do not exist. |
| Hotkey() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The Hotkey function creates, modifies, enables, or disables a hotkey while the script is running. |
| Hotkeys/Hotstrings | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Depends on hook and key-state parity. |
| Hotstring() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The Hotstring function creates, modifies, enables, or disables a hotstring while the script is running. |
| If | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Conditional statement. |
| IL_Add() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds an image to an image list, optionally can resize or split the image. |
| IL_Create() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates an image list and returns its unique ID. Differs in that it only takes one parameter, LargeIcons, because the first two parameters, InitialCount and GrowCount, have been omitted because C# handles memory internally. |
| IL_Destroy() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Removes an ImageList from the global list of ImageLists. Note, this does not dispose it, it just removes the reference. The garbage collector will handle final disposal when the reference count goes to 0. |
| Image | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Cross-platform image class for capturing, loading, or creating an image, then drawing, transforming, searching, and reading/writing its pixels before saving it or handing out a bitmap handle. Search/SearchAll/SearchPixel return match objects ({X, Y[, Color]}, "" or an empty array on a miss) with an optional flat (x, y, width, height) region; MeasureText returns {Width, Height}; fonts use Gui.SetFont-style (Options, FontName) arguments; the capture origin/density metadata is exposed as OriginX/OriginY/ScaleX/ScaleY ("" after Rotate/Flip until SetOrigin re-anchors); the per-pixel alpha transform is Alpha(0..1). Full on Windows, partial on Linux (X11); Wayland and macOS are unverified. Replaces the earlier ImageCapture() function. |
| ImageSearch() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Searches a region of the screen for an image, with the AHK signature (writes the found position to the ByRef output variables and returns true/false). Does not support file types of .ani, .emf, .exif or .wmf. Only 32-bit color is supported. For searching a captured/loaded image instead of the live screen, see the Image class's Search/SearchAll/SearchPixel. |
| Import | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Import declaration imports a module, or imports names from a module. |
| in | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Membership operator. |
| IndexError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| IniDelete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes a value from a standard format.ini file. |
| IniRead() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Reads a value, section or list of section names from a standard format.ini file. |
| IniWrite() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Writes a value or section to a standard format.ini file. |
| InputBox() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The InputBox function displays an input box to ask the user to enter a string. On Linux and macOS the dialog is built with Eto and honours only the Password option; the W, H, X, Y and T (timeout) options are ignored, so Result never returns "Timeout". |
| InputHook() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The InputHook function creates an object which can be used to collect or intercept keyboard input. |
| InputHook.BackspaceIsUndo | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Treats Backspace as undo for collected input. |
| InputHook.CaseSensitive | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether match checks are case-sensitive. |
| InputHook.EndKey | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Key that ended the input hook. |
| InputHook.EndMods | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Modifier-state snapshot when input ended. |
| InputHook.EndReason | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Reason the input hook ended. |
| InputHook.FindAnywhere | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Matches phrases anywhere in the input buffer. |
| InputHook.InProgress | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether the input hook is currently running. |
| InputHook.Input | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Text captured so far by the input hook. |
| InputHook.KeyOpt() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets key-specific behavior options for the input hook. |
| InputHook.Match | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Phrase that matched and ended input, if any. |
| InputHook.MinSendLevel | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Minimum SendLevel accepted by the hook. |
| InputHook.NotifyNonText | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether non-text key notifications are enabled. |
| InputHook.OnChar | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Callback invoked for character input. |
| InputHook.OnEnd | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Callback invoked when input capture ends. |
| InputHook.OnKeyDown | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Callback invoked on key-down events. |
| InputHook.OnKeyUp | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Callback invoked on key-up events. |
| InputHook.OnMouseMove | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | OnMouseMove(hook, dx, dy) queues every move report, including (0, 0). Windows derives deltas from desktop-clamped hook positions, so outward movement at an edge can be zero; macOS uses Quartz deltas. Linux requires a matching keysharp-inputd 1.1 daemon and mouse-hook permission (without either, no moves are reported): relative values are raw device-specific evdev counts, while physical absolute values are changes in normalized [0,65535] axes. Linux A_EventInfo includes DeviceId (0 synthetic, positive physical) and IsAbsolute; synthetic absolute deltas are zero because no relative sample exists. Windows and macOS instead provide global-screen X/Y when available and omit DeviceId; X/Y need not equal the prior position plus dx/dy, and macOS reports the frozen cursor position while movement is suppressed. |
| InputHook.Start() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Starts capturing input. |
| InputHook.Stop() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Stops capturing input. |
| InputHook.Timeout | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Maximum capture duration in seconds. |
| InputHook.VisibleNonText | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether visible non-text keys are collected. |
| InputHook.VisibleText | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether visible text characters are collected. |
| InputHook.Wait() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Waits until capture ends or times out. |
| InstallKeybdHook() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The InstallKeybdHook function installs or uninstalls the keyboard hook. |
| InstallMouseHook() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The InstallMouseHook function installs or uninstalls the mouse hook. |
| InStr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Searches for a string within another string, returning the 1-based index where it was found. Use negative numbers for searching in reverse order. |
| Int16 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Int32 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Int64 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Int8 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| Integer() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the integer portion of a number. |
| IntPtr | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| is | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Type check operator |
| is not | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Type check operator |
| IsAlnum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string is alphanumeric. |
| IsAlpha() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string contains only letters. |
| IsDigit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string contains only digits. |
| IsFloat() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a value is a floating-point number. |
| IsInteger() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a value is an integer. |
| IsLabel() | 🟡 Partial | 🟡 Partial | 🟡 Partial | 🟡 Partial | Does not support expressions. |
| IsLower() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string is lowercase. |
| IsNumber() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a value is numeric. |
| IsObject() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The IsObject function returns a non-zero number if the specified value is an object. |
| IsSet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The IsSet operator and IsSetRef function return a non-zero number if the specified variable has been assigned a value. |
| IsSetRef() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The IsSet operator and IsSetRef function return a non-zero number if the specified variable has been assigned a value. |
| IsSpace() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string contains only whitespace. |
| IsTime() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a value is a valid time/date string. |
| IsUpper() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string is uppercase. |
| IsXDigit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns true if a string contains only hexadecimal digits. |
| Join() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Joins arguments into a string using a separator. |
| Json | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Encodes script values as JSON and decodes JSON into script values. Indent pretty-prints, CaseSense sets the key comparison of every decoded Map, and a JSON null decodes to unset unless NullValue names a stand-in for it. A Boolean is written as true/false where the Integer 1 is written as 1. |
| Keyboard/Mouse send (synthetic input) | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Requires platform permissions on macOS. |
| KeyError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class for missing keys/items. |
| KeyHistory() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The KeyHistory function displays script info and a history of the most recent keystrokes and mouse clicks. |
| KeyWait() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The KeyWait function waits for a key or mouse/controller button to be released or pressed down. |
| ListHotkeys() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The ListHotkeys function displays the hotkeys in use by the current script, whether their subroutines are currently running, and whether they use a hook. |
| ListLines() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ListLines function enables or disables line logging or displays the script lines most recently executed. |
| ListVars() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ListVars function displays the script's variables: their names and current contents. |
| ListView.Add() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds a row to a ListView control. |
| ListView.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes one row or all rows in a ListView. |
| ListView.DeleteCol() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes a column from a ListView control. |
| ListView.GetCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets item, selected-item, or column count in a ListView. |
| ListView.GetNext() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the next row matching selection/focus criteria. |
| ListView.GetText() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets text from a ListView row and column. |
| ListView.Insert() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Inserts a row at a specific position in a ListView. |
| ListView.InsertCol() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Inserts a column into a ListView. |
| ListView.Modify() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes ListView row state, text, or icon. |
| ListView.ModifyCol() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes ListView column options and width. |
| ListView.SetImageList() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assigns an image list for ListView icons. |
| ListViewGetContent() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The ListViewGetContent function returns content data from a list-view control, such as rows, columns, or count values. |
| Ln() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the base e (natural) logarithm of a number. Throws an exception if a negative number is passed in. |
| LoadPicture() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Loads an image, icon or cursor. Differs in that instead of writing to a ref argument, it returns a structure whose fields are Handle and ImageType. |
| Lock | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | A mutual-exclusion lock for code shared between RealThreads: Acquire([Timeout]) and Release(). Covers what LockRun cannot - a timed acquire, and a lock held across several statements. Reentrant and owned by a real thread; Acquire blocks that whole real thread. |
| LockRun() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Runs code under a lock to prevent concurrent execution overlap. |
| Log() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the base 10 logarithm of a number. Throws an exception if a negative number is passed in. |
| Loop | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Loop statement. |
| Loop (files & folders) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Language/runtime capability. |
| Loop (normal) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Repeats a block a specified number of times or indefinitely. |
| Loop (read file contents) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Language/runtime capability. |
| Loop File | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Lists files and folders at a location, matching a specified pattern, optionally recursing. |
| Loop Files | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerates files/folders matching a pattern. |
| Loop Parse | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Parses the string either one character at a time, or broken into pieces based on the delimiter. Note this accepts strings as delimiters, unlike AHK which did not. |
| Loop Read | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Reads though a file one line at a time. Optionally supports an output file, which can then be used with FileAppend with no filename argument. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| Loop Reg | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Reads through registry keys and values, optionally recursive. Additionally supports HKEY_PERFORMANCE_DATA, and an accessor A_LoopRegValue to get the values. Supports data types except the following, which will return UNKNOWN: REG_LINK, REG_RESOURCE_LIST, REG_FULL_RESOURCE_DESCRIPTOR, REG_RESOURCE_REQUIREMENTS_LIST, REG_DWORD_BIG_ENDIAN. |
| LTrim() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Trims characters from the end of a string. |
| Mail() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sends email via configured SMTP settings. |
| Map | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Map object type. |
| Map.__Enum() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerates key-value pairs. |
| Map.__Item | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Indexer property for getting or setting map values by key. |
| Map.__New() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Constructs a new Map object. |
| Map.Capacity | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets the internal capacity for map entries. |
| Map.CaseSense | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves or sets the map's case sensitivity setting. |
| Map.Clear() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Clears all key/value pairs in a map. |
| Map.Clone() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a shallow copy of all of the values and keys of the map. |
| Map.Count | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Number of key/value pairs in the map. |
| Map.Default | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default value returned for missing keys. |
| Map.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes a key/value pair out of a map if the key exists, else throws an exception. |
| Map.Get() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets a value by key with optional fallback default. |
| Map.Has() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether a dictionary contains a value, even an empty one, for the given key. |
| Map.MaxIndex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the largest integer key contained in the map. Returns empty string if no keys were integers. |
| Map.MinIndex() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the smallest integer key contained in the map. Returns empty string if no keys were integers. |
| Map.Set() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets zero or more items. |
| Max() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the larger of two numbers. If either is not numeric, the empty string is returned. The largest value of an array is computed if one is passed in. |
| MemberError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| MemoryError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| Menu() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Menu/MenuBar object provides an interface to create and modify a menu or menu bar, add and modify menu items, and retrieve information about the menu or menu bar. |
| Menu.Add() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Adds an item to a menu. The Right and RTL item options are Win32 menu attributes with no Eto counterpart, so they are parsed and ignored on Linux and macOS; every other option, including the Break/BarBreak column controls, works on all platforms. |
| Menu.AddStandard() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds standard tray menu items. |
| Menu.Check() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Checks a menu item. |
| Menu.ClickCount | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Number of clicks required to trigger a tray menu item. |
| Menu.Default | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Default menu item name or position. |
| Menu.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes one menu item or all items. |
| Menu.Disable() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Disables a menu item. |
| Menu.Enable() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enables a menu item. |
| Menu.Handle | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Native menu handle. |
| Menu.Insert() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Inserts an item into a menu. Shares its option parsing with Menu.Add, so the Right and RTL options are likewise ignored on Linux and macOS. |
| Menu.MenuItemCount | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns number of items in a menu. |
| Menu.MenuItemName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns item text/name for a menu entry. |
| Menu.Rename() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Renames a menu item. |
| Menu.SetColor() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets menu background color. |
| Menu.SetIcon() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets icon for a menu item. |
| Menu.Show() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Shows the menu at a screen position. |
| Menu.ToggleCheck() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles checked state of a menu item. |
| Menu.ToggleEnable() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles enabled state of a menu item. |
| Menu.ToggleItemVis() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Toggles visibility of a menu item. |
| Menu.Uncheck() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Unchecks a menu item. |
| MenuBar() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Menu/MenuBar object provides an interface to create and modify a menu or menu bar, add and modify menu items, and retrieve information about the menu or menu bar. |
| MenuFromHandle() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The MenuFromHandle function retrieves the Menu or MenuBar object corresponding to a Win32 menu handle. |
| MenuSelect() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The MenuSelect function invokes a menu item from the menu bar of the specified window. Like the Control functions, on Linux and macOS it can only reach menus of windows created by this script, not those of other applications. |
| MethodError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| Min() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the smaller of two numbers. If either is not numeric, the empty string is returned. The smaller value of an array is computed if one is passed in. |
| Mod() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the remainder when the first number is divided by the second number. Throws an exception if the second number is 0. |
| Modifiers on either term of a custom combination | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Keysharp extension: both terms of a custom combination may carry modifiers (`<^a & b::`, `a & <^b::`). Those on the prefix are tested against the modifiers held when the prefix key went down and kept until it is released, so a key whose firmware asserts modifiers and drops them immediately still works as a prefix; those on the suffix are tested against the live state as the suffix is pressed. A combination asking for neither continues to ignore the modifier state. This covers keyboards whose Copilot key emits LWin+LShift+F23 and whose Office key emits LCtrl+LShift+LAlt+LWin. |
| Monitor | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Keysharp-specific Ks class for one display: identity and metadata beyond the AHK MonitorGet* functions, plus brightness and DDC/CI control. A Monitor is a snapshot of the topology (call Refresh() to re-read) plus a live handle to the device. On macOS the geometry, identity and change events are verified, but Model and Adapter are always empty (CoreGraphics exposes no product-name or adapter API) and Connection only distinguishes the built-in panel. |
| Monitor.All | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Every monitor in index order, built from one topology enumeration. Preferred over a loop of Monitor(i), which re-enumerates per monitor and can be shifted by a hotplug midway through. |
| Monitor.Brightness | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Get or set the monitor's brightness as a percentage (0-100). Each access is a real device transaction (tens of milliseconds over DDC/CI) and is deliberately not cached, because the monitor's own buttons can change the value too. Windows uses WMI for the built-in panel and DDC/CI for external monitors. Linux uses the kernel backlight class for the built-in panel (a direct sysfs write where permitted, else logind's SetBrightness) and DDC/CI over /dev/i2c-* for external monitors, which needs i2c-dev loaded and access to the bus (a root install adds a udev rule granting the local-seat user access to display-controller i2c buses only; ddcutil's own rule works too) - hence partial. macOS uses the private DisplayServices framework for the built-in panel and Apple's own displays, and DDC/CI for every other external monitor: on Apple Silicon over the private IOAVService (the only option, since IOFramebuffer does not exist under the DCP architecture), on Intel over the public IOKit I2C API - the Intel path is implemented but untested. Throws an OSError naming the reason where unsupported; test HasBrightness to branch without an exception. |
| Monitor.Connection / .IsInternal / .Adapter | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | How the monitor is attached (HDMI, DisplayPort, eDP, DVI, VGA, Internal, or blank), whether it is a built-in panel, and the adapter driving it. Windows reports the DisplayConfig output technology and the adapter's marketing name; Linux derives the kind from the DRM connector name and reports the DRM driver as the adapter. macOS distinguishes only built-in from external and exposes no adapter name. |
| Monitor.Count | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The number of monitors; the same value as MonitorGetCount(). |
| Monitor.FromId() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The monitor whose Id matches, or blank when that monitor is not attached - the lookup that makes a persisted Id useful. Unlike the other factories this must resolve metadata for every attached display, since an id is not part of the topology snapshot. |
| Monitor.FromMouse() | 🟢 Full | 🟢 Full | 🟡 Partial | ⚪ Unknown | The monitor the cursor is on. On Wayland the cursor position is only available through a compositor backend, matching MouseGetPos. |
| Monitor.FromPoint() | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The monitor containing a native screen point, or the nearest monitor when the point lies in a gap. Replaces the removed Ks.MonitorFromPoint() function, which returned an index instead of a Monitor. |
| Monitor.FromWindow() | 🟢 Full | 🟢 Full | 🟡 Partial | ⚪ Unknown | The monitor a window overlaps most. Inherits the platform's foreign-window geometry support, so on Wayland it needs a compositor backend (KWin/GNOME/Cinnamon/COSMIC) to locate the window at all. |
| Monitor.GetVCP() / .SetVCP() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Read or write a raw DDC/CI VCP feature (MCCS): 0x10 brightness, 0x12 contrast, 0x60 input source, 0x62 speaker volume, 0xD6 power mode. GetVCP returns {current, max}. SetVCP drives the monitor's firmware directly - an input-source or power code will switch the monitor away from this computer, and some displays mishandle codes they document, so verify a code against the monitor's MCCS documentation first. SetVCP reads the feature back before writing it, on the transport it already has open, so a code the monitor does not implement raises an OSError instead of reporting a success the display silently ignored - a DDC/CI write is unacknowledged, so that read is the only evidence the code exists. Windows uses dxva2; Linux talks to /dev/i2c-* directly, which needs i2c-dev loaded and bus access (granted by the udev rule a root install adds). macOS reaches external monitors over DDC/CI - the private IOAVService on Apple Silicon, the public IOKit I2C API on Intel (implemented but untested) - and cannot reach the built-in panel at all, which has no DDC/CI connection. |
| Monitor.HasBrightness | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Whether Brightness works for this monitor. This is a real probe of the device rather than a platform guess, so it costs one brightness read. |
| Monitor.Model / .Manufacturer / .Serial / .Id | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Panel identity from EDID. Id survives reboots and re-plugging and is the value to persist (for example to restore a window layout per monitor set); panels that report no serial are disambiguated by connector, making Id stable per port rather than per panel. Windows reads the EDID Windows cached under the monitor's registry key; Linux reads /sys/class/drm/*/edid under both X11 and Wayland. macOS answers from CoreGraphics vendor/model/serial numbers and has no product-name API, so Model is always blank there. Every field is blank when the display reports nothing. |
| Monitor.OnChange() | 🟢 Full | ⚪ Unknown | ⚪ Unknown | ⚪ Unknown | Calls Callback(hook, kind) whenever the display configuration changes, returning a MonitorHook whose Stop() cancels it (Count limits the number of fires, default -1 = unlimited). kind is 'topology' when the set of attached monitors changed (plug/unplug, dock/undock) and 'settings' when the same monitors are attached but their resolution, position, scale or primary assignment changed; A_EventInfo holds the monitor count after the change. The classification is done by diffing topology snapshots rather than by decoding native change flags, so it is identical on every platform and duplicate notifications that change nothing are dropped. Windows listens to SystemEvents.DisplaySettingsChanged, Linux to GDK's monitor signals (which cover X11 and Wayland alike), macOS to CGDisplayRegisterReconfigurationCallback. The Linux and macOS backends are written but have never been run against a real display change - hence unknown. |
| Monitor.Orientation | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Clockwise rotation of the desktop content in degrees: 0, 90, 180 or 270. |
| Monitor.PhysicalWidth / .PhysicalHeight / .Dpi | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The panel's physical size in millimetres, and the resulting dots per inch. Dpi is expressed in the same units as W and H, so it is a physical pixel density on Windows and X11 but a logical one on Wayland and macOS. Blank when the display reports no physical size. |
| Monitor.Primary | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | The primary monitor. |
| Monitor.Refresh() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Re-reads this monitor's topology and metadata in place and returns the same object. Matches by name first so the monitor is still tracked after the display order changes, falling back to the index. Returns blank (falsy) instead, leaving the object's last-known values untouched, when the monitor is no longer attached - the state a Monitor.OnChange 'topology' handler is in after the display it was holding is unplugged, so it reports the loss the same way FromId does rather than throwing. |
| Monitor.RefreshRate | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Vertical refresh in Hz as a float (59.94, not 59), or blank when unknown. Windows uses the DisplayConfig rational rate, X11 computes it from the RandR mode's pixel clock, Wayland uses wl_output's mode refresh. macOS returns 0 for some built-in panels, which is reported as blank. |
| Monitor.Scale | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The monitor's authored-size scale; 1.0 is 100%. It scales dimensions, never absolute positions. Replaces the removed Ks.MonitorGetScale() function. Wayland and macOS always report 1.0 because their native coordinate spaces already account for UI scaling, so the value only varies on Windows and X11. |
| Monitor.X / .Y / .Width / .Height / .Bounds / .WorkArea | 🟢 Full | 🟢 Full | 🟢 Full | ⚪ Unknown | Geometry in native screen coordinates, the same space MonitorGet reports. Bounds and WorkArea are objects with x, y, w and h. |
| MonitorGet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the selected monitor index and writes its native screen-coordinate bounds to the supplied output variables. An omitted index or 0 selects the primary monitor; any other out-of-range index raises a ValueError, matching AutoHotkey v2, rather than silently substituting the primary. Image.FromMonitor() resolves the monitor the same way and inherits that validation. |
| MonitorGetCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the total number of monitors. |
| MonitorGetName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the operating system's name of the specified monitor. An out-of-range monitor index raises a ValueError, matching AutoHotkey v2. |
| MonitorGetPrimary() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the number of the primary monitor. |
| MonitorGetWorkArea() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the selected monitor index and writes its native screen-coordinate work area to the supplied output variables. An out-of-range monitor index raises a ValueError, matching AutoHotkey v2. |
| MouseClick() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The MouseClick function clicks or holds down a mouse button, or turns the mouse wheel. |
| MouseClickDrag() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The MouseClickDrag function clicks and holds the specified mouse button, moves the mouse to the destination coordinates, then releases the button. |
| MouseGetPos() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The MouseGetPos function retrieves the current position of the mouse cursor, and optionally which window and control it is hovering over. |
| MouseMove() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The MouseMove function moves the mouse cursor. |
| MsgBox() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Displays the specified text in a small window containing one or more buttons. Option values 6, 768, 4096, 8192, 262144 and 16384 are not supported on any platform (there is no help button), and when a timeout or an owner window is used the text is right justified. On Linux and macOS the toolkit offers only the OK, OK/Cancel, Yes/No and Yes/No/Cancel button sets, so button options 2 (Abort/Retry/Ignore), 5 (Retry/Cancel) and 6 (Cancel/Try Again/Continue) are unavailable there and fall back to OK. The four icon options behave the same everywhere. |
| Named parameters | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp extension. Binds an argument to the callee's parameter of that name instead of by position; must trail the positional arguments. Works for script functions, built-ins, methods, constructors (built-in classes included: Buffer(ByteCount: 16), Error(Message: msg)), bound functions, COM objects (via IDispatch DISPIDs) and Clr calls (where they also drive overload selection). Not available in '[]' (that is the map literal), and not for the receiver. A call's named arguments travel as one first-class value (Ks.NamedArgs, a case-insensitive Map whose keys are the names; the last argument slot is reserved for it): any variadic parameter collects the ones it does not declare as an ordinary trailing element and a variadic call re-emits it, so a wrapper forwards names without knowing they exist, and NamedArgs(name, value) — imported from the Ks module — makes a named call dynamically; a variadic that consumes its tail as values (Format, Push, Max) receives it as data. See #Warn NamedArg. |
| NormalizeEol() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Normalizes all line endings to a requested or platform-default sequence. |
| not | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical NOT operator. |
| Number() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Number function converts a numeric string to a pure integer or floating-point number. |
| NumGet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The NumGet function returns the binary number stored at the specified address+offset. A type which does not name a number raises a ValueError, and the name is matched in full where AutoHotkey reads only its first character. |
| NumPut() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The NumPut function stores one or more numbers in binary format at the specified address+offset. A type or value which does not name a number raises a ValueError. |
| ObjAddRef() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ObjAddRef and ObjRelease functions increment or decrement an object's reference count. |
| ObjBindMethod() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ObjBindMethod function creates a BoundFunc object which calls a method of a given object. |
| Object() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates a new Object; optional key/value arguments initialize own properties. |
| Object.__Ref() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns a property-reference (PropRef) object. |
| Object.OwnPropCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns number of own properties defined directly on the object. |
| ObjFree() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Releases object references associated with a pointer/COM wrapper context. |
| ObjFromPtr() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Creates or retrieves an object wrapper from a raw pointer. |
| ObjFromPtrAddRef() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Creates/retrieves an object wrapper from a pointer and increments its reference count. |
| ObjGetBase | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the value's base object. Differs in that it only returns the name of the base type as a string. |
| ObjGetCapacity() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the current capacity of the object's internal own-property storage. |
| ObjGetDataPtr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the address of the object's structured data (typed properties). |
| ObjGetDataSize() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the size of the object's structure (typed properties), in bytes. |
| ObjHasOwnProp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether the object defines the specified own property name. |
| ObjHasProp() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Checks for a property without invoking an overridden HasProp method. |
| ObjOwnPropCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the number of own properties currently defined on the object. |
| ObjOwnProps | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Enumerates an object's own properties. |
| ObjPtr() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Returns the raw pointer address of an object. |
| ObjPtrAddRef() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Returns object pointer address and increments its reference count. |
| ObjRelease() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The ObjAddRef and ObjRelease functions increment or decrement an object's reference count. |
| ObjSetBase() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets an object's base object. The native type cannot change and the base chain may not become circular. Neither object may have typed properties, since those fix the memory layout; a prototype which has none is writable. |
| ObjSetCapacity() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Unlike AHK doesn't set the exact capacity, but ensures the internal own props objects can hold the requested number of props. |
| ObjSetDataPtr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets the address of the object's structured data. (Slated for removal in AHK; prefer Struct.At.) |
| OnClipboardChange() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Registers callbacks for native platform clipboard content changes. |
| OnError() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The OnError function registers a function to be called automatically whenever an unhandled error occurs. |
| OnExit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The OnExit function registers a function to be called automatically whenever the script exits. |
| OnMessage() | 🟢 Full | 🟡 Partial | 🟡 Partial | ⚪ Unknown | The OnMessage function registers a function to be called automatically whenever the script receives the specified message. Off Windows there is no native message queue, so the input messages are synthesized from the toolkit events raised by the script's own GUI: WM_MOUSEMOVE, the L/R/M button down, up and double-click messages, WM_MOUSEWHEEL, WM_KEYDOWN/UP, WM_SYSKEYDOWN/UP and WM_CHAR. As on Windows, the callback receives the CONTROL's handle and its thread's last-found window is the GUI the control belongs to. Anything outside that set is never delivered, and messages to windows the script does not own cannot be seen at all. Clicks on a single-line Edit are also missed, because GTK's entry consumes the button press before the toolkit raises its own event, and a press on a window's own background is delivered twice by the native event dispatch and de-duplicated here. Verified on X11 and Wayland; the macOS path shares the code but is untested. |
| or | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Logical OR operator. |
| Ord() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns the numeric two byte unicode value for the first character in a string. This differs from V2 in that it also takes an optional second parameter which specified the 1-based index in the string to return the numeric value for, rather than only doing it for the first character. |
| OSError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| OutputDebug() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The OutputDebug function sends a string to the debugger (if any) for display. Ks.OutputDebugLine additionally takes a flag to clear the debug output. |
| OutputDebugLine() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Writes a debug line with newline terminator. |
| Overlay | 🟢 Full | 🟢 Full | 🟡 Partial | ⚪ Unknown | Click-through, always-on-top overlay used by Highlight and ToolTip. Draw through Canvas and publish with Present; SetImage copies an image and Redraw builds a replacement canvas. Pointer events require ClickThrough := false; GNOME/Cinnamon use compositor-positioned client surfaces so animated frames stay on Wayland's native buffer path. Runtime-tested on Windows, covered by automated backing tests on Linux, and unverified on macOS. |
| Parser and runtime execution | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Script execution is provided by Keysharp.Core. Source parsing is an optional Roslyn-free component; lowering and C# compilation are supplied by the optional compiler component. |
| ParseScript() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Parses, lowers, and compilation-validates script text with the optional compiler component. Returns an empty string on success or formatted errors on failure. |
| Pause() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Pause function pauses the script's current thread or sets the pause state of the underlying thread. |
| Persistent() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Persistent function prevents the script from exiting automatically when its last thread completes, allowing it to stay running in an idle state. |
| PixelGetColor() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Returns the pixel value at the specified coordinate as a hexadecimal string like 0x010203. Differs because the mode parameter is not supported because it is not needed. |
| PixelSearch() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Searches a region of the screen for a pixel of the specified color. Differs in that instead of writing to ref arguments, it returns a structure whose fields are what the original input parameter names would have been. |
| PostMessage() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The PostMessage function places a message in the message queue of a window or control. |
| ProcessClose() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Forces the first matching process to close. |
| ProcessExist() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Checks if the specified process exists. |
| ProcessGetName() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ProcessGetName and ProcessGetPath functions return the name or path of the specified process. |
| ProcessGetParent() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ProcessGetParent function returns the process ID (PID) of the process which created the specified process. |
| ProcessGetPath() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The ProcessGetName and ProcessGetPath functions return the name or path of the specified process. |
| ProcessInfo | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Represents process handle metadata and redirected I/O streams. |
| ProcessInfo.ExitCode | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets process exit code after termination. |
| ProcessInfo.ExitTime | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets process exit time. |
| ProcessInfo.HasExited | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether the process has exited. |
| ProcessInfo.Kill() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Terminates the process. |
| ProcessInfo.StdErr | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets redirected standard-error stream. |
| ProcessInfo.StdIn | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets redirected standard-input stream. |
| ProcessInfo.StdOut | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets redirected standard-output stream. |
| ProcessSetPriority() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes the priority level of the first matching process. |
| ProcessWait() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Waits for the specified process to exist. |
| ProcessWaitClose() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Waits for all matching processes to close. |
| PropertyError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| PropRef | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Property-reference object type. |
| Props | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Helper for creating property definitions. |
| Random() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes a random number in the range of x to y. |
| RandomSeed() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets seed for the pseudo-random generator used by Random(). |
| RealThread | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Represents a real background thread handle/promise. |
| RealThread.Call() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Invokes work on the underlying real thread context. |
| RealThread.ContinueWith() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Schedules continuation after thread task completion. |
| RealThread.Wait() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Waits for real thread completion. |
| RegCreateKey() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The RegCreateKey function creates a registry key without writing a value. |
| RegDelete | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Deletes a value from the registry. |
| RegDeleteKey() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Deletes a key from the registry. |
| RegExMatch() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Searches a string for a regular expression match. When there is no match the output variable is left unset (blank in v2.0 compatibility mode). |
| RegExMatchCs() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Runs .NET/C# regex match and returns match details. |
| RegExMatchInfo | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Match object returned by RegExMatch. |
| RegExMatchInfoCs | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Match object returned by the Keysharp case-sensitive .NET regular-expression helper. |
| RegExReplace() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Replaces text matching a regular expression pattern. |
| RegExReplaceCs() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Runs .NET/C# regex replace. |
| Registry APIs | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Windows Registry APIs are Windows-only. |
| RegRead() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Reads a value from the registry. Supports REG_QWORD in addition to the other types. |
| RegWrite | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Writes a value to the registry. Supports REG_QWORD in addition to the other types. |
| Reload() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Reload function replaces the currently running instance of the script with a new one. |
| RequestCapabilities() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Requests or queries platform permissions and returns the status of each capability. |
| Return | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns from a function/subroutine, optionally with a value. |
| Round() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes a number rounded to either the nearest integer, a specified number of decimal places, or a specified number of digits. |
| RTrim() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Trims characters from the beginning of a string. |
| Run() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Run and RunWait functions run an external program. RunWait will wait until the program finishes before continuing. |
| RunAs() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Specifies a set of user credentials to use for all subsequent uses of Run and RunWait. |
| RunScript() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Executes script source text/file in a script engine context. Requires the optional compiler component at runtime; compiled artifacts detect this call and include the component unless it is explicitly excluded. |
| RunWait() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Run and RunWait functions run an external program. RunWait will wait until the program finishes before continuing. |
| Screen capture and pixel/image functions | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Pixel/image search and screen capture depend on platform-specific backends. |
| Script-owned window management | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Creating and driving the script's own GUI windows. Built on WinForms (Windows) and Eto (Linux/macOS); the object model, events, controls, menus, ListView and TreeView all behave the same. Remaining differences: the ActiveX and Custom control types are Win32-only, ListView supports only the Report view off Windows, raw Win32 style options are ignored, per-monitor DPI re-layout is Windows-only, and a client cannot position its own window on Wayland without a compositor backend. |
| Send() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sends simulated keystrokes. |
| SendEvent() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sends keystrokes via Event mode. |
| SendInput() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sends keystrokes via Input mode. |
| SendLevel() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets the send level for generated input. |
| SendMessage() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The SendMessage function sends a message to a window or control and waits for acknowledgement. |
| SendMode() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sets the default send mode. "Play" has no journal-playback equivalent on Linux or macOS and is downgraded to "Event" with a one-time warning to stderr; Input and Event behave as documented. |
| SendPlay() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sends keystrokes via Play mode. Play mode depends on the Win32 journal-playback hook, which has no equivalent elsewhere: Linux and macOS silently downgrade to Event mode and print a one-time warning to stderr. |
| SendText() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Sends text without translating key names. |
| SetCapsLockState() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetCapsLockState, SetNumLockState and SetScrollLockState functions set the state of the corresponding key. Can also force the key to stay on or off. |
| SetControlDelay() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetControlDelay function sets the delay that will occur after each control-modifying function. |
| SetDefaultMouseSpeed() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The SetDefaultMouseSpeed function sets the mouse speed that will be used if unspecified in Click, MouseMove, MouseClick and MouseClickDrag. |
| SetKeyDelay() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The SetKeyDelay function sets the delay that will occur after each keystroke sent by the Send or ControlSend functions. |
| SetMouseDelay() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | The SetMouseDelay function sets the delay that will occur after each mouse movement or click. |
| SetNumLockState() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetCapsLockState, SetNumLockState and SetScrollLockState functions set the state of the corresponding key. Can also force the key to stay on or off. |
| SetRegView() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Sets the registry view used by registry functions, allowing them in a 64-bit script to access the 32-bit registry view. |
| SetScrollLockState() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetCapsLockState, SetNumLockState and SetScrollLockState functions set the state of the corresponding key. Can also force the key to stay on or off. |
| SetStoreCapsLockMode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetStoreCapsLockMode function determines whether to restore the state of the CapsLock key after a Send function. |
| SetTimer() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetTimer function causes a function to be called automatically and repeatedly at a specified time interval. |
| SetTitleMatchMode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetTitleMatchMode function sets the matching behavior of the WinTitle parameter in built-in functions such as WinWait. |
| SetWinDelay() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SetWinDelay function sets the delay that will occur after each windowing function, such as WinActivate. |
| SetWorkingDir() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes the script's current working directory. |
| ShowDebug() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Shows or toggles debug UI/log output. |
| Shutdown() | 🟢 Full | 🟢 Full | 🟢 Full | 🟡 Partial | Shuts down, restarts, or logs off the system. macOS uses System Events; its normal actions are implemented, but AHK's force flag has no direct equivalent and is ignored with a diagnostic. |
| Sin() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the hyperbolic sine of a number. |
| Sinh() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the hyperbolic sine of a number. |
| Sleep() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Sleep function waits the specified amount of time before continuing. |
| Sort() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Arranges a variable's contents in alphabetical, numerical, or random order (optionally removing duplicates). The back slash option also supports specifying a forward slash / so it can be used for paths on non-Windows systems. |
| Sound APIs | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Audio device/endpoint support differs by platform. |
| SoundBeep() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Emits a tone at the requested frequency and duration on every platform. Windows uses the Win32 Beep API. Linux and macOS synthesize the sine wave to an in-memory WAV and play it through the same backend as SoundPlay, so Frequency and Duration are honoured there too; Linux needs one of the players listed under SoundPlay to be installed. |
| SoundGetInterface() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Retrieves a native COM interface of a sound device or component and is Windows-specific. |
| SoundGetMute() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟢 Full | Retrieves a mute setting of a sound device. Differs in that there is no support for components, so the function only takes one parameter: the 1-based index, or name for the device. Platform statuses inherited from curated 'Sound APIs'; per-function validation pending. |
| SoundGetName() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟢 Full | Retrieves the name of a sound device. Differs in that there is no support for components, so the function only takes one parameter: the 1-based index, or name for the device. Platform statuses inherited from curated 'Sound APIs'; per-function validation pending. |
| SoundGetVolume() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟢 Full | Retrieves a mute setting of a sound device. Differs in that there is no support for components, so the function only takes one parameter: the 1-based index, or name for the device. Platform statuses inherited from curated 'Sound APIs'; per-function validation pending. |
| SoundPlay() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Plays a sound file. The "*n" standard-sound syntax (*-1, *16, *32, *48, *64) works on every platform: Windows passes the number to MessageBeep, Linux uses the freedesktop sound theme and macOS /System/Library/Sounds, the latter two falling back to a synthesized beep when the desktop ships no such sound. Windows plays via MCI, so any format with an installed codec works (.wav, .mp3, .avi, ...); MCI limits paths to roughly 127 characters. macOS uses afplay (wav/aiff/caf/mp3/m4a). Linux picks the best installed player for the file type - paplay or aplay for uncompressed/libsndfile formats, otherwise ffplay, mpv, gst-play-1.0 or mpg123 - and raises an error naming them if none is present. On every platform, starting a new file stops the previous one, SoundPlay on a nonexistent file stops playback and raises, and playback stops when the script exits. |
| SoundSetMute() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟢 Full | Changes a mute setting of a sound device. Differs in that there is no support for components, so the function only takes one parameter: the 1-based index, or name for the device. Platform statuses inherited from curated 'Sound APIs'; per-function validation pending. |
| SoundSetVolume() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟢 Full | Changes a volume setting of a sound device. Differs in that there is no support for components, so the function only takes one parameter: the 1-based index, or name for the device. Platform statuses inherited from curated 'Sound APIs'; per-function validation pending. |
| SplitPath() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Separates a file name or URL into its name, directory, extension, and drive. Differs in that instead of writing to ref arguments, it returns a structure whose fields are what the original input parameter names would have been. |
| Sqrt() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the square root of a number. Throws an exception if the argument is negative. |
| StatusBarGetText() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Retrieves text from a native Win32 status bar control. No non-Windows status-bar accessibility backend is implemented. |
| StatusBarWait() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Waits for native Win32 status-bar text and depends on StatusBarGetText; no non-Windows status-bar accessibility backend is implemented. |
| StrCompare() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Compares two strings alphabetically. Note this supports local, human readable comparison as well. |
| StrGet() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Copies a string from a memory address or buffer, optionally converting it from a given code page. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| String() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Converts a value to a string. For an object, the result is whatever its ToString() returned, so a ToString() which returns no value makes String() return no value too rather than raising. |
| String.EndsWith() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether a string ends with the specified suffix. The CaseSense parameter matches InStr; comparisons are culture-invariant unless the Locale option is given. |
| String.StartsWith() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns whether a string starts with the specified prefix. The CaseSense parameter matches InStr; comparisons are culture-invariant unless the Locale option is given. |
| StringBuffer() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Creates a mutable string buffer object. |
| StrLen() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves the count of how many characters are in a string. |
| StrLower() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Converts a string to lowercase. |
| StrPtr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The StrPtr function returns the current memory address of a string. |
| StrPut() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Writes string data to a buffer/address using specified encoding. An encoding name which cannot be resolved raises a ValueError rather than falling back to another encoding. |
| StrReplace() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Replaces occurrences of a substring and returns the updated string. |
| StrSplit() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves one or more characters from the specified position in a string. |
| StrTitle() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The StrLower, StrUpper and StrTitle functions convert a string to lowercase, uppercase or title case. |
| Struct.__Ref() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves a nested struct or virtual property reference. |
| struct.__Value | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Backing value property for struct instances. |
| Struct.Array | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Base class for fixed-length, fixed-element-type structured arrays. |
| Struct.Ptr | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Pointer class/property used for pointer-to-struct fields, native parameters and struct addresses. |
| structures | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | User-defined structures via the struct keyword: typed fields, nesting, At, numeric types, pointer classes, #StructPack alignment, and structured arrays (Int32[10] / Struct.Array). |
| StrUpper() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Converts a string to uppercase. |
| SubStr() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Retrieves one or more characters from the specified position in a string. |
| super | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keyword that accesses base-class methods and properties. |
| Suspend() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Suspend function disables or enables all or selected hotkeys and hotstrings. |
| Switch | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Selects one case branch based on a value/expression. |
| SysGet() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Gets system information. Non-Windows builds implement monitor dimensions/count, mouse presence/buttons, network state and selected session metrics; Win32-only system metrics have no portable equivalent and are not implemented. |
| SysGetIPAddresses() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The SysGetIPAddresses function returns an array of the system's IPv4 addresses. |
| TabControl.SetTabIcon() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Sets icon for a tab page in tab controls. |
| Tan() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the tangent of a number. |
| Tanh() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Computes the hyperbolic tangent of a number. |
| TargetError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| Task | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Work that finishes later; every CLR call returning a .NET Task hands one back. Result is a snapshot that never waits, Await(task) and Wait([Timeout]) are the waiting forms, and Then(fn) reacts without blocking. |
| Taskbar | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Draws a badge and a progress bar on one window's taskbar button (SetBadge, SetProgress, SetProgressState). Called on the class it decorates the application's own button and every window opened afterwards, which is what Linux and macOS do in any case; constructed with a window handle it decorates that one button, a distinction only Windows makes. HasBadgeIcon/IsPerWindow report what the platform can draw. Windows uses ITaskbarList3, per window. Linux uses the Unity LauncherEntry protocol, which carries a number rather than an icon, covers the whole application, reaches only docks implementing it, and decorates the entry named by DESKTOP_ENTRY; macOS badges the dock tile with text and draws progress on the tile, also application-wide. Linux and macOS are compile-checked only. |
| TaskSource | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The settling half of a Task the script owns: hand .Task to whoever waits, then Resolve([Value]) or Reject([Reason]) when the answer arrives. This is how a hotkey or GUI event takes part in Task.WhenAny. |
| Thread | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Thread settings and controls. |
| Thread (object) | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Thread is a class rather than a function: calling it runs the AHK sub-functions (NoTimers/Priority/Interrupt) unchanged, and an instance describes one pseudo-thread: Id, Index (1-based), IsActive, Kind, Elapsed, Priority, Critical, Paused, IsInterruptible, Underlying and Exit. An Is prefix marks a read-only predicate; a settable mode is named for the mode. Obtained from A_Thread, Under or RealThread.Threads, never constructed. There is one object per pseudo-thread, so `thr == A_Thread` tests whether it is the running one. It revalidates its identity on every access, so one held past its pseudo-thread's life reports IsActive false rather than describing whichever pseudo-thread reused the pooled slot. Reads work from any real thread; setters and Exit require the owning one. |
| Throw | 🟡 Partial | 🟡 Partial | 🟡 Partial | 🟡 Partial | Rethrowing with throw is only allowed directly within the scope of catch, not from an arbitrary point (eg from functions). |
| TimeoutError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| ToolTip() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | Creates an always-on-top window anywhere on the screen. On Linux and macOS it is drawn with the cross-platform Overlay primitive rather than a native tooltip, which on Wayland needs a compositor the Overlay supports (KWin, GNOME, or Cinnamon with the Keysharp extension). |
| Tray icon and menu | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Tray icon, its menu and TrayTip notifications. On Linux the tray depends on the desktop providing a StatusNotifier/AppIndicator host - some environments need an extension before an icon appears at all - and notifications go through the desktop notification service. macOS uses a status item in the menu bar. |
| TraySetIcon() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Changes the script's tray icon. |
| TrayTip() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Creates a toast message window near the tray icon. Differs in that the Mute option is accepted but has no effect (there is no way to mute the system sound) and a large icon cannot be requested. The registry key EnableBalloonTips is not observed for disabling the notification. The option 4 has no effect because the tray icon is always shown at the top of the toast. On Linux and macOS the toast is an Eto Notification, so its appearance and duration are decided by the desktop notification service. |
| TreeView.Add() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Adds an item to a TreeView. |
| TreeView.Delete() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Deletes one item or all items in a TreeView. |
| TreeView.Get() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets state information for a TreeView item. |
| TreeView.GetChild() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the first child item of a TreeView node. |
| TreeView.GetCount() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the total item count in a TreeView. |
| TreeView.GetNext() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the next sibling item in a TreeView. |
| TreeView.GetNode() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Returns TreeView node object by node id/handle. |
| TreeView.GetParent() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the parent item of a TreeView node. |
| TreeView.GetPrev() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the previous sibling item in a TreeView. |
| TreeView.GetSelection() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets the currently selected TreeView item. |
| TreeView.GetText() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets text of a TreeView item. |
| TreeView.Modify() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Changes TreeView item text, icon, or state. |
| TreeView.SetImageList() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Assigns an image list for TreeView icons. |
| Trim() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Trims characters from the beginning and end of a string. |
| True | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Boolean true constant. |
| Try | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Starts exception-handling scope for statements that may throw. |
| Type | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The Type function returns the class name of a value. |
| TypeError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| UInt16 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| UInt32 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| UInt8 | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Predefined numeric struct type for typed fields and native calls. |
| UnsetError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| UnsetItemError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| Until | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Loop-until condition syntax for terminating a loop when condition becomes true. |
| ValueError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |
| VarSetStrCapacity() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Does nothing because the.NET runtime manages all memory. |
| VerCompare() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The VerCompare function compares two version strings. |
| While | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | While-loop statement. |
| WinActivate() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinActivate function activates the specified window. |
| WinActivateBottom() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinActivateBottom function is similar to the WinActivate function but it activates the bottommost matching window rather than the topmost. |
| WinActive() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinActive function checks if the specified window is active and returns its unique ID (HWND). |
| WinClose() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinClose function closes the specified window. |
| WinEvent | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Keysharp-specific Ks class for cross-platform window-event subscriptions. Windows uses SetWinEventHook; Linux uses X11 or native compositor sources; macOS uses Accessibility AXObserver streams and requires Accessibility permission. |
| WinEvent.Active() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to active/foreground window changes; also fires when the active window's title changes (so late-matching criteria are caught). Callback receives (hook, hwnd, dwmsEventTime). |
| WinEvent.CaretMove() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to text caret (insertion point) movement. hwnd is the caret owner's top-level window and A_EventInfo holds the caret rectangle { X, Y, Width, Height } in screen coordinates; unchanged positions are suppressed. Sourced from the same accessibility plumbing as CaretGetPos, so coverage matches it: Windows uses the MSAA caret (EVENT_OBJECT_LOCATIONCHANGE on OBJID_CARET), Linux the AT-SPI object:text-caret-moved signal, macOS the AXSelectedTextChanged notification. Applications that draw their own caret without exposing it to accessibility report nothing. |
| WinEvent.Count | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Remaining number of times the callback will fire (-1 = unlimited). |
| WinEvent.EventType | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | The event kind this subscription listens for, e.g. "Active" or "Move". |
| WinEvent.Exist() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to a matching window appearing (created, shown, or its title changed so it now matches); fires once per matching window and respects DetectHiddenWindows. Subsumes the reference library's Create. macOS uses per-application AXObserver streams and requires Accessibility permission. |
| WinEvent.IsActive | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Whether the subscription is still receiving events. |
| WinEvent.Minimize() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to window minimize/iconify. |
| WinEvent.Move() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to window move/resize. Every event is delivered as-is (not coalesced). |
| WinEvent.NotExist() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to a matching window disappearing (destroyed, hidden/cloaked, or its title changed so it no longer matches). Subsumes the reference library's Close. Tracks the set of matching top-level windows while they are alive (mirroring the reference library) so it fires reliably even though the window may be gone by the time the event arrives. |
| WinEvent.Pause() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Pauses (1), unpauses (0) or toggles (-1) a single hook (instance) or all hooks (static). A paused hook stays registered but does not fire. Returns the resulting paused state. |
| WinEvent.Paused | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Gets or sets whether a single hook (instance) or all hooks (static) are paused. |
| WinEvent.Restore() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to restoration from a minimized state. X11 derives it from _NET_WM_STATE_HIDDEN transitions, Wayland maps compositor restore events, and macOS uses AXWindowDeminiaturized. |
| WinEvent.Stop() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Cancels the subscription. Also runs on __Delete, but GC timing is unpredictable so call it (or let the owning thread tear down) explicitly. |
| WinEvent.TitleChange() | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Subscribes to window title changes. |
| WinExist() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinExist function checks if the specified window exists and returns the unique ID (HWND) of the first matching window. |
| WinFromPoint() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Returns window handle located at screen coordinates. |
| WinGetAlwaysOnTop() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetAlwaysOnTop function returns a non-zero value if the specified window is always-on-top. On macOS, only supported for windows owned by the calling process; always returns false for windows owned by other applications. |
| WinGetClass() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetClass function retrieves the specified window's class name. |
| WinGetClientPos() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetClientPos function retrieves the position and size of the specified window's client area. |
| WinGetControls() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Returns the ClassNN of the controls in a window. Off Windows only windows created by this script have real controls; for a foreign application X11 enumerates child X windows (which a client-side-drawn GTK/Qt app does not have), and Wayland and macOS report none. Use the bundled AtSpi (Linux) or Ax (macOS) library for cross-process control access. |
| WinGetControlsHwnd() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Returns the window handles of the controls in a window. Off Windows only windows created by this script have real controls; for a foreign application X11 enumerates child X windows (which a client-side-drawn GTK/Qt app does not have), and Wayland and macOS report none. Use the bundled AtSpi (Linux) or Ax (macOS) library for cross-process control access. |
| WinGetCount() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetCount function returns the number of existing windows that match the specified criteria. |
| WinGetEnabled() | 🟢 Full | 🟢 Full | 🟡 Partial | 🔴 Unsupported | Retrieves the enabled/disabled state of the specified window. On macOS this always reflects whether the window exists, not its true enabled state, since there is no API to query it. |
| WinGetExStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Retrieves the extended style of the specified window. Neither X11, Wayland nor macOS has an equivalent of Win32 extended window styles; all three always return 0. |
| WinGetID() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetID function returns the unique ID (HWND) of the specified window. |
| WinGetIDLast() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetIDLast function returns the unique ID (HWND) of the last/bottommost window if there is more than one match. |
| WinGetList() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetList function returns an array of unique IDs (HWNDs) for all existing windows that match the specified criteria. |
| WinGetMinMax() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetMinMax function returns a non-zero number if the specified window is maximized or minimized. |
| WinGetPID() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetPID function returns the Process ID (PID) of the specified window. |
| WinGetPos() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetPos function retrieves the position and size of the specified window. |
| WinGetProcessName() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetProcessName function returns the name of the process that owns the specified window. |
| WinGetProcessPath() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetProcessPath function returns the full path and name of the process that owns the specified window. |
| WinGetStyle() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Retrieves the style of the specified window. No non-Windows platform has a real Win32 style word, so for a script-owned window the toolkit state is projected onto the WS_* bits scripts actually test: WS_CAPTION/WS_POPUP from the border style, WS_SYSMENU, WS_THICKFRAME, WS_MINIMIZEBOX and WS_MAXIMIZEBOX from the frame buttons, plus WS_VISIBLE, WS_DISABLED, WS_MINIMIZE and WS_MAXIMIZE from the window state. Foreign windows expose no style word at all: X11 and macOS return 0, and Wayland reports only WS_CAPTION from the compositor decoration state. |
| WinGetText | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Retrieves the text from a window. On X11 the text is gathered by walking the X window tree and reading each child's WM_NAME, so a modern GTK or Qt application - whose widgets are drawn client-side and are not X windows - yields little or nothing. Wayland exposes no window text at all and returns empty; macOS reads it through the Accessibility API. |
| WinGetTitle() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinGetTitle function retrieves the title of the specified window. |
| WinGetTransColor() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Retrieves the transparency colour key of the specified window. No non-Windows backend implements a per-colour transparency key; X11, Wayland and macOS always return 0. |
| WinGetTransparent() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinGetTransparent function returns the degree of transparency of the specified window. |
| WinHide() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinHide function hides the specified window. On macOS, windows owned by the calling process are hidden individually; for windows owned by other applications, macOS provides no per-window hide API, so the entire owning application is hidden instead. |
| WinKill() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinKill function forces the specified window to close. On Wayland a real force-kill is provided by the Keysharp GNOME or Cinnamon shell extension (compositor-dependent), falling back to a graceful close request. |
| WinMaximize() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMaximize function enlarges the specified window to its maximum size. |
| WinMaximizeAll() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🟡 Partial | Maximizes all top-level windows. |
| WinMinimize() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMinimize function collapses the specified window into a button on the task bar. |
| WinMinimizeAll() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMinimizeAll and WinMinimizeAllUndo functions minimize or unminimize all windows. |
| WinMinimizeAllUndo() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMinimizeAll and WinMinimizeAllUndo functions minimize or unminimize all windows. |
| WinMove() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMove function changes the position and/or size of the specified window. |
| WinMoveBottom() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinMoveBottom function sends the specified window to the bottom of stack; that is, beneath all other windows. On Wayland this is provided by the Keysharp GNOME or Cinnamon shell extension (compositor-dependent). On macOS, only supported for windows owned by the calling process, via NSWindow.orderBack(); macOS provides no API to lower another process's window. |
| WinMoveTop() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinMoveTop function brings the specified window to the top of the stack without explicitly activating it. On Wayland this is provided natively on KWin (compositor scripting) or by the Keysharp GNOME or Cinnamon shell extension (compositor-dependent). On macOS this is done via the Accessibility AXRaise action, which raises the window within its owning application without activating that application. |
| WinRedraw() | 🟢 Full | 🟡 Partial | 🟡 Partial | 🔴 Unsupported | Redraws a window. Own windows are invalidated through the toolkit on every platform. For a foreign window X11 approximates it with XClearWindow, Wayland has no equivalent and raises an OSError, and macOS reports success without doing anything because AppKit owns repainting. |
| WinRestore() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinRestore function unminimizes or unmaximizes the specified window if it is minimized or maximized. |
| WinSetAlwaysOnTop() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinSetAlwaysOnTop function makes the specified window stay on top of all other windows (except other always-on-top windows). On macOS, only supported for windows owned by the calling process; macOS provides no API to change another process's window level, and raising it via Accessibility would require repeatedly stealing focus, so it is a no-op for windows owned by other applications. |
| WinSetEnabled() | 🟢 Full | 🟢 Full | 🟡 Partial | 🔴 Unsupported | Enables or disables the specified window. macOS has no faithful public equivalent of Win32 EnableWindow for another application, so the feature is intentionally unsupported there. |
| WinSetExStyle() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Changes the extended style of the specified window. Neither X11, Wayland nor macOS has an equivalent of Win32 extended window styles. On a script-owned window the call is accepted and does nothing; on a foreign window it raises an OSError. |
| WinSetRegion() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | The WinSetRegion function changes the shape of the specified window to be the specified rectangle, ellipse, or polygon. This is implemented purely via the Win32 GDI region APIs (HRGN/SetWindowRgn) and is Windows-only; non-rectangular window shaping has no equivalent implementation on Linux or macOS. |
| WinSetStyle() | 🟢 Full | 🔴 Unsupported | 🟡 Partial | 🔴 Unsupported | Changes the style of the specified window. X11 has no way to write Win32 window styles: the call is accepted and does nothing on a script-owned window and raises an OSError on a foreign window. Wayland maps only WS_CAPTION, to the compositor decoration state, and only for compositor-backed windows. macOS has no equivalent. |
| WinSetTitle() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinSetTitle function changes the title of the specified window. On macOS, windows owned by the calling process have their title set directly; for windows owned by other applications, setting AXTitle via the Accessibility API is attempted, but most applications treat it as read-only and the call has no effect. |
| WinSetTransColor() | 🟢 Full | 🔴 Unsupported | 🔴 Unsupported | 🔴 Unsupported | Makes all pixels of the chosen colour invisible. No non-Windows backend implements a per-colour transparency key; X11, Wayland and macOS raise an OSError. Whole-window opacity via WinSetTransparent is supported instead. |
| WinSetTransparent() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinSetTransparent function makes the specified window semi-transparent. On Wayland this is provided natively on KWin (compositor scripting) or by the Keysharp GNOME or Cinnamon shell extension (compositor-dependent). On macOS, only supported for windows owned by the calling process, via NSWindow.alphaValue; macOS provides no public API to change another process's window opacity. |
| WinShow() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟡 Partial | The WinShow function unhides the specified window. On macOS, windows owned by the calling process are restored individually; for windows owned by other applications, the entire owning application is unhidden (the inverse of WinHide's fallback). |
| WinWait() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinWait function waits until the specified window exists. |
| WinWaitActive() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinWaitActive and WinWaitNotActive functions wait until the specified window is active or not active. |
| WinWaitClose() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinWaitClose function waits until no matching windows can be found. |
| WinWaitNotActive() | 🟢 Full | 🟢 Full | 🟡 Partial | 🟢 Full | The WinWaitActive and WinWaitNotActive functions wait until the specified window is active or not active. |
| ZeroDivisionError | 🟢 Full | 🟢 Full | 🟢 Full | 🟢 Full | Built-in error class. |