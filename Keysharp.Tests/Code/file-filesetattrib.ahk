#NoTrayIcon
#Include <assert>

if (DirExist("./FileSetAttrib"))
	DirDelete("./FileSetAttrib", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"
DirCreate("./FileSetAttrib")
DirCopy(dir, "./FileSetAttrib", true)

Assert(DirExist("./FileSetAttrib"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file1.txt"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file2.txt"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file3txt"), A_LineNumber)

dir := "./FileSetAttrib"
attr := FileGetAttrib(dir)

AssertEq(attr, "D", A_LineNumber)

dir := "./FileSetAttrib/file1.txt"
attr := FileGetAttrib(dir)

#if WINDOWS
	AssertEq(attr, "A", A_LineNumber)
#else
	AssertEq(attr, "N", A_LineNumber)
#endif

FileSetAttrib("r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "R", A_LineNumber)

FileSetAttrib("-r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "N", A_LineNumber)

FileSetAttrib("^r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "R", A_LineNumber)

FileSetAttrib("^r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "N", A_LineNumber)

if (DirExist("./FileSetAttrib"))
	DirDelete("./FileSetAttrib", true)

FileAppend "pass", "*"
