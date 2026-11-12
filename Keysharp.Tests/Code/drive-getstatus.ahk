#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetStatus("C:\")
	missing := A_Temp "\keysharp-missing-volume-" A_TickCount

	AssertEq(DriveGetStatus(A_WinDir), "Ready", A_LineNumber)

	; These reach a device by its drive letter, so a longer path is rejected before anything is opened.
	Throws(() => DriveGetStatusCD(A_WinDir), A_LineNumber, ValueError)
	Throws(() => DriveLock(A_WinDir), A_LineNumber, ValueError)
	Throws(() => DriveUnlock(A_WinDir), A_LineNumber, ValueError)
#elif OSX
	val := DriveGetStatus("/")
	missing := "/keysharp-missing-volume-" A_TickCount
#else
	val := DriveGetStatus("/dev") ; /dev seems to work better than /dev/sda on VMs.
	missing := "/keysharp-missing-volume-" A_TickCount

	Throws(() => DriveLock(""), A_LineNumber, OSError)
	Throws(() => DriveUnlock(""), A_LineNumber, OSError)
#endif

AssertEq(val, "Ready", A_LineNumber)
AssertEq(DriveGetStatus(missing), "Invalid", A_LineNumber)

; An empty drive names nothing to open, on any platform.
Throws(() => DriveEject(""), A_LineNumber, OSError)
Throws(() => DriveRetract(""), A_LineNumber, OSError)

FileAppend "pass", "*"
