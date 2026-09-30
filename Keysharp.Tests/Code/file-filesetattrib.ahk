#NoTrayIcon
#Include <assert>
#Import Ks { A_DirSeparator }

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
attribRoot := A_Temp A_DirSeparator "ks-setattrib"
try DirDelete attribRoot, true
DirCreate attribRoot A_DirSeparator "sub"
#if WINDOWS
FileSetAttrib "+H", attribRoot A_DirSeparator "sub"
Assert(InStr(FileGetAttrib(attribRoot A_DirSeparator "sub"), "H"), A_LineNumber)
#endif
FileAppend "x", attribRoot A_DirSeparator "t.txt"
FileAppend "x", attribRoot A_DirSeparator "sub" A_DirSeparator "t.txt"
FileAppend "x", attribRoot A_DirSeparator "sub" A_DirSeparator "u.txt"
FileSetAttrib "+R", attribRoot A_DirSeparator "t.txt", "R"
Assert(InStr(FileGetAttrib(attribRoot A_DirSeparator "sub" A_DirSeparator "t.txt"), "R") && !InStr(FileGetAttrib(attribRoot A_DirSeparator "sub" A_DirSeparator "u.txt"), "R"), A_LineNumber)
FileSetAttrib "-R", attribRoot A_DirSeparator "t.txt", "R"
; An unknown attribute letter is an error rather than clearing every attribute.
unchanged := FileGetAttrib(attribRoot A_DirSeparator "sub" A_DirSeparator "u.txt")
Throws(() => FileSetAttrib("Q", attribRoot A_DirSeparator "sub" A_DirSeparator "u.txt"), A_LineNumber, ValueError)
AssertEq(FileGetAttrib(attribRoot A_DirSeparator "sub" A_DirSeparator "u.txt"), unchanged, A_LineNumber)
DirDelete attribRoot, true


FileAppend "pass", "*"
