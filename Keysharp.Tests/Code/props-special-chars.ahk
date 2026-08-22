#NoTrayIcon
#Include <assert>

x := A_Space
y := A_Tab

Assert(x = " ", A_LineNumber)

Assert(y = "`t", A_LineNumber)

FileAppend "pass", "*"
