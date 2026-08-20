#NoTrayIcon

; A native IDispatch client (cJson.ahk is the real-world one) enumerates a Keysharp object by calling
; __Enum(2) and then invoking the returned enumerator with two VT_BYREF|VT_VARIANT out-parameters.
; The CLR hands IReflect.InvokeMember a null `modifiers`, so nothing about the call itself says those
; slots are byref -- Enumerator.Call's contract is what does. This drives the whole path through the
; CCW so a regression shows up as an enumeration that yields nothing.

Ok(cond) => FileAppend(cond ? "pass" : "fail", "*")

IID_IDispatch := Buffer(16), IID_NULL := Buffer(16, 0)
DllCall("ole32\CLSIDFromString", "Str", "{00020400-0000-0000-C000-000000000046}", "Ptr", IID_IDispatch)

Vt(pd, slot) => NumGet(NumGet(pd, "Ptr"), slot * A_PtrSize, "Ptr")

GetDisp(obj) {
    p := ObjPtrAddRef(obj)
    DllCall(Vt(p, 0), "Ptr", p, "Ptr", IID_IDispatch, "Ptr*", &pd := 0, "Int")
    return pd
}

; Returns the DISPID, or 0 if GetIDsOfNames itself failed -- `d` is zero-initialised, so the HRESULT is
; what separates "no such name" (which writes DISPID_UNKNOWN) from "never answered".
DispIdOf(pd, name) {
    names := Buffer(A_PtrSize), n := name
    NumPut("Ptr", StrPtr(n), names)
    hr := DllCall(Vt(pd, 5), "Ptr", pd, "Ptr", IID_NULL, "Ptr", names, "UInt", 1, "UInt", 0, "Ptr", d := Buffer(4, 0), "Int")
    return hr = 0 ? NumGet(d, "Int") : 0
}

; Enumerate obj through IDispatch and return the pairs as "k=v|k=v|"
ComEnumerate(obj) {
    pd := GetDisp(obj)
    id := DispIdOf(pd, "__Enum")
    Ok(id != -1 && id != 0)

    two := Buffer(24, 0), dp := Buffer(24, 0), res := Buffer(24, 0)
    NumPut("UShort", 3, two, 0), NumPut("Int", 2, two, 8)                     ; VT_I4 2
    NumPut("Ptr", two.Ptr, dp, 0), NumPut("UInt", 1, dp, 16)
    DllCall(Vt(pd, 6), "Ptr", pd, "Int", id, "Ptr", IID_NULL, "UInt", 0
        , "UShort", 3, "Ptr", dp, "Ptr", res, "Ptr", 0, "Ptr", 0, "Int")      ; METHOD|PROPERTYGET
    Ok(NumGet(res, 0, "UShort") = 9)                                          ; VT_DISPATCH
    pEnum := NumGet(res, 8, "Ptr")

    out := ""
    Loop 10 {
        k_ := Buffer(24, 0), v_ := Buffer(24, 0), argv := Buffer(48, 0)
        NumPut("UShort", 0x400C, argv, 0),  NumPut("Ptr", v_.Ptr, argv, 8)    ; rgvarg is right-to-left
        NumPut("UShort", 0x400C, argv, 24), NumPut("Ptr", k_.Ptr, argv, 32)
        dp2 := Buffer(24, 0), r2 := Buffer(24, 0)
        NumPut("Ptr", argv.Ptr, dp2, 0), NumPut("UInt", 2, dp2, 16)
        DllCall(Vt(pEnum, 6), "Ptr", pEnum, "Int", 0, "Ptr", IID_NULL, "UInt", 0
            , "UShort", 1, "Ptr", dp2, "Ptr", r2, "Ptr", 0, "Ptr", 0, "Int")
        if NumGet(r2, 0, "UShort") != 3 || NumGet(r2, 8, "Int") = 0
            break
        out .= VariantText(k_) "=" VariantText(v_) "|"
    }
    return out
}

VariantText(var) {
    vt := NumGet(var, 0, "UShort")
    if vt = 8                                        ; VT_BSTR
        return StrGet(NumGet(var, 8, "Ptr"), "UTF-16")
    if vt = 3                                        ; VT_I4
        return NumGet(var, 8, "Int")
    return "<vt" vt ">"
}

Ok(ComEnumerate(Map("a", 1, "b", 2)) = "a=1|b=2|")
Ok(ComEnumerate([10, 20, 30]) = "1=10|2=20|3=30|")

; The [ByRef] marker sits on Enumerator.Call's `params object[]`, so it covers the whole tail: every
; slot reports ByRef, including ones past the single declared parameter.
en := Map("a", 1).__Enum(2)
Ok(en.IsByRef(1))
Ok(en.IsByRef(2))
Ok(en.IsByRef())
Ok(en.Params[1].ByRef = 1 && en.Params[1].Variadic = 1)

; A function with no ByRef parameters must still report none.
Plain(a, b) => a + b
Ok(!Plain.IsByRef(1))
Ok(!Plain.IsByRef())

; A Keysharp object must also still be constructible through the same path -- cJson's loads.c builds its
; result by invoking ObjPtr(Map) as DISPATCH_METHOD.
pdMapClass := GetDisp(Map)
dp0 := Buffer(24, 0), res3 := Buffer(24, 0)
DllCall(Vt(pdMapClass, 6), "Ptr", pdMapClass, "Int", 0, "Ptr", IID_NULL, "UInt", 0
    , "UShort", 1, "Ptr", dp0, "Ptr", res3, "Ptr", 0, "Ptr", 0, "Int")
Ok(NumGet(res3, 0, "UShort") = 9)

; Invokes dispid on pd with two VT_BYREF|VT_VARIANT slots holding 11 and 22, and reports what came back.
SwapThrough(pd, dispid) {
    x_ := Buffer(24, 0), y_ := Buffer(24, 0), av := Buffer(48, 0)
    NumPut("UShort", 3, x_, 0), NumPut("Int", 11, x_, 8)
    NumPut("UShort", 3, y_, 0), NumPut("Int", 22, y_, 8)
    NumPut("UShort", 0x400C, av, 0),  NumPut("Ptr", y_.Ptr, av, 8)        ; rgvarg is right-to-left
    NumPut("UShort", 0x400C, av, 24), NumPut("Ptr", x_.Ptr, av, 32)
    dp := Buffer(24, 0), res := Buffer(24, 0)
    NumPut("Ptr", av.Ptr, dp, 0), NumPut("UInt", 2, dp, 16)
    DllCall(Vt(pd, 6), "Ptr", pd, "Int", dispid, "Ptr", IID_NULL, "UInt", 0
        , "UShort", 1, "Ptr", dp, "Ptr", res, "Ptr", 0, "Ptr", 0, "Int")
    return VariantText(x_) "," VariantText(y_)
}

; A script function's own [ByRef] parameters must keep working over the same dispatch path.
Swapper(&a, &b) {
    t := a, a := b, b := t
    return 1
}
Ok(SwapThrough(GetDisp(Swapper), 0) = "22,11")

; And so must a class method's. A lowered method carries its receiver as the FIRST parameter, so the
; caller's argument slots sit one place away from the parameter list the marks have to be read off.
class Holder {
    Swap(&a, &b) {
        t := a, a := b, b := t
        return 1
    }
}
pdHolder := GetDisp(Holder())
idSwap := DispIdOf(pdHolder, "Swap")
Ok(idSwap != -1 && idSwap != 0)
Ok(SwapThrough(pdHolder, idSwap) = "22,11")
