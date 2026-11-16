#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

struct POINT {
    x : Int32
    y : Int32
}

struct COMMA_POINT {
    x : Int32, y : Int32
}

struct BOX {
    pt : POINT
}

struct BASE_POINT {
    x : Int32
}

struct CHILD_POINT extends BASE_POINT {
    y : Int32
}

struct EARLY_POINT {
    x : Int32
    y : Int32
}

struct LATE_POINT {
    x : Int32
    y : Int32
}

struct FLOAT_FIELD {
    f : Float32
}

struct DWORD {
    v : UInt32
    __Value {
        get => this.v
        set => this.v := value
    }
}

struct PACKED {
    a : Int8
}

struct UNION {
    a : Int32
}

struct DYN_BASE {
}

struct PTR_HOLDER {
}

struct PACK_BASE {
    x : Int32
}

struct PACK_CHILD extends PACK_BASE {
    y : Int32
}

struct LOCK_BASE {
    x : Int32
}

struct LOCK_CHILD extends LOCK_BASE {
    y : Int32
}

struct FWD_BOX {
    pt : FWD_POINT
}

struct FWD_POINT {
    x : Int32
    y : Int32
}

struct INIT_BASE {
    x : Int32
}

struct INIT_CHILD extends INIT_BASE {
    __Init() {
        this.x := 42
    }
}

struct MY_INT extends Int32 {
}

; A type specifier may be a parenthesized expression, or an expression starting with an identifier
; followed by calls/property access. It is evaluated in static __Init(), so `this` is the class object.
; [v2.1-alpha.30]
struct DYNAMIC_TYPES {
    static PickType() => Int16
    a : (Int32)
    b : this.PickType()
    c : Int8[3]
}

class CLASS_GET_BASE {
    a {
        get {
            throw Error("getter called")
        }
    }
}

class CLASS_FIELD_ASSIGN extends CLASS_GET_BASE {
    a := 1
}

PropIsSet(obj, name) {
    try {
        _ := obj.%name%
        return true
    } catch as _propertyError {
        if _propertyError is UnsetError
            return false
        throw _propertyError
    }
}

dp := Object.DefineProp
dp(EARLY_POINT.Prototype, "z", {type:Int32})
dp(PACKED.Prototype, "b", {type:Int32, pack:1})
dp(UNION.Prototype, "b", {type:Int32, offset:"a"})
dp(PTR_HOLDER.Prototype, "pt", {type:POINT.Ptr})

pt := POINT(10, 20)
for args in [[1], [1, 2, 3]]
    Throws(() => POINT(args*), A_LineNumber, Error)
for args in [[unset, 2], [Int32(1), 2]]
    Throws(() => POINT(args*), A_LineNumber, TypeError)
Throws(() => Int32[2](1), A_LineNumber, Error)
AssertEq(POINT.Ptr(pt.Ptr).__Value.Ptr, pt.Ptr, A_LineNumber)
AssertEq(Struct.At(0).Size, 0, A_LineNumber)
AssertEq(Struct.At(1).Ptr, 1, A_LineNumber)

; Struct instances cannot own dynamic properties, including through the Obj functions.
for target in [pt, POINT.At(pt.Ptr), Int32(), POINT.Ptr(), Int32[2]()] {
    Throws(() => ObjGetCapacity(target), A_LineNumber, TypeError)
    Throws(() => ObjSetCapacity(target, 10), A_LineNumber, TypeError)
    Throws(() => ObjHasOwnProp(target, "x"), A_LineNumber, TypeError)
    Throws(() => ObjOwnPropCount(target), A_LineNumber, TypeError)
    Throws(() => ObjOwnProps(target), A_LineNumber, TypeError)
    Throws(() => dp(target, "Extra", {Value: 1}), A_LineNumber, TypeError)
}
Assert(ObjGetCapacity(POINT.Prototype) >= 2, A_LineNumber)

Assert(!(pt.Size != 8), A_LineNumber)

Assert(!(!HasProp(pt, "x") || !HasProp(pt, "y")), A_LineNumber)

pt.__New()

Assert(!(pt.x != 10 || pt.y != 20), A_LineNumber)

cpt := COMMA_POINT()
cpt.x := 7
cpt.y := 9

Assert(!(cpt.Size != 8 || cpt.x != 7 || cpt.y != 9), A_LineNumber)

holder := BOX()
holder.pt.x := 10
holder.pt.y := 20

Assert(!(holder.pt.x != 10 || holder.pt.y != 20), A_LineNumber)

threw := false
try
    holder.pt := pt
catch as e
    threw := e is Error && e.Message == "Assignment to struct is not supported."

Assert(!(!threw), A_LineNumber)

pt2 := POINT.At(pt.Ptr)

Assert(!(pt2.x != 10 || pt2.y != 20), A_LineNumber)

num := Int32(42)

Assert(!(num.Size != 4 || num.__Value != 42), A_LineNumber)

Assert(!(!HasProp(num, "__Value")), A_LineNumber)

pointPtrClass := POINT.Ptr

Assert(!(!(pointPtrClass is Class)), A_LineNumber)

Assert(!(ObjHasOwnProp(POINT, "Ptr")), A_LineNumber)

Assert(!(!HasBase(CHILD_POINT.Ptr, BASE_POINT.Ptr) || !HasBase(CHILD_POINT.Ptr.Prototype, BASE_POINT.Ptr.Prototype)), A_LineNumber)

ptrnum := Int32.Ptr()
ptrtarget := Int32()
ptrtarget.__Value := 123
ptrnum.__Value := ptrtarget

Assert(!(ptrnum.Size != A_PtrSize || ptrnum.__Value.__Value != 123), A_LineNumber)

iptr := IntPtr()
iptr.__Value := 456

Assert(!(iptr.Size != A_PtrSize || iptr.__Value != 456), A_LineNumber)

unusedPtrClass := EARLY_POINT.Ptr

early := EARLY_POINT()
early.x := 1
early.y := 2
early.z := 3

Assert(!(early.Size != 12 || early.z != 3), A_LineNumber)

late := LATE_POINT()

threw := false
try
    dp(LATE_POINT.Prototype, "z", {type:Int32})
catch
    threw := true

Assert(!(!threw), A_LineNumber)

packedInst := PACKED()
packedInst.b := 0x11223344

Assert(!(packedInst.Size != 5 || packedInst.b != 0x11223344), A_LineNumber)

unionInst := UNION(2, 1)

Assert(!(unionInst.Size != 4 || unionInst.a != 2), A_LineNumber)

ptrHolder := PTR_HOLDER()

Assert(!(PropIsSet(ptrHolder, "pt")), A_LineNumber)

ptrHolder.__New(pt)

Assert(!(!(ptrHolder.pt is POINT) || ptrHolder.pt.Ptr != pt.Ptr), A_LineNumber)

ptrHolder.__New(unset)

Assert(!(PropIsSet(ptrHolder, "pt")), A_LineNumber)

DynPoint := Class("DynPoint", DYN_BASE)
dp(DynPoint.Prototype, "x", {type:Int32})
dp(DynPoint.Prototype, "y", {type:Int32})

DynChild := Class("DynChild", DynPoint)
dp(DynChild.Prototype, "z", {type:Int32})

dynPoint := DynPoint()
dynPoint.x := 10
dynPoint.y := 20

dynChild := DynChild(1, 2, 3)

Assert(!(dynPoint.Size != 8
    || dynPoint.x != 10
    || dynPoint.y != 20
    || dynChild.Size != 12
    || dynChild.x != 1
    || dynChild.y != 2
    || dynChild.z != 3), A_LineNumber)

continuedInitializers := {Count: 0}
continueInitializer := (error, mode) => (continuedInitializers.Count++, -1)
OnError(continueInitializer)
dynChild.__New(10, "invalid", 30)
OnError(continueInitializer, 0)
AssertEq(continuedInitializers.Count, 1, A_LineNumber)
AssertEq(dynChild.x, 1, A_LineNumber)
AssertEq(dynChild.y, 2, A_LineNumber)
AssertEq(dynChild.z, 30, A_LineNumber)

buf := Buffer(4)
atLock := POINT.At(buf.Ptr)

threw := false
try
    dp(POINT.Prototype, "z", {type:Int32})
catch
    threw := true

Assert(!(atLock.Size != 8 || !threw), A_LineNumber)

; As in AutoHotkey, a base stays open while only its subclasses' definitions refer to it, so a field added to it now
; comes before theirs.
dp(PACK_BASE.Prototype, "z", {type:Int32})

packChild := PACK_CHILD()
packChild.x := 1
packChild.y := 3

Assert(!(packChild.Size != 12 || packChild.x != 1 || packChild.y != 3), A_LineNumber)

fwdBox := FWD_BOX()
fwdPoint := FWD_POINT()
fwdBox.pt.x := 10
fwdBox.pt.y := 20

Assert(!(fwdBox.Size != 8 || fwdBox.pt.x != 10 || fwdBox.pt.y != 20), A_LineNumber)

initChild := INIT_CHILD()
AssertEq(INIT_CHILD(70).x, 70, A_LineNumber)

Assert(!(initChild.Ptr == 0 || initChild.Size != 4 || initChild.x != 42), A_LineNumber)

myInt := MY_INT(123)

Assert(!(myInt.Ptr == 0 || myInt.Size != 4 || myInt.__Value != 123), A_LineNumber)

threw := false
try CLASS_FIELD_ASSIGN()
catch
    threw := true

Assert(!(!threw), A_LineNumber)

; Type specifiers as expressions: (Int32), this.PickType() and an array type. [v2.1-alpha.30]
dynamic := DYNAMIC_TYPES()
dynamic.a := 0x12345678
dynamic.b := 0x1234
dynamic.c[1] := 7
dynamic.c[2] := 8
dynamic.c[3] := 9

Assert(!(dynamic.a != 0x12345678 || dynamic.b != 0x1234
    || dynamic.c[1] != 7 || dynamic.c[2] != 8 || dynamic.c[3] != 9), A_LineNumber)

AssertEq(ObjGetDataSize(POINT.Prototype), 8, A_LineNumber)
AssertEq(ObjGetDataSize(POINT.Prototype), POINT().Size, A_LineNumber)
AssertEq(ObjGetDataSize(DYNAMIC_TYPES.Prototype), dynamic.Size, A_LineNumber)
Throws(() => POINT.Prototype.Size, A_LineNumber, TypeError)

; Reading Size on a prototype is invalid and leaves its layout open.
openSize := Class(Struct)
DefineProp(openSize.Prototype, "First", {Type: Int32})
Throws(() => openSize.Prototype.Size, A_LineNumber, TypeError)
DefineProp(openSize.Prototype, "Second", {Type: Int32})
AssertEq(ObjGetDataSize(openSize.Prototype), 8, A_LineNumber)

; Base is read-only on both a prototype and an instance carrying typed properties. [v2.1-alpha.27]
threw := false
try
    POINT.Prototype.Base := Struct.Prototype
catch ValueError
    threw := true

Assert(!(!threw), A_LineNumber)

threw := false
try
    POINT().Base := Object.Prototype
catch ValueError
    threw := true

Assert(!(!threw), A_LineNumber)

; A prototype with no typed properties still accepts Base assignment. [v2.1-alpha.29]
try
    CLASS_FIELD_ASSIGN.Prototype.Base := CLASS_GET_BASE.Prototype
catch
    Assert(false, A_LineNumber)

; A Float32 field preserves the binary value, as NumGet does.
floatField := FLOAT_FIELD()
floatField.f := 1.2345
AssertEq(floatField.f, 1.2345000505447388, A_LineNumber)
AssertEq(floatField.f, NumGet(floatField.Ptr, "Float"), A_LineNumber)

; Property type strings and typeless (byte-count) types were both removed. [v2.1-alpha.30]
for badType in ["i32", 4] {
    threw := false
    try
        dp(LATE_POINT.Prototype, "bad", {type: badType})
    catch ValueError
        threw := true

    Assert(!(!threw), A_LineNumber)
}

#if WINDOWS
{
    pt3 := POINT()
    ; The counter writes eight bytes and is available without access to an input desktop.
    Assert(!(!DllCall("QueryPerformanceCounter", POINT.Ptr, pt3)), A_LineNumber)

    Assert(!(!IsNumber(pt3.x) || !IsNumber(pt3.y)), A_LineNumber)

    hwnd := DllCall("WindowFromPoint", POINT, pt3, "ptr")

    pt4 := unset
    Assert(!(!DllCall("QueryPerformanceCounter", POINT.Ptr, &pt4)), A_LineNumber)

    Assert(!(!(pt4 is POINT)), A_LineNumber)

    Assert(!(DllCall("IsWindow", POINT.Ptr, unset) != 0), A_LineNumber)

    pp := POINT.Ptr()
    pp.__Value := pt3

    Assert(!(!DllCall("QueryPerformanceCounter", POINT.Ptr, pp)), A_LineNumber)

    threw := false
    try
        DllCall("IsWindow", POINT.Ptr, 123)
    catch as e
        threw := e is TypeError

    Assert(!(!threw), A_LineNumber)

    hwnd := DllCall("GetDesktopWindow", "ptr")
    pid := unset
    tid := DllCall("GetWindowThreadProcessId", "ptr", hwnd, DWORD.Ptr, &pid, "uint")

    Assert(!(!IsNumber(pid) || pid == 0), A_LineNumber)

    threw := false
    try
        nullPtr := DllCall("GetModuleHandle", "str", "__keysharp_missing_module__", POINT.Ptr)
    catch as e
        threw := e is UnsetError

    Assert(!(!threw), A_LineNumber)

    kernel32 := DllCall("GetModuleHandle", "str", "kernel32", Int32.Ptr)

    Assert(!(!IsNumber(kernel32) || kernel32 == 0), A_LineNumber)
}
#endif

; Native marshalling reads the declared base layout and rejects inaccessible storage before entering native code.
struct NATIVE_DERIVED extends BASE_POINT {
    Extra: Int64
}
struct NATIVE_THREE {
    Bytes: UInt8[3]
}
struct NATIVE_PACKED {
    #StructPack 1
    Tag: UInt8
    Value: UInt32
    Tail: UInt8[3]
}
AssertEq(ObjGetDataSize(NATIVE_PACKED.Prototype), 8, A_LineNumber)
nativeDerived := NATIVE_DERIVED()
nativeDerived.x := 37
nativeCalls := {Count: 0}
nativeIdentity := CallbackCreate(value => (++nativeCalls.Count, value), "Fast", [Int32, Int32])
Throws(() => DllCall(nativeIdentity, Int32, Int32.At(1), "Int"), A_LineNumber, Error)
for address in [0, 1]
    Throws(() => DllCall(nativeIdentity, BASE_POINT, BASE_POINT.At(address), "Int"), A_LineNumber, Error)
#if WINDOWS && X64
; Windows x64 passes the struct's own address for a size other than 1, 2, 4 or 8 bytes.
nativeThree := NATIVE_THREE()
AssertEq(DllCall(nativeIdentity, NATIVE_THREE, nativeThree, "UInt"), nativeThree.Ptr & 0xFFFFFFFF, A_LineNumber)
AssertEq(DllCall(nativeIdentity, FLOAT_FIELD, FLOAT_FIELD(1.0), "UInt"), 0x3F800000, A_LineNumber)
nativeCalls.Count := 0
#else
for unsupported in [NATIVE_THREE, FLOAT_FIELD, NATIVE_PACKED] {
    Throws(() => DllCall(nativeIdentity, unsupported, unsupported(), "Int"), A_LineNumber, ValueError)
    Throws(() => DllCall(nativeIdentity, "Int", 0, unsupported), A_LineNumber, ValueError)
    Throws(() => CallbackCreate(value => 0, "Fast", [unsupported, Int32]), A_LineNumber, ValueError)
    Throws(() => CallbackCreate(() => 0, "Fast", [unsupported]), A_LineNumber, ValueError)
}
#endif
nativeErrors := []
continueNativeError := (error, mode) => (nativeErrors.Push(Type(error)), -1)
OnError(continueNativeError)
invalidAggregateInput := DllCall(nativeIdentity, BASE_POINT, 1, "Int")
AssertEq(nativeErrors.Length, 1, A_LineNumber)
invalidPointerInput := DllCall(nativeIdentity, Int32.Ptr, "invalid", "Int")
OnError(continueNativeError, 0)
AssertEq(nativeErrors.Length, 2, A_LineNumber)
for errorType in nativeErrors
    AssertEq(errorType, "TypeError", A_LineNumber)
AssertEq(invalidAggregateInput ?? "", "", A_LineNumber)
AssertEq(invalidPointerInput ?? "", "", A_LineNumber)
AssertEq(nativeCalls.Count, 0, A_LineNumber)
AssertEq(DllCall(nativeIdentity, BASE_POINT, nativeDerived, "Int"), 37, A_LineNumber)
AssertEq(nativeCalls.Count, 1, A_LineNumber)
CallbackFree(nativeIdentity)
baseReturn := CallbackCreate(() => nativeDerived, "Fast", [BASE_POINT])
AssertEq(DllCall(baseReturn, "Int"), 37, A_LineNumber)
CallbackFree(baseReturn)

; A structure stays open to new fields until its layout is needed, so two structs may refer to each other however their
; definitions are ordered. [v2.1-alpha.38+]
struct OPEN_FIRST {
    Second : OPEN_SECOND.Ptr
}
struct OPEN_SECOND {
    First : OPEN_FIRST
    Number : Int32
}
AssertEq(ObjGetDataSize(OPEN_FIRST.Prototype), A_PtrSize, A_LineNumber)
AssertEq(ObjGetDataSize(OPEN_SECOND.Prototype), 2 * A_PtrSize, A_LineNumber)
openSecond := OPEN_SECOND()
openSecond.Number := 5
openSecond.First.Second := openSecond
AssertEq(openSecond.First.Second.Number, 5, A_LineNumber)

; Until it closes, a field reports only the Offset it was given, and Pack when one was given.
DescribeField(proto, name) {
    desc := Object.Prototype.GetOwnPropDesc.Call(proto, name)
    return (desc.HasOwnProp("Offset") ? desc.Offset : "-") "/" (desc.HasOwnProp("Pack") ? desc.Pack : "-")
}
openPoint := Class(Struct)
DefineProp(openPoint.Prototype, "X", {Type: Int8})
DefineProp(openPoint.Prototype, "Y", {Type: Int32, Pack: 2})
DefineProp(openPoint.Prototype, "Z", {Type: Int16, Offset: "y"})
DefineProp(openPoint.Prototype, "W", {Type: Int8, Offset: 7})
AssertEq(DescribeField(openPoint.Prototype, "X"), "-/-", A_LineNumber)
AssertEq(DescribeField(openPoint.Prototype, "Y"), "-/2", A_LineNumber)
AssertEq(DescribeField(openPoint.Prototype, "Z"), "Y/-", A_LineNumber)
AssertEq(DescribeField(openPoint.Prototype, "W"), "7/-", A_LineNumber)
AssertEq(ObjGetDataSize(openPoint.Prototype), 8, A_LineNumber)
AssertEq(DescribeField(openPoint.Prototype, "Y"), "2/2", A_LineNumber)
AssertEq(DescribeField(openPoint.Prototype, "Z"), "2/-", A_LineNumber)

; A subclass, a containing struct or an array type leaves it open; closing any of them, or an instance, closes it.
lateBase := Class(Struct)
DefineProp(lateBase.Prototype, "A", {Type: Int32})
lateChild := Class(lateBase)
DefineProp(lateChild.Prototype, "C", {Type: Int8})
lateHolder := Class(Struct)
DefineProp(lateHolder.Prototype, "Inner", {Type: lateBase})
lateArray := lateBase[2]
DefineProp(lateBase.Prototype, "B", {Type: Int32})
AssertEq(ObjGetDataSize(lateChild.Prototype), 12, A_LineNumber)
AssertEq(ObjGetDataSize(lateHolder.Prototype), 8, A_LineNumber)
AssertEq(ObjGetDataSize(lateArray.Prototype), 16, A_LineNumber)
AssertError(() => DefineProp(lateBase.Prototype, "D", {Type: Int8}), "Error: Cannot add typed property. []", A_LineNumber)
lateInstance := Class(Struct)
DefineProp(lateInstance.Prototype, "A", {Type: Int32})
lateInstance()
Throws(() => DefineProp(lateInstance.Prototype, "B", {Type: Int8}), A_LineNumber, Error)
for invalidOffset in ["missing", "A", -1, 1.0, "4"]
    AssertError(() => DefineProp(Class(lateBase).Prototype, "E", {Type: Int8, Offset: invalidOffset}), "ValueError: Parameter #3 invalid. []", A_LineNumber)

; A script's subclass of a pointer class holds the pointer as a struct of its own, so it is constructed with its owner
; and the field reads through its __Value. [v2.1-alpha.38+]
pointerNews := []
customPointer := Class(Int32.Ptr)
DefineProp(customPointer.Prototype, "__Value", {Get: (this) => "custom"})
DefineProp(customPointer.Prototype, "__New", {Call: (this, *) => pointerNews.Push(1)})
customHolder := Class(Struct)
DefineProp(customHolder.Prototype, "P", {Type: customPointer})
customInstance := customHolder()
AssertEq(pointerNews.Length, 1, A_LineNumber)
AssertEq(customInstance.P, "custom", A_LineNumber)
AssertEq(ObjGetDataSize(customHolder.Prototype), A_PtrSize, A_LineNumber)
plainHolder := Class(Struct)
DefineProp(plainHolder.Prototype, "P", {Type: Class(Int32.Ptr)})
plainInstance := plainHolder()
AssertEq(plainInstance.P ?? "unset", "unset", A_LineNumber)
plainInstance.P := Int32(7)
AssertEq(plainInstance.P.__Value, 7, A_LineNumber)

; Numeric subclasses keep their initialization and __Value when used as fields or native values.
numberNews := []
customNumber := Class(Int32)
DefineProp(customNumber.Prototype, "__Value", {Get: (this) => 70, Set: (this, value) => NumPut("Int", value + 1, this)})
DefineProp(customNumber.Prototype, "__New", {Call: (this, *) => numberNews.Push(1)})
numberHolder := Class(Struct)
DefineProp(numberHolder.Prototype, "Number", {Type: customNumber})
numberInstance := numberHolder()
AssertEq(numberNews.Length, 1, A_LineNumber)
numberInstance.Number := 4
AssertEq(NumGet(numberInstance, "Int"), 5, A_LineNumber)
AssertEq(numberInstance.Number, 70, A_LineNumber)
AssertEq(Object.Prototype.GetOwnPropDesc.Call(numberHolder.Prototype, "Number").Type, customNumber, A_LineNumber)
plainNumber := Class(Int32)
plainNumberHolder := Class(Struct)
DefineProp(plainNumberHolder.Prototype, "Number", {Type: plainNumber})
plainNumberInstance := plainNumberHolder(13)
AssertEq(plainNumberInstance.Number, 13, A_LineNumber)

; Added fields preserve inherited numeric storage and the subclass's initialization.
extendedNumber := Class(Int32)
DefineProp(extendedNumber.Prototype, "Extra", {Type: Int32})
extendedValue := extendedNumber()
AssertEq(extendedValue.__Value, 0, A_LineNumber)
extendedValue.__Value := 11
extendedValue.Extra := 22
AssertEq(extendedValue.__Value, 11, A_LineNumber)
AssertEq(NumGet(extendedValue, "Int"), 11, A_LineNumber)
AssertEq(extendedValue.Extra, 22, A_LineNumber)
DefineProp(extendedNumber.Prototype, "__New", {Call: (this, *) => this.Extra := 6})
extendedHolder := Class(Struct)
DefineProp(extendedHolder.Prototype, "Number", {Type: extendedNumber})
extendedInstance := extendedHolder()
AssertEq(extendedInstance.Number, 0, A_LineNumber)
AssertEq(NumGet(extendedInstance, 4, "Int"), 6, A_LineNumber)
extendedInstance.Number := 9
AssertEq(extendedInstance.Number, 9, A_LineNumber)
AssertEq(NumGet(extendedInstance, "Int"), 9, A_LineNumber)
AssertEq(NumGet(extendedInstance, 4, "Int"), 6, A_LineNumber)
extendedCallback := CallbackCreate(value => value + 1, "Fast", [extendedNumber, extendedNumber])
AssertEq(DllCall(extendedCallback, extendedNumber, 7, extendedNumber), 8, A_LineNumber)
CallbackFree(extendedCallback)

mixedNumber := Class(Float32)
DefineProp(mixedNumber.Prototype, "Tag", {Type: Int32})
mixedCallback := CallbackCreate(value => value + 0.5, "Fast", [mixedNumber, mixedNumber])
AssertEq(DllCall(mixedCallback, mixedNumber, 1.5, mixedNumber), 2.0, A_LineNumber)
CallbackFree(mixedCallback)
#if !WINDOWS || !X64
unalignedNumber := Class(Int32)
DefineProp(unalignedNumber.Prototype, "Extra", {Type: Int16, Offset: 5})
Throws(() => CallbackCreate(value => value, "Fast", [unalignedNumber, Int32]), A_LineNumber, ValueError)
#endif

numberCallback := CallbackCreate(value => value, "Fast", [customNumber, Int32])
AssertEq(DllCall(numberCallback, customNumber, 4, "Int"), 70, A_LineNumber)
CallbackFree(numberCallback)
numberCallback := CallbackCreate(() => 4, "Fast", [customNumber])
AssertEq(DllCall(numberCallback, "Int"), 5, A_LineNumber)
AssertEq(DllCall(numberCallback, customNumber), 70, A_LineNumber)
CallbackFree(numberCallback)
#if WINDOWS && X64
floatNumber := Class(Float32)
floatNumberCallback := CallbackCreate(value => value, "Fast", [floatNumber, floatNumber])
AssertEq(DllCall(floatNumberCallback, floatNumber, 1.5, floatNumber), 1.5, A_LineNumber)
CallbackFree(floatNumberCallback)
#endif

; As in AutoHotkey, a nested struct reads as its __Value when it has one, and is constructed with its owner: its own
; initializers and a __New without parameters run before the owner's.
struct NESTED_VALUE {
    Raw : UInt32
    __New() => NESTED_OWNER.Trace .= "value,"
    __Value {
        get => this.Raw ? this.Raw * 10 : unset
        set => this.Raw := value
    }
}
struct NESTED_INIT {
    Number : Int32 := 4
    __New() {
        this.Number += 5
        NESTED_OWNER.Trace .= "init,"
    }
}
struct NESTED_BASE {
    Value : NESTED_VALUE
}
struct NESTED_OWNER extends NESTED_BASE {
    static Trace := ""
    Init : NESTED_INIT
    __Init() => NESTED_OWNER.Trace .= "owner-init,"
    __New(mark) => NESTED_OWNER.Trace .= mark
}
nestedOwner := NESTED_OWNER("owner-new")
AssertEq(NESTED_OWNER.Trace, "value,init,owner-init,owner-new", A_LineNumber)
AssertEq(nestedOwner.Init.Number, 9, A_LineNumber)
AssertEq(nestedOwner.Value ?? "unset", "unset", A_LineNumber)
Throws(() => nestedOwner.Value, A_LineNumber, UnsetError)
nestedOwner.Value := 3
AssertEq(nestedOwner.Value, 30, A_LineNumber)
DefineProp(NESTED_INIT.Prototype, "__New", {Call: (this) => (this.Number := 20, NESTED_OWNER.Trace .= "changed,")})
DefineProp(NESTED_OWNER.Prototype, "__Init", {Call: (this) => NESTED_OWNER.Trace .= "changed-owner,"})
NESTED_OWNER.Trace := ""
nestedOwner := NESTED_OWNER("again")
AssertEq(nestedOwner.Init.Number, 20, A_LineNumber)
AssertEq(NESTED_OWNER.Trace, "value,changed,changed-owner,again", A_LineNumber)

struct NESTED_CALL {
    Number : Int32 := 7
    __Value => -1
    Call(owner, increment) {
        Assert(owner is NESTED_CALL_OWNER, A_LineNumber)
        return this.Number + increment
    }
}
struct NESTED_CALL_OWNER {
    Child : NESTED_CALL
}
callOwner := NESTED_CALL_OWNER()
AssertEq(callOwner.Child, -1, A_LineNumber)
AssertEq(callOwner.Child(5), 12, A_LineNumber)

; A struct which contains itself by value has no valid layout; pointers remain valid.
selfContained := Class(Struct)
DefineProp(selfContained.Prototype, "Self", {Type: selfContained})
AssertEq(ObjGetDataSize(selfContained.Prototype), 0, A_LineNumber)
Throws(() => selfContained.Prototype.Size, A_LineNumber, TypeError)
AssertError(() => selfContained(), "TypeError: Invalid struct. []", A_LineNumber)

continuedLayout := {Errors: 0, Constructed: 0}
DefineProp(selfContained.Prototype, "__New", {Call: (this, *) => ++continuedLayout.Constructed})
continueLayout := (error, mode) => (++continuedLayout.Errors, -1)
OnError(continueLayout)
invalidInstance := selfContained()
invalidView := selfContained.At(0)
invalidExternal := ObjCreateWithDataPtr(selfContained, 0)
OnError(continueLayout, 0)
AssertEq(continuedLayout.Errors, 3, A_LineNumber)
AssertEq(continuedLayout.Constructed, 0, A_LineNumber)
AssertEq(invalidInstance ?? "", "", A_LineNumber)
AssertEq(invalidView ?? "", "", A_LineNumber)
AssertEq(invalidExternal ?? "", "", A_LineNumber)

mutualFirst := Class(Struct)
mutualSecond := Class(Struct)
DefineProp(mutualFirst.Prototype, "Other", {Type: mutualSecond})
DefineProp(mutualSecond.Prototype, "Other", {Type: mutualFirst})
arrayContained := Class(Struct)
DefineProp(arrayContained.Prototype, "Items", {Type: arrayContained[2]})
baseContained := Class(Struct)
derivedContained := Class(baseContained)
DefineProp(baseContained.Prototype, "Child", {Type: derivedContained})
invalidDerived := Class(selfContained)
DefineProp(invalidDerived.Prototype, "Number", {Type: Int32})
for invalidStruct in [mutualFirst, mutualSecond, arrayContained, baseContained, derivedContained, invalidDerived] {
    AssertEq(ObjGetDataSize(invalidStruct.Prototype), 0, A_LineNumber)
    Throws(() => invalidStruct.Prototype.Size, A_LineNumber, TypeError)
    Throws(() => invalidStruct(), A_LineNumber, TypeError)
    Throws(() => invalidStruct.At(0), A_LineNumber, TypeError)
    Throws(() => ObjCreateWithDataPtr(invalidStruct, 0), A_LineNumber, TypeError)
    Throws(() => CallbackCreate((*) => 0, "Fast", [invalidStruct, Int32]), A_LineNumber, ValueError)
}
nativeArray := Int32[2]
Throws(() => DefineProp(nativeArray.Prototype, "Extra", {Type: Int32}), A_LineNumber, Error)
; Keysharp preserves the element stride when an array subclass adds fields.
; AutoHotkey alpha.39 loses this write and raises UnsetItemError when element 2 is read.
arraySubclass := Class(nativeArray)
DefineProp(arraySubclass.Prototype, "Extra", {Type: Int32})
AssertEq(ObjGetDataSize(arraySubclass.Prototype), 12, A_LineNumber)
arraySubclassInstance := arraySubclass()
arraySubclassInstance[2] := 9
AssertEq(arraySubclassInstance[2], 9, A_LineNumber)
AssertEq(NumGet(arraySubclassInstance, 4, "Int"), 9, A_LineNumber)
offsetNames := Class(Struct)
DefineProp(offsetNames.Prototype, "Zulu", {Type: Int32})
DefineProp(offsetNames.Prototype, "Alpha", {Type: Int32, Offset: "Zulu"})
AssertEq(DescribeField(offsetNames.Prototype, "Alpha"), "Zulu/-", A_LineNumber)
Throws(() => DefineProp(Class(Struct).Prototype, "Invalid", {Type: Int32.Prototype}), A_LineNumber, ValueError)
Throws(() => CallbackCreate((*) => 0, "Fast", [Int32.Prototype, Int32]), A_LineNumber, ValueError)
Throws(() => CallbackCreate(() => 0, "Fast", [Int32.Prototype]), A_LineNumber, ValueError)
Throws(() => DefineProp(Class(Struct).Prototype, "Invalid", {Type: {Prototype: Int32.Prototype}}), A_LineNumber, ValueError)
typeProbeReads := []
fakeType := {}
DefineProp(fakeType, "Prototype", {Get: (this) => (typeProbeReads.Push(1), Int32.Prototype)})
Throws(() => DefineProp(Class(Struct).Prototype, "Invalid", {Type: fakeType}), A_LineNumber, ValueError)
AssertEq(typeProbeReads.Length, 0, A_LineNumber)
#if WINDOWS
Throws(() => DefineProp(Class(Struct).Prototype, "Invalid", {Type: ComValue(3, 0)}), A_LineNumber, ValueError)
#endif
pointerHolder := Class(Struct)
DefineProp(pointerHolder.Prototype, "Pointer", {Type: Int32.Ptr})
pointerInstance := pointerHolder()
Throws(() => pointerInstance.Pointer := Any.Prototype, A_LineNumber, TypeError)
pointerCallback := CallbackCreate(value => value, "Fast", 1)
Throws(() => DllCall(pointerCallback, Int32.Ptr, Any.Prototype), A_LineNumber, TypeError)
CallbackFree(pointerCallback)

for offset in [0x7FFFFFFFFFFFFFFF, 0x7FFFFFFFFFFFFFF7] {
    overflowOffset := Class(Struct)
    DefineProp(overflowOffset.Prototype, "Value", {Type: Int64, Offset: offset})
    Throws(() => overflowOffset.Prototype.Size, A_LineNumber, TypeError)
    AssertEq(ObjGetDataSize(overflowOffset.Prototype), 0, A_LineNumber)
    Throws(() => overflowOffset(), A_LineNumber, TypeError)
    Throws(() => overflowOffset.At(0), A_LineNumber, TypeError)
}

FileAppend "pass", "*"
