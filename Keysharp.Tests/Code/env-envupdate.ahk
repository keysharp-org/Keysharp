#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Nothing was changed through EnvSet, so there is nothing to publish: EnvUpdate succeeds and leaves the
; process environment as it was.
before := EnvGet("PATH")

try
	EnvUpdate()
catch
	Assert(false, A_LineNumber)

AssertEq(EnvGet("PATH"), before, A_LineNumber)

FileAppend "pass", "*"
