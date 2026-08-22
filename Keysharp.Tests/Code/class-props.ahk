#NoTrayIcon
#Include <assert>

class testclass
{
	_a := 123
	static _b := 555
	arr := [1, 2, 3, 4, 5, 6]

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

	static b
	{
		get
		{
			return this._b
		}
	}

	__Item[X] ;Change case on purpose.
	{
		get
		{
			return this.arr[x]
		}

		set
		{
			this.arr[x] := value
		}
	}
}

testclassobj := testclass()

Assert(HasProp(testclassobj, "__Item") && testclassobj.HasProp("__Item"), A_LineNumber)
	
val := testclassobj.a

AssertEq(val, 123, A_LineNumber)

testclassobj.a := 999

val := testclassobj.a

AssertEq(val, 999, A_LineNumber)

val := testclass.b

AssertEq(val, 555, A_LineNumber)

val := testclassobj[3]

AssertEq(val, 3, A_LineNumber)

testclassobj[3] := 100
val := testclassobj[3]

AssertEq(val, 100, A_LineNumber)

class PropTestOTB
{
	x := 0
	__Item[name] {
		get {
		global
		return x
		}
		set {
		global
		x := value
		}
	}
}

otb := PropTestOTB()

Assert(HasProp(otb, "__Item") && otb.HasProp("__Item"), A_LineNumber)
	
otb[999] := 123
val := otb[777]

AssertEq(val, 123, A_LineNumber)
	
class PropTestThis
{
	x := 0
	xprop {
		get {
		global
		return x
		}
		set {
		this.x := value
		}
	}
}

ptt := PropTestThis()

Assert(!HasProp(ptt, "__Item") && !ptt.HasProp("__Item"), A_LineNumber)
	
ptt.xprop := 123
val := ptt.xprop

AssertEq(val, 123, A_LineNumber)

; Ensure the special super property is properly implemented.
x := 0

class Test1 extends Test2 {
	Meth1()
	 {
		global x += 1
		return super.Meth1()
	}
}

class Test2 extends Test3 {
	Meth1()
	{
		global x += 1
		return super.Meth1()
	}
}

class Test3 {
	Meth1()
	{
		global x
		return x++
	}
}

t1 := test1()
y := t1.Meth1()

AssertEq(y, 2, A_LineNumber)

AssertEq(x, 3, A_LineNumber)
	
Assert(!HasProp(t1, "__Item") && !t1.HasProp("__Item"), A_LineNumber)


class Test {
    Len[Param?] {
        get {
			global x
			if IsSet(Param)
				x := 3
			else
				x := 4
        }
    }
    Len(Param?) {
		global x
		if IsSet(Param)
			x := 1
		else
			x := 2
    }
}

T := Test()

x := 0
T.Len      ; .call without param
AssertEq(x, 2, A_LineNumber)

x := 0
_ := T.Len ; .get without param
AssertEq(x, 4, A_LineNumber)

x := 0
_ := T.Len[1]   ; .get with param
AssertEq(x, 3, A_LineNumber)

x := 0
T.Len()    ; .call without param
AssertEq(x, 2, A_LineNumber)

x := 0
T.Len(1)   ; .call with param
AssertEq(x, 1, A_LineNumber)

FileAppend "pass", "*"
