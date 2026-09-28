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

#if WINDOWS
obj := Map(1, "a", "b", 2)
punk := ObjPtr(obj)
AssertEq(Type(punk), "Integer", A_LineNumber)
AssertEq(ObjPtr(obj), punk, A_LineNumber)
context := Buffer(A_PtrSize)
NumPut("Ptr", punk, context)
contexts := Map(punk, obj)
Assert(contexts.Has(NumGet(context, "Ptr")), A_LineNumber)
AssertEq(ObjFromPtrAddRef(NumGet(context, "Ptr")), obj, A_LineNumber)
ObjAddRef(punk), count := ObjRelease(punk)

AssertEq(count, 0, A_LineNumber)

got := ObjFromPtrAddRef(punk)

Assert(got is Map, A_LineNumber)

ObjAddRef(punk), count := ObjRelease(punk)

AssertEq(count, 0, A_LineNumber)

obj2 := Map(1, "a", "b", 2)
punk := ObjPtrAddRef(obj2) ; returns a raw pointer with the ref count being 1 initially
ObjAddRef(punk), count := ObjRelease(punk)

AssertEq(count, 1, A_LineNumber)

punk2 := ObjPtrAddRef(obj2)
ObjAddRef(punk2), count := ObjRelease(punk2)

AssertEq(count, 2, A_LineNumber)
ObjRelease(punk2)
ObjRelease(punk)

obj3 := Map(1, "a", "b", 2)
punk3 := ObjPtrAddRef(obj3)
got2 := ObjFromPtrAddRef(punk3)

Assert(got2 is Map, A_LineNumber)

ObjRelease(punk3)
#endif

FileAppend "pass", "*"
