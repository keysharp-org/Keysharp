#NoTrayIcon
#Include <assert>

x := " test`t"
y := LTrim(x)

Assert(y = "test`t", A_LineNumber)
	
x := "test"
y := LTrim(x)

Assert(y = "test", A_LineNumber)
	
x := "`ttest "
y := LTrim(x)

Assert(y = "test ", A_LineNumber)
	
x := "`ttest`t "
y := LTrim(x)

Assert(y = "test`t ", A_LineNumber)

AssertEq(LTrim(" `ttest`t "), "test`t ", A_LineNumber)

; An empty OmitChars trims nothing.
AssertEq(LTrim(" x", ""), " x", A_LineNumber)
AssertEq(LTrim("--x-y--", "-"), "x-y--", A_LineNumber)

FileAppend "pass", "*"
