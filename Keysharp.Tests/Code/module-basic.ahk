#NoTrayIcon

; =========================
; module-basic.ahk
; Basic module isolation + alias + built-in shadowing via AHK module
; =========================

#import Other
#import Other as O
#import AHK
#Include <assert>

; Our own global var + function in __Main
MyVar := 1
ShowVar() => MyVar

; ---- Test: main has its own globals
a := ShowVar()
AssertEq(a, 1, A_LineNumber)

; ---- Test: Other has its own globals
a := Other.ShowVar()
AssertEq(a, 2, A_LineNumber)

; ---- Test: alias refers to default export (module object)
a := O.ShowVar()
AssertEq(a, 2, A_LineNumber)

; ---- Test: non-exported module global var should be inaccessible (per docs example)
a := ""
try a := Other.MyVar
catch
    a := "inaccessible"

AssertEq(a, "inaccessible", A_LineNumber)

; ---- Test: shadow built-in function; access built-in via AHK module
Abs(x) => "mine"

a := Abs(5)
AssertEq(a, "mine", A_LineNumber)

a := AHK.Abs(-5)
AssertEq(a, 5, A_LineNumber)


FileAppend "pass", "*"

#Module Other
MyVar := 2
export ShowVar() => MyVar
