#NoTrayIcon
#Include <assert>

x := "a,b,c,d"
y := StrSplit(x, ",")
exp := [ "a", "b", "c", "d" ]

Assert(exp = y, A_LineNumber)

x := "abcd"
y := StrSplit(x)

Assert(exp = y, A_LineNumber)

x := "	a, b,c ,d	"
y := StrSplit(x, ",", "`t ")

Assert(exp = y, A_LineNumber)

x := "	a, b-c _d	"
y := StrSplit(x, [ ",", "-", "_" ], "`t ")

Assert(exp = y, A_LineNumber)

x := "abcd"
y := StrSplit(x, , , 1)
exp := [ "abcd" ]

Assert(exp = y, A_LineNumber)

y := StrSplit(x, , , 2)
exp := [ "a", "bcd" ]

Assert(exp = y, A_LineNumber)

y := StrSplit(x, , , 3)
exp := [ "a", "b", "cd" ]

Assert(exp = y, A_LineNumber)

y := StrSplit(x, , , 4)
exp := [ "a", "b", "c", "d" ]

Assert(exp = y, A_LineNumber)

y := StrSplit(x, , , 5)
exp := [ "a", "b", "c", "d" ]

Assert(exp = y, A_LineNumber)

x := "a,b,c,d"
y := StrSplit(x, ",", , 3)
exp := [ "a", "b", "c,d" ]

Assert(exp = y, A_LineNumber)

x := "	a, b-c _d	"
y := StrSplit(x, [ ",", "-", "_" ], "`t ", 3)
exp := [ "a", "b", "c _d" ]

Assert(exp = y, A_LineNumber)

x := "a | b | c"
y := StrSplit(x, " | ")
exp := [ "a", "b", "c" ]

Assert(exp = y, A_LineNumber)

x := "a | b , c"
y := StrSplit(x, [" | ", " , "])
exp := [ "a", "b", "c" ]

Assert(exp = y, A_LineNumber)

FileAppend "pass", "*"
