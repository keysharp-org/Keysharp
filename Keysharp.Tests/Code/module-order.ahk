#NoTrayIcon
#Include <assert>

; =========================
; module-order.ahk
; Execution order + exports callable before module body executes
; =========================

; MainReady is implicitly visible to other modules through the __Main module object.
MainReady := 1

#import Z
#import Late as LateMod

; ---- Dependency execution: Z imports W and captures WState
a := Z.ObservedW()
AssertEq(a, "W executed", A_LineNumber)

; ---- Late imports __Main, so __Main executes before Late.
; Late exports functions that should be callable even before Late body has run.

; LateBodyRan is set in Late's module body, which should not have executed yet.
a := LateMod.GetLateBodyRan()
AssertEq(a, "", A_LineNumber)  ; expecting unset/blank before Late executes

; A module function should be callable pre-exec and can see __Main's globals.
a := LateMod.GetMainReady()
AssertEq(a, 1, A_LineNumber)


FileAppend "pass", "*"

#Module W
WState := "W executed"
GetWState() => WState

#Module Z
#import W
Observed := W.GetWState()
ObservedW() => Observed

#Module Late
#import __Main as Main
GetMainReady() => Main.MainReady
GetLateBodyRan() => (IsSet(LateBodyRan) ? LateBodyRan : "")
LateBodyRan := 1
