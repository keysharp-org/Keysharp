#NoTrayIcon
#Include <assert>

l :=
t :=
r :=
b :=
monget := MonitorGetWorkArea(, &l, &t, &r, &b)

Assert(l >= 0 && r >= 0 && t >= 0 && b >= 0 && monget > 0, A_LineNumber)

FileAppend "pass", "*"
