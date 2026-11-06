#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Import Ks { RealThread }

global Finished := false
WatchdogThread := RealThread(Watchdog)
SetTitleMatchMode(3)
MonitorGetWorkArea(MonitorGetPrimary(), &Left, &Top, &Right, &Bottom)

try {
    CheckPosition(Left + 40, Top + 60)
    CheckPosition(Left + 100, Top + 120)
    CheckPosition(Left + 60)
    CheckPosition(, Top + 100)
    CheckPosition()
} finally {
    Finished := true
}

FileAppend "pass", "*"
ExitApp

CheckPosition(X?, Y?) {
    Probe := {Title: "Keysharp InputBox position " ProcessExist() " " A_TickCount,
        HasX: IsSet(X), HasY: IsSet(Y), X: IsSet(X) ? X : 0, Y: IsSet(Y) ? Y : 0,
        Handle: 0, SeenAt: 0, InitialAt: 0, MovedAt: 0, LastMatchedAt: 0,
        MoveMatched: false, Width: 0, Height: 0, Error: ""}
    Probe.Criterion := Probe.Title " ahk_pid " ProcessExist()
    Tick := (*) => Observe(Probe)
    SetTimer(Tick, 25)
    try {
        Options := "W260 H140 T3"
        if IsSet(X)
            Options .= " X" X
        if IsSet(Y)
            Options .= " Y" Y
        Result := InputBox("This dialog closes automatically.", Probe.Title, Options, "kept value")
    } finally {
        SetTimer(Tick, 0)
    }
    AssertEq(Probe.Error, "", A_LineNumber)
    Assert(Probe.Handle != 0, A_LineNumber)
    Assert(Probe.InitialAt != 0, A_LineNumber)
    Assert(Probe.MoveMatched, A_LineNumber)
    Assert(Probe.LastMatchedAt - Probe.MovedAt >= 500, A_LineNumber)
    AssertEq(Result.Result, "Timeout", A_LineNumber)
    AssertEq(Result.Value, "kept value", A_LineNumber)

    ; Compositor close notifications can arrive after the modal task completes.
    ClosedAt := A_TickCount
    Loop {
        RemainingTitle := WinExist(Probe.Criterion)
        RemainingHandle := Probe.Handle ? WinExist(Probe.Handle) : 0
        if (!RemainingTitle && !RemainingHandle) || A_TickCount - ClosedAt >= 1000
            break
        Sleep(25)
    }
    AssertEq(RemainingTitle, 0, A_LineNumber)
    AssertEq(RemainingHandle, 0, A_LineNumber)
}

Observe(Probe) {
    if Probe.Error != ""
        return
    try {
        Handle := WinExist(Probe.Criterion)
        if !Handle
            return
        if !Probe.Handle
            Probe.Handle := Handle
        WinGetPos(&X, &Y, &Width, &Height, Handle)
        if Width <= 0 || Height <= 0
            return
        if !Probe.SeenAt
            Probe.SeenAt := A_TickCount
        if !Probe.InitialAt {
            if (Probe.HasX && X != Probe.X) || (Probe.HasY && Y != Probe.Y)
                return
            if A_TickCount - Probe.SeenAt < 200
                return
            Probe.Width := Width, Probe.Height := Height
            Probe.InitialX := X, Probe.InitialY := Y
            Probe.InitialAt := A_TickCount
        } else if !Probe.MovedAt {
            ; Omitted axes keep the compositor's placement, including after delayed correlation.
            if X != Probe.InitialX || Y != Probe.InitialY || Width != Probe.Width || Height != Probe.Height {
                Probe.Error := "Initial placement changed from " Probe.InitialX "," Probe.InitialY " to " X "," Y
                return
            }
            if A_TickCount - Probe.InitialAt < 1200
                return
            Probe.TargetX := X + 55, Probe.TargetY := Y + 35
            Probe.MovedAt := A_TickCount
            WinMove(Probe.TargetX, Probe.TargetY, , , Handle)
        } else {
            AtTarget := X = Probe.TargetX && Y = Probe.TargetY && Width = Probe.Width && Height = Probe.Height
            if Probe.MoveMatched && !AtTarget
                Probe.Error := "Moved placement changed from " Probe.TargetX "," Probe.TargetY " to " X "," Y
            if AtTarget {
                Probe.MoveMatched := true
                Probe.LastMatchedAt := A_TickCount
            }
        }
    } catch Any as Failure {
        Probe.Error := Failure.Message
    }
}

Watchdog() {
    Loop 300 {
        if Finished
            return
        Sleep(100)
    }
    if !Finished {
        FileAppend "fail InputBox positioning watchdog`n", "*"
        ExitApp(1)
    }
}
