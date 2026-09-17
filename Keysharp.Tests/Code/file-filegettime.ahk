#NoTrayIcon
#Include <assert>

dir := "../../../Keysharp.Tests/Code/DirCopy/file1.txt"
time := FileGetTime(dir)

AssertEq(StrLen(time), 14, A_LineNumber)

AssertEq(IsTime(time), 1, A_LineNumber)

FileAppend "pass", "*"
