#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { RealThread, Await, A_RealThread, A_Thread, Lock }
#MaxThreads 256
#Include <assert>
lockit := Lock()
tharr := []
tharr.Length := 100
tot := 0

; The scoped form of Lock, which is ordinary script code now that LockRun is gone.
LockedCall(lock, callback, args*)
{
	lock.Acquire()

	try
		return callback(args*)
	finally
		lock.Release()
}

rtAddTot(o)
{
	global tot
	tot += o
}

rtfunc1(obj)
{
	LockedCall(lockit, rtAddTot, obj)
}

fo := rtfunc1

Loop 100
{
	tharr[A_Index] := RealThread(fo, A_Index)
}

Loop 100
{
	Await(tharr[A_Index].Task)
}

Loop 100
{
	tharr[A_Index] := RealThread(fo, 1)
}

Loop 100
{
	Await(tharr[A_Index].Task)
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

fo := rtfunc2

Loop 100
{
	tharr[A_Index] := RealThread(fo)
}

Loop 100
{
	; Await on the entry task both waits and yields the body's value.
	tot += Await(tharr[A_Index].Task)
}

tharr.Length := 0

AssertEq(tot, 10000, A_LineNumber)

; A named function reaches a worker as a plain reference.
rtNamed(n) => n * 2

named := RealThread(rtNamed, 21)
AssertEq(Await(named.Task), 42, A_LineNumber)
AssertEq(named.Task.Status, "Succeeded", A_LineNumber)

; ContinueWith is gone; Task.Then is the continuation, and it runs on the thread that asked for it.
chained := RealThread(rtNamed, 4)
; A property, not a bare name: assigning one inside the lambda would declare a local and leave this empty.
chainedBox := {Value: ""}
Await(chained.Task.Then(v => chainedBox.Value := v + 1))
AssertEq(chainedBox.Value, 9, A_LineNumber)

AssertEq(LockedCall(lockit, rtNamed, 3), 6, A_LineNumber)

; Task.Wait honours its timeout instead of blocking until the body finishes, and never throws.
slowWorker := RealThread(() => Sleep(2000))

Assert(!slowWorker.Task.Wait(50), A_LineNumber)
Assert(slowWorker.Task.IsPending && slowWorker.Task.Status == "Pending", A_LineNumber)
Assert(!slowWorker.Task.IsSucceeded && !slowWorker.Task.IsFailed && !slowWorker.Task.IsCanceled, A_LineNumber)
Assert(slowWorker.IsAlive, A_LineNumber)

Await(slowWorker.Task)

Assert(slowWorker.Task.IsSucceeded && slowWorker.Task.Status == "Succeeded", A_LineNumber)
Assert(!slowWorker.Task.IsPending && !slowWorker.Task.IsFailed && !slowWorker.Task.IsCanceled, A_LineNumber)

; A body error now travels on the entry task and rethrows when it is awaited.
failedWorker := RealThread(() => Throw(Error("expected worker failure")))
caught := ""

try
	Await(failedWorker.Task)
catch Error as e
	caught := e.Message

AssertEq(caught, "expected worker failure", A_LineNumber)
Assert(failedWorker.Task.IsFailed && failedWorker.Task.Status == "Failed", A_LineNumber)
AssertEq(failedWorker.Task.Error.Message, "expected worker failure", A_LineNumber)

; Starting arguments, Send, and self-identification from inside the worker.
argWorker := RealThread(RealThreadArgEntry, 21)

AssertEq(argWorker.Send(() => A_RealThread.Id), argWorker.Id, A_LineNumber)

; The entry task settles when the BODY returns, even though the worker stays up to serve its timer.
AssertEq(Await(argWorker.Task), 42, A_LineNumber)
Assert(argWorker.IsAlive, A_LineNumber)

; Post returns a Task, so queued work is observable.
AssertEq(Await(argWorker.Post(() => 7)), 7, A_LineNumber)

argWorker.Post(() => argWorker.Exit())
Await(argWorker.Terminated)

Assert(!argWorker.IsAlive, A_LineNumber)
Assert(argWorker.Task.IsSucceeded, A_LineNumber)

RealThreadArgEntry(n) {
	; Keeps the worker's event loop alive until Exit() is requested, so Post/Send have somewhere to land.
	SetTimer(() => 0, 100)
	return n * 2
}

; Thread.Kind names the launch site; a RealThread body reports RealThread, a timer Timer.
kindWorker := RealThread(() => A_Thread.Kind)

Assert(Await(kindWorker.Task) == "RealThread" && A_Thread.Kind == "Auto", A_LineNumber)

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

Await(holder.Task)

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

Await(coordWorker.Task)

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
Await(workerThread.Task)

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
