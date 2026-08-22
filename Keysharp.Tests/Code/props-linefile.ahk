#NoTrayIcon
#Include <assert>

; A_LineFile in the main script is the same as A_ScriptFullPath.
Assert(A_LineFile = A_ScriptFullPath, A_LineNumber)

#include props-linefile-target.ahk

; A_LineFile referenced inside code that lives in an #included file must report that
; included file's full path, not the main script's (the canonical "am I the main
; script?" idiom A_LineFile = A_ScriptFullPath relies on this).
CheckIncludedLineFile()

FileAppend "pass", "*"
