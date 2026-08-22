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
	
if (DirExist("./FileDelete"))
	DirDelete("./FileDelete", true)

FileAppend "pass", "*"
