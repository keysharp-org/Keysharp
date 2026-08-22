#NoTrayIcon
#Include <assert>

x := EnvGet("PATH")

Assert(x != "", A_LineNumber) 

dummy := "dummynothing123"
x := EnvGet(dummy)

AssertEq(x, "", A_LineNumber)

FileAppend "pass", "*"
