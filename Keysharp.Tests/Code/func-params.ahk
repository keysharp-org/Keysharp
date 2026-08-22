#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

initfunc(a, b)
{
	return a + b
}

func(a, b, c)
{
	global x := a
	global y := b
	global z := c
}

func(11, 22, 33)

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 33, A_LineNumber)

func(initfunc(1, 2), initfunc(3, 4) * 2, initfunc(5, 6) * 3)

AssertEq(x, 3, A_LineNumber)

AssertEq(y, 14, A_LineNumber)

AssertEq(z, 33, A_LineNumber)

myfunc(a, b, c)
{
	return a + b + c
}

myfunc(xx := 1, yy := 2, zz := 3)

AssertEq(xx, 1, A_LineNumber)

AssertEq(yy, 2, A_LineNumber)

AssertEq(zz, 3, A_LineNumber)

val := myfunc(xxx := 1, yyy := 2, zzz := 3)

AssertEq(val, 6, A_LineNumber)

AssertEq(xxx, 1, A_LineNumber)

AssertEq(yyy, 2, A_LineNumber)

AssertEq(zzz, 3, A_LineNumber)

val := myfunc((x4 := 3) / 2, (y4 := 2) + 2, (z4 := 1) * 2) ; The parsing is a little trickier in nested expressions as arguments.

AssertEq(x4, 3, A_LineNumber)

AssertEq(y4, 2, A_LineNumber)

AssertEq(z4, 1, A_LineNumber)

AssertEq(val, 7.5, A_LineNumber)

; Try nested arguments referring the global, local and static vars.

myfunc2(xx)
{
	return xx
}

x := 10

TestParamFunc() {
	global x--
	static yy := 123
	local ll := 8
	
	AssertEq(x, 9, A_LineNumber)

	val := myfunc2((x := 10) / 5)
	
	Assert(val == 2 && x == 10, A_LineNumber)
		
	AssertEq(yy, 123, A_LineNumber)

	val := myfunc2((yy := 9) / 3)
	
	Assert(val == 3 && yy == 9, A_LineNumber)
	
	AssertEq(ll, 8, A_LineNumber)

	val := myfunc2((ll := 20) * 5)
	
	Assert(val == 100 && ll == 20, A_LineNumber)
}

TestParamFunc()

FileAppend "pass", "*"
