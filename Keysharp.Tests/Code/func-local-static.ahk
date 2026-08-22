#NoTrayIcon
#Include <assert>

initfunc(a, b)
{
	return a + b
}

func()
{
	x := 1
	y := 2
	static Z := 3

	AssertEq(x, 1, A_LineNumber)

	AssertEq(y, 2, A_LineNumber)
	
	AssertEq(z, 3, A_LineNumber)
}

func()

x := 1
y := 2
z := 3

func2()
{
	static
	local x := 22, y := initfunc(10, 20)

	AssertEq(x, 22, A_LineNumber)
	
	AssertEq(y, 30, A_LineNumber)
}

func2()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func3()
{
	static x := 111
	y := 22
	static z := 333, ZZ := initfunc(1, 2)

	AssertEq(x, 111, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 333, A_LineNumber)
	
	AssertEq(zz, 3, A_LineNumber)
}

func3()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

x := 1
y := 2
z := 3

func4()
{
	static
	local x := 11, y := 22, z := 33, zz := initfunc(5, 6)

	AssertEq(x, 11, A_LineNumber)

	AssertEq(y, 22, A_LineNumber)

	AssertEq(z, 33, A_LineNumber)
	
	AssertEq(zz, 11, A_LineNumber)

	x := 111, y := 222, z := 333, zz := initfunc(7, 8)

	AssertEq(x, 111, A_LineNumber)

	AssertEq(y, 222, A_LineNumber)

	AssertEq(z, 333, A_LineNumber)
	
	AssertEq(zz, 15, A_LineNumber)
}

func4()

AssertEq(x, 1, A_LineNumber)

AssertEq(y, 2, A_LineNumber)

AssertEq(z, 3, A_LineNumber)

reffunc1(&a)
{
	a := 100
}

func5()
{
	static x := 111
	reffunc1(&x := 0)
	
	AssertEq(x, 100, A_LineNumber)
}

func5()

class class1 {
	func6() {
		static i := 2
		return ++i
	}
}

func6() {
	static i := 2
	return ++i
}

t1 := class1()
x := t1.func6()
AssertEq(x, 3, A_LineNumber)

x := unset
x := t1.func6()
AssertEq(x, 4, A_LineNumber)

t2 := class1()
x := unset
x := t2.func6()
AssertEq(x, 5, A_LineNumber)

x := unset
x := t2.func6()
AssertEq(x, 6, A_LineNumber)

x := unset
x := func6()
AssertEq(x, 3, A_LineNumber)

FileAppend "pass", "*"
