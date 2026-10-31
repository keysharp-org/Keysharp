#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>

dir := TestFixturePath("DirCopy/file1.txt")
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
