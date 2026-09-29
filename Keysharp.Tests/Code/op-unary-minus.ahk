#NoTrayIcon
#Include <assert>

x := 2
y := -2

Assert(y = -2, A_LineNumber)

Assert(!(y != -2), A_LineNumber)

y := -y

Assert(y = 2, A_LineNumber)

Assert(!(y != 2), A_LineNumber)

y := -(x * y)

Assert(y = -4, A_LineNumber)

Assert(!(y != -4), A_LineNumber)

y := y * -1

Assert(y = 4, A_LineNumber)

Assert(!(y != 4), A_LineNumber)

y := y / -1

Assert(y = -4, A_LineNumber)

Assert(!(y != -4), A_LineNumber)

y := -(y / -2)

Assert(y = -2, A_LineNumber)

Assert(!(y != -2), A_LineNumber)
	
y := -4 + 5 * -10

Assert(y = -54, A_LineNumber)

Assert(!(y != -54), A_LineNumber)

y := -2.5

Assert(y = -2.5, A_LineNumber)

y := 2.5
y := -y

Assert(y = -2.5, A_LineNumber)

y := "2.5"
y := -y

Assert(y = -2.5, A_LineNumber)

y := "-2.5"
y := -y

Assert(y = 2.5, A_LineNumber)

y := "0x0A"
y := -y

Assert(y = -10, A_LineNumber)

y := "-0x0A"
y := -y

Assert(y = 10, A_LineNumber)

; Unary plus converts to a number.
x := "5"
AssertEq(Type(+x), "Integer", A_LineNumber)
x := "5.0"
AssertEq(+x, 5.0, A_LineNumber)
x := true
AssertEq(+x, 1, A_LineNumber)
x := "abc"
Throws(() => +x, A_LineNumber, TypeError)

FileAppend "pass", "*"
