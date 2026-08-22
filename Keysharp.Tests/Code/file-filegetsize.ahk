#NoTrayIcon
#Include <assert>

dir := "../../../Keysharp.Tests/Code/DirCopy/file1.txt"
size := FileGetSize(dir)

AssertEq(size, 14, A_LineNumber)

size := FileGetSize(dir, "k")

AssertEq(size, 0, A_LineNumber)
	
size := FileGetSize(dir, "m")

AssertEq(size, 0, A_LineNumber)
	
size := FileGetSize(dir, "g")

AssertEq(size, 0, A_LineNumber)
	
size := FileGetSize(dir, "t")

AssertEq(size, 0, A_LineNumber)

FileAppend "pass", "*"
