#NoTrayIcon
#Include <assert>

o := "ooo"

Assert(StrCompare(o, "blue") > 0 and StrCompare(o, "red") < 0, A_LineNumber)

Assert(!(StrCompare(o, "red") > 0 and StrCompare(o, "blue") < 0), A_LineNumber)

Assert(!(StrCompare(o, "xxx") > 0 and StrCompare(o, "zzz") < 0), A_LineNumber)
	
Assert(!(StrCompare(o, "zzz") > 0 and StrCompare(o, "xxx") < 0), A_LineNumber)

FileAppend "pass", "*"
