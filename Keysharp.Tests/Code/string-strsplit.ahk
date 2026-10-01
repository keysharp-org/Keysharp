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

; The string and the omit characters convert as any string argument does: a number as its text, an object through its
; ToString method or else a TypeError. A delimiter converts the same way unless it is an object, which must be an Array
; of strings, as in AHK.
Assert(StrSplit(12345, 3) = ["12", "45"], A_LineNumber)
Assert(StrSplit("1x1", ",", 1) = ["x"], A_LineNumber)
Throws(() => StrSplit({}), A_LineNumber, TypeError)

; A blank delimiter splits into characters, an empty string or a MaxParts of 0 gives no items, and an empty list or
; an empty delimiter in one is an error.
AssertEq(StrSplit("abc", "").Length, 3, A_LineNumber)
AssertEq(StrSplit("", ",").Length, 0, A_LineNumber)
AssertEq(StrSplit("", "").Length, 0, A_LineNumber)
AssertEq(StrSplit("a,b", ",", , 0).Length, 0, A_LineNumber)
AssertEq(StrSplit("a,b,,", ",", , -5).Length, 4, A_LineNumber)
Throws(() => StrSplit("a,b", [",", ""]), A_LineNumber, ValueError)
Throws(() => StrSplit("a,b", []), A_LineNumber, ValueError)
Throws(() => StrSplit("a,b", Map()), A_LineNumber, ValueError)
Throws(() => StrSplit("1a2", [1]), A_LineNumber, ValueError)

; The last of MaxParts characters is the rest of the string, without OmitChars at either end.
y := StrSplit(" a b c ", , " ", 2)
AssertEq(y.Length, 2, A_LineNumber)
AssertEq(y[2], "b c", A_LineNumber)

FileAppend "pass", "*"
