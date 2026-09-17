#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

CoordMode("Pixel", "Screen")
MonitorGet(MonitorGetPrimary(), &left, &top, &right, &bottom)

; A sparse grid over the whole primary monitor stays fast yet reaches past a solid black or white area.
stepX := Max(1, (right - left) // 32)
stepY := Max(1, (bottom - top) // 32)
found := ""
foundX := foundY := 0
py := top

while (found = "" && py < bottom)
{
	px := left

	while (px < right)
	{
		pix := PixelGetColor(px, py)

		if (pix != "0xFFFFFF" && pix != "0x000000")
		{
			found := pix, foundX := px, foundY := py
			break
		}

		px += stepX
	}

	py += stepY
}

Assert(found != "", A_LineNumber)

if (found != "")
{
	AssertEq(PixelSearch(&outX, &outY, foundX, foundY, foundX + 1, foundY + 1, found), 1, A_LineNumber)
	AssertEq(outX, foundX, A_LineNumber)
	AssertEq(outY, foundY, A_LineNumber)
}

FileAppend "pass", "*"
