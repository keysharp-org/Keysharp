#NoTrayIcon
#Include <assert>

val := SysGet(80)

Assert(val > 0, A_LineNumber)

val := SysGet(0)

AssertEq(val, A_ScreenWidth, A_LineNumber)

val := SysGet(43)

Assert(val > 0, A_LineNumber)

val := SysGet(19)

Assert(val > 0, A_LineNumber)

FileAppend "pass", "*"
