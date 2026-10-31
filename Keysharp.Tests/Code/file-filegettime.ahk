#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>

dir := TestFixturePath("DirCopy/file1.txt")
time := FileGetTime(dir)

AssertEq(StrLen(time), 14, A_LineNumber)

AssertEq(IsTime(time), 1, A_LineNumber)

FileAppend "pass", "*"
