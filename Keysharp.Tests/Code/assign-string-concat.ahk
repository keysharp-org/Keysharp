#NoTrayIcon
#Include <assert>

x := "hello"
y := x " world"
y := x . " world"

Assert(!(x != "hello"), A_LineNumber)
	
Assert(!(y != "hello world"), A_LineNumber)
	
Assert(y = "hello world", A_LineNumber)
	
y := x " world"

Assert(!(y != "hello world"), A_LineNumber)
	
Assert(y = "hello world", A_LineNumber)

y := x . " world " x
	
AssertEq(y, "hello world hello", A_LineNumber)

y := x " world " . x
	
AssertEq(y, "hello world hello", A_LineNumber)

y := x . " world " . x
	
AssertEq(y, "hello world hello", A_LineNumber)

y := x " world " x
	
AssertEq(y, "hello world hello", A_LineNumber)

FileAppend "pass", "*"
