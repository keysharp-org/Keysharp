#NoTrayIcon

#import KS { FileFullPath }
; #Include %A_ScriptDir%/header.ahk

origdir := A_WorkingDir
dir := "../../../Keysharp.Tests/Code/DirCopy"
fullpath := FileFullPath(dir)
SetWorkingDir(fullpath)

if (A_WorkingDir == fullpath)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#if WINDOWS
	SetWorkingDir("C:\a\fake\path") ; Non-existent folders don't get assigned.
#else
	SetWorkingDir("/a/fake/path")
#endif

if (A_WorkingDir == fullpath) ; So it should remain unchanged.
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetWorkingDir(origdir)
