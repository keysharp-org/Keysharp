#NoTrayIcon
#Include <assert>

if (DirExist("./DirMove"))
	DirDelete("./DirMove", true)

if (DirExist("./DirCopy3"))
	DirDelete("./DirCopy3", true)

if (DirExist("./DirCopy3-rename"))
	DirDelete("./DirCopy3-rename", true)

path := "../../../Keysharp.Tests/Code/"
dir := path . "DirCopy"

DirCopy(dir, "./DirMove")
	
Assert(DirExist("./DirMove"), A_LineNumber)

Assert(FileExist("./DirMove/file1.txt"), A_LineNumber)

Assert(FileExist("./DirMove/file2.txt"), A_LineNumber)

Assert(FileExist("./DirMove/file3txt"), A_LineNumber)

DirMove("./DirMove", "./DirCopy3")

Assert(!DirExist("./DirMove"), A_LineNumber)
	
Assert(DirExist("./DirCopy3"), A_LineNumber)

Assert(FileExist("./DirCopy3/file1.txt"), A_LineNumber)

Assert(FileExist("./DirCopy3/file2.txt"), A_LineNumber)

Assert(FileExist("./DirCopy3/file3txt"), A_LineNumber)

threw := false

try
{
    DirMove("./DirCopy3", "./DirCopy3") ; Both of these should not throw because ./DirCopy3 already exists.
}
catch
{
	threw := true
}

Assert(threw, A_LineNumber)

threw := false
try
{
    DirMove("./DirCopy3", "./DirCopy3", 0)
}
catch
{
	threw := true
}

Assert(threw, A_LineNumber)

DirCopy(dir, "./DirMove")
DirMove("./DirMove", "./DirCopy3", 1) ;Will copy into because ./DirCopy3 already exists.

Assert(DirExist("./DirCopy3/DirMove"), A_LineNumber)

Assert(FileExist("./DirCopy3/DirMove/file1.txt"), A_LineNumber)


Assert(FileExist("./DirCopy3/DirMove/file2.txt"), A_LineNumber)

Assert(FileExist("./DirCopy3/DirMove/file3txt"), A_LineNumber)
	
DirMove("./DirCopy3", "./DirCopy3-rename", "R")

if (DirExist("./DirMove"))
	DirDelete("./DirMove", true)

if (DirExist("./DirCopy3"))
	DirDelete("./DirCopy3", true)
	
if (DirExist("./DirCopy3-rename"))
	DirDelete("./DirCopy3-rename", true)

FileAppend "pass", "*"
