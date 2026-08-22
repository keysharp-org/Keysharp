#NoTrayIcon
#Include <assert>

b := "blue"
o := "ooo"
r := "red"
x := "xxx"
z := "zzz"

Assert(StrCompare(o, b) > 0 and StrCompare(o, r) < 0, A_LineNumber)
	
Assert(!(StrCompare(o, r) > 0 and StrCompare(o, b) < 0), A_LineNumber)

Assert(!(StrCompare(o, x) > 0 and StrCompare(o, z) < 0), A_LineNumber)
	
Assert(!(StrCompare(o, z) > 0 and StrCompare(o, x) < 0), A_LineNumber)

FileAppend "pass", "*"
