#NoTrayIcon
#Include <assert>

x := "hello"

Assert(!(x != "hello"), A_LineNumber)
	
Assert(x = "hello", A_LineNumber)

FileAppend "pass", "*"
