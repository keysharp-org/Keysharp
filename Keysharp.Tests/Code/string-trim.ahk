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

FileAppend "pass", "*"
