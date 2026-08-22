#NoTrayIcon
#Include <assert>

ZeroParams() => 0

try {
    ZeroParams()
} catch {
    Assert(false, A_LineNumber)
}

Throws(() => ZeroParams(0), A_LineNumber)

OneParam(a) => 0

Throws(() => OneParam(), A_LineNumber)

try {
    OneParam(0)
} catch {
    Assert(false, A_LineNumber)
}

Throws(() => OneParam(0, 0), A_LineNumber)

try {
    OneParam(0, unset)
} catch {
    Assert(false, A_LineNumber)
}

VariadicOneParam(a*) => 0

try {
    VariadicOneParam()
} catch {
    Assert(false, A_LineNumber)
}

try {
    VariadicOneParam(0)
} catch {
    Assert(false, A_LineNumber)
}

try {
    VariadicOneParam(0, 0)
} catch {
    Assert(false, A_LineNumber)
}

class TestClass1 {
    __Item[a] {
        get => 0
        set => 0
    }
}

t1 := TestClass1()

Throws(() => (a := t1[]), A_LineNumber)

try {
    a := t1[1]
} catch {
    Assert(false, A_LineNumber)
}

Throws(() => (a := t1[1, 2]), A_LineNumber)

class TestClass2 {
    __Item[a*] {
        get => 0
        set => 0
    }
}

t2 := TestClass2()

try {
    a := t2[]
} catch {
    Assert(false, A_LineNumber)
}

try {
    a := t2[1]
} catch {
    Assert(false, A_LineNumber)
}

try {
    a := t2[1, 2]
} catch {
    Assert(false, A_LineNumber)
}

class TestClass3 {
    __Item[a, b*] {
        get => 0
        set => 0
    }
}

t3 := TestClass3()

Throws(() => (a := t3[]), A_LineNumber)

try {
    a := t3[1]
} catch {
    Assert(false, A_LineNumber)
}

try {
    a := t3[1, 2]
} catch {
    Assert(false, A_LineNumber)
}

FileAppend "pass", "*"
