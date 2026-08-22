#NoTrayIcon
#Include <assert>

x := " test`t"
y := RTrim(x)

Assert(y = " test", A_LineNumber)
	
x := "test"
y := RTrim(x)

Assert(y = "test", A_LineNumber)
	
x := "`ttest "
y := RTrim(x)

Assert(y = "`ttest", A_LineNumber)
	
x := "`ttest`t "
y := RTrim(x)

Assert(y = "`ttest", A_LineNumber)

FileAppend "pass", "*"
