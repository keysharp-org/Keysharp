#import KS { A_NewLine, A_ProcessArch, A_OSArch }
#NoTrayIcon
#Include <assert>

; Can't really test if some of these properties have "valid" values. So at least just test if they can be compiled properly in a script.

#if WINDOWS
expectedOsType := "WINDOWS"
#elif OSX
expectedOsType := "OSX"
#else
expectedOsType := "LINUX"
#endif

Assert(A_OSType = expectedOsType, A_LineNumber)

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

Assert(A_ProcessArch = expectedArch, A_LineNumber)

; A_OSArch uses the same names. It only differs from A_ProcessArch under emulation, so just check it
; is one of the known values rather than tying the test to the host.
Assert(A_OSArch ~= "^(X64|ARM64|X86|ARM)$", A_LineNumber)

x := A_WorkingDir

Assert(x != "", A_LineNumber)

x := A_ScriptName

AssertEq(x, "props-script-properties.ahk", A_LineNumber)

x := A_ScriptFullPath

Assert(x != "", A_LineNumber)
		
x := A_ScriptDir

Assert(x != "", A_LineNumber)

#if !OSX
x := A_ScriptHwnd

Assert(x > 0, A_LineNumber)
#endif

x := A_LineNumber ; This is not a reliable indicator of the line because the preprocessor condenses everything.

Assert(x > 0, A_LineNumber) ; 

oldx := x
x := A_LineNumber

Assert(x > oldx, A_LineNumber)

x := A_LineFile

Assert(x = A_ScriptFullPath, A_LineNumber)  ; These two are always the same except for when the latter is in an include file.

myfunc()
{
	y := A_ThisFunc

	Assert(y = "myfunc", A_LineNumber)
}

myfunc()

AssertEq(A_IsUnicode, true, A_LineNumber)

Assert(A_NewLine = "`n" || A_NewLine = "`r`n", A_LineNumber)

; The AutoHotkey version Keysharp implements.
Assert(A_AhkVersion = "2.1-alpha.31", A_LineNumber)

Assert(!IsSet(A_E) && !IsSet(A_IPAddress) && !IsSet(A_PeekFrequency) && !IsSet(A_PI) && !IsSet(A_TempFile)
	&& !IsSet(A_ThisMenu) && !IsSet(A_ThisMenuItem) && !IsSet(A_ThisMenuItemPos), A_LineNumber)

FileAppend "pass", "*"
