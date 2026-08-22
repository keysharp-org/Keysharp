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

FileAppend "pass", "*"
