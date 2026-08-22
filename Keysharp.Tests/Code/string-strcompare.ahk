#NoTrayIcon
#Include <assert>

x := "a"
y := "b"
z := StrCompare(x, y)

Assert(z = -1, A_LineNumber)

x := "a"
y := "a"
z := StrCompare(x, y)

Assert(z = 0, A_LineNumber)
	
x := "b"
y := "a"
z := StrCompare(x, y)

Assert(z = 1, A_LineNumber)

x := "a"
y := "B"
z := StrCompare(x, y)

Assert(z = -1, A_LineNumber)

x := "A"
y := "a"
z := StrCompare(x, y)

Assert(z = 0, A_LineNumber)

z := StrCompare(x, y, 0)

Assert(z = 0, A_LineNumber)
	
z := StrCompare(x, y, "off")

Assert(z = 0, A_LineNumber)

z := StrCompare(x, y, false)

Assert(z = 0, A_LineNumber)

x := "b"
y := "A"
z := StrCompare(x, y)

Assert(z = 1, A_LineNumber)
	
x := "A"
y := "a"
z := StrCompare(x, y, 1)

Assert(z < 0, A_LineNumber)

z := StrCompare(x, y, "on")

Assert(z < 0, A_LineNumber)
	
z := StrCompare(x, y, true)

Assert(z < 0, A_LineNumber)

x := "A11"
y := "A100"
z := StrCompare(x, y, 1)

Assert(z > 0, A_LineNumber)
	
x := "A11"
y := "A100"
z := StrCompare(x, y, "logical")

Assert(z < 0, A_LineNumber)

FileAppend "pass", "*"
