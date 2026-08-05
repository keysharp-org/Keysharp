#NoTrayIcon

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

If (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := myclass.classfuncstatic()

If (val == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Test directly referring to static class methods
val := 0
fo := myclass.classfuncstatic
val := fo(myclass)

If (val == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := true ? myclass.classfuncstatic : myclass.classfuncstatic2
val := fo(myclass)

If (val == 10)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncusesstatic()

If (val == 1230)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

myclass.s1 := 1

val := classobj.classfuncusesstatic()

If (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncwithlocalvars()

If (val == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncwithreadmembervars()

If (val == 15129)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

classobj.classfuncwithwritelocalmembervars()

if (classobj.x == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (classobj.y == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

classobj.classfuncwithwritemembervars()

if (classobj.x == 88)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (classobj.y == 99)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncwithlocalstaticvars()

if (val == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

classobj.classfuncwriteglobalvars()

if (classobj.a == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (a == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := myclass.classfuncstaticwithparams(150, 2)

if (val == 300)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncwithparams(500, 2)

if (val == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := myclass.classvarfuncstatic(1, 2, 3)

If (val == 12)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := classobj.classvarfunc(1, 2, 3)

If (val == 12)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := classobj.classfuncwiththis()

If (val == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

classobj.ClassFuncCaseSensitive()

if (classobj.a == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (classobj.b == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

classobj.a := ""
classobj.b := ""

classobj.classfunccasesensitive()

if (classobj.a == 1000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (classobj.b == 2000)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

myclass.s1 := ""
myclass.ClassFuncCaseSensitiveStatic()

if (myclass.s1 == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

funcadd := classobj.classfuncwithparams.Bind(classobj)

val := funcadd(10, 20)

if (val == 200)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

funcadd := myclass.classfuncstaticwithparams.Bind(myclass)

val := funcadd(10, 10)

if (val == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

funcadd := classobj.classvarfunc.Bind(classobj)

val := funcadd(1, 2, 3)

if (val == 12)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

funcadd := myclass.classvarfuncstatic.Bind(myclass)

val := funcadd(1, 2, 3)

if (val == 12)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (a == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
val := class2obj.classfunc0()

if (val == 0 && a == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
class2obj.classfunc1 1

if (a == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
class2obj.classfunc2 1, 2

if (a == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
class2obj.classfunc3 1

if (a == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
class2obj.classfunc3 1, 2, 4, 5, 6

if (a == 18)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
a := ""
class2obj.classfunc3(1, 2, arr*) ; variadic spread operator can't be used with command style because it's mistaken for a multiplication with the next line.

if (a == 9)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := class2obj.classfunc4(1)

if (val == 16)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := class2obj.classfunc4(1,,)

if (val == 16)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := class2obj.classfunc4(1, 10, &a := 15)

if (val == 26 && a == 26)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := class2obj.classfuncimplicit()

if (val == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := class2obj.classfunc0.Bind(class2obj)
a := ""
val := fo()

if (val == 0 && a == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := class2obj.GetMethod("classfunc1")
a := ""
val := fo(class2obj, 123)

if (val == 123 && a == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := class2obj.classfunc2.Bind(class2obj)
a := ""
val := fo(123)

if (val == 128 && a == 128)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := class2obj.classfunc3
a := ""
val := fo(class2obj, 1)

if (val == 6 && a == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
val := fo(class2obj, 1, 2, 4, 5, 6)

if (val == 18 && a == 18)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

fo := class2obj.classfuncimplicit.Bind(class2obj)
a := ""
val := fo()

if (val == 0 && a == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
val := fo(1, 2, 4, 5, 6)

if (val == 18 && a == 18)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

a := ""
val := fo(arr*)

if (val == 6 && a == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (temp == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"