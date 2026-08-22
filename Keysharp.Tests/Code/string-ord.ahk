#NoTrayIcon
#Include <assert>

x := Ord("t")

Assert(x = 116, A_LineNumber)

x := Ord("et")
			
Assert(x = 101, A_LineNumber)

FileAppend "pass", "*"
