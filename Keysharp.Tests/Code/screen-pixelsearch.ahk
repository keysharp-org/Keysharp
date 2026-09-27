#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <screen-fixture>

fixture := ScreenTestFixture()
try {
    CoordMode("Pixel", "Screen")
    left := fixture.X + 5, top := fixture.Y + 5
    right := fixture.X + fixture.Width - 6, bottom := fixture.Y + fixture.Height - 6
    AssertEq(PixelSearch(&x, &y, left, top, right, bottom, 0xCC5533), 1, A_LineNumber)
    AssertEq(x, left, A_LineNumber)
    AssertEq(y, top, A_LineNumber)
    AssertEq(PixelSearch(&x, &y, right, bottom, left, top, 0xCC5533), 1, A_LineNumber)
    AssertEq(x, right, A_LineNumber)
    AssertEq(y, bottom, A_LineNumber)
    AssertEq(PixelSearch(&x, &y, left, top, right, bottom, 0x112233), 0, A_LineNumber)
} finally
    fixture.Window.Destroy()

FileAppend "pass", "*"
