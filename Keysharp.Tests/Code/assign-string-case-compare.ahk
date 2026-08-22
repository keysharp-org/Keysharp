#NoTrayIcon
#Include <assert>

x := "HeLlO WoRlD"

AssertEq(x, "HeLlO WoRlD", A_LineNumber)

Assert(!(x == "hello world"), A_LineNumber)

Assert(x = "HeLlO WoRlD", A_LineNumber)
	
Assert(x = "hello world", A_LineNumber)


Assert(!(!(x == "HeLlO WoRlD")), A_LineNumber)

Assert(!(x == "hello world"), A_LineNumber)

Assert(!(!(x = "HeLlO WoRlD")), A_LineNumber)
	
Assert(!(!(x = "hello world")), A_LineNumber)

FileAppend "pass", "*"
