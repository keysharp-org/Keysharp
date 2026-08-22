#NoTrayIcon
#Include <assert>

if (DirExist("./FileMove"))
	DirDelete("./FileMove", true)

if (DirExist("./FileMove2"))
	DirDelete("./FileMove2", true)

DirCreate("./FileMove")
path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy"
FileCopy(dir . "/*", "./FileMove/")
	
Assert(DirExist("./FileMove"), A_LineNumber)

Assert(FileExist("./FileMove/file1.txt"), A_LineNumber)

Assert(FileExist("./FileMove/file2.txt"), A_LineNumber)

Assert(FileExist("./FileMove/file3txt"), A_LineNumber)

DirCreate("./FileMove2")

Assert(DirExist("./FileMove2"), A_LineNumber)

FileMove("./FileMove/*", "./FileMove2")

Assert(!FileExist("./FileMove/file1.txt"), A_LineNumber)

Assert(!FileExist("./FileMove/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileMove/file3txt"), A_LineNumber)

Assert(FileExist("./FileMove2/file1.txt"), A_LineNumber)

Assert(FileExist("./FileMove2/file2.txt"), A_LineNumber)

Assert(FileExist("./FileMove2/file3txt"), A_LineNumber)

if (DirExist("./FileMove"))
	DirDelete("./FileMove", true)

if (DirExist("./FileMove2"))
	DirDelete("./FileMove2", true)

FileAppend "pass", "*"
