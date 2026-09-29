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

; With the timestamp omitted, the time is set to now.
touched := A_Temp "\ks-settime.txt"
try FileDelete touched
FileAppend "x", touched
FileSetTime "20000101000000", touched
FileSetTime , touched
Assert(DateDiff(A_Now, FileGetTime(touched), "Seconds") < 60, A_LineNumber)
Throws(() => FileSetTime("notatime", touched), A_LineNumber, ValueError)
FileDelete touched


FileAppend "pass", "*"
