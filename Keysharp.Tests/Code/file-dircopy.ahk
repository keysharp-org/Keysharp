#NoTrayIcon
#Include <assert>

if (DirExist("./DirCopy2"))
	DirDelete("./DirCopy2", true)

DirCopy("../../../Keysharp.Tests/Code/DirCopy", "./DirCopy2")
VerifyAndDelete(true)

DirCopy("../../../Keysharp.Tests/Code/DirCopy/DirCopy.zip", "./DirCopy2", true)
VerifyAndDelete(false)

b := false

try
{
    DirCopy("../../../Keysharp.Tests/Code/DirCopy/DirCopy.zip", "./DirCopy2", false)
}
catch
{
    b := true
}

Assert(b, A_LineNumber)

VerifyAndDelete(true)

VerifyAndDelete(del)
{
    Assert(DirExist("./DirCopy2"), A_LineNumber)

    Assert(FileExist("./DirCopy2/file1.txt"), A_LineNumber)

    Assert(FileExist("./DirCopy2/file2.txt"), A_LineNumber)

    Assert(FileExist("./DirCopy2/file3txt"), A_LineNumber)

    if (del)
    {
        if (DirExist("./DirCopy2"))
	        DirDelete("./DirCopy2", true)

        Assert(!(DirExist("./DirCopy2")), A_LineNumber)
    }
}

FileAppend "pass", "*"
