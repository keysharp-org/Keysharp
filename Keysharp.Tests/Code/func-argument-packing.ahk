#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

Collect(values*) => values
class PackingTarget {
    Collect(head, rest*) => [this, head, rest]
    Value {
        get => this.Stored
        set => this.Stored := value
    }
    __Item[keys*] {
        get => keys
        set => this.LastWrite := [keys, value]
    }
}
target := PackingTarget()
source := [1, 2, 3]
result := target.Collect("head", source*)
AssertEq(result[1], target, A_LineNumber)
AssertEq(result[2], "head", A_LineNumber)
AssertEq(result[3].Length, 3, A_LineNumber)
result[3][1] := 99
AssertEq(source[1], 1, A_LineNumber)
bound := ObjBindMethod(target, "Collect", "bound")
AssertEq(bound(source*)[3][1], 1, A_LineNumber)
AssertEq(target.Collect("empty")[3].Length, 0, A_LineNumber)
target.Value := source
AssertEq(target.Value, source, A_LineNumber)
target["a", "b"] := source
AssertEq(target.LastWrite[1].Length, 2, A_LineNumber)
AssertEq(target.LastWrite[1][2], "b", A_LineNumber)
AssertEq(target.LastWrite[2], source, A_LineNumber)
target[] := 42
AssertEq(target.LastWrite[1].Length, 0, A_LineNumber)
AssertEq(target.LastWrite[2], 42, A_LineNumber)
AssertEq(target["a", "b"][2], "b", A_LineNumber)

holes := [1, , 3]
copied := Collect(holes*)
AssertEq(copied.Length, 3, A_LineNumber)
Assert(!copied.Has(2), A_LineNumber)
copied[1] := 20
AssertEq(holes[1], 1, A_LineNumber)
AssertEq(Collect([]*).Length, 0, A_LineNumber)

ChangeSource() {
    global source
    source[1] := 90
    return 4
}
result := Collect(source*, ChangeSource())
AssertEq(result[1], 1, A_LineNumber)
AssertEq(result[4], 4, A_LineNumber)
AssertEq(source[1], 90, A_LineNumber)

nativeEnum := Array.Prototype.__Enum
enumReads := 0
OverrideEnum(this, count) {
    global nativeEnum
    return nativeEnum.Call([77], count)
}
GetEnum(this) {
    global enumReads
    enumReads++
    return OverrideEnum
}
GetNativeEnum(this) {
    global enumReads, nativeEnum
    enumReads++
    this[1] := 88
    return nativeEnum
}
custom := [1, 2]
custom.DefineProp("__Enum", {Get: GetEnum})
result := Collect(custom*)
AssertEq(enumReads, 1, A_LineNumber)
AssertEq(result.Length, 1, A_LineNumber)
AssertEq(result[1], 77, A_LineNumber)
custom.DefineProp("__Enum", {Get: GetNativeEnum})
result := Collect(custom*)
AssertEq(enumReads, 2, A_LineNumber)
AssertEq(result[1], 88, A_LineNumber)
AssertEq(result.Length, 2, A_LineNumber)

savedEnum := Array.Prototype.GetOwnPropDesc("__Enum")
try {
    Array.Prototype.DefineProp("__Enum", {Call: OverrideEnum})
    result := Collect(source*)
    AssertEq(result.Length, 1, A_LineNumber)
    AssertEq(result[1], 77, A_LineNumber)
} finally {
    Array.Prototype.DefineProp("__Enum", savedEnum)
}

class CustomArray extends Array {
    __Enum(count) => OverrideEnum(this, count)
}
result := Collect(CustomArray(1, 2)*)
AssertEq(result[1], 77, A_LineNumber)
total := 0
for value in [1, 2, 3]
    total += value
AssertEq(total, 6, A_LineNumber)
for values in [[], [1], [1, 2], [1, 2, 3], [1, 2, 3, 4]]
    AssertEq(values.Capacity, 4, A_LineNumber)

FileAppend "pass", "*"
