#NoTrayIcon
#Include <assert>

x := true
y := false

Assert(x and y = false, A_LineNumber)

Assert(!(x and y = true), A_LineNumber)

Assert(!((x and y) = true), A_LineNumber)
	
Assert(not ((x and y) = true), A_LineNumber)

Assert(!(!((x and y) = false)), A_LineNumber)
	
Assert(!(not ((x and y) = false)), A_LineNumber)

Assert(true and false = false, A_LineNumber)

Assert(!(("true" and false) = true), A_LineNumber)
	
Assert(not (("true" and "false") = true), A_LineNumber)


x := 1
y := 0

Assert((x and y) = false, A_LineNumber)

Assert(!((x and y) = true), A_LineNumber)

Assert(!((x and y) = true), A_LineNumber)
	
Assert(not ((x and y) = true), A_LineNumber)

Assert(!(!((x and y) = false)), A_LineNumber)
	
Assert(!(not ((x and y) = false)), A_LineNumber)
	
Assert((1 and 0) = false, A_LineNumber)

Assert(!(("1" and 0) = true), A_LineNumber)
	
Assert(not (("1" and "0x0") = true), A_LineNumber)

x := 1.234
y := 5.678

Assert((x and y) = y, A_LineNumber)

Assert(!((x and y) = true), A_LineNumber)

Assert(!((x and y) = false), A_LineNumber)
	
Assert(not ((x and y) = false), A_LineNumber)

Assert(!(!((x and y) = y)), A_LineNumber)

Assert(!(not ((x and y) = y)), A_LineNumber)

Assert((1.234 and 5.678) = 5.678, A_LineNumber)

Assert(!(("1.234" and 5.678) = false), A_LineNumber)
	
Assert(not (("1.234" and "5.678") = false), A_LineNumber)

; Now do again with &&

x := true
y := false

Assert((x && y) = false, A_LineNumber)

Assert(!((x && y) = true), A_LineNumber)

Assert(!((x && y) = true), A_LineNumber)

Assert(not ((x && y) = true), A_LineNumber)

Assert(!(!((x && y) = false)), A_LineNumber)

Assert(!(not ((x && y) = false)), A_LineNumber)

Assert((true && false) = false, A_LineNumber)

Assert(!(("true" && false) = true), A_LineNumber)

Assert(not (("true" && "false") = true), A_LineNumber)

x := 1
y := 0

Assert((x && y) = false, A_LineNumber)

Assert(!((x && y) = true), A_LineNumber)

Assert(!((x && y) = true), A_LineNumber)

Assert(not ((x && y) = true), A_LineNumber)

Assert(!(!((x && y) = false)), A_LineNumber)

Assert(!(not ((x && y) = false)), A_LineNumber)
	
Assert((1 && 0) = false, A_LineNumber)

Assert(!(("1" && 0) = true), A_LineNumber)

Assert(not (("1" && "0x0") = true), A_LineNumber)

x := 1.234
y := 5.678

Assert((x && y) = y, A_LineNumber)

Assert(!((x && y) = false), A_LineNumber)

Assert(!((x && y) = false), A_LineNumber)

Assert(not ((x && y) = false), A_LineNumber)

Assert(!(!((x && y) = y)), A_LineNumber)

Assert(!(not ((x && y) = y)), A_LineNumber)

Assert((1.234 && 5.678) = 5.678, A_LineNumber)

Assert(!(("1.234" && 5.678) = false), A_LineNumber)

Assert(not (("1.234" && "5.678") = false), A_LineNumber)

A := 1, B := {}, C := 20, D := True, E := "String" ; All operands are truthy and will be evaluated
x := A && B && C && D && E ; The last truthy operand is returned ("String")

AssertEq(x, "String", A_LineNumber)
	
A := 1, B := "", C := 0, D := False, E := "String" ; B is falsey, C and D are false
x := A && B && ++C && D && E ; The first falsey operand is returned (""). C, D and E are not evaluated and C is never incremented

AssertEq(x, "", A_LineNumber)

AssertEq(C, 0, A_LineNumber)

evalfunc(p1)
{
	return p1
}

val := evalfunc(1 && 2)

AssertEq(val, 2, A_LineNumber)

val := evalfunc("1" && 2)

AssertEq(val, 2, A_LineNumber)

val := evalfunc("1" && "0x2")

AssertEq(val, "0x2", A_LineNumber)

val := evalfunc(x := 1 && 2)

Assert(val == 2 && x == 2, A_LineNumber)

val := evalfunc(x := "1" && 2)

Assert(val == 2 && x == 2, A_LineNumber)

val := evalfunc(x := "0x1" && 2)

Assert(val == 2 && x == 2, A_LineNumber)

AssertEq((1 && 2), 2, A_LineNumber)

AssertEq(("1" && 2), 2, A_LineNumber)

AssertEq(("0x1" && 2), 2, A_LineNumber)

AssertEq(("0x1" && "0x2"), "0x2", A_LineNumber)

val := evalfunc(1 && true && 20 && "true")

AssertEq(val, "true", A_LineNumber)
	
val := evalfunc(x := "1" && true && 20 && "true")

Assert(val == "true" && x == "true", A_LineNumber)

Assert(x := ("1" && true && "0x20" && "true") == "true" && x == "true", A_LineNumber)

val := evalfunc(1 && true && 20 && "true" && 0)

AssertEq(val, 0, A_LineNumber)
	
val := evalfunc(x := 1 && true && 20 && "true" && 0)

Assert(val == 0 && x == 0, A_LineNumber)

AssertEq((1 && true && "20" && "true" && 0), 0, A_LineNumber)

FileAppend "pass", "*"
