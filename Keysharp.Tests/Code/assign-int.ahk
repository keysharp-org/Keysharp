#NoTrayIcon
#Include <assert>

x := 1
y:=2
z := x + y

Assert(!(x != 1), A_LineNumber)

Assert(!(y!=2), A_LineNumber)
	
Assert(!(z!=	3), A_LineNumber)
	
Assert(x = 1, A_LineNumber)

Assert(y = 2, A_LineNumber)

Assert(z = 3, A_LineNumber)

Assert(!(x != 1), A_LineNumber)
	
Assert(!(y!=2), A_LineNumber)
	
Assert(!(z!=	3), A_LineNumber)
	
Assert(x = 1, A_LineNumber)

Assert(y = 2, A_LineNumber)

Assert(z = 3, A_LineNumber)

x := 1 + 2

Assert(!(x != 3), A_LineNumber)

x := -1 + -2

Assert(!(x != -3), A_LineNumber)

FileAppend "pass", "*"
