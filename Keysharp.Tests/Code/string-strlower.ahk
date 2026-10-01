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

AssertEq(y, "All Caps", A_LineNumber)

; Only whitespace starts a word.
AssertEq(StrTitle("hello-world 3rd o'neil"), "Hello-world 3Rd O'neil", A_LineNumber)
AssertEq(StrTitle("mIxEd`tcase`nnext"), "Mixed`tCase`nNext", A_LineNumber)
	
x := "all caps"
y := StrTitle(x)

AssertEq(y, "All Caps", A_LineNumber)
	
x := "All Caps"
y := StrTitle(x)

AssertEq(y, "All Caps", A_LineNumber)

FileAppend "pass", "*"
