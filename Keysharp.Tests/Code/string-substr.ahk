#NoTrayIcon
#Include <assert>

x := "abcdefghijkl"
y := SubStr(x)

Assert(x = y, A_LineNumber)

y := SubStr(x, 1, 1)

Assert("a" = y, A_LineNumber)

y := SubStr(x, 1, 5)

Assert("abcde" = y, A_LineNumber)

y := SubStr(x, 1, 11)

Assert("abcdefghijk" = y, A_LineNumber)

y := SubStr(x, 1, -1)

Assert("abcdefghijk" = y, A_LineNumber)

y := SubStr(x, 1, -11)

Assert("a" = y, A_LineNumber)
	
y := SubStr(x, 1, -12)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, 1, -13)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, 4, -3)

Assert("defghi" = y, A_LineNumber)
	
y := SubStr(x, 6, -6)

Assert("f" = y, A_LineNumber)
	
y := SubStr(x, 7, -6)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, 7, -7)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, 0)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, StrLen(x) + 1)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, 2, 1)

Assert("b" = y, A_LineNumber)
	
y := SubStr(x, 2, 1)

Assert("b" = y, A_LineNumber)
	
y := SubStr(x, 4, 3)

Assert("def" = y, A_LineNumber)
	
y := SubStr(x, 10, 3)

Assert("jkl" = y, A_LineNumber)
	
y := SubStr(x, 12, 1)

Assert("l" = y, A_LineNumber)
	
y := SubStr(x, -1)

Assert("l" = y, A_LineNumber)
	
y := SubStr(x, -5)

Assert("hijkl" = y, A_LineNumber)
	
y := SubStr(x, -12)

Assert(x = y, A_LineNumber)

y := SubStr(x, -13)

Assert(x = y, A_LineNumber)
	
y := SubStr(x, -5, 5)

Assert("hijkl" = y, A_LineNumber)
	
y := SubStr(x, -5, 3)

Assert("hij" = y, A_LineNumber)
	
y := SubStr(x, -5, -3)

Assert("hi" = y, A_LineNumber)
	
y := SubStr(x, -5, -5)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, -5, -6)

Assert("" = y, A_LineNumber)
	
y := SubStr(x, -5, -13)

Assert("" = y, A_LineNumber)

FileAppend "pass", "*"
