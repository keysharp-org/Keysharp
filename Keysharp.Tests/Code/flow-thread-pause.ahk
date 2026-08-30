#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { RealThread, Await, A_Thread }
#Include <assert>

; Pause(1) marks the UNDERLYING thread; it only sets a flag, and the thread observes that flag when it
; next resumes. This covers the resume point: without it the three writes below are dead stores.
global pauseOrder := ""

; The unpause has to come from outside, because timers are suspended while a thread is paused.
worker := RealThread(UnpauseMainThread)

SetTimer(PauseUnderlying, -1)

; Sleep pumps, so the timer runs here, pauses this thread, and this Sleep does not return until the
; worker's posted callback clears the flag.
Sleep 50

pauseOrder .= "resumed"

Await(worker.Terminated)

AssertEq(pauseOrder, "paused|unpaused|resumed", A_LineNumber)
Assert(!A_IsPaused && !A_Thread.Paused, A_LineNumber)

PauseUnderlying() {
	global pauseOrder
	pauseOrder .= "paused|"
	Pause(1)
}

UnpauseMainThread() {
	; Long enough that the main thread is parked at the resume point before this lands.
	Sleep 400
	RealThread.Main.Post(ClearUnderlyingPause)
	Sleep 300
}

ClearUnderlyingPause() {
	global pauseOrder
	pauseOrder .= "unpaused|"
	; This callback runs on the main thread, stacked on the paused one, so Underlying is that thread.
	A_Thread.Underlying.Paused := false
}

FileAppend "pass", "*"
