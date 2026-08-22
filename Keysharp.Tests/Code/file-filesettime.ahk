#NoTrayIcon
#Include <assert>

if (DirExist("./FileSetTime"))
	DirDelete("./FileSetTime", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"
DirCopy(dir, "./FileSetTime")

Assert(DirExist("./FileSetTime"), A_LineNumber)

Assert(FileExist("./FileSetTime/file1.txt"), A_LineNumber)

Assert(FileExist("./FileSetTime/file2.txt"), A_LineNumber)

Assert(FileExist("./FileSetTime/file3txt"), A_LineNumber)

FileSetTime("20200101131415", "./FileSetTime/file1.txt", "m")
filetime := FileGetTime("./FileSetTime/file1.txt", "m")

AssertEq("20200101131415", filetime, A_LineNumber)

FileSetTime("20200101131416", "./FileSetTime/file1.txt", "c")
filetime := FileGetTime("./FileSetTime/file1.txt", "c")

AssertEq("20200101131416", filetime, A_LineNumber)

FileSetTime("20200101131417", "./FileSetTime/file1.txt", "a")
filetime := FileGetTime("./FileSetTime/file1.txt", "a")

AssertEq("20200101131417", filetime, A_LineNumber)

if (DirExist("./FileSetTime"))
	DirDelete("./FileSetTime", true)

FileAppend "pass", "*"
