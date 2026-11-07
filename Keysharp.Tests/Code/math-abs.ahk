#NoTrayIcon
#Include <assert>

AssertEq(1, Abs(1), A_LineNumber)

AssertEq(1, Abs(-1), A_LineNumber)

AssertEq(9.81, Abs(-9.81), A_LineNumber)

AssertEq(0, Abs(0), A_LineNumber)

AssertEq(0, Abs(-0), A_LineNumber)

; The least Integer has no positive counterpart and is returned unchanged, as in AutoHotkey.
x := -9223372036854775807 - 1
AssertEq(Abs(x), x, A_LineNumber)

FileAppend "pass", "*"
