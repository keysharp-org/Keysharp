#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy/*.txt"

val := FileExist(dir)

#if WINDOWS
	AssertEq("A", val, A_LineNumber)
#else
	AssertEq("N", val, A_LineNumber)
#endif

#if	WINDOWS
AssertEq(FileExist(A_MyDocuments), "RD", A_LineNumber)  ; Unsure what it is in linux.//TODO
#endif

workingDir := A_WorkingDir
testDir := A_Temp "/keysharp-fileexist-" A_TickCount
DirCreate(testDir)

try {
	FileAppend('return "Hello";', testDir "/Example.csx")
	DirCreate(testDir "/Folder")
	SetWorkingDir(testDir)

	attributes := FileExist(testDir "/Example.csx")
	Assert(attributes != "", A_LineNumber)
	AssertEq(FileExist("Example.csx"), attributes, A_LineNumber)
	AssertEq(FileExist("./Example.csx"), attributes, A_LineNumber)
	AssertEq(FileExist("*.csx"), attributes, A_LineNumber)
	AssertEq(FileExist("Example.cs?"), attributes, A_LineNumber)
	Assert(InStr(FileExist("Folder"), "D"), A_LineNumber)
	AssertEq(FileExist("Missing.csx"), "", A_LineNumber)
	AssertEq(FileExist("Missing*.csx"), "", A_LineNumber)
	AssertEq(FileExist(""), "", A_LineNumber)

	; RunCsx uses this condition to distinguish filenames from inline C#.
	script := "Example.csx"
	route := "inline"
	if script ~= "i)^[^\r\n]+\.csx?$" && (attrib := FileExist(script)) && !InStr(attrib, "D")
		route := "file"
	AssertEq(route, "file", A_LineNumber)
}
finally {
	SetWorkingDir(workingDir)
	DirDelete(testDir, true)
}
#if WINDOWS
	Assert(InStr(FileExist(SubStr(A_WinDir, 1, 3)), "D"), A_LineNumber)
#else
	Assert(InStr(FileExist("/"), "D"), A_LineNumber)
#endif

; A wildcard matches hidden files and folders as well.
if (DirExist("./FileExist"))
	DirDelete("./FileExist", true)

DirCreate("./FileExist/onlydir")
FileAppend("", "./FileExist/x.hid")

#if WINDOWS
	FileSetAttrib("+H", "./FileExist/x.hid")
	Assert(InStr(FileExist("./FileExist/x.hid"), "H"), A_LineNumber)
#endif

Assert(FileExist("./FileExist/*.hid"), A_LineNumber)
Assert(InStr(FileExist("./FileExist/only*"), "D"), A_LineNumber)
AssertEq(FileExist("./FileExist/*.none"), "", A_LineNumber)
AssertEq(FileExist("   "), "", A_LineNumber)
AssertEq(DirExist(""), "", A_LineNumber)
; Wildcards count only in the last part of the path.
AssertEq(FileExist("./File*/x.hid"), "", A_LineNumber)
DirDelete("./FileExist", true)

FileAppend "pass", "*"
