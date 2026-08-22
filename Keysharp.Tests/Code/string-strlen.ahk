#NoTrayIcon
#Include <assert>

x := "test"
y := StrLen(x)

Assert(y = 4, A_LineNumber)
	
x := ""
y := StrLen(x)

Assert(y = 0, A_LineNumber)

FileAppend "pass", "*"
