#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetCapacity("C:\")

	; Any path on the drive answers for it, and a path that does not exist is an error rather than its drive letter.
	AssertEq(DriveGetCapacity(A_WinDir), DriveGetCapacity(SubStr(A_WinDir, 1, 3)), A_LineNumber)
	Throws(() => DriveGetCapacity(A_Temp "\keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#elif OSX
	val := DriveGetCapacity("/")
#else
	val := DriveGetCapacity("/")
#endif

Assert(val > 1000, A_LineNumber)
Throws(() => DriveGetCapacity(""), A_LineNumber, ValueError)

FileAppend "pass", "*"
