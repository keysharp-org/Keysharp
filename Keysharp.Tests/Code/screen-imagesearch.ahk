#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { Image }
#Include <assert>
#Include <screen-fixture>

fixture := ScreenTestFixture()
try {
    CoordMode("Pixel", "Screen")
    needle := Image.Create(8, 8, 0xCC5533)
    bitmap := needle.ToBitmap()
    WinGetPos(&left, &top, &width, &height, fixture.Window)
    AssertEq(ImageSearch(&x, &y, left, top, left + width - 1, top + height - 1, "HBITMAP:" bitmap), 1, A_LineNumber)
    Assert(Abs(x - fixture.X) <= 2 && Abs(y - fixture.Y) <= 2, A_LineNumber)
    AssertEq(PixelGetColor(x + 4, y + 4), "0xCC5533", A_LineNumber)
} finally {
    if IsSet(needle)
        needle.Dispose()
    fixture.Window.Destroy()
}

FileAppend "pass", "*"
