#NoTrayIcon
#Include <assert>

x := 1.123

Assert(x = 1.123, A_LineNumber)

Assert(!(x != 1.123), A_LineNumber)
	
Assert(x = 1.123, A_LineNumber)

Assert(!(x != 1.123), A_LineNumber)
	
x := 1.123 + 1

Assert(!(x != 2.123), A_LineNumber)

x := 1.123 * 2

Assert(!(x != 2.246), A_LineNumber)

FileAppend "pass", "*"
