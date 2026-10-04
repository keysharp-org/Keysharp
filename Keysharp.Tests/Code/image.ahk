#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#import KS { Image, A_DirSeparator }
#Include <assert>

Channel(image, x, y, channel) {
    rgba := image.GetPixelData(4)
    return rgba[(y * image.Width + x) * 4 + channel + 1]
}

HasAlpha(image) {
    rgba := image.GetPixelData(4)
    loop rgba.Size // 4
        if rgba[A_Index * 4]
            return true
    return false
}

; The right and bottom edges of dark pixels on a light image.
InkExtent(image) {
    inkPixels := image.GetPixelData(1), width := image.Width, right := 0, bottom := 0
    loop inkPixels.Size
        if inkPixels[A_Index] < 128 {
            right := Max(right, Mod(A_Index - 1, width) + 1)
            bottom := Max(bottom, (A_Index - 1) // width + 1)
        }
    return {Right: right, Bottom: bottom}
}

transparent := Image.Create(8, 6)
Assert(transparent.Width == 8 && transparent.Height == 6, A_LineNumber)
AssertEq(Channel(transparent, 0, 0, 3), 0, A_LineNumber)

; ToClr() hands out the underlying toolkit bitmap with pending work materialized.
Assert(transparent.ToClr().Width == 8 && transparent.ToClr().Height == 6, A_LineNumber)

colored := Image.Create(4, 3, "0xFF445566")
AssertEq(Channel(colored, 0, 0, 0), 0x44, A_LineNumber)
AssertEq(Channel(colored, 0, 0, 1), 0x55, A_LineNumber)
AssertEq(Channel(colored, 0, 0, 2), 0x66, A_LineNumber)
AssertEq(Channel(Image.Create(4, 3, "0x80445566"), 0, 0, 3), 0x80, A_LineNumber)

; A colour string that is no name or number raises, rather than drawing black or transparent.
Throws(() => Image.Create(4, 4, "Rde"), A_LineNumber, ValueError)
badColour := Image.Create(4, 4)
Throws(() => badColour.FillRect(0, 0, 4, 4, "Rde"), A_LineNumber, ValueError)
Throws(() => badColour.Clear("Rde"), A_LineNumber, ValueError)
Throws(() => badColour.SetPixel(0, 0, "Rde"), A_LineNumber, ValueError)
Throws(() => badColour.SearchPixel("Rde"), A_LineNumber, ValueError)
Throws(() => badColour.Search(Image.Create(1, 1), trans: "Rde"), A_LineNumber, ValueError)
AssertEq(badColour.GetPixel(1, 1), 0, A_LineNumber)

; A queued Clear rewrites the image's own pixels, and a draw after it still lands on them.
cleared := Image.Create(2, 2, "Red").Clear(0x80112233)
AssertEq(cleared.GetPixel(0, 0), 0x80112233, A_LineNumber)
cleared.FillRect(1, 1, 1, 1, "Blue")
AssertEq(cleared.GetPixel(1, 1), 0xFF0000FF, A_LineNumber)
AssertEq(cleared.GetPixel(0, 1), 0x80112233, A_LineNumber)

canvas := Image.Create(12, 12)
copy := canvas.Copy()
AssertEq(canvas.FillRect(1, 1, 8, 8, "Red"), canvas, A_LineNumber)
canvas.FillEllipse(4, 4, 6, 6, "Blue")
canvas.DrawRect(0, 0, 12, 12, "Lime", 1)
AssertEq(canvas.GetPixel(6, 6), 0xFF0000FF, A_LineNumber)
AssertEq(canvas.GetPixel(0, 0), 0xFF00FF00, A_LineNumber)
AssertEq(Channel(copy, 1, 1, 3), 0, A_LineNumber)

; A translucent outline paints each pixel once, corners included, also when the bars would overlap.
frame := Image.Create(10, 8)
frame.DrawRect(0, 0, 10, 8, "0x80FF0000", 2)
for point in [[0, 0], [9, 0], [0, 7], [9, 7], [1, 1], [8, 6], [5, 0], [0, 4], [9, 4], [5, 7]]
    AssertEq(frame.GetPixel(point[1], point[2]), 0x80FF0000, A_LineNumber)
AssertEq(frame.GetPixel(5, 4), 0, A_LineNumber)
thick := Image.Create(10, 4)
thick.DrawRect(0, 0, 10, 4, "0x80FF0000", 3)
for point in [[0, 0], [9, 0], [0, 3], [9, 3], [5, 1], [5, 2]]
    AssertEq(thick.GetPixel(point[1], point[2]), 0x80FF0000, A_LineNumber)

text := Image.Create(120, 40)
text.DrawText("Hi", 2, 2, "Black", "s14", "Sans")
Assert(HasAlpha(text), A_LineNumber)
styled := Image.Create(120, 40)
styled.DrawText("Hi", 2, 2, "Black", "s14 bold italic", "Sans")
Assert(HasAlpha(styled), A_LineNumber)

; Font options are checked when DrawText is called, so a bad one neither surfaces at a later read nor discards
; the draws queued before it, and raises again on every call.
queuedText := Image.Create(40, 20)
queuedText.FillRect(0, 0, 4, 4, "Red")
Throws(() => queuedText.DrawText("Hi", 0, 0, "Black", "s12 bogus"), A_LineNumber, ValueError)
Throws(() => queuedText.DrawText("Hi", 0, 0, "Black", "cRed"), A_LineNumber, ValueError)
Throws(() => queuedText.MeasureText("Hi", "s12 bogus"), A_LineNumber, ValueError)
AssertEq(queuedText.GetPixel(1, 1), 0xFFFF0000, A_LineNumber)
Throws(() => queuedText.DrawText("Hi", 0, 0, "Black", "s12 bogus"), A_LineNumber, ValueError)

; Text is sized in draw units whatever resolution a bitmap carries, so on a copied or loaded image it matches a
; fresh canvas and stays inside MeasureText's box.
fresh := Image.Create(120, 80, "White")
fresh.DrawText("Hg", 2, 2, "Black", "s14", "Arial")
box := fresh.MeasureText("Hg", "s14", "Arial")
ink := InkExtent(fresh)
Assert(ink.Right > 2 + box.Width / 2 && ink.Right <= 2 + Ceil(box.Width), A_LineNumber)
Assert(ink.Bottom > 2 + box.Height / 2 && ink.Bottom <= 2 + Ceil(box.Height), A_LineNumber)
copied := Image.Create(120, 80, "White").Copy()
#if WINDOWS
copied.ToClr().SetResolution(192, 192)    ; as an image saved at 192 DPI loads
#endif
copied.DrawText("Hg", 2, 2, "Black", "s14", "Arial")
copiedInk := InkExtent(copied)
Assert(copiedInk.Right == ink.Right && copiedInk.Bottom == ink.Bottom, A_LineNumber)

source := Image.Create(3, 3, "0xFF112233")
target := Image.Create(8, 8)
target.DrawImage(source, 2, 1)
source.Clear("Blue")
AssertEq(target.GetPixel(3, 2), 0xFF112233, A_LineNumber)
source.Dispose()
target.FillRect(0, 0, 1, 1, "Red")
AssertEq(target.GetPixel(3, 2), 0xFF112233, A_LineNumber)

; Overlapping self-draws read a snapshot of the source pixels.
self := Image.Create(8, 4)
self.FillRect(0, 0, 2, 4, "Red")
self.FillRect(2, 0, 2, 4, "Blue")
self.DrawImage(self, 2, 0)
AssertEq(self.GetPixel(3, 2), 0xFFFF0000, A_LineNumber)
AssertEq(self.GetPixel(5, 2), 0xFF0000FF, A_LineNumber)

canvas := Image.Create(20, 10, "Black")
AssertEq(canvas.Scale(2), canvas, A_LineNumber)
Assert(canvas.Width == 40 && canvas.Height == 20, A_LineNumber)
Assert(canvas.ScaleX == 2 && canvas.ScaleY == 2, A_LineNumber)
canvas.Scale(0.5, 1)
Assert(canvas.Width == 20 && canvas.Height == 20, A_LineNumber)
Assert(canvas.ScaleX == 1 && canvas.ScaleY == 2, A_LineNumber)

canvas := Image.Create(20, 10, "Black")
canvas.Crop(5, 2, 8, 4)
Assert(canvas.Width == 8 && canvas.Height == 4, A_LineNumber)
Assert(canvas.OriginX == 5 && canvas.OriginY == 2, A_LineNumber)

scaled := Image.Create(20, 10, "Black")
scaled.Scale(2).Crop(4, 2, 6, 4)
Assert(scaled.OriginX == 2 && scaled.OriginY == 1, A_LineNumber)
scaled.Rotate(90)
Assert(scaled.OriginX == "" && scaled.ScaleX == "", A_LineNumber)
Throws(() => scaled.SetOrigin(100, 200, "bad"), A_LineNumber, TypeError)
Assert(scaled.OriginX == "" && scaled.OriginY == "", A_LineNumber)
Assert(scaled.ScaleX == "" && scaled.ScaleY == "", A_LineNumber)
scaled.SetOrigin(30, 40, 2)
Assert(scaled.OriginX == 30 && scaled.OriginY == 40, A_LineNumber)
Assert(scaled.ScaleX == 2 && scaled.ScaleY == 2, A_LineNumber)
Throws(() => scaled.SetOrigin(100, 200, 0), A_LineNumber, ValueError)
Assert(scaled.OriginX == 30 && scaled.OriginY == 40, A_LineNumber)
Assert(scaled.ScaleX == 2 && scaled.ScaleY == 2, A_LineNumber)

; Continuing a scale conversion or validation error leaves the complete mapping unchanged.
continueOriginError := (*) => -1
OnError(continueOriginError)
scaled.SetOrigin(100, 200, 3, {})
scaled.SetOrigin(100, 200, 3, -1)
OnError(continueOriginError, 0)
Assert(scaled.OriginX == 30 && scaled.OriginY == 40, A_LineNumber)
Assert(scaled.ScaleX == 2 && scaled.ScaleY == 2, A_LineNumber)
scaled.Flip()
Assert(scaled.OriginX == "" && scaled.ScaleX == 2, A_LineNumber)

original := Image.Create(20, 10, "Black")
copy := original.Copy()
copy.Crop(2, 1, 10, 5).Scale(2)
Assert(copy.Width == 20 && copy.OriginX == 2 && copy.ScaleX == 2, A_LineNumber)
Assert(original.Width == 20 && original.Height == 10, A_LineNumber)
Assert(original.OriginX == 0 && original.ScaleX == 1, A_LineNumber)

layout := Image.Create(4, 3, "0xFF000040")
layout.SetPixel(3, 2, "0xFF030240")
layoutRgba := layout.GetPixelData(4)
AssertEq(layoutRgba.Size, 48, A_LineNumber)
Assert(layoutRgba[1] == 0 && layoutRgba[2] == 0 && layoutRgba[3] == 0x40 && layoutRgba[4] == 0xFF, A_LineNumber)
Assert(layoutRgba[45] == 3 && layoutRgba[46] == 2 && layoutRgba[47] == 0x40 && layoutRgba[48] == 0xFF, A_LineNumber)
gray := layout.GetPixelData(1)
Assert(gray.Size == 12 && gray[1] == 7 && gray[12] == 9, A_LineNumber)
Throws(() => layout.GetPixelData(2), A_LineNumber)

rotated := Image.Create(20, 10, "Black")
rotated.Rotate(90)
Assert(rotated.Width == 10 && rotated.Height == 20, A_LineNumber)

flipped := Image.Create(20, 10, "Black")
flipped.SetPixel(0, 0, "Red").SetPixel(19, 0, "Blue")
flipped.Flip()
AssertEq(flipped.GetPixel(0, 0), 0xFF0000FF, A_LineNumber)
AssertEq(flipped.GetPixel(19, 0), 0xFFFF0000, A_LineNumber)

pixels := Image.Create(8, 8)
pixels.SetPixel(3, 4, 0x123456)
AssertEq(pixels.GetPixel(3, 4), 0xFF123456, A_LineNumber)
pixels.SetPixel(2, 3, "0x80112233")
AssertEq(pixels.GetPixel(2, 3), 0x80112233, A_LineNumber)
pixels.Flip()
movedPixel := pixels.SearchPixel(0x112233, variation: 4)
Assert(movedPixel.x == 5 && movedPixel.y == 3, A_LineNumber)

path := A_Temp A_DirSeparator "keysharp-image-" A_TickCount ".png"
try {
    Image.Create(20, 10, "0xFF102030").Save(path)
    Assert(FileExist(path) != "", A_LineNumber)
    loaded := Image.FromFile(path)
    Assert(loaded.Width == 20 && loaded.Height == 10, A_LineNumber)
    Assert(loaded.ScaleX == 1 && loaded.ScaleY == 1, A_LineNumber)

    wide := Image.FromFile(path, -1, 30)
    Assert(wide.Width == 60 && wide.Height == 30, A_LineNumber)
    narrow := Image.FromFile(path, 40, -1)
    Assert(narrow.Width == 40 && narrow.Height == 20, A_LineNumber)

    saved := path ".scaled.png"
    loaded.Scale(2).Save(saved)
    reloaded := Image.FromFile(saved)
    Assert(reloaded.Width == 40 && reloaded.Height == 20, A_LineNumber)
    FileDelete(saved)
} finally {
    if FileExist(path)
        FileDelete(path)
}

bitmapSource := Image.Create(16, 12, "Blue")
bitmapSource.FillRect(0, 0, 16, 4, "Red")
bitmapCopy := Image.FromBitmap(bitmapSource.ToBitmap())
Assert(bitmapCopy.Width == 16 && bitmapCopy.Height == 12, A_LineNumber)
AssertEq(bitmapCopy.GetPixel(0, 0), 0xFFFF0000, A_LineNumber)
AssertEq(bitmapCopy.GetPixel(0, 11), 0xFF0000FF, A_LineNumber)

; A bitmap handle keeps its alpha, from Image.ToBitmap as from LoadPicture.
alphaSource := Image.Create(2, 2, "Blue")
alphaSource.SetPixel(0, 0, "0xFFFF0000").SetPixel(1, 0, "0x80FF0000")
alphaCopy := Image.FromBitmap(alphaSource.ToBitmap())
AssertEq(alphaCopy.GetPixel(0, 0), 0xFFFF0000, A_LineNumber)
AssertEq(alphaCopy.GetPixel(1, 0) >> 24, 0x80, A_LineNumber)
AssertEq(alphaCopy.GetPixel(0, 1), 0xFF0000FF, A_LineNumber)

; IL_Create takes AutoHotkey's counts. With Resize false, IL_Add divides a picture across its width into
; list-sized images and returns the index of the first; a picture narrower than one image adds none.
alphaPath := A_Temp A_DirSeparator "keysharp-image-alpha-" A_TickCount ".png"
stripPath := A_Temp A_DirSeparator "keysharp-image-strip-" A_TickCount ".png"
try {
    alphaSource.Save(alphaPath)
    loadedAlpha := Image.FromBitmap(LoadPicture(alphaPath))
    AssertEq(loadedAlpha.GetPixel(0, 0), 0xFFFF0000, A_LineNumber)
    AssertEq(loadedAlpha.GetPixel(1, 0) >> 24, 0x80, A_LineNumber)
    AssertEq(loadedAlpha.GetPixel(0, 1), 0xFF0000FF, A_LineNumber)

    Image.Create(256, 1, "Red").Save(stripPath)
    small := IL_Create(10)
    large := IL_Create(10, 10, true)
    AssertEq(IL_Add(small, stripPath, 0xFFFFFF, false), 1, A_LineNumber)
    smallTiles := IL_Add(small, stripPath, 0xFFFFFF, false) - 1
    AssertEq(IL_Add(large, stripPath, 0xFFFFFF, false), 1, A_LineNumber)
    largeTiles := IL_Add(large, stripPath, 0xFFFFFF, false) - 1
    Assert(largeTiles >= 2 && smallTiles > largeTiles, A_LineNumber)
    AssertEq(IL_Add(small, alphaPath, 0xFFFFFF, false), 0, A_LineNumber)
    AssertEq(IL_Add(small, stripPath, 0xFFFFFF, true), 2 * smallTiles + 1, A_LineNumber)
    AssertEq(IL_Destroy(small), 1, A_LineNumber)
    AssertEq(IL_Destroy(small), 0, A_LineNumber)
    IL_Destroy(large)
} finally {
    for file in [alphaPath, stripPath]
        if FileExist(file)
            FileDelete(file)
}

haystack := Image.Create(20, 12, "Black")
haystack.FillRect(2, 2, 3, 3, "Red")
haystack.FillRect(10, 6, 3, 3, "Red")
needle := Image.Create(3, 3, "Red")

first := haystack.Search(needle)
Assert(first.x == 2 && first.y == 2, A_LineNumber)
last := haystack.Search(needle, Direction: "BottomRight")
Assert(last.x == 10 && last.y == 6, A_LineNumber)
wild := haystack.Search(needle, trans: "Red")
Assert(wild.x == 0 && wild.y == 0, A_LineNumber)
AssertEq(haystack.Search(Image.Create(3, 3, "Magenta")), "", A_LineNumber)
selfMatch := haystack.Search(haystack)
Assert(selfMatch.x == 0 && selfMatch.y == 0, A_LineNumber)

pixel := haystack.SearchPixel("Red")
Assert(pixel.x == 2 && pixel.y == 2 && pixel.color == 0xFFFF0000, A_LineNumber)
near := haystack.SearchPixel(0xFE0202, variation: 4)
Assert(near.x == 2 && near.y == 2, A_LineNumber)
AssertEq(haystack.SearchPixel("White"), "", A_LineNumber)
Throws(() => haystack.SearchPixel("Red", Direction: "Center"), A_LineNumber, ValueError)

directions := Map(
    "TopLeft", [0, 4, 12, 20, 24],
    "TopRight", [4, 0, 12, 24, 20],
    "BottomLeft", [20, 24, 12, 0, 4],
    "BottomRight", [24, 20, 12, 4, 0],
    "LeftTop", [0, 20, 12, 4, 24],
    "LeftBottom", [20, 0, 12, 24, 4],
    "RightTop", [4, 24, 12, 0, 20],
    "RightBottom", [24, 4, 12, 20, 0])
corners := Image.Create(5, 5, "Black")
for position in [0, 4, 12, 20, 24]
    corners.SetPixel(Mod(position, 5), position // 5, "Red")
dot := Image.Create(1, 1, "Red")
for direction, expected in directions {
    found := corners.Search(dot, Direction: direction)
    AssertEq(found.X + found.Y * 5, expected[1], A_LineNumber)
    matches := corners.SearchAll(dot, Direction: direction)
    AssertEq(matches.Length, expected.Length, A_LineNumber)
    for index, match in matches
        AssertEq(match.X + match.Y * 5, expected[index], A_LineNumber)
    if InStr(direction, "Top") == 1 || InStr(direction, "Bottom") == 1 {
        pixel := corners.SearchPixel("Red", Direction: direction)
        AssertEq(pixel.X + pixel.Y * 5, expected[1], A_LineNumber)
    }
}
center := corners.Search(dot, Direction: "Center")
Assert(center.X == 2 && center.Y == 2, A_LineNumber)
center := corners.SearchAll(dot, Direction: "Center")[1]
Assert(center.X == 2 && center.Y == 2, A_LineNumber)
AssertEq(corners.Search(dot, Direction: "bOtToMrIgHt").X, 4, A_LineNumber)
AssertEq(corners.SearchPixel("Red", Direction: "bOtToMrIgHt").Y, 4, A_LineNumber)
for invalid in [0, 1, 4, 9, 10, "", "1", "TopLef", " TopLeft "] {
    Throws(() => corners.Search(dot, Direction: invalid), A_LineNumber, ValueError)
    Throws(() => corners.SearchAll(dot, Direction: invalid), A_LineNumber, ValueError)
    Throws(() => corners.SearchPixel("Red", Direction: invalid), A_LineNumber, ValueError)
}
Throws(() => corners.Search(dot, Width: 0, Direction: "bad"), A_LineNumber, ValueError)
Throws(() => corners.SearchAll(dot, Width: 0, Direction: "bad"), A_LineNumber, ValueError)
for invalid in [0, 4, 5, "", "4", "FullContnt", "Direct3D", " FullContent "] {
    Throws(() => Image.FromWindow("ahk_id -1", invalid), A_LineNumber, ValueError)
    Throws(() => Image.FromWindow("ahk_id -1", {Mode: invalid}), A_LineNumber, ValueError)
}

all := haystack.SearchAll(needle)
AssertEq(all.Length, 2, A_LineNumber)
Assert(all[1].x == 2 && all[1].y == 2, A_LineNumber)
Assert(all[2].x == 10 && all[2].y == 6, A_LineNumber)
AssertEq(Image.Create(6, 6, "Blue").SearchAll(needle).Length, 0, A_LineNumber)

region := Image.Create(24, 16, "Black")
region.FillRect(2, 2, 3, 3, "Red").FillRect(12, 7, 3, 3, "Red")
match := region.Search(needle, 10, 5, 10, 8)
Assert(match.x == 12 && match.y == 7, A_LineNumber)
AssertEq(region.SearchAll(needle, 10, 5, 10, 8).Length, 1, A_LineNumber)
AssertEq(region.SearchPixel("Red", 10, 5, 10, 8).x, 12, A_LineNumber)

empty := Image.Create(10, 8, "Black")
black := Image.Create(2, 2, "Black")
AssertEq(empty.SearchPixel("Black", 10, 0, 4, 4), "", A_LineNumber)
AssertEq(empty.SearchPixel("Black", 0, 8, 4, 4, 255), "", A_LineNumber)
AssertEq(empty.SearchPixel("Black", 0, 0, 0, 4), "", A_LineNumber)
AssertEq(empty.Search(black, 10, 0, 4, 4), "", A_LineNumber)
AssertEq(empty.SearchAll(black, 0, 8, 4, 4).Length, 0, A_LineNumber)

rounded := Image.Create(24, 24)
rounded.FillRoundRect(0, 0, 24, 24, 8, "Red")
AssertEq(rounded.GetPixel(12, 12), 0xFFFF0000, A_LineNumber)
AssertEq(rounded.GetPixel(12, 0), 0xFFFF0000, A_LineNumber)
AssertEq(Channel(rounded, 0, 0, 3), 0, A_LineNumber)
square := Image.Create(10, 10)
square.FillRoundRect(0, 0, 10, 10, 0, "Red")
AssertEq(Channel(square, 0, 0, 3), 0xFF, A_LineNumber)

bufferSource := Image.Create(4, 3, "0xFF204060")
bufferSource.SetPixel(1, 1, "0x80AABBCC")
buffer := bufferSource.GetPixelData(4)
rebuilt := Image.FromBuffer(buffer, 4, 3, 4)
Assert(rebuilt.Width == 4 && rebuilt.Height == 3, A_LineNumber)
AssertEq(rebuilt.GetPixel(0, 0), bufferSource.GetPixel(0, 0), A_LineNumber)
AssertEq(rebuilt.GetPixel(1, 1), 0x80AABBCC, A_LineNumber)

gray := bufferSource.GetPixelData(1)
fromGray := Image.FromBuffer(gray, 4, 3, 1)
grayPixel := fromGray.GetPixel(0, 0)
AssertEq((grayPixel >> 24 & 0xFF), 0xFF, A_LineNumber)
AssertEq((grayPixel >> 16 & 0xFF), (grayPixel >> 8 & 0xFF), A_LineNumber)
AssertEq((grayPixel >> 8 & 0xFF), (grayPixel & 0xFF), A_LineNumber)
Throws(() => Image.FromBuffer(buffer, 8, 8, 4), A_LineNumber)

target := Image.Create(4, 3)
AssertEq(target.SetPixelData(buffer, 4), target, A_LineNumber)
AssertEq(target.GetPixel(1, 1), 0x80AABBCC, A_LineNumber)
Throws(() => Image.Create(8, 8).SetPixelData(buffer, 4), A_LineNumber)

; GetPixelData, SetPixelData and FromBuffer share one default, so all three compose without a bpp.
defaulted := bufferSource.GetPixelData()
AssertEq(defaulted.Size, buffer.Size, A_LineNumber)
roundTrip := Image.Create(4, 3)
roundTrip.SetPixelData(bufferSource.GetPixelData())
AssertEq(roundTrip.GetPixel(0, 0), bufferSource.GetPixel(0, 0), A_LineNumber)
AssertEq(roundTrip.GetPixel(1, 1), 0x80AABBCC, A_LineNumber)
AssertEq(Image.FromBuffer(bufferSource.GetPixelData(), 4, 3).GetPixel(1, 1), 0x80AABBCC, A_LineNumber)

view := {Ptr: buffer.Ptr, Size: buffer.Size}
fromView := Image.FromBuffer(view, 4, 3, 4)
AssertEq(fromView.GetPixel(1, 1), 0x80AABBCC, A_LineNumber)
viewTarget := Image.Create(4, 3)
viewTarget.SetPixelData(view, 4)
AssertEq(viewTarget.GetPixel(1, 1), 0x80AABBCC, A_LineNumber)
Throws(() => Image.FromBuffer({}, 4, 3, 4), A_LineNumber)

grayscale := Image.Create(4, 4, "0xFF3060A0").Grayscale()
AssertEq(Channel(grayscale, 1, 1, 0), 89, A_LineNumber)
AssertEq(Channel(grayscale, 1, 1, 0), Channel(grayscale, 1, 1, 1), A_LineNumber)
AssertEq(Channel(grayscale, 1, 1, 1), Channel(grayscale, 1, 1, 2), A_LineNumber)
AssertEq(Channel(grayscale, 1, 1, 3), 255, A_LineNumber)

alpha := Image.Create(4, 4, "0xFF112233").Alpha(0.5)
AssertEq(Channel(alpha, 0, 0, 3), 128, A_LineNumber)
AssertEq(Channel(alpha, 0, 0, 0), 0x11, A_LineNumber)

bright := Image.Create(4, 4, "0xFF204060").Brightness(1)
AssertEq(bright.GetPixel(0, 0), 0xFFFFFFFF, A_LineNumber)
contrast := Image.Create(4, 4, "0xFF204060").Contrast(-1)
AssertEq(Channel(contrast, 0, 0, 0), 128, A_LineNumber)
AssertEq(Channel(contrast, 0, 0, 1), 128, A_LineNumber)
AssertEq(Channel(contrast, 0, 0, 2), 128, A_LineNumber)

; Exact halves of the luminance round up, as AutoHotkey's Round does: these give 28.5, 21.5, 53.5 and 72.5.
for rgb, expected in Map(0x0000FA, 29, 0x0004A8, 22, 0x01551D, 54, 0x057129, 73, 0x00FF00, 150, 0xFFFFFF, 255, 0x000000, 0) {
    source := Image.Create(1, 1, rgb)
    AssertEq(NumGet(source.GetPixelData(1), 0, "UChar"), expected, A_LineNumber)
    AssertEq(source.Grayscale().GetPixel(0, 0), 0xFF000000 | expected * 0x010101, A_LineNumber)
    AssertEq(NumGet(source.GetPixelData(1), 0, "UChar"), expected, A_LineNumber)
}
AssertEq(Image.Create(1, 1, 0x204060).Brightness(0.5).GetPixel(0, 0), 0xFFA0C0E0, A_LineNumber)
AssertEq(Image.Create(1, 1, 0x204060).Brightness(-0.25).GetPixel(0, 0), 0xFF000020, A_LineNumber)
AssertEq(Image.Create(1, 1, 0x818380).Contrast(0.5).GetPixel(0, 0), 0xFF828480, A_LineNumber)
AssertEq(Image.Create(1, 1, 0x204060).Contrast(0.5).GetPixel(0, 0), 0xFF002050, A_LineNumber)
AssertEq(Channel(Image.Create(1, 1, "0x81112233").Alpha(0.5), 0, 0, 3), 64, A_LineNumber)
AssertEq(Channel(Image.Create(1, 1, "0xC3112233").Alpha(0.3), 0, 0, 3), 58, A_LineNumber)

resized := Image.Create(20, 10, "Black")
AssertEq(resized.Resize(40, 20), resized, A_LineNumber)
Assert(resized.Width == 40 && resized.Height == 20, A_LineNumber)
Assert(resized.ScaleX == 2 && resized.ScaleY == 2, A_LineNumber)
aspect := Image.Create(20, 10, "Black").Resize(-1, 10)
Assert(aspect.Width == 20 && aspect.Height == 10, A_LineNumber)
Throws(() => Image.Create(20, 10).Resize(0, 10), A_LineNumber)
Throws(() => Image.Create(20, 10).Resize(-1, -1), A_LineNumber)

disposed := Image.Create(8, 6)
disposed.Dispose()
Throws(() => disposed.Width, A_LineNumber)
Throws(() => disposed.Scale(2), A_LineNumber)

FileAppend "pass", "*"
