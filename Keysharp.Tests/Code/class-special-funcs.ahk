#NoTrayIcon

#import __Main
#import KS { Collect }
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

If (gval == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (gval == 6)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

If (gval == 15)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

class testclass2
{
	a := 1
	b := 2
	c := 3
}

testclassobj := testclass2()
cloneobj := testclassobj.Clone()

If (cloneobj.a == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (cloneobj.b == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

If (cloneobj.c == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

class testclass3 {
	static Call(a) {
		return a * 10
	}
}

val := testclass3(10)

If (val == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


val := TestWithCustomStaticCall() ; internally calls the custom Call() to return 123 instead of a new object

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := TestWithCustomStaticCall.Call() ; also returns 123

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (obj is TestWithCustomInstanceCall)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := obj.Call() ; intelligent enough to resolve to the instance Call() to return 123, instead of the default static one.

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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
		
		if (val == 123)
			FileAppend "pass", "*"
		else
			FileAppend "fail", "*"

		val := lg456()
		
		if (val == 456)
			FileAppend "pass", "*"
		else
			FileAppend "fail", "*"

		testfunc := this.classfunc123
		val := testfunc(this)
		
		if (val == 123)
			FileAppend "pass", "*"
		else
			FileAppend "fail", "*"
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

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := fc.ig456()

if (val == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := fc.iginit123()

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := foclass.sg123.Call()

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := foclass.sg456.Call()

if (val == 456)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := foclass.stestmemberfunc.Call(foclass)

if (val == 789)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"