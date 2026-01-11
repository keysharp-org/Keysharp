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

if (HasProp(testclassobj, "__Item") && testclassobj.HasProp("__Item"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
val := testclassobj.a

If (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

testclassobj.a := 999

val := testclassobj.a

If (val == 999)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := testclass.b

If (val == 555)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

val := testclassobj[3]

If (val == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

testclassobj[3] := 100
val := testclassobj[3]

If (val == 100)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (HasProp(otb, "__Item") && otb.HasProp("__Item"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
otb[999] := 123
val := otb[777]

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
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

if (!HasProp(ptt, "__Item") && !ptt.HasProp("__Item"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
ptt.xprop := 123
val := ptt.xprop

if (val == 123)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

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

if (y == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (x == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (!HasProp(t1, "__Item") && !t1.HasProp("__Item"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


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
if (x == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
_ := T.Len ; .get without param
if (x == 4)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
_ := T.Len[1]   ; .get with param
if (x == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
T.Len()    ; .call without param
if (x == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 0
T.Len(1)   ; .call with param
if (x == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"