#NoTrayIcon
#Include <assert>
	
if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

path := "../../../Keysharp.Tests/Code/"
DirCreate("./FileCopy")
dir := path . "DirCopy"
FileCopy(dir . "/file1.txt", "./FileCopy/file1.txt")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)
	
FileDelete("./FileCopy/file1.txt")

Assert(!FileExist("./FileCopy/file1.txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)
	
DirCreate("./FileCopy")
FileCopy(dir . "/*.txt", "./FileCopy")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")
FileCopy(dir . "/*.txt", "./FileCopy/*.*")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")
FileCopy(dir . "/*.txt", "./FileCopy/*.bak")

Assert(FileExist("./FileCopy/file1.bak"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.bak"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3.bak"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")
FileCopy(dir . "/*.txt", "./FileCopy/*")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3txt"), A_LineNumber)


Assert(!FileExist("./FileCopy/file3.txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")
FileCopy(dir . "/*.txt", "./FileCopy/*.")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/file3.txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")

try
{
    FileCopy(dir . "/*.txt", "./NonExistentDir/*")
}
catch
{
}

Assert(!FileExist("./FileCopy/NonExistentDir/file1.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/NonExistentDir/file2.txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/NonExistentDir/file3txt"), A_LineNumber)

Assert(!FileExist("./FileCopy/NonExistentDir/file3.txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

DirCreate("./FileCopy")
FileCopy(dir . "/*", "./FileCopy/*")

Assert(FileExist("./FileCopy/file1.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file2.txt"), A_LineNumber)

Assert(FileExist("./FileCopy/file3txt"), A_LineNumber)

if (DirExist("./FileCopy"))
	DirDelete("./FileCopy", true)

FileAppend "pass", "*"
