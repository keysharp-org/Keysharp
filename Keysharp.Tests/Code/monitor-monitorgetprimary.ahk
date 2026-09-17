#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

primary := MonitorGetPrimary()
Assert(primary > 0 && primary <= MonitorGetCount(), A_LineNumber)

FileAppend "pass", "*"
