#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>

#CSharp
public static void SeedFileTimeError() => System.Runtime.InteropServices.Marshal.SetLastPInvokeError(12345);
#EndCSharp

dir := TestFixturePath("DirCopy/file1.txt")
time := FileGetTime(dir)

AssertEq(StrLen(time), 14, A_LineNumber)

AssertEq(IsTime(time), 1, A_LineNumber)

; A blank WhichTime is the modification time, and a folder has times as a file does.
AssertEq(FileGetTime(dir, ""), FileGetTime(dir, "M"), A_LineNumber)
folder := TestFixturePath("DirCopy")
AssertEq(IsTime(FileGetTime(folder)), 1, A_LineNumber)
AssertEq(FileGetTime(folder, ""), FileGetTime(folder, "M"), A_LineNumber)
AssertEq(IsTime(FileGetTime(folder, "C")), 1, A_LineNumber)
Throws(() => FileGetTime(dir, "X"), A_LineNumber, ValueError)
Throws(() => FileGetTime(""), A_LineNumber, ValueError)
SeedFileTimeError()
Throws(() => FileGetTime(TestFixturePath("DirCopy/missing.txt")), A_LineNumber, OSError)
AssertEq(2, A_LastError, A_LineNumber)
#if WINDOWS
try
{
	FileGetTime(TestFixturePath("DirCopy/missing/file.txt"))
	Assert(false, A_LineNumber)
}
catch OSError as e
	AssertEq(3, e.Number, A_LineNumber)
#endif

FileAppend "pass", "*"
