#NoTrayIcon

#import __Main
#import KS { Collect }
#Include <assert>
gval := 0

class testclass
{
	__New()
	{
		__Main.gval := 100
	}

	__Delete()
	{
		__Main.gval := 999
	}
}

testclassobj := testclass()

testclassobj := ""
timeout := A_TickCount + 2000

while (gval != 999 && A_TickCount < timeout)
{
	Sleep(100)
	Collect()
}

AssertEq(gval, 999, A_LineNumber)

class enumclass
{
	arr := [1, 2, 3]

	__Enum(ct)
	{
		return this.arr.__Enum(ct)
	}
}

gval := 0
testclassobj := enumclass()

for i,v in testclassobj
{
	gval += v
}

AssertEq(gval, 6, A_LineNumber)

class subenumclass extends enumclass
{
	subarr := [4, 5, 6]

	__Enum(ct)
	{
		return this.subarr.__Enum(ct)
	}
}

gval := 0
testclassobj := subenumclass()

for i,v in testclassobj
{
	gval += v
}

AssertEq(gval, 15, A_LineNumber)

class testclass2
{
	a := 1
	b := 2
	c := 3
}

testclassobj := testclass2()
cloneobj := testclassobj.Clone()

AssertEq(cloneobj.a, 1, A_LineNumber)

AssertEq(cloneobj.b, 2, A_LineNumber)

AssertEq(cloneobj.c, 3, A_LineNumber)

class testclass3 {
	static Call(a) {
		return a * 10
	}
}

val := testclass3(10)

AssertEq(val, 100, A_LineNumber)


val := TestWithCustomStaticCall() ; internally calls the custom Call() to return 123 instead of a new object

AssertEq(val, 123, A_LineNumber)

val := TestWithCustomStaticCall.Call() ; also returns 123

AssertEq(val, 123, A_LineNumber)

; class with one custom static Call() method which replaces the default one.
; this prevents an instance of this class from every being created.
class TestWithCustomStaticCall
{
	static Call()
	{
		return 123
	}
}

class TestWithCustomInstanceCall
{
	Call()
	{
		return 123
	}
}

obj := TestWithCustomInstanceCall() ; creates an instance of the class.

Assert(obj is TestWithCustomInstanceCall, A_LineNumber)

val := obj.Call() ; intelligent enough to resolve to the instance Call() to return 123, instead of the default static one.

AssertEq(val, 123, A_LineNumber)

Gfunc123(*)
{
	return 123
}

Gfunc456(*)
{
	return 456
}

; Sort of a combination of instance, static, and intializiation funcs with direct function references to global functions.
class foclass
{
	static sg123 := true ? gfunc123 : gfunc456
	static sg456 := true ? gfunc456 : gfunc123
	static stestmemberfunc := this.sclassfunc789

	ig123 := true ? gfunc123 : gfunc456
	ig456 := true ? gfunc456 : gfunc123
	iginit123 := this.classfunc123

	classfunc()
	{
		lg123 := true ? gfunc123 : gfunc456
		lg456 := true ? gfunc456 : gfunc123

		val := lg123()
		
		AssertEq(val, 123, A_LineNumber)

		val := lg456()
		
		AssertEq(val, 456, A_LineNumber)

		testfunc := this.classfunc123
		val := testfunc(this)
		
		AssertEq(val, 123, A_LineNumber)
	}

	ClassFunc123()
	{
		return 123
	}
	
	static sClassFunc789()
	{
		return 789
	}
}

fc := foclass()
fc.classfunc()

val := fc.ig123()

AssertEq(val, 123, A_LineNumber)

val := fc.ig456()

AssertEq(val, 456, A_LineNumber)
	
val := fc.iginit123()

AssertEq(val, 123, A_LineNumber)

val := foclass.sg123.Call()

AssertEq(val, 123, A_LineNumber)

val := foclass.sg456.Call()

AssertEq(val, 456, A_LineNumber)

val := foclass.stestmemberfunc.Call(foclass)

AssertEq(val, 789, A_LineNumber)

FileAppend "pass", "*"
