#NoTrayIcon
#Include <assert>

path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy/*.txt"

val := FileExist(dir)

#if WINDOWS
	AssertEq("A", val, A_LineNumber)
#else
	AssertEq("N", val, A_LineNumber)
#endif

#if	WINDOWS
AssertEq(FileExist(A_MyDocuments), "RD", A_LineNumber)  ; Unsure what it is in linux.//TODO
#endif

FileAppend "pass", "*"
