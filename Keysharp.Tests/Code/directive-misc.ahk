#NoTrayIcon

#import KS { * }
#CLIPBOARDTIMEOUT 2000
#ERRORSTDOUT
#USEHOOK true
#MAXTHREADS 100
#MAXTHREADSBUFFER 1
#MAXTHREADSPERHOTKEY 150
#NOTRAYICON
#SUSPENDEXEMPT 1
#WINACTIVATEFORCE
#DLLLOAD *i user32.dll
; Compile-time only (it picks the PE subsystem of a --compile exe build), so here it just has to be accepted and do nothing.
#CONSOLEAPP
#Include <assert>

AssertEq(A_ClipboardTimeout, 2000, A_LineNumber)

Assert(A_UseHook, A_LineNumber)
	
Assert(A_MaxThreadsBuffer, A_LineNumber)

AssertEq(A_MaxThreadsPerHotkey, 150, A_LineNumber)

Assert(A_NoTrayIcon, A_LineNumber)
	
Assert(A_SuspendExempt, A_LineNumber)

Assert(A_WinActivateForce, A_LineNumber)

#INPUTLEVEL 50

AssertEq(A_InputLevel, 50, A_LineNumber)

FileAppend "pass", "*"

ExitApp()
