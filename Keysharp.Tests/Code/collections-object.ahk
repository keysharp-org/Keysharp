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

AssertEq(obj.OwnPropCount(), 2, A_LineNumber)

baseObj := { c: 3 }
ObjSetBase(obj, baseObj)

Assert(HasBase(obj, baseObj), A_LineNumber)

gotBase := ObjGetBase(obj)
Assert(gotBase.c = 3, A_LineNumber)

Assert(obj.c = 3, A_LineNumber)

Throws(() => ObjSetBase({ d: 4 }, []), A_LineNumber)

Throws(() => ObjSetBase(Any.Prototype, Object.Prototype), A_LineNumber)

o1 := {}
o2 := {}
ObjSetBase(o1, o2)

Throws(() => ObjSetBase(o2, o1), A_LineNumber)

AssertEq(ObjGetBase("x"), String.Prototype, A_LineNumber)

o3 := {}
Throws(() => (o3.Base := 1), A_LineNumber)

o4 := {}
defined := DefineProp(o4, "answer", {Value: 42})
Assert(defined = o4 && o4.answer = 42, A_LineNumber)

base4 := {inherited: true}
ObjSetBase(o4, base4)
Assert(ObjHasProp(o4, "answer") && ObjHasProp(o4, "inherited")
	&& !ObjHasProp(o4, "missing") && !ObjHasProp(0, "Base"), A_LineNumber)

FileAppend "pass", "*"
