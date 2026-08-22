#NoTrayIcon

#import KS { FileFullPath }
#Include <assert>
; #Include %A_ScriptDir%/header.ahk

origdir := A_WorkingDir
dir := "../../../Keysharp.Tests/Code/DirCopy"
fullpath := FileFullPath(dir)
SetWorkingDir(fullpath)

AssertEq(A_WorkingDir, fullpath, A_LineNumber)

#if WINDOWS
	SetWorkingDir("C:\a\fake\path") ; Non-existent folders don't get assigned.
#else
	SetWorkingDir("/a/fake/path")
#endif

AssertEq(A_WorkingDir, fullpath, A_LineNumber)  ; So it should remain unchanged.

SetWorkingDir(origdir)

FileAppend "pass", "*"
