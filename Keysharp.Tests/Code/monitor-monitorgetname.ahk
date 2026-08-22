#NoTrayIcon
#Include <assert>

ct := MonitorGetCount()
names := ""

loop ct
	names .= MonitorGetName(A_Index)
	
Assert(names != "", A_LineNumber)

FileAppend "pass", "*"
