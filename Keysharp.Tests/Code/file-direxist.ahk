#ErrorStdOut
#Warn All, StdOut
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

; As in AutoHotkey, a file is not a folder.
AssertEq(DirExist(dir), "", A_LineNumber)

dir := path . "DirCopy/file2.txt"

Assert(FileExist(dir), A_LineNumber)
AssertEq(DirExist(dir), "", A_LineNumber)

dir := path . "DirCopy/file3txt"

Assert(FileExist(dir), A_LineNumber)
AssertEq(DirExist(dir), "", A_LineNumber)

; A wildcard finds the first folder it matches and passes over files.
AssertEq(DirExist("./DirExist/Sub*"), "D", A_LineNumber)
AssertEq(DirExist(path . "DirCopy/*.txt"), "", A_LineNumber)
Assert(InStr(DirExist(path . "DirCop*"), "D"), A_LineNumber)

DirDelete("./DirExist", true)

FileAppend "pass", "*"
