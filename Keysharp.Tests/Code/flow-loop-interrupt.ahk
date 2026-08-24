#NoTrayIcon
#Include <assert>

; A Loop yields to the message queue between iterations, so timers and other threads still run while one
; is spinning. That poll lives in the loop enumerator's MoveNext, against a pseudo-thread it resolves once
; on entry -- these are the behaviours that arrangement has to preserve.

class T {
    static Fired := false
    static Ticks := 0
}

Fire() {
    T.Fired := true
}

Bump() {
    T.Ticks++
}

; An infinite Loop must not starve a timer.
SetTimer(Fire, -50)
t0 := A_TickCount
Loop {
    if (T.Fired || A_TickCount - t0 > 4000)
        break
}
Assert(T.Fired, A_LineNumber)

; Neither must a counted one.
T.Fired := false
SetTimer(Fire, -50)
t0 := A_TickCount
Loop 100000000 {
    if (T.Fired || A_TickCount - t0 > 4000)
        break
}
Assert(T.Fired, A_LineNumber)

; A periodic timer keeps firing, rather than the loop polling only once.
T.Ticks := 0
SetTimer(Bump, 20)
t0 := A_TickCount
Loop {
    if (T.Ticks >= 3 || A_TickCount - t0 > 4000)
        break
}
SetTimer(Bump, 0)
Assert(T.Ticks >= 3, A_LineNumber)

; Critical suppresses the poll, so the timer waits for the loop to finish.
T.Fired := false
SetTimer(Fire, -50)
Critical("On")
t0 := A_TickCount
Loop {
    if (A_TickCount - t0 > 400)
        break
}
ranWhileCritical := T.Fired
Critical("Off")
Assert(!ranWhileCritical, A_LineNumber)

; The poll deadline lives on the pseudo-thread, not on each loop, so nesting does not starve a timer (nor
; make the inner and outer loop poll back to back).
T.Ticks := 0
SetTimer(Bump, 20)
t0 := A_TickCount
Loop {
    Loop 50 {
    }
    if (T.Ticks >= 2 || A_TickCount - t0 > 4000)
        break
}
SetTimer(Bump, 0)
Assert(T.Ticks >= 2, A_LineNumber)

; A_Index stays writable from inside the body, which is why the loop tests it rather than its own counter.
seen := 0
Loop 10 {
    seen++
    if (A_Index = 3)
        A_Index := 8
}
AssertEq(seen, 5, A_LineNumber)

FileAppend "pass", "*"
