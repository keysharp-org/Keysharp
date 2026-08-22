#NoTrayIcon

; =========================
; module-export.ahk
; Default export, named exports, exported variable assignment, quoted import behavior
; =========================

#import D as DDefault
#import "D" { Named as DNamed }
#import E
#import V as ModV

; quoted module import should not add module name unless alias is given
#import "Q"
#Include <assert>

; ---- default export callable via alias
a := DDefault()
AssertEq(a, 123, A_LineNumber)

; ---- named export callable via explicit import
a := DNamed()
AssertEq(a, 5, A_LineNumber)

; ---- a bare import binds an explicit default under the module name
a := E()
AssertEq(a, 321, A_LineNumber)

; ---- importing via "from D" should NOT add D unless aliased
a := IsSet(D)
AssertEq(a, false, A_LineNumber)

; ---- quoted import "Q" should NOT add Q to namespace
a := IsSet(Q)
AssertEq(a, false, A_LineNumber)

; ---- exported variables can be assigned by other modules
a := ModV.Var
AssertEq(a, 1, A_LineNumber)

ModV.Var := 7
a := ModV.Var
AssertEq(a, 7, A_LineNumber)


FileAppend "pass", "*"

#Module D
export default DefaultFunc() => 123
export Named() => 5

#Module E
export default ExplicitDefaultBare() => 321

#Module V
export Var := 1

#Module Q
; empty on purpose
