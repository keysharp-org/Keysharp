#NoTrayIcon
#Include <assert>

; =========================
; module-order.ahk
; Execution order + exports callable before module body executes
; =========================

; Export MainReady so other modules can access it via __Main module object.
export MainReady := 1

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

; Exported function should be callable pre-exec and can see __Main export.
a := LateMod.GetMainReady()
AssertEq(a, 1, A_LineNumber)


FileAppend "pass", "*"

#Module W
WState := "W executed"
export GetWState() => WState

#Module Z
#import W
Observed := W.GetWState()
export ObservedW() => Observed

#Module Late
#import __Main as Main
export GetMainReady() => Main.MainReady
export GetLateBodyRan() => (IsSet(LateBodyRan) ? LateBodyRan : "")
LateBodyRan := 1
