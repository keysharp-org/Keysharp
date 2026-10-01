#NoTrayIcon
#Include <assert>

x := Ord("t")

Assert(x = 116, A_LineNumber)

x := Ord("et")
			
Assert(x = 101, A_LineNumber)

; A lone surrogate, as SubStr leaves one of a pair, is its own code unit.
AssertEq(Ord(Chr(0x1F600)), 0x1F600, A_LineNumber)
AssertEq(Ord(SubStr(Chr(0x1F600), 2)), 0xDE00, A_LineNumber)

FileAppend "pass", "*"
