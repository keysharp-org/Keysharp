#NoTrayIcon
#Include <assert>

class testclass
{
	a := ""
	b := ""
	c := ""
	
	__New(_a, _b, _c)
	{
		global
		this.a := _a
		this.b := _b
		this.c := _c
	}
}

class testsubclass extends testclass
{
	x := ""
	y := ""
	z := ""
	zz := ""
	
	__New(p1, p2, p3, p4)
	{
		global
		super.__New(p1, p2, p3)
		this.x := p1 * 10
		this.y := p2 * 10
		this.z := p3 * 10
		this.zz := p4 * 10
	}
}

testclassobj := testclass(1, 2, 3)
testsubclassobj := testsubclass(1, 2, 3, 4)

val := testclassobj.a

AssertEq(val, 1, A_LineNumber)

val := testclassobj.b

AssertEq(val, 2, A_LineNumber)
	
val := testclassobj.c

AssertEq(val, 3, A_LineNumber)
	
val := testsubclassobj.a

AssertEq(val, 1, A_LineNumber)

val := testsubclassobj.b

AssertEq(val, 2, A_LineNumber)
	
val := testsubclassobj.c

AssertEq(val, 3, A_LineNumber)
	
val := testsubclassobj.x

AssertEq(val, 10, A_LineNumber)

val := testsubclassobj.y

AssertEq(val, 20, A_LineNumber)
	
val := testsubclassobj.z

AssertEq(val, 30, A_LineNumber)

val := testsubclassobj.zz

AssertEq(val, 40, A_LineNumber)

class testclassnoargs
{
	a := ""
	b := ""
	c := ""
	
	__New()
	{
		global
		this.a := 1
		this.b := 2
		this.c := 3
	}
}

class testsubclassfourargs extends testclassnoargs
{
	x := ""
	y := ""
	
	__New(p1, p2)
	{
		global
		super.__New()
		this.x := p1 * 10
		this.y := p2 * 10
	}
}

testclassobj := testclassnoargs()
testsubclassobj := testsubclassfourargs(1, 2)

val := testclassobj.a

AssertEq(val, 1, A_LineNumber)

val := testclassobj.b

AssertEq(val, 2, A_LineNumber)
	
val := testclassobj.c

AssertEq(val, 3, A_LineNumber)

val := testsubclassobj.x

AssertEq(val, 10, A_LineNumber)

val := testsubclassobj.y

AssertEq(val, 20, A_LineNumber)

class testclassthreeargs
{
	a := 1
	b := 2
	c := 3
	
	__New(_a, _b, _c)
	{
		global
		this.a := _a
		this.b := _b
		this.c := _c
	}
}

class testsubclassnoargs extends testclassthreeargs
{
	x := ""
	y := ""
	
	__New(a, b, c)
	{
		global
		super.__New(a, b, c)
		this.x := 100
		this.y := 200
	}
}

testclassobj := testclassthreeargs(4, 5, 6)
testsubclassobj := testsubclassnoargs(1, 2, 3)

val := testclassobj.a

AssertEq(val, 4, A_LineNumber)

val := testclassobj.b

AssertEq(val, 5, A_LineNumber)
	
val := testclassobj.c

AssertEq(val, 6, A_LineNumber)

val := testsubclassobj.x

AssertEq(val, 100, A_LineNumber)

val := testsubclassobj.y

AssertEq(val, 200, A_LineNumber)

val := testsubclassobj.a

Assert(IsSet(val), A_LineNumber)

val := testsubclassobj.b

Assert(IsSet(val), A_LineNumber)

val := testsubclassobj.c

Assert(IsSet(val), A_LineNumber)

testsubclassobj := testsubclassnoargs(7, 8, 9) ; No constructor parameters defined in the subclass, so just forward them to the base.

val := testsubclassobj.a

AssertEq(val, 7, A_LineNumber)

val := testsubclassobj.b

AssertEq(val, 8, A_LineNumber)

val := testsubclassobj.c

AssertEq(val, 9, A_LineNumber)

class class1
{
	sum := 0

	__New(args*)
	{
		global sum
		local temp := 0

		for n in args
		{
			temp += n
		}

		this.sum := temp
	}
}

arr := [1, 2, 3]
c1 := class1()

AssertEq(c1.sum, 0, A_LineNumber)
	
c1 := class1(arr*)

AssertEq(c1.sum, 6, A_LineNumber)

c1 := class1(1, arr*)
		
AssertEq(c1.sum, 7, A_LineNumber)
	
c1 := class1(1, 2, arr*)

AssertEq(c1.sum, 9, A_LineNumber)

c1 := ""

class class2
{
	sum := 0

	__New(args*)
	{
		global sum
		local temp := 0

		for n in args
		{
			temp += n
		}

		this.sum := temp
	}
}

c2 := class2()

AssertEq(c2.sum, 0, A_LineNumber)

c2 := class2(1, 2, 3)

AssertEq(c2.sum, 6, A_LineNumber)
	
c2 := class2(arr*)

AssertEq(c2.sum, 6, A_LineNumber)
	
c2 := class2(1, 2, arr*)

AssertEq(c2.sum, 9, A_LineNumber)

c2 := ""

class class3
{
	sum := 0

	__New(theparams*)
	{
		global sum
		local temp := 0

		for n in theparams
		{
			temp += n
		}

		this.sum := temp
	}
}

c3 := class3(1, 2, 3)

AssertEq(c3.sum, 6, A_LineNumber)

c3 := class3(arr*)

AssertEq(c3.sum, 6, A_LineNumber)

c3 := class3(1, 2, arr*)

AssertEq(c3.sum, 9, A_LineNumber)

c3 := ""

class class4
{
	sum := 0

	__New(p1, p2, theparams*)
	{
		global sum
		local temp := p1 + p2

		if (theparams.Length)
		{
			for n in theparams
			{
				temp += n
			}
		}

		this.sum := temp
	}
}

c4 := class4(1, 2)

AssertEq(c4.sum, 3, A_LineNumber)

c4 := class4(1, 2, arr*)

AssertEq(c4.sum, 9, A_LineNumber)

class class5
{
	func(a := "`r", b := "`n", c := "`t")
	{
		return a . b . c
	}
}

c5 := class5()
val := c5.func()

AssertEq(val, "`r`n`t", A_LineNumber)

FileAppend "pass", "*"
