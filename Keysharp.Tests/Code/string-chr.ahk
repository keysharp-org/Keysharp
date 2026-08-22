#NoTrayIcon
#Include <assert>

x := Chr(116)

Assert(x = "t", A_LineNumber)

FileAppend "pass", "*"
