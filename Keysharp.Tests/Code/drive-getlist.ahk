#NoTrayIcon
#Include <assert>

val := DriveGetList()
			
#if WINDOWS
	AssertEq(SubStr(val, 1, 1), "C", A_LineNumber)
#else
	AssertEq(SubStr(val, 1, 1), "/", A_LineNumber)
#endif

FileAppend "pass", "*"
