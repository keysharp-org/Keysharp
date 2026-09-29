#NoTrayIcon
#Include <assert>

AssertEq(1, 1.0, A_LineNumber)

Assert(1 = "1", A_LineNumber)

AssertEq(1, "1", A_LineNumber)

Assert(1 != 2.0, A_LineNumber)

Assert("0.10" = 0.1, A_LineNumber)

Assert(513 = "0x201", A_LineNumber)

AssertEq(513.0, "0x201", A_LineNumber)

Assert("a" = "A", A_LineNumber)

Assert(!("a" == "A"), A_LineNumber)

; = and != agree on Arrays, comparing elements with =.
x := [1, "A"], y := [1.0, "a"]
Assert(x = y && !(x != y), A_LineNumber)
Assert([1, 2] != [1, 3], A_LineNumber)
x := [1, , 3]
Assert(x = [1, , 3] && x != [1, 2, 3], A_LineNumber)

; NaN equals nothing, itself included.
x := Buffer(8), NumPut("int64", 0x7FF8000000000000, x), y := NumGet(x, "double")
Assert(!(y = y) && y != y, A_LineNumber)

FileAppend "pass", "*"
