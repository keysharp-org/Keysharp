#NoTrayIcon
#Include <assert>

path := "../../../Keysharp.Tests/Code/DirCopy"
val := FileGetAttrib(path)

AssertEq(val, "D", A_LineNumber)

val := FileGetAttrib(path . "/file1.txt")

#if WINDOWS
	AssertEq("A", val, A_LineNumber)
#else
	AssertEq("N", val, A_LineNumber)
#endif

val := FileGetAttrib(path . "/file2.txt")

#if WINDOWS
	AssertEq("A", val, A_LineNumber)
#else
	AssertEq("N", val, A_LineNumber)
#endif

val := FileGetAttrib(path . "/file3txt")

#if WINDOWS
	AssertEq("A", val, A_LineNumber)
#else
	AssertEq("N", val, A_LineNumber)
#endif

FileAppend "pass", "*"
