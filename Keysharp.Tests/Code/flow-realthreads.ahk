#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { RealThread, LockRun, A_RealThread, A_Thread, Lock }
#MaxThreads 256
#Include <assert>
lockit := ""
tharr := []
tharr.Length := 100
tot := 0

rtAddTot(o)
{
	global tot
	tot += o
}

rtfunc1(obj)
{
	LockRun(lockit, (o) => rtAddTot(o), obj)
}

fo := Func("rtfunc1")

Loop 100
{
	tharr[A_Index] := RealThread(fo, A_Index).ContinueWith(fo, 1)
}

Loop 100
{
	tharr[A_Index].Wait()
}

tharr.Length := 0

AssertEq(tot, 5150, A_LineNumber)

tharr := []
tharr.Length := 100
tot := 0

rtSumTot()
{
	ct := 0

	Loop 100
	{
		ct++
	}

	return ct
}

rtfunc2()
{
	return rtSumTot()
}

fo := Func("rtfunc2")

Loop 100
{
	tharr[A_Index] := RealThread(fo)
}

Loop 100
{
	; Wait reports completion; the body's value is read from Result.
	Assert(!(!tharr[A_Index].Wait()), A_LineNumber)

	tot += tharr[A_Index].Result
}

tharr.Length := 0

AssertEq(tot, 10000, A_LineNumber)

; Wait must honour its timeout instead of blocking until the body finishes.
slowWorker := RealThread(() => Sleep(2000))

Assert(!slowWorker.Wait(50) && slowWorker.Active && !slowWorker.Succeeded
	&& !slowWorker.Failed && !slowWorker.Canceled, A_LineNumber)

slowWorker.Wait()

Assert(!slowWorker.Active && slowWorker.Succeeded && !slowWorker.Failed && !slowWorker.Canceled, A_LineNumber)

; A body error is reported where it occurs and leaves a failed worker outcome.
suppressExpected := (args*) => -1
OnError(suppressExpected)
failedWorker := RealThread(() => Throw(Error("expected worker failure")))
failedWorker.Wait()
OnError(suppressExpected, 0)
Assert(!failedWorker.Active && !failedWorker.Succeeded && failedWorker.Failed && !failedWorker.Canceled, A_LineNumber)

; Starting arguments, Send, and self-identification from inside the worker.
argWorker := RealThread(RealThreadArgEntry, 21)

AssertEq(argWorker.Send(() => A_RealThread.Id), argWorker.Id, A_LineNumber)

argWorker.Post(() => argWorker.Exit())
argWorker.Wait()

AssertEq(argWorker.Result, 42, A_LineNumber)
Assert(argWorker.Succeeded, A_LineNumber)

RealThreadArgEntry(n) {
	; Keeps the worker's event loop alive until Exit() is requested, so Post/Send have somewhere to land.
	SetTimer(() => 0, 100)
	return n * 2
}

; Thread.Kind names the launch site; a RealThread body reports RealThread, a timer Timer.
kindWorker := RealThread(() => A_Thread.Kind)
kindWorker.Wait()

Assert(kindWorker.Result == "RealThread" && A_Thread.Kind == "Auto", A_LineNumber)

global kindFromTimer := ""
SetTimer(CaptureTimerKind, -1)

while kindFromTimer == ""
	Sleep 10

AssertEq(kindFromTimer, "Timer over Auto", A_LineNumber)

CaptureTimerKind() {
	global kindFromTimer := A_Thread.Kind " over " A_Thread.Underlying.Kind
}

; The settable modes must accept a script boolean. A bool-typed CLR setter would throw an
; InvalidCastException here that no try/catch could intercept.
A_Thread.Critical := true

Assert(A_Thread.Critical && !A_Thread.IsInterruptible, A_LineNumber)

A_Thread.Critical := false

Assert(!A_Thread.Critical && A_Thread.IsInterruptible && !A_Thread.Paused, A_LineNumber)

; Lock: a contended timed acquire fails while a worker holds it, and succeeds once released.
global sharedLock := Lock()

Assert(sharedLock.Acquire(), A_LineNumber)

sharedLock.Release()

; A released lock is immediately re-acquirable, including with a zero timeout.
Assert(sharedLock.Acquire(0), A_LineNumber)

sharedLock.Release()

holder := RealThread(HoldSharedLock)
Sleep 300

Assert(!sharedLock.Acquire(50), A_LineNumber)

holder.Wait()

Assert(sharedLock.Acquire(1000), A_LineNumber)

sharedLock.Release()

HoldSharedLock() {
	sharedLock.Acquire()
	Sleep 600
	sharedLock.Release()
}

CoordMode "Mouse", "Screen"

coordWorker := RealThread(RealThreadEntry)

cb2 := CallbackCreate(SetCoordModeMouseClient)
Loop 10000 {
	DllCall(cb2)
}

coordWorker.Wait()

Assert(A_CoordModeMouse = "Screen", A_LineNumber)

workerReady := false
workerStop := false
workerCallback := 0
workerThread := RealThread(WorkerCallbackOwner)

while !workerReady
	Sleep 10

CoordMode "Mouse", "Client"
ret1 := DllCall(workerCallback)
ret2 := DllCall(workerCallback)
workerStop := true
workerThread.Wait()

Assert(ret1 = 101 && ret2 = 202 && A_CoordModeMouse = "Client", A_LineNumber)

RealThreadEntry() {
	CoordMode "Mouse", "Screen"
	cb1 := CallbackCreate(SetCoordModeMouseWindow)

	Loop 10000 {
		DllCall(cb1)
	}

	CallbackFree(cb1)
}

SetCoordModeMouseWindow() {
	if A_CoordModeMouse = "Client" {
		Assert(false, A_LineNumber)
		ExitApp()
	}

	CoordMode "Mouse", "Window"
}

SetCoordModeMouseClient() {
	if A_CoordModeMouse = "Window" {
		Assert(false, A_LineNumber)
		ExitApp()
	}

	CoordMode "Mouse", "Client"
}

WorkerCallbackOwner() {
	global workerReady, workerStop, workerCallback

	CoordMode "Mouse", "Screen"
	workerCallback := CallbackCreate(CheckWorkerCoordModeAffinity, "Fast")
	workerReady := true

	while !workerStop
		Sleep 10

	CallbackFree(workerCallback)
}

CheckWorkerCoordModeAffinity() {
	static callCount := 0
	callCount++

	if callCount = 1 {
		if A_CoordModeMouse != "Screen"
			return -1

		CoordMode "Mouse", "Window"
		return 101
	}
	else if callCount = 2 {
		if A_CoordModeMouse != "Window"
			return -2

		return 202
	}

	return -3
}

FileAppend "pass", "*"
