#NoTrayIcon
#Include <assert>

z := ""

Loop Parse "hello"
{
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

z := ""

Loop Parse "hello" {
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

z := ""

Loop Parse "hello", , "l"
{
	z .= A_LoopField
}

AssertEq(z, "heo", A_LineNumber)

z := ""

Loop Parse "hello", , "l" {
	z .= A_LoopField
}

AssertEq(z, "heo", A_LineNumber)

x := "hello"
z := ""

Loop Parse x
{
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

global x := "hello"
y := "x"
z := ""

Loop Parse %y%
{
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

x := "hel,lo"
z := ""

Loop Parse x, ","
{
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

x := "hel,lo"
z := ""

Loop Parse x, ",", "l"
{
	z .= A_LoopField
}

AssertEq(z, "heo", A_LineNumber)
	
x := "hel,lo"
y := ","
z := ""

Loop Parse x, y, "l" ; this is a comment
{
	z .= A_LoopField
}

AssertEq(z, "heo", A_LineNumber)

v := "l"
x := "hel,lo"
y := ","
z := ""

Loop Parse x, y, v ; another comment
{
	z .= A_LoopField
}

AssertEq(z, "heo", A_LineNumber)
	
x := "`"first field`",SecondField,`"the word `"`"special`"`" is quoted literally`",,`"last field, has literal comma`""
z := ""

Loop Parse x, "csv"
{
	z .= A_LoopField
}

AssertEq(z, "first fieldSecondFieldthe word `"special`" is quoted literallylast field, has literal comma", A_LineNumber)

x := "h.e-l,l;o"
y := ".-,;"
z := ""

Loop Parse x, y
{
	z .= A_LoopField
}

AssertEq(z, "hello", A_LineNumber)

ParseFields(input, delims := "", omit := "") {
	s := ""

	Loop Parse input, delims, omit
		s .= "[" A_LoopField "]"

	return s
}

; Without OmitChars nothing is trimmed, a space included, and an empty field is still a field.
AssertEq(ParseFields("a b"), "[a][ ][b]", A_LineNumber)
AssertEq(ParseFields(" a , b ", ","), "[ a ][ b ]", A_LineNumber)
AssertEq(ParseFields("a,,b,", ","), "[a][][b][]", A_LineNumber)
AssertEq(ParseFields("a,  ,b", ",", " "), "[a][][b]", A_LineNumber)
AssertEq(ParseFields("a b", , " "), "[a][b]", A_LineNumber)
AssertEq(ParseFields(" x , y ", "CSV", " "), "[x][y]", A_LineNumber)
AssertEq(ParseFields(""), "", A_LineNumber)
AssertEq(ParseFields("", ","), "", A_LineNumber)
AssertEq(ParseFields("", "CSV"), "", A_LineNumber)

FileAppend "pass", "*"
