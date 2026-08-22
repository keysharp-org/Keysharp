#NoTrayIcon
#Include <assert>

x := 1
y := x

Assert(x = 1, A_LineNumber)

Assert(x = 1, A_LineNumber)
	
Assert(!(x != 1), A_LineNumber)

Assert(y = 1, A_LineNumber)
	
Assert(!(y != 1), A_LineNumber)

Assert(!(x != y), A_LineNumber)

a := b := 123

AssertEq(a, 123, A_LineNumber)

AssertEq(b, 123, A_LineNumber)

FileAppend "pass", "*"
