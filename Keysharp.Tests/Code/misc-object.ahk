#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

x := 1
a := [10, 20, 30]

Assert(HasMethod(a, "Contains"), A_LineNumber)

Assert(HasMethod(a, "CoNtAiNs"), A_LineNumber) ; test case insensitive once.
	
Assert(HasMethod(a, "RemoveAt"), A_LineNumber)
	
Assert(HasMethod(a, "Push"), A_LineNumber)
	
Assert(HasMethod(a, "Pop"), A_LineNumber)

Assert(a.HasMethod("Contains"), A_LineNumber)
	
Assert(a.HasMethod("CoNtAiNs"), A_LineNumber) ; test case insensitive once.
	
Assert(a.HasMethod("RemoveAt"), A_LineNumber)
	
Assert(a.HasMethod("Push"), A_LineNumber)
	
Assert(a.HasMethod("Pop"), A_LineNumber)

fo := a.GetMethod("Push")
fo(a, 40)

AssertEq(a.Length, 4, A_LineNumber)

a := [10, 20, 30]
fo := GetMethod(a, "Push")
fo(a, 40)

AssertEq(a.Length, 4, A_LineNumber)

a := [10, 20, 30]
fo := GetMethod(a, "RemoveAt")
fo(a, 1)

AssertEq(a.Length, 2, A_LineNumber)

Assert(a = [20, 30], A_LineNumber)

plainObj := {}

AssertEq(plainObj.Base, Object.Prototype, A_LineNumber)

AssertEq(plainObj.Base.Base, Any.Prototype, A_LineNumber)

obj := Map(1, "a", "b", 2)
punk := ObjPtr(obj)
AssertEq(Type(punk), "Integer", A_LineNumber)
AssertEq(ObjPtr(obj), punk, A_LineNumber)
context := Buffer(A_PtrSize)
NumPut("Ptr", punk, context)
contexts := Map(punk, obj)
Assert(contexts.Has(NumGet(context, "Ptr")), A_LineNumber)
got := ObjFromPtrAddRef(NumGet(context, "Ptr"))
AssertEq(got, obj, A_LineNumber)
Assert(got is Map, A_LineNumber)
ptrReads := []
ptrHolder := {}.DefineProp("Ptr", {Get: (*) => (ptrReads.Push(1), punk)})
AssertEq(ObjFromPtrAddRef(ptrHolder), obj, A_LineNumber)
AssertEq(ptrReads.Length, 1, A_LineNumber)
AssertEq(ObjAddRef(punk), 1, A_LineNumber)
AssertEq(ObjRelease(punk), 0, A_LineNumber)

#if WINDOWS
AssertEq(ObjAddRef(obj), 1, A_LineNumber)
AssertEq(ObjRelease(punk), 0, A_LineNumber)
#endif

Throws(() => ObjFromPtr(0), A_LineNumber, ValueError)
Throws(() => ObjFromPtrAddRef("invalid"), A_LineNumber, ValueError)
Throws(() => ObjAddRef(0), A_LineNumber, ValueError)
Throws(() => ObjRelease(0), A_LineNumber, ValueError)

; Native code can query the pointer's IUnknown interface.
iid := Buffer(16, 0)
NumPut("UChar", 0xC0, iid, 8)
NumPut("UChar", 0x46, iid, 15)
queryInterface := NumGet(NumGet(punk, "Ptr"), "Ptr")
queried := 0
result := DllCall(queryInterface, "Ptr", punk, "Ptr", iid, "Ptr*", &queried, "Int")
AssertEq(result, 0, A_LineNumber)
AssertEq(queried, punk, A_LineNumber)
if result = 0 && queried != 0 {
	AssertEq(ObjFromPtrAddRef(queried), obj, A_LineNumber)
	AssertEq(ObjRelease(queried), 0, A_LineNumber)
}

owned := ObjPtrAddRef(obj)
second := ObjPtrAddRef(obj)
AssertEq(owned, punk, A_LineNumber)
AssertEq(second, punk, A_LineNumber)
AssertEq(ObjAddRef(punk), 3, A_LineNumber)
AssertEq(ObjRelease(punk), 2, A_LineNumber)
AssertEq(ObjRelease(second), 1, A_LineNumber)
adopted := ObjFromPtr(owned)
AssertEq(adopted, obj, A_LineNumber)
AssertEq(ObjAddRef(punk), 1, A_LineNumber)
AssertEq(ObjRelease(punk), 0, A_LineNumber)
AssertEq(ObjPtr(adopted), punk, A_LineNumber)

; GetMethod follows the prototype chain and returns the function object as stored, checking ParamCount against it.
withMethod := {}
withMethod.DefineProp("M", {call: (this, a, b := 2) => a + b})
wm := {base: withMethod}
method := GetMethod(wm, "M")
AssertEq(method(wm, 1), 3, A_LineNumber)
Assert(GetMethod(method) == method, A_LineNumber)
wm.DefineProp("Extra", {call: (this) => "extra"})
AssertEq(GetMethod(wm, "Extra")(wm), "extra", A_LineNumber)
Assert(HasMethod(wm, "M", 1) && HasMethod(wm, "M", 2) && !HasMethod(wm, "M", 3), A_LineNumber)
Assert(!HasMethod(wm, "Missing") && !HasMethod("abc"), A_LineNumber)
Throws(() => GetMethod(wm, "M", 5), A_LineNumber, ValueError)

; Indexing an object without __Item is a member error, and an object literal's base is validated as ObjSetBase
; validates it.
Throws(() => ({})[1], A_LineNumber, PropertyError)
Throws(() => {base: 5}, A_LineNumber)

FileAppend "pass", "*"
