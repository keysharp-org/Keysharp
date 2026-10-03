#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSerial("C:\")

	Assert(val > 1, A_LineNumber)
	AssertEq(DriveGetSerial("C:"), val, A_LineNumber)
	Throws(() => DriveGetSerial(A_Temp "\keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#elif OSX
	val := DriveGetSerial("/")

	Assert(val >= 0, A_LineNumber)
#else
	val := DriveGetSerial("/dev") ; the filesystem holding /dev, which every Linux machine has

	Assert(val >= 0, A_LineNumber)
#endif

FileAppend "pass", "*"
