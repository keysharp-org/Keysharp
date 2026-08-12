#ErrorStdOut
#Warn All, StdOut
#Import "Ks" { Image }

Check(condition) {
    static number := 0
    number++
    FileAppend(condition ? "pass" : "fail-" number, "*")
}

Throws(callback) {
    try
        callback()
    catch
        return true
    return false
}

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

transparent := Image.Create(8, 6)
Check(transparent.Width == 8 && transparent.Height == 6)
Check(Channel(transparent, 0, 0, 3) == 0)

colored := Image.Create(4, 3, "0xFF445566")
Check(Channel(colored, 0, 0, 0) == 0x44)
Check(Channel(colored, 0, 0, 1) == 0x55)
Check(Channel(colored, 0, 0, 2) == 0x66)
Check(Channel(Image.Create(4, 3, "0x80445566"), 0, 0, 3) == 0x80)

canvas := Image.Create(12, 12)
copy := canvas.Copy()
Check(canvas.FillRect(1, 1, 8, 8, "Red") == canvas)
canvas.FillEllipse(4, 4, 6, 6, "Blue")
canvas.DrawRect(0, 0, 12, 12, "Lime", 1)
Check(canvas.GetPixel(6, 6) == 0xFF0000FF)
Check(canvas.GetPixel(0, 0) == 0xFF00FF00)
Check(Channel(copy, 1, 1, 3) == 0)

text := Image.Create(120, 40)
text.DrawText("Hi", 2, 2, "Black", "s14", "Sans")
Check(HasAlpha(text))
styled := Image.Create(120, 40)
styled.DrawText("Hi", 2, 2, "Black", "s14 bold italic", "Sans")
Check(HasAlpha(styled))

source := Image.Create(3, 3, "0xFF112233")
target := Image.Create(8, 8)
target.DrawImage(source, 2, 1)
Check(target.GetPixel(3, 2) == 0xFF112233)

canvas := Image.Create(20, 10, "Black")
Check(canvas.Scale(2) == canvas)
Check(canvas.Width == 40 && canvas.Height == 20)
Check(canvas.ScaleX == 2 && canvas.ScaleY == 2)
canvas.Scale(0.5, 1)
Check(canvas.Width == 20 && canvas.Height == 20)
Check(canvas.ScaleX == 1 && canvas.ScaleY == 2)

canvas := Image.Create(20, 10, "Black")
canvas.Crop(5, 2, 8, 4)
Check(canvas.Width == 8 && canvas.Height == 4)
Check(canvas.OriginX == 5 && canvas.OriginY == 2)

scaled := Image.Create(20, 10, "Black")
scaled.Scale(2).Crop(4, 2, 6, 4)
Check(scaled.OriginX == 2 && scaled.OriginY == 1)
scaled.Rotate(90)
Check(scaled.OriginX == "" && scaled.ScaleX == "")
scaled.SetOrigin(30, 40, 2)
Check(scaled.OriginX == 30 && scaled.OriginY == 40)
Check(scaled.ScaleX == 2 && scaled.ScaleY == 2)
scaled.Flip()
Check(scaled.OriginX == "" && scaled.ScaleX == 2)

original := Image.Create(20, 10, "Black")
copy := original.Copy()
copy.Crop(2, 1, 10, 5).Scale(2)
Check(copy.Width == 20 && copy.OriginX == 2 && copy.ScaleX == 2)
Check(original.Width == 20 && original.Height == 10)
Check(original.OriginX == 0 && original.ScaleX == 1)

layout := Image.Create(4, 3, "0xFF000040")
layout.SetPixel(3, 2, "0xFF030240")
layoutRgba := layout.GetPixelData(4)
Check(layoutRgba.Size == 48)
Check(layoutRgba[1] == 0 && layoutRgba[2] == 0 && layoutRgba[3] == 0x40 && layoutRgba[4] == 0xFF)
Check(layoutRgba[45] == 3 && layoutRgba[46] == 2 && layoutRgba[47] == 0x40 && layoutRgba[48] == 0xFF)
gray := layout.GetPixelData(1)
Check(gray.Size == 12 && gray[1] == 7 && gray[12] == 9)
Check(Throws(() => layout.GetPixelData(2)))

rotated := Image.Create(20, 10, "Black")
rotated.Rotate(90)
Check(rotated.Width == 10 && rotated.Height == 20)

flipped := Image.Create(20, 10, "Black")
flipped.SetPixel(0, 0, "Red").SetPixel(19, 0, "Blue")
flipped.Flip()
Check(flipped.GetPixel(0, 0) == 0xFF0000FF)
Check(flipped.GetPixel(19, 0) == 0xFFFF0000)

pixels := Image.Create(8, 8)
pixels.SetPixel(3, 4, 0x123456)
Check(pixels.GetPixel(3, 4) == 0xFF123456)
pixels.SetPixel(2, 3, "0x80112233")
Check(pixels.GetPixel(2, 3) == 0x80112233)
pixels.Flip()
movedPixel := pixels.SearchPixel(0x112233, variation: 4)
Check(movedPixel.x == 5 && movedPixel.y == 3)

path := A_Temp "\keysharp-image-" A_TickCount ".png"
try {
    Image.Create(20, 10, "0xFF102030").Save(path)
    Check(FileExist(path) != "")
    loaded := Image.FromFile(path)
    Check(loaded.Width == 20 && loaded.Height == 10)
    Check(loaded.ScaleX == 1 && loaded.ScaleY == 1)

    wide := Image.FromFile(path, -1, 30)
    Check(wide.Width == 60 && wide.Height == 30)
    narrow := Image.FromFile(path, 40, -1)
    Check(narrow.Width == 40 && narrow.Height == 20)

    saved := path ".scaled.png"
    loaded.Scale(2).Save(saved)
    reloaded := Image.FromFile(saved)
    Check(reloaded.Width == 40 && reloaded.Height == 20)
    FileDelete(saved)
} finally {
    if FileExist(path)
        FileDelete(path)
}

bitmapSource := Image.Create(16, 12, "Blue")
bitmapCopy := Image.FromBitmap(bitmapSource.ToBitmap())
Check(bitmapCopy.Width == 16 && bitmapCopy.Height == 12)

haystack := Image.Create(20, 12, "Black")
haystack.FillRect(2, 2, 3, 3, "Red")
haystack.FillRect(10, 6, 3, 3, "Red")
needle := Image.Create(3, 3, "Red")

first := haystack.Search(needle)
Check(first.x == 2 && first.y == 2)
last := haystack.Search(needle, direction: 4)
Check(last.x == 10 && last.y == 6)
wild := haystack.Search(needle, trans: "Red")
Check(wild.x == 0 && wild.y == 0)
Check(haystack.Search(Image.Create(3, 3, "Magenta")) == "")

pixel := haystack.SearchPixel("Red")
Check(pixel.x == 2 && pixel.y == 2 && pixel.color == 0xFFFF0000)
near := haystack.SearchPixel(0xFE0202, variation: 4)
Check(near.x == 2 && near.y == 2)
Check(haystack.SearchPixel("White") == "")
Check(Throws(() => haystack.SearchPixel("Red", direction: 5)))

all := haystack.SearchAll(needle)
Check(all.Length == 2)
Check(all[1].x == 2 && all[1].y == 2)
Check(all[2].x == 10 && all[2].y == 6)
Check(Image.Create(6, 6, "Blue").SearchAll(needle).Length == 0)

region := Image.Create(24, 16, "Black")
region.FillRect(2, 2, 3, 3, "Red").FillRect(12, 7, 3, 3, "Red")
match := region.Search(needle, 10, 5, 10, 8)
Check(match.x == 12 && match.y == 7)
Check(region.SearchAll(needle, 10, 5, 10, 8).Length == 1)
Check(region.SearchPixel("Red", 10, 5, 10, 8).x == 12)

empty := Image.Create(10, 8, "Black")
black := Image.Create(2, 2, "Black")
Check(empty.SearchPixel("Black", 10, 0, 4, 4) == "")
Check(empty.SearchPixel("Black", 0, 8, 4, 4, 255) == "")
Check(empty.SearchPixel("Black", 0, 0, 0, 4) == "")
Check(empty.Search(black, 10, 0, 4, 4) == "")
Check(empty.SearchAll(black, 0, 8, 4, 4).Length == 0)

rounded := Image.Create(24, 24)
rounded.FillRoundRect(0, 0, 24, 24, 8, "Red")
Check(rounded.GetPixel(12, 12) == 0xFFFF0000)
Check(rounded.GetPixel(12, 0) == 0xFFFF0000)
Check(Channel(rounded, 0, 0, 3) == 0)
square := Image.Create(10, 10)
square.FillRoundRect(0, 0, 10, 10, 0, "Red")
Check(Channel(square, 0, 0, 3) == 0xFF)

bufferSource := Image.Create(4, 3, "0xFF204060")
bufferSource.SetPixel(1, 1, "0x80AABBCC")
buffer := bufferSource.GetPixelData(4)
rebuilt := Image.FromBuffer(buffer, 4, 3, 4)
Check(rebuilt.Width == 4 && rebuilt.Height == 3)
Check(rebuilt.GetPixel(0, 0) == bufferSource.GetPixel(0, 0))
Check(rebuilt.GetPixel(1, 1) == 0x80AABBCC)

gray := bufferSource.GetPixelData(1)
fromGray := Image.FromBuffer(gray, 4, 3, 1)
grayPixel := fromGray.GetPixel(0, 0)
Check((grayPixel >> 24 & 0xFF) == 0xFF)
Check((grayPixel >> 16 & 0xFF) == (grayPixel >> 8 & 0xFF))
Check((grayPixel >> 8 & 0xFF) == (grayPixel & 0xFF))
Check(Throws(() => Image.FromBuffer(buffer, 8, 8, 4)))

target := Image.Create(4, 3)
Check(target.SetPixelData(buffer, 4) == target)
Check(target.GetPixel(1, 1) == 0x80AABBCC)
Check(Throws(() => Image.Create(8, 8).SetPixelData(buffer, 4)))

view := {Ptr: buffer.Ptr, Size: buffer.Size}
fromView := Image.FromBuffer(view, 4, 3, 4)
Check(fromView.GetPixel(1, 1) == 0x80AABBCC)
viewTarget := Image.Create(4, 3)
viewTarget.SetPixelData(view, 4)
Check(viewTarget.GetPixel(1, 1) == 0x80AABBCC)
Check(Throws(() => Image.FromBuffer({}, 4, 3, 4)))

grayscale := Image.Create(4, 4, "0xFF3060A0").Grayscale()
Check(Channel(grayscale, 1, 1, 0) == 89)
Check(Channel(grayscale, 1, 1, 0) == Channel(grayscale, 1, 1, 1))
Check(Channel(grayscale, 1, 1, 1) == Channel(grayscale, 1, 1, 2))
Check(Channel(grayscale, 1, 1, 3) == 255)

alpha := Image.Create(4, 4, "0xFF112233").Alpha(0.5)
Check(Channel(alpha, 0, 0, 3) == 128)
Check(Channel(alpha, 0, 0, 0) == 0x11)

bright := Image.Create(4, 4, "0xFF204060").Brightness(1)
Check(bright.GetPixel(0, 0) == 0xFFFFFFFF)
contrast := Image.Create(4, 4, "0xFF204060").Contrast(-1)
Check(Channel(contrast, 0, 0, 0) == 128)
Check(Channel(contrast, 0, 0, 1) == 128)
Check(Channel(contrast, 0, 0, 2) == 128)

resized := Image.Create(20, 10, "Black")
Check(resized.Resize(40, 20) == resized)
Check(resized.Width == 40 && resized.Height == 20)
Check(resized.ScaleX == 2 && resized.ScaleY == 2)
aspect := Image.Create(20, 10, "Black").Resize(-1, 10)
Check(aspect.Width == 20 && aspect.Height == 10)
Check(Throws(() => Image.Create(20, 10).Resize(0, 10)))
Check(Throws(() => Image.Create(20, 10).Resize(-1, -1)))

disposed := Image.Create(8, 6)
disposed.Dispose()
Check(Throws(() => disposed.Width))
Check(Throws(() => disposed.Scale(2)))
