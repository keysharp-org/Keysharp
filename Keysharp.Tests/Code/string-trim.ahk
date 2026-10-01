#NoTrayIcon
#Include <assert>

x := " test`t"
y := Trim(x)

Assert(y = "test", A_LineNumber)
	
x := "test"
y := Trim(x)

Assert(y = "test", A_LineNumber)
	
x := "`ttest "
y := Trim(x)

Assert(y = "test", A_LineNumber)
	
x := "`ttest`t "
y := Trim(x)

Assert(y = "test", A_LineNumber)

AssertEq(Trim(" `ttest`t "), "test", A_LineNumber)

; An empty OmitChars trims nothing.
AssertEq(Trim(" x`t", ""), " x`t", A_LineNumber)
AssertEq(Trim("--x-y--", "-"), "x-y", A_LineNumber)

FileAppend "pass", "*"
