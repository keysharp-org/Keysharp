#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>

if (DirExist("./FileRecycleEmpty"))
	DirDelete("./FileRecycleEmpty", true)

dir := TestFixturePath("DirCopy")
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
FileRecycleEmpty("C:\")

if (DirExist("./FileRecycleEmpty"))
	DirDelete("./FileRecycleEmpty", true)

FileAppend "pass", "*"
