#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; Coordinates are negative for a monitor left of or above the primary, so only the extent is checked.
AssertEq(MonitorGet(, &l, &t, &r, &b), MonitorGetPrimary(), A_LineNumber)
Assert(r > l && b > t, A_LineNumber)

Loop MonitorGetCount()
{
	AssertEq(MonitorGet(A_Index, &l, &t, &r, &b), A_Index, A_LineNumber)
	Assert(r > l && b > t, A_LineNumber)
}

FileAppend "pass", "*"
