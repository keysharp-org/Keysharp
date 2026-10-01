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
    right := left + width - 1, bottom := top + height - 1
    AssertEq(ImageSearch(&x, &y, left, top, right, bottom, "HBITMAP:" bitmap), 1, A_LineNumber)
    Assert(Abs(x - fixture.X) <= 2 && Abs(y - fixture.Y) <= 2, A_LineNumber)
    AssertEq(PixelGetColor(x + 4, y + 4), "0xCC5533", A_LineNumber)
    AssertEq(ImageSearch(&x, &y, left, top, right, bottom, "*DirBottomRight HBITMAP:" needle.ToBitmap()), 1, A_LineNumber)
    Assert(Abs(x - (fixture.X + fixture.Width - 8)) <= 2 && Abs(y - (fixture.Y + fixture.Height - 8)) <= 2, A_LineNumber)

    ; *Trans reads hexadecimal without 0x, and a variation applies after any other option.
    framed := Image.Create(8, 8, 0xFFFFAA)
    framed.FillRect(2, 2, 4, 4, 0xCC5533)
    AssertEq(ImageSearch(&x, &y, left, top, right, bottom, "*TransFFFFAA HBITMAP:" framed.ToBitmap()), 1, A_LineNumber)
    near := Image.Create(8, 8, 0xCC5530)
    AssertEq(ImageSearch(&x, &y, left, top, right, bottom, "HBITMAP:" near.ToBitmap()), 0, A_LineNumber)
    AssertEq(ImageSearch(&x, &y, left, top, right, bottom, "*w8 *h8 *4 HBITMAP:" near.ToBitmap()), 1, A_LineNumber)
} finally {
    if IsSet(needle)
        needle.Dispose()
    fixture.Window.Destroy()
}

FileAppend "pass", "*"
