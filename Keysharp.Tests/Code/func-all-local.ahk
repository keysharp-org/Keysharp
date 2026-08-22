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
	x := 11
	y := 22
	z := 33
	
	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

a := 100
b := 200
c := 300

func2()
{
	local a := 111
	local b := 222
	local c := 333

	AssertEq(a, 111, A_LineNumber)

	AssertEq(b, 222, A_LineNumber)

	AssertEq(c, 333, A_LineNumber)

	a := 11
	b := 22
	c := 33

	AssertEq(a, 11, A_LineNumber)

	AssertEq(b, 22, A_LineNumber)

	AssertEq(c, 33, A_LineNumber)
}

func2()

AssertEq(a, 100, A_LineNumber)

AssertEq(b, 200, A_LineNumber)

AssertEq(c, 300, A_LineNumber)

a := 100
b := 200
c := 300

func3()
{
	local a := 444, b := 555, c := 666

	AssertEq(a, 444, A_LineNumber)

	AssertEq(b, 555, A_LineNumber)

	AssertEq(c, 666, A_LineNumber)

	a := 777
	b := 888
	c := 999

	AssertEq(a, 777, A_LineNumber)

	AssertEq(b, 888, A_LineNumber)

	AssertEq(c, 999, A_LineNumber)
}

func3()

AssertEq(a, 100, A_LineNumber)

AssertEq(b, 200, A_LineNumber)

AssertEq(c, 300, A_LineNumber)

x := fa() . fb() . fc("fa()")

AssertEq(x, "l!fa()z", A_LineNumber)

fa()
{
	return "l"
}

fb(x := "")
{
;	return x
	return "!" . x
}

fc(x, y := "z")
{
	return x . y
}

x := 1
y := 2
z := 3

func4()
{
	local x := initfunc(1, 2), y := initfunc(3, 4) * 2, z := initfunc(5, 6) * 3

	AssertEq(x, 3, A_LineNumber)

	AssertEq(y, 14, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
}

func4()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

FileAppend "pass", "*"
