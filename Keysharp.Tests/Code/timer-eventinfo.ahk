#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import Ks { Timer, A_Timers, A_RealThread }
#Include <assert>

Thread "Interrupt", 0
finishedCases := 0
innerCalls := 0
savedAutoInfo := A_EventInfo
autoInfo := {Source: "auto"}
A_EventInfo := autoInfo

try {
	SetTimer(OneShotInfo, -1, 2)
	WaitForCases(1)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(RepeatingInfo, 1)
	WaitForCases(2)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(StoppedBeforeRead, 1, 3)
	WaitForCases(3)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(RearmedBeforeRead, -1, 6)
	WaitForCases(4)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(OverwrittenBeforeRead, -1)
	WaitForCases(5)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(OuterInfo, 1)
	WaitForCases(6)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(BareResetInfo, -1, 5)
	WaitForCases(7)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)

	SetTimer(PeriodOnlyRearmInfo, -1, 6)
	WaitForCases(8)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
} finally {
	for cleanupInfo in A_Timers
		SetTimer(cleanupInfo.Callback, 0)
	A_EventInfo := savedAutoInfo
}

FileAppend "pass", "*"

WaitForCases(_expected) {
	global finishedCases
	_deadline := A_TickCount + 5000
	while finishedCases < _expected && A_TickCount < _deadline
		Sleep(10)
	if finishedCases != _expected
		throw Error("Timer event information callback did not finish.")
}

InfoFromHelper() => A_EventInfo

OneShotInfo() {
	global finishedCases
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, OneShotInfo, A_LineNumber)
	AssertEq(_info.Period, -1, A_LineNumber)
	AssertEq(_info.Priority, 2, A_LineNumber)
	AssertEq(_info.RealThread, A_RealThread, A_LineNumber)
	Assert(!_info.InProgress, A_LineNumber)
	AssertEq(_info.EndReason, "Completed", A_LineNumber)
	Assert(_info.Elapsed is Integer && _info.Elapsed >= 0, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	AssertEq(InfoFromHelper(), _info, A_LineNumber)
	Throws(() => _info.Period := 25, A_LineNumber)
	A_EventInfo := "script value"
	AssertEq(A_EventInfo, "script value", A_LineNumber)
	AssertEq(InfoFromHelper(), "script value", A_LineNumber)
	finishedCases += 1
}

RepeatingInfo() {
	global finishedCases
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, RepeatingInfo, A_LineNumber)
	AssertEq(_info.Period, 1, A_LineNumber)
	Assert(_info.InProgress, A_LineNumber)
	AssertEq(_info.EndReason, "", A_LineNumber)
	_active := A_Timers
	AssertEq(_active.Length, 1, A_LineNumber)
	AssertEq(_active[1], _info, A_LineNumber)
	AssertEq(A_Timers[1], _info, A_LineNumber)
	SetTimer(, 60000, 4)
	AssertEq(_info.Period, 60000, A_LineNumber)
	AssertEq(_info.Priority, 4, A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	AssertEq(A_Timers[1], _info, A_LineNumber)
	SetTimer(, 0)
	Assert(!_info.InProgress, A_LineNumber)
	AssertEq(_info.EndReason, "Stopped", A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	finishedCases += 1
}

StoppedBeforeRead() {
	global finishedCases
	; The first read still describes the run which launched this callback.
	SetTimer(, 0)
	SetTimer(, 60000, 8)
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, StoppedBeforeRead, A_LineNumber)
	AssertEq(_info.Period, 1, A_LineNumber)
	AssertEq(_info.Priority, 3, A_LineNumber)
	AssertEq(_info.EndReason, "Stopped", A_LineNumber)
	Assert(!_info.InProgress, A_LineNumber)
	Assert(_info.Elapsed is Integer && _info.Elapsed >= 0, A_LineNumber)
	_replacement := A_Timers[1]
	Assert(_replacement != _info, A_LineNumber)
	Assert(_replacement.InProgress, A_LineNumber)
	AssertEq(_replacement.Period, 60000, A_LineNumber)
	AssertEq(_replacement.Priority, 8, A_LineNumber)
	AssertEq(_replacement.Callback, _info.Callback, A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

RearmedBeforeRead() {
	global finishedCases
	SetTimer(,, 7)
	AssertEq(A_Timers.Length, 1, A_LineNumber)
	_replacement := A_Timers[1]
	AssertEq(_replacement.Period, 250, A_LineNumber)
	AssertEq(_replacement.Priority, 7, A_LineNumber)
	SetTimer(, 60000)
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, RearmedBeforeRead, A_LineNumber)
	AssertEq(_info.Period, -1, A_LineNumber)
	AssertEq(_info.Priority, 6, A_LineNumber)
	AssertEq(_info.EndReason, "Completed", A_LineNumber)
	Assert(!_info.InProgress, A_LineNumber)
	Assert(_info.Elapsed is Integer && _info.Elapsed >= 0, A_LineNumber)
	AssertEq(A_Timers[1], _replacement, A_LineNumber)
	Assert(_replacement != _info, A_LineNumber)
	Assert(_replacement.InProgress, A_LineNumber)
	AssertEq(_replacement.Period, 60000, A_LineNumber)
	AssertEq(_replacement.Priority, 7, A_LineNumber)
	AssertEq(_replacement.Callback, _info.Callback, A_LineNumber)
	AssertEq(InfoFromHelper(), _info, A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

BareResetInfo() {
	global finishedCases
	_original := A_EventInfo
	SetTimer()
	_replacement := A_Timers[1]
	Assert(_replacement != _original, A_LineNumber)
	Assert(_replacement.InProgress, A_LineNumber)
	AssertEq(_replacement.Period, 250, A_LineNumber)
	AssertEq(_replacement.Priority, 0, A_LineNumber)
	AssertEq(_original.EndReason, "Completed", A_LineNumber)
	AssertEq(_original.Period, -1, A_LineNumber)
	AssertEq(_original.Priority, 5, A_LineNumber)
	AssertEq(A_EventInfo, _original, A_LineNumber)

	SetTimer(, 60000, 8)
	SetTimer(, 0)
	SetTimer()
	_reset := A_Timers[1]
	Assert(_reset != _replacement, A_LineNumber)
	AssertEq(_reset.Period, 250, A_LineNumber)
	AssertEq(_reset.Priority, 0, A_LineNumber)
	AssertEq(_replacement.EndReason, "Stopped", A_LineNumber)
	AssertEq(_replacement.Period, 60000, A_LineNumber)
	AssertEq(_replacement.Priority, 8, A_LineNumber)
	AssertEq(A_EventInfo, _original, A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

PeriodOnlyRearmInfo() {
	global finishedCases
	_original := A_EventInfo
	SetTimer(, 60000)
	_replacement := A_Timers[1]
	Assert(_replacement != _original, A_LineNumber)
	AssertEq(_replacement.Period, 60000, A_LineNumber)
	AssertEq(_replacement.Priority, 0, A_LineNumber)
	AssertEq(_original.Priority, 6, A_LineNumber)
	AssertEq(_original.EndReason, "Completed", A_LineNumber)

	SetTimer(,, 9)
	SetTimer(, 0)
	SetTimer(, 120000)
	_reset := A_Timers[1]
	Assert(_reset != _replacement, A_LineNumber)
	AssertEq(_reset.Period, 120000, A_LineNumber)
	AssertEq(_reset.Priority, 0, A_LineNumber)
	AssertEq(_replacement.Priority, 9, A_LineNumber)
	AssertEq(_replacement.EndReason, "Stopped", A_LineNumber)
	AssertEq(A_EventInfo, _original, A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

OverwrittenBeforeRead() {
	global finishedCases
	A_EventInfo := "replacement before first read"
	AssertEq(A_EventInfo, "replacement before first read", A_LineNumber)
	AssertEq(InfoFromHelper(), "replacement before first read", A_LineNumber)
	finishedCases += 1
}

OuterInfo() {
	global finishedCases
	SetTimer(, 60000)
	SetTimer(InnerInfo, -1)
	WaitForInner(1)
	_outer := A_EventInfo
	Assert(_outer is Timer, A_LineNumber)
	AssertEq(_outer.Callback, OuterInfo, A_LineNumber)
	AssertEq(_outer.Period, 60000, A_LineNumber)
	AssertEq(InfoFromHelper(), _outer, A_LineNumber)
	SetTimer(InnerInfo, -1)
	WaitForInner(2)
	AssertEq(A_EventInfo, _outer, A_LineNumber)
	AssertEq(InfoFromHelper(), _outer, A_LineNumber)
	SetTimer(, 0)
	AssertEq(_outer.EndReason, "Stopped", A_LineNumber)
	finishedCases += 1
}

WaitForInner(_expected) {
	global innerCalls
	_deadline := A_TickCount + 5000
	while innerCalls < _expected && A_TickCount < _deadline
		Sleep(10)
	if innerCalls != _expected
		throw Error("Nested timer did not interrupt its outer callback.")
}

InnerInfo() {
	global innerCalls
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, InnerInfo, A_LineNumber)
	AssertEq(_info.EndReason, "Completed", A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	AssertEq(InfoFromHelper(), _info, A_LineNumber)
	A_EventInfo := "inner value"
	AssertEq(InfoFromHelper(), "inner value", A_LineNumber)
	innerCalls += 1
}
