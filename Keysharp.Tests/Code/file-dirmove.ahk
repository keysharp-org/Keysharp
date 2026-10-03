#ErrorStdOut
#Warn All, StdOut
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

; Mode 2 merges into an existing destination: source files overwrite theirs, and its other files stay.
if (DirExist("./DirMove2"))
	DirDelete("./DirMove2", true)

DirCopy(dir, "./DirMove2/Src")
DirCreate("./DirMove2/Dest")
FileAppend("old", "./DirMove2/Dest/file1.txt")
FileAppend("keep", "./DirMove2/Dest/extra.txt")
DirCreate("./DirMove2/Src/Only")
DirCreate("./DirMove2/Src/Both")
DirCreate("./DirMove2/Dest/Both")
FileAppend("new", "./DirMove2/Src/Only/s.txt")
FileAppend("new", "./DirMove2/Src/Both/b.txt")
FileAppend("keep", "./DirMove2/Dest/Both/k.txt")
DirMove("./DirMove2/Src", "./DirMove2/Dest", 2)

Assert(!DirExist("./DirMove2/Src"), A_LineNumber)
Assert(!DirExist("./DirMove2/Dest/Src"), A_LineNumber)
AssertEq(FileGetSize("./DirMove2/Dest/file1.txt"), FileGetSize(dir . "/file1.txt"), A_LineNumber)
Assert(FileExist("./DirMove2/Dest/file3txt"), A_LineNumber)
Assert(FileExist("./DirMove2/Dest/extra.txt"), A_LineNumber)
Assert(FileExist("./DirMove2/Dest/Only/s.txt"), A_LineNumber)
Assert(FileExist("./DirMove2/Dest/Both/b.txt"), A_LineNumber)
Assert(FileExist("./DirMove2/Dest/Both/k.txt"), A_LineNumber)

; A folder cannot be merged into itself, and the mode is one of 0, 1, 2 and R.
Throws(() => DirMove("./DirMove2/Dest", "./DirMove2/Dest", 2), A_LineNumber, OSError)
Assert(FileExist("./DirMove2/Dest/extra.txt"), A_LineNumber)
Throws(() => DirMove("./DirMove2/Dest", "./DirMove2/Other", 3), A_LineNumber, ValueError)
Throws(() => DirMove("./DirMove2/Dest", Chr(0), 2), A_LineNumber, OSError)
DirDelete("./DirMove2", true)

if (DirExist("./DirMove"))
	DirDelete("./DirMove", true)

if (DirExist("./DirCopy3"))
	DirDelete("./DirCopy3", true)
	
if (DirExist("./DirCopy3-rename"))
	DirDelete("./DirCopy3-rename", true)

#if !WINDOWS
	#Import Ks { Clr }
	System := Clr.Load("System")
	Base := A_Temp "/keysharp-move-links-" ProcessExist()
	try
	{
		DirCreate(Base "/External")
		FileAppend("keep", Base "/External/keep.txt", "UTF-8-RAW")
		DirCreate(Base "/Source")
		DirCreate(Base "/Dest/Link")
		System.IO.Directory.CreateSymbolicLink(Base "/Source/Link", Base "/External")
		Throws(() => DirMove(Base "/Source", Base "/Dest", 2), A_LineNumber, OSError)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)
		AssertEq(System.IO.DirectoryInfo(Base "/Source/Link").LinkTarget, Base "/External", A_LineNumber)
		System.IO.Directory.CreateSymbolicLink(Base "/SourceRoot", Base "/External")
		Throws(() => DirMove(Base "/SourceRoot", Base "/Dest", 2), A_LineNumber, OSError)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)
		DirMove(Base "/SourceRoot", Base "/Dest", 1)
		AssertEq(DirExist(Base "/SourceRoot"), "", A_LineNumber)
		AssertEq(System.IO.DirectoryInfo(Base "/Dest/SourceRoot").LinkTarget, Base "/External", A_LineNumber)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)

		DirCreate(Base "/FinalSource")
		FileAppend("new", Base "/FinalSource/keep.txt", "UTF-8-RAW")
		System.IO.Directory.CreateSymbolicLink(Base "/Dest/FinalSource", Base "/External")
		Throws(() => DirMove(Base "/FinalSource", Base "/Dest", 1), A_LineNumber, OSError)
		AssertEq(FileRead(Base "/FinalSource/keep.txt"), "new", A_LineNumber)
		AssertEq(System.IO.DirectoryInfo(Base "/Dest/FinalSource").LinkTarget, Base "/External", A_LineNumber)
		AssertEq(FileRead(Base "/External/keep.txt"), "keep", A_LineNumber)
	}
	finally
	{
		if DirExist(Base)
			DirDelete(Base, true)
	}
#endif

#if LINUX
	; /dev/shm supplies a separate device on the supported Linux test hosts.
	if DirExist("/dev/shm") && !InStr(A_Temp, "/dev/shm", true)
	{
		SourceBase := A_Temp "/keysharp-move-cross-" ProcessExist()
		DestBase := "/dev/shm/keysharp-move-cross-" ProcessExist()
		Available := false
		try
		{
			DirCreate(SourceBase)
			DirCreate(DestBase)
			Available := true
		}
		catch
		{
		}

		try
		{
			if Available
			{
				DirCreate(SourceBase "/External")
				FileAppend("keep", SourceBase "/External/keep.txt", "UTF-8-RAW")
				for Mode in [1, 2]
				{
					Source := SourceBase "/Source" Mode
					Dest := DestBase "/Dest" Mode
					DirCreate(Source "/Child")
					DirCreate(Dest "/Child")
					FileAppend("new", Source "/Child/a.txt", "UTF-8-RAW")
					FileAppend("keep", Dest "/Child/b.txt", "UTF-8-RAW")
					System.IO.Directory.CreateSymbolicLink(Source "/DirectoryLink", SourceBase "/External")
					System.IO.File.CreateSymbolicLink(Source "/FileLink", SourceBase "/External/keep.txt")
					DirMove(Source, Dest, Mode)
					AssertEq(DirExist(Source), "", A_LineNumber)
					AssertEq(FileRead(Dest "/Child/a.txt"), "new", A_LineNumber)
					AssertEq(FileRead(Dest "/Child/b.txt"), "keep", A_LineNumber)
					AssertEq(DirExist(Dest "/Source" Mode), "", A_LineNumber)
					AssertEq(System.IO.DirectoryInfo(Dest "/DirectoryLink").LinkTarget, SourceBase "/External", A_LineNumber)
					AssertEq(System.IO.FileInfo(Dest "/FileLink").LinkTarget, SourceBase "/External/keep.txt", A_LineNumber)
					AssertEq(FileRead(SourceBase "/External/keep.txt"), "keep", A_LineNumber)
				}

				; A symlinked parent names the destination device just as its real path does.
				System.IO.Directory.CreateSymbolicLink(SourceBase "/Alias", DestBase)
				DirCreate(SourceBase "/ViaLink/Child")
				DirCreate(DestBase "/ViaLink")
				FileAppend("linked", SourceBase "/ViaLink/Child/a.txt", "UTF-8-RAW")
				DirMove(SourceBase "/ViaLink", SourceBase "/Alias/ViaLink", 1)
				AssertEq(FileRead(DestBase "/ViaLink/Child/a.txt"), "linked", A_LineNumber)
				AssertEq(DirExist(SourceBase "/ViaLink"), "", A_LineNumber)
			}
		}
		finally
		{
			if DirExist(SourceBase)
				DirDelete(SourceBase, true)
			if DirExist(DestBase)
				DirDelete(DestBase, true)
		}
	}
#endif

FileAppend "pass", "*"
