#NoTrayIcon
#Include <assert>

obj := { a: 1, b: 2 }

cap0 := ObjGetCapacity(obj)
Assert(cap0 >= 2, A_LineNumber)

cap1 := ObjSetCapacity(obj, 64)
Assert(cap1 >= 64 && ObjGetCapacity(obj) >= 64, A_LineNumber)

count := 0
for name, value in ObjOwnProps(obj)
{
	if ((name = "a" && value = 1) || (name = "b" && value = 2))
		count += 1
}

Assert(count = 2, A_LineNumber)

AssertEq(ObjOwnPropCount(obj), 2, A_LineNumber)

baseObj := { c: 3 }
ObjSetBase(obj, baseObj)

Assert(HasBase(obj, baseObj), A_LineNumber)

gotBase := ObjGetBase(obj)
Assert(gotBase.c = 3, A_LineNumber)

Assert(obj.c = 3, A_LineNumber)

; As in AutoHotkey, a base of another native type, or one that would make a loop, is an invalid base, and a value
; which is no object is the wrong type.
Throws(() => ObjSetBase({ d: 4 }, []), A_LineNumber, ValueError)

Throws(() => ObjSetBase(Any.Prototype, Object.Prototype), A_LineNumber, ValueError)

Throws(() => ObjSetBase(1, Object.Prototype), A_LineNumber, TypeError)

o1 := {}
o2 := {}
ObjSetBase(o1, o2)

Throws(() => ObjSetBase(o2, o1), A_LineNumber, ValueError)

AssertEq(ObjGetBase("x"), String.Prototype, A_LineNumber)

o3 := {}
Throws(() => (o3.Base := 1), A_LineNumber, TypeError)

o4 := {}
defined := DefineProp(o4, "answer", {Value: 42})
Assert(defined = o4 && o4.answer = 42, A_LineNumber)

base4 := {inherited: true}
ObjSetBase(o4, base4)
Assert(HasProp(o4, "answer") && HasProp(o4, "inherited")
	&& !HasProp(o4, "missing") && !HasProp(0, "Base"), A_LineNumber)

; A literal's unset value defines no property, and deleting a dynamic property returns an empty string.
LiteralOf(v?) => {a: 1, b: (v?)}
AssertEq(ObjOwnPropCount(LiteralOf()), 1, A_LineNumber)
dynamicHolder := {}
dynamicHolder.DefineProp("p", {Get: (this) => 1})
AssertEq(dynamicHolder.DeleteProp("p"), "", A_LineNumber)

FileAppend "pass", "*"
