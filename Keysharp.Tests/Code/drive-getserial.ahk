#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSerial("C:\")

	Assert(val > 1, A_LineNumber)
#elif OSX
	val := DriveGetSerial("/")

	Assert(val >= 0, A_LineNumber)
#else
	val := DriveGetSerial("/dev/sda")

	Assert(val >= 0, A_LineNumber)
#endif

FileAppend "pass", "*"
