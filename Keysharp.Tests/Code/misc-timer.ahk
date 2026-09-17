#NoTrayIcon

#MaxThreads 2
#Include <assert>

x := 0

fo1 := TimerHandler
SetTimer(fo1, 20)

TimerHandler(*)
{
global
	x++

	if (x == 5)
	{
		SetTimer(fo1, 0)
	}
}

Sleep(300) ; 15 periods: long enough to see a timer that failed to stop at 5

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
SetTimer(TimerHandler3, -30)
SetTimer(TimerHandler4, -1)
Sleep(-1)

TimerHandler4() {
	global
	Sleep(80)
	doDelayEnd := A_TickCount
}

; Sleep(-1) returns only once TimerHandler4 has finished, so only unwinding the dispatch separates that stamp
; from this line. A return that skipped the handler leaves doDelayEnd at 0, which no tolerance hides.
Assert(doDelayEnd != 0 && A_TickCount - doDelayEnd < 50, A_LineNumber)

; TimerHandler3 came due while TimerHandler4 held the thread, so it must be queued rather than dropped, and
; must still run exactly once. WHEN it runs is deliberately not asserted: Keysharp drains it before Sleep(-1)
; returns, while AutoHotkey v2.0.26 leaves it queued until the script sleeps again.
Sleep(100)

AssertEq(x, 1, A_LineNumber)

FileAppend "pass", "*"
