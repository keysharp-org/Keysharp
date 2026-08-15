#import KS { A_KeysharpPath, A_NewLine, A_ProcessArch, A_OSArch }
#NoTrayIcon

; Can't really test if some of these properties have "valid" values. So at least just test if they can be compiled properly in a script.

#if WINDOWS
expectedOsType := "WINDOWS"
#elif OSX
expectedOsType := "OSX"
#else
expectedOsType := "LINUX"
#endif

if (A_OSType = expectedOsType)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Exactly one architecture symbol is predefined, and it names A_ProcessArch.
#if X64
expectedArch := "X64"
#elif ARM64
expectedArch := "ARM64"
#elif X86
expectedArch := "X86"
#elif ARM
expectedArch := "ARM"
#else
expectedArch := "<none defined>"
#endif

if (A_ProcessArch = expectedArch)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A_OSArch uses the same names. It only differs from A_ProcessArch under emulation, so just check it
; is one of the known values rather than tying the test to the host.
if (A_OSArch ~= "^(X64|ARM64|X86|ARM)$")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := A_WorkingDir

if (x != "")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := A_ScriptName

if (x == "props-script-properties.ahk")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := A_ScriptFullPath

if (x != "")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
x := A_ScriptDir

if (x != "")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#if !OSX
x := A_ScriptHwnd

if (x > 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
#endif

x := A_LineNumber ; This is not a reliable indicator of the line because the preprocessor condenses everything.

if (x > 0) ; 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

oldx := x
x := A_LineNumber

if (x > oldx)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := A_LineFile

if (x = A_ScriptFullPath) ; These two are always the same except for when the latter is in an include file.
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

myfunc()
{
	y := A_ThisFunc

	if (y = "myfunc")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}

myfunc()

if (A_IsUnicode == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_KeysharpPath = A_AhkPath && (A_NewLine = "`n" || A_NewLine = "`r`n"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; The AutoHotkey version Keysharp implements.
if (A_AhkVersion = "2.1-alpha.30")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (!IsSet(A_E) && !IsSet(A_IPAddress) && !IsSet(A_PeekFrequency) && !IsSet(A_PI) && !IsSet(A_TempFile)
	&& !IsSet(A_ThisMenu) && !IsSet(A_ThisMenuItem) && !IsSet(A_ThisMenuItemPos))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
