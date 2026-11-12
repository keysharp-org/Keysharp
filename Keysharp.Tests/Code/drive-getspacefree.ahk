#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSpaceFree("C:\")

	Assert(DriveGetSpaceFree(A_WinDir) > 10, A_LineNumber)
	Throws(() => DriveGetSpaceFree(A_Temp "\keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#elif OSX
	val := DriveGetSpaceFree("/")
#else
	val := DriveGetSpaceFree("/dev") ; the filesystem holding /dev, which every Linux machine has
#endif

Assert(val > 10, A_LineNumber)
Throws(() => DriveGetSpaceFree(""), A_LineNumber, ValueError)

FileAppend "pass", "*"
