#NoTrayIcon
#Include <assert>

class testclass
{
	a := 123
	b := 456
	static c := 888
	static d := 1000

	BaseCaseSensitiveFunc()
	{
        testclass.a := 999
		this.a := 1212
	}
	
	static BaseCaseSensitiveFuncStatic()
	{
		testclass.c := 3131
	}
}

class testsubclass extends testclass
{
	_a := 321

	a
	{
		get
		{
			return this._a
		}

		set
		{
			this._a := value
		}
	}

	static b := 654

	_c := 999

	c
	{
		get
		{
			return this._c
		}

		set
		{
			this._c := value
		}
	}

	static d := 2000
	
	setbasea()
	{
		this.base.a := 500
	}

	GetBasea()
	{
		return this.base.a
	}

	SubCaseSensitiveFunc()
	{
        this.a := 1212
		this.basecasesensitivefunc()
	}
	
	static SubCaseSensitiveFuncStatic()
	{
		testclass.basecasesensitivefuncstatic()
	}
}

testclassobj := testclass()
testsubclassobj := testsubclass()

Assert(testclassobj is testclass, A_LineNumber)

Assert(testsubclassobj is testclass, A_LineNumber)

Assert(testsubclassobj is testsubclass, A_LineNumber)

val := testclassobj.a

AssertEq(val, 123, A_LineNumber)

val := testsubclassobj.a

AssertEq(val, 321, A_LineNumber)

val := testclassobj.b

AssertEq(val, 456, A_LineNumber)

val := testsubclass.b

AssertEq(val, 654, A_LineNumber)

val := testclass.c

AssertEq(val, 888, A_LineNumber)

val := testsubclassobj.c

AssertEq(val, 999, A_LineNumber)

val := testclass.d

AssertEq(val, 1000, A_LineNumber)

val := testsubclass.d

AssertEq(val, 2000, A_LineNumber)
	
testsubclassobj.setbasea()

val := testsubclassobj.getbasea()

AssertEq(val, 500, A_LineNumber)

val := testsubclassobj.a

AssertEq(val, 321, A_LineNumber)
	
testsubclassobj.base.a := 777

val := testsubclassobj.getbasea()

AssertEq(val, 777, A_LineNumber)

val := testsubclassobj.a

AssertEq(val, 321, A_LineNumber)

classname := testclassobj.__Class

AssertEq(classname, "testclass", A_LineNumber)

classname := testsubclassobj.__Class

AssertEq(classname, "testsubclass", A_LineNumber)

testsubclassobj.a := ""
testsubclassobj.base.a := ""

testsubclassobj.subcasesensitivefunc()

AssertEq(testsubclass.a, 999, A_LineNumber)
	
AssertEq(testsubclassobj.a, 1212, A_LineNumber)

testclass.c := ""
testsubclass.subcasesensitivefuncstatic()

AssertEq(testclass.c, 3131, A_LineNumber)

class MyArray extends Array
{
	__Item[index]
	{
		get
		{
			return 123
		}
	}
}

classname := MyArray()

Assert(classname is Array, A_LineNumber)

Assert(classname is MyArray, A_LineNumber)

val := classname[100]

AssertEq(val, 123, A_LineNumber)

class MyMap extends Map
{
	__Item[index]
	{
		get
		{
			return 321
		}
	}
}

classname := MyMap()

Assert(classname is Map, A_LineNumber)

Assert(classname is MyMap, A_LineNumber)

val := classname[100]

AssertEq(val, 321, A_LineNumber)

class base1
{
	__Item[index]
	{
		get
		{
			return 1
		}
	}
}

class sub1 extends base1
{
	__Item[index]
	{
		get
		{
			return 2
		}
	}
}

class subarr1 extends Array
{
}

class subarr2 extends subarr1
{
	__Item[index]
	{
		get
		{
			return 3
		}
	}
}

obj := sub1()
val := obj[999]

AssertEq(val, 2, A_LineNumber)

obj := subarr2()

Assert(obj is subarr2 && obj is subarr1 && obj is Array, A_LineNumber)

val := obj[999]

AssertEq(val, 3, A_LineNumber)

class submap1 extends Map
{
}

class submap2 extends submap1
{
	__Item[index]
	{
		get
		{
			return 4
		}
	}
}

obj := submap2()

Assert(obj is submap2 && obj is submap1 && obj is Map, A_LineNumber)

val := obj[999]

AssertEq(val, 4, A_LineNumber)

testclass.c := 101
myfunc := testsubclass.basecasesensitivefuncstatic

myfunc(testsubclass)

AssertEq(testclass.c, 3131, A_LineNumber)

testclass.c := 101
myfunc := testsubclass.subcasesensitivefuncstatic

myfunc(testsubclass)

AssertEq(testclass.c, 3131, A_LineNumber)

testsubclassobj.a := 0
myfunc := testsubclassobj.basecasesensitivefunc
myfunc(testsubclassobj)

AssertEq(testsubclassobj.a, 1212, A_LineNumber)
	
testsubclassobj.a := 0
myfunc := testsubclassobj.SubCaseSensitiveFunc
myfunc(testsubclassobj)

AssertEq(testsubclassobj.a, 1212, A_LineNumber)

class myarrayclass1 extends Array
{
	__item[k1]
	{
		get
		{
			return 1
		}
		
	}
}

mac := myarrayclass1()
mac.Push(123)
val := mac[1]

AssertEq(val, 1, A_LineNumber)

mac[1] := 999
val := mac[1]

AssertEq(val, 1, A_LineNumber)

class myarrayclass2 extends Array
{
	__item[p1]
	{
		set
		{
			super[p1] := value
		}
	}
}

mac := myarrayclass2()
mac.Push(123)
val := mac[1]

AssertEq(val, 123, A_LineNumber)

mac[1] := 999
val := mac[1]

AssertEq(val, 999, A_LineNumber)

class mymapclass1 extends map
{
	__item[a*]
	{
		get
		{
            if (a.Length == 1)
                return super[a[1]]
            sum := 0
            for k in a
                sum += k
			return super[sum]
		}

		set
		{
            if (a.Length == 1) {
                super[a[1]] := value
                return
            }
            sum := 0
            for k in a
                sum += k
			super[sum] := sum
		}
	}
}

mmp := mymapclass1()

mmp["asdf"] := 123
val := mmp["asdf"]

AssertEq(val, 123, A_LineNumber)

mmp[1, 2] := 123
val := mmp[1, 2]

AssertEq(val, 3, A_LineNumber)

mmp[1, 2, 3] := 123
val := mmp[1, 2, 3]

AssertEq(val, 6, A_LineNumber)
	
class myarrayclass3 extends Array
{
	__item[p1*]
	{
		set
		{
			temp := 0

			for n in p1
			{
				temp += n
			}

			super[p1[1]] := temp + value
		}
	}
}

mac := myarrayclass3()
mac.Push(1)
mac[1, 2, 3, 4] := 100
val := mac[1]

AssertEq(val, 110, A_LineNumber)

class myarrayclass4 extends Array
{
	__item[p1*]
	{
		get
		{
			temp := 0

			for n in p1
			{
				temp += n
			}

			return temp
		}
	}
}

mac := myarrayclass4()
val := mac[1, 2, 3, 4]

AssertEq(val, 10, A_LineNumber)

class myarrayclass5 extends Array
{
	__item[p1, p2, p3*]
	{
		get
		{
			temp := p1 + p2

			for n in p3
			{
				temp += n
			}

			return temp
		}
	}
}

mac := myarrayclass5()
val := mac[1, 2, 3, 4]

AssertEq(val, 10, A_LineNumber)

	
class myarrayclass6 extends Array
{
	; Special test which references a property defined in the base built-in Array type.
	; The reason this is special is that it must be properly cased by the parser to compile.
	doublecount
	{
		get
		{
			return this.length * 2 ; Meant to refer to the base Array.Count property.
		}
	}
}

mac := myarrayclass6(1, 2, 3, 4)
val := mac.doublecount

AssertEq(val, 8, A_LineNumber)

class myinitclass
{
	p1 := 123
}

mic := myinitclass()
val := mic.p1

AssertEq(val, 123, A_LineNumber)
	
return123func()
{
	return 123
}

class myfuncinitclass
{
	p1 := return123func()
}

mic := myfuncinitclass()
val := mic.p1

AssertEq(val, 123, A_LineNumber)

class mybaseclass
{
	x := 100

	basefunc()
	{
		this.x := 123
	}

	retfunc(xx)
	{
		return xx
	}
}

class mysubclass extends mybaseclass
{
	basefunc()
	{
		super.basefunc()
		this.x +=1
		temp := this.x
		val := this.retfunc((this.x := 99) / 3) ; Nested assignment within an expression referencing already declared global property.
		
		AssertEq(this.x, 99, A_LineNumber)

		AssertEq(val, 33, A_LineNumber)

		this.x := temp
	}
}

msc := mysubclass()
msc.basefunc()
val := msc.x

AssertEq(val, 124, A_LineNumber)

msc := mysubclass()
msc.base.base.basefunc()
val := msc.base.x

AssertEq(val, 123, A_LineNumber)

; Test subclasses that derive from built in types and access the base properties before either class is fully initialized.
; This ensures the initialization chain of __Init() and __New() work properly.

class bigarr extends Array
{
	Capacity := 10000
}

mybigarr := bigarr(1, 2, 3)

Assert(mybigarr is Array, A_LineNumber)

Assert(mybigarr is bigarr, A_LineNumber)

AssertEq(mybigarr.Capacity, 10000, A_LineNumber)
	
Assert(mybigarr[1] == 1 && mybigarr[2] == 2 && mybigarr[3] == 3, A_LineNumber)
	
class Mapi extends Map {
	CaseSense := false
	DerivedDefault := ""

	__New(args*)
	{
		this.DerivedDefault := unset
        super.__New(args*)
	}
}

cim := Mapi("a", 1, "B", 2)

Assert(cim is Map, A_LineNumber)

Assert(cim is Mapi, A_LineNumber)

Assert(cim["A"] == 1 && cim["b"] == 2, A_LineNumber)
	
class dupepropsbase
{
	a {
		get => 123
	}
}

class dupepropssub extends dupepropsbase
{
	_a := 999

	a
	{
		get => this._a
		set => this._a := value
	}

	getlocala()
	{
        a := 1
		return a
	}

	getthisa()
	{
		return this.a
	}

	getsupera()
	{
		return super.a
	}
}

classobj := dupepropssub()

AssertEq(classobj.a, 999, A_LineNumber)

AssertEq(classobj.base.base.a, 123, A_LineNumber)

AssertEq(classobj.getlocala(), 1, A_LineNumber)

AssertEq(classobj.getthisa(), 999, A_LineNumber)

AssertEq(classobj.getsupera(), 123, A_LineNumber)

; A member declared on a script class SHADOWS the one it inherits from a built-in base. Most already did,
; through the prototype chain; __Enum did not, because enumeration reached the built-in through its C#
; interface without ever consulting that chain.
class OverridingMap extends Map
{
	hits := 0
	__Enum(n)
	{
		this.hits += 1
		return Map("overridden", 1).__Enum(n)
	}
	Has(key) => "shadowed"
}

om := OverridingMap()
om["real"] := 1
seen := ""
for k, v in om
	seen .= k "=" v ";"

Assert(seen == "overridden=1;" && om.hits == 1, A_LineNumber)

AssertEq(om.Has("real"), "shadowed", A_LineNumber)

; ... and a built-in NOT overridden still resolves to the built-in.
AssertEq(om.Count, 1, A_LineNumber)

FileAppend "pass", "*"
