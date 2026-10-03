#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

if (DirExist("./DirCopy2"))
	DirDelete("./DirCopy2", true)

DirCopy("../../../Keysharp.Tests/Code/DirCopy", "./DirCopy2")
VerifyAndDelete(false)

DirCopy("../../../Keysharp.Tests/Code/DirCopy", "./DirCopy2", true)
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

DirCreate("./DirCopy2")
Throws(() => DirCopy("./DirCopy2", "./DirCopy2/Child", true), A_LineNumber, OSError)
AssertEq(DirExist("./DirCopy2/Child"), "", A_LineNumber)
Throws(() => DirCopy("./DirCopy2", "./DirCopy2", true), A_LineNumber, OSError)
Throws(() => DirCopy("./DirCopy2", Chr(0), true), A_LineNumber, OSError)
DirDelete("./DirCopy2", true)

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

#if !WINDOWS
	#Import Ks { Clr }
	System := Clr.Load("System")
	Base := A_Temp "/keysharp-copy-links-" ProcessExist()
	try
	{
		DirCreate(Base "/External")
		FileAppend("keep", Base "/External/keep.txt", "UTF-8-RAW")
		DirCreate(Base "/Source")
		System.IO.Directory.CreateSymbolicLink(Base "/Source/DirectoryLink", Base "/External")
		System.IO.File.CreateSymbolicLink(Base "/Source/FileLink", Base "/External/keep.txt")
		System.IO.Directory.CreateSymbolicLink(Base "/RootDest", Base "/External")
		Throws(() => DirCopy(Base "/Source", Base "/RootDest", true), A_LineNumber, OSError)
		AssertEq(FileExist(Base "/External/FileLink"), "", A_LineNumber)
		DirCopy(Base "/Source", Base "/Dest")
		AssertEq(System.IO.DirectoryInfo(Base "/Dest/DirectoryLink").LinkTarget, Base "/External", A_LineNumber)
		AssertEq(System.IO.FileInfo(Base "/Dest/FileLink").LinkTarget, Base "/External/keep.txt", A_LineNumber)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)

		DirCreate(Base "/DirectorySource/Entry")
		FileAppend("new", Base "/DirectorySource/Entry/new.txt", "UTF-8-RAW")
		DirCreate(Base "/DirectoryDest")
		System.IO.Directory.CreateSymbolicLink(Base "/DirectoryDest/Entry", Base "/External")
		Throws(() => DirCopy(Base "/DirectorySource", Base "/DirectoryDest", true), A_LineNumber, OSError)
		AssertEq(System.IO.DirectoryInfo(Base "/DirectoryDest/Entry").LinkTarget, Base "/External", A_LineNumber)
		AssertEq(FileExist(Base "/External/new.txt"), "", A_LineNumber)

		DirCreate(Base "/FileSource")
		FileAppend("new", Base "/FileSource/keep.txt", "UTF-8-RAW")
		DirCreate(Base "/FileDest")
		System.IO.File.CreateSymbolicLink(Base "/FileDest/keep.txt", Base "/External/keep.txt")
		Throws(() => DirCopy(Base "/FileSource", Base "/FileDest", true), A_LineNumber, OSError)
		AssertEq(System.IO.FileInfo(Base "/FileDest/keep.txt").LinkTarget, Base "/External/keep.txt", A_LineNumber)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)
	}
	finally
	{
		if DirExist(Base)
			DirDelete(Base, true)
	}
#endif

FileAppend "pass", "*"
