#NoTrayIcon
#Include <assert>

x := 0xAA

Assert(x = 170, A_LineNumber)

x := 0xBB

Assert(x = 187, A_LineNumber)

x := 0xCC

Assert(x = 204, A_LineNumber)

x := 0xDD

Assert(x = 221, A_LineNumber)

x := 0xDd

Assert(x = 221, A_LineNumber)

FileAppend "pass", "*"
