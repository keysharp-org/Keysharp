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

FileAppend "pass", "*"
