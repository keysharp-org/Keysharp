#ErrorStdOut
#Warn All, StdOut
#import KS { Overlay, Font, Image }
#Include <assert>

; The overlay owns the window and its borrowed Image canvas.
ov := Overlay(0, 0, 32, 16)
c := ov.Canvas
Assert(c is Object && c.Width == 32 && c.Height == 16, A_LineNumber)
Assert(c == ov.Canvas, A_LineNumber)                       ; stable while the surface is (see below)

c.FillRect(0, 0, 8, 8, "0xFF0000")
AssertEq(c.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)

; Drawing stages only: nothing reaches the screen until Present, and publishing an
; unchanged canvas must be harmless.
ov.Present()
ov.Present()
AssertEq(c.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)

; The canvas is the platform's presentable buffer, so anything that would replace or
; free those pixels is refused rather than silently detaching the overlay.
Throws(() => c.Dispose(), A_LineNumber)
Throws(() => c.__New("nonexistent.png"), A_LineNumber)
Throws(() => c.SetPixelData(Buffer(32 * 16 * 4)), A_LineNumber)
Throws(() => c.Scale(2), A_LineNumber)
Throws(() => c.Rotate(90), A_LineNumber)
Throws(() => c.Crop(0, 0, 4, 4), A_LineNumber)
Throws(() => c.Resize(8, 8), A_LineNumber)
Throws(() => c.Grayscale(), A_LineNumber)
AssertEq(c.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)       ; no refusal damaged it

; Copy escapes all of that: an ordinary image, independent and transformable.
copy := c.Copy()
AssertEq(copy.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)
copy.Grayscale()
Assert(copy.GetPixel(2, 2) != 0xFFFF0000, A_LineNumber)
AssertEq(c.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)       ; original untouched
copy.Dispose()

; A Ks.Font must measure the same as the option string it stands for, and a smaller
; font smaller — MeasureText once took these as plain strings and got it wrong.
big := Font("s24", "Arial")
viaFont := c.MeasureText("Wg", big).Height
viaString := c.MeasureText("Wg", "s24", "Arial").Height
AssertEq(viaFont, viaString, A_LineNumber)
Assert(c.MeasureText("Wg", Font("s8", "Arial")).Height < viaFont, A_LineNumber)

; Present publishes the current canvas; SetImage accepts only an independent source.
Throws(() => ov.SetImage(ov.Canvas), A_LineNumber)

; Redraw replaces the canvas, so a reference taken before it is dead afterwards.
stale := ov.Canvas
ov.Redraw((canvas) => canvas.FillRect(0, 0, 4, 4, "0x00FF00"))
Assert(stale != ov.Canvas, A_LineNumber)
Throws(() => stale.GetPixel(0, 0), A_LineNumber)
AssertEq(ov.Canvas.GetPixel(1, 1), 0xFF00FF00, A_LineNumber)

ov.Destroy()

; FromImage copies its source and uses its pixel dimensions when Width and Height are omitted.
src := Image.Create(24, 12, "0x0000FF")
fromImg := Overlay.FromImage(src)
Assert(fromImg.Canvas.Width == 24 && fromImg.Canvas.Height == 12, A_LineNumber)
AssertEq(fromImg.Canvas.GetPixel(3, 3), 0xFF0000FF, A_LineNumber)
Assert(fromImg.Canvas != src, A_LineNumber)               ; copied, not adopted
src.FillRect(0, 0, 24, 12, "0x00FF00")
AssertEq(fromImg.Canvas.GetPixel(3, 3), 0xFF0000FF, A_LineNumber)   ; and independent of it
fromImg.Destroy()

positioned := Overlay.FromImage(src, 7, 9)
Assert(positioned.X == 7 && positioned.Y == 9, A_LineNumber)
AssertEq(positioned.Canvas.GetPixel(3, 3), 0xFF00FF00, A_LineNumber)
positioned.Destroy()

; The constructor has one unambiguous shape: no arguments, or a complete rectangle.
Throws(() => Overlay(src), A_LineNumber)
Throws(() => Overlay(7, 9, 24), A_LineNumber)
named := Overlay(X: 7, Y: 9, Width: 24, Height: 12)
Assert(named.X == 7 && named.Y == 9 && named.Width == 24 && named.Height == 12, A_LineNumber)
named.Destroy()
src.Dispose()

FileAppend "pass", "*"
