#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Ks { Collect }
#Include <assert>

; Return true to continue enumerating, false to stop
EnumFunc(&x) {
    static i := 0
    x := ++i
    if (i == 4)
        i := 0
    return i != 0
}

a := 0
; An enumerator is a function which can just be called
EnumFunc(&a)
AssertEq(a, 1, A_LineNumber)

; Reset the internal counter to 0
EnumFunc(&a)
EnumFunc(&a)
EnumFunc(&a)

a := 0
; Iterable objects have __Enum(NumOfArgs) defined, which can be called to get an enumerator with a certain number of arguments
arr := [2,3,4]
e := arr.__Enum(2)

while (e(&i, &j))
    a += j

AssertEq(a, 9, A_LineNumber)

MakeDisposedEnumerator(deleted) {
    iterator := [1].__Enum(1)
    iterator.DefineProp("__Delete", {Value: (*) => deleted.Push(1)})
    for disposedValue in iterator {
    }
    AssertEq(deleted.Length, 0, A_LineNumber)
}
deletedEnumerators := []
MakeDisposedEnumerator(deletedEnumerators)
timeout := A_TickCount + 2000
while !deletedEnumerators.Length && A_TickCount < timeout
    Sleep(30), Collect()
AssertEq(deletedEnumerators.Length, 1, A_LineNumber)

entries := Map("alpha", "A", "bravo", "B")
RegExMatch("hello", "(?<word>hello)", &match)
for scenario in [
    {Source: arr, Single: 2, First: [1, 2], Second: [2, 3]},
    {Source: entries, Single: "alpha", First: ["alpha", "A"], Second: ["bravo", "B"]},
    {Source: match, Single: "hello", First: [0, "hello"], Second: ["word", "hello"]}
] {
    next := scenario.Source.__Enum()
    Assert(next(&value), A_LineNumber)
    AssertEq(value, scenario.Single, A_LineNumber)
    Assert(next(&key, &value), A_LineNumber)
    AssertEq(key, scenario.Second[1], A_LineNumber)
    AssertEq(value, scenario.Second[2], A_LineNumber)

    next := scenario.Source.__Enum(1)
    Throws(() => next(), A_LineNumber, ArgumentError)
    Throws(next.Bind(&value, {}), A_LineNumber, ArgumentError)
    Throws(() => next(123), A_LineNumber, TypeError)
    Assert(next(&value), A_LineNumber)
    AssertEq(value, scenario.Single, A_LineNumber)

    next := scenario.Source.__Enum(2)
    key := "unchanged"
    Throws(next.Bind(&key), A_LineNumber, ArgumentError)
    Throws(next.Bind(&key, 123), A_LineNumber, TypeError)
    AssertEq(key, "unchanged", A_LineNumber)
    Assert(next(&key, &value), A_LineNumber)
    AssertEq(key, scenario.First[1], A_LineNumber)
    AssertEq(value, scenario.First[2], A_LineNumber)
}

next := arr.__Enum(0)
Throws(() => next(unset, unset, unset), A_LineNumber, ArgumentError)
Assert(next(), A_LineNumber)
Assert(next(&value), A_LineNumber)
AssertEq(value, 3, A_LineNumber)
Assert(next(unset), A_LineNumber)
Assert(!next(&value), A_LineNumber)
Throws(() => next(123), A_LineNumber, TypeError)
next := arr.__Enum(2)
Assert(next(&key, unset), A_LineNumber)
AssertEq(key, 1, A_LineNumber)

getterCalls := []
enumerationTarget := {}.DefineProp("Value", {Get: (*) => (getterCalls.Push(1), 7)})
next := enumerationTarget.OwnProps()
Assert(next(&key,), A_LineNumber)
AssertEq(key, "Value", A_LineNumber)
AssertEq(getterCalls.Length, 0, A_LineNumber)
next := enumerationTarget.OwnProps()
Assert(next(, &value), A_LineNumber)
AssertEq(value, 7, A_LineNumber)
AssertEq(getterCalls.Length, 1, A_LineNumber)

; A for loop checks whether the passed object has __Enum, and calls it with the number of requested arguments.
; For example `for i, j, k in e` calls `e.__Enum(3)` to get the enumerator function (if __Enum is defined) 
; If an object doesn't contain __Enum, then the object/function is assumed to be the enumerator
a := 0
for i in EnumFunc {
    a += i
}

AssertEq(a, 6, A_LineNumber)

a := 0
; Using the spread operator calls either the enumerator function, or __Enum(1)
a := [EnumFunc*].Length

AssertEq(a, 3, A_LineNumber)

Enum1(&x) {
    static i := 0
    x := Mod(++i, 4) * 2
    return x // 2
}

Enum2(&x, &y) {
    static i := 0
    x := Mod(++i, 4), y := x * 2
    return x
}

o := {}.DefineProp("__Enum", {call: (this, num) => num == 1 ? Enum1 : Enum2})

a := 0
; In this case __Enum will be called with the number of requested paramenters, so it is equivalent to `for i in o.__Enum(1)`
for i in o {
    a += i
}

AssertEq(a, 12, A_LineNumber)

a := 0, b := 0
; And this one is equivalent to `for i in o.__Enum(2)`
for i, j in o {
    a += i
    b += j
}

AssertEq(a, 6, A_LineNumber)

AssertEq(b, 12, A_LineNumber)

a := 0
; Here `o.__Enum(1)` is called
a := [o*]

AssertEq(a[3], 6, A_LineNumber)

a := 0
; Using Bind we can ignore certain arguments, here the second argument is ignored
a := [o.__Enum(2).Bind(, &_)*] ; This is also testing a difficult to parse statement that ends in the * spread operator inside of an array literal.

AssertEq(a[3], 3, A_LineNumber)

a := 0
; And now the first argument is ignored.
a := [o.__Enum(2).Bind(&_)*]

AssertEq(a[3], 6, A_LineNumber)
    
try {
    ; Incorrect number of parameters should throw.
    for i, j in EnumFunc {
        a += i
    }
    Assert(false, A_LineNumber)
} catch {
}

FileAppend "pass", "*"
