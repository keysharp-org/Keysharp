#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSpaceFree("C:\")
#elif OSX
	val := DriveGetSpaceFree("/")
#else
	val := DriveGetSpaceFree("/dev/sda")
#endif
			
Assert(val > 10, A_LineNumber)

FileAppend "pass", "*"
