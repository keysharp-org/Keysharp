#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#import KS { Image }

; A capture of the script's own window shows that window, not what lies beneath it, and its origin maps
; the window's pixels back to the screen. The swatch is shorter than a title bar, so an origin off by the
; title bar misses it, and the window beneath has its own color, so capturing it instead cannot pass.
beneath := Gui(, "Keysharp capture beneath")
beneath.BackColor := "112233"
beneath.Show("w800 h600")
target := Gui(, "Keysharp capture target")
target.BackColor := "FFFFFF"
swatch := target.AddText("x40 y50 w100 h16 BackgroundCC5533", "")
try {
    target.Show("w320 h220")
    deadline := A_TickCount + 3000
    Loop {
        img := Image.FromWindow(target)
        WinGetPos(&swatchX, &swatchY, &swatchWidth, &swatchHeight, swatch.Hwnd)
        x := Round((swatchX + swatchWidth // 2 - img.OriginX) * img.ScaleX)
        y := Round((swatchY + swatchHeight // 2 - img.OriginY) * img.ScaleY)
        color := x >= 0 && y >= 0 && x < img.Width && y < img.Height ? img.GetPixel(x, y) & 0xFFFFFF : -1
        ; Painting follows Show, so an early capture can predate the swatch.
        if color = 0xCC5533 || A_TickCount > deadline
            break
        Sleep(50)
    }
    AssertEq(Format("0x{:06X}", color), "0xCC5533", A_LineNumber)

    WinGetPos(&frameX, &frameY, , , target)
    WinGetClientPos(&clientX, &clientY, , , target)
    Assert((img.OriginX = frameX && img.OriginY = frameY) || (img.OriginX = clientX && img.OriginY = clientY), A_LineNumber)
} finally {
    target.Destroy()
    beneath.Destroy()
}

FileAppend "pass", "*"
