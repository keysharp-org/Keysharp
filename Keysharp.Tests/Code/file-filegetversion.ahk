#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { A_KsVersion }
#Include <assert>
#Include <testpaths>

dir := "./Keysharp.Core.dll"
ver := FileGetVersion(dir)
split := StrSplit(ver, ".")
len := split.Length

AssertEq(len, 4, A_LineNumber)

; A_KsVersion is Keysharp.Core.dll's own file version attribute.
AssertEq(ver, A_KsVersion, A_LineNumber)

; As in AutoHotkey, a file without version information is an error, as is an empty name.
Throws(() => FileGetVersion(TestFixturePath("DirCopy/file1.txt")), A_LineNumber, OSError)
Throws(() => FileGetVersion(""), A_LineNumber, ValueError)

#if WINDOWS
; The fixed numeric version, without the text the FileVersion string may add.
Assert(FileGetVersion(A_WinDir "\System32\notepad.exe") ~= "^\d+\.\d+\.\d+\.\d+$", A_LineNumber)
#endif

FileAppend "pass", "*"
