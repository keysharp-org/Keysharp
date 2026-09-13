#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon

#import KS { RealThread, Await, Monitor, WinEvent }
#Include <assert>

; A display-change hook does not keep the real thread that started it running, as an OnMessage for
; WM_DISPLAYCHANGE would not: the worker ends once its body returns, and its hook ends with it.
monitorWorker := RealThread(() => Monitor.OnChange((*) => 0))
monitorHook := Await(monitorWorker.Task)
Assert(monitorWorker.Terminated.Wait(5000), A_LineNumber)
Assert(!monitorWorker.IsAlive, A_LineNumber)
Assert(!monitorHook.InProgress, A_LineNumber)
Assert(monitorHook.EndReason = "Exit" || monitorHook.EndReason = "Failed", A_LineNumber)

; A running WinEvent keeps its real thread running, as the WinEvent library's hooks keep a script running, until it
; stops. A host without a window-event source ends it Failed at once, which leaves nothing to check.
winWorker := RealThread(StartWinEvent)
winHook := Await(winWorker.Task)

if winHook.InProgress {
	Sleep 100                                   ; long enough for a worker with no root to have ended
	Assert(winWorker.IsAlive, A_LineNumber)
	winHook.Stop()
	Assert(winWorker.Terminated.Wait(5000), A_LineNumber)
	AssertEq(winHook.EndReason, "Stopped", A_LineNumber)
}

StartWinEvent() {
	hook := WinEvent("ahk_class KeysharpNoSuchWindowClass")
	hook.OnExist := (*) => 0
	hook.Start()
	return hook
}

FileAppend "pass", "*"
