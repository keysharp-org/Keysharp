#NoTrayIcon
#Include <assert>

OnError(LogError1)
OnError(LogError2)
OnError(LogError3)

LogError1(exception, mode) {
	global x := ++x
}

LogError2(exception, mode) {
	global x := ++x
}

LogError3(exception, mode) {
	global x := ++x
	return -1
}

x := 0
WinActivate("C3D38B48-B165-4A69-9D8F-020DCD360712")

AssertEq(x, 3, A_LineNumber)

OnError(LogError1, 0)
OnError(LogError2, 0)

x := 0
WinActivate("C3D38B48-B165-4A69-9D8F-020DCD360712")

AssertEq(x, 1, A_LineNumber)

x := 0

fo1 := TimerHandler
SetTimer(fo1, 20)

TimerHandler(*)
{
global
	 x := ++x

	if (x == 1)
	{
		SetTimer(fo1, 0)
	}

	Exit()
	x := 123
}

; Wait for the first tick, then long enough for several more periods to show it turned itself off.
Loop 200
{
	if x != 0
		break

	Sleep 10
}

Sleep(100)

AssertEq(x, 1, A_LineNumber)

OnError(LogError3, 0)
FileAppend "pass", "*"

ExitApp()
