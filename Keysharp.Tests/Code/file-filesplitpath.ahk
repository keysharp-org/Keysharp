#NoTrayIcon
#Include <assert>

path := A_ScriptDir . "/DirCopy/file1.txt"
filename :=
dir :=
ext :=
drive :=
namenoext :=
url := ""

Clear()
{

	global
	path := ""
	filename := ""
	dir := ""
	ext := ""
	namenoext := ""
	drive := ""
	url := ""
}

SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

AssertEq(filename, "file1.txt", A_LineNumber)

AssertEq(ext, "txt", A_LineNumber)

AssertEq(namenoext, "file1", A_LineNumber)

#if WINDOWS
	AssertEq("Keysharp.Tests\Code\DirCopy", SubStr(dir, -StrLen("Keysharp.Tests\Code\DirCopy")), A_LineNumber)

	Assert(StrLower("C:") == StrLower(drive) || StrLower("D:") == StrLower(drive), A_LineNumber)

	SplitPath("C:\Windows", &filename, &dir, &ext, &namenoext, &drive)

	AssertEq(StrLower("c:"), StrLower(drive), A_LineNumber)

	AssertEq(StrLower("c:"), StrLower(dir), A_LineNumber)
#else
	AssertEq(StrLower(A_ScriptDir . "/DirCopy"), StrLower(StrReplace(dir, "\", "/")), A_LineNumber)

	AssertEq("/", StrLower(drive), A_LineNumber)

#endif

Clear()
url := "https://domain.com"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("", filename, A_LineNumber)

AssertEq("https://domain.com", dir, A_LineNumber)

AssertEq("", ext, A_LineNumber)

AssertEq("", namenoext, A_LineNumber)

AssertEq("https://domain.com", drive, A_LineNumber)

Clear()
url := "https://domain.com/images"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("", filename, A_LineNumber)

AssertEq("https://domain.com/images", dir, A_LineNumber)

AssertEq("", ext, A_LineNumber)

AssertEq("", namenoext, A_LineNumber)

AssertEq("https://domain.com", drive, A_LineNumber)

Clear()
url := "https://domain.com/images/afile.jpg"
SplitPath(url, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("afile.jpg", filename, A_LineNumber)

AssertEq("https://domain.com/images", dir, A_LineNumber)

AssertEq("jpg", ext, A_LineNumber)

AssertEq("afile", namenoext, A_LineNumber)

AssertEq("https://domain.com", drive, A_LineNumber)

Clear()
path := "\\machinename"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("", filename, A_LineNumber)

AssertEq("\\machinename", dir, A_LineNumber)

AssertEq("", ext, A_LineNumber)

AssertEq("", namenoext, A_LineNumber)

AssertEq("\\machinename", drive, A_LineNumber)

Clear()
path := "\\machinename\dir"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("", filename, A_LineNumber)

AssertEq("\\machinename\dir", dir, A_LineNumber)

AssertEq("", ext, A_LineNumber)

AssertEq("", namenoext, A_LineNumber)

AssertEq("\\machinename", drive, A_LineNumber)

Clear()
path := "\\machinename\dir\filename.txt"
SplitPath(path, &filename, &dir, &ext, &namenoext, &drive)

AssertEq("filename.txt", filename, A_LineNumber)

AssertEq("\\machinename\dir", dir, A_LineNumber)

AssertEq("txt", ext, A_LineNumber)

AssertEq("filename", namenoext, A_LineNumber)

AssertEq("\\machinename", drive, A_LineNumber)

FileAppend "pass", "*"
