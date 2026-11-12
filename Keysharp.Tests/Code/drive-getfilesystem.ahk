#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetFileSystem("C:\")

	; The file system and label belong to a volume, so only its root names it.
	AssertEq(DriveGetFileSystem("C:"), val, A_LineNumber)
	Throws(() => DriveGetFileSystem(A_WinDir), A_LineNumber, OSError)
	Throws(() => DriveGetFileSystem(A_Temp "\keysharp-missing-volume-" A_TickCount), A_LineNumber, OSError)
	AssertEq(DriveGetLabel("C:"), DriveGetLabel("C:\"), A_LineNumber)
	Throws(() => DriveGetLabel(A_WinDir), A_LineNumber, OSError)
	Throws(() => DriveSetLabel(A_Temp "\keysharp-missing-volume-" A_TickCount, "x"), A_LineNumber, OSError)
#else
	val := DriveGetFileSystem("/")
#endif

Assert(
#if WINDOWS
	val == "NTFS" || val == "FAT32" || val == "FAT" || val == "CDFS" || val == "UDF"
#else
	val != ""
#endif
, A_LineNumber)

FileAppend "pass", "*"
