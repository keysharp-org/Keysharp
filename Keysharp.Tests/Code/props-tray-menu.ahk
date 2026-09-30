#NoTrayIcon
#Include <assert>
#import KS { RealThread, Await }

; The tray menu and its tooltip belong to the script, not to the icon: #NoTrayIcon withholds only the icon,
; and both stay readable and settable without one, as they are in AutoHotkey.

AssertEq(Type(A_TrayMenu), "Menu", A_LineNumber)
Assert(A_TrayMenu.Handle > 0, A_LineNumber)

; A menu with no icon is still a menu to build on.
A_TrayMenu.Add("Custom", Noop)
Assert(A_TrayMenu.Handle > 0, A_LineNumber)

A_IconTip := "custom tip"
AssertEq(A_IconTip, "custom tip", A_LineNumber)

Noop(*) {
}

; A standard item given a callback of the script's own is no longer standard, as in AutoHotkey, so Suspend leaves
; its checkmark alone.
suspendItem := A_TrayMenu.ToClr().Items.Find("&Suspend Hotkeys", true).GetValue(0)
A_TrayMenu.Add("&Suspend Hotkeys", Noop)
Suspend(1)
Assert(!suspendItem.Checked, A_LineNumber)
Suspend(0)

; The standard Pause Script item toggles the thread that was running when it was chosen, as in AutoHotkey. Chosen
; while the script is idle, it pauses the script itself, which holds off timers, and its checkmark shows that. The
; item is clicked through the backing toolkit menu, and a worker drives the steps because timers stop meanwhile.
global pauseItem := A_TrayMenu.ToClr().Items.Find("&Pause Script", true).GetValue(0)
global ticks := 0, pausedTicks := 0, done := false
Assert(!pauseItem.Checked, A_LineNumber)
SetTimer(CountTick, 10)
watchdogThread := RealThread(Watchdog)
driver := RealThread(ChoosePauseScriptTwice)

CountTick() {
	global ticks
	ticks++
}

ChoosePauseScriptTwice() {
	; Long enough that the auto-execute thread has ended and the script is idle.
	Sleep 100
	Await(RealThread.Main.Post(ChoosePauseScript))
	; Queued behind the item's own thread, so by its end the idle thread is paused and the item checked.
	Await(RealThread.Main.Post(RecordPausedTicks))
	Assert(pauseItem.Checked, A_LineNumber)
	; Timers would run many times over in this span if they were not held.
	Sleep 200
	Await(RealThread.Main.Post(ResumeFromTray))
	Sleep 200
	Assert(!pauseItem.Checked, A_LineNumber)
	RealThread.Main.Post(Finish)
}

ChoosePauseScript() {
	pauseItem.PerformClick()
}

RecordPausedTicks() {
	global pausedTicks
	AssertEq(A_IsPaused, 1, A_LineNumber)
	pausedTicks := ticks
}

ResumeFromTray() {
	AssertEq(ticks, pausedTicks, A_LineNumber)
	ChoosePauseScript()
}

Finish() {
	global done
	AssertEq(A_IsPaused, 0, A_LineNumber)
	Assert(ticks > pausedTicks, A_LineNumber)
	done := true
	FileAppend "pass", "*"
	ExitApp
}

Watchdog() {
	Loop 100 {
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
