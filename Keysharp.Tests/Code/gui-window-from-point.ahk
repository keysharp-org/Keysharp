#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { Image, WinFromPoint }
#Include <assert>

Target := Gui("+AlwaysOnTop", "Window point target")
Cover := Gui("+AlwaysOnTop", "Window point cover")
try {
    Target.Show("x40 y80 w240 h180 NA")
    WinGetClientPos(&X, &Y, &Width, &Height, "ahk_id " Target.Hwnd)
    AssertEq(WindowAt(X + Width // 2, Y + Height // 2, Target.Hwnd), Target.Hwnd, A_LineNumber)
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

    Cover.Show("x" (X + 30) " y" (Y + 30) " w100 h70 NA")
    WinGetClientPos(&CoverX, &CoverY, &CoverWidth, &CoverHeight, "ahk_id " Cover.Hwnd)
    PointX := CoverX + CoverWidth // 2
    PointY := CoverY + CoverHeight // 2
    AssertEq(WindowAt(PointX, PointY, Cover.Hwnd), Cover.Hwnd, A_LineNumber)

    Cover.Hide()
    AssertEq(WindowAt(PointX, PointY, Target.Hwnd), Target.Hwnd, A_LineNumber)
} finally {
    Cover.Destroy()
    Target.Destroy()
}

FileAppend "pass", "*"

WindowAt(X, Y, Expected) {
    Loop 100 {
        PointHandle := WinFromPoint(X, Y)
        if PointHandle = Expected
            return PointHandle
        Sleep(10)
    }
    return PointHandle
}
