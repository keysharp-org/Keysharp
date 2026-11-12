#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetType("C:\")
	missing := A_Temp "\keysharp-missing-volume-" A_TickCount

	AssertEq(DriveGetType("C:"), val, A_LineNumber)
	AssertEq(DriveGetType(A_WinDir), DriveGetType(SubStr(A_WinDir, 1, 3)), A_LineNumber)
#elif OSX
	val := DriveGetType("/")
	missing := "/keysharp-missing-volume-" A_TickCount
#else
	val := DriveGetType("/dev") ; the filesystem holding /dev, which every Linux machine has
	missing := "/keysharp-missing-volume-" A_TickCount
#endif

Assert(val == "Fixed" || val == "RAMDisk", A_LineNumber)
AssertEq(DriveGetType(missing), "", A_LineNumber)

FileAppend "pass", "*"
