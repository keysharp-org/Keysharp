#NoTrayIcon
#Include <assert>

x := ""

Assert(!(x != ""), A_LineNumber)

Assert(x = "", A_LineNumber)

FileAppend "pass", "*"
