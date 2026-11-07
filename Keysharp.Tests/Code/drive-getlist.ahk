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

; The type matches in any case, and a word that is not a type is an error rather than every drive.
AssertEq(DriveGetList(StrLower(driveType)), DriveGetList(driveType), A_LineNumber)
Throws(() => DriveGetList("Bogus"), A_LineNumber, ValueError)
Throws(() => DriveGetList(""), A_LineNumber, ValueError)

FileAppend "pass", "*"
