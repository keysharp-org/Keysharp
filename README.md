# Keysharp #
* [Overview](#markdown-header-overview)
* [Differences](#markdown-header-differences)
	+ [Behaviors](#markdown-header-behaviors-functionality)
	+ [Syntax](#markdown-header-syntax)
	+ [Additions/Improvements](#markdown-header-additionsimprovements)
	+ [Removals](#markdown-header-removals)
* [Code acknowledgements](#markdown-header-code-acknowledgements)

## How do I get set up? ##
* If .NET 9 (or .NET 10 on Linux) is not installed on your machine, you need to download and run the x64 ".NET Desktop Runtime" installer from [here](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).

### Installing on Windows ###
* Download and run the Keysharp installer from the [Releases](https://github.com/mfeemster/keysharp/releases) page.
	+ The install path can be optionally added to the $PATH varible, so you can run it from the command line from anywhere.
		+ The path entry will be removed upon uninstall.
	+ It also registers Keysharp.exe as the default program to open `.ks` files. So after installing, double click any `.ks` file to run it.
	
### Portable run on Windows ###
* Download and unzip the zip file from the [Releases](https://github.com/mfeemster/keysharp/releases) page.
	+ CD to the unzipped folder.
	+ Run `.\Keysharp.exe yourfilename.ahk`

### Installing on Linux ###
* Download and extract the Keysharp installer tarball from the [Releases](https://github.com/mfeemster/keysharp/releases) page.
+ Run the install.sh script with sudo: `sudo sh ./install.sh` which does the following:
	+ Installs all necessary dependencies except .NET 10 runtime. You need to install that manually.
		+ Instructions for installing .NET on Linux can be found [here](https://learn.microsoft.com/en-us/dotnet/core/install/linux).
	+ Registers Keysharp as the default program to open `.ks` files. So after installing, double click any `.ks` file to run it.
	+ Creates a symlink at `/usr/local/bin/keysharp` so you can run it from the command line from anywhere.
	
### Building from source on Windows ###
* Download the latest version of [Visual Studio 2022](https://visualstudio.microsoft.com/vs/community/).
	+ This should install .NET 9. If it doesn't, you need to install it manually from the link above.
* Open Keysharp.sln
* Build all (building the installer is not necessary).
* CD to bin\release\net9.0-windows
* Run `.\Keysharp.exe yourtestfile.ahk`
	
## Overview ##

Keysharp is a fork and improvement of the abandoned IronAHK project, which itself was a C# re-write of the C++ AutoHotkey project.

The intent is for Keysharp to run on Windows, Linux and eventually Mac. For now, only Windows is supported and partial support for Linux.

This project is in the alpha testing stage and is not yet recommended for production systems.

Some general notes about Keysharp's implementation of the [AutoHotkey v2 specification](https://www.autohotkey.com/docs/v2/):

* The syntax is v2 style. While some remnants of V1 will work, it's unintentional and only v2 is supported.

* The operation of Keysharp is different than AHK. While AHK is an interpreted scripting language, Keysharp actually creates a compiled .NET executable and runs it.

* The process for reading and running a script is:
	+ Pass the script to Keysharp.exe which parses it and generates a Document Object Model (DOM) tree.
	+ The DOM compiler generates C# code for a single program.
	+ The C# program code is compiled into an in-memory executable.
	+ The executable is ran in memory as a new process.
	+ Optionally output the generated C# code to a .cs file for debugging purposes with the `-codeout` option.
	+ Optionally output the generated executable to an .exe file for running standalone in the future with the `-exeout` option.

* Keysharp supports files with the `.ahk` extension, however installing it will not register it with that extension. Instead, it will register the other extension it supports, `.ks`.

* In addition to `Keysharp.exe`, there is another executable that ships with the installer named `Keyview.exe`. This program can be used to see the C# code that is generated from the corresponding script code.
	+ It gives real-time feedback so you can see immediately when you have a syntax error.
	+ It is recommended that you use this to write code.
	+ The features are very primitive at the moment, and help improving it would be greatly appreciated.

Despite our best efforts to remain compatible with the AHK v2 spec, there are differences. Some of these differences are a reduction in functionality, and others are an increase. There are also slight syntax changes.

## Differences: ##

###	Behaviors/Functionality: ###
* Keysharp follows the .NET memory model.
	+ There is no variable caching with strings vs numbers. All variables are C# objects.
	+ Values not stored in variables are like regular variables, only eligible to be freed once they go out of scope.
```
	FileOpen("test.txt", "w").Write("hello") ; The temporary file object does not get deleted at the end of the line, only possibly at the end of the current scope.
```
	+ Object destructors/finalizers are called at a random point in time, and `Collect()` should be used if they need to be invoked predictably.
	+ Object destructors (`__Delete`) are implemented with C# finalizers, which are quite heavy-weight and are not automatically present for all objects. The finalizer state is determined at object creation based on whether `__Delete` is present in the prototype chain, or at the point `__Delete` is defined. If `__Delete` is defined later in the prototype chain then instance finalizers are not automatically activated; the activation can be forced manually by temporarily reassigning a different base for the instance.
	+ On script exit all non-local variables are enumerated, finalizers disabled, and `__Delete` called if present. This also includes class static variables.
* AHK says about the inc/dec ++/-- operators on empty variables: "Due to backward compatibility, the operators ++ and -- treat blank variables as zero, but only when they are alone on a line".
	+ Keysharp breaks this and will instead create a variable, initialize it to zero, then increment it.
	+ For example, a file with nothing but the line `x++` in it, will end with a variable named x which has the value of 1.
* Function objects behave differently in a few ways.
	+ The underlying function object class is named `FuncObj`. This was named so, instead of `Func`, because C# already contains a built in class named `Func`. `MsgBox is Func` is still supported though, as is `MsgBox is FuncObj`.
	+ Function objects can be created by passing the name of the function as as a direct reference or as a string to `Func()`.
	+ This can be done by passing the name of the desired function as a direct reference or as a string, and optionally an object and a parameter count like so:
		+ `Func(functionName [, object, paramCount])`.
		+ `Func("functionName" [, object, paramCount])`.
	+ Each call to these functions returns a new unique function object. For a given function, it's best to create one object and reference that throughout the script.
	+ For built-in functions which take a function object as a parameter, there are four ways to call them:
```
	Func1() {
	}
	SetTimer(Func1) ; Pass a direct reference to the function.
	SetTimer("Func1") ; Pass the name of the function.
	SetTimer(Func(Func1)) ; Pass a direct reference to the function as an argument to Func().
	SetTimer(Func("Func1")) ; Pass the name of the function as an argument to Func().
```
* Exception classes aren't, and can't be, derived from `KeysharpObject`.
	+ That is because for the exception mechanics to work in C#, all exception objects must be derived from the base `System.Exception` class, and multiple inheritance is not allowed.
	+ User-defined exception classes must derive from `Error`.
	+ `Error is Error` evaluates to false, because currently `Error()` is implemented as a function, which is a `FuncObj`. `UserDefinedError is Error` evaluates to true.
* `StrPtr()` works slightly differently because C# strings are constant.
	+ `StrPtr(variable)` returns a custom `StringBuffer` object which is entangled with the original string. When this object is used with DllCall, NumPut etc, then the `StringBuffer` is used as the pointer, and the entangled string is updated after the function call. 
	+ `StrPtr("literal")` with a literal string will pin the string from garbage collection and return the actual address of the string. This string must not be modified, and should be freed after use with `ObjFree()`.
	+ Instead of `StrPtr` it is recommended to use a `StringBuffer` instance instead.
* `CallbackCreate()` does not support the `CDecl/C` option because the program will be run in 64-bit mode.
	+ Passing string pointers to `DllCall()` when passing a created callback is recommended against. See explanation above under `StrPtr()`.
	+ Usage of the created callback will be inefficient, so usage of `CallbackCreate()` is discouraged.
* Deleting a tab via `GuiCtrl.Delete()` does not reassociate the controls that it contains with the next tab. Instead, they are all deleted.
* The size and positioning of some GUI components will be slightly different than AHK because WinForms uses different defaults.
	+ There is an additional positioning option `xc` and `yc` which position the control relative to the container. For example inside a tab `xc+10` would position the control 10 pixels from the left side of the tab control. 
	+ GroupBoxes can be used as containers by calling `GuiObj.UseGroup(groupbox)`, and to exit the group call `GuiObj.UseGroup()`.
* The class name for statusbar/statusstrip objects created by Keysharp is "WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1". However, for accessing a statusbar created by another, non .NET program, the class name is still "msctls_statusbar321".
* Using the class name with `ClassNN` on .NET controls gives long, version specific names such as "WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1" for a statusbar/statusstrip.
	+ This is because a simpler class names can't be specified in code the way they can in AHK with calls to `CreatWindowEx()`.
	+ These long names may change from machine to machine, and may change for the same GUI if you edit its code.
	+ There is an new `NetClassNN` property alongside `ClassNN`.
	+ The class names of all GUI controls created in Keysharp are prefixed with the string "Keysharp", eg: `KeysharpButton`, `KeysharpEdit` etc...
	+ `NetClassNN` will give values like 'KeysharpButton6' (note that the final digit is the same for the `ClassNN` and the `NetClassNN`).
	+ Due to the added simplicity, `NetClassNN` is preferred over `ClassNN` for WinForms controls created with Keysharp.
	+ This is used internally in the index operator for the Gui class, where if a control with a matching `ClassNN` is not found, then controls are searched for their `NetClassNN` values.
* `TrayTip()` functions slightly differently.
	+ Muting the sound played by the tip is not supported with the `Mute` option. The sound will be whatever the user has configured in their system settings.
	+ The option `4` to use the program's tray icon is not supported. It is always shown in the title of the tip.
	+ The option `32` to use the large version of the program's tray icon is not supported. Windows will always show the small version.
* `Sleep()` works, but uses `Application.DoEvents()` internally which is not a good programming practice and can lead to hard to solve bugs.
	+ For this reason, it's recommended that users use timers for repeated execution rather than a loop with calls to `Sleep()`.
* The Optimization section of the `#HotIf` documentation doesn't apply to Keysharp because it uses compiled code, thus the expressions are never re-evaluated.
* The `#ErrorStdOut` directive will not print to the console unless piping is used. For example:
	+ `.\Keysharp.exe .\test.ahk | more`
	+ `.\Keysharp.exe .\test.ahk | more > out.txt`
* Menu items, whether shown or not, have no impact on threading.
* `AddStandard()` detects menu items by string, instead of ID, because WinForms doesn't expose the ID.
* `ControlMove()` and `ControlSetPos()` operate relative to their immediate parent, which may not be the main window if they are contained in a nested control.
+ Delays are not inserted after every window and control related call. Due to the design of Keysharp, this is not needed and causes out of order message processing bugs.
	+ `SetWinDelay()`, `A_WinDelay`, `SetControlDelay` and `A_ControlDelay` exist but have no effect.
* Function objects are much slower than direct function calls due to the need to use reflection. So for repeated function calls, such as those involving math, it's best to use the functions directly.
* The `File` object is internally named `KeysharpFile` so that it doesn't conflict with `System.IO.File`.
* In `SetTimer()`, the priority is not in the range -2147483648 and 2147483647, instead it is only 0-4.
* If a `ComObject` with `VarType` of `VT_DISPATCH` and a null pointer value is assinged a non-null pointer value, its type does not change. The `Ptr` member remains available.
* `A_LineNumber` is not a reliable indicator of the line number because the preprocessor condenses the code before parsing and compiling it.
* `ObjPtr()` returns an IUnknown `ComValue` with the pointer wrapped in it, whereas `ObjPtrAddRef()` returns a raw pointer.
* Pointers returned by `StrPtr()` must be freed by passing the value to a new function named `ObjFree()`.
	+ `StrPtr()` does not return the address of the string, instead it returns the address of a copy of the bytes of the string.
* `Sleep()` will not do any sleeping if shutdown has been initiated.
* The concat-assign operator `.=` is not optimized to modify the left operand inplace, meaning calling it in a loop will be very slow. If many concats are required then use a `StringBuffer` instead.
* `/Debug` command line switch is not implemented.  
* If a script is compiled then none of Keysharp or AutoHotkey command parameters apply. 
	
###	Syntax: ###
* `DllCall()` has the following caveats:
	+ Use `Ptr` and `StringBuffer` for double pointer parameters such as `LPTSTR*`. This is recommended over the use of `StrPtr()`.
* `ImageSearch()` takes an options string as a fifth parameter, rather than inserted in the string before the `imageFile` parameter.
* A leading plus sign on numeric values, such as `+123` or `+0x123` is not supported. It has no effect anyway, so just omit it.
* AHK `unset` is implemented as `null`. `IsSet(x)` is equivalent to `x == null`.
* Use of the dereference syntax `%expression%` inside functions is highly discouraged. This is because using it will cause every function call to construct an object which captures all local variables, and depending on the number of variables the performance loss may be significant.
* `Goto` statements cannot use any type of variable. They must be labels known at compile time and function just like goto statements in C#.
* `Goto` statements being called as a function like `Goto("Label")` are not supported. Instead, just use `goto Label`.
* The `#Requires` directive differs in the following ways:
	+ In addition to supporting `AutoHotkey`, it also supports `Keysharp`.
	+ Sub versions such as -alpha and -beta are not supported. Only the four numerical values values contained in the assembly version in the form of `0.0.0.0` are supported.	
* Global variables can be accessed from anywhere by using the `Program.` prefix: `program.a := 123`.
* For any `__Enum()` class method, it should have a parameter value of 2 when returning `Array` or `Map`, since their enumerators have two fields.
* RegEx uses PCRE2 engine powered by the PCRE.NET library. There are a few limitations compared to the AutoHotkey implementation:
	+ The following options are different:
		+ S: Studies the pattern to try improve its performance.
			+ This is not supported. All RegEx objects are internally created with the `PcreOptions.Compiled` option specified, so performance should be reasonable.
		+ u: This new option disables optimizations PCRE2_NO_AUTO_POSSESS, PCRE2_NO_START_OPTIMIZE, and PCRE2_NO_DOTSTAR_ANCHOR. This option can be useful when using callouts, since these optimizations might prevent some callouts from happening.
	+ Callouts differ in a few ways:
		+ The callout function cannot be a closure, it must be a top-level function
		+ Callouts do not set `A_EventInfo`
		+ The callout function must be a top-level function
		+ A named callout must be enclosed in "", '', or {}
	+ RegEx operator ~= returns a RegExMatchInfo, which is treated as an integer in comparison or math operations

###	Additions/Improvements: ###
* `Map` internally uses a real hashmap, which means item access, insertions and removals are faster, which is especially true for larger datasets. To keep at least partial compatibility with AutoHotkey the `Map` object is copied and sorted before enumeration, which means modifying the `Map` during enumeration will not have the same effect as in AHK. 
	+ A new `HashMap` class has been added which extends `Map` and does not perform sorting before enumeration.
* The spread operator `*` may be used multiple times in one function call. `MyFunc(arr1*, arr2*)` is allowed.
* Buffer has an `__Item[]` indexer which can be used to read a byte at a 1-based offset.
* Buffer has `ToHex()`, `ToBase64()`, and `ToByteArray()` methods which can be used to convert the contents to string (hex or base64), or a byte-array to for example write to a file.
* A new class named `StringBuffer` which can be used for passing string memory to `DllCall()` which will be written to inside of the call.
	+ There are two methods for creating a `StringBuffer`:
		+ `StringBuffer(str := "") => StringBuffer`: Creates a `StringBuffer` with a string of `str` and a capacity of 256.
		+ `StringBuffer(str, capacity) => StringBuffer`: Creates a `StringBuffer` with a string of `str` and a capacity of `Max(16, capacity)`.
	+ `StringBuffer` is implicitly castable to `String`.
```
	sb := StringBuffer("hello")
	MsgBox(sb) ; Shows "hello".
```
	+ As an alternative to passing a `Buffer` object with type `Ptr` to a function which will allocate and place string data into the buffer, the caller can instead use a `StringBuffer` object to hold the new string.
	+ This relieves the caller of having to create a `Buffer` object, then call `StrGet()` on the new string data.
	+ `wsprintf()` is one such example.
```
	; Using a Buffer:	
	ZeroPaddedNumber := Buffer(20)
	DllCall("wsprintf", "Ptr", ZeroPaddedNumber, "Str", "%010d", "Int", 432, "Cdecl")
	MsgBox(StrGet(ZeroPaddedNumber)) ; Shows "0000000432".
	
	; Using a StringBuffer:
	sb := StringBuffer()
	DllCall("wsprintf", "Ptr", sb, "Str", "%010d", "Int", 432, "Cdecl")
	MsgBox(sb) ; No need to use StrGet() anymore.
```
	+ `StringBuffer` internally uses a `StringBuilder` which is how C# P/Invoke handles string pointers.
* New methods for `Array`:
	+ `Add(value) => Integer` : Adds a single element to the array.
		+ This should be more efficient than `Push(values*)` when adding a single item because it's not variadic. It also returns the length of the array after the add completes.
	+ `Filter(callback: (value [, index]) => Boolean) => Array`: Applies a filter to each element of the array and returns a new array consisting of all elements for which `callback` returned true.
	+ `FindIndex(callback: (value [, index]) => Boolean, startIndex := 1) => Integer`: Returns the index of the first element for which `callback` returned true, starting at `startIndex`. Returns 0 if `callback` never returned true.
		+ If `startIndex` is negative, the search starts from the end of the array and moves toward the beginning.
	+ `IndexOf(value, startIndex := 1) => Integer`: Returns the index of the first item in the array which equals value, starting at `startIndex`. Returns 0 if value is not found.
		+ If `startIndex` is negative, the search starts from the end of the array and moves toward the beginning.
	+ `Join(separator := ',') => String`: Joins together the string representation of all array elements, separated by `separator`.
	+ `MapTo(callback: (value [, index]) => Any, startIndex := 1) => Array`: Maps each element of the array, starting at `startIndex`, into a new array where the mapping in `callback` performs some operation.
```
	lam := (x, i) => x * i
	arr := [10, 20, 30]
	arr2 := arr.MapTo(lam)
```
	+ `Sort(callback: (a, b) => Integer) => this`: Sorts the array in place. The callback should use the usual logic of returning -1 when `a < b`, 0 when `a == b` and 1 otherwise.
* Hyperbolic versions of the trigonometric functions:
	+ `Sinh(value) => Double`
	+ `Cosh(value) => Double`
	+ `Tanh(value) => Double`
* A New function `RandomSeed(Integer)` to reinitialize the random number generator for the current thread with a specified numerical seed.
* New file functions:
	+ `FileDirName(filename) => String` to return the full path to filename, without the actual filename or trailing directory separator character.
	+ `FileFullPath(filename) => String` to return the full path to filename.
* New window functions:
	+ `WinFromPoint(x, y)` to get the window at a specific screen position.
	+ `WinMaximizeAll()` to maximize all windows.
	+ `WinGetAlwaysOnTop([winTitle, winText, excludeTitle, excludeText]) => Integer` to determine whether a window will always stay on top of other windows.
* `Run/RunWait()` can take an extra string for the argument instead of appending it to the program name string. However, the original functionality still works too.
	+ The new signature is: `Run/RunWait(target [, workingDir, options, &outputVarPID, args])`.
* When specifying colors for GUI components, the list of supported known colors can be found [here](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.knowncolor).
* `ListView` supports a new method `DeleteCol(col) => Boolean` to remove a column. The value returned indicates whether the column was found and deleted.
* New methods and properties for `Menu`:
	+ `HideItem()`, `ShowItem()` and `ToggleItemVis()` which can show, hide or toggle the visibility of a specific menu item.
	+ `MenuItemName()` to get the name of a menu item, rather than having to use `DllCall()`.
	+ `SetForeColor()` to set the fore (text) color of a menu item.
	+ `MenuItemCount` to get the number of sub items within a menu.
* `Picture` supports clearing the picture by setting the `Value` property to empty.
* New options for `UpDown`:
	+ These relieve the caller of having to use native Windows API calls.
	+ `IncrementXXX` to specify an increment other than 1.
		+ `MyGui.Add("UpDown", "x5 y55 vMyNud Increment10", 1)`
	+ `Hex` to show the numeric value in hexadecimal.
* `TabControl` supports a new method `SetTabIcon(tabIndex, imageIndex)` to relieve the caller of having to use `SendMessage()`.
* `TreeView` supports a new method `GetNode(nodeIndex) => TreeNode` which retrieves a raw winforms TreeNode object based on a passed in ID.
* Gui controls support taking a boolean `Autosize` (default: `false`) argument in the `Add()` method to allow them to optimally size themselves.
* `Gui` has a new property named `Visible` which get/set whether the window is visible or not.
* A new function `ShowDebug()` to show the main window and focus the debug output tab.
* A new function `OutputDebugLine()` which is the same as `OutputDebug()` but appends a linebreak at the end of the string.
* `EnvUpdate()` is retained to provide for a cross platform way to update environment variables.
* The 40 character limit for hotstring abbreviations has been removed. There is no limit to the length.
* `FileGetSize()` supports `G` and `T` for gigabytes and terabytes.
* `DateAdd()` and `DateDiff()` support taking a value of `"L"` for the `TimeUnits` parameter to add miLliseconds or return the elapsed time in milliseconds, respectively.
	+ See the new accessors `A_NowMs`/`A_NowUTCMs`.
* New function `FormatCs()` is an alternative to AHK `Format`. The syntax used in `Format()` is exactly that of `string.Format()` in C#, except with 1-based indexing. Traditional AHK style formatting is not supported.
	+ Full documentation for the formatting rules can be found [here](https://learn.microsoft.com/en-us/dotnet/api/system.string.format).
* `SubStr()` uses a default of 1 for the second parameter, `startingPos`, to relieve the caller of always having to specify it.
* New string functions:
	+ `Base64Decode(str) => Array` to convert a Base64 string to a Buffer containing the decoded bytes.
	+ `Base64Encode(value) => String` to convert a byte array to a Base64 string.
	+ `NormalizeEol(str, eol) => String` to make all line endings in a string match the value passed in, or the default for the current environment.
	+ `StartsWith(value, token [,comparison]) => Boolean` and `EndsWith(value, token [,comparison]) => Boolean` to determine if the beginning or end of a string start/end with a given string.
	+ `Join(separator, params*) => String` to join each parameter together as a string, separated by `separator`.
		+ Pass params as `params*` if it's a collection.
* New RegEx functions `RegExMatchCs()` and `RegExReplaceCs()` which use the C# style regular expression syntax rather than PCRE2.
	+ `OutputVar` in `RegExMatchCs()` will be of type `RegExMatchInfoCs`.
	+ PCRE exceptions are not thrown when there is an error, instead C# regex exceptions are thrown.
	+ To learn more about C# regular expressions, see [here](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions).
	+ The following options are different:
		+ -A: Forces the pattern to be anchored; that is, it can match only at the start of Haystack. Under most conditions, this is equivalent to explicitly anchoring the pattern by means such as `^`.
			+ -This is not supported, instead just use `^` or `\A` in your regex string.

		+ -C: Enables the auto-callout mode.
			+ -This is not supported. C# regular expressions don't support calling an event handler for each match. You must manually iterate through the matches yourself.
			
		+ -D: Forces dollar-sign ($) to match at the very end of Haystack, even if Haystack's last item is a newline. Without this option, $ instead matches right before the final newline (if there is one). Note: This option is ignored when the `m` option is present.
			+ -This is not supported, instead just use `$`. However, this will only match `\n`, not `\r\n`. To match the `CR/LF` character combination, include `\r?$` in the regular expression pattern.

		+ -J: Allows duplicate named subpatterns.
			+ -This is not supported.

		+ -S: Studies the pattern to try improve its performance.
			+ -This is not supported. All RegEx objects are internally created with the `RegexOptions.Compiled` option specified, so performance should be reasonable.

		+ -U: Ungreedy.	
			+ -This is not supported, instead use `?` after: `*, ?, +, and {min,max}`.

		+ -X: Enables PCRE features that are incompatible with Perl.
			+ -This is not supported because it's Perl specific.

		+ ``` `a `n `r ```: Causes specific characters to be recognized as newlines.
			+ -This is not supported.

		+ `\K` is not supported, instead, try using `(?<=abc)`.
		
* The v1 `Map` methods `MaxIndex()` and `MinIndex()` are still supported. They are also supported for `Array`.
* New function `ImageCapture(x, y, width, height [, filename]) => Bitmap` can be used to return a bitmap screenshot of an area of the screen and optionally save it to file.
* Rich text boxes are supported by passing `RichEdit` to `Gui.Add()`. The same options from `Edit` are supported with the following caveats:
	+ `Multiline` is true by default.
	+ `WantReturn` and `Password` are not supported.
	+ `Uppercase` and `Lowercase` are supported, but only for key presses, not for pasting.
	+ The `Gui.Control.Value` property will only get/set the displayed text of the control. To get/set the raw rich text, use the new property `Gui.Control.RichText`.
		+ Use `AltSubmit` with `Submit()` to get the raw rich text.
		+ Attempting to use `Gui.Control.RichText` on any control other than `RichEdit` will throw an exception.
* Loading icons from .NET DLLs is supported by passing the name of the icon resource in place of the icon number.
	+ To set the tray icon to the built in suspended icon:
		+ `TraySetIcon(A_KeysharpCorePath, "Keysharp_s.ico")`
	+ To set a menu item to the same:
		+ `parentMenu.SetIcon("Menu caption", A_KeysharpCorePath, "Keysharp_s.ico")`
* New clipboard functions:
	+ `CopyImageToClipboard(filename [,options])` is supported which copies an image to the clipboard.
		+ Uses the same arguments as `LoadPicture()`.
		+ This is a fully separate copy and does not share any handle, or perform any file locking with the original image being read.
	+ `IsClipboardEmpty() => Boolean` returns whether the clipboard is truly empty.
* When sending a string through `SendMessage()` using the `WM_COPYDATA` message type, the caller is no longer responsible for creating the special `COPYDATA` struct.
	+ Instead, just pass `WM_COPYDATA (0x4A)` as the message type and the string as the `lparam`, and `SendMessage()` will handle it internally.
	+ Note, this will send the string as UTF-16 Unicode. If you need to send to a program which expects ASCII, then you'll need to manually create the `COPYDATA` struct.
* A new function `Collect()` which calls `GC.Collect()` to force a memory collection.
	+ This rarely ever has to be used in properly written code.
	+ Calling `Collect()` may not always have an immediate effect. For example if an object is assigned to a variable inside a function and then the variable is assigned an empty string then calling `Collect()` after it will not cause the object destructor to be called. Only after the function has returned will the object be considered to have no references and `Collect()` starts working.
	+ If an object destructor needs to be called immediately then it may better to call `Object.__Delete()` manually.
* In addition to using `#ClipboardTimeout`, a new accessor named `A_ClipboardTimeout` can be used at any point in the program to get or set that value.
* A compiled script can be reloaded.
	+ AHK does not support reloading a compiled script.
* A new function `RunScript(code, callbackOrAsync?, name := "DynamicScript", executable?)` which dynamically parses, compiles, and runs the provided code. Optionally provide the script name; whether to run it asynchronously (non-unset non-zero `callbackOrAsync` causes async run without a callback); an executable path to run the compiled assembly (defaults to the current process). 
  If `callbackOrAsync` is provided a function then it is called after the script has finished with the `ProcessInfo` as the only argument. Over multiple runs `RunScript` is faster than running the process manually and writing to StdIn because of assembly and compilation caching.   
  This function returns a `ProcessInfo` object encapsulating info and I/O for the process. Available properties: `HasExited`, `ExitCode`, `ExitTime` (YYYYMMDDHH24MISS), `StdOut`, `StdErr`, `StdIn` (as `KeysharpFile`). Available methods: `Kill()`.
* `A_EventInfo` is not limited to positive values when reporting the mouse wheel scroll amount.
	+ When scrolling up, the value will be positive, and negative when scrolling down.
* New accessors:
	+ `A_AllowTimers` returns whether timers are allowed or not. It's also easier to set this value rather than call `Thread("NoTimers")`.
	+ `A_CommandLine` returns the command line string. This is preferred over passing `GetCommandLine` to `DllCall()` as noted above.
	+ `A_DefaultHotstringCaseSensitive` returns the default hotstring case sensitivity mode.
	+ `A_DefaultHotstringConformToCase` returns the default hotstring case conformity mode.
	+ `A_DefaultHotstringDetectWhenInsideWord` returns the default hotstring word detection mode.
	+ `A_DefaultHotstringDoBackspace` returns the default hotstring backspacing mode.
	+ `A_DefaultHotstringDoReset` returns the default hotstring resetting mode.
	+ `A_DefaultHotstringEndCharRequired` returns the default hotstring ending character mode.
	+ `A_DefaultHotstringEndChars` returns the default hotstring ending characters.
	+ `A_DefaultHotstringKeyDelay` returns the default hotstring key delay length in milliseconds.
	+ `A_DefaultHotstringNoMouse` returns whether mouse clicks are prevented from resetting the hotstring recognizer because `#Hotstring NoMouse` was specified.
	+ `A_DefaultHotstringOmitEndChar` returns the default hotstring ending character replacement mode.
	+ `A_DefaultHotstringPriority` returns the default hotstring priority.
	+ `A_DefaultHotstringSendMode` returns the default hotstring sending mode.
	+ `A_DefaultHotstringSendRaw` returns the default hotstring raw sending mode.
	+ `A_DirSeparator` returns the directory separator character which is `\` on Windows and `/` elsewhere.
	+ `A_HasExited` returns whether shutdown has been initiated.
	+ `A_KeysharpCorePath` provides the full path to the Keysharp.Core.dll file.
	+ `A_LoopRegValue` which makes it easy to get a registry value when using `Loop Reg`.
	+ `A_MaxThreads` returns the value `n` specified with `#MaxThreads n`.
	+ `A_NoTrayIcon` returns whether the tray icon was hidden with #NoTrayIcon.
	+ `A_NowMs`/`A_NowUTCMs` returns the current local/UTC time formatted to include milliseconds like so "YYYYMMDDHH24MISS.ff".
		+ These can be used with `DateAdd()`/`DateDiff()` using `"L"` for the `TimeUnits` parameter.
	+ `A_SuspendExempt` returns whether subsequent hotkeys and hotstrings will be exmpt from suspension because `#SuspendExempt true` was specified.
	+ `A_TotalScreenHeight` returns the total height in pixels of the virtual screen.
	+ `A_TotalScreenWidth` returns the total width in pixels of the virtual screen.
	+ `A_UseHook` returns the value `n` specified with `#UseHook n`.
	+ `A_WinActivateForce` returns whether the forceful method of activating a window is in effect because `#WinActivateForce` was specified.
	+ `A_WorkAreaHeight` returns the height of the working area of the primary screen.
	+ `A_WorkAreaWidth` returns the width of the working area of the primary screen.
* `Log(number, base := 10)` is by default base 10, but it can accept a double as the second parameter to specify a custom base.
* In `SetTimer()`:
	+ In the callback function, `A_EventInfo` is set to the function object used to create the timer.
	+ This allows the handler to alter the timer by passing the function object back to another call to `SetTimer()`.
	+ Timers are not disabled when the program menu is shown.
* A new timer function `EnabledTimerCount()` which returns the number of currently enabled timers in existence.
* Reference parameters for functions using `&` are supported with the following improvements and caveats:
	+ Passing class members, array indexes and map values by reference is supported.
		+ `func(&classobj.classprop)`
		+ `func(&myarray[5])`
		+ `func(&mymap["mykey"])`
	+ Reference parameters in functions work for class methods, global functions, built in functions, lambdas and function objects.
		+ Lambdas with a single reference parameter can be declared with no parentheses:
			+ `lam := &a => a := (a * 2)`
	+ When passing a class member variable as a dynamic reference to a function from within another function of that same class, the `this` prefix must be used:
```
	class myclass
	{
		x := 11
		y11 := 0
				
		myclassreffunc(&val)
		{
		}
				
		callmyclassreffunc()
		{
			myclassreffunc(&this.y%x%) ; Use this.
		}
	}
```
* New functions for encrypting/decrypting an object:
	+ Encrypt or decrypt an object using the AES algorithm: `AES(value, key, decrypt := false) => Array`.
	+ Generate hash values using various algorithms: `MD5(value) => String`, `SHA1(value) => String`, `SHA256(value) => String`, `SHA384(value) => String`, `SHA512(value) => String`.
	+ Calculate the CRC32 polynomial of an object: `CRC32(value) => Integer`.
	+ Generate a secure cryptographic random number: `SecureRandom(min, max) => Decimal`.
* New class and functions for managing real threads which are not related to the green threads that are used for the rest of the project.
	+ A `RealThread` is created by calling `StartRealThread()`.
```
	class RealThread
	{
		RealThread(Task)
		RealThread ContinueWith(funcobj [, params*]) => RealThread ; Call `funcobj` after the task completes, optionally passing `params` to it and return a new `RealThread` object for the continuation thread.
		Wait([timeout]) ; Wait until the thread object which was passed to the constructor completes. Optionally return after a specified timeout period in milliseconds elapses.
	}
```
	+ `StartRealThread(funcobj [, params*]) => RealThread` Call `funcobj` in a real thread, optionally passing `params` to it, and return a `RealThread` object.
	+ `LockRun(lockobj, funcobj [, params*])` Call `funcobj` inside of a lock on `lockobj`, optionally passing `params` to it.
		+ `lockobj` must be initialized to some value, such as an empty string.
* `FuncObj()`, `IsFunc()`, `Any.HasProp()` and `ObjBindMethod()` take a new optional parameter which specifies the parameter count of the method to search for.
	+ The new signatures are:
		+ `ObjBindMethod(obj [, paramCount , method, params]) => FuncObj`
		+ `FuncObj(name, object [, paramCount]) => FuncObj`
		+ `IsFunc(functionName [, paramCount]) => Integer`
		+ `Any.HasProp(propName [, paramCount]) => Integer`
			+ The only properties which can have parameters are the `__Item[]` indexer properties.
	+ This is needed to resolve the proper overloaded method. However, overloaded methods are currently not supported, but might be implemented in the future.
	+ Omit `paramCount` or pass -1 to just use the first encountered method on the specified object with the specified name.
* `KeysharpObject` has a new method `OwnPropCount()` which corresponds to the global function `ObjOwnPropCount()`.
* `ComObjConnect()` takes an optional third parameter as a boolean (default: `false`) which specifies whether to write additional information to the debug output tab when events are received.
* New function `Mail(recipients, subject, message, options)` to send an email.
	+ `recipients`: A list of receivers of the message.
	+ `subject`: Subject of the message.
	+ `message`: Message body.
	+ `options`: A `Map` with any the following optional key/value pairs:
		+ "attachments": A string or `Array` of strings of file paths to send as attachments.
		+ "bcc": A string or `Array` of strings of blind carbon copy recipients.
		+ "cc": A string or `Array` of strings of carbon copy recipients.
		+ "from": A string of comma separated from address.
		+ "replyto": A string of comma separated reply address.
		+ "host": The SMTP client hostname and port string in the form "hostname:port".
		+ "header": A string of additional header information.
* Preprocessor directives are supported using the familiar syntax of C#.
	+ `#if symbol` is used to enable a section of code if symbol is defined.
	+ By default, the following are defined:
		+ `WINDOWS` if you are running the script on Microsoft Windows.
		+ `LINUX` if you are running the script on linux.
		+ `KEYSHARP`
	+ `#else` can be used to take an alternate path if the preceding `#if` evaluates to false.
	+ `#elif symbol` can be used to evaluate another symbol if the preceding `#if` or `#elif` evaluate to false.
	+ All preprocessor blocks must end with an `#endif`
	+ New preprocessor symbols can be defined using `#define symbol`.
	+ Logical statements can be evaluated using the operators `&&`, `||` and `!`.
	+ Evaluation of preprocessor statements are case insensitive.
	+ Some examples are:
```
		#if WINDOWS
			MsgBox("Windows")
		#elif LINUX
			MsgBox("linux")
		#else
			MsgBox("Unsupported OS")
		#endif
		
		#if !(WINDOWS || LINUX)
			MsgBox("Unsupported OS")
		#endif
		
		#if 1
			MsgBox("Always true")
		#endif
		
		#if 0
			MsgBox("Always false")
		#endif
		
		#define NEW_DEFINE
		#if NEW_DEFINE
			MsgBox("True because of new definition")
		#endif
```
* New preprocessor directives have been added.
	+ `#HookMutexName <name>` allows renaming the mutex objects created to detect keyboard and mouse hooks in other running scripts. The default name is "Keysharp".
	+ Assembly description attributes may be changed with the following directives (with the desired value as the only argument of the directive):
		+ `#AssemblyName`
		+ `#AssemblyDescription`
		+ `#AssemblyConfiguration`
		+ `#AssemblyCompany`
		+ `#AssemblyProduct`
		+ `#AssemblyCopyright`
		+ `#AssemblyTrademark`
		+ `#AssemblyVersion`
* Experimental `Clr` class has been added which aims to provide CLR interop with regular AutoHotkey syntax, meaning easy access to CLR libraries.
	+ `Clr.Load(asmOrPath)` loads a CLR assembly from a dll file or assembly name, and returns a `ManagedAssembly` or `ManagedNamespace` object. Example: `System := Clr.Load("System")`
		+ `ManagedNamespace` can be accessed with property access syntax to get namespaces and types (`ManagedType`). Example: `linq := System.Linq.Enumerable`
		+ `ManagedType` may be accessed for static methods/properties, or called to create a new `ManagedInstance`. 
		+ `ManagedInstance` may be accessed with normal AutoHotkey syntax for properties, methods, and indexer access. Example: `linq.Where(nums, isOdd)`
		+ Basic type marshalling between AutoHotkey and CLR is supported (including function objects), more complicated types may not currently work. 
	+ `Clr.GetNamespaceName(ManagedNamespace)` returns the full intenal namespace name of the namespace wrapped by `ManagedNamespace`.
	+ `Clr.GetTypeName(ManagedType)` returns the full internal type name of the type wrapped by `ManagedType`.
* Command line switches may start with either `/` (Windows-only), `-` or `--`. 
* Command line switches
    - `--script`    
	  Causes a compiled script to ignore its main code and instead executes the provided script. For this to apply, `--script` must be the first command line argument.    
	  Example: `CompiledScript.exe /script /ErrorStdOut MyScript.ahk "Script's arg 1"`
	- `--version`, `-v`  
	  Displays Keysharp version.
	- `--codeout`  
	  In addition to running the script, Keysharp outputs a .cs file with the same name as the script containing the code which was used to compile. This is the same code displayed in Keyview. 
	- `--exeout`  
	  In addition to running the script, Keysharp outputs a .exe file which can be ran as standalone from Keysharp (but still requires .NET 9).
	- `--minimalexeout`    
	  Same as `--exeout` but the number of file dependencies is reduced by embedding them in Scriptname.dll. The resulting program will have five dependencies: Scriptname.exe, Scriptname.dll, Keysharp.Core.dll, Scriptname.deps.json, and Scriptname.runtime.config. To get a truly single-file executable the script must be compiled as a C# project, for example as Keysharp.OutputTest in the Keysharp solution.
	- `--validate`  
	  Compiles but does not run the script. Can be used to check for load-time errors.
	- `--assembly [Type Method]`  
	  Reads pre-compiled assembly code from the file or StdIn and runs it. Optionally also provide the entrypoint type and method, but if omitted then the default type `Keysharp.CompiledMain.program` and method `Main` are used.
	
###	Removals: ###
* `ListLines()` is non-functional because C# doesn't support it.
* The `R`, `Dn` or `Tn` parameters in `FormatTime()` are not supported, except for 0x80000000 to disallow user overrides.
	+ If you want to specify a particular format or order, do it in the format argument. There is no need or reason to have one argument alter the other.
	+ [Here](https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings) is a list of the C# style DateTime formatters which are supported.
* Static text controls do not send the Windows `API WM_CTLCOLORSTATIC (0x0138)` message to their parent controls like they do in AHK.
* Double click handlers for buttons are not supported.
* UpDown controls with paired buddy controls are not supported. Keysharp just uses the regular NumericUpDown control in C#.
	+ The options `16`, `Horz` and `Wrap` have no effect.
	+ The min and max values cannot be swapped.
* `IL_Create()` only takes one parameter: `largeIcons`. `initialCount` and `growCount` are no longer needed because memory is handled internally.
* `LoadPicture()` does not accept a `GDI+` argument as an option.
* For slider events, the second parameter passed to the event handler will always be `0` because it's not possible to retrieve the method by which the slider was moved in C#.
* `PixelGetColor()` ignores the `mode` parameter.
* `DirSelect()`:
	+ The `1`, `3` and `5` options don't apply and the New Folder button will always be shown.
	+ Modality cannot be configured with `Gui.Opt("+OwnDialogs")` because the folder select dialog is always modal.
	+ Restricting folder navigation is not supported.
* `MsgBox()`:
	+ The modality options are ignored.
	+ The message box will block the window that launched it by default. If `+OwnDialogs` is in effect, then all GUIs in the script are blocked until it is dismissed.
	+ System modal dialog boxes are no longer supported on Windows.
	+ The help option `16384` is ignored.
* Only `Tab3` is supported, no older tab functionality is present.
* When adding a `ListView`, the `Count` option is not supported because C# can't preallocate memory for a `ListView`.
* The address of a variable cannot be taken using the reference operator.
	+ It returns a VarRef object as in AHK.
* `OnMessage()` doesn't observe any of the behavior mentioned in the documentation regarding the message check interval because it's implemented in a different way.
	+ A GUI object is required for `OnMessage()` to be used.
* Pausing a script is not supported because a Keysharp script is actually a running program.
	+ The pause menu item and `Pause()` function have been removed.
* `ObjAddRef()` and `ObjPtrAddRef()` do not have an effect for non-COM objects. Instead, use the following:
	+ `newref := theobj ; adds 1 to the reference count`
	+ `newref := "" ; subtracts 1 from the reference count`
* `#Warn` to enable/disable compiler warnings is not supported yet.
* The `/script` option for compiled scripts does not apply and is therefore not implemented.
* The Help and Window Spy menu items are not implemented yet.
* `Download()` only supports the `*0` option, and not any other numerical values.
* Properties other than `__Item[]` cannot take parameters. If you need to pass a parameter, use a method instead.
	+ This also applies to properties which have been dynamically defined with `DefineProp()`.
* When passing `"Interrupt"` as the first argument to `Thread()`, the third argument for `LineCount` is not supported because Keysharp does not support line level awareness.
* Tooltips do not automatically disappear when clicking on them.

## Code acknowledgements ##

* The initial IronAHK developers 2010 - 2015
* [Logical string comparison](https://www.codeproject.com/Articles/22175/Sorting-Strings-for-Humans-with-IComparer), [cddl 1.0](https://opensource.org/licenses/cddl1.php)
* [Cross platform INI file processor](https://www.codeproject.com/articles/20053/a-complete-win-ini-file-utility-class)
* [P/Invoke calls](https://www.pinvoke.net)
* [Tuple splatter](https://github.com/iotalambda/TupleSplatter/tree/master/TupleSplatter)
* [Semver version parsing](https://github.com/WalkerCodeRanger/semver)
* [PictureBox derivation](https://www.codeproject.com/articles/717312/pixelbox-a-picturebox-with-configurable-interpolat)
* [Using SendMessage() with string](https://gist.github.com/BoyCook/5075907)
* [Program icon](https://thenounproject.com/icon/mechanical-keyboard-switch-2987081/) is a derivative of work by [Bamicon](https://thenounproject.com/bamicon/)
* [NAudio](https://github.com/naudio/NAudio)
* [Scintilla editor for .NET](https://github.com/desjarlais/Scintilla.NET)
* [Scintilla setup code in Keyview](https://github.com/robinrodricks/ScintillaNET.Demo)
* Various posts on [Stack Overflow](https://stackoverflow.com/)

## Who do I talk to? ##

Please make an account here and post a ticket.