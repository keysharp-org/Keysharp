#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
	
if (DirExist("./FileDelete"))
	DirDelete("./FileDelete", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"

DirCopy(dir, "./FileDelete")
DirCreate("./FileDelete/folder.txt")
FileDelete("./FileDelete/*.txt")

Assert(DirExist("./FileDelete/"), A_LineNumber)
; As in AutoHotkey, a wildcard deletes files only.
Assert(DirExist("./FileDelete/folder.txt"), A_LineNumber)

Assert(!FileExist("./FileDelete/file1.txt"), A_LineNumber)

Assert(!FileExist("./FileDelete/file2.txt"), A_LineNumber)

Assert(FileExist("./FileDelete/file3txt"), A_LineNumber)

FileDelete("./FileDelete/*")

Assert(!FileExist("./FileDelete/file3txt"), A_LineNumber)

; As in AutoHotkey, an exact path must exist, while a wildcard with no matches succeeds, even in a missing folder.
Throws(() => FileDelete("./FileDelete/file1.txt"), A_LineNumber, OSError)
Throws(() => FileDelete(""), A_LineNumber, ValueError)
FileDelete("./FileDelete/*.txt")
FileDelete("./FileDelete/missing/*.txt")

; As FindFirstFile does in AutoHotkey, "*." matches only names without an extension.
DirCreate("./FileDelete/NoExt")
FileAppend("", "./FileDelete/NoExt/noext")
FileAppend("", "./FileDelete/NoExt/a.txt")
Assert(FileExist("./FileDelete/NoExt/*."), A_LineNumber)
FileDelete("./FileDelete/NoExt/*.")
AssertEq(FileExist("./FileDelete/NoExt/noext"), "", A_LineNumber)
Assert(FileExist("./FileDelete/NoExt/a.txt"), A_LineNumber)
AssertEq(FileExist("./FileDelete/NoExt/*."), "", A_LineNumber)

if (DirExist("./FileDelete"))
	DirDelete("./FileDelete", true)

FileAppend "pass", "*"
