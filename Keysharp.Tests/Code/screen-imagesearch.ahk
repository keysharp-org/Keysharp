x :=
y := 0 
CoordMode("Mouse", "Screen")
ImageCapture(10, 10, 500, 500, "./imagesearch.bmp")

l :=
t :=
r :=
b := 0
monget := MonitorGetWorkArea(, &l, &t, &r, &b)
ImageSearch(&x, &y, 0, 0, r, b, "./imagesearch.bmp")

if (x == 10 && y == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"