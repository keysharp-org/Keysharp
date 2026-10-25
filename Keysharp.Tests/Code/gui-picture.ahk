#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#import Ks { Image }
#Include <assert>

Window := Gui("-DPIScale")
Picture := Window.AddPicture("w32 h16")
Artwork := Image.Create(32, 16)
Artwork.FillRect(0, 0, 32, 16, "0xFFFF0000")
Picture.Value := "HBITMAP:" Artwork.ToBitmap()
Artwork.Dispose()
AssertEq(Picture.ToClr().Image.Width, 32, A_LineNumber)
AssertEq(Picture.ToClr().Image.Height, 16, A_LineNumber)

Picture.Move(, , 48, 24)
Artwork := Image.Create(32, 16)
Picture.Value := "HBITMAP:" Artwork.ToBitmap()
Artwork.Dispose()
AssertEq(Picture.ToClr().Image.Width, 48, A_LineNumber)
AssertEq(Picture.ToClr().Image.Height, 24, A_LineNumber)

; A Picture added with an image denser than its size still takes that size in GUI units.
Artwork := Image.Create(96, 48)
Added := Window.AddPicture("w48 h24", "HBITMAP:" Artwork.ToBitmap())
Artwork.Dispose()
Added.GetPos(, , &AddedWidth, &AddedHeight)
AssertEq(AddedWidth, 48, A_LineNumber)
AssertEq(AddedHeight, 24, A_LineNumber)

; Shown, an image keeps the detail a scaled display can show, while a sparser one is not enlarged past its box.
Window.Show("NA")
Detail := Min(96, Round(48 * Window.PixelScale))
Assert(Added.ToClr().Image.Width >= Detail, A_LineNumber)
Artwork := Image.Create(96, 48)
Picture.Value := "HBITMAP:" Artwork.ToBitmap()
Artwork.Dispose()
Assert(Picture.ToClr().Image.Width >= Detail, A_LineNumber)
Artwork := Image.Create(32, 16)
Picture.Value := "HBITMAP:" Artwork.ToBitmap()
Artwork.Dispose()
AssertEq(Picture.ToClr().Image.Width, 48, A_LineNumber)
Window.Destroy()
FileAppend("pass", "*")
