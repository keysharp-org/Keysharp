#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

if (FileExist("./testini2.ini"))
	FileDelete("./testini2.ini")

dir := "../../../Keysharp.Tests/Code/testini.ini"
FileCopy(dir, "./testini2.ini", true)

Assert(FileExist("./testini2.ini"), A_LineNumber)

val := IniRead("./testini2.ini", "sectionone", "keyval")

AssertEq("theval", val, A_LineNumber)

val := IniRead("./testini2.ini", "SectionOne", "keyval") ; Section names are case-insensitive.

AssertEq("theval", val, A_LineNumber)

val := IniRead("./testini2.ini", "sectiontwo")

AssertEq("groupkey1=groupval1`ngroupkey2=groupval2`ngroupkey3=groupval3", val, A_LineNumber)

val := IniRead("./testini2.ini")

AssertEq("sectionone`nsectiontwo`nsectionthree", val, A_LineNumber)

IniWrite("thevalnew", "./testini2.ini", "sectionone", "keyval")
val := IniRead("./testini2.ini", "sectionone", "keyval")

AssertEq("thevalnew", val, A_LineNumber)

str := "groupkey11=groupval11`ngroupkey12=groupval12`ngroupkey13=groupval13`n"
IniWrite(str, "./testini2.ini", "sectiontwo")
val := IniRead("./testini2.ini", "sectiontwo")

AssertEq("groupkey11=groupval11`ngroupkey12=groupval12`ngroupkey13=groupval13", val, A_LineNumber)

IniDelete("./testini2.ini", "sectiontwo", "groupkey11")
val := IniRead("./testini2.ini", "sectiontwo")

AssertEq("groupkey12=groupval12`ngroupkey13=groupval13", val, A_LineNumber)

b := false

try
{
    val := IniRead("./testini2.ini", "sectiontwo", "doesntexist")
}
catch
{
    b := true
}

Assert(b, A_LineNumber)
    
b := false

try
{
    val := IniRead("./testini2.ini", "sectiontwo", "thiskeydoesntexist", 123)
}
catch
{
    b := true
}

Assert(!b && val == 123, A_LineNumber)

IniDelete("./testini2.ini", "sectiontwo")
val := IniRead("./testini2.ini", "sectiontwo",, "")

AssertEq("", val, A_LineNumber)

; A value containing '=' reads back whole, and other keys survive writing and deleting one key.
IniWrite("http://x/?a=b&c=d", "./testini2.ini", "sectionone", "url")
AssertEq(IniRead("./testini2.ini", "sectionone", "url"), "http://x/?a=b&c=d", A_LineNumber)
IniWrite("again", "./testini2.ini", "sectionone", "keyval")
AssertEq(IniRead("./testini2.ini", "sectionone", "url"), "http://x/?a=b&c=d", A_LineNumber)
IniDelete("./testini2.ini", "sectionone", "keyval")
AssertEq(IniRead("./testini2.ini", "sectionone", "keyval", "gone"), "gone", A_LineNumber)
AssertEq(IniRead("./testini2.ini", "sectionone", "url"), "http://x/?a=b&c=d", A_LineNumber)
AssertEq(IniRead("./testini2.ini", "sectionthree", "groupkey11"), "groupval11", A_LineNumber)
AssertEq(IniRead("./testini2.ini"), "sectionone`nsectionthree", A_LineNumber)

if (FileExist("./testini2.ini"))
	FileDelete("./testini2.ini")

; A new file keeps characters outside the ANSI code page.
if (FileExist("./testini3.ini"))
	FileDelete("./testini3.ini")

unicodeVal := "a" Chr(0x2713) "b"
IniWrite(unicodeVal, "./testini3.ini", "s", "k")
AssertEq(IniRead("./testini3.ini", "s", "k"), unicodeVal, A_LineNumber)
AssertEq(IniRead("./testini3.ini"), "s", A_LineNumber)
FileDelete("./testini3.ini")

; Writing one key and deleting another leaves the comment, the blank line and the line endings around them alone.
if (FileExist("./testini4.ini"))
	FileDelete("./testini4.ini")

FileAppend("[s]`r`n; note`r`na=1`r`n`r`nb=2`r`nc=3`r`n", "./testini4.ini", "UTF-16")
IniWrite("4", "./testini4.ini", "s", "d")
IniDelete("./testini4.ini", "s", "b")
AssertEq(FileRead("./testini4.ini"), "[s]`r`n; note`r`na=1`r`n`r`nc=3`r`nd=4`r`n", A_LineNumber)
FileDelete("./testini4.ini")

; Empty names are supplied names; omitting Key operates on the whole section.
IniWrite("blank key", "./testini5.ini", "s", "")
AssertEq(IniRead("./testini5.ini", "s", ""), "blank key", A_LineNumber)
IniWrite("keep", "./testini5.ini", "s", "k")
IniDelete("./testini5.ini", "s", "")
AssertEq(IniRead("./testini5.ini", "s", "", "gone"), "gone", A_LineNumber)
AssertEq(IniRead("./testini5.ini", "s", "k"), "keep", A_LineNumber)
IniWrite("blank section", "./testini5.ini", "", "k")
AssertEq(IniRead("./testini5.ini", "", "k"), "blank section", A_LineNumber)
FileDelete("./testini5.ini")

FileAppend("[s] ignored]`nk=first`n", "./testini5.ini", "UTF-16")
AssertEq(IniRead("./testini5.ini", "s", "k"), "first", A_LineNumber)
FileDelete("./testini5.ini")

#if !WINDOWS
	#Import Ks { Clr }
	System := Clr.Load("System")
	Target := A_Temp "/keysharp-ini-target-" ProcessExist() ".ini"
	Link := A_Temp "/keysharp-ini-link-" ProcessExist() ".ini"
	try
	{
		FileAppend("[s]`na=1`nb=2`n", Target, "UTF-8-RAW")
		System.IO.File.CreateSymbolicLink(Link, Target)
		IniWrite("3", Link, "s", "a")
		AssertEq(System.IO.FileInfo(Link).LinkTarget, Target, A_LineNumber)
		AssertEq(IniRead(Target, "s", "a"), "3", A_LineNumber)
		IniDelete(Link, "s", "b")
		AssertEq(System.IO.FileInfo(Link).LinkTarget, Target, A_LineNumber)
		AssertEq(IniRead(Target, "s", "b", "gone"), "gone", A_LineNumber)
	}
	finally
	{
		if FileExist(Link)
			FileDelete(Link)
		if FileExist(Target)
			FileDelete(Target)
	}
#endif

FileAppend "pass", "*"
