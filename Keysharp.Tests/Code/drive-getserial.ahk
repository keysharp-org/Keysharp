#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetSerial("C:\")

	Assert(val >= 0 && val <= 0xFFFFFFFF, A_LineNumber)
	AssertEq(DriveGetSerial("C:"), val, A_LineNumber)
	Throws(() => DriveGetSerial(A_Temp "\keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#elif OSX
	; The serial comes from the volume's UUID, which the root volume has.
	val := DriveGetSerial("/")

	Assert(val >= 0 && val <= 0xFFFFFFFF, A_LineNumber)
	Throws(() => DriveGetSerial("/keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#else
	; Container and network roots can lack a UUID.
	if RunWait('sh -c "findmnt -nro UUID --target / | grep -q ."') == 0
	{
		val := DriveGetSerial("/")
		Assert(val >= 0 && val <= 0xFFFFFFFF, A_LineNumber)
		AssertEq(DriveGetSerial("/."), val, A_LineNumber)
	}
	Throws(() => DriveGetSerial("/proc"), A_LineNumber, OSError) ; no device, so no UUID
	Throws(() => DriveGetSerial("/keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
#endif

FileAppend "pass", "*"
