#NoTrayIcon

#import KS { * }
#CLIPBOARDTIMEOUT 2000
#ERRORSTDOUT
#Warn All, StdOut
#USEHOOK true
#MAXTHREADS 100
#MAXTHREADSBUFFER 1
#MAXTHREADSPERHOTKEY 150
#NOTRAYICON
#SUSPENDEXEMPT 1
F1::Suspend(-1)
F2::return
:*:exempt::ok
DirectiveInFunction() {
    #SuspendExempt false
}
#Hotstring S
:*:hotstringdefault::ok
:S0:explicitoff::ok
#Hotstring S0
F3::return
:*:ordinary::ok
{
    #SuspendExempt true
    F4::return
    :*:grouped::ok
}
F6::return
:*:following::ok
{
    #SuspendExempt false
    #Hotstring S
    :*:nesteddefault::ok
    #Hotstring S0
}
#WINACTIVATEFORCE
#DLLLOAD *i user32.dll
; Compile-time only (it picks the PE subsystem of a --compile exe build), so here it just has to be accepted and do nothing.
#App { ConsoleApp: true }
#Include <assert>

AssertEq(A_ClipboardTimeout, 2000, A_LineNumber)

Assert(A_MaxThreadsBuffer, A_LineNumber)

AssertEq(A_MaxThreadsPerHotkey, 150, A_LineNumber)

Assert(A_NoTrayIcon, A_LineNumber)

Assert(A_WinActivateForce, A_LineNumber)

#INPUTLEVEL 50

AssertEq(A_InputLevel, 50, A_LineNumber)

Hotkey("F5", (*) => "")
Suspend(1)
Hotstring("::dynamic", "ok")
Hotstring(":S:dynamicexempt", "ok")
Hotstring(":S0:dynamicordinary", "ok")

#if !SUSPENDEXEMPT_INSPECT
FileAppend "pass", "*"

ExitApp()
#endif
