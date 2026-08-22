#NoTrayIcon
#Include <assert>

a := ""

class myclass
{
	a := ""
	b :=
	c := "asdf"
	x := 123
	y := this.x
	static s1 := 10

	classfunc()
	{
		return 123
	}

	static classfuncstatic()
	{
		return this.s1
	}

	static classfuncstatic2()
	{
		return 456
	}

	classfuncusesstatic()
	{
		return myclass.s1 * this.x
	}

	classfuncwithlocalvars()
	{
		lv1 := 10
		lv2 := 10
		return lv1 * lv2
	}

	classfuncwithreadmembervars()
	{
		return this.x * this.y
	}

	classfuncwithwritelocalmembervars()
	{
		x := 88
		y := 99
	}

	classfuncwithwritemembervars()
	{
		this.x := 88
		this.y := 99
	}

	classfuncwithlocalstaticvars()
	{
		static aa := 100
		return aa * 10
	}

	classfuncwriteglobalvars()
	{
		this.a := 0
		global a := 1
	}
	
	static classfuncstaticwithparams(val1, val2)
	{
		return val1 * val2
	}

	classfuncwithparams(val1, val2)
	{
		return val1 * val2
	}

	classvarfunc(p1, theparams*)
	{
		temp := p1

		for n in theparams
		{
			temp += theparams[A_Index]
		}
	
		temp += p1

		for n in theparams
		{
			temp += n
		}

		return temp
	}
	
	static classvarfuncstatic(p1, theparams*)
	{
		temp := p1

		for n in theparams
		{
			temp += theparams[A_Index]
		}
	
		temp += p1

		for n in theparams
		{
			temp += n
		}

		return temp
	}
	
	classfuncwiththis()
	{
		this.a := 999
		val := this.a
		return val
	}

	ClassFuncCaseSensitive()
	{
		this.a := 1000
		this.ClassFuncCaseSensitive2()
		this.classfunccasesensitive2()
	}

	ClassFuncCaseSensitive2()
	{
		this.b := 2000
	}

	static ClassFuncCaseSensitiveStatic()
	{
		this.ClassFuncCaseSensitiveStatic2()
		this.classfunccasesensitivestatic2()
	}

	static ClassFuncCaseSensitiveStatic2()
	{
		this.s1 := 999
	}
}

classobj := myclass()
val := classobj.classfunc()

AssertEq(val, 123, A_LineNumber)

val := myclass.classfuncstatic()

AssertEq(val, 10, A_LineNumber)

; Test directly referring to static class methods
val := 0
fo := myclass.classfuncstatic
val := fo(myclass)

AssertEq(val, 10, A_LineNumber)

fo := true ? myclass.classfuncstatic : myclass.classfuncstatic2
val := fo(myclass)

AssertEq(val, 10, A_LineNumber)

val := classobj.classfuncusesstatic()

AssertEq(val, 1230, A_LineNumber)

myclass.s1 := 1

val := classobj.classfuncusesstatic()

AssertEq(val, 123, A_LineNumber)

val := classobj.classfuncwithlocalvars()

AssertEq(val, 100, A_LineNumber)

val := classobj.classfuncwithreadmembervars()

AssertEq(val, 15129, A_LineNumber)

classobj.classfuncwithwritelocalmembervars()

AssertEq(classobj.x, 123, A_LineNumber)

AssertEq(classobj.y, 123, A_LineNumber)

classobj.classfuncwithwritemembervars()

AssertEq(classobj.x, 88, A_LineNumber)

AssertEq(classobj.y, 99, A_LineNumber)

val := classobj.classfuncwithlocalstaticvars()

AssertEq(val, 1000, A_LineNumber)

classobj.classfuncwriteglobalvars()

AssertEq(classobj.a, 0, A_LineNumber)

AssertEq(a, 1, A_LineNumber)

val := myclass.classfuncstaticwithparams(150, 2)

AssertEq(val, 300, A_LineNumber)

val := classobj.classfuncwithparams(500, 2)

AssertEq(val, 1000, A_LineNumber)

val := myclass.classvarfuncstatic(1, 2, 3)

AssertEq(val, 12, A_LineNumber)
	
val := classobj.classvarfunc(1, 2, 3)

AssertEq(val, 12, A_LineNumber)

val := classobj.classfuncwiththis()

AssertEq(val, 999, A_LineNumber)

classobj.ClassFuncCaseSensitive()

AssertEq(classobj.a, 1000, A_LineNumber)

AssertEq(classobj.b, 2000, A_LineNumber)

classobj.a := ""
classobj.b := ""

classobj.classfunccasesensitive()

AssertEq(classobj.a, 1000, A_LineNumber)

AssertEq(classobj.b, 2000, A_LineNumber)

myclass.s1 := ""
myclass.ClassFuncCaseSensitiveStatic()

AssertEq(myclass.s1, 999, A_LineNumber)

funcadd := classobj.classfuncwithparams.Bind(classobj)

val := funcadd(10, 20)

AssertEq(val, 200, A_LineNumber)

funcadd := myclass.classfuncstaticwithparams.Bind(myclass)

val := funcadd(10, 10)

AssertEq(val, 100, A_LineNumber)

funcadd := classobj.classvarfunc.Bind(classobj)

val := funcadd(1, 2, 3)

AssertEq(val, 12, A_LineNumber)

funcadd := myclass.classvarfuncstatic.Bind(myclass)

val := funcadd(1, 2, 3)

AssertEq(val, 12, A_LineNumber)

; Test command style when using methods.

class myclass2
{
	classfunc0()
	{
		global a := 0
		return 0
	}

	classfunc1(p1)
	{
		global a := p1
		return p1
	}
	
	classfunc2(p1, p2 := 5)
	{
		temp := p1 + p2
		global a := temp
		return temp
	}

	classfunc3(p1, p2 := 5, p3*)
	{
		temp := p1 + p2

		if (p3.Length)
		{
			for n in p3
			{
				temp += p3[A_Index]
			}
		}
	
		global a := temp
		return temp
	}
	
	classfunc4(p1, p2 := 5, &p3 := 10)
	{
		return p3 := p1 + p2 + p3
	}

	classfuncimplicit(args*)
	{
		temp := 0

		for n in args
		{
			temp += args[A_Index]
		}

		global a := temp
		return temp
	}
}

a := ""
arr := [1, 2, 3]
class2obj := myclass2()
class2obj.classfunc0

AssertEq(a, 0, A_LineNumber)

a := ""
val := class2obj.classfunc0()

Assert(val == 0 && a == 0, A_LineNumber)

a := ""
class2obj.classfunc1 1

AssertEq(a, 1, A_LineNumber)

a := ""
class2obj.classfunc2 1, 2

AssertEq(a, 3, A_LineNumber)

a := ""
class2obj.classfunc3 1

AssertEq(a, 6, A_LineNumber)

a := ""
class2obj.classfunc3 1, 2, 4, 5, 6

AssertEq(a, 18, A_LineNumber)
	
a := ""
class2obj.classfunc3(1, 2, arr*) ; variadic spread operator can't be used with command style because it's mistaken for a multiplication with the next line.

AssertEq(a, 9, A_LineNumber)

val := class2obj.classfunc4(1)

AssertEq(val, 16, A_LineNumber)
	
val := class2obj.classfunc4(1,,)

AssertEq(val, 16, A_LineNumber)
	
val := class2obj.classfunc4(1, 10, &a := 15)

Assert(val == 26 && a == 26, A_LineNumber)

val := class2obj.classfuncimplicit()

AssertEq(val, 0, A_LineNumber)

fo := class2obj.classfunc0.Bind(class2obj)
a := ""
val := fo()

Assert(val == 0 && a == 0, A_LineNumber)

fo := class2obj.GetMethod("classfunc1")
a := ""
val := fo(class2obj, 123)

Assert(val == 123 && a == 123, A_LineNumber)

fo := class2obj.classfunc2.Bind(class2obj)
a := ""
val := fo(123)

Assert(val == 128 && a == 128, A_LineNumber)

fo := class2obj.classfunc3
a := ""
val := fo(class2obj, 1)

Assert(val == 6 && a == 6, A_LineNumber)

a := ""
val := fo(class2obj, 1, 2, 4, 5, 6)

Assert(val == 18 && a == 18, A_LineNumber)

fo := class2obj.classfuncimplicit.Bind(class2obj)
a := ""
val := fo()

Assert(val == 0 && a == 0, A_LineNumber)

a := ""
val := fo(1, 2, 4, 5, 6)

Assert(val == 18 && a == 18, A_LineNumber)

a := ""
val := fo(arr*)

Assert(val == 6 && a == 6, A_LineNumber)

; Accessing class members dynamically:
temp := 0
class mydynclass
{
	x := 11
	y11 := 123

	mydynclassreffunc(&val)
	{
		global temp := val
	}

	callmydynclassreffunc()
	{
		this.mydynclassreffunc(&this.y%this.x%) ; Use this.
	}
}

dc := mydynclass()
dc.callmydynclassreffunc()

AssertEq(temp, 123, A_LineNumber)

FileAppend "pass", "*"
