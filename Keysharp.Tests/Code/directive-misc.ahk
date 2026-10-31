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

; Set by a directive past the end of the auto-execute section, which still applies when the script loads.
AssertEq(A_HotIfTimeout, 123, A_LineNumber)

#INPUTLEVEL 50

Hotkey("F5", (*) => "")
Suspend(1)
Hotstring("::dynamic", "ok")
Hotstring(":S:dynamicexempt", "ok")
Hotstring(":S0:dynamicordinary", "ok")

#if !SUSPENDEXEMPT_INSPECT
; Hotkey's options may run together, and S and B may end the text without their digit, as in AutoHotkey.
try
{
	Hotkey("F5", , "T2 P1 S")
	Hotkey("F5", , "B")
}
catch
	Assert(false, A_LineNumber)

for InvalidOption in ["X", "O", "T0", "T256", "I101", "B2", "P-"]
	Throws(() => Hotkey("F5", , InvalidOption), A_LineNumber, ValueError)
Hotkey("F5", , "T2P-3B0S1I100")
; An invalid option leaves no hotkey behind.
Throws(() => Hotkey("F21", (*) => 0, "X"), A_LineNumber, ValueError)
Throws(() => Hotkey("F21", , "On"), A_LineNumber, TargetError)
Throws(() => Hotkey("F21", "NoSuchHotkey"), A_LineNumber, ValueError)
HotIfWinActive("no window has this title")
Throws(() => Hotkey("F5", , "P1"), A_LineNumber, TargetError)
HotIfWinActive()

FileAppend "pass", "*"

ExitApp()
#endif
return
#HOTIFTIMEOUT 123
