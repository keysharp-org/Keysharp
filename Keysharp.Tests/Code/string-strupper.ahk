#NoTrayIcon
#Include <assert>

x := "ALL CAPS"
y := StrUpper(x)

Assert(y = "ALL CAPS", A_LineNumber)
	
x := "AlL CaPs"
y := StrUpper(x)

Assert(y = "ALL CAPS", A_LineNumber)
	
x := "all caps"
y := StrUpper(x)

Assert(y = "ALL CAPS", A_LineNumber)
	
x := ""
y := StrUpper(x)

Assert(y = "", A_LineNumber)
	
x := "ALL CAPS"
y := StrTitle(x)

Assert(y = "ALL CAPS", A_LineNumber)
	
x := "all caps"
y := StrTitle(x)

Assert(y = "All Caps", A_LineNumber)
	
x := "All Caps"
y := StrTitle(x)

Assert(y = "All Caps", A_LineNumber)

FileAppend "pass", "*"
