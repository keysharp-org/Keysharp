#NoTrayIcon
#Include <assert>

a := 1
globalFatArrow := (*) => a := 2
globalFatArrow()

AssertEq(a, 1, A_LineNumber)

a := 1
globalFatArrowWithArg := (a) => a := 2
globalFatArrowWithArg(1)

AssertEq(a, 1, A_LineNumber)

a := 1
globalAnonFunc := (*) {
    a := 2
}
globalAnonFunc()

AssertEq(a, 1, A_LineNumber)

g()

g() {
    localClosure() {
        b := 2
    }

    b := 1
    localClosure()
    AssertEq(b, 2, A_LineNumber)

    b := 1
    localAnonClosure := (*) => b := 2
    localAnonClosure()
    AssertEq(b, 2, A_LineNumber)

    static localStaticClosure() {
        b := 2
    }

    b := 1
    localStaticClosure()
    AssertEq(b, 1, A_LineNumber)

    closureLocalVar() {
        local b := 2
    }

    b := 1
    closureLocalVar()
    AssertEq(b, 1, A_LineNumber)

    closureStaticVar() {
        static c := 2
    }

    static c := 1
    closureStaticVar()
    AssertEq(c, 1, A_LineNumber)
}

StaticLocalFuncs() {
    return f2()
    static f1() => 1
    static f2() => f1()
}

a := 0
a := StaticLocalFuncs()

AssertEq(a, 1, A_LineNumber)

StaticDynClosure() {
    static name := "a"
    static a := 1

    closureRead()
    closureWrite()

    closureRead() {
        AssertEq(%name%, 1, A_LineNumber)
    }

    closureWrite() {
        %name% := 5
    }

    AssertEq(a, 5, A_LineNumber)
}

StaticDynClosure()

; A nested function is a closure only when it, or a function within it, reads a local of an enclosing function, directly
; or by referring to a closure. Any other is one object however often the enclosing function runs. `==` compares a
; function by its method, so identity is checked through an own property.
SameFunc(a, b) {
    static n := 0
    a.DefineProp("Mark" (++n), {Value: 1})
    return b.HasOwnProp("Mark" n)
}

StaticOnlyNested() {
    static count := 0
    ++count
    return nested

    nested() {
        return count
    }
}

f1 := StaticOnlyNested(), f2 := StaticOnlyNested()
Assert(SameFunc(f1, f2), A_LineNumber)
Assert(not f1 is Closure, A_LineNumber)
AssertEq(f1(), 2, A_LineNumber)

StaticOnlyArrow() {
    static count := 0
    return () => count
}

f1 := StaticOnlyArrow(), f2 := StaticOnlyArrow()
Assert(SameFunc(f1, f2), A_LineNumber)
Assert(not f1 is Closure, A_LineNumber)

LocalCaptureNested() {
    x := 1
    return nested

    nested() => x
}

f1 := LocalCaptureNested(), f2 := LocalCaptureNested()
Assert(not SameFunc(f1, f2), A_LineNumber)
Assert(f1 is Closure, A_LineNumber)

SelfRefNested() {
    return tick

    tick() => tick
}

f1 := SelfRefNested(), f2 := SelfRefNested()
Assert(SameFunc(f1, f2), A_LineNumber)
Assert(SameFunc(f1, f1()), A_LineNumber)
Assert(not f1 is Closure, A_LineNumber)

SiblingNested() {
    return first

    first() => second
    second() => first
}

f1 := SiblingNested(), f2 := SiblingNested()
Assert(SameFunc(f1, f2), A_LineNumber)
Assert(SameFunc(f1()(), f1), A_LineNumber)
Assert(not f1 is Closure, A_LineNumber)

; B refers to C, declared later, which captures x; so B reads Outer's frame through A.
ForwardThroughOuter() {
    x := 5
    return A

    A() {
        B() => C
        return B
    }

    C() => x
}

f1 := ForwardThroughOuter()
Assert(f1 is Closure, A_LineNumber)
Assert(f1() is Closure, A_LineNumber)
AssertEq(f1()()(), 5, A_LineNumber)

CaptureThroughNested() {
    x := 6
    return first

    first() {
        inner() => x
        return inner
    }
}

f1 := CaptureThroughNested()
Assert(f1 is Closure, A_LineNumber)
AssertEq(f1()(), 6, A_LineNumber)

; The inner arrow reaches g only through g's own name, and g captures x.
NamedArrowCapture() {
    x := 7
    return g(k) => k = 0 ? x : () => g
}

f1 := NamedArrowCapture()
Assert(f1 is Closure, A_LineNumber)
Assert(f1(1) is Closure, A_LineNumber)
Assert(SameFunc(f1(1)(), f1), A_LineNumber)
AssertEq(f1(1)()(0), 7, A_LineNumber)

; A named fat arrow is a function of the function holding it, as a nested function is: its name is one object for the
; whole call, bound before the expression runs. Outside any function it is a function of the module, and in a field
; initializer one of __Init.
NamedArrowScope() {
    x := 1
    early := g
    loop 2
        last := g() => (x, g)
    return [early, last, g]
}

r := NamedArrowScope()
Assert(SameFunc(r[1], r[2]) && SameFunc(r[2], r[3]) && SameFunc(r[2], r[2]()), A_LineNumber)

topNamed := TopNamedArrow() => 9
TopNamedCaller() => TopNamedArrow
Assert(SameFunc(TopNamedCaller(), topNamed), A_LineNumber)

class ArrowField {
    cb := fieldArrow() => fieldArrow
}

f1 := ArrowField().cb
Assert(SameFunc(f1, f1()), A_LineNumber)

; A dynamic reference, a RegEx callout included, reaches only the enclosing variables the function names, so they
; decide whether it is a closure.
RegExHelper(text) {
    isNum(s) => RegExMatch(s, "^\d+$")
    return isNum
}

f1 := RegExHelper("a"), f2 := RegExHelper("b")
Assert(SameFunc(f1, f2), A_LineNumber)
Assert(f1("12"), A_LineNumber)

DynamicUpVar() {
    x := 1, y := 2
    reader(n) => (y, %n%)
    return reader
}

f1 := DynamicUpVar()
Assert(f1 is Closure, A_LineNumber)
AssertEq(f1("y"), 2, A_LineNumber)

class ClosureOwner {
    Field := 8

    ThisDeep() {
        outer() {
            deep() => this.Field
            return deep
        }
        return outer
    }

    NoThis() {
        nested() => 1
        return nested
    }
}

obj := ClosureOwner()
f1 := obj.ThisDeep()
Assert(f1 is Closure, A_LineNumber)
AssertEq(f1()(), 8, A_LineNumber)
Assert(SameFunc(obj.NoThis(), obj.NoThis()), A_LineNumber)

; Each call finds the timer the previous one set, so it restarts rather than adding another.
MultiPress() {
    static count := 0
    ++count
    SetTimer(done, -20)

    done() {
        global pressed
        pressed .= count ","
        count := 0
        SetTimer(done, 0)
    }
}

global pressed := ""
MultiPress(), MultiPress(), MultiPress()
loop 100 {
    if pressed != ""
        break
    Sleep(20)
}
Sleep(60)
AssertEq(pressed, "3,", A_LineNumber)

; A closure's timer finds itself by the closure's own name to stop.
selfStopped := {n: 0}

SelfStopping() {
    step := 1
    SetTimer(tick() => (selfStopped.n += step, selfStopped.n >= 2 && SetTimer(tick, 0)), 10)
}

SelfStopping()
loop 100 {
    if selfStopped.n >= 2
        break
    Sleep(10)
}
Sleep(80)
AssertEq(selfStopped.n, 2, A_LineNumber)

FileAppend "pass", "*"
