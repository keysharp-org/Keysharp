#NoTrayIcon
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
    } catch as e {
        if e is UnsetError
            return false
        throw e
    }
}

dp := Object.DefineProp
dp(EARLY_POINT.Prototype, "z", {type:Int32})
dp(PACKED.Prototype, "b", {type:Int32, pack:1})
dp(UNION.Prototype, "b", {type:Int32, offset:"a"})
dp(PTR_HOLDER.Prototype, "pt", {type:POINT.Ptr})

pt := POINT()

Assert(!(pt.Size != 8), A_LineNumber)

Assert(!(!HasProp(pt, "x") || !HasProp(pt, "y")), A_LineNumber)

pt.x := 10
pt.y := 20

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

num := Int32()
num.__Value := 42

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

unionInst := UNION()
unionInst.a := 1
unionInst.b := 2

Assert(!(unionInst.Size != 4 || unionInst.a != 2), A_LineNumber)

ptrHolder := PTR_HOLDER()

Assert(!(PropIsSet(ptrHolder, "pt")), A_LineNumber)

ptrHolder.pt := pt

Assert(!(!(ptrHolder.pt is POINT) || ptrHolder.pt.Ptr != pt.Ptr), A_LineNumber)

ptrHolder.pt := unset

Assert(!(PropIsSet(ptrHolder, "pt")), A_LineNumber)

DynPoint := Class("DynPoint", DYN_BASE)
dp(DynPoint.Prototype, "x", {type:Int32})
dp(DynPoint.Prototype, "y", {type:Int32})

DynChild := Class("DynChild", DynPoint)
dp(DynChild.Prototype, "z", {type:Int32})

dynPoint := DynPoint()
dynPoint.x := 10
dynPoint.y := 20

dynChild := DynChild()
dynChild.x := 1
dynChild.y := 2
dynChild.z := 3

Assert(!(dynPoint.Size != 8
    || dynPoint.x != 10
    || dynPoint.y != 20
    || dynChild.Size != 12
    || dynChild.x != 1
    || dynChild.y != 2
    || dynChild.z != 3), A_LineNumber)

buf := Buffer(4)
atLock := POINT.At(buf.Ptr)

threw := false
try
    dp(POINT.Prototype, "z", {type:Int32})
catch
    threw := true

Assert(!(atLock.Size != 8 || !threw), A_LineNumber)

threw := false
try
    dp(PACK_BASE.Prototype, "z", {type:Int32})
catch
    threw := true

packChild := PACK_CHILD()
packChild.x := 1
packChild.y := 3

Assert(!(packChild.Size != 8 || packChild.x != 1 || packChild.y != 3 || !threw), A_LineNumber)

fwdBox := FWD_BOX()
fwdPoint := FWD_POINT()
fwdBox.pt.x := 10
fwdBox.pt.y := 20

Assert(!(fwdBox.Size != 8 || fwdBox.pt.x != 10 || fwdBox.pt.y != 20), A_LineNumber)

initChild := INIT_CHILD()

Assert(!(initChild.Ptr == 0 || initChild.Size != 4 || initChild.x != 42), A_LineNumber)

myInt := MY_INT()
myInt.__Value := 123

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

; A prototype reports the struct size of the class it belongs to, matching its instances. [v2.1-alpha.23]
Assert(!(POINT.Prototype.Size != 8 || POINT.Prototype.Size != POINT().Size
    || DYNAMIC_TYPES.Prototype.Size != dynamic.Size), A_LineNumber)

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
    Assert(!(!DllCall("GetCursorPos", POINT.Ptr, pt3)), A_LineNumber)

    Assert(!(!IsNumber(pt3.x) || !IsNumber(pt3.y)), A_LineNumber)

    hwnd := DllCall("WindowFromPoint", POINT, pt3, "ptr")

    pt4 := unset
    Assert(!(!DllCall("GetCursorPos", POINT.Ptr, &pt4)), A_LineNumber)

    Assert(!(!(pt4 is POINT)), A_LineNumber)

    Assert(!(DllCall("IsWindow", POINT.Ptr, unset) != 0), A_LineNumber)

    pp := POINT.Ptr()
    pp.__Value := pt3

    Assert(!(!DllCall("GetCursorPos", POINT.Ptr, pp)), A_LineNumber)

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

FileAppend "pass", "*"
