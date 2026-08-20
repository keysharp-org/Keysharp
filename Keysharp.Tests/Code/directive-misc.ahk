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

if (A_ClipboardTimeout == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_UseHook)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (A_MaxThreadsBuffer)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_MaxThreadsPerHotkey == 150)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_NoTrayIcon)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (A_SuspendExempt)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_WinActivateForce)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#INPUTLEVEL 50

if (A_InputLevel == 50)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

ExitApp()