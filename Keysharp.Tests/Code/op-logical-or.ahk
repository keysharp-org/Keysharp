#NoTrayIcon
#Include <assert>

x := true
y := false

Assert(x or y, A_LineNumber)

Assert((x or y) = true, A_LineNumber)

Assert(!(!(x or y)), A_LineNumber)

Assert(!(not (x or y)), A_LineNumber)

Assert(!((x or y) = false), A_LineNumber)

Assert(true or false = true, A_LineNumber)

Assert(!(("true" or false) = false), A_LineNumber)
	
Assert(not (("true" or "false") = false), A_LineNumber)

x := 1
y := 0

Assert(x or y, A_LineNumber)

Assert((x or y) = true, A_LineNumber)

Assert(!(!(x or y)), A_LineNumber)

Assert(!(not (x or y)), A_LineNumber)

Assert(!((x or y) = false), A_LineNumber)
	
Assert((1 or 0) = true, A_LineNumber)

Assert(!(("1" or 0) = false), A_LineNumber)
	
Assert(not (("1" or "0x0") = false), A_LineNumber)

x := 1.234
y := 5.678

Assert(x or y, A_LineNumber)

Assert((x or y) = x, A_LineNumber)

Assert(!(!(x or y)), A_LineNumber)

Assert(!(not (x or y)), A_LineNumber)

Assert(!((x or y) = false), A_LineNumber)

Assert((1.234 or 5.678) = 1.234, A_LineNumber)

Assert(!(("1.234" or 5.678) = false), A_LineNumber)
	
Assert(not (("1.234" or "5.678") = false), A_LineNumber)

; Now do again with ||

x := true
y := false

Assert(x || y, A_LineNumber)

Assert((x || y) = true, A_LineNumber)

Assert(!(!(x || y)), A_LineNumber)

Assert(!(not (x || y)), A_LineNumber)

Assert(!((x || y) = false), A_LineNumber)

Assert(true || false = true, A_LineNumber)

Assert(!(("true" || false) = false), A_LineNumber)
	
Assert(not (("true" || "false") = false), A_LineNumber)

x := 1
y := 0

Assert(x || y, A_LineNumber)

Assert((x || y) = true, A_LineNumber)

Assert(!(!(x || y)), A_LineNumber)

Assert(!(not (x || y)), A_LineNumber)

Assert(!((x || y) = false), A_LineNumber)

Assert((1 || 0) = true, A_LineNumber)

Assert(!(("1" || 0) = false), A_LineNumber)
	
Assert(not (("1" || "0x0") = false), A_LineNumber)

x := 1.234
y := 5.678

Assert(x || y, A_LineNumber)

Assert((x || y) = x, A_LineNumber)

Assert(!(!(x || y)), A_LineNumber)

Assert(!(not (x || y)), A_LineNumber)

Assert(!((x || y) = false), A_LineNumber)

Assert((1.234 || 5.678) = 1.234, A_LineNumber)

Assert(!(("1.234" || 5.678) = false), A_LineNumber)
	
Assert(not (("1.234" || "5.678") = false), A_LineNumber)

A := "", B := False, C := 0, D := "String", E := 20 ; At least one operand is truthy. All operands up until D (including) will be evaluated
x := A || B || C || D || ++E ; The first truthy operand is returned ("String"). E is not evaluated and is never incremented

AssertEq(x, "String", A_LineNumber)

AssertEq(E, 20, A_LineNumber)

A := "", B := False, C := 0 ; All operands are falsey and will be evaluated
x := A || B || C ; The last falsey operand is returned (0)

AssertEq(x, 0, A_LineNumber)

evalfunc(p1)
{
	return p1
}

val := evalfunc(0 || 2)

AssertEq(val, 2, A_LineNumber)

val := evalfunc("0" || 2)

AssertEq(val, 2, A_LineNumber)

val := evalfunc("0" || "0x2")

AssertEq(val, "0x2", A_LineNumber)

val := evalfunc(x := 0 || 2)

Assert(val == 2 && x == 2, A_LineNumber)

val := evalfunc(x := "0" || 2)

Assert(val == 2 && x == 2, A_LineNumber)

val := evalfunc(x := "0" || "0x2")

Assert(val == "0x2" && x == "0x2", A_LineNumber)

AssertEq((0 || 2), 2, A_LineNumber)

AssertEq(("0" || 2), 2, A_LineNumber)
	
AssertEq(("0x0" || 2), 2, A_LineNumber)

AssertEq(("0x0" || "0x2"), "0x2", A_LineNumber)

Assert((x := 0 || 2) == 2 && x == 2, A_LineNumber)

val := evalfunc(x := "" || false || 0 || 123)

Assert(val == 123 && x == 123, A_LineNumber)
	
AssertEq(("" || "false" || 0 || 123), "false", A_LineNumber)

Assert(!(("" || "false" || 0 || 123) == false), A_LineNumber)

; Negating an unset variable raises rather than treating it as false.
a := unset
Throws(() => !a, A_LineNumber)

FileAppend "pass", "*"
