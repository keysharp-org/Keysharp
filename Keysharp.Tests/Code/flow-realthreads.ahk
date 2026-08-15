#NoTrayIcon

#import KS { RealThread, LockRun, A_RealThread, A_Thread, Lock }
#MaxThreads 256
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

If tot == 5150
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
	if !tharr[A_Index].Wait()
		FileAppend "fail", "*"

	tot += tharr[A_Index].Result
}

tharr.Length := 0

If tot == 10000
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Wait must honour its timeout instead of blocking until the body finishes.
slowWorker := RealThread(() => Sleep(2000))

if !slowWorker.Wait(50) && slowWorker.Status == "Running"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

slowWorker.Wait()

if slowWorker.Status == "Done"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Starting arguments, Send, and self-identification from inside the worker.
argWorker := RealThread(RealThreadArgEntry, 21)

if argWorker.Send(() => A_RealThread.Id) == argWorker.Id
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

argWorker.Post(() => argWorker.Exit())
argWorker.Wait()

if argWorker.Result == 42
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

RealThreadArgEntry(n) {
	; Keeps the worker's event loop alive until Exit() is requested, so Post/Send have somewhere to land.
	SetTimer(() => 0, 100)
	return n * 2
}

; Thread.Kind names the launch site; a RealThread body reports RealThread, a timer Timer.
kindWorker := RealThread(() => A_Thread.Kind)
kindWorker.Wait()

if kindWorker.Result == "RealThread" && A_Thread.Kind == "Auto"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

global kindFromTimer := ""
SetTimer(CaptureTimerKind, -1)

while kindFromTimer == ""
	Sleep 10

if kindFromTimer == "Timer over Auto"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CaptureTimerKind() {
	global kindFromTimer := A_Thread.Kind " over " A_Thread.Underlying.Kind
}

; The settable modes must accept a script boolean. A bool-typed CLR setter would throw an
; InvalidCastException here that no try/catch could intercept.
A_Thread.Critical := true

if A_Thread.Critical && !A_Thread.IsInterruptible
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

A_Thread.Critical := false

if !A_Thread.Critical && A_Thread.IsInterruptible && !A_Thread.Paused
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Lock: a contended timed acquire fails while a worker holds it, and succeeds once released.
global sharedLock := Lock()

if sharedLock.Acquire()
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

sharedLock.Release()

; A released lock is immediately re-acquirable, including with a zero timeout.
if sharedLock.Acquire(0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

sharedLock.Release()

holder := RealThread(HoldSharedLock)
Sleep 300

if !sharedLock.Acquire(50)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

holder.Wait()

if sharedLock.Acquire(1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if A_CoordModeMouse = "Screen"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if ret1 = 101 && ret2 = 202 && A_CoordModeMouse = "Client"
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
		FileAppend "fail", "*"
		ExitApp()
	}

	CoordMode "Mouse", "Window"
}

SetCoordModeMouseClient() {
	if A_CoordModeMouse = "Window" {
		FileAppend "fail", "*"
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
