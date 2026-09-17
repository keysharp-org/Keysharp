#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetCapacity("C:\")
#elif OSX
	val := DriveGetCapacity("/")
#else
	val := DriveGetCapacity("/dev") ; the filesystem holding /dev, which every Linux machine has
#endif
			
Assert(val > 1000, A_LineNumber)

FileAppend "pass", "*"
