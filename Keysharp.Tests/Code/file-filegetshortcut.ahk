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

	; Spaces, quotes, =, % and $ in the target, arguments and values survive the desktop entry's quoting and escapes.
	spaced := "./FileGetShortcut/dir with space"
	spacedFile := FileFullPath(spaced "/file 1.txt")
	DirCreate(spaced)
	FileAppend("x", spacedFile)
	args := '--flag=1 --name "a b" "say \"hi\"" 100% $HOME ""'
	desc := "Multi`tline = desc "
	FileCreateShortcut(spacedFile, "./testshortcut_multi.desktop", spaced, args, desc)
	multiTarget := multiDir := multiArgs := multiDesc := multiIcon := multiType := ""
	FileGetShortcut("./testshortcut_multi.desktop", &multiTarget, &multiDir, &multiArgs, &multiDesc, &multiIcon, &multiType)
	AssertEq(spacedFile, multiTarget, A_LineNumber)
	AssertEq(spaced, multiDir, A_LineNumber)
	AssertEq(args, multiArgs, A_LineNumber)
	AssertEq(desc, multiDesc, A_LineNumber)
	AssertEq("", multiIcon, A_LineNumber)
	AssertEq("Application", multiType, A_LineNumber)
	entry := FileRead("./testshortcut_multi.desktop")
	Assert(InStr(entry, '`nExec="' spacedFile '" --flag=1 --name "a b" "say \\"hi\\"" 100%% "\\$HOME" ""`n', true), A_LineNumber)
	Assert(InStr(entry, "`nComment=Multi\tline = desc\s`n", true), A_LineNumber)

	; Arguments are read without the target, and omitted outputs are skipped.
	multiArgs := ""
	FileGetShortcut("./testshortcut_multi.desktop", , , &multiArgs)
	AssertEq(args, multiArgs, A_LineNumber)

	; A standard Link uses its URL. Legacy links with only Exec remain readable.
	url := "https://example.org/a?q=1#section"
	FileCreateShortcut(url, "./testshortcut_multi.desktop", , , , , "Link")
	Assert(InStr(FileRead("./testshortcut_multi.desktop"), "`nURL=" url "`n", true), A_LineNumber)
	FileGetShortcut("./testshortcut_multi.desktop", &multiTarget, , , , , &multiType)
	AssertEq(url, multiTarget, A_LineNumber)
	AssertEq("Link", multiType, A_LineNumber)
	FileDelete("./testshortcut_multi.desktop")
	comment := Chr(160) "note" Chr(160)
	FileAppend("[Desktop Entry]`nType=Link`nName=legacy`nExec=/bin/tool --flag`nComment=" comment "`n", "./testshortcut_multi.desktop", "UTF-8-RAW")
	FileGetShortcut("./testshortcut_multi.desktop", &multiTarget, , &multiArgs, &multiDesc)
	AssertEq("/bin/tool", multiTarget, A_LineNumber)
	AssertEq("--flag", multiArgs, A_LineNumber)
	AssertEq(comment, multiDesc, A_LineNumber)

	; A dangling link is still a link, and a shortcut saved over a link replaces it rather than writing through it.
	missing := FileFullPath(spaced "/gone.txt")
	FileCreateShortcut(missing, "./testshortcut_link.lnk")
	multiTarget := ""
	FileGetShortcut("./testshortcut_link.lnk", &multiTarget)
	AssertEq(missing, multiTarget, A_LineNumber)
	FileCreateShortcut(spacedFile, "./testshortcut_link.lnk", , "x")
	Assert(!FileExist(missing), A_LineNumber)
	FileGetShortcut("./testshortcut_link.lnk", &multiTarget, , &multiArgs)
	AssertEq(spacedFile, multiTarget, A_LineNumber)
	AssertEq("x", multiArgs, A_LineNumber)
	FileCreateShortcut(spacedFile, "./testshortcut_link.lnk")
	FileGetShortcut("./testshortcut_link.lnk", &multiTarget, , &multiArgs)
	AssertEq(spacedFile, multiTarget, A_LineNumber)
	AssertEq("", multiArgs, A_LineNumber)

	; An ordinary file is not a shortcut, and a folder in the way is an error that leaves its contents alone.
	Throws(() => FileGetShortcut(spacedFile), A_LineNumber, OSError)
	DirCreate("./FileGetShortcut/taken.lnk")
	FileAppend("keep", "./FileGetShortcut/taken.lnk/keep.txt")
	Throws(() => FileCreateShortcut(spacedFile, "./FileGetShortcut/taken.lnk"), A_LineNumber, OSError)
	Throws(() => FileCreateShortcut(spacedFile, "./FileGetShortcut/taken.lnk", , "x"), A_LineNumber, OSError)
	AssertEq("keep", FileRead("./FileGetShortcut/taken.lnk/keep.txt"), A_LineNumber)

	FileDelete("./testshortcut_multi.desktop")
	FileDelete("./testshortcut_link.lnk")
#endif

if (DirExist("./FileGetShortcut"))
	DirDelete("./FileGetShortcut", true)

if (FileExist("./testshortcut.lnk"))
	FileDelete("./testshortcut.lnk")

FileAppend "pass", "*"
