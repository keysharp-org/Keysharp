#NoTrayIcon
#Include <assert>

x := 1
y := &x

Assert(!(x = y), A_LineNumber)

FileAppend "pass", "*"
