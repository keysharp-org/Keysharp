#NoTrayIcon
#Include <assert>

x := "a,b,c,d,e,f"
varct := ""
z := "abcdef"
y := StrReplace(x, ",")


Assert(y = z, A_LineNumber)

y := StrReplace(x, ",", "")

Assert(y = "abcdef", A_LineNumber)

y := StrReplace(x, ",", ".")

Assert(y = "a.b.c.d.e.f", A_LineNumber)

y := StrReplace(x, ",", ".", "On")

Assert(y = "a.b.c.d.e.f", A_LineNumber)

y := StrReplace(x, ",", ".", unset, &varct)

Assert(y = "a.b.c.d.e.f", A_LineNumber)

Assert(varct = 5, A_LineNumber)
	
y := StrReplace(x, ",", ".", , &varct, 3)

Assert(y = "a.b.c.d,e,f", A_LineNumber)

Assert(varct = 3, A_LineNumber)
	
y := StrReplace(x, "")

Assert(y = x, A_LineNumber)

y := StrReplace(x, "a", "A", 1)

Assert(y = "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", "On")

Assert(y = "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", true)

Assert(y = "A,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", 0)

Assert(y = "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", "Off")

Assert(y = "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "A", "1", false)

Assert(y = "1,b,c,d,e,f", A_LineNumber)

y := StrReplace(x, "a", "A", "On", &varct, 9)
		
Assert(y = "A,b,c,d,e,f", A_LineNumber)
	
Assert(varct = 1, A_LineNumber)

FileAppend "pass", "*"
