#NoTrayIcon
#Include <assert>

if (DirExist("./FileRecycle"))
	DirDelete("./FileRecycle", true)

DirCreate("./FileRecycle")
dir := "../../../Keysharp.Tests/Code/DirCopy"

FileCopy(dir . "/*", "./FileRecycle/")

Assert(FileExist("./FileRecycle/file1.txt"), A_LineNumber)

Assert(FileExist("./FileRecycle/file2.txt"), A_LineNumber)

Assert(FileExist("./FileRecycle/file3txt"), A_LineNumber)

; A quote in the name must not break the platform's recycle command line.
quotedPath := "./FileRecycle/file with ' quote.txt"
FileAppend("argument safety", quotedPath)
FileRecycle(quotedPath)

Assert(!FileExist(quotedPath), A_LineNumber)

FileRecycle("./FileRecycle/file1.txt")

Assert(!FileExist("./FileRecycle/file1.txt"), A_LineNumber)

Assert(FileExist("./FileRecycle/file2.txt"), A_LineNumber)

Assert(FileExist("./FileRecycle/file3txt"), A_LineNumber)

FileRecycle("./FileRecycle/*.txt")

Assert(!FileExist("./FileRecycle/file2.txt"), A_LineNumber)

Assert(FileExist("./FileRecycle/file3txt"), A_LineNumber)

FileRecycle("./FileRecycle/*")

Assert(!FileExist("./FileRecycle/file3txt"), A_LineNumber)

if (DirExist("./FileRecycle"))
	DirDelete("./FileRecycle", true)

FileAppend "pass", "*"
