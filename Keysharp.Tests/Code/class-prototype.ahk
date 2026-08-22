#NoTrayIcon
#Include <assert>

a := 0
Object.Prototype.DefineProp("protoCall", {call:(*) {
    global a := 1
    }
})
({}.protoCall())

AssertEq(a, 1, A_LineNumber)

b := 0
Object.Prototype.DefineProp("protoGet", {get:(*) => 1})
b := {}.protoGet

AssertEq(b, 1, A_LineNumber)

b := 0
Object.Prototype.DefineProp("protoValue", {value:1})
b := {}.protoValue

AssertEq(b, 1, A_LineNumber)

class Test {
    HasOwnProp(*) => 1 
    protoGet {
        get => 2
    }
}

class TestExtend extends Test {
    protoValue := 2
}

o := TestExtend()

b := 0
b := o.HasOwnProp("test")
AssertEq(b, 1, A_LineNumber)

o.base := Object.Prototype
b := o.HasOwnProp("test")
AssertEq(b, 0, A_LineNumber)

Assert(Type(o) = "Object", A_LineNumber)

a := 0
o.protoCall()
AssertEq(a, 1, A_LineNumber)

b := 0
b := o.protoGet
AssertEq(b, 1, A_LineNumber)

b := 0
b := o.protoValue
AssertEq(b, 2, A_LineNumber)

c := Class()

AssertEq(c.Base, Class.Prototype, A_LineNumber)

AssertEq(c.Base.Base, Object.Prototype, A_LineNumber)

AssertEq(c.Base.Base.Base, Any.Prototype, A_LineNumber)

t := Test()

AssertEq(t.Base.__Class, "Test", A_LineNumber)


AssertEq(Object.Base, Any, A_LineNumber)

AssertEq(Any.Base, Class.Prototype, A_LineNumber)

AssertEq(Class.Base, Object, A_LineNumber)

; A Class object accepts Base assignment. [v2.1-alpha.30]
class BaseAssignA {
}

class BaseAssignB {
}

oldBase := BaseAssignB.Base
BaseAssignB.Base := BaseAssignA

AssertEq(BaseAssignB.Base, BaseAssignA, A_LineNumber)

BaseAssignB.Base := oldBase

AssertEq(BaseAssignB.Base, oldBase, A_LineNumber)

FileAppend "pass", "*"
