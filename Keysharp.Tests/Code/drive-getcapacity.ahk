#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetCapacity("C:\")
#elif OSX
	val := DriveGetCapacity("/")
#else
	val := DriveGetCapacity("/dev/sda")
#endif
			
Assert(val > 1000, A_LineNumber)

FileAppend "pass", "*"
