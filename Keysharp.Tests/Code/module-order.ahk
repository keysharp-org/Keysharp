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
#import UntilTail
#import NestedOrderTail
#import ChildFirst:Child as ChildFirstChild

; ---- Dependency execution: Z imports W and captures WState
a := Z.ObservedW()
AssertEq(a, "W executed", A_LineNumber)

; ---- A parent importing its nested module runs after it, however they are declared; nesting alone adds no order
AssertEq(NestedOrderTail.Observed, "child,parent", A_LineNumber)
AssertEq(ChildFirstChild.ObservedParent, "parent ran", A_LineNumber)

; ---- Late imports __Main, so __Main executes before Late.
; Late exports functions that should be callable even before Late body has run.

; LateBodyRan is set in Late's module body, which should not have executed yet.
a := LateMod.GetLateBodyRan()
AssertEq(a, "", A_LineNumber)  ; expecting unset/blank before Late executes

; A module function should be callable pre-exec and can see __Main's globals.
a := LateMod.GetMainReady()
AssertEq(a, 1, A_LineNumber)

; ---- A module body, here the last one in the file, may end with Loop...Until. [v2.1-alpha.32]
AssertEq(UntilTail.Count, 3, A_LineNumber)


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

; Modules run from the last declared unless an import says otherwise. NestedOrderTail, declared after both, reaches
; NestedOrder first, so only NestedOrder's import of its child can run the child before it.
#Module NestedOrder:Child
Ran := "child"

#Module NestedOrder
#import NestedOrder:Child
Ran := Child.Ran ",parent"

#Module NestedOrderTail
#import NestedOrder
Observed := NestedOrder.Ran

; The child imports its parent here, against the default order.
#Module ChildFirst
Ran := "parent ran"

#Module ChildFirst:Child
#import ChildFirst
ObservedParent := ChildFirst.Ran

#Module UntilTail
Count := 0
Loop
	Count++
Until Count = 3
