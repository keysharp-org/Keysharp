#NoTrayIcon
#Include <assert>

#if WINDOWS
	val := DriveGetFileSystem("C:\")
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
