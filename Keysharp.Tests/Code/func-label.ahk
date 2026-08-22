#NoTrayIcon
#Include <assert>

x := 1
goto labelz
y := 2

labelz:
z := 3

AssertEq(x, 1, A_LineNumber)

AssertEq(y, unset, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

FileAppend "pass", "*"
