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
	global x := 11, y := 22, z := 33

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)

	x := 101, y := 202, z := 303

	AssertEq(x, 101, A_LineNumber)

	AssertEq(y, 202, A_LineNumber)

	AssertEq(z, 303, A_LineNumber)
}

func()

AssertEq(x, 101, A_LineNumber)

AssertEq(y, 202, A_LineNumber)

AssertEq(z, 303, A_LineNumber)

a := 100
b := 200
c := 300

func2()
{
	global
	a := 111
	b := 222
	c := 333

	AssertEq(a, 111, A_LineNumber)

	AssertEq(b, 222, A_LineNumber)

	AssertEq(c, 333, A_LineNumber)
}

func2()

AssertEq(a, 111, A_LineNumber)

AssertEq(b, 222, A_LineNumber)

AssertEq(c, 333, A_LineNumber)

func3()
{
	global x := initfunc(1, 2), y := initfunc(3, 4) * 2, z := initfunc(5, 6) * 3

	AssertEq(x, 3, A_LineNumber)

	AssertEq(y, 14, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func3()

AssertEq(x, 3, A_LineNumber)

AssertEq(y, 14, A_LineNumber)

AssertEq(z, 33, A_LineNumber)

FileAppend "pass", "*"
