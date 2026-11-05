#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import Ks { Timer, A_Timers, A_RealThread, RealThread, Await }
#Include <assert>

workers := []
callbackFinished := false
callbackHandle := ""
orderCallbacks := 0

continuedConstruction := {Count: 0}
constructorHandler := (_error, _mode) => (_error is Error ? (continuedConstruction.Count++, -1) : 0)
objectConstructor := GetMethod(Object, "Call")
OnError(constructorHandler)
refusedTimer := Timer()
AssertEq(refusedTimer, "", A_LineNumber)
refusedTimer := objectConstructor(Timer)
AssertEq(refusedTimer, "", A_LineNumber)
CheckContinuedConstruction21()
OnError(constructorHandler, 0)
AssertEq(continuedConstruction.Count, 3, A_LineNumber)

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
	AssertEq(A_Timers[1], handle, A_LineNumber)
	AssertEq(handle.Callback, Tick, A_LineNumber)
	AssertEq(handle.Period, 60000, A_LineNumber)
	AssertEq(handle.Priority, 3, A_LineNumber)
	AssertEq(handle.RealThread, A_RealThread, A_LineNumber)
	Assert(handle.InProgress, A_LineNumber)
	AssertEq(handle.EndReason, "", A_LineNumber)
	CheckElapsedMissing(handle)
	Assert(!HasProp(handle, "LastRun"), A_LineNumber)
	Assert(!HasMethod(handle, "Start"), A_LineNumber)
	Assert(!HasMethod(handle, "Stop"), A_LineNumber)
	Throws(() => handle.Callback := Tick, A_LineNumber)
	Throws(() => handle.Period := 10, A_LineNumber)
	Throws(() => handle.Priority := 1, A_LineNumber)
	Throws(() => handle.RealThread := A_RealThread, A_LineNumber)
	Throws(() => handle.InProgress := false, A_LineNumber)
	Throws(() => handle.EndReason := "Stopped", A_LineNumber)
	Throws(() => handle.Elapsed := 0, A_LineNumber)

	; Existing handles follow configuration changes to their active registration.
	SetTimer(Tick, -120000, -4)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(handle.Priority, -4, A_LineNumber)
	SetTimer(Tick,, 2)
	AssertEq(FindTimer(Tick, A_RealThread), handle, A_LineNumber)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(handle.Priority, 2, A_LineNumber)
	SetTimer(handle.Callback, 0)
	Assert(!handle.InProgress, A_LineNumber)
	AssertEq(handle.EndReason, "Stopped", A_LineNumber)
	AssertEq(handle.Callback, Tick, A_LineNumber)
	AssertEq(handle.Period, -120000, A_LineNumber)
	AssertEq(handle.Priority, 2, A_LineNumber)
	AssertEq(handle.RealThread, A_RealThread, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	AssertEq(snapshot.Length, 1, A_LineNumber)
	Assert(!snapshot[1].InProgress, A_LineNumber)
	CheckElapsedMissing(handle)

	; A retained description remains ended when the same callback gets another timer.
	SetTimer(Tick, 60000)
	replacement := FindTimer(Tick, A_RealThread)
	Assert(replacement != handle, A_LineNumber)
	AssertEq(FindTimer(Tick, A_RealThread), replacement, A_LineNumber)
	Assert(!handle.InProgress, A_LineNumber)
	AssertEq(handle.EndReason, "Stopped", A_LineNumber)
	Assert(replacement.InProgress, A_LineNumber)
	AssertEq(replacement.Priority, 0, A_LineNumber)
	AssertEq(handle.Period, -120000, A_LineNumber)
	SetTimer(Tick, 0)
	Assert(!replacement.InProgress, A_LineNumber)
	AssertEq(replacement.EndReason, "Stopped", A_LineNumber)

	Critical
	try {
		for period in [1, -1] {
			SetTimer(Tick, period)
			shortTimer := FindTimer(Tick, A_RealThread)
			AssertEq(shortTimer.Period, period, A_LineNumber)
			SetTimer(shortTimer.Callback, 0)
		}
	} finally
		Critical "Off"

	; Rearming from inside a callback creates a fresh observable lifetime.
	Critical
	SetTimer(CompleteAndRearm, -1)
	callbackHandle := FindTimer(CompleteAndRearm, A_RealThread)
	Critical "Off"
	WaitForCallback()
	AssertEq(callbackHandle.EndReason, "Completed", A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	endedElapsed := callbackHandle.Elapsed
	Sleep(30)
	Assert(callbackHandle.Elapsed > endedElapsed, A_LineNumber)

	callbackFinished := false
	Critical
	SetTimer(StopAndRearm, 1)
	callbackHandle := FindTimer(StopAndRearm, A_RealThread)
	Critical "Off"
	WaitForCallback()
	AssertEq(callbackHandle.EndReason, "Stopped", A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
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
	Assert(workerTimer.RealThread != mainTimer.RealThread, A_LineNumber)
	stopTask := workerTimer.RealThread.Post(SetTimer.Bind(workerTimer.Callback, 0))
	if !stopTask.Wait(5000)
		throw Error("Timer worker did not process SetTimer.")
	Await(stopTask)
	AssertEq(workerTimer.EndReason, "Stopped", A_LineNumber)
	Assert(mainTimer.InProgress, A_LineNumber)
	Assert(worker.Terminated.Wait(5000), A_LineNumber)
	AssertEq(A_Timers.Length, 1, A_LineNumber)
	SetTimer(mainTimer.Callback, 0)

	worker := RealThread(StartWorkerTimer)
	workers.Push(worker)
	if !worker.Task.Wait(5000)
		throw Error("Timer worker did not finish its entry function.")
	workerTimer := Await(worker.Task)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	Assert(worker.Exit().Wait(5000), A_LineNumber)
	Assert(!workerTimer.InProgress, A_LineNumber)
	AssertEq(workerTimer.EndReason, "Stopped", A_LineNumber)
	AssertEq(workerTimer.Callback, Tick, A_LineNumber)
	AssertEq(workerTimer.Period, 60000, A_LineNumber)
	AssertEq(workerTimer.RealThread, worker, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
} finally {
	for cleanupTimer in A_Timers
		SetTimer(cleanupTimer.Callback, 0)
	for cleanupWorker in workers
		if cleanupWorker.IsAlive
			cleanupWorker.Exit().Wait(5000)
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

WaitForCallback() {
	global callbackFinished
	_deadline := A_TickCount + 5000
	while !callbackFinished && A_TickCount < _deadline
		Sleep(10)
	if !callbackFinished
		throw Error("Timer callback did not finish.")
}

CompleteAndRearm() {
	global callbackHandle, callbackFinished
	AssertEq(A_EventInfo, callbackHandle, A_LineNumber)
	Assert(!callbackHandle.InProgress, A_LineNumber)
	AssertEq(callbackHandle.EndReason, "Completed", A_LineNumber)
	CheckElapsedPresent(callbackHandle)
	_elapsedAtStart := callbackHandle.Elapsed
	Sleep(30)
	Assert(callbackHandle.Elapsed > _elapsedAtStart, A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	SetTimer(, 60000)
	_rearmed := FindTimer(CompleteAndRearm, A_RealThread)
	Assert(_rearmed != callbackHandle, A_LineNumber)
	AssertEq(A_EventInfo, callbackHandle, A_LineNumber)
	CheckElapsedMissing(_rearmed)
	Assert(_rearmed.InProgress, A_LineNumber)
	Assert(!callbackHandle.InProgress, A_LineNumber)
	AssertEq(callbackHandle.EndReason, "Completed", A_LineNumber)
	SetTimer(, 0)
	AssertEq(_rearmed.EndReason, "Stopped", A_LineNumber)
	callbackFinished := true
}

StopAndRearm() {
	global callbackHandle, callbackFinished
	AssertEq(A_EventInfo, callbackHandle, A_LineNumber)
	Assert(callbackHandle.InProgress, A_LineNumber)
	CheckElapsedPresent(callbackHandle)
	SetTimer(, 0)
	AssertEq(callbackHandle.EndReason, "Stopped", A_LineNumber)
	AssertEq(A_Timers.Length, 0, A_LineNumber)
	SetTimer(, 60000)
	_rearmed := FindTimer(StopAndRearm, A_RealThread)
	Assert(_rearmed != callbackHandle, A_LineNumber)
	AssertEq(A_EventInfo, callbackHandle, A_LineNumber)
	CheckElapsedMissing(_rearmed)
	Assert(_rearmed.InProgress, A_LineNumber)
	Assert(!callbackHandle.InProgress, A_LineNumber)
	AssertEq(callbackHandle.EndReason, "Stopped", A_LineNumber)
	SetTimer(, 0)
	AssertEq(_rearmed.EndReason, "Stopped", A_LineNumber)
	callbackFinished := true
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

CheckContinuedConstruction21() {
	#Requires AutoHotkey v2.1-alpha
	AssertEq(Timer() ?? "missing", "missing", A_LineNumber)
}

CheckElapsedMissing21(_description) {
	#Requires AutoHotkey v2.1-alpha
	Assert(!IsSet(_description.Elapsed?), A_LineNumber)
	AssertEq(_description.Elapsed ?? "missing", "missing", A_LineNumber)
	Throws(() => _description.Elapsed, A_LineNumber, UnsetError)
}

CheckElapsedPresent(_description) {
	#Requires AutoHotkey v2.1-alpha
	_elapsed := _description.Elapsed ?? "missing"
	Assert(_elapsed is Integer && _elapsed >= 0, A_LineNumber)
}

CheckTimerOrder() {
	global orderCallbacks
	Critical
	SetTimer(NeverFirst, 60000)
	SetTimer(OrderFirst, 60000)
	SetTimer(NeverSecond, 60000)
	SetTimer(OrderSecond, 60000)
	_initial := A_Timers
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond, OrderSecond])
	_first := _initial[2]
	_second := _initial[4]
	CheckElapsedMissing(_first)
	SetTimer(OrderFirst, 1)
	Critical "Off"
	WaitForOrderCallbacks(1)
	CheckElapsedPresent(_first)
	CheckElapsedMissing(_second)
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond, OrderSecond])

	SetTimer(OrderSecond, 1)
	WaitForOrderCallbacks(2)
	CheckElapsedPresent(_second)
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond, OrderSecond])
	AssertEq(A_Timers[2], _first, A_LineNumber)
	AssertEq(A_Timers[4], _second, A_LineNumber)

	SetTimer(OrderFirst)
	SetTimer(OrderFirst,, 5)
	SetTimer(OrderFirst, 120000)
	AssertTimerOrder([NeverFirst, OrderFirst, NeverSecond, OrderSecond])
	AssertEq(A_Timers[2], _first, A_LineNumber)

	; A new lifetime follows every existing registration.
	SetTimer(NeverFirst, 0)
	SetTimer(NeverFirst, 60000)
	AssertTimerOrder([OrderFirst, NeverSecond, OrderSecond, NeverFirst])
	Assert(A_Timers[4] != _initial[1], A_LineNumber)
	AssertEq(_initial[1].EndReason, "Stopped", A_LineNumber)
	SetTimer(OrderFirst, -1)
	WaitForOrderCallbacks(3)
	AssertTimerOrder([NeverSecond, OrderSecond, NeverFirst, OrderFirst])
	Assert(A_Timers[4] != _first, A_LineNumber)
	AssertEq(_first.EndReason, "Completed", A_LineNumber)
	AssertEq(_initial[1].Callback, NeverFirst, A_LineNumber)

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

WaitForOrderCallbacks(_expected) {
	global orderCallbacks
	_deadline := A_TickCount + 5000
	while orderCallbacks < _expected && A_TickCount < _deadline
		Sleep(10)
	if orderCallbacks != _expected
		throw Error("Unexpected number of ordering callbacks.")
}

NeverFirst() {
}

NeverSecond() {
}

OrderFirst() {
	global orderCallbacks
	CheckElapsedPresent(A_EventInfo)
	SetTimer(, 60000)
	orderCallbacks += 1
}

OrderSecond() {
	global orderCallbacks
	SetTimer(, 60000)
	orderCallbacks += 1
}
