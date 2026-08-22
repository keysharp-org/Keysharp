#NoTrayIcon
#Include <assert>

x := "`""

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(34), A_LineNumber)

x := '"'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(34), A_LineNumber)

x := "``"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(0x60), A_LineNumber)

x := '""'

AssertEq(x.Length, 2, A_LineNumber)

Assert(x[1] == Chr(34) && x[2] == Chr(34), A_LineNumber)

x := '`''

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(0x27), A_LineNumber)

x := "`n"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(10), A_LineNumber)

x := "`r"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(13), A_LineNumber)

x := '`n'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(10), A_LineNumber)

x := '`r'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(13), A_LineNumber)

x := "`s"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], " ", A_LineNumber)

x := "`b"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(8), A_LineNumber)

x := '`s'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], " ", A_LineNumber)

x := '`b'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(8), A_LineNumber)

x := "`v"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(11), A_LineNumber)

x := "`f"

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(12), A_LineNumber)

x := '`v'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(11), A_LineNumber)

x := '`f'

AssertEq(x.Length, 1, A_LineNumber)

AssertEq(x[1], Chr(12), A_LineNumber)

; There was once a bug where certain verbatim strings being passed as function arguments wasn't parsing.
strargfunc(x)
{
    return x
}

strargfunc2(x, y)
{
    return x
}

value := 123
xx := strargfunc('"' value "'")

AssertEq(xx, "`"123'", A_LineNumber)

xx := ""
xx := strargfunc2('"' value "'", -1)

AssertEq(xx, "`"123'", A_LineNumber)

FileAppend "pass", "*"
