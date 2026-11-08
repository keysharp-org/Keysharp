#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon

#Requires AutoHotkey v2.1-alpha
#Include <assert>

struct X {
    y : Int32[10]
}

struct XX {
    items : X[2]
}

; A struct field of array type contributes its full array size, and exposes Length/element access.
z := X()
Assert(!(z.Size != 40 || z.y.Size != 40 || z.y.Length != 10), A_LineNumber)

z.y[1] := 11
z.y[10] := 99
Assert(!(z.y[1] != 11 || z.y[10] != 99), A_LineNumber)

Assert(!(!(z.y is Int32[10]) || (z.y is Int32[100])), A_LineNumber)

; Nested array of structs: XX has 2 X's, each 40 bytes => 80.
zz := XX()
Assert(!(zz.Size != 80), A_LineNumber)

; Class-level indexing of a struct class yields a fixed-size structured-array class.
arrCls := Int32[10]
inst := arrCls()

Assert(!(inst.Size != 40), A_LineNumber)

Assert(!(inst.Length != 10), A_LineNumber)

inst[1] := 100
inst[10] := 200
inst[-1] := 250   ; negative index counts from the end, so -1 is element 10

Assert(!(inst[1] != 100), A_LineNumber)

Assert(!(inst[10] != 250 || inst[-1] != 250), A_LineNumber)

; The array class has stable identity (cached), and differs by element type and length.
Assert(!(!(inst is Int32[10])), A_LineNumber)

Assert(!(inst is Int32[5]), A_LineNumber)

Assert(!(inst is Float32[10]), A_LineNumber)

; Out-of-bounds indices throw IndexError.
threw := false
try
    _ := inst[11]
catch IndexError
    threw := true
Assert(!(!threw), A_LineNumber)

threw := false
try
    _ := inst[0]
catch IndexError
    threw := true
Assert(!(!threw), A_LineNumber)

; Structured-array arguments pass their storage address, regardless of its size.
arrayAddressCb := CallbackCreate(address => address, "Fast", [IntPtr, IntPtr])
for arrayType in [Int8[2], Int32[2], Int32[10]] {
    nativeArray := arrayType()
    nativeArray[1] := 7
    AssertEq(DllCall(arrayAddressCb, arrayType, nativeArray, "Ptr"), nativeArray.Ptr, A_LineNumber)
}

Throws(() => DllCall(arrayAddressCb, Int32[2]), A_LineNumber, ValueError)
CallbackFree(arrayAddressCb)

writeArrayCb := CallbackCreate(address => NumPut("Int", 42, address), "Fast", [IntPtr, "void"])
DllCall(writeArrayCb, Int32[10], &outputArray, "void")
Assert(outputArray is Int32[10], A_LineNumber)
AssertEq(outputArray[1], 42, A_LineNumber)
CallbackFree(writeArrayCb)

; Callback array parameters share the caller's storage and preserve array subclasses of every size.
arraySubtype := Class("ArraySubtype", Int32[2])
for arrayType in [Float32[2], Int32[10], arraySubtype] {
    callbackArray := arrayType()
    callbackArray[1] := 12
    arrayCb := CallbackCreate(values => values is arrayType ? (values[1] := values[1] + 1, values.Ptr) : 0, "Fast", [arrayType, IntPtr])
    AssertEq(DllCall(arrayCb, "Ptr", callbackArray.Ptr, "Ptr"), callbackArray.Ptr, A_LineNumber)
    AssertEq(callbackArray[1], 13, A_LineNumber)
    CallbackFree(arrayCb)
}
Throws(() => CallbackCreate(() => callbackArray, "Fast", [arraySubtype]), A_LineNumber, ValueError)

; An array of exactly the pointer's target class decays to the address of its first element, both in a typed
; property, which then reads back as the same array, and in a DllCall argument. [v2.1-alpha.34+]
struct BytePointer {
    Target : UInt8.Ptr
}
bytes := UInt8[4]()
pointer := BytePointer()
pointer.Target := bytes
Assert(pointer.Target == bytes, A_LineNumber)
AssertEq(NumGet(pointer, "ptr"), bytes.Ptr, A_LineNumber)
Throws(() => pointer.Target := Int8[4](), A_LineNumber, TypeError)
pointer.Target := unset
AssertEq(pointer.Target ?? "none", "none", A_LineNumber)
pointer.Target := bytes
fillCb := CallbackCreate(address => NumPut("UChar", 65, address), "Fast", [IntPtr, "void"])
DllCall(fillCb, UInt8.Ptr, bytes, "void")
AssertEq(bytes[1], 65, A_LineNumber)
CallbackFree(fillCb)

struct ARRAY_INIT {
    static Created := 0
    Number : Int32 := 7
    __New() {
        ARRAY_INIT.Created += 1
        this.Number += 1
    }
}
initialized := ARRAY_INIT[2]()
AssertEq(ARRAY_INIT.Created, 2, A_LineNumber)
AssertEq(initialized[1].Number, 8, A_LineNumber)
AssertEq(initialized[2].Number, 8, A_LineNumber)
Assert(initialized[1] == initialized[1], A_LineNumber)
Assert(initialized[1] != initialized[2], A_LineNumber)
initializedView := ARRAY_INIT[2].At(initialized)
AssertEq(initializedView[1].Number, 8, A_LineNumber)
AssertEq(ARRAY_INIT.Created, 2, A_LineNumber)

struct ARRAY_VALUE {
    Number : Int32 := 3
    __Value {
        get => this.Number ? this.Number * 10 : unset
        set => this.Number := value
    }
}
values := ARRAY_VALUE[2]()
AssertEq(values[1], 30, A_LineNumber)
values[2] := 6
AssertEq(values[2], 60, A_LineNumber)
values[2] := 0
AssertEq(values[2] ?? "unset", "unset", A_LineNumber)
Throws(() => values[2], A_LineNumber, UnsetError)
targets := Int32.Ptr[2]()
target := Int32(17)
targets[1] := target
Assert(targets[1] == target, A_LineNumber)
AssertEq(targets[1].__Value, 17, A_LineNumber)
AssertEq(targets[2] ?? "unset", "unset", A_LineNumber)

struct ARRAY_NUMBER extends Int32 {
    __New() => this.__Value := 7
}
numbers := ARRAY_NUMBER[2]()
AssertEq(numbers[1], 7, A_LineNumber)
AssertEq(numbers[2], 7, A_LineNumber)

; Overflowing layouts are rejected before allocation or any access through a pointer.
overflowArray := Int64[0x7FFFFFFFFFFFFFFF]
Throws(() => overflowArray.Prototype.Size, A_LineNumber, TypeError)
Throws(() => overflowArray(), A_LineNumber, TypeError)
Throws(() => overflowArray.At(0), A_LineNumber, TypeError)

FileAppend "pass", "*"
