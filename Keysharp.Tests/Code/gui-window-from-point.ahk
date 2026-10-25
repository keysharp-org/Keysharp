#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Image, WinFromPoint, WinEvent }
#Include <assert>

Target := Gui("+AlwaysOnTop", "Window point target")
Cover := Gui("+AlwaysOnTop", "Window point cover")
#if OSX
TargetHandle := Target.Hwnd
Assert(TargetHandle > 0 && TargetHandle <= 0xFFFFFFFF, A_LineNumber)
AssertEq(WinExist(TargetHandle), TargetHandle, A_LineNumber)
AssertEq(GuiFromHwnd(TargetHandle), Target, A_LineNumber)
Cover.Opt("+Owner" TargetHandle)
Events := []
Hook := WinEvent(TargetHandle)
Hook.OnActive := (Hook, Hwnd, Time) => Events.Push(Hwnd)
Hook.Start()
#endif
try {
    Target.Show("x40 y80 w240 h180")
    AssertEq(WinWaitActive(Target, , 2), Target.Hwnd, A_LineNumber)
    AssertEq(WindowAtCenter(Target), Target.Hwnd, A_LineNumber)
    WinGetClientPos(&X, &Y, &Width, &Height, "ahk_id " Target.Hwnd)
    Assert(Target.PixelScale > 0, A_LineNumber)
    Canvas := Image.Create(240, 180, , Target.PixelScale)
    AssertEq(Canvas.Width, Round(240 * Target.PixelScale), A_LineNumber)
    AssertEq(Canvas.Height, Round(180 * Target.PixelScale), A_LineNumber)
    Canvas.Dispose()
#if WINDOWS
    Target.Opt("-DPIScale")
    AssertEq(Target.PixelScale, 1, A_LineNumber)
    Target.Opt("+DPIScale")
#endif

    Found := WinFromPoint(X + Width // 2, Y + Height // 2)
    WinGetClientPos(&FoundX, &FoundY, &FoundWidth, &FoundHeight, "ahk_id " Found)
    AssertEq(FoundX, X, A_LineNumber)
    AssertEq(FoundY, Y, A_LineNumber)
    AssertEq(FoundWidth, Width, A_LineNumber)
    AssertEq(FoundHeight, Height, A_LineNumber)

    Cover.Show("x" (X + 30) " y" (Y + 30) " w100 h70")
    AssertEq(WinWaitActive(Cover, , 2), Cover.Hwnd, A_LineNumber)
    AssertEq(WindowAtCenter(Cover), Cover.Hwnd, A_LineNumber)
    WinGetClientPos(&CoverX, &CoverY, &CoverWidth, &CoverHeight, "ahk_id " Cover.Hwnd)
    WinGetClientPos(&X, &Y, &Width, &Height, "ahk_id " Target.Hwnd)

    ; A compositor can place the windows independently, so use their actual overlap.
    OverlapLeft := Max(X, CoverX), OverlapTop := Max(Y, CoverY)
    OverlapRight := Min(X + Width, CoverX + CoverWidth)
    OverlapBottom := Min(Y + Height, CoverY + CoverHeight)
    PointX := X + Width // 2, PointY := Y + Height // 2
    if OverlapLeft < OverlapRight && OverlapTop < OverlapBottom {
        PointX := OverlapLeft + (OverlapRight - OverlapLeft) // 2
        PointY := OverlapTop + (OverlapBottom - OverlapTop) // 2
        AssertEq(WindowAt(PointX, PointY, Cover.Hwnd), Cover.Hwnd, A_LineNumber)
    }

    Cover.Hide()
    WinActivate(Target)
    AssertEq(WinWaitActive(Target, , 2), Target.Hwnd, A_LineNumber)
    WinGetClientPos(&X, &Y, &Width, &Height, "ahk_id " Target.Hwnd)
    ; Hiding a tiled window can also change the remaining window's placement.
    if PointX < X || PointX >= X + Width || PointY < Y || PointY >= Y + Height
        PointX := X + Width // 2, PointY := Y + Height // 2
    AssertEq(WindowAt(PointX, PointY, Target.Hwnd), Target.Hwnd, A_LineNumber)
#if OSX
    AssertEq(Target.Hwnd, TargetHandle, A_LineNumber)
    AssertEq(WinGetID(Target.Title), TargetHandle, A_LineNumber)
    AssertEq(WinGetList(Target.Title)[1], TargetHandle, A_LineNumber)
    WinActivate(TargetHandle)
    AssertEq(WinWaitActive(TargetHandle, , 2), TargetHandle, A_LineNumber)
    AssertEq(WinActive(Target), TargetHandle, A_LineNumber)
    AssertEq(WinActive("ahk_id " TargetHandle), TargetHandle, A_LineNumber)
    AssertEq(WinActive(Target.Title), TargetHandle, A_LineNumber)
    AssertEq(WinActive("A"), TargetHandle, A_LineNumber)
    WinExist(Target)
    AssertEq(WinActive(), TargetHandle, A_LineNumber)
    Loop 100 {
        if Events.Length
            break
        Sleep(10)
    }
    Assert(Events.Length > 0, A_LineNumber)
    for EventHandle in Events
        AssertEq(EventHandle, TargetHandle, A_LineNumber)

    Target.Hide()
    AssertEq(Target.Hwnd, TargetHandle, A_LineNumber)
    AssertEq(WinActive(TargetHandle), 0, A_LineNumber)
    AssertEq(WinExist(TargetHandle), TargetHandle, A_LineNumber)
    AssertEq(WinExist(Target.Title), 0, A_LineNumber)
    DetectHiddenWindows(true)
    AssertEq(WinExist(Target.Title), TargetHandle, A_LineNumber)
    DetectHiddenWindows(false)
    WinShow(TargetHandle)
    WinActivate(TargetHandle)
    AssertEq(WinWaitActive(TargetHandle, , 2), TargetHandle, A_LineNumber)
    WinHide(TargetHandle)
    AssertEq(WinExist(TargetHandle), TargetHandle, A_LineNumber)
    AssertEq(WinExist(Target.Title), 0, A_LineNumber)
    WinShow(TargetHandle)
    WinMinimize(TargetHandle)
    AssertEq(Target.Hwnd, TargetHandle, A_LineNumber)
    AssertEq(WinGetMinMax(TargetHandle), -1, A_LineNumber)
    WinRestore(TargetHandle)
    AssertEq(Target.Hwnd, TargetHandle, A_LineNumber)
    AssertEq(WinGetMinMax(TargetHandle), 0, A_LineNumber)
#endif
} finally {
#if OSX
    Hook.Stop()
#endif
    Cover.Destroy()
    Target.Destroy()
}

FileAppend "pass", "*"

WindowAtCenter(Window) {
    ; Wayland placement can change while the shown window is being correlated.
    Loop 100 {
        WinGetClientPos(&ClientX, &ClientY, &ClientWidth, &ClientHeight, "ahk_id " Window.Hwnd)
        PointHandle := WinFromPoint(ClientX + ClientWidth // 2, ClientY + ClientHeight // 2)
        if PointHandle = Window.Hwnd
            return PointHandle
        Sleep(10)
    }
    return PointHandle
}

WindowAt(X, Y, Expected) {
    Loop 100 {
        PointHandle := WinFromPoint(X, Y)
        if PointHandle = Expected
            return PointHandle
        Sleep(10)
    }
    return PointHandle
}
