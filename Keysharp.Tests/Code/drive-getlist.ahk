#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

val := DriveGetList()
			
#if WINDOWS
	first := SubStr(val, 1, 1)
	driveType := DriveGetType(first . ":\")
	Assert(driveType != "", A_LineNumber)
	Assert(InStr(DriveGetList(driveType), first), A_LineNumber)
#else
	first := StrSplit(val, ";")[1]
	driveType := DriveGetType(first)
	Assert(driveType != "", A_LineNumber)
	Assert(InStr(";" . DriveGetList(driveType) . ";", ";" . first . ";"), A_LineNumber)
#endif

FileAppend "pass", "*"
