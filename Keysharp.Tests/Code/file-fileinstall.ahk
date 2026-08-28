#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#App { Files: ["file-fileinstall.ahk", "Gui/monkey.ico"] }
#Include <assert>

destination := A_Temp "/ks_fileinstall_test.ahk"
source := A_ScriptFullPath

if FileExist(destination)
	FileDelete(destination)

FileInstall("file-fileinstall.ahk", destination)
AssertEq(FileRead(destination), FileRead(source), A_LineNumber)

threw := false

try
	FileInstall("file-fileinstall.ahk", destination)
catch
	threw := true

AssertEq(threw, true, A_LineNumber)
FileInstall("file-fileinstall.ahk", destination, 1)
AssertEq(FileRead(destination), FileRead(source), A_LineNumber)

missingDirectory := A_Temp "/ks_fileinstall_missing"

if DirExist(missingDirectory)
	DirDelete(missingDirectory, 1)

threw := false

try
	FileInstall("file-fileinstall.ahk", missingDirectory "/file.ahk")
catch
	threw := true

AssertEq(threw, true, A_LineNumber)
AssertEq(DirExist(missingDirectory), "", A_LineNumber)
FileDelete(destination)
FileAppend "pass", "*"
