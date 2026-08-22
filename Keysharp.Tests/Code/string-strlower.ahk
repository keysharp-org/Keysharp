#NoTrayIcon
#Include <assert>

x := "ALL CAPS"
y := StrLower(x)

Assert(y = "all caps", A_LineNumber)
	
x := "AlL CaPs"
y := StrLower(x)

Assert(y = "all caps", A_LineNumber)
	
x := "all caps"
y := StrLower(x)

Assert(y = "all caps", A_LineNumber)
	
x := ""
y := StrLower(x)

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
