#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { RealThread, Await, A_Thread }
#Include <assert>

; Timers are held while any thread is paused, so every unpause below comes from work posted by a worker, which
; launches a new thread just as a hotkey would. The watchdog ends a run whose pause is never cleared.
global done := false
watchdogThread := RealThread(Watchdog)

; Pause(1) marks the UNDERLYING thread; it only sets a flag, and the thread observes that flag when it
; next resumes. This covers the resume point: without it the three writes below are dead stores.
global pauseOrder := ""
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

Throws(() => Pause(2), A_LineNumber, ValueError)
Throws(() => Pause("x"), A_LineNumber, ValueError)

global ticks := 0
SetTimer(CountTick, 10)

CountTick() {
	global ticks
	ticks++
}

; The auto-execute thread has nothing beneath it, as in AutoHotkey, so these change nothing and timers keep running.
Pause 1
AssertEq(A_IsPaused, 0, A_LineNumber)
Pause -1
AssertEq(A_IsPaused, 0, A_LineNumber)
ticksBefore := ticks
Sleep 100
Assert(ticks > ticksBefore, A_LineNumber)

; Once a thread has ended, its Thread object changes nothing, even when the script continues the TargetError: it
; must not pause the recycled slot, which would hold off every timer.
global endedThread := ""
SetTimer(CaptureEndedThread, -1)

while !IsObject(endedThread)
	Sleep 10

Assert(!endedThread.IsActive, A_LineNumber)
OnError(ContinueError)
endedThread.Paused := true
OnError(ContinueError, 0)
ticksBefore := ticks
Sleep 100
Assert(ticks > ticksBefore, A_LineNumber)

CaptureEndedThread() {
	global endedThread
	endedThread := A_Thread
}

ContinueError(*) => -1

; Each thread is paused independently: a thread launched over a paused one can pause itself, unpausing it leaves the
; one beneath still paused, and timers stay held until the last pause is cleared.
global nested := "", mainPausing := false, ticksPaused := 0
nestedDriver := RealThread(DriveNestedPauses)
mainPausing := true
Pause
nested .= "main-resumed"
Sleep 100
Await(nestedDriver.Terminated)

AssertEq(nested, "inner-pause(1)|unpause-inner(1,1)|inner-resumed(1)|unpause-outer(1)|main-resumed", A_LineNumber)
Assert(ticks > ticksPaused, A_LineNumber)

DriveNestedPauses() {
	while !mainPausing
		Sleep 10

	; Long enough that the main thread is waiting inside Pause before anything is posted to it.
	Sleep 100
	inner := RealThread.Main.Post(PauseInner)
	; Queued behind PauseInner, so it runs over PauseInner once PauseInner has paused itself.
	RealThread.Main.Post(UnpauseInner)
	Await(inner)
	; Timers would run many times over in this span if they were not held.
	Sleep 200
	RealThread.Main.Post(UnpauseOuter)
}

PauseInner() {
	global nested, ticksPaused
	ticksPaused := ticks
	nested .= "inner-pause(" A_IsPaused ")|"
	Pause
	nested .= "inner-resumed(" A_IsPaused ")|"
}

UnpauseInner() {
	global nested
	nested .= "unpause-inner(" A_IsPaused "," A_Thread.Underlying.Underlying.Paused ")|"
	Pause 0
}

UnpauseOuter() {
	global nested
	AssertEq(ticks, ticksPaused, A_LineNumber)
	nested .= "unpause-outer(" A_IsPaused ")|"
	Pause 0
}

; With no thread beneath but the idle one, Pause 1 pauses the script itself: timers are held, while launched threads
; still run, and the thread that paused it ends normally.
global idleTicks := 0
idleDriver := RealThread(DriveIdlePause)

DriveIdlePause() {
	; Long enough that the auto-execute thread has ended and the script is idle.
	Sleep 100
	; Returns only once IdlePause has ended, which a wait for the idle thread's pause would prevent.
	Await(RealThread.Main.Post(IdlePause))
	Sleep 200
	RealThread.Main.Post(IdleUnpause)
	Sleep 100
	RealThread.Main.Post(IdleFinish)
}

IdlePause() {
	global idleTicks
	AssertEq(A_Thread.Index, 1, A_LineNumber)
	Pause 1
	AssertEq(A_IsPaused, 1, A_LineNumber)
	idleTicks := ticks
}

IdleUnpause() {
	AssertEq(A_IsPaused, 1, A_LineNumber)
	AssertEq(ticks, idleTicks, A_LineNumber)
	Pause -1
	AssertEq(A_IsPaused, 0, A_LineNumber)
}

IdleFinish() {
	global done
	Assert(ticks > idleTicks, A_LineNumber)
	done := true
	FileAppend "pass", "*"
	ExitApp
}

Watchdog() {
	Loop 150 {
		if done
			return
		Sleep 100
	}

	; A worker's loop also ends when the script exits, so the flag is checked again.
	if !done {
		FileAppend "fail watchdog", "*"
		ExitApp
	}
}
