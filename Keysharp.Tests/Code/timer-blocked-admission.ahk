#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

Thread "Interrupt", 0
fires := 0
fn := () {
    global fires += 1
}

; Both repeating and one-shot callbacks remain due while a higher-priority thread runs.
for period in [100, -100] {
    fires := 0
    Thread "Priority", 1
    SetTimer(fn, period)
    Sleep(150)
    AssertEq(fires, 0, A_LineNumber)
    Thread "Priority", 0
    Sleep(-1)
    SetTimer(fn, 0)
    AssertEq(fires, 1, A_LineNumber)
}

; Raising the timer's priority also releases a parked callback without resetting its deadline.
fires := 0
Thread "Priority", 1
SetTimer(fn, 100)
Sleep(150)
SetTimer(fn,, 1)
Sleep(-1)
SetTimer(fn, 0)
AssertEq(fires, 1, A_LineNumber)
Thread "Priority", 0

; Timer permission blocks callbacks without preventing other script work.
fires := 0
Thread "NoTimers", true
SetTimer(fn, 100)
Sleep(150)
AssertEq(fires, 0, A_LineNumber)
Thread "NoTimers", false
Sleep(-1)
SetTimer(fn, 0)
AssertEq(fires, 1, A_LineNumber)

; Reset and cancellation still apply to a callback already waiting in the queue.
fires := 0
Thread "Priority", 1
SetTimer(fn, 100)
Sleep(150)
SetTimer(fn, -1000)
Thread "Priority", 0
Sleep(-1)
AssertEq(fires, 0, A_LineNumber)
SetTimer(fn, 0)

Thread "Priority", 1
SetTimer(fn, -1)
Sleep(-1)
SetTimer(fn, 0)
Thread "Priority", 0
Sleep(-1)
AssertEq(fires, 0, A_LineNumber)

; Completion of the higher-priority pseudo-thread releases the underlying timer.
fn2 := () {
    SetTimer(fn, 100)
    Sleep(150)
}
SetTimer(fn2, -1, 1)
Sleep(-1)
Sleep(-1)
SetTimer(fn, 0)
AssertEq(fires, 1, A_LineNumber)

FileAppend "pass", "*"
