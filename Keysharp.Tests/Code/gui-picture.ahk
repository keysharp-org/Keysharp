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
Window.Destroy()
FileAppend("pass", "*")
