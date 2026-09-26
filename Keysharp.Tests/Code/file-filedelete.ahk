#NoTrayIcon
#Include <assert>
	
if (DirExist("./FileDelete"))
	DirDelete("./FileDelete", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"

DirCopy(dir, "./FileDelete")
FileDelete("./FileDelete/*.txt")

Assert(DirExist("./FileDelete/"), A_LineNumber)
	
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

if (DirExist("./FileDelete"))
	DirDelete("./FileDelete", true)

FileAppend "pass", "*"
