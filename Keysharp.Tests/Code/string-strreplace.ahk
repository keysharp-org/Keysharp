#NoTrayIcon
#Include <assert>

x := "a,b,c,d,e,f"
varct := ""
z := "abcdef"
y := StrReplace(x, ",")


AssertEq(y, z, A_LineNumber)

y := StrReplace(x, ",", "")

AssertEq(y, "abcdef", A_LineNumber)

y := StrReplace(x, ",", ".")

AssertEq(y, "a.b.c.d.e.f", A_LineNumber)

y := StrReplace(x, ",", ".", "On")

AssertEq(y, "a.b.c.d.e.f", A_LineNumber)

y := StrReplace(x, ",", ".", unset, &varct)

AssertEq(y, "a.b.c.d.e.f", A_LineNumber)

AssertEq(varct, 5, A_LineNumber)
	
y := StrReplace(x, ",", ".", , &varct, 3)

AssertEq(y, "a.b.c.d,e,f", A_LineNumber)

AssertEq(varct, 3, A_LineNumber)
	
y := StrReplace(x, "")

AssertEq(y, x, A_LineNumber)

y := StrReplace(x, "a", "A", 1)

AssertEq(y, "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", "On")

AssertEq(y, "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", true)

AssertEq(y, "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", 0)

AssertEq(y, "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", "Off")

AssertEq(y, "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", false)

AssertEq(y, "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", "On", &varct, 9)
		
AssertEq(y, "A,b,c,d,e,f", A_LineNumber)
	
AssertEq(varct, 1, A_LineNumber)

FileAppend "pass", "*"
