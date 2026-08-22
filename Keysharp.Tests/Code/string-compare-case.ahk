#NoTrayIcon
#Include <assert>

x := "a"
y := "a"

Assert(x = y, A_LineNumber)

AssertEq(x, y, A_LineNumber)

Assert(!(x != y), A_LineNumber)

Assert(!(x !== y), A_LineNumber)

Assert(!(!(x = y)), A_LineNumber)

Assert(!(!(x == y)), A_LineNumber)

x := "a"
y := "A"

Assert(x = y, A_LineNumber)

Assert(!(x == y), A_LineNumber)

Assert(!(x != y), A_LineNumber)

Assert(x !== y, A_LineNumber)

Assert(!(!(x = y)), A_LineNumber)

Assert(!(x == y), A_LineNumber)

FileAppend "pass", "*"
