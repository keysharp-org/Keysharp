#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Include <testpaths>

if (DirExist("./FileCreateShortcut"))
	DirDelete("./FileCreateShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

path := TestFixturePath()
dir := path . "DirCopy"
DirCopy(dir, "./FileCreateShortcut/")
	
if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
	
FileCreateShortcut("./FileCreateShortcut/file1.txt", "./testshortcut.lnk", "", "", "TestDescription", TestFixturePath("../../assets/Keysharp.ico"), "")

Assert(FileExist("./testshortcut.lnk"), A_LineNumber)

if (DirExist("./FileCreateShortcut"))
	DirDelete("./FileCreateShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

FileAppend "pass", "*"
