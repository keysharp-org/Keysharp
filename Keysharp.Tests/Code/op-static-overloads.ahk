#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>
#Import Ks { Boolean }
#Import OperatorModuleA
#Import OperatorModuleB

class Both {
    static Value := 100
    Value := 200
    static +(Right) => this.Value + Right
    +(Right) => this.Value + Right
    static -(Right) => this.Value + 1 + Right
    static *(Right) => this.Value + 2 + Right
    static /(Right) => this.Value + 3 + Right
    static //(Right) => this.Value + 4 + Right
    static **(Right) => this.Value + 5 + Right
    static &(Right) => this.Value + 6 + Right
    static |(Right) => this.Value + 7 + Right
    static ^(Right) => this.Value + 8 + Right
    static <<(Right) => this.Value + 9 + Right
    static >>(Right) => this.Value + 10 + Right
    static >>>(Right) => this.Value + 11 + Right
    static <(Right) => this.Value + 12 + Right
    static <=(Right) => this.Value + 13 + Right
    static >(Right) => this.Value + 14 + Right
    static >=(Right) => this.Value + 15 + Right
    static =(Right) => this.Value + 16 + Right
    static ==(Right) => this.Value + 17 + Right
    static !=(Right) => this.Value + 18 + Right
    static !==(Right) => this.Value + 19 + Right
    static .(Right) => this.Value + 20 + Right
    static ~=(Right) => this.Value + 21 + Right
    static !~=(Right) => this.Value + 22 + Right
    static +() => this.Value + 23
    +() => this.Value + 23
    static -() => this.Value + 24
    static ~() => this.Value + 25
    static !() => this.Value + 26
    static ?() => this.Value
    ?() => false
    static ++() {
        this.Value += 1
        return this
    }
    static --() {
        this.Value -= 1
        return this
    }
}
AssertEq(Both + 2, 102, A_LineNumber)
AssertEq(Both() + 2, 202, A_LineNumber)
AssertEq(Both - 2, 103, A_LineNumber)
AssertEq(Both * 2, 104, A_LineNumber)
AssertEq(Both / 2, 105, A_LineNumber)
AssertEq(Both // 2, 106, A_LineNumber)
AssertEq(Both ** 2, 107, A_LineNumber)
AssertEq(Both & 2, 108, A_LineNumber)
AssertEq(Both | 2, 109, A_LineNumber)
AssertEq(Both ^ 2, 110, A_LineNumber)
AssertEq(Both << 2, 111, A_LineNumber)
AssertEq(Both >> 2, 112, A_LineNumber)
AssertEq(Both >>> 2, 113, A_LineNumber)
AssertEq(Both < 2, 114, A_LineNumber)
AssertEq(Both <= 2, 115, A_LineNumber)
AssertEq(Both > 2, 116, A_LineNumber)
AssertEq(Both >= 2, 117, A_LineNumber)
AssertEq(Both = 2, 118, A_LineNumber)
AssertEq(Both == 2, 119, A_LineNumber)
AssertEq(Both != 2, 120, A_LineNumber)
AssertEq(Both !== 2, 121, A_LineNumber)
AssertEq(Both . 2, 122, A_LineNumber)
AssertEq(Both ~= 2, 123, A_LineNumber)
AssertEq(Both !~= 2, 124, A_LineNumber)
AssertEq(+Both, 123, A_LineNumber)
AssertEq(+Both(), 223, A_LineNumber)
AssertEq(-Both, 124, A_LineNumber)
AssertEq(~Both, 125, A_LineNumber)
AssertEq(!Both, 126, A_LineNumber)
AssertEq(Boolean(Both), true, A_LineNumber)
AssertEq(Boolean(Both()), false, A_LineNumber)
Both.Value := 0
AssertEq(Both ? 1 : 0, 0, A_LineNumber)
AssertEq(Both || 42, 42, A_LineNumber)
Both.Value := 100
AssertEq(Both && 42, 42, A_LineNumber)
alias := Both
alias += 2
AssertEq(alias, 102, A_LineNumber)
alias := Both
old := alias++
AssertEq(old.Value, 101, A_LineNumber)
AssertEq(alias.Value, 101, A_LineNumber)
AssertEq((--alias).Value, 100, A_LineNumber)

class DerivedBoth extends Both {
    static Value := 300
    static -() => 900
    +(Right) => 800 + Right
}
AssertEq(DerivedBoth + 2, 302, A_LineNumber)
AssertEq(DerivedBoth() + 2, 802, A_LineNumber)
AssertEq(-DerivedBoth, 900, A_LineNumber)
AssertEq(DerivedBoth - 2, 303, A_LineNumber)
class PlainDerived extends Both {
}
AssertEq(PlainDerived + 2, 102, A_LineNumber)

class StaticOnly {
    static +(Right) => 10 + Right
}
class InstanceOnly {
    +(Right) => 20 + Right
}
AssertEq(StaticOnly + 2, 12, A_LineNumber)
Throws(() => StaticOnly() + 2, A_LineNumber, TypeError)
Throws(() => InstanceOnly + 2, A_LineNumber, TypeError)
Throws(() => Both() - 2, A_LineNumber, TypeError)
Throws(() => 2 + Both, A_LineNumber, TypeError)
Throws(() => Both.Prototype + 2, A_LineNumber, TypeError)
alias := StaticOnly
AssertEq(++alias, 11, A_LineNumber)

class Outer {
    class Inner {
        static +(Right) => Right + 50
        +(Right) => Right + 60
    }
}
AssertEq(Outer.Inner + 2, 52, A_LineNumber)
AssertEq(Outer.Inner() + 2, 62, A_LineNumber)
class Startup {
    static Result := Later + 2
}
class Later {
    static Value := 70
    static Result := this + 1
    static +(Right) => this.Value + Right
}
AssertEq(Startup.Result, 72, A_LineNumber)
AssertEq(Later.Result, 71, A_LineNumber)

struct OperatorStructBase {
    Value : Int32
    static Value := 80
    +(Right) => this.Value + Right
    static +(Right) => this.Value + Right
}
struct OperatorStructDerived extends OperatorStructBase {
    static Value := 90
}
structValue := OperatorStructDerived()
structValue.Value := 5
AssertEq(structValue + 2, 7, A_LineNumber)
AssertEq(OperatorStructDerived + 2, 92, A_LineNumber)
dynamicStructClass := Class("DynamicOperatorStruct", OperatorStructDerived)
dynamicStructValue := dynamicStructClass()
dynamicStructValue.Value := 7
AssertEq(dynamicStructValue + 2, 9, A_LineNumber)
AssertEq(dynamicStructClass + 2, 92, A_LineNumber)

class ManifestChild extends ManifestBase {
}
class ManifestBase {
    +(Right) => 700 + Right
    static +(Right) => 800 + Right
}
AssertEq(ManifestChild() + 2, 702, A_LineNumber)
AssertEq(ManifestChild + 2, 802, A_LineNumber)

dynamicClass := Class("Dynamic", Both)
dynamicClass.Value := 400
AssertEq(dynamicClass + 2, 402, A_LineNumber)
AssertEq(dynamicClass() + 2, 202, A_LineNumber)
copy := Both.Clone()
AssertEq(copy + 2, 102, A_LineNumber)
AssertEq(Boolean(Class()), true, A_LineNumber)
class ClassLike extends Class {
    static +(Right) => 500 + Right
    +(Right) => 600 + Right
}
AssertEq(ClassLike + 2, 502, A_LineNumber)
; Class.Call constructs a class object, not an ordinary instance of the derived factory.
Throws(() => ClassLike() + 2, A_LineNumber, TypeError)
AssertEq(OperatorModuleA.Shared + 2, 12, A_LineNumber)
AssertEq(OperatorModuleB.Shared + 2, 22, A_LineNumber)
AssertEq(OperatorModuleA.Shared() + 2, 32, A_LineNumber)
AssertEq(OperatorModuleB.Shared() + 2, 42, A_LineNumber)

class BrokenStatic {
    static +(Right) {
        return unset
    }
    static -() {
        throw ValueError("static operator")
    }
}
Throws(() => BrokenStatic + 1, A_LineNumber, UnsetError)
Throws(() => -BrokenStatic, A_LineNumber, ValueError)

Both.DefineProp("+", {Call: (Self, Right) => 999})
Both.Base := StaticOnly
Both.Prototype := StaticOnly.Prototype
AssertEq(Both + 2, 102, A_LineNumber)
AssertEq(copy + 2, 102, A_LineNumber)
AssertEq(dynamicClass + 2, 402, A_LineNumber)
FileAppend "pass", "*"

#Module OperatorModuleA
class Shared {
    static +(Right) => 10 + Right
    +(Right) => 30 + Right
}

#Module OperatorModuleB
class Shared {
    static +(Right) => 20 + Right
    +(Right) => 40 + Right
}
