#NoTrayIcon
#Include <assert>

monget := MonitorGetPrimary()

Assert(monget >= 0, A_LineNumber)

FileAppend "pass", "*"
