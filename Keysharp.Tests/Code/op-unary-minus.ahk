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

FileAppend "pass", "*"
