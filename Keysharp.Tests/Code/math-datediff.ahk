#NoTrayIcon
#Include <assert>

d1 := "20050126"
d2 := "20040126"
val := DateDiff(d1, d2, "days")

AssertEq(val, 366, A_LineNumber)

d1 := "20230110"
d2 := "20230115"
val := DateDiff(d2, d1, "days")

AssertEq(val, 5, A_LineNumber)

val := DateDiff(d1, d2, "days")

AssertEq(val, -5, A_LineNumber)
d1 := "2023021002"
d2 := "2023021001"
val := DateDiff(d1, d2, "h")

AssertEq(val, 1, A_LineNumber)
val := DateDiff(d2, d1, "h")

AssertEq(val, -1, A_LineNumber)

d1 := "202302100230"
d2 := "202302100225"
val := DateDiff(d1, d2, "m")

AssertEq(val, 5, A_LineNumber)

val := DateDiff(d2, d1, "m")

AssertEq(val, -5, A_LineNumber)

d1 := "20230210023015"
d2 := "20230210022510"
val := DateDiff(d1, d2, "s")

AssertEq(val, 305, A_LineNumber)

val := DateDiff(d2, d1, "s")

AssertEq(val, -305, A_LineNumber)

d1 := "20230210023015.500"
d2 := "20230210023015.100"
val := DateDiff(d1, d2, "l")

AssertEq(val, 400, A_LineNumber)

val := DateDiff(d2, d1, "l")

AssertEq(val, -400, A_LineNumber)

FileAppend "pass", "*"
