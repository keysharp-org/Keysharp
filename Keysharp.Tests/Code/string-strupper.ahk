#NoTrayIcon
#Include <assert>

x := "ALL CAPS"
y := StrUpper(x)

AssertEq(y, "ALL CAPS", A_LineNumber)
	
x := "AlL CaPs"
y := StrUpper(x)

AssertEq(y, "ALL CAPS", A_LineNumber)
	
x := "all caps"
y := StrUpper(x)

AssertEq(y, "ALL CAPS", A_LineNumber)
	
x := ""
y := StrUpper(x)

AssertEq(y, "", A_LineNumber)
	
x := "ALL CAPS"
y := StrTitle(x)

AssertEq(y, "ALL CAPS", A_LineNumber)
	
x := "all caps"
y := StrTitle(x)

AssertEq(y, "All Caps", A_LineNumber)
	
x := "All Caps"
y := StrTitle(x)

AssertEq(y, "All Caps", A_LineNumber)

FileAppend "pass", "*"
