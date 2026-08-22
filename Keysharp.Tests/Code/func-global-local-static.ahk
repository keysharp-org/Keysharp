#NoTrayIcon
#Include <assert>

x := 1
y := 2
z := 3

initfunc(a, b)
{
	return a + b
}

func()
{
	global x := 11
	local y := 22
	static z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func()

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3
xx := ""

func2()
{
	global x := 11, xx := 111
	local y := 22, yy := 222
	static z := 33, zz := 333

	AssertEq(x, 11, A_LineNumber)

	AssertEq(xx, 111, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)
	
	AssertEq(yy, 222, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)

	AssertEq(zz, 333, A_LineNumber)
}

func2()

AssertEq(x, 11, A_LineNumber)

AssertEq(xx, 111, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3
xx := ""

func3()
{
	global x := initfunc(1, 2), xx := initfunc(3, 4) * 2
	local y := initfunc(5, 6), yy := initfunc(7, 8) * 3
	static z := initfunc(9, 10) * 4, zz := initfunc(11, 12) * 5

	AssertEq(x, 3, A_LineNumber)

	AssertEq(xx, 14, A_LineNumber)

	AssertEq(y, 11, A_LineNumber)
	
	AssertEq(yy, 45, A_LineNumber)

	AssertEq(z, 76, A_LineNumber)

	AssertEq(zz, 115, A_LineNumber)
}

func3()

AssertEq(x, 3, A_LineNumber)

AssertEq(xx, 14, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

FileAppend "pass", "*"
