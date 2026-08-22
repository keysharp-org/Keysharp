#NoTrayIcon
#Include <assert>

o := "ooo"

Assert(!(not (StrCompare(o, "blue") > 0 and StrCompare(o, "red") < 0)), A_LineNumber)

Assert(not (StrCompare(o, "red") > 0 and StrCompare(o, "blue") < 0), A_LineNumber)

Assert(not (StrCompare(o, "xxx") > 0 and StrCompare(o, "zzz") < 0), A_LineNumber)
	
Assert(not (StrCompare(o, "zzz") > 0 and StrCompare(o, "xxx") < 0), A_LineNumber)

FileAppend "pass", "*"
