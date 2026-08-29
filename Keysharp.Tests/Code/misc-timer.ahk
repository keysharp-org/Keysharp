#NoTrayIcon

#MaxThreads 2
#Include <assert>

x := 0

fo1 := TimerHandler
SetTimer(fo1, 100)

TimerHandler(*)
{
global
	x++

	if (x == 5)
	{
		SetTimer(fo1, 0)
	}
}

Sleep(1000)

AssertEq(x, 5, A_LineNumber)

x := 0
fo1 := TimerHandler2
SetTimer(fo1, 10)

TimerHandler2(*)
{
global x
	x++
	SetTimer(A_EventInfo, 0)

}

Sleep(100)

AssertEq(x, 1, A_LineNumber)

x := 0

SetTimer(TimerHandler3, -10) ; Ensure only one timer gets created because the handler is cached.
SetTimer(TimerHandler3, -20)

TimerHandler3()
{
	global x
	x++
}

Sleep(50)

AssertEq(x, 1, A_LineNumber)

x := 0
SetTimer(TimerHandler3, -1) ; Ensure the timer is called immediately if the period is 1
Sleep(-1)

AssertEq(x, 1, A_LineNumber)

x := 0, doDelayEnd := 0
; Fill max threads with TimerHandler4, and ensure TimerHandler3 is queued behind it.
SetTimer(TimerHandler3, -100)
SetTimer(TimerHandler4, -1)
Sleep(-1)

TimerHandler4() {
	global
	Sleep(120)
	doDelayEnd := A_TickCount
}

; Sleep(-1) returns only once TimerHandler4 has finished, so no time has passed since it stamped the tick.
AssertEq(A_TickCount, doDelayEnd, A_LineNumber)

; TimerHandler3 came due while TimerHandler4 held the thread, so it must be queued rather than dropped, and
; must still run exactly once. WHEN it runs is deliberately not asserted: Keysharp drains it before Sleep(-1)
; returns, while AutoHotkey v2.0.26 leaves it queued until the script sleeps again.
Sleep(300)

AssertEq(x, 1, A_LineNumber)

FileAppend "pass", "*"
