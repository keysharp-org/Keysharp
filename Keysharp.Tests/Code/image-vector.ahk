#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#Import Ks { Image }
#Include <assert>

Channel(ImageValue, X, Y, Index) {
	Pixel := ImageValue.GetPixel(X, Y)
	return Pixel >> [16, 8, 0, 24][Index + 1] & 0xFF
}

HasAlpha(ImageValue) {
    Data := ImageValue.GetPixelData(4)
    loop Data.Size // 4
        if Data[A_Index * 4]
            return true
    return false
}

Identity := Image.Create(8, 8).Transform
Assert(Identity.ScaleX == 1 && Identity.ScaleY == 1, A_LineNumber)
Assert(Identity.OffsetX == 0 && Identity.OffsetY == 0, A_LineNumber)
Assert(Identity.SkewX == 0 && Identity.SkewY == 0, A_LineNumber)

Picture := Image.Create(80, 60)
Update := {ScaleX: 2, OffsetX: 7}
Picture.Transform := Update
Update.ScaleX := 9
Readback := Picture.Transform
Assert(Readback.ScaleX == 2 && Readback.ScaleY == 1 && Readback.OffsetX == 7, A_LineNumber)
Readback.OffsetX := 99
AssertEq(Picture.Transform.OffsetX, 7, A_LineNumber)
Picture.Transform := {OffsetY: 5}
Assert(Picture.Transform.ScaleX == 2 && Picture.Transform.OffsetX == 7 && Picture.Transform.OffsetY == 5, A_LineNumber)

TransformImage := Image.Create(24, 10)
TransformImage.Transform := {}
AssertEq(TransformImage.Transform.ScaleX, 1, A_LineNumber)
TransformImage.Transform := {offsetx: 2}
AssertEq(TransformImage.Transform.OffsetX, 2, A_LineNumber)
PriorTransform := TransformImage.Transform
Throws(() => (TransformImage.Transform := {ScaleX: 0}), A_LineNumber, ValueError)
AssertEq(TransformImage.Transform.OffsetX, PriorTransform.OffsetX, A_LineNumber)
AssertEq(TransformImage.Transform.ScaleX, PriorTransform.ScaleX, A_LineNumber)
Throws(() => (TransformImage.Transform := {Unknown: 1}), A_LineNumber, ValueError)
Throws(() => (TransformImage.Transform := []), A_LineNumber, TypeError)

Captured := Image.Create(24, 8)
Captured.Transform := {OffsetX: 2}
Captured.FillRect(0, 0, 4, 4, "Red")
Captured.Transform := {OffsetX: 12}
Captured.FillRect(0, 0, 4, 4, "Blue")
AssertEq(Captured.GetPixel(3, 2), 0xFFFF0000, A_LineNumber)
AssertEq(Captured.GetPixel(13, 2), 0xFF0000FF, A_LineNumber)

Rotated := Image.Create(12, 8)
Rotated.Transform := {ScaleX: 0, ScaleY: 0, SkewX: -1, SkewY: 1, OffsetX: 10}
Rotated.FillRect(1, 1, 3, 3, "Red")
AssertEq(Rotated.GetPixel(7, 2), 0xFFFF0000, A_LineNumber)

Triangle := Image.Path().MoveTo(2, 2).LineTo(18, 2).LineTo(10, 18).Close()
AssertEq(Picture.FillPath(Triangle, "Red"), Picture, A_LineNumber)
AssertEq(Picture.DrawPath(Triangle, "White", 2), Picture, A_LineNumber)
AssertEq(Picture.GetPixel(27, 15), 0xFFFF0000, A_LineNumber)

Helpers := Image.Path()
Helpers.AddRect(2, 2, 12, 10)
Helpers.AddRoundRect(18, 2, 12, 10, 4)
Helpers.AddEllipse(34, 2, 12, 10)
Points := [{X: 52, Y: 2}, {X: 64, Y: 2}, {X: 58, Y: 12}]
Helpers.AddPolygon(Points)
Points[1].X := 0
Canvas := Image.Create(70, 16).FillPath(Helpers, "Lime")
AssertEq(Canvas.GetPixel(5, 5), 0xFF00FF00, A_LineNumber)
AssertEq(Canvas.GetPixel(24, 7), 0xFF00FF00, A_LineNumber)
AssertEq(Canvas.GetPixel(40, 7), 0xFF00FF00, A_LineNumber)
AssertEq(Canvas.GetPixel(58, 6), 0xFF00FF00, A_LineNumber)

Curve := Image.Path().ArcTo(2, 2, 20, 20, 0, 180)
CurveImage := Image.Create(24, 24)
AssertEq(CurveImage.DrawPath(Curve, "Blue", 3), CurveImage, A_LineNumber)
Assert(HasAlpha(CurveImage), A_LineNumber)

Cubic := Image.Path().MoveTo(2, 20).CubicTo(2, 2, 22, 2, 22, 20)
CubicImage := Image.Create(24, 24).DrawPath(Cubic, "White", 3)
Assert(Channel(CubicImage, 12, 7, 3) > 0, A_LineNumber)

EvenOdd := Image.Path().AddRect(2, 2, 24, 24).AddRect(8, 8, 12, 12)
NonZero := Image.Path("NonZero").AddRect(2, 2, 24, 24).AddRect(8, 8, 12, 12)
EvenImage := Image.Create(28, 28).FillPath(EvenOdd, "Red")
NonZeroImage := Image.Create(28, 28).FillPath(NonZero, "Red")
AssertEq(Channel(EvenImage, 14, 14, 3), 0, A_LineNumber)
AssertEq(NonZeroImage.GetPixel(14, 14), 0xFFFF0000, A_LineNumber)

Unit := Image.Path().AddRect(0, 0, 5, 5)
Combined := Image.Path().AddPath(Unit).AddPath(Unit, {OffsetX: 10})
Unit.Clear()
CombinedImage := Image.Create(20, 8).FillPath(Combined, "Blue")
AssertEq(CombinedImage.GetPixel(2, 2), 0xFF0000FF, A_LineNumber)
AssertEq(CombinedImage.GetPixel(12, 2), 0xFF0000FF, A_LineNumber)

SelfPath := Image.Path().AddRect(0, 0, 5, 5)
SelfPath.AddPath(SelfPath, {OffsetX: 10})
SelfImage := Image.Create(18, 8).FillPath(SelfPath, "Red")
AssertEq(SelfImage.GetPixel(2, 2), 0xFFFF0000, A_LineNumber)
AssertEq(SelfImage.GetPixel(12, 2), 0xFFFF0000, A_LineNumber)

OriginalPath := Image.Path().AddRect(0, 0, 5, 5)
ClonedPath := OriginalPath.Clone().AddRect(10, 0, 5, 5)
OriginalImage := Image.Create(18, 8).FillPath(OriginalPath, "Lime")
CloneImage := Image.Create(18, 8).FillPath(ClonedPath, "Lime")
AssertEq(Channel(OriginalImage, 12, 2, 3), 0, A_LineNumber)
AssertEq(CloneImage.GetPixel(12, 2), 0xFF00FF00, A_LineNumber)

QueuedPath := Image.Path().AddRect(1, 1, 8, 8)
Queued := Image.Create(12, 12).FillPath(QueuedPath, "Yellow")
QueuedPath.Clear().AddRect(20, 20, 2, 2)
AssertEq(Queued.GetPixel(4, 4), 0xFFFFFF00, A_LineNumber)

Clipped := Image.Create(30, 20)
Clipped.Clip(Image.Path().AddRect(2, 2, 20, 16))
Clipped.Clip(Image.Path().AddEllipse(8, 2, 16, 16))
Clipped.FillRect(0, 0, 30, 20, "Red")
AssertEq(Channel(Clipped, 4, 10, 3), 0, A_LineNumber)
AssertEq(Clipped.GetPixel(12, 10), 0xFFFF0000, A_LineNumber)
Clipped.Clip().FillRect(0, 0, 2, 2, "Blue")
AssertEq(Clipped.GetPixel(0, 0), 0xFF0000FF, A_LineNumber)

EmptyClip := Image.Create(8, 8)
EmptyClip.Clip(Image.Path()).FillRect(0, 0, 8, 8, "Red")
AssertEq(Channel(EmptyClip, 4, 4, 3), 0, A_LineNumber)

ClipRing := Image.Path().AddRect(1, 1, 18, 18).AddRect(6, 6, 8, 8)
RingImage := Image.Create(20, 20)
RingImage.Clip(ClipRing).FillRect(0, 0, 20, 20, "Lime")
AssertEq(RingImage.GetPixel(3, 3), 0xFF00FF00, A_LineNumber)
AssertEq(Channel(RingImage, 10, 10, 3), 0, A_LineNumber)

FixedClip := Image.Create(24, 8)
FixedClip.Transform := {OffsetX: 10}
FixedClip.Clip(Image.Path().AddRect(0, 0, 5, 5))
FixedClip.Transform := {OffsetX: 0}
FixedClip.Clear("Blue").FillRect(0, 0, 24, 8, "Lime")
AssertEq(FixedClip.GetPixel(2, 2), 0xFF0000FF, A_LineNumber)
AssertEq(FixedClip.GetPixel(12, 2), 0xFF00FF00, A_LineNumber)

StateImage := Image.Create(30, 20)
StateImage.Transform := {OffsetX: 3}
Saved := StateImage.SaveState()
StateImage.Transform := {OffsetX: 15}
StateImage.Clip(Image.Path())
StateImage.RestoreState(Saved).FillRect(0, 0, 4, 4, "Red")
AssertEq(StateImage.GetPixel(4, 2), 0xFFFF0000, A_LineNumber)
AssertEq(Channel(StateImage, 16, 2, 3), 0, A_LineNumber)
Throws(() => Image.Create(2, 2).RestoreState(Saved), A_LineNumber, ValueError)

Linear := Image.Brush.LinearGradient(5, 0, 25, 0, "Red", "Blue")
Gradient := Image.Create(30, 10).FillPath(Image.Path().AddRect(0, 0, 30, 10), Linear)
Assert(Channel(Gradient, 2, 5, 0) > Channel(Gradient, 2, 5, 2), A_LineNumber)
Assert(Channel(Gradient, 27, 5, 2) > Channel(Gradient, 27, 5, 0), A_LineNumber)

GradientStroke := Image.Create(30, 10).DrawPath(Image.Path().MoveTo(2, 5).LineTo(28, 5), Linear, 3)
Assert(Channel(GradientStroke, 4, 5, 0) > Channel(GradientStroke, 4, 5, 2), A_LineNumber)
Assert(Channel(GradientStroke, 26, 5, 2) > Channel(GradientStroke, 26, 5, 0), A_LineNumber)

Translucent := Image.Brush.LinearGradient(5, 0, 15, 0, "0x80FF0000", "0x80FF0000")
TranslucentImage := Image.Create(20, 8).FillPath(Image.Path().AddRect(0, 0, 20, 8), Translucent)
Assert(Channel(TranslucentImage, 2, 4, 3) >= 126 && Channel(TranslucentImage, 2, 4, 3) <= 130, A_LineNumber)

Radial := Image.Brush.RadialGradient(15, 15, 12, 10, "White", "Black")
RadialImage := Image.Create(30, 30).FillPath(Image.Path().AddRect(0, 0, 30, 30), Radial)
Assert(Channel(RadialImage, 15, 15, 0) > Channel(RadialImage, 25, 15, 0), A_LineNumber)

TextImage := Image.Create(120, 40)
TextImage.DrawText("Vector", 2, 2, Linear, "s14", "Sans")
Assert(HasAlpha(TextImage), A_LineNumber)

Throws(() => Image.Path("BadRule"), A_LineNumber, ValueError)
Throws(() => Image.Path().LineTo(1, 1), A_LineNumber, ValueError)
Throws(() => Image.Brush.LinearGradient(1, 1, 1, 1, "Red", "Blue"), A_LineNumber, ValueError)
Throws(() => Image.Brush.RadialGradient(1, 1, 0, 1, "Red", "Blue"), A_LineNumber, ValueError)
Throws(() => Image.Create(2, 2).Clone(), A_LineNumber, Error)

FileAppend "pass", "*"
