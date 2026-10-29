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

; Indexing a value yielded by a parameterless getter requires that getter to return a value.
class UnsetIndexHolder {
	Data => unset
	Indexed[Index] => unset
	Indexable => NullableIndexValue()
	Writable {
		get => unset
		set => unset
	}
}
class NullableIndexValue {
	__Item[Index] => unset
}
unsetHolder := UnsetIndexHolder()
AssertEq(unsetHolder.Data ?? "missing", "missing", A_LineNumber)
Assert(!IsSet(unsetHolder.Data), A_LineNumber)
AssertEq(unsetHolder.Indexed[1] ?? "missing", "missing", A_LineNumber)
AssertEq(unsetHolder.Indexable[1] ?? "missing", "missing", A_LineNumber)
AssertEq(unsetHolder.Missing ?? "missing", "missing", A_LineNumber)
Throws(() => unsetHolder.Missing[1] ?? "missing", A_LineNumber, PropertyError)
AssertError(() => unsetHolder.Data[1], "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => unsetHolder.Data[1] ?? 0, "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => unsetHolder.Data() ?? 0, "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => unsetHolder.Data[1] := 2, "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => unsetHolder.Writable[1] := 2, "UnsetError: No value was returned. [Writable]", A_LineNumber)
ownUnsetHolder := {}
ownUnsetHolder.DefineProp("Data", {Get: (This) => unset})
ownUnsetHolder.DefineProp("Writable", {Get: (This) => unset, Set: (This, Value) => unset})
AssertError(() => ownUnsetHolder.Data[1], "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => ownUnsetHolder.Data[1] := 2, "UnsetError: No value was returned. [Data]", A_LineNumber)
AssertError(() => ownUnsetHolder.Writable[1] := 2, "UnsetError: No value was returned. [Writable]", A_LineNumber)
indexedRefValue := [1, 2]
indexedVarRef := &indexedRefValue
AssertEq(indexedVarRef.__Value[1], 1, A_LineNumber)
indexedVarRef.__Value[2] := 3
AssertEq(indexedVarRef.__Value[2], 3, A_LineNumber)
unsetRefValue := unset
unsetVarRef := &unsetRefValue
AssertError(() => unsetVarRef.__Value[1], "UnsetError: No value was returned. [__Value]", A_LineNumber)
AssertError(() => unsetVarRef.__Value[1] ?? 0, "UnsetError: No value was returned. [__Value]", A_LineNumber)
AssertError(() => unsetVarRef.__Value[1] := 2, "UnsetError: No value was returned. [__Value]", A_LineNumber)
AssertError(() => Any.Prototype.Base[1] := 2, "UnsetError: No value was returned. [Base]", A_LineNumber)

class IndexedSetterParent {
	P[Index] {
		set => this.Seen := Index
	}
}
setterParent := IndexedSetterParent()
setterParent.DefineProp("P", {Get: (This) => [1, 2]})
setterParent.P[2] := 7
AssertEq(setterParent.Seen, 2, A_LineNumber)
setterParent.DefineProp("P", {Value: [1, 2]})
setterParent.DeleteProp("Seen")
setterParent.P[2] := 7
AssertEq(setterParent.P[2], 7, A_LineNumber)
Assert(!setterParent.HasOwnProp("Seen"), A_LineNumber)

class SuperDataParent {
	static Value := 1
	static Handler := (*) => 123
}
class SuperDataChild extends SuperDataParent {
	static Change() => super.Value := 2
	static Clear() {
		super.Value := unset
		super.Handler := unset
	}
}
SuperDataChild.Change()
AssertEq(SuperDataParent.Value, 1, A_LineNumber)
AssertEq(SuperDataChild.Value, 2, A_LineNumber)
Assert(SuperDataChild.HasOwnProp("Value"), A_LineNumber)
SuperDataChild.Clear()
AssertEq(SuperDataParent.Value, 1, A_LineNumber)
Assert(SuperDataChild.HasOwnProp("Value") && (SuperDataChild.Value ?? "missing") == "missing", A_LineNumber)
Assert(!HasMethod(SuperDataChild, "Handler"), A_LineNumber)
Throws(() => SuperDataChild.Handler(), A_LineNumber)

; A descriptor has to describe exactly one kind of property. A Get, Set or Call that is not an object counts as
; absent rather than as an error. [v2.1-alpha.34+]
descriptorTarget := {}
Throws(() => descriptorTarget.DefineProp("A", {Value: 1, Pack: 4}), A_LineNumber, ValueError)
Throws(() => descriptorTarget.DefineProp("A", {Get: 1}), A_LineNumber, ValueError)
Throws(() => descriptorTarget.DefineProp("A", {Offset: 0}), A_LineNumber, ValueError)
Throws(() => descriptorTarget.DefineProp("", {Value: 1}), A_LineNumber, ValueError)
Throws(() => descriptorTarget.DefineProp("A", {Type: Int32}), A_LineNumber, Error)
MessageOf(callback) {
	try
		callback()
	catch Error as err
		return err.Message
	return "none"
}
AssertEq(MessageOf(() => DefineProp(descriptorTarget, "A", {})), "Parameter #3 of DefineProp is invalid.", A_LineNumber)
AssertEq(MessageOf(() => descriptorTarget.DefineProp("A", {})), "Parameter #2 of Object.Prototype.DefineProp is invalid.", A_LineNumber)
descriptorTarget.DefineProp("B", {Get: 1, Value: 5})
AssertEq(descriptorTarget.B, 5, A_LineNumber)
descriptorTarget.DefineProp("C", {Get: (*) => 7, Set: "x"})
AssertEq(descriptorTarget.C, 7, A_LineNumber)

; Enumerable: false hides a property from Props, though not from OwnProps. Assigning the property keeps it hidden,
; and redefining it without Enumerable shows it again. A hidden property does not shadow its base's. [v2.1-alpha.34+]
PropNames(obj) {
	names := ""
	for enumeratedName in obj.Props()
		names .= enumeratedName ","
	return names
}
enumerated := {}
enumerated.DefineProp("Hidden", {Value: 1, Enumerable: false})
enumerated.DefineProp("Shown", {Value: 2})
enumerated.DefineProp("HiddenGetter", {Get: (*) => 3, Enumerable: 0})
AssertEq(PropNames(enumerated), "Shown,", A_LineNumber)
ownNames := ""
for ownName in enumerated.OwnProps()
	ownNames .= ownName ","
Assert(InStr(ownNames, "Hidden,") && InStr(ownNames, "HiddenGetter,"), A_LineNumber)
enumerated.Hidden := 4
AssertEq(PropNames(enumerated), "Shown,", A_LineNumber)
enumerated.DefineProp("Hidden", {Value: 5})
Assert(InStr(PropNames(enumerated), "Hidden,"), A_LineNumber)
shadowed := {base: {X: 1}}
shadowed.DefineProp("X", {Value: 2, Enumerable: false})
for shadowName, shadowValue in shadowed.Props()
	AssertEq(shadowName "=" shadowValue, "X=1", A_LineNumber)
; A method has no value to list, so it does not shadow a base's property either.
class PropsBase {
	X => 1
}
class PropsDerived extends PropsBase {
	X() => 2
}
AssertEq(PropNames(PropsDerived()), "X,", A_LineNumber)
; Each kind of accessor is inherited separately: a read takes the nearest getter and a call the nearest method.
methodHolder := PropsDerived()
AssertEq(methodHolder.X, 1, A_LineNumber)
AssertEq(methodHolder.X(), 2, A_LineNumber)
class PropsGetterOverMethod extends PropsDerived {
	X => 3
}
getterHolder := PropsGetterOverMethod()
AssertEq(getterHolder.X, 3, A_LineNumber)
AssertEq(getterHolder.X(), 2, A_LineNumber)
class PropsIndexedBase {
	Y[Index] => Index * 10
}
class PropsMethodOverIndexed extends PropsIndexedBase {
	Y() => 5
}
indexedHolder := PropsMethodOverIndexed()
AssertEq(indexedHolder.Y[2], 20, A_LineNumber)
AssertEq(indexedHolder.Y(), 5, A_LineNumber)
; With no getter anywhere, a read yields the method's function object, which any index then applies to.
onlyCall := (This) => 9
methodHolder.DefineProp("OnlyCall", {Call: onlyCall})
Assert(methodHolder.OnlyCall is Func, A_LineNumber)
Throws(() => methodHolder.OnlyCall[1], A_LineNumber, PropertyError)
onlyCall.DefineProp("__Item", {Get: (This, Index) => Index * 2})
AssertEq(methodHolder.OnlyCall[3], 6, A_LineNumber)
AssertEq(methodHolder.OnlyCall(), 9, A_LineNumber)
struct HiddenField {
	Shown : Int32
}
DefineProp(HiddenField.Prototype, "Hidden", {Type: Int32, Enumerable: false})
fieldNames := PropNames(HiddenField())
Assert(InStr(fieldNames, "Shown,") && !InStr(fieldNames, "Hidden,"), A_LineNumber)
AssertEq(HiddenField.Prototype.Size, 8, A_LineNumber)
; `Static` followed by `:=` or `=>` names a member rather than marking one static.
class StaticNamedMembers {
	Static := 1
	static Shared := 2
	static Static => 3
}
staticNamed := StaticNamedMembers()
AssertEq(staticNamed.Static, 1, A_LineNumber)
AssertEq(StaticNamedMembers.Shared, 2, A_LineNumber)
AssertEq(StaticNamedMembers.Static, 3, A_LineNumber)

FileAppend "pass", "*"
