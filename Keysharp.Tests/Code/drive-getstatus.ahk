#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetStatus("C:\")
#elif OSX
	val := DriveGetStatus("/")
#else
	val := DriveGetStatus("/dev") ; /dev seems to work better than /dev/sda on VMs.
#endif
			
AssertEq(val, "Ready", A_LineNumber)

FileAppend "pass", "*"
