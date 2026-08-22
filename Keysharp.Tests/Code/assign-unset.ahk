#NoTrayIcon
#Include <assert>

x := ""

Assert(!(x != ""), A_LineNumber)
	
Assert(x = "", A_LineNumber)

x := 123
x := unset

Assert(x is unset, A_LineNumber)

Assert(x = unset, A_LineNumber)

Assert(x = unset, A_LineNumber)

Assert(unset = x, A_LineNumber)
	
Assert(unset = x, A_LineNumber)

Assert(!(x != unset), A_LineNumber)

Assert(!(x !== unset), A_LineNumber)

Assert(!(unset != x), A_LineNumber)
	
Assert(!(unset !== x), A_LineNumber)

FileAppend "pass", "*"
