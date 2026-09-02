#NoTrayIcon
#Include <assert>

class testclass
{
	a := 0
	b := 0
}

o1 := testclass()
testfunc(o1) ; Class with declared members a, b.
o1 := { a : "" }
testfunc(o1) ; Class with dynamic member a.

o1 := testclass()
o1.DefineProp("a", { ; Define a dynamic property over a declared one of the same name.
		get: (this) => 123,
		set: (this, v) => this.b := v
	})

o1.a := 100
val := o1.a

AssertEq(val, 123, A_LineNumber)
	
AssertEq(o1.b, 100, A_LineNumber)

o1.DefineProp("a", { ; Redefine a dynamic property over previously declared dynamic property on a class with a declared property of the same name.
		set: (this, v) => this.b := v
	})

o1.a := 200

AssertEq(o1.a, 123, A_LineNumber)

AssertEq(o1.b, 200, A_LineNumber)

class extestclass extends testclass
{
}

eo1 := extestclass()
eo1_a := eo1.a
eo1.DefineProp("a", { ; Define dynamic property in a derived class where the base class has a declared property of the same name.
		set: (this, v) => this.b := v, get: (this) => eo1_a
	})

eo1.a := 200

AssertEq(eo1.a, 0, A_LineNumber)

AssertEq(eo1.b, 200, A_LineNumber)

val := 100
o1 := testclass()
o1.DefineProp("a", { ; Define a dynamic Call property that takes a reference parameter, over a declared one of the same name.
		Call: (this, &v) => this.b := v := 999
	})

o1.a(&val)

AssertEq(val, 999, A_LineNumber)

o1 := testclass()
o1.DefineProp("c", {
		value: 123
	})

o1.DefineProp("getsetprop", {
		get: (this) => 456,
		set: (this) => this.b := 789
	})
	
b := true

try
{
	val := o1.GetOwnPropDesc("a") ; Get a value property object for a declared property.

	AssertEq(val.Value, 0, A_LineNumber)

	o1.a := 999

	Assert(o1.GetOwnPropDesc("a").Value == o1.a && o1.a == 999, A_LineNumber)

	val := o1.GetOwnPropDesc("c") ; Get a value property object for a dynamic property.

	AssertEq(val.Value, 123, A_LineNumber)

	val := o1.GetOwnPropDesc("getsetprop") ; Get a get property object for a dynamic property. Note that get must be called like a method().

	AssertEq(val.get(), 456, A_LineNumber)
	
	val := o1.GetOwnPropDesc("getsetprop") ; Get a get property object for a dynamic property. Note that get must be called like a method().
	val.set()

	AssertEq(o1.b, 0, A_LineNumber)  ; val was considered "this" inside of set(), so it never set o1.b.

	Assert(ObjHasOwnProp(o1, "a"), A_LineNumber) ; 
	
	Assert(o1.HasOwnProp("c"), A_LineNumber)
}
catch
{
	b := false
}

Assert(b, A_LineNumber)

o1 := { a : 1, c : 123 }
b := true

try
{
	val := o1.GetOwnPropDesc("a") ; Get a value property object for a declared property in an object literal.

	AssertEq(val.Value, 1, A_LineNumber)
	
	val := o1.GetOwnPropDesc("c")

	AssertEq(val.Value, 123, A_LineNumber)
	
	Assert(ObjHasOwnProp(o1, "a"), A_LineNumber)
	
	Assert(o1.HasOwnProp("c"), A_LineNumber)
}
catch
{
	b := false
}

Assert(b, A_LineNumber)
	
o1 := testclass()

AssertEq(ObjOwnPropCount(o1), 2, A_LineNumber)  ; Count all declared properties.
	
Assert(ObjHasOwnProp(o1, "a") && o1.HasOwnProp("a"), A_LineNumber) ; Call both forms of *HasOwnProp().
	
class extclass2 extends testclass
{
	c := 0
}

o1 := extclass2()

AssertEq(ObjOwnPropCount(o1), 3, A_LineNumber)  ; Count all declared properties in base and derived class.

o1.DefineProp("d", {
		call : () => 123
	})

AssertEq(ObjOwnPropCount(o1), 4, A_LineNumber)  ; Count all declared and dynamic properties in base and derived class.
	
Assert(ObjHasOwnProp(o1, "a") && o1.HasOwnProp("b") && o1.HasOwnProp("c") && o1.HasOwnProp("d"), A_LineNumber)

o1 := { one : 1}
_ObjDefineProp := Object.Prototype.DefineProp
_ObjDefineProp(Object.Prototype, "OwnPropCount", {call: (this) => ObjOwnPropCount(this)})

AssertEq(o1.OwnPropCount(), 1, A_LineNumber)  ; Count all declared properties in object literal.

Assert(ObjHasOwnProp(o1, "One") && !o1.HasOwnProp("two"), A_LineNumber)

o1.DefineProp("d", {
		call : () => 123
	})
	
AssertEq(o1.OwnPropCount(), 2, A_LineNumber)  ; Count all declared and dynamic properties in object literal.
	
Assert(ObjHasOwnProp(o1, "one") && o1.HasOwnProp("d"), A_LineNumber)

o1 := [1, 2, 3]

Assert(o1.OwnPropCount() == 0 && ObjOwnPropCount(o1) == 0, A_LineNumber) ; Declared properties for built in types are not counted.
	
Assert(!ObjHasOwnProp(o1, "capacity") && !o1.HasOwnProp("Count") && !o1.HasOwnProp("Length"), A_LineNumber)

Assert(o1.HasProp("__Item"), A_LineNumber)
	
m := Map("one", 1, "two", 2, "three", 3)

Assert(m.HasProp("__Item"), A_LineNumber)

o1 := testclass()
o1.DefineProp("c", { ; Dynamically create property with getter and setter.
		get: (this) => 123,
		set: (this, v) => this.b := v
	})
o1.a := 100
o1.b := 200
b := false
i := 0

For Name, Value in o1.OwnProps() ; Enumerator inline with a for loop. Retrieve values is implicitly true because of two loop variables.
{
	if (name == "a" && value == 100)
		b := true
	else if (name == "b" && value == 200)
		b := true
	else if (name == "c" && value == 123)
		b := true
	else
		b := false

	i++
}

Assert(b && i == 3, A_LineNumber)

o1.a := 100
o1.b := 200
b := false
i := 0
op := o1.OwnProps() ; Retrieve value must be specified.

For Name,Value in op ; Enumerator variable with a for loop.
{
	if (name == "a" && value == 100)
		b := true
	else if (name == "b" && value == 200)
		b := true
	else if (name == "c" && value == 123)
		b := true
	else
		b := false

	i++
}

Assert(b && i == 3, A_LineNumber)

i := 0
b := false
o1.a := 0
m := { one : 1, two : 2, three : (this) => o1.a := 123 }

for name in m.OwnProps() { ; Enumerator inline with a for loop, names only.
	if (name == "one")
		b := true
	else if (name == "two")
		b := true
	else if (name == "three")
		b := true
	else
		b := false

	i++
}

Assert(b && i == 3 && o1.a == 0, A_LineNumber) ; Ensure the last prop didn't get called.

testfunc(testclassobj)
{
	testclassobj.DefineProp("prop", { ; Dynamically defined property with getter.
		get: (this) => 123
	})

	val := testclassobj.prop
	
	AssertEq(val, 123, A_LineNumber)

	testclassobj.DefineProp("prop", { ; Overwrite previous with dynamically defined property with getter and setter.
		get: (this) => 123,
		set: (this, v) => this.a := v
	})

	testclassobj.prop := 100
	val := testclassobj.a

	AssertEq(val, 100, A_LineNumber)
	
	testclassobj.DefineProp("prop", { ; Overwrite previous with dynamically defined property with getter, setter and call.
		get: (this) => 123,
		set: (this, v) => this.a := v,
		call: (this, p*) => this.a := p.Length
	})

	testclassobj.prop := 200
	val := testclassobj.a

	AssertEq(val, 200, A_LineNumber)

	testclassobj.prop()
	val := testclassobj.a

	AssertEq(val, 0, A_LineNumber)

	testclassobj.DefineProp("prop", { ; Overwrite previous with dynamically defined call.
		call: (this, p*) => this.a := p.Length
	})
	
	testclassobj.prop(1, 2)

	AssertEq(testclassobj.a, 2, A_LineNumber)

	testclassobj.DefineProp("prop", { ; Overwrite previous with dynamically defined call.
		value: 123
	})

	val := testclassobj.prop

	AssertEq(val, "123", A_LineNumber)

	testclassobj.prop := (this, p*) => this.a := p.Length ; Overwrite previous with dynamically defined value property with direct fat arrow function assignment.
	testclassobj.prop()

	AssertEq(testclassobj.a, 0, A_LineNumber)

	testclassobj.prop(1, 2)

	AssertEq(testclassobj.a, 2, A_LineNumber)

	testclassobj.prop(1, 2, 3)

	AssertEq(testclassobj.a, 3, A_LineNumber)

	testclassobj.DefineProp("prop", { ; Overwrite previous with dynamically defined get property which returns another fat arrow function.
		get: (*) => ((this, p*) => this.a := p.Length)
	})

	testclassobj.prop() ; Retrieve value from get, which will be a Func, then call it using ().

	AssertEq(testclassobj.a, 0, A_LineNumber)

	testclassobj.prop(1)

	AssertEq(testclassobj.a, 1, A_LineNumber)

	testclassobj.prop(1, 2)

	AssertEq(testclassobj.a, 2, A_LineNumber)

	testclassobj.prop(1, 2, 3)

	AssertEq(testclassobj.a, 3, A_LineNumber)
}

; --- The two-variable form omits what it cannot value, rather than calling a getter that must fail.
; Every expectation below was read off AutoHotkey v2.0.26 and v2.1-alpha.30, which agree exactly.
OwnPropKeys(subject, twoVar)
{
	joined := ""

	if (twoVar)
	{
		for propName, propValue in subject.OwnProps()
			joined .= propName "|"
	}
	else
	{
		for propName in subject.OwnProps()
			joined .= propName "|"
	}

	return joined
}

; A prototype is not an instance of its class, so none of its getters has a receiver they would accept.
AssertEq(OwnPropKeys(Array.Prototype, true), "__Class|", A_LineNumber)
Assert(InStr(OwnPropKeys(Array.Prototype, false), "Push|") > 0, A_LineNumber)   ; the one-variable form still names them

opIndexed := {Keep: 1}
opIndexed.DefineProp("Idx", {get: (this, i) => i})
AssertEq(OwnPropKeys(opIndexed, true), "Keep|", A_LineNumber)                   ; an indexed getter has no index to be given
Assert(InStr(OwnPropKeys(opIndexed, false), "Idx|") > 0, A_LineNumber)

opBoth := {Keep: 1}
opBoth.DefineProp("Idx", {get: (this, i) => i, call: (this) => 7})
AssertEq(OwnPropKeys(opBoth, true), "Keep|", A_LineNumber)                      ; a Call must not approve an indexed Get

opVariadic := {}
opVariadic.DefineProp("V", {get: (this, req, rest*) => req})
AssertEq(OwnPropKeys(opVariadic, true), "", A_LineNumber)                       ; variadic does not make a required param optional

opSetOnly := {}
opSetOnly.DefineProp("S", {set: (this, v) => v})
AssertEq(OwnPropKeys(opSetOnly, true), "", A_LineNumber)
Assert(InStr(OwnPropKeys(opSetOnly, false), "S|") > 0, A_LineNumber)

opCallOnly := {}
opCallOnly.DefineProp("C", {call: (this) => 7})
AssertEq(OwnPropKeys(opCallOnly, true), "", A_LineNumber)
Assert(InStr(OwnPropKeys(opCallOnly, false), "C|") > 0, A_LineNumber)

opGettable := {}
opGettable.DefineProp("G", {get: (this) => 11, call: (this) => 7})
AssertEq(OwnPropKeys(opGettable, true), "G|", A_LineNumber)

; A descriptor that cannot produce a value does not end the property search: a setter-only one is walked past,
; and a Call-only one is taken only once nothing further up the chain has yielded a value.
class OpShadowBase
{
	fromField := 1
	getter => 5
	method() => "method"
}

class OpShadowDerived extends OpShadowBase
{
}

opShadow := OpShadowDerived()

opShadow.DefineProp("fromField", {set: (*) => 1})

; The instance field WAS the only holder of the value, so nothing is left to read.
Throws(() => opShadow.fromField, A_LineNumber)

Assert(opShadow.HasOwnProp("fromField") && ObjHasOwnProp(opShadow, "fromField"), A_LineNumber)

opShadow.DefineProp("getter", {set: (*) => 1})

AssertEq(opShadow.getter, 5, A_LineNumber)

opShadow.DefineProp("method", {set: (*) => 1})

AssertEq(Type(opShadow.method), "Func", A_LineNumber)

AssertEq(opShadow.method(), "method", A_LineNumber)

opCallShadow := OpShadowDerived()

opCallShadow.DefineProp("getter", {call: (*) => "callonly"})

AssertEq(opCallShadow.getter, 5, A_LineNumber)

; A descriptor names at least one of Value, Get, Set and Call; Value cannot accompany the other three; and each of
; Get, Set and Call holds a function object. A rejected descriptor leaves the property untouched.
opDesc := {}

opFn := (*) => 1

Throws(() => opDesc.DefineProp("d", {}), A_LineNumber)

Throws(() => opDesc.DefineProp("d", {note: "x"}), A_LineNumber)

Throws(() => opDesc.DefineProp("d", {value: 1, call: opFn}), A_LineNumber)

Throws(() => opDesc.DefineProp("d", {value: 1, get: opFn}), A_LineNumber)

Throws(() => opDesc.DefineProp("d", {value: 1, set: opFn}), A_LineNumber)

Throws(() => opDesc.DefineProp("d", {get: 5}), A_LineNumber)

Assert(!opDesc.HasOwnProp("d"), A_LineNumber)

; Extra keys alongside Value are ignored rather than rejected, and the accepted shapes still define.
opDesc.DefineProp("d", {value: 7, note: "x"})

AssertEq(opDesc.d, 7, A_LineNumber)

opDesc.DefineProp("e", {get: (*) => 11, set: (*) => 0})

AssertEq(opDesc.e, 11, A_LineNumber)

opDesc.DefineProp("f", {call: (*) => "called"})

AssertEq(opDesc.f(), "called", A_LineNumber)

; Reading a name nothing in the chain has is a PropertyError, and the free function matches the method form.
Throws(() => ({}).noSuchProperty, A_LineNumber)

AssertEq(({}).noSuchProperty ?? "DEFAULT", "DEFAULT", A_LineNumber)

Throws(() => ObjHasOwnProp(5, "x"), A_LineNumber)

Throws(() => ObjOwnPropCount("str"), A_LineNumber)

Throws(() => ObjOwnProps(5), A_LineNumber)

; A property whose getter yields nothing is unset, not missing — carrying a Set slot as well does not change that.
OpUnsetValue()
{
	local u
	return u
}

opUnset := {}

opUnset.DefineProp("g", {get: (*) => OpUnsetValue(), set: (*) => 0})

opUnsetKind := ""

try
	opUnset.g
catch as opErr
	opUnsetKind := Type(opErr)

AssertEq(opUnsetKind, "UnsetError", A_LineNumber)

FileAppend "pass", "*"
