#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSpaceFree("C:\")
#elif OSX
	val := DriveGetSpaceFree("/")
#else
	val := DriveGetSpaceFree("/dev") ; the filesystem holding /dev, which every Linux machine has
#endif
			
Assert(val > 10, A_LineNumber)

FileAppend "pass", "*"
