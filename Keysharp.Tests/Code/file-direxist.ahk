#NoTrayIcon
#Include <assert>

if (DirExist("./DirExist"))
	DirDelete("./DirExist", true)

path := "../../../Keysharp.Tests/Code/"
dir := "./DirExist/SubDir1/SubDir2/SubDir3"
DirCreate(dir)

Assert(DirExist("./DirExist"), A_LineNumber)
	
Assert(DirExist("./DirExist/SubDir1"), A_LineNumber)
	
Assert(DirExist("./DirExist/SubDir1/SubDir2"), A_LineNumber)
	
Assert(DirExist("./DirExist/SubDir1/SubDir2/SubDir3"), A_LineNumber)
	
val := DirExist(dir)

AssertEq(val, "D", A_LineNumber)

dir := path . "DirCopy/file1.txt"

Assert(FileExist(dir), A_LineNumber)

#if WINDOWS
	AssertEq(DirExist(dir), "A", A_LineNumber)
#else
	AssertEq(DirExist(dir), "N", A_LineNumber)
#endif

dir := path . "DirCopy/file2.txt"

Assert(FileExist(dir), A_LineNumber)

#if WINDOWS
	AssertEq(DirExist(dir), "A", A_LineNumber)
#else
	AssertEq(DirExist(dir), "N", A_LineNumber)
#endif

dir := path . "DirCopy/file3txt"

Assert(FileExist(dir), A_LineNumber)

#if WINDOWS
	AssertEq(DirExist(dir), "A", A_LineNumber)
#else
	AssertEq(DirExist(dir), "N", A_LineNumber)
#endif

FileAppend "pass", "*"
