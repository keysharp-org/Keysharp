#NoTrayIcon
#Include <assert>

if (DirExist("./FileCreateShortcut"))
	DirDelete("./FileCreateShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy"
DirCopy(dir, "./FileCreateShortcut/")
	
if (FileExist("./fileappend.txt"))
	FileDelete("./fileappend.txt")
	
FileCreateShortcut("./FileCreateShortcut/file1.txt", "./testshortcut.lnk", "", "", "TestDescription", "../../../assets/Keysharp.ico", "")

Assert(FileExist("./testshortcut.lnk"), A_LineNumber)

if (DirExist("./FileCreateShortcut"))
	DirDelete("./FileCreateShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

FileAppend "pass", "*"
