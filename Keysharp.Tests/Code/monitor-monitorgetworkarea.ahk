#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Coordinates are negative for a monitor left of or above the primary, so only the extent is checked.
AssertEq(MonitorGetWorkArea(, &l, &t, &r, &b), MonitorGetPrimary(), A_LineNumber)
Assert(r > l && b > t, A_LineNumber)

Loop MonitorGetCount()
{
	AssertEq(MonitorGetWorkArea(A_Index, &l, &t, &r, &b), A_Index, A_LineNumber)
	Assert(r > l && b > t, A_LineNumber)
}

FileAppend "pass", "*"
