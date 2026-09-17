#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

Assert(MonitorGetCount() > 0, A_LineNumber)

FileAppend "pass", "*"
