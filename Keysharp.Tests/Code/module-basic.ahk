#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

; =========================
; module-basic.ahk
; Basic module isolation + alias + built-in shadowing via AHK module
; =========================

#import Other
#import Other as O
#import AHK
#import "AHK" { * }
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

; ---- Test: alias refers to the module object
a := O.ShowVar()
AssertEq(a, 2, A_LineNumber)

; ---- Every global declared directly by a module is visible through its module object.
AssertEq(Other.MyVar, 2, A_LineNumber)

; ---- Test: shadow built-in function; access built-in via AHK module
Abs(x) => "mine"

a := Abs(5)
AssertEq(a, "mine", A_LineNumber)

a := AHK.Abs(-5)
AssertEq(a, 5, A_LineNumber)

; ---- a custom AHK module can import from the script graph and override a built-in
AssertEq(FromAhk(), 2, A_LineNumber)
AssertEq(Ceil(0.2), "custom", A_LineNumber)


FileAppend "pass", "*"

#Module Other
MyVar := 2
ShowVar() => MyVar

#Module AHK
#Import "Other" { ShowVar as OtherShow }
FromAhk() => OtherShow()
Ceil(*) => "custom"
