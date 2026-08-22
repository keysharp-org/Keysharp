#NoTrayIcon

#import KS { Image }
#Include <assert>
x :=
y := 0
CoordMode("Pixel", "Screen")
hbitmap := Image.FromRect(100, 100, 500, 500).ToBitmap()

l :=
t :=
r :=
b := 0
monget := MonitorGetWorkArea(, &l, &t, &r, &b)
ImageSearch(&x, &y, 0, 0, r, b, "HBITMAP:" hbitmap)

Assert(x == 100 && y == 100, A_LineNumber)

FileAppend "pass", "*"
