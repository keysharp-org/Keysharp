#NoTrayIcon
#Include <assert>

if (DirExist("./FileRecycleEmpty"))
	DirDelete("./FileRecycleEmpty", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"
DirCreate("./FileRecycleEmpty")
FileCopy(dir . "/*", "./FileRecycleEmpty/")

Assert(FileExist("./FileRecycleEmpty/file1.txt"), A_LineNumber)

Assert(FileExist("./FileRecycleEmpty/file2.txt"), A_LineNumber)

Assert(FileExist("./FileRecycleEmpty/file3txt"), A_LineNumber)

FileRecycle("./FileRecycleEmpty/*")

Assert(!FileExist("./FileRecycleEmpty/file1.txt"), A_LineNumber)

Assert(!FileExist("./FileRecycleEmpty/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileRecycleEmpty/file3txt"), A_LineNumber)

FileRecycleEmpty()

if (DirExist("./FileRecycleEmpty"))
	DirDelete("./FileRecycleEmpty", true)

DirCreate("./FileRecycleEmpty")
FileCopy(dir . "/*", "./FileRecycleEmpty/")

Assert(FileExist("./FileRecycleEmpty/file1.txt"), A_LineNumber)

Assert(FileExist("./FileRecycleEmpty/file2.txt"), A_LineNumber)

Assert(FileExist("./FileRecycleEmpty/file3txt"), A_LineNumber)

FileRecycle("./FileRecycleEmpty/*")

Assert(!FileExist("./FileRecycleEmpty/file1.txt"), A_LineNumber)

Assert(!FileExist("./FileRecycleEmpty/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileRecycleEmpty/file3txt"), A_LineNumber)

FileRecycleEmpty("C:\")

if (DirExist("./FileRecycleEmpty"))
	DirDelete("./FileRecycleEmpty", true)

FileAppend "pass", "*"
