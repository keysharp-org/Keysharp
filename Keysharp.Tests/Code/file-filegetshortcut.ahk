#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#import KS { FileFullPath }
#Include <assert>
#Include <testpaths>
; #Include %A_ScriptDir%/header.ahk

if (DirExist("./FileGetShortcut"))
	DirDelete("./FileGetShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

path := TestFixturePath()
iconPath := "./FileGetShortcut/Keysharp.ico"
dir := path . "DirCopy"
DirCopy(dir, "./FileGetShortcut/")
FileCopy(TestFixturePath("../../assets/Keysharp.ico"), iconPath)
SplitPath(FileFullPath("./FileGetShortcut/file1.txt"), , &fullpath)

#if LINUX
	FileCreateShortcut("./FileGetShortcut/file1.txt", "./testshortcut.lnk")
	FileGetShortcut("./testshortcut.lnk",
												&outTarget,
												&outDir,
												&outArgs,
												&outDescription,
												&outIcon,
												&outIconNum,
												&outRunState)

AssertEq(StrLower(FileFullPath("./FileGetShortcut/file1.txt")), StrLower(outTarget), A_LineNumber)

AssertEq(fullpath, outDir, A_LineNumber)

Assert(outDescription == "" &&
	outArgs == "" &&
	outIcon == "" &&
	outIconNum == "" &&
	outRunState == ""
, A_LineNumber)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

#endif

#if WINDOWS
	FileCreateShortcut("./FileGetShortcut/file1.txt", "./testshortcut.lnk", fullpath, "", "TestDescription", iconPath, "")
#else
	FileCreateShortcut("./FileGetShortcut/file1.txt", "./testshortcut.lnk", fullpath, "", "TestDescription", iconPath, 2)
#endif

Assert(FileExist("./testshortcut.lnk"), A_LineNumber)

outTarget :=
outDir :=
outArgs :=
outDescription :=
outIcon :=
outIconNum :=
outRunState := ""
FileGetShortcut("./testshortcut.lnk",
	&outTarget,
	&outDir,
	&outArgs,
	&outDescription,
	&outIcon,
	&outIconNum,
	&outRunState)

AssertEq(StrLower(FileFullPath("./FileGetShortcut/file1.txt")), StrLower(outTarget), A_LineNumber)

AssertEq(fullpath, outDir, A_LineNumber)

AssertEq("TestDescription", outDescription, A_LineNumber)

AssertEq("", outArgs, A_LineNumber)

expectedIcon := iconPath
#if LINUX || OSX
expectedIcon := FileFullPath(expectedIcon)
#endif

AssertEq(expectedIcon, outIcon, A_LineNumber)

#if WINDOWS
	AssertEq("1", outIconNum, A_LineNumber)

	AssertEq("1", outRunState, A_LineNumber)
#else
	AssertEq("Link", outIconNum, A_LineNumber)

	AssertEq("", outRunState, A_LineNumber)
#endif

if (DirExist("./FileGetShortcut"))
	DirDelete("./FileGetShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

FileAppend "pass", "*"
