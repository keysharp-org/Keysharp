#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import Ks { Timer, A_Timers, A_RealThread, RealThread, Await }
#Include <assert>

Thread "Interrupt", 0
workers := []
finishedCases := 0
innerCalls := 0
savedAutoInfo := A_EventInfo
autoInfo := {Source: "auto"}
A_EventInfo := autoInfo

; Construction continued past its error hands back no half-built description.
continuedConstruction := {Count: 0}
constructorHandler := (_error, _mode) => (_error is Error ? (continuedConstruction.Count++, -1) : 0)
objectConstructor := GetMethod(Object, "Call")
OnError(constructorHandler)
refusedTimer := objectConstructor(Timer)
OnError(constructorHandler, 0)
AssertEq(refusedTimer, "", A_LineNumber)
AssertEq(continuedConstruction.Count, 1, A_LineNumber)

try {
	Throws(() => Timer(), A_LineNumber)
	Throws(() => objectConstructor(Timer), A_LineNumber, ValueError)
	Assert(A_Timers is Array, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)

	SetTimer(Tick, 60000, 3)
	snapshot := A_Timers
	handle := FindTimer(Tick, A_RealThread)
	Assert(handle is Timer, A_LineNumber)
	AssertEq(snapshot[1], handle, A_LineNumber)
	AssertEq(handle.Callback, Tick, A_LineNumber)
	AssertEq(handle.Period, 60000, A_LineNumber)
	AssertEq(handle.Priority, 3, A_LineNumber)
	AssertEq(handle.RealThread, A_RealThread, A_LineNumber)
	Assert(handle.InProgress, A_LineNumber)
	AssertEq(handle.EndReason, "", A_LineNumber)
	CheckElapsedMissing(handle)
	Throws(() => handle.Period := 10, A_LineNumber)

	; Existing handles follow configuration changes to their active registration.
	SetTimer(Tick, -120000, -4)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(handle.Priority, -4, A_LineNumber)
	SetTimer(Tick,, 2)
	SetTimer(Tick)
	AssertEq(FindTimer(Tick, A_RealThread), handle, A_LineNumber)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(handle.Priority, 2, A_LineNumber)
	SetTimer(handle.Callback, 0)
	Assert(!handle.InProgress, A_LineNumber)
	AssertEq(handle.EndReason, "Stopped", A_LineNumber)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	AssertEq(snapshot.Length, 1, A_LineNumber)

	; A retained description remains ended when the same callback gets another timer.
	SetTimer(Tick, 60000)
	replacement := FindTimer(Tick, A_RealThread)
	Assert(replacement != handle, A_LineNumber)
	AssertEq(handle.EndReason, "Stopped", A_LineNumber)
	Assert(replacement.InProgress, A_LineNumber)
	AssertEq(replacement.Priority, 0, A_LineNumber)
	SetTimer(Tick, 0)

	CheckTimerOrder()

	; Each real thread sees only its own registration of the same callback.
	SetTimer(Tick, 60000)
	mainTimer := FindTimer(Tick, A_RealThread)
	worker := RealThread(StartWorkerTimer)
	workers.Push(worker)
	if !worker.Task.Wait(5000)
		throw Error("Timer worker did not finish its entry function.")
	workerTimer := Await(worker.Task)
	AssertEq(A_Timers.Length, 1, A_LineNumber)
	AssertEq(A_Timers[1].RealThread, A_RealThread, A_LineNumber)
	AssertEq(workerTimer.Callback, mainTimer.Callback, A_LineNumber)
	AssertEq(workerTimer.RealThread, worker, A_LineNumber)
	stopTask := workerTimer.RealThread.Post(SetTimer.Bind(workerTimer.Callback, 0))
	if !stopTask.Wait(5000)
		throw Error("Timer worker did not process SetTimer.")
	Await(stopTask)
	AssertEq(workerTimer.EndReason, "Stopped", A_LineNumber)
	Assert(mainTimer.InProgress, A_LineNumber)
	Assert(worker.Terminated.Wait(5000), A_LineNumber)
	SetTimer(mainTimer.Callback, 0)

	; Owner shutdown stops the timers of that real thread.
	worker := RealThread(StartWorkerTimer)
	workers.Push(worker)
	if !worker.Task.Wait(5000)
		throw Error("Timer worker did not finish its entry function.")
	workerTimer := Await(worker.Task)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	Assert(worker.Exit().Wait(5000), A_LineNumber)
	Assert(!workerTimer.InProgress, A_LineNumber)
	AssertEq(workerTimer.EndReason, "Stopped", A_LineNumber)

	; A_EventInfo describes the timer lifetime which launched the current thread.
	SetTimer(OneShotInfo, -1, 2)
	WaitForCases(2)
	SetTimer(RepeatingInfo, 1)
	WaitForCases(3)
	SetTimer(RearmBeforeRead, -1, 6)
	WaitForCases(4)
	SetTimer(OverwrittenBeforeRead, -1)
	WaitForCases(5)
	SetTimer(OuterInfo, 1)
	WaitForCases(6)
	AssertEq(A_EventInfo, autoInfo, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
} finally {
	for cleanupTimer in A_Timers
		SetTimer(cleanupTimer.Callback, 0)
	for cleanupWorker in workers
		if cleanupWorker.IsAlive
			cleanupWorker.Exit().Wait(5000)
	A_EventInfo := savedAutoInfo
}

FileAppend "pass", "*"

Tick() {
}

FindTimer(_callback, _owner) {
	for _timer in A_Timers
		if _timer.Callback == _callback && _timer.RealThread == _owner
			return _timer
	throw Error("Expected timer is absent from A_Timers.")
}

WaitForCases(_expected) {
	global finishedCases
	_deadline := A_TickCount + 5000
	while finishedCases < _expected && A_TickCount < _deadline
		Sleep(10)
	if finishedCases != _expected
		throw Error("Timer callback did not finish.")
}

StartWorkerTimer() {
	SetTimer(Tick, 60000)
	AssertEq(A_Timers.Length, 1, A_LineNumber)
	AssertEq(A_Timers[1].RealThread, A_RealThread, A_LineNumber)
	return FindTimer(Tick, A_RealThread)
}

CheckElapsedMissing(_description) {
	#Requires AutoHotkey v2.0
	AssertEq(_description.Elapsed, "", A_LineNumber)
	CheckElapsedMissing21(_description)
}

CheckElapsedMissing21(_description) {
	#Requires AutoHotkey v2.1-alpha
	AssertEq(_description.Elapsed ?? "missing", "missing", A_LineNumber)
}

CheckTimerOrder() {
	Critical
	SetTimer(NeverFirst, 60000)
	SetTimer(OrderFirst, 60000)
	SetTimer(NeverSecond, 60000)
	_initial := A_Timers
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond])
	_first := _initial[2]
	SetTimer(OrderFirst, 1)
	Critical "Off"

	; A run and an update from its callback keep the registration's place.
	WaitForCases(1)
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond])
	AssertEq(A_Timers[2], _first, A_LineNumber)

	; A new lifetime follows every existing registration.
	SetTimer(NeverFirst, 0)
	SetTimer(NeverFirst, 60000)
	AssertTimerOrder([OrderFirst, NeverSecond, NeverFirst])
	AssertEq(_initial[1].EndReason, "Stopped", A_LineNumber)

	for _description in A_Timers
		SetTimer(_description.Callback, 0)
	_elapsedAtStop := _first.Elapsed
	Sleep(30)
	Assert(_first.Elapsed > _elapsedAtStop, A_LineNumber)
}

AssertTimerOrder(_expected) {
	_actual := A_Timers
	AssertEq(_actual.Length, _expected.Length, A_LineNumber)
	for _index, _callback in _expected
		AssertEq(_actual[_index].Callback, _callback, A_LineNumber)
}

NeverFirst() {
}

NeverSecond() {
}

OrderFirst() {
	global finishedCases
	SetTimer(, 60000)
	finishedCases += 1
}

InfoFromHelper() => A_EventInfo

OneShotInfo() {
	global finishedCases
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Callback, OneShotInfo, A_LineNumber)
	AssertEq(_info.Period, -1, A_LineNumber)
	AssertEq(_info.Priority, 2, A_LineNumber)
	Assert(!_info.InProgress, A_LineNumber)
	AssertEq(_info.EndReason, "Completed", A_LineNumber)
	Assert(_info.Elapsed is Integer && _info.Elapsed >= 0, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	AssertEq(InfoFromHelper(), _info, A_LineNumber)
	A_EventInfo := "script value"
	AssertEq(InfoFromHelper(), "script value", A_LineNumber)
	finishedCases += 1
}

RepeatingInfo() {
	global finishedCases
	_info := A_EventInfo
	Assert(_info.InProgress, A_LineNumber)
	AssertEq(A_Timers[1], _info, A_LineNumber)
	SetTimer(, 0)
	AssertEq(_info.EndReason, "Stopped", A_LineNumber)
	; Rearming starts a new lifetime, while A_EventInfo keeps the one that launched this thread.
	SetTimer(, 60000)
	Assert(A_Timers[1] != _info, A_LineNumber)
	AssertEq(A_EventInfo, _info, A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

RearmBeforeRead() {
	global finishedCases
	; A disabled timer takes the defaults for whatever SetTimer omits.
	SetTimer(,, 7)
	AssertEq(A_Timers[1].Period, 250, A_LineNumber)
	AssertEq(A_Timers[1].Priority, 7, A_LineNumber)
	SetTimer(, 0)
	SetTimer(, 60000)
	AssertEq(A_Timers[1].Priority, 0, A_LineNumber)
	SetTimer(, 0)
	SetTimer()
	AssertEq(A_Timers[1].Period, 250, A_LineNumber)
	; The first read still describes the run which launched this callback.
	_info := A_EventInfo
	Assert(_info is Timer, A_LineNumber)
	AssertEq(_info.Period, -1, A_LineNumber)
	AssertEq(_info.Priority, 6, A_LineNumber)
	AssertEq(_info.EndReason, "Completed", A_LineNumber)
	SetTimer(, 0)
	finishedCases += 1
}

OverwrittenBeforeRead() {
	global finishedCases
	A_EventInfo := "replacement before first read"
	AssertEq(InfoFromHelper(), "replacement before first read", A_LineNumber)
	finishedCases += 1
}

OuterInfo() {
	global finishedCases
	SetTimer(, 60000)
	; The first interruption comes before this thread reads A_EventInfo, the second after.
	SetTimer(InnerInfo, -1)
	WaitForInner(1)
	_outer := A_EventInfo
	AssertEq(_outer.Callback, OuterInfo, A_LineNumber)
	SetTimer(InnerInfo, -1)
	WaitForInner(2)
	AssertEq(A_EventInfo, _outer, A_LineNumber)
	SetTimer(, 0)
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
	AssertEq(A_EventInfo.Callback, InnerInfo, A_LineNumber)
	A_EventInfo := "inner value"
	innerCalls += 1
}
