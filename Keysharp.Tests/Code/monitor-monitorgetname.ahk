#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

seen := Map()

Loop MonitorGetCount()
{
	name := MonitorGetName(A_Index)
	Assert(name != "", A_LineNumber)

	; Whatever the source, the names have to tell the monitors apart.
	Assert(!seen.Has(name), A_LineNumber)
	seen[name] := A_Index

#if WINDOWS
	; GDI always names a display device, and always in this shape.
	AssertEq(SubStr(name, 1, 4), "\\.\", A_LineNumber)
#endif
}

AssertEq(MonitorGetName(), MonitorGetName(MonitorGetPrimary()), A_LineNumber)

FileAppend "pass", "*"
