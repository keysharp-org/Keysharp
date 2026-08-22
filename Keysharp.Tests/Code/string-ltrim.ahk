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

FileAppend "pass", "*"
