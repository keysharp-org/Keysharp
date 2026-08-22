#NoTrayIcon
#Include <assert>

if (FileExist("./testini2.ini"))
	FileDelete("./testini2.ini")

dir := "../../../Keysharp.Tests/Code/testini.ini"
FileCopy(dir, "./testini2.ini", true)

Assert(FileExist("./testini2.ini"), A_LineNumber)

val := IniRead("./testini2.ini", "sectionone", "keyval")

AssertEq("theval", val, A_LineNumber)

val := IniRead("./testini2.ini", "sectiontwo")

AssertEq("groupkey1=groupval1`ngroupkey2=groupval2`ngroupkey3=groupval3", val, A_LineNumber)

val := IniRead("./testini2.ini")

AssertEq("sectionone`nsectiontwo`nsectionthree", val, A_LineNumber)

IniWrite("thevalnew", "./testini2.ini", "sectionone", "keyval")
val := IniRead("./testini2.ini", "sectionone", "keyval")

AssertEq("thevalnew", val, A_LineNumber)

str := "groupkey11=groupval11`ngroupkey12=groupval12`ngroupkey13=groupval13"
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
	
if (FileExist("./testini2.ini"))
	FileDelete("./testini2.ini")

FileAppend "pass", "*"
