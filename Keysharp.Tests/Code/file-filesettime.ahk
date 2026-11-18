#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>
#Import Ks { A_DirSeparator }

if (DirExist("./FileSetTime"))
	DirDelete("./FileSetTime", true)

dir := TestFixturePath("DirCopy")
DirCopy(dir, "./FileSetTime")

Assert(DirExist("./FileSetTime"), A_LineNumber)

Assert(FileExist("./FileSetTime/file1.txt"), A_LineNumber)

Assert(FileExist("./FileSetTime/file2.txt"), A_LineNumber)

Assert(FileExist("./FileSetTime/file3txt"), A_LineNumber)

FileSetTime("20200101131415", "./FileSetTime/file1.txt", "m")
filetime := FileGetTime("./FileSetTime/file1.txt", "m")

AssertEq("20200101131415", filetime, A_LineNumber)

#if LINUX
; Linux keeps no settable creation time, so each item fails and the modification time is left alone.
try
{
	FileSetTime("20200101131416", "./FileSetTime/file1.txt", "c")
	Assert(false, A_LineNumber)
}
catch Error as e
{
	AssertEq(1, e.Extra, A_LineNumber)
	AssertEq(95, A_LastError, A_LineNumber)
}
AssertEq("20200101131415", FileGetTime("./FileSetTime/file1.txt", "m"), A_LineNumber)
#else
FileSetTime("20200101131416", "./FileSetTime/file1.txt", "c")
filetime := FileGetTime("./FileSetTime/file1.txt", "c")

AssertEq("20200101131416", filetime, A_LineNumber)
#endif

FileSetTime("20200101131417", "./FileSetTime/file1.txt", "a")
filetime := FileGetTime("./FileSetTime/file1.txt", "a")

AssertEq("20200101131417", filetime, A_LineNumber)

; A blank WhichTime is the modification time.
FileSetTime("20200101131418", "./FileSetTime/file2.txt", "")
AssertEq("20200101131418", FileGetTime("./FileSetTime/file2.txt", ""), A_LineNumber)

; A read-only file's times can still be set.
FileSetAttrib("+R", "./FileSetTime/file2.txt")
FileSetTime("20200101131419", "./FileSetTime/file2.txt")
AssertEq("20200101131419", FileGetTime("./FileSetTime/file2.txt"), A_LineNumber)
FileSetAttrib("-R", "./FileSetTime/file2.txt")

; A folder named directly is changed even in the default files-only mode, while a wildcard reaches it only with D.
DirCreate("./FileSetTime/sub")
#if !LINUX
FileSetTime("20190101000001", "./FileSetTime/sub", "C")
AssertEq("20190101000001", FileGetTime("./FileSetTime/sub", "C"), A_LineNumber)
#endif
; A file watcher lists a new folder in the working tree, and under relatime that read refreshes the access time.
accessed := A_Temp A_DirSeparator "ks-settime-dir-" A_TickCount
DirCreate(accessed)
FileSetTime("20190101000002", accessed, "A")
AssertEq("20190101000002", FileGetTime(accessed, "A"), A_LineNumber)
DirDelete(accessed)
FileSetTime("20190101000003", "./FileSetTime/sub")
AssertEq("20190101000003", FileGetTime("./FileSetTime/sub"), A_LineNumber)
FileSetTime("20190101000004", "./FileSetTime/su*")
AssertEq("20190101000003", FileGetTime("./FileSetTime/sub"), A_LineNumber)
FileSetTime("20190101000005", "./FileSetTime/su*", "", "D")
AssertEq("20190101000005", FileGetTime("./FileSetTime/sub"), A_LineNumber)

AssertError(() => FileSetTime("20200101000000", "./FileSetTime/file1.txt", "X"), 'ValueError: Invalid WhichTime "X". Expected M, C or A. [X]', A_LineNumber)
Throws(() => FileSetTime("20200101000000", ""), A_LineNumber, ValueError)
; As in AutoHotkey, Mode takes only F, D and R (or V and K), with spaces and tabs between.
AssertError(() => FileSetTime("20200101000000", "./FileSetTime/file1.txt", , "FX"), 'ValueError: Invalid Mode "FX". Expected F, D, R, V or K. [FX]', A_LineNumber)
FileSetTime("20200101000006", "./FileSetTime/su*", , " d`tr ")
AssertEq("20200101000006", FileGetTime("./FileSetTime/sub"), A_LineNumber)

if (DirExist("./FileSetTime"))
	DirDelete("./FileSetTime", true)

; With the timestamp omitted, the time is set to now.
touched := A_Temp A_DirSeparator "ks-settime.txt"
try FileDelete touched
FileAppend "x", touched
FileSetTime "20000101000000", touched
FileSetTime , touched
Assert(DateDiff(A_Now, FileGetTime(touched), "Seconds") < 60, A_LineNumber)
Throws(() => FileSetTime("notatime", touched), A_LineNumber, ValueError)
FileDelete touched


FileAppend "pass", "*"
