#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetType("C:\")
#elif OSX
	val := DriveGetType("/")
#else
	val := DriveGetType("/dev") ; the filesystem holding /dev, which every Linux machine has
#endif

Assert(val == "Fixed" || val == "RAMDisk", A_LineNumber)

FileAppend "pass", "*"
