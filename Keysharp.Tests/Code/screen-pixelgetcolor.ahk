#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <screen-fixture>

fixture := ScreenTestFixture()
try {
    CoordMode("Pixel", "Screen")
    AssertEq(PixelGetColor(fixture.X + fixture.Width // 2, fixture.Y + fixture.Height // 2), "0xCC5533", A_LineNumber)
    AssertEq(PixelGetColor(fixture.X - 10, fixture.Y + 10), "0x112233", A_LineNumber)
} finally
    fixture.Window.Destroy()

FileAppend "pass", "*"
