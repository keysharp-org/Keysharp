#NoTrayIcon
#Include <assert>

d1 := "20040126000000"
d2 := "20050126000000"
val := DateAdd(d1, 366, "days")

AssertEq(d2, val, A_LineNumber)

val := DateAdd(d2, -366, "days")

AssertEq(d1, val, A_LineNumber)

d1 := "2023021002"
d2 := "20230210070000"
val := DateAdd(d1, 5, "h")

AssertEq(d2, val, A_LineNumber)

d1 := "20230210020000"
val := DateAdd(d2, -5, "h")

AssertEq(d1, val, A_LineNumber)

d1 := "202302100225"
d2 := "20230210023000"
val := DateAdd(d1, 5, "m")

AssertEq(d2, val, A_LineNumber)

d1 := "20230210022500"
val := DateAdd(d2, -5, "m")

AssertEq(d1, val, A_LineNumber)

d1 := "20230210022510"
d2 := "20230210022515"
val := DateAdd(d1, 5, "s")

AssertEq(d2, val, A_LineNumber)

val := DateAdd(d2, -5, "s")

AssertEq(d1, val, A_LineNumber)

d1 := "20230210022500"
d2 := "20230210022530"
val := DateAdd(d1, 0.5, "m")

AssertEq(d2, val, A_LineNumber)

val := DateAdd(d2, -0.5, "m")

AssertEq(d1, val, A_LineNumber)

d1 := "20230210020000"
d2 := "20230210023000"
val := DateAdd(d1, 0.5, "h")

AssertEq(d2, val, A_LineNumber)

val := DateAdd(d2, -0.5, "h")

AssertEq(d1, val, A_LineNumber)

d1 := "20040126000000"
d2 := "20040126120000"
val := DateAdd(d1, 0.5, "d")

AssertEq(d2, val, A_LineNumber)

val := DateAdd(d2, -0.5, "d")

AssertEq(d1, val, A_LineNumber)

d1 := "20230210023015.100"
d2 := "20230210023015.500"
val := DateAdd(d1, 400, "l")

AssertEq(val, d2, A_LineNumber)
	
d1 := "20230210023015.100"
d2 := "20230210023016.100"
val := DateAdd(d1, 1, "s")

AssertEq(val, d2, A_LineNumber)

FileAppend "pass", "*"
