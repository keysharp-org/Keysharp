#NoTrayIcon
#Include <assert>

monget := MonitorGetCount()

Assert(monget >= 0, A_LineNumber)

FileAppend "pass", "*"
