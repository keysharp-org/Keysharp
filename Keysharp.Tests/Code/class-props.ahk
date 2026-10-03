#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
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

; obj.prop[i] := v reaches a parameterized setter with its index, in every assignment form.
setLog := []
class IndexedProps {
	p[i] {
		get => "get" i
		set => setLog.Push("set " i "=" value)
	}
	q {
		get => this._q
		set => this._q := value
	}
}
ip := IndexedProps()
ip.p[3] := "W"
AssertEq(setLog.Pop(), "set 3=W", A_LineNumber)
ip.p[4] .= "x"
AssertEq(setLog.Pop(), "set 4=get4x", A_LineNumber)
propName := "p"
ip.%propName%[5] := "D"
AssertEq(setLog.Pop(), "set 5=D", A_LineNumber)

; A property without parameters indexes the value its getter returns, and keeps its backing field.
ip.q := [1, 2, 3]
ip.q[2] := 20
AssertEq(ip.q[2], 20, A_LineNumber)
Assert(ip.HasOwnProp("_q"), A_LineNumber)
ip.q[2]++
AssertEq(ip.q[2], 21, A_LineNumber)

; A missing property indexed in an assignment goes to __Set with the index, or is a PropertyError.
class IndexedMetaSet {
	__Set(name, params, value) => setLog.Push(name " " params.Length " " value)
}
IndexedMetaSet().nope[1] := "Z"
AssertEq(setLog.Pop(), "nope 1 Z", A_LineNumber)
Throws(() => ({}).nope[1] := 1, A_LineNumber, PropertyError)

; Empty brackets index a member's value rather than calling or replacing the member itself.
class EmptyIndexValue {
	Value := 10
	__Item {
		get => this.Value
		set => this.Value := value
	}
}
class EmptyIndexHolder {
	Data := EmptyIndexValue()
	Reads := 0
	Accessor {
		get {
			this.Reads++
			return this.Data
		}
	}
}
emptyHolder := EmptyIndexHolder()
AssertEq(emptyHolder.Data[], 10, A_LineNumber)
emptyHolder.Data[] := 20
Assert(emptyHolder.Data is EmptyIndexValue, A_LineNumber)
AssertEq(emptyHolder.Data.Value, 20, A_LineNumber)
emptyName := "Data"
AssertEq(emptyHolder.%emptyName%[], 20, A_LineNumber)
emptyHolder.%emptyName%[] := 30
emptyHolder.Accessor[] += 2
AssertEq(emptyHolder.Reads, 1, A_LineNumber)
AssertEq(emptyHolder.Accessor[]++, 32, A_LineNumber)
AssertEq(emptyHolder.Reads, 2, A_LineNumber)
AssertEq(--emptyHolder.%emptyName%[], 32, A_LineNumber)
AssertEq(emptyHolder.Data[] ?? 0, 32, A_LineNumber)
emptyRef := &emptyHolder.Data[]
AssertEq(emptyRef.__Value, 32, A_LineNumber)
emptyRef.__Value := 34
AssertEq(emptyHolder.Data[], 34, A_LineNumber)
emptyRef := &emptyHolder.%emptyName%[]
emptyRef.__Value := 36
AssertEq(emptyHolder.Data[], 36, A_LineNumber)
emptyIndexes := []
Assert(emptyHolder.Data[emptyIndexes*] is EmptyIndexValue, A_LineNumber)

#if WINDOWS
; UIA wraps native property VARIANTs in an object whose indexer reads a stored COM reference.
class PropertyVariant {
	__New() {
		this.Storage := Buffer(8 + 2 * A_PtrSize, 0)
		this.Reference := ComValue(0x400C, this.Storage.Ptr)
	}
	__Item {
		get => this.Reference[]
		set => this.Reference[] := value
	}
}
variantProperty := PropertyVariant()
NumPut("UShort", 3, variantProperty.Storage)
NumPut("Int", 42, variantProperty.Storage, 8)
AssertEq(variantProperty[], 42, A_LineNumber)
variantProperty[] := "UIA property"
AssertEq(variantProperty[], "UIA property", A_LineNumber)
variantProperty[] := "Updated property"
AssertEq(variantProperty[], "Updated property", A_LineNumber)
DllCall("oleaut32\VariantClear", "Ptr", variantProperty.Storage)
NumPut("UShort", 11, variantProperty.Storage)
NumPut("Short", -1, variantProperty.Storage, 8)
Assert(variantProperty[], A_LineNumber)
NumPut("Short", 0, variantProperty.Storage, 8)
Assert(!variantProperty[], A_LineNumber)
#endif

FileAppend "pass", "*"
