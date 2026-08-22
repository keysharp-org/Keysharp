#NoTrayIcon
#Include <assert>

val := DriveGetStatusCD("C:\\")
			
AssertEq(val, "error", A_LineNumber)

FileAppend "pass", "*"
