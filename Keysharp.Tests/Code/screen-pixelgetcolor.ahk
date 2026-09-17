#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

CoordMode("Pixel", "Screen")
MonitorGet(MonitorGetPrimary(), &left, &top, &right, &bottom)

AssertEq(RegExMatch(PixelGetColor(left, top), "^0x[0-9A-F]{6}$"), 1, A_LineNumber)

; A sparse grid over the whole primary monitor stays fast yet reaches past a solid black or white area.
stepX := Max(1, (right - left) // 32)
stepY := Max(1, (bottom - top) // 32)
found := ""
py := top

while (found = "" && py < bottom)
{
	px := left

	while (px < right)
	{
		pix := PixelGetColor(px, py)

		if (pix != "0xFFFFFF" && pix != "0x000000")
		{
			found := pix
			break
		}

		px += stepX
	}

	py += stepY
}

Assert(found != "", A_LineNumber)

FileAppend "pass", "*"
