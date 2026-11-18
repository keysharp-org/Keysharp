#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

Thread "Interrupt", 0
completed := 0
running := false
reentered := false

SetTimer(Tick, 80)
deadline := A_TickCount + 5000
while (completed < 3 && !reentered && A_TickCount < deadline)
    Sleep(10)
SetTimer(Tick, 0)

AssertEq(reentered, false, A_LineNumber)
AssertEq(completed, 3, A_LineNumber)

FileAppend "pass", "*"

Tick() {
    global completed, running, reentered

    if running {
        reentered := true
        SetTimer(, 0)
        return
    }

    running := true
    Sleep(240)
    ; An overdue timer may leave the scheduler asleep while its callback is running.
    Sleep(-1)
    completed += 1
    if (completed = 3)
        SetTimer(, 0)
    running := false
}
