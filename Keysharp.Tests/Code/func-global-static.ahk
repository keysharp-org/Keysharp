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
	global x := 11, y := 22
	static z := 33
	static zz := initfunc(1, 2), zzz := initfunc(3, 4) * 2

	AssertEq(z, 33, A_LineNumber)
	
	AssertEq(zz, 3, A_LineNumber)
	
	AssertEq(zzz, 14, A_LineNumber)
}

func()

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func2()
{
	global x := 11
	global y := 22
	static z

	Assert(z is unset, A_LineNumber)
}

func2()

AssertEq(x, 11, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)


x := 1
y := 2
z := 3

func3()
{
	global
	static x := 111
	y := 22
	static z := 333, zz := initfunc(5, 6), zzz := initfunc(7, 8) * 2

	AssertEq(x, 111, A_LineNumber)

	AssertEq(z, 333, A_LineNumber)
	
	AssertEq(zz, 11, A_LineNumber)
	
	AssertEq(zzz, 30, A_LineNumber)
}

func3()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 22, A_LineNumber)

AssertEq(z, 3, A_LineNumber)


x := 1
y := 2
z := 3

func4()
{
	global
	static x
	static y
	static z

	x := 11
	y := 22
	z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func4()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

FileAppend "pass", "*"
