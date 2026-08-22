#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetType("C:\")
#elif OSX
	val := DriveGetType("/")
#else
	val := DriveGetType("/dev/sda")
#endif

Assert(val == "Fixed" || val == "RAMDisk", A_LineNumber)

FileAppend "pass", "*"
