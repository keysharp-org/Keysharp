#NoTrayIcon
#Include <assert>

x := "ALL CAPS"
y := StrLower(x)

AssertEq(y, "all caps", A_LineNumber)
	
x := "AlL CaPs"
y := StrLower(x)

AssertEq(y, "all caps", A_LineNumber)
	
x := "all caps"
y := StrLower(x)

AssertEq(y, "all caps", A_LineNumber)
	
x := ""
y := StrLower(x)

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
