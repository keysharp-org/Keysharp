#NoTrayIcon
#Include <assert>

if (DirExist("./FileSetAttrib"))
	DirDelete("./FileSetAttrib", true)

dir := "../../../Keysharp.Tests/Code/DirCopy"
DirCreate("./FileSetAttrib")
DirCopy(dir, "./FileSetAttrib", true)

Assert(DirExist("./FileSetAttrib"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file1.txt"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file2.txt"), A_LineNumber)

Assert(FileExist("./FileSetAttrib/file3txt"), A_LineNumber)

dir := "./FileSetAttrib"
attr := FileGetAttrib(dir)

AssertEq(attr, "D", A_LineNumber)

dir := "./FileSetAttrib/file1.txt"
attr := FileGetAttrib(dir)

#if WINDOWS
	AssertEq(attr, "A", A_LineNumber)
#else
	AssertEq(attr, "N", A_LineNumber)
#endif

FileSetAttrib("r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "R", A_LineNumber)

FileSetAttrib("-r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "N", A_LineNumber)

FileSetAttrib("^r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "R", A_LineNumber)

FileSetAttrib("^r", dir)
attr := FileGetAttrib(dir)

AssertEq(attr, "N", A_LineNumber)

if (DirExist("./FileSetAttrib"))
	DirDelete("./FileSetAttrib", true)

; A folder named without wildcards is changed in the default mode, and with R that name is changed in every folder.
attribRoot := A_Temp "\ks-setattrib"
try DirDelete attribRoot, true
DirCreate attribRoot "\sub"
FileSetAttrib "+H", attribRoot "\sub"
Assert(InStr(FileGetAttrib(attribRoot "\sub"), "H"), A_LineNumber)
FileAppend "x", attribRoot "\t.txt"
FileAppend "x", attribRoot "\sub\t.txt"
FileAppend "x", attribRoot "\sub\u.txt"
FileSetAttrib "+R", attribRoot "\t.txt", "R"
Assert(InStr(FileGetAttrib(attribRoot "\sub\t.txt"), "R") && !InStr(FileGetAttrib(attribRoot "\sub\u.txt"), "R"), A_LineNumber)
FileSetAttrib "-R", attribRoot "\t.txt", "R"
; An unknown attribute letter is an error rather than clearing every attribute.
Throws(() => FileSetAttrib("Q", attribRoot "\sub\u.txt"), A_LineNumber, ValueError)
Assert(InStr(FileGetAttrib(attribRoot "\sub\u.txt"), "A"), A_LineNumber)
DirDelete attribRoot, true


FileAppend "pass", "*"
